Imports System.Text.Json.Serialization

''' <summary>
''' 网页与宿主之间基于 Web Message 传递的消息信封。
''' </summary>
''' <remarks>
''' 之所以需要这条通道：WebView2 的 <c>AddHostObjectToScript</c> 依赖 COM IDispatch 包装，
''' 在部分运行时/对象组合之下会在读取属性的阶段就失败（例如 E_INVALIDARG 0x80070057），
''' 而且它 <b>不会</b> 创建 <c>window.{name}</c>，只能通过 <c>chrome.webview.hostObjects.{name}</c> 访问。
''' 基于 postMessage 的通道没有任何 COM 依赖，是最可靠的双向通信方式。
''' </remarks>
Public Class HostBridgeMessage

    ''' <summary>
    ''' 消息类型：<c>host_call</c> 表示网页请求调用宿主命令，
    ''' <c>host_result</c> 表示宿主回传调用结果，<c>host_log</c> 表示网页写日志。
    ''' </summary>
    ''' <returns></returns>
    Public Property action As String

    ''' <summary>
    ''' 调用序号，网页侧用它把返回结果对应回当初的 Promise
    ''' </summary>
    ''' <returns></returns>
    Public Property id As String

    ''' <summary>
    ''' 需要调用的宿主命令名称
    ''' </summary>
    ''' <returns></returns>
    Public Property command As String

    ''' <summary>
    ''' 宿主命令的 json 字符串载荷
    ''' </summary>
    ''' <returns></returns>
    Public Property payload As String

    ''' <summary>
    ''' 日志文本（<c>host_log</c> 消息使用）
    ''' </summary>
    ''' <returns></returns>
    Public Property message As String

End Class
