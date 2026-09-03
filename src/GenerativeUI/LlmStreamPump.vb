Imports System.Text
Imports System.Threading.Tasks
Imports Ollama

''' <summary>
''' 把大语言模型的思考过程与输出内容以流式的方式实时推送到网页端，
''' 让用户能够看见「AI 正在怎么生成这个界面」，而不是只看到一个进度条。
''' </summary>
''' <remarks>
''' LLM 的 token 回调频率非常高（每几十毫秒一次），如果每个 token 都直接发一条
''' web message，会产生大量的跨进程调用并拖慢 UI。因此这里先把 token 攒进缓冲区，
''' 由一个后台循环按固定间隔（默认 120ms）批量冲刷到网页。
''' </remarks>
Public Class LlmStreamPump

    ReadOnly ui As WebUI
    ReadOnly pending As New Dictionary(Of String, StringBuilder)(StringComparer.OrdinalIgnoreCase)
    ReadOnly gate As New Object

    Dim worker As Task
    Dim cancelled As Boolean = False
    Dim currentPhase As String = "work"
    Dim currentLabel As String = ""
    Dim startedAt As DateTime

    ''' <summary>
    ''' 缓冲区冲刷到网页的最小间隔（毫秒）
    ''' </summary>
    ''' <returns></returns>
    Public Property FlushIntervalMs As Integer = 120

    ''' <summary>
    ''' 是否启用流式推送；关闭之后 LLM 的输出只会写入诊断日志，不会推送到网页
    ''' </summary>
    ''' <returns></returns>
    Public Property Enabled As Boolean = True

    ''' <summary>
    ''' 单条 web message 之中最多携带多少个字符，超出部分会被拆分成多条发送
    ''' </summary>
    ''' <returns></returns>
    Public Property MaxChunkChars As Integer = 1200

    Sub New(ui As WebUI)
        Me.ui = ui
    End Sub

    ''' <summary>
    ''' 把一个 LLM 客户端挂到这个推送器之上。
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

        SyncLock gate
            pending.Clear()
            currentPhase = If(phase, "work")
            currentLabel = If(label, currentPhase)
            startedAt = DateTime.Now
            cancelled = False

            If worker Is Nothing Then
                worker = Task.Run(AddressOf FlushLoop)
            End If
        End SyncLock

        Call ui.PostMessage(New With {
            .action = "llm_stream",
            .mode = "begin",
            .phase = currentPhase,
            .label = currentLabel
        })
    End Sub

    ''' <summary>
    ''' 写入一段 LLM 输出的 token 文本
    ''' </summary>
    ''' <param name="phase">阶段标识</param>
    ''' <param name="kind">内容类型：<c>think</c> 为思考过程，<c>output</c> 为正文输出</param>
    ''' <param name="text">token 文本</param>
    Public Sub Write(phase As String, label As String, kind As String, text As String)
        If Not Enabled OrElse String.IsNullOrEmpty(text) Then
            Return
        End If

        SyncLock gate
            Dim key As String = $"{phase}|{kind}"

            If Not pending.ContainsKey(key) Then
                pending(key) = New StringBuilder()
            End If

            Call pending(key).Append(text)
        End SyncLock
    End Sub

    ''' <summary>
    ''' 结束当前阶段的流式推送，并冲刷掉缓冲区之中剩余的内容
    ''' </summary>
    ''' <param name="errorText">当这一阶段发生异常时传入错误文本</param>
    Public Sub Finish(Optional errorText As String = Nothing)
        Call Flush()

        cancelled = True
        worker = Nothing

        If Not Enabled OrElse ui Is Nothing Then
            Return
        End If

        Call ui.PostMessage(New With {
            .action = "llm_stream",
            .mode = "end",
            .phase = currentPhase,
            .error = errorText
        })
    End Sub

    Private Async Function FlushLoop() As Task
        Do While Not cancelled
            Try
                Await Task.Delay(FlushIntervalMs)
            Catch ex As Exception
                Return
            End Try

            If cancelled Then
                Return
            End If

            Call Flush()
        Loop
    End Function

    ''' <summary>
    ''' 把缓冲区之中的内容拆成合适大小的块推送到网页
    ''' </summary>
    Private Sub Flush()
        Dim chunks As New List(Of StreamChunk)

        SyncLock gate
            For Each kv As KeyValuePair(Of String, StringBuilder) In pending
                If kv.Value.Length = 0 Then
                    Continue For
                End If

                Dim parts As String() = kv.Key.Split("|"c)
                Dim text As String = kv.Value.ToString()

                kv.Value.Clear()

                Dim offset As Integer = 0

                Do While offset < text.Length
                    Dim len As Integer = Math.Min(MaxChunkChars, text.Length - offset)

                    chunks.Add(New StreamChunk With {
                        .phase = parts(0),
                        .kind = parts(1),
                        .text = text.Substring(offset, len)
                    })

                    offset += len
                Loop
            Next
        End SyncLock

        If ui Is Nothing Then
            Return
        End If

        For Each chunk As StreamChunk In chunks
            Call ui.PostMessage(New With {
                .action = "llm_stream",
                .mode = "append",
                .phase = chunk.phase,
                .kind = chunk.kind,
                .text = chunk.text
            })
        Next
    End Sub

    Private Class StreamChunk
        Public Property phase As String
        Public Property kind As String
        Public Property text As String
    End Class

End Class
