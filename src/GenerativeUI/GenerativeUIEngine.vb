Imports System.Linq
Imports System.Text
Imports System.Text.Json
Imports System.Threading.Tasks
Imports Ollama

''' <summary>
''' 生成式界面引擎：负责把用户的自然语言需求交给大语言模型，
''' 由大语言模型编写出 html 界面代码，再渲染到 <see cref="WebUI"/> 控件之中。
''' </summary>
''' <remarks>
''' 引擎内部完成了「提问 → 抽取 html → 校验 → 规整 → 渲染」的完整管线，
''' 并且在生成失败的时候自动回退为框架内置的错误页面，保证界面永远不会白屏。
''' </remarks>
Public Class GenerativeUIEngine

    ReadOnly ui As WebUI
    ReadOnly llm As LLMClient
    ReadOnly host As JavascriptInterop

    ''' <summary>
    ''' 生成 html 界面时所使用的系统提示词，默认由 <see cref="UIAuthoringPrompt"/> 依据
    ''' 宿主命令注册表自动生成，调用方可以追加或者完全替换。
    ''' </summary>
    ''' <returns></returns>
    Public Property Rules As String

    ''' <summary>
    ''' 判定模型输出是否为合法 html 文档的最小长度阈值
    ''' </summary>
    ''' <returns></returns>
    Public Property MinHtmlLength As Integer = 256

    ''' <summary>
    ''' 生成非法 html 时的最大重试次数
    ''' </summary>
    ''' <returns></returns>
    Public Property MaxRetry As Integer = 1

    ''' <summary>
    ''' 页面渲染之后的运行期体检发现问题时的最大自动修复轮数。
    ''' 每一轮都是一次完整的 html 重新生成，会明显增加等待时间，因此不宜过大。
    ''' </summary>
    ''' <returns></returns>
    Public Property MaxRepair As Integer = 2

    ''' <summary>
    ''' 渲染完成之后等待页面自检结果的时间（毫秒）。
    ''' 太短会漏掉延迟绑定的事件，太长用户等得久。
    ''' </summary>
    ''' <returns></returns>
    Public Property InspectDelayMs As Integer = 1500

    ''' <summary>
    ''' 等待页面自检结果的超时时间（毫秒），超时之后按「页面健康」处理以避免流程卡死
    ''' </summary>
    ''' <returns></returns>
    Public Property HealthTimeoutMs As Integer = 5000

    ''' <summary>
    ''' 最近一次生成的、尚未注入引导脚本的原始 html 代码。
    ''' 自动修复的时候把它回喂给模型，比回喂注入了引导脚本的完整文档更短也更清晰。
    ''' </summary>
    ''' <returns></returns>
    Public Property LastRawHtml As String

    ''' <summary>
    ''' 最近一次页面体检的结果
    ''' </summary>
    ''' <returns></returns>
    Public Property LastHealth As PageHealth

    ''' <summary>
    ''' 引擎的运行状态变化事件
    ''' </summary>
    ''' <param name="message"></param>
    ''' <param name="level">info / success / warn / error</param>
    Public Event Status(message As String, level As String)

    Sub New(ui As WebUI, llm As LLMClient, Optional host As JavascriptInterop = Nothing)
        Me.ui = ui
        Me.llm = llm

        If host Is Nothing AndAlso Not ui Is Nothing Then
            Me.host = ui.Host
        Else
            Me.host = host
        End If

        Me.Rules = UIAuthoringPrompt.Build(Me.host)

        If Not llm Is Nothing Then
            llm.system_message = Me.Rules
        End If
    End Sub

    ''' <summary>
    ''' 当前引擎所使用的大语言模型客户端
    ''' </summary>
    ''' <returns></returns>
    Public ReadOnly Property Model As LLMClient
        Get
            Return llm
        End Get
    End Property

    ''' <summary>
    ''' 清空大语言模型的对话记忆，通常在切换任务目标的时候调用
    ''' </summary>
    ''' <returns></returns>
    Public Function ClearMemory() As GenerativeUIEngine
        If Not llm Is Nothing Then
            Call llm.Clear()
        End If

        Return Me
    End Function

    ''' <summary>
    ''' 向界面推送一条状态文本
    ''' </summary>
    ''' <param name="message"></param>
    ''' <param name="level">info / success / warn / error</param>
    Public Sub PushStatus(message As String, Optional level As String = "info")
        RaiseEvent Status(message, level)

        If Not ui Is Nothing Then
            Call ui.PushStatus(message, level)
        End If
    End Sub

    ''' <summary>
    ''' 让大语言模型依据需求描述生成一段 html 界面代码（只生成，不渲染）
    ''' </summary>
    ''' <param name="request">写给大语言模型的需求描述，其中应当包含参数清单等上下文信息</param>
    ''' <returns>规整之后的 html 文档；生成失败时返回空字符串</returns>
    Public Async Function GenerateHTML(request As String) As Task(Of String)
        Dim prompt As String = request
        Dim response As LLMsResponse = Nothing

        For retry As Integer = 0 To MaxRetry
            Call PushStatus(If(retry = 0, "正在等待大语言模型编写操作界面…", $"第 {retry} 次重试：模型输出不是合法的 HTML，正在重新生成…"))

            response = Await llm.Chat(prompt)

            LastOutput = If(response Is Nothing, "", response.output)

            Dim html As String = HtmlPage.ExtractHtml(LastOutput)

            If HtmlPage.IsHtmlDocument(html) AndAlso html.Length >= MinHtmlLength Then
                LastRawHtml = html

                Call PushStatus("界面代码生成完毕，正在渲染…", "success")

                Return HtmlPage.Normalize(html, stateJson:=BuildState())
            End If

            prompt = "你上一次的回复不是一个可以被渲染的完整 HTML 文档（没有找到以 <!DOCTYPE html> 或者 <html> 开头的完整文档结构）。" &
                     "请重新输出：只允许输出一个 ```html 代码块，代码块内必须是完整的 HTML 文档，不要写任何解释文字。" &
                     vbCrLf & vbCrLf & "原始需求：" & vbCrLf & request
        Next

        Return ""
    End Function

    ''' <summary>
    ''' 生成 html 界面并立即渲染到 WebView2 之中；生成失败时渲染框架内置的错误页面
    ''' </summary>
    ''' <param name="request">写给大语言模型的需求描述</param>
    ''' <param name="errorTitle">生成失败时错误页面所显示的标题</param>
    ''' <returns>生成成功则返回 True</returns>
    Public Async Function Render(request As String, Optional errorTitle As String = "生成式界面构建失败") As Task(Of Boolean)
        Try
            Dim html As String = Await GenerateHTML(request)

            If String.IsNullOrEmpty(html) Then
                Call ShowGenerateFailure(errorTitle)
                Return False
            End If

            ' ---- 第 0 层：宿主侧静态预检，命中就直接修正，省掉一次浏览器往返 ----
            Dim staticIssues As String() = HtmlPage.StaticCheck(html)

            If staticIssues.Length > 0 Then
                Call PushStatus($"界面代码存在 {staticIssues.Length} 处明显问题，正在让模型修正…", "warn")

                Dim repaired As String = Await Repair(
                    staticIssues.Select(Function(s) New PageIssue With {.kind = "static", .message = s}).ToArray())

                If Not String.IsNullOrEmpty(repaired) Then
                    html = repaired
                End If
            End If

            ' ---- 渲染 → 运行期体检 → 自动修复 ----
            Dim attempt As Integer = 0

            Do
                Call ui.ResetDiagnostics()
                Call ui.SetUI(html)

                Await Task.Delay(InspectDelayMs)

                Dim health As PageHealth = Await WaitHealth()

                LastHealth = health

                If health Is Nothing OrElse Not health.NeedsRepair() Then
                    ' 页面健康或者超时收不到报告都直接放行
                    Call ClearBanner()
                    Return True
                End If

                If attempt >= MaxRepair Then
                    Call PushStatus($"已经尝试修复 {MaxRepair} 次仍然存在异常，将回退为框架内置的参数表单", "warn")
                    Call ShowBanner("界面仍然存在异常，正在切换为内置表单", "error")

                    Await Task.Delay(1200)

                    Return False
                End If

                attempt += 1

                Call PushStatus($"检测到界面运行异常，正在让模型自我修复（第 {attempt}/{MaxRepair} 次）…", "warn")
                Call ShowBanner("检测到界面异常，正在自动修复…", "warn")

                Dim fixedHtml As String = Await Repair(health.issues)

                If String.IsNullOrEmpty(fixedHtml) Then
                    ' 修复失败，保留当前界面至少还能看见
                    Call ClearBanner()
                    Return True
                End If

                html = fixedHtml
            Loop
        Catch ex As Exception
            Call App.LogException(ex)
            Call PushStatus($"生成界面时出现异常: {ex.Message}", "error")
            Call ui.SetUI(HtmlPage.ErrorPage(title:=errorTitle, message:=ex.Message, detail:=ex.ToString()))

            Return False
        End Try
    End Function

    Private Sub ShowGenerateFailure(errorTitle As String)
        Call PushStatus("大语言模型没有返回可用的界面代码", "error")
        Call ui.SetUI(HtmlPage.ErrorPage(
            title:=errorTitle,
            message:="大语言模型多次尝试之后依然没有产出可以被渲染的 HTML 文档，请重新选择脚本再试一次。",
            detail:=LastOutput))
    End Sub

    ''' <summary>
    ''' 等待页面回传自检报告
    ''' </summary>
    Private Async Function WaitHealth() As Task(Of PageHealth)
        Dim waiter As New TaskCompletionSource(Of PageHealth)(TaskCreationOptions.RunContinuationsAsynchronously)

        ui.HealthWaiter = waiter

        Dim timeout As Task = Task.Delay(HealthTimeoutMs)

        If Await Task.WhenAny(waiter.Task, timeout) Is timeout Then
            ui.HealthWaiter = Nothing
            Return Nothing
        End If

        Return Await waiter.Task
    End Function

    ''' <summary>
    ''' 把浏览器之中真实报出来的错误连同上一次的完整代码一起回喂给模型，让它自我修复
    ''' </summary>
    ''' <param name="issues">页面回传的问题清单</param>
    ''' <returns>修复之后的 html 文档；修复失败时返回空字符串</returns>
    Private Async Function Repair(issues As PageIssue()) As Task(Of String)
        Dim sb As New StringBuilder
        Dim n As Integer = 0

        Call sb.AppendLine("你上一次编写的 HTML 界面在 WebView2 之中运行时出现了问题，请修复它。")
        Call sb.AppendLine()
        Call sb.AppendLine("## 发现的问题")

        If Not issues Is Nothing Then
            For Each issue As PageIssue In issues
                If issue Is Nothing Then
                    Continue For
                End If

                n += 1

                Call sb.AppendLine($"{n}. [{issue.kind}] {issue.message}")

                If Not String.IsNullOrWhiteSpace(issue.extra) Then
                    Call sb.AppendLine($"   位置/对象: {issue.extra}")
                End If
            Next
        End If

        If n = 0 Then
            Call sb.AppendLine("（宿主没有拿到具体的错误信息，但是检测到界面存在功能性缺失，请仔细检查参数控件是否齐全、事件是否正确绑定。）")
        End If

        Call sb.AppendLine()
        Call sb.AppendLine("## 你上一次生成的完整代码")
        Call sb.AppendLine("```html")
        Call sb.AppendLine(If(LastRawHtml, ""))
        Call sb.AppendLine("```")
        Call sb.AppendLine()
        Call sb.AppendLine("## 修复要求")
        Call sb.AppendLine("- 逐一解决上面列出的每一个问题，一个都不能遗漏。")
        Call sb.AppendLine("- 输出修复之后的**完整** HTML 文档，不要只输出差异片段或者局部代码。")
        Call sb.AppendLine("- 同样只输出一个 ```html 代码块，代码块之外不要写任何解释文字。")
        Call sb.AppendLine("- 不要引入任何外部资源，不要使用 ES module 与 import。")
        Call sb.AppendLine("- 所有 await 都要包在 try/catch 之中，并用 GenUI.status(e.message, 'error') 把错误显示出来。")

        Return Await GenerateHTML(sb.ToString())
    End Function

    ''' <summary>
    ''' 在页面顶部显示一条提示横幅（不遮挡原有界面，用户仍然可以继续操作）
    ''' </summary>
    Private Sub ShowBanner(message As String, level As String)
        Call ui.EvalScriptAsync(
            $"window.genui_banner && window.genui_banner({JsonSerializer.Serialize(message)}, {JsonSerializer.Serialize(level)})")
    End Sub

    Private Sub ClearBanner()
        Call ui.EvalScriptAsync("window.genui_banner && window.genui_banner('')")
    End Sub

    ''' <summary>
    ''' 最近一次大语言模型的原始输出，用于错误诊断
    ''' </summary>
    ''' <returns></returns>
    Public Property LastOutput As String

    ''' <summary>
    ''' 显示框架内置的加载中页面
    ''' </summary>
    ''' <param name="title"></param>
    ''' <param name="message"></param>
    ''' <param name="steps"></param>
    Public Sub ShowLoading(title As String, message As String, Optional steps As String() = Nothing)
        Call ui.SetUI(HtmlPage.LoadingPage(title, message, steps))
    End Sub

    ''' <summary>
    ''' 构造注入到页面之中的宿主状态数据
    ''' </summary>
    ''' <returns></returns>
    Private Function BuildState() As String
        Dim params As String = If(StateParams, "[]")

        Return $"{{""params"": {params}}}"
    End Function

    ''' <summary>
    ''' 需要注入到页面之中的参数描述 json 数组，通常来自于业务侧对任务的分析结果
    ''' </summary>
    ''' <returns></returns>
    Public Property StateParams As String

End Class
