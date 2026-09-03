Imports System.Runtime.InteropServices
Imports System.Threading.Tasks

''' <summary>
''' 注入到 WebView2 网页之中的宿主对象：网页中的 JavaScript 代码可以通过
''' <c>window.host</c> 访问到这个对象，从而获得到调用 .NET 宿主代码的能力。
''' </summary>
''' <remarks>
''' 这个对象只暴露出一个统一的 <see cref="Invoke(String, String)"/> 入口，
''' 具体可用的宿主能力则通过 <see cref="Commands"/> 注册表进行注册。
''' 采用这种设计的好处在于：新增宿主能力的时候不需要修改这个 COM 可见类型，
''' 也不需要手工同步维护写给 LLM 看的提示词文本——命令清单会自动的
''' 由 <see cref="HostCommandRegistry.Describe"/> 生成出来。
''' </remarks>
<ClassInterface(ClassInterfaceType.AutoDual)>
<ComVisible(True)>
Public Class JavascriptInterop

    ReadOnly registry As New HostCommandRegistry()

    Dim logger As Action(Of String)

    Sub New()
    End Sub

    ''' <summary>
    ''' 挂载一个日志处理器，用于接收网页侧的调试日志输出
    ''' </summary>
    ''' <param name="logger"></param>
    Friend Sub SetLogger(logger As Action(Of String))
        Me.logger = logger
    End Sub

    ''' <summary>
    ''' 当前这个宿主对象所持有的命令注册表，通过向注册表之中添加命令即可
    ''' 扩展网页侧可以调用的宿主能力。
    ''' </summary>
    ''' <returns></returns>
    ''' <remarks>
    ''' 这个属性不暴露给 COM：注册表中的 <see cref="HostCommand.Handler"/> 是委托类型，
    ''' 无法被 COM 封送，若一并发布出去会导致整个 class interface 的 type info 生成失败，
    ''' 表现为页面调用任何宿主方法都返回 DISP_E_UNKNOWNNAME。
    ''' </remarks>
    <ComVisible(False)>
    Public ReadOnly Property Commands As HostCommandRegistry
        Get
            Return registry
        End Get
    End Property

    ''' <summary>
    ''' 网页调用宿主能力的唯一入口
    ''' </summary>
    ''' <param name="command">
    ''' 目标宿主命令的名称，必须是已经通过 <see cref="Commands"/> 注册过的命令名
    ''' </param>
    ''' <param name="payload">
    ''' 传递给目标命令的 json 字符串载荷，没有参数的时候请传入 <c>"{}"</c> 而不是空引用
    ''' </param>
    ''' <returns>
    ''' json 字符串形式的调用结果。成功时为 <c>{ok:true, data:...}</c>；
    ''' 失败时为 <c>{ok:false, error:"..."}</c>。任何异常都不会穿透 COM 边界。
    ''' </returns>
    ''' <remarks>
    ''' 注意：这个方法名不能使用 <c>Invoke</c>。<c>Invoke</c> 是 COM IDispatch 自身的
    ''' 方法名，.NET 在为托管对象构建 COM 分发表时会过滤掉与 IDispatch/IUnknown 成员
    ''' 同名的成员，导致 GetIDsOfNames("invoke") 返回 DISP_E_UNKNOWNNAME (0x80020006)。
    ''' </remarks>
    ''' <example>
    ''' // 在网页之中调用宿主命令
    ''' const result = JSON.parse(await host.callHost("run_script", JSON.stringify({k: 3})));
    ''' if (!result.ok) { throw new Error(result.error); }
    ''' </example>
    Public Async Function CallHost(command As String, payload As String) As Task(Of String)
        Dim name As String = If(command, "").Trim().ToLower()

        ' 框架内置的自检命令：全部走 callHost 这个带参数的入口，
        ' 避免在 COM 表面之上再暴露无参方法（无参方法在 WebView2 的异步代理之上
        ' 可能被当成属性，调用时报 “unable to call method on non-function”）。
        ' 注意返回值必须同样遵守 {ok, data, error} 契约，否则网页侧解析会失败。
        Select Case name
            Case "ping", "__ping__"
                Return HostMessage.Success(PingInfo())
            Case "version", "__version__"
                Return HostMessage.Success(Version())
            Case "get_commands", "__commands__", "commands"
                Return HostMessage.Success(GetCommands())
        End Select

        Dim cmd As HostCommand = registry.Find(command)
        Dim args As String = If(payload, "{}")

        If cmd Is Nothing Then
            Dim known As String = String.Join(", ", registry.Names)

            Call WriteLog($"未知的宿主命令: '{command}'，当前可用: {known}")

            Return HostMessage.Failure($"宿主命令 '{command}' 没有被注册，当前可用的命令有: {known}")
        End If

        Try
            Dim result As String = Await cmd.Handler.Invoke(args)

            Call WriteLog($"<{command}> -> {Clip(result)}")

            Return If(String.IsNullOrEmpty(result), HostMessage.Success(True), result)
        Catch ex As Exception
            Call App.LogException(ex)
            Call WriteLog($"<{command}> error: {ex.Message}")

            Return HostMessage.Failure($"{ex.GetType.Name}: {ex.Message}")
        End Try
    End Function

    ''' <summary>
    ''' 以 json 数组的形式返回当前所有已经注册了的宿主命令的元数据信息，
    ''' 网页中的 JavaScript 代码可以通过 callHost('get_commands') 做能力探测。
    ''' </summary>
    ''' <returns></returns>
    <ComVisible(False)>
    Public Function GetCommands() As String
        Return registry.GetJson()
    End Function

    ''' <summary>
    ''' 网页侧向宿主写日志，用于排查生成式界面之中 JavaScript 代码的运行时错误
    ''' </summary>
    ''' <param name="message"></param>
    Public Sub Log(message As String)
        Call WriteLog($"[js] {message}")
    End Sub

    ''' <summary>
    ''' 宿主对象的版本号，网页侧可以通过 callHost('version') 获取
    ''' </summary>
    ''' <returns></returns>
    <ComVisible(False)>
    Public Function Version() As String
        Return "GenerativeUI/1.0"
    End Function

    ''' <summary>
    ''' 连通性自检信息，网页侧通过 callHost('ping') 获取，
    ''' 返回值形如 <c>GenerativeUI/1.0 (6 commands)</c>。
    ''' </summary>
    ''' <returns></returns>
    <ComVisible(False)>
    Public Function PingInfo() As String
        Return $"{Version()} ({registry.Names.Length} commands)"
    End Function

    Private Shared Function Clip(text As String, Optional maxLen As Integer = 120) As String
        If String.IsNullOrEmpty(text) Then
            Return ""
        ElseIf text.Length <= maxLen Then
            Return text
        Else
            Return text.Substring(0, maxLen) & "..."
        End If
    End Function

    Private Sub WriteLog(message As String)
        If Not logger Is Nothing Then
            Try
                Call logger(message)
            Catch ex As Exception
                Call Console.WriteLine(ex.Message)
            End Try
        End If

        Call Console.WriteLine(message)
    End Sub

End Class
