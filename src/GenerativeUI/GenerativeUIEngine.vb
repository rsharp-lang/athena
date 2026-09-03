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
    Public ReadOnly Property LLM As LLMClient
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
                Call PushStatus("大语言模型没有返回可用的界面代码", "error")
                Call ui.SetUI(HtmlPage.ErrorPage(
                    title:=errorTitle,
                    message:="大语言模型多次尝试之后依然没有产出可以被渲染的 HTML 文档，请重新选择脚本再试一次。",
                    detail:=LastOutput))

                Return False
            End If

            Call ui.SetUI(html)

            Return True
        Catch ex As Exception
            Call App.LogException(ex)
            Call PushStatus($"生成界面时出现异常: {ex.Message}", "error")
            Call ui.SetUI(HtmlPage.ErrorPage(title:=errorTitle, message:=ex.Message, detail:=ex.ToString()))

            Return False
        End Try
    End Function

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
