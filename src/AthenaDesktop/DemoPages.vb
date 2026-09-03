Imports System.IO
Imports System.Reflection
Imports GenerativeUI

''' <summary>
''' demo 项目之中内置的 html 页面模板读取器
''' </summary>
Public Module DemoPages

    ReadOnly cache As New Dictionary(Of String, String)(StringComparer.OrdinalIgnoreCase)

    ''' <summary>
    ''' 从当前程序集之中读取一个嵌入的 html 模板文件
    ''' </summary>
    ''' <param name="fileName"></param>
    ''' <returns></returns>
    Public Function ReadResource(fileName As String) As String
        SyncLock cache
            If cache.ContainsKey(fileName) Then
                Return cache(fileName)
            End If
        End SyncLock

        Dim assembly As Assembly = Assembly.GetExecutingAssembly()
        Dim name As String = assembly.GetManifestResourceNames() _
            .FirstOrDefault(Function(n) n.EndsWith(fileName, StringComparison.OrdinalIgnoreCase))

        If name Is Nothing Then
            Throw New FileNotFoundException($"找不到嵌入的界面模板资源: {fileName}")
        End If

        Dim text As String

        Using reader As New StreamReader(assembly.GetManifestResourceStream(name), New Text.UTF8Encoding(False))
            text = reader.ReadToEnd()
        End Using

        SyncLock cache
            cache(fileName) = text
        End SyncLock

        Return text
    End Function

    ''' <summary>
    ''' demo 的启动引导页：提供「打开 R 脚本」与「载入 Iris 演示脚本」两个入口
    ''' </summary>
    ''' <returns></returns>
    Public Function Launcher() As String
        Return HtmlPage.Normalize(ReadResource("launcher.html"))
    End Function

End Module
