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
    ''' <example>
    ''' // 在网页之中调用宿主命令
    ''' const result = JSON.parse(await host.invoke("run_script", JSON.stringify({k: 3})));
    ''' if (!result.ok) { throw new Error(result.error); }
    ''' </example>
    Public Async Function Invoke(command As String, Optional payload As String = "{}") As Task(Of String)
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
    ''' 网页中的 JavaScript 代码可以通过这个函数做能力探测。
    ''' </summary>
    ''' <returns></returns>
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
    ''' 宿主对象的版本号，网页侧可以调用这个函数判断宿主对象是否已经成功注入
    ''' </summary>
    ''' <returns></returns>
    Public Function Version() As String
        Return "GenerativeUI/1.0"
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
