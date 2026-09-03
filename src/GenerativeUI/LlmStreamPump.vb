Imports System.Threading.Tasks
Imports Ollama

''' <summary>
''' 把大语言模型的思考过程与输出内容实时推送到网页端的接线器。
''' </summary>
''' <remarks>
''' 这个类型只负责把 <see cref="LLMClient.HookResponseStream(Action(Of String), Action(Of String))"/>
''' 的 token 回调转发到 <see cref="WebUI"/> 之上，<b>不做任何缓冲、攒批或者节流</b>：
''' 每收到一个 token 就立刻推送到网页端，保证用户在页面上看到的内容与模型的
''' 生成过程完全同步。
''' </remarks>
Public Class LlmStreamPump

    ReadOnly ui As WebUI

    Dim currentPhase As String = "work"

    ''' <summary>
    ''' 是否启用流式推送；关闭之后模型的输出不会推送到网页
    ''' </summary>
    ''' <returns></returns>
    Public Property Enabled As Boolean = True

    Sub New(ui As WebUI)
        Me.ui = ui
    End Sub

    ''' <summary>
    ''' 把一个 LLM 客户端挂到这个推送器之上
    ''' </summary>
    ''' <param name="llm">需要被监听的大语言模型客户端</param>
    ''' <param name="phase">阶段标识，用于在网页上区分当前是哪一个阶段在输出</param>
    ''' <param name="label">阶段的中文显示名</param>
    Public Sub Attach(llm As LLMClient, phase As String, Optional label As String = Nothing)
        If llm Is Nothing Then
            Return
        End If

        Dim p As String = If(phase, "work")
        Dim t As String = If(label, p)

        Call llm.HookResponseStream(
            getOutputToken:=Sub(token) Write(p, t, "output", token),
            getThinkToken:=Sub(token) Write(p, t, "think", token))
    End Sub

    ''' <summary>
    ''' 开始一个新的阶段：网页端会清空上一阶段的显示内容
    ''' </summary>
    ''' <param name="phase">阶段标识</param>
    ''' <param name="label">阶段中文显示名</param>
    Public Sub Begin(phase As String, Optional label As String = Nothing)
        If Not Enabled OrElse ui Is Nothing Then
            Return
        End If

        currentPhase = If(phase, "work")

        Call ui.PushStreamBegin(currentPhase, If(label, currentPhase))
    End Sub

    ''' <summary>
    ''' 把一个 token 文本直接推送到网页端
    ''' </summary>
    ''' <param name="phase">阶段标识</param>
    ''' <param name="label">阶段显示名</param>
    ''' <param name="kind">内容类型：<c>think</c> 为思考过程，<c>output</c> 为正文输出</param>
    ''' <param name="text">token 文本</param>
    Public Sub Write(phase As String, label As String, kind As String, text As String)
        If Not Enabled OrElse ui Is Nothing OrElse String.IsNullOrEmpty(text) Then
            Return
        End If

        Call ui.PushStreamToken(If(phase, "work"), kind, text)
    End Sub

    ''' <summary>
    ''' 结束当前阶段的流式推送
    ''' </summary>
    ''' <param name="errorText">当这一阶段发生异常时传入错误文本</param>
    Public Sub Finish(Optional errorText As String = Nothing)
        If Not Enabled OrElse ui Is Nothing Then
            Return
        End If

        Call ui.PushStreamEnd(currentPhase, errorText)
    End Sub

End Class
