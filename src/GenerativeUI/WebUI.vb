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

        ' 宿主对象只需要注入一次，页面导航之后依然有效
        Call WebView21.CoreWebView2.AddHostObjectToScript("host", js)

        webViewReady = True

        If Not String.IsNullOrEmpty(pendingHtml) Then
            Call SetUI(pendingHtml)
        End If
    End Sub

    Private Sub WebView21_NavigationCompleted(sender As Object, e As CoreWebView2NavigationCompletedEventArgs) Handles WebView21.NavigationCompleted
        RaiseEvent UIReady()
    End Sub

End Class
