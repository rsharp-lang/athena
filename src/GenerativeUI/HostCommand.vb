Imports System.Text
Imports System.Text.Json
Imports System.Threading.Tasks

''' <summary>
''' 宿主命令处理器委托：接收来自网页的 json 字符串载荷，返回 json 字符串结果。
''' </summary>
''' <param name="payload">
''' 网页通过 <c>host.callHost</c> 传入的 json 字符串，当命令不需要任何参数的时候请传入 <c>"{}"</c>。
''' </param>
''' <returns>
''' 回传给网页的 json 字符串，建议使用 <see cref="HostMessage.Success(Object)"/> 或者
''' <see cref="HostMessage.Failure(String)"/> 生成，以维持统一的 <c>{ok, data, error}</c> 契约。
''' </returns>
''' <remarks>
''' 统一使用 String 作为进出参数类型，是为了保证 COM 互操作边界上的兼容性与安全性：
''' 宿主对象是通过 <c>AddHostObjectToScript</c> 注入到 WebView2 网页之中的。
''' </remarks>
Public Delegate Function HostCommandHandler(payload As String) As Task(Of String)

''' <summary>
''' 一个可以被网页中的 JavaScript 代码通过 <c>host.callHost(name, payload)</c> 调用到的宿主命令
''' </summary>
Public Class HostCommand

    ''' <summary>
    ''' 命令名称，网页侧调用时所使用的唯一标识符，建议使用 snake_case 风格命名
    ''' </summary>
    ''' <returns></returns>
    Public Property Name As String
    ''' <summary>
    ''' 命令的功能说明文本，这个属性会被自动写入到 LLM 的系统提示词之中，
    ''' 使得生成出来的界面代码知道应该如何调用这个宿主命令。
    ''' </summary>
    ''' <returns></returns>
    Public Property Description As String
    ''' <summary>
    ''' 命令载荷的 json 示例片段，同样会被自动写入到 LLM 的系统提示词之中
    ''' </summary>
    ''' <returns></returns>
    Public Property PayloadSchema As String
    ''' <summary>
    ''' 命令所对应的 .NET 端处理函数
    ''' </summary>
    ''' <returns></returns>
    Public Property Handler As HostCommandHandler

    Public Overrides Function ToString() As String
        Return $"[{Name}] {Description}"
    End Function

End Class

''' <summary>
''' 宿主命令注册表：所有可供网页调用的宿主能力都集中注册在这个对象之中
''' </summary>
Public Class HostCommandRegistry

    ReadOnly commands As New Dictionary(Of String, HostCommand)(StringComparer.OrdinalIgnoreCase)

    ''' <summary>
    ''' 注册一个新的宿主命令
    ''' </summary>
    ''' <param name="name">命令名称，网页侧调用时所使用的唯一标识符</param>
    ''' <param name="description">写给 LLM 看的功能说明</param>
    ''' <param name="schema">写给 LLM 看的 payload json 示例片段</param>
    ''' <param name="handler">命令对应的 .NET 处理函数</param>
    ''' <returns>当前注册表对象自身，便于链式调用</returns>
    Public Function Register(name As String,
                             description As String,
                             schema As String,
                             handler As HostCommandHandler) As HostCommandRegistry

        If String.IsNullOrWhiteSpace(name) Then
            Throw New ArgumentException("the host command name can not be empty!", NameOf(name))
        End If
        If handler Is Nothing Then
            Throw New ArgumentNullException(NameOf(handler))
        End If

        commands(name.Trim()) = New HostCommand With {
            .Name = name.Trim(),
            .Description = If(description, "").Trim(),
            .PayloadSchema = If(schema, "").Trim(),
            .Handler = handler
        }

        Return Me
    End Function

    ''' <summary>
    ''' 注册一个新的宿主命令
    ''' </summary>
    ''' <param name="command"></param>
    ''' <returns>当前注册表对象自身，便于链式调用</returns>
    Public Function Register(command As HostCommand) As HostCommandRegistry
        Return Register(command.Name, command.Description, command.PayloadSchema, command.Handler)
    End Function

    ''' <summary>
    ''' 按照命令名称查找对应的宿主命令，找不到的时候返回 Nothing
    ''' </summary>
    ''' <param name="name"></param>
    ''' <returns></returns>
    Public Function Find(name As String) As HostCommand
        If String.IsNullOrWhiteSpace(name) OrElse Not commands.ContainsKey(name.Trim()) Then
            Return Nothing
        Else
            Return commands(name.Trim())
        End If
    End Function

    ''' <summary>
    ''' 获取所有已经注册了的宿主命令
    ''' </summary>
    ''' <returns></returns>
    Public ReadOnly Property All As HostCommand()
        Get
            Return commands.Values.ToArray()
        End Get
    End Property

    ''' <summary>
    ''' 获取所有已经注册了的宿主命令的名称列表
    ''' </summary>
    ''' <returns></returns>
    Public ReadOnly Property Names As String()
        Get
            Return commands.Keys.ToArray()
        End Get
    End Property

    ''' <summary>
    ''' 生成一份可以注入到 LLM 系统提示词之中的宿主 API 清单文本。
    ''' 通过这个机制，新注册的宿主能力可以自动的被 LLM 所知晓，不需要手工维护提示词。
    ''' </summary>
    ''' <returns></returns>
    Public Function Describe() As String
        Dim sb As New StringBuilder

        For Each cmd As HostCommand In All
            Call sb.AppendLine($"- 命令名: {cmd.Name}")
            Call sb.AppendLine($"  功能说明: {cmd.Description}")

            If Not String.IsNullOrWhiteSpace(cmd.PayloadSchema) Then
                Call sb.AppendLine($"  payload 示例: {cmd.PayloadSchema}")
            End If
        Next

        Return sb.ToString()
    End Function

    ''' <summary>
    ''' 以 json 数组的形式导出当前所有已注册命令的元数据信息
    ''' </summary>
    ''' <returns></returns>
    Public Function GetJson() As String
        Dim meta = All.Select(Function(c)
                                  Return New With {
                                      .name = c.Name,
                                      .description = c.Description,
                                      .payload = c.PayloadSchema
                                  }
                              End Function).ToArray()

        Return JsonSerializer.Serialize(meta)
    End Function

    Public Overrides Function ToString() As String
        Return String.Join(", ", Names)
    End Function

End Class
