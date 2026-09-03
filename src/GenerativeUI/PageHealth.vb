Imports System.Linq

''' <summary>
''' 由页面回传的一条运行时问题记录
''' </summary>
Public Class PageIssue

    ''' <summary>
    ''' 问题类型：
    ''' <c>error</c> 未捕获的 js 异常（语法错误、null 引用等）；
    ''' <c>rejection</c> 没有被 catch 的 Promise 异常；
    ''' <c>console</c> 页面调用 console.error 输出的内容；
    ''' <c>missing-dom</c> 取不到目标 DOM 元素；
    ''' <c>missing-api</c> 调用了不存在的 GenUI 方法；
    ''' <c>static</c> 宿主侧静态预检发现的问题；
    ''' <c>check</c> 页面自检发现的结构性问题。
    ''' </summary>
    ''' <returns></returns>
    Public Property kind As String
    ''' <summary>
    ''' 问题的具体描述，通常包含错误消息与堆栈
    ''' </summary>
    ''' <returns></returns>
    Public Property message As String
    ''' <summary>
    ''' 附加信息，例如出错的位置或者缺失的元素 id
    ''' </summary>
    ''' <returns></returns>
    Public Property extra As String

    Public Overrides Function ToString() As String
        Return $"[{kind}] {message}"
    End Function

End Class

''' <summary>
''' 一次页面渲染之后的健康检查结果
''' </summary>
Public Class PageHealth

    ''' <summary>
    ''' 页面是否健康（不存在需要修复的问题）
    ''' </summary>
    ''' <returns></returns>
    Public Property healthy As Boolean

    ''' <summary>
    ''' 页面上带 data-gu-param 属性的控件数量
    ''' </summary>
    ''' <returns></returns>
    Public Property controls As Integer

    ''' <summary>
    ''' 参数清单之中期望有多少个控件
    ''' </summary>
    ''' <returns></returns>
    Public Property expected As Integer

    ''' <summary>
    ''' 页面上的按钮数量
    ''' </summary>
    ''' <returns></returns>
    Public Property buttons As Integer

    ''' <summary>
    ''' 发现的所有问题
    ''' </summary>
    ''' <returns></returns>
    Public Property issues As PageIssue()

    Public Sub New()
        issues = New PageIssue() {}
    End Sub

    ''' <summary>
    ''' 只保留「确定会破坏功能」的硬错误。
    ''' console 输出与按钮缺失之类的问题只记录、不触发自动修复，避免误报导致无谓的重新生成。
    ''' </summary>
    ''' <returns></returns>
    Public Function FatalIssues() As PageIssue()
        Dim fatal As New HashSet(Of String)(StringComparer.OrdinalIgnoreCase) From {
            "error", "rejection", "missing-dom", "missing-api", "static"
        }

        If issues Is Nothing Then
            Return New PageIssue() {}
        End If

        Return issues _
            .Where(Function(i) Not i Is Nothing AndAlso fatal.Contains(If(i.kind, ""))) _
            .ToArray()
    End Function

    ''' <summary>
    ''' 是否存在「参数清单非空但页面上一个参数控件都没有」这类结构性缺失
    ''' </summary>
    ''' <returns></returns>
    Public Function MissingControls() As Boolean
        Return expected > 0 AndAlso controls = 0
    End Function

    ''' <summary>
    ''' 是否需要触发自动修复：存在硬错误，或者参数控件完全缺失
    ''' </summary>
    ''' <returns></returns>
    Public Function NeedsRepair() As Boolean
        Return FatalIssues().Length > 0 OrElse MissingControls()
    End Function

End Class
