Imports System.ComponentModel
Imports System.Text.Json
Imports Microsoft.Web.WebView2.Core
Imports Ollama

''' <summary>
''' 生成式界面的渲染容器：内部持有一个 WebView2 控件用于渲染 LLM 生成出来的 html 页面，
''' 同时向页面之中注入一个 <see cref="JavascriptInterop"/> 宿主对象，
''' 使得页面内的 JavaScript 代码具备调用 .NET 宿主功能代码的能力。
''' </summary>
Public Class WebUI

    ''' <summary>
    ''' 宿主对象在构造阶段就创建好，这样调用方可以在窗体 Load 事件之中，
    ''' 也就是 WebView2 初始化完成之前就完成宿主命令的注册。
    ''' </summary>
    ReadOnly js As New JavascriptInterop()

    Dim llm As LLMClient
    Dim pendingHtml As String
    Dim webViewReady As Boolean = False
    Dim enableDevTools As Boolean = False
    Dim webMessageHooked As Boolean = False

    ''' <summary>
    ''' 最近一次页面渲染之后收集到的运行时问题
    ''' </summary>
    ''' <returns></returns>
    Public ReadOnly Property PageIssues As New List(Of PageIssue)()

    ''' <summary>
    ''' 最近一次页面自检的结果
    ''' </summary>
    ''' <returns></returns>
    <DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)>
    Public Property LastHealth As PageHealth

    ''' <summary>
    ''' 页面自检结果回传的完成信号，生成式引擎通过它等待页面的体检报告
    ''' </summary>
    Friend HealthWaiter As TaskCompletionSource(Of PageHealth)

    ''' <summary>
    ''' 当网页回传运行时错误或者自检结果的时候触发
    ''' </summary>
    ''' <param name="health"></param>
    Public Event PageDiagnostics(health As PageHealth)

    ''' <summary>
    ''' 清空上一次页面留下的问题记录，通常在重新渲染页面之前调用，
    ''' 避免旧页面的错误被算到新页面头上。
    ''' </summary>
    Public Sub ResetDiagnostics()
        SyncLock PageIssues
            PageIssues.Clear()
        End SyncLock

        LastHealth = Nothing
        HealthWaiter = Nothing
    End Sub

    ''' <summary>
    ''' 注入到网页之中的宿主对象，通过 <c>WebUI.Host.Commands.Register(...)</c>
    ''' 即可向网页暴露出新的宿主能力。
    ''' </summary>
    ''' <returns></returns>
    Public ReadOnly Property Host As JavascriptInterop
        Get
            Return js
        End Get
    End Property

    ''' <summary>
    ''' 设置绑定到当前生成式界面之上的大语言模型客户端
    ''' </summary>
    ''' <returns></returns>
    Public ReadOnly Property Model As LLMClient
        Get
            Return llm
        End Get
    End Property

    ''' <summary>
    ''' WebView2 控件是否已经初始化完毕，在初始化完毕之前调用 <see cref="SetUI(String)"/>
    ''' 只会将 html 缓存起来，等到初始化完成之后再自动渲染。
    ''' </summary>
    ''' <returns></returns>
    Public ReadOnly Property IsReady As Boolean
        Get
            Return webViewReady
        End Get
    End Property

    ''' <summary>
    ''' 是否启用 WebView2 的开发者工具（F12）
    ''' </summary>
    ''' <returns></returns>
    <DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)>
    Public Property DeveloperTools As Boolean
        Get
            Return enableDevTools
        End Get
        Set(value As Boolean)
            enableDevTools = value
        End Set
    End Property

    ''' <summary>
    ''' 网页通过 <c>host.log(...)</c> 输出的日志
    ''' </summary>
    ''' <param name="message"></param>
    Public Event HostLog(message As String)

    ''' <summary>
    ''' 当 WebView2 完成初始化并且页面加载完毕之后触发
    ''' </summary>
    Public Event UIReady()

    Public Function SetLLM(llm As LLMClient) As WebUI
        Me.llm = llm
        Return Me
    End Function

    ''' <summary>
    ''' 渲染一段 html 文档到 WebView2 控件之中
    ''' </summary>
    ''' <param name="html">完整的 html 文档字符串</param>
    Public Sub SetUI(html As String)
        If InvokeRequired Then
            Call Invoke(Sub() SetUI(html))
            Return
        End If

        If Not webViewReady OrElse WebView21.CoreWebView2 Is Nothing Then
            ' WebView2 还没有初始化完成，先缓存起来
            pendingHtml = html
            Return
        End If

        pendingHtml = Nothing

        Call WebViewLoader.NavigateToLargeString(WebView21, value:=html)
    End Sub

    ''' <summary>
    ''' 向当前页面推送一条 json 消息，页面可以通过 <c>window.chrome.webview.addEventListener('message', ...)</c> 接收
    ''' </summary>
    ''' <param name="payload"></param>
    Public Sub PostMessage(payload As Object)
        If WebView21 Is Nothing OrElse WebView21.CoreWebView2 Is Nothing Then
            Return
        End If

        Dim json As String = JsonSerializer.Serialize(payload)

        Try
            If InvokeRequired Then
                Call Invoke(Sub() WebView21.CoreWebView2.PostWebMessageAsJson(json))
            Else
                Call WebView21.CoreWebView2.PostWebMessageAsJson(json)
            End If
        Catch ex As Exception
            Call Console.WriteLine(ex.Message)
        End Try
    End Sub

    ''' <summary>
    ''' 向当前页面推送一条状态文本：会同时通过 web message 与页面内的
    ''' <c>genui_status</c> 全局函数两种方式发送，以兼容不同写法的生成式页面。
    ''' </summary>
    ''' <param name="message"></param>
    ''' <param name="level">状态级别：info / success / warn / error</param>
    Public Sub PushStatus(message As String, Optional level As String = "info")
        Call PostMessage(New With {
            .action = "status",
            .message = message,
            .level = level
        })
        Call EvalScriptAsync($"window.genui_status && window.genui_status({JsonSerializer.Serialize(message)}, {JsonSerializer.Serialize(level)})")
    End Sub

    ''' <summary>
    ''' 通知网页：一个新的流式输出阶段开始了，网页端应当清空上一阶段的显示内容
    ''' </summary>
    ''' <param name="phase">阶段标识</param>
    ''' <param name="label">阶段的中文显示名</param>
    Public Sub PushStreamBegin(phase As String, Optional label As String = Nothing)
        Call PostMessage(New With {
            .action = "llm_stream",
            .mode = "begin",
            .phase = phase,
            .label = label
        })
    End Sub

    ''' <summary>
    ''' 把大语言模型输出的一个 token 文本直接推送到网页端。
    ''' 收到即发，宿主侧不做任何缓冲或者攒批处理，保证网页端看到的内容完全是实时的。
    ''' </summary>
    ''' <param name="phase">阶段标识</param>
    ''' <param name="kind">内容类型：<c>think</c> 为思考过程，<c>output</c> 为正文输出</param>
    ''' <param name="text">本次收到的 token 文本</param>
    Public Sub PushStreamToken(phase As String, kind As String, text As String)
        Call PostMessage(New With {
            .action = "llm_stream",
            .mode = "append",
            .phase = phase,
            .kind = kind,
            .text = text
        })
    End Sub

    ''' <summary>
    ''' 通知网页：当前阶段的流式输出已经结束
    ''' </summary>
    ''' <param name="phase">阶段标识</param>
    ''' <param name="errorText">这一阶段发生异常时传入的错误文本</param>
    Public Sub PushStreamEnd(phase As String, Optional errorText As String = Nothing)
        Call PostMessage(New With {
            .action = "llm_stream",
            .mode = "end",
            .phase = phase,
            .error = errorText
        })
    End Sub

    ''' <summary>
    ''' 在当前页面之中异步执行一段 JavaScript 代码，执行失败的时候静默忽略
    ''' </summary>
    ''' <param name="script"></param>
    Public Sub EvalScriptAsync(script As String)
        If WebView21 Is Nothing OrElse WebView21.CoreWebView2 Is Nothing Then
            Return
        End If

        Try
            If InvokeRequired Then
                Call BeginInvoke(Sub() RunScript(script))
            Else
                Call RunScript(script)
            End If
        Catch ex As Exception
            Call Console.WriteLine(ex.Message)
        End Try
    End Sub

    Private Async Sub RunScript(script As String)
        Try
            Await WebView21.ExecuteScriptAsync(script)
        Catch ex As Exception
            Call Console.WriteLine(ex.Message)
        End Try
    End Sub

    Private Sub OnHostLog(message As String)
        RaiseEvent HostLog(message)
    End Sub

    Private Async Sub WebUI_Load(sender As Object, e As EventArgs) Handles Me.Load
        Call js.SetLogger(AddressOf OnHostLog)

        Await WebViewLoader.Init(WebView21, enableDevTool:=enableDevTools)
    End Sub

    Private Sub WebView21_CoreWebView2InitializationCompleted(sender As Object, e As CoreWebView2InitializationCompletedEventArgs) Handles WebView21.CoreWebView2InitializationCompleted
        If Not e.IsSuccess OrElse WebView21.CoreWebView2 Is Nothing Then
            Call Console.WriteLine($"WebView2 init failed: {e.InitializationException?.Message}")
            Return
        End If

        If enableDevTools Then
            Call WebViewLoader.DeveloperOptions(WebView21, True)
        End If

        ' 宿主对象只需要注入一次，页面导航之后依然有效。
        ' 注意 AddHostObjectToScript 只会把对象注册到 chrome.webview.hostObjects.{name}，
        ' 并不会创建 window.{name}；而且这条链路依赖 COM IDispatch，某些情况下会在
        ' 读取属性的阶段就失败，因此主通道改用 Web Message（见 OnWebMessage）。
        Call WebView21.CoreWebView2.AddHostObjectToScript("host", js)

        If Not webMessageHooked Then
            webMessageHooked = True

            AddHandler WebView21.CoreWebView2.WebMessageReceived, AddressOf OnWebMessage
        End If

        webViewReady = True

        If Not String.IsNullOrEmpty(pendingHtml) Then
            Call SetUI(pendingHtml)
        End If
    End Sub

    Private Sub WebView21_NavigationCompleted(sender As Object, e As CoreWebView2NavigationCompletedEventArgs) Handles WebView21.NavigationCompleted
        RaiseEvent UIReady()
    End Sub

    ''' <summary>
    ''' 处理网页通过 <c>chrome.webview.postMessage</c> 发送过来的宿主调用请求。
    ''' 这是网页调用宿主能力的<b>主通道</b>：相对 COM 宿主对象，它没有任何
    ''' IDispatch 依赖，也不会受 AddHostObjectToScript 包装失败的影响。
    ''' </summary>
    Private Async Sub OnWebMessage(sender As Object, e As CoreWebView2WebMessageReceivedEventArgs)
        Dim root As JsonElement

        Try
            Using doc As JsonDocument = JsonDocument.Parse(e.WebMessageAsJson)
                root = doc.RootElement.Clone()
            End Using
        Catch ex As Exception
            Call Console.WriteLine($"无法解析网页消息: {ex.Message}")
            Return
        End Try

        Select Case JsonStr(root, "action")
            Case "host_call"
                Dim result As String = Await js.CallHost(JsonStr(root, "command"), If(JsonStr(root, "payload"), "{}"))

                ' result 本身就是一段 json 文本，这里作为字符串回传，
                ' 由网页侧再解析一次，避免 json 嵌套转义带来的问题
                Call PostMessage(New With {
                    .action = "host_result",
                    .id = JsonStr(root, "id"),
                    .result = If(result, "")
                })

            Case "host_log"
                Call OnHostLog(If(JsonStr(root, "message"), ""))

            Case "genui_errors"
                ' 页面捕获到了运行时错误
                Call MergeIssues(root)
                Call CompleteHealthWaiter()

            Case "genui_check"
                ' 页面自检报告
                Call ApplyCheck(root)
        End Select
    End Sub

    Private Shared Function JsonStr(root As JsonElement, name As String) As String
        Dim v As JsonElement

        If root.TryGetProperty(name, v) AndAlso v.ValueKind = JsonValueKind.String Then
            Return v.GetString()
        End If

        Return Nothing
    End Function

    Private Shared Function JsonInt(root As JsonElement, name As String) As Integer
        Dim v As JsonElement

        If root.TryGetProperty(name, v) AndAlso v.ValueKind = JsonValueKind.Number Then
            Return v.GetInt32()
        End If

        Return 0
    End Function

    ''' <summary>
    ''' 收集网页回传的运行时错误
    ''' </summary>
    Private Sub MergeIssues(root As JsonElement)
        Dim arr As JsonElement

        If Not root.TryGetProperty("errors", arr) OrElse arr.ValueKind <> JsonValueKind.Array Then
            Return
        End If

        SyncLock PageIssues
            For Each item As JsonElement In arr.EnumerateArray()
                Call PageIssues.Add(New PageIssue With {
                    .kind = JsonStr(item, "kind"),
                    .message = JsonStr(item, "message"),
                    .extra = JsonStr(item, "extra")
                })
            Next
        End SyncLock
    End Sub

    ''' <summary>
    ''' 合并页面自检结果与已经收集到的运行时错误，得出最终的体检报告
    ''' </summary>
    Private Sub ApplyCheck(root As JsonElement)
        Dim health As New PageHealth With {
            .controls = JsonInt(root, "controls"),
            .expected = JsonInt(root, "expected"),
            .buttons = JsonInt(root, "buttons")
        }

        Dim all As New List(Of PageIssue)

        SyncLock PageIssues
            all.AddRange(PageIssues)
        End SyncLock

        Dim arr As JsonElement

        If root.TryGetProperty("issues", arr) AndAlso arr.ValueKind = JsonValueKind.Array Then
            For Each item As JsonElement In arr.EnumerateArray()
                If item.ValueKind = JsonValueKind.String Then
                    all.Add(New PageIssue With {.kind = "check", .message = item.GetString()})
                End If
            Next
        End If

        If root.TryGetProperty("errors", arr) AndAlso arr.ValueKind = JsonValueKind.Array Then
            For Each item As JsonElement In arr.EnumerateArray()
                all.Add(New PageIssue With {
                    .kind = JsonStr(item, "kind"),
                    .message = JsonStr(item, "message"),
                    .extra = JsonStr(item, "extra")
                })
            Next
        End If

        health.issues = all.ToArray()
        health.healthy = Not health.NeedsRepair()

        LastHealth = health

        RaiseEvent PageDiagnostics(health)
        Call CompleteHealthWaiter()
    End Sub

    Private Sub CompleteHealthWaiter()
        Dim waiter As TaskCompletionSource(Of PageHealth) = HealthWaiter

        If Not waiter Is Nothing Then
            HealthWaiter = Nothing
            Call waiter.TrySetResult(LastHealth)
        End If
    End Sub

End Class
