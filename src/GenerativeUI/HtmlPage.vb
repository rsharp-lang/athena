Imports System.IO
Imports System.Net
Imports System.Reflection
Imports System.Text
Imports System.Text.RegularExpressions

''' <summary>
''' 生成式界面的 html 文档处理工具：负责从大语言模型的输出文本之中抽取 html 代码、
''' 校验文档骨架、注入引导脚本与基础样式，以及渲染框架内置的加载页与错误页。
''' </summary>
Public Module HtmlPage

    ReadOnly cache As New Dictionary(Of String, String)(StringComparer.OrdinalIgnoreCase)

    ''' <summary>
    ''' 基础样式表（设计令牌 + 基础组件外观）的文本内容
    ''' </summary>
    ''' <returns></returns>
    Public ReadOnly Property BaseCss As String
        Get
            Return ReadResource("base.css")
        End Get
    End Property

    ''' <summary>
    ''' 引导脚本（<c>window.GenUI</c> 宿主调用门面）的文本内容
    ''' </summary>
    ''' <returns></returns>
    Public ReadOnly Property BootstrapScript As String
        Get
            Return ReadResource("bootstrap.js")
        End Get
    End Property

    ''' <summary>
    ''' 从程序集的嵌入式资源之中读取一个 html 模板文件
    ''' </summary>
    ''' <param name="fileName">模板文件名，例如 <c>loading.html</c></param>
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

        Using reader As New StreamReader(assembly.GetManifestResourceStream(name), New UTF8Encoding(False))
            text = reader.ReadToEnd()
        End Using

        SyncLock cache
            cache(fileName) = text
        End SyncLock

        Return text
    End Function

    ''' <summary>
    ''' 从大语言模型的输出文本之中抽取出 html 文档代码
    ''' </summary>
    ''' <param name="llmOutput">大语言模型返回的原始文本</param>
    ''' <returns>抽取出来的 html 文档；当模型输出之中不存在任何 html 内容的时候返回空字符串</returns>
    ''' <remarks>
    ''' 依次尝试：```html 代码块 → 任意 ``` 代码块 → 从 &lt;!DOCTYPE&gt; 或 &lt;html&gt; 开始的全文截断。
    ''' </remarks>
    Public Function ExtractHtml(llmOutput As String) As String
        If String.IsNullOrWhiteSpace(llmOutput) Then
            Return ""
        End If

        Dim text As String = llmOutput.Trim()
        Dim block As Match = Regex.Match(text, "```(?:html|HTML|htm|xml)?\s*([\s\S]*?)```")

        If block.Success Then
            Return block.Groups(1).Value.Trim()
        End If

        Dim startPos As Integer = text.IndexOf("<!DOCTYPE", StringComparison.OrdinalIgnoreCase)

        If startPos < 0 Then
            startPos = text.IndexOf("<html", StringComparison.OrdinalIgnoreCase)
        End If

        If startPos < 0 Then
            Return ""
        End If

        Dim html As String = text.Substring(startPos)

        ' 去掉结尾处可能残留的 markdown 代码块结束标记
        html = Regex.Replace(html, "```+\s*$", "")
        ' 去掉结尾处模型可能追加的多余说明段落
        html = Regex.Replace(html, "</html>[\s\S]*$", "</html>", RegexOptions.IgnoreCase)

        Return html.Trim()
    End Function

    ''' <summary>
    ''' 判断给定的文本是否是一个看起来完整的 html 文档
    ''' </summary>
    ''' <param name="html"></param>
    ''' <returns></returns>
    Public Function IsHtmlDocument(html As String) As Boolean
        If String.IsNullOrWhiteSpace(html) OrElse html.Length < 64 Then
            Return False
        End If

        Return Regex.IsMatch(html, "<html[\s>]", RegexOptions.IgnoreCase) AndAlso
               Regex.IsMatch(html, "</html\s*>", RegexOptions.IgnoreCase)
    End Function

    ''' <summary>
    ''' 规整 html 文档：补齐文档类型声明与 head 骨架，并向其中注入基础样式、
    ''' 宿主状态数据与引导脚本。
    ''' </summary>
    ''' <param name="html">原始 html 文本，可以是完整文档也可以只是 body 片段</param>
    ''' <param name="stateJson">
    ''' 需要注入到页面之中的宿主状态数据（json 对象字符串），
    ''' 会以 <c>window.GENUI_STATE</c> 的形式暴露给页面脚本。
    ''' </param>
    ''' <returns>可以直接交给 <see cref="WebUI.SetUI(String)"/> 渲染的 html 文档</returns>
    Public Function Normalize(html As String, Optional stateJson As String = Nothing) As String
        Dim doc As String = If(html, "").Trim()

        If Not doc.StartsWith("<!DOCTYPE", StringComparison.OrdinalIgnoreCase) Then
            doc = "<!DOCTYPE html>" & vbLf & doc
        End If

        Dim head As New StringBuilder()

        head.AppendLine("<meta charset=""utf-8""/>")
        head.AppendLine("<meta name=""viewport"" content=""width=device-width, initial-scale=1""/>")
        head.AppendLine($"<style id=""genui-base"">{vbLf}{BaseCss}{vbLf}</style>")

        If Not String.IsNullOrWhiteSpace(stateJson) Then
            head.AppendLine($"<script>window.GENUI_STATE = {stateJson};</script>")
        End If

        head.AppendLine($"<script id=""genui-boot"">{vbLf}{BootstrapScript}{vbLf}</script>")

        Dim headTag As Match = Regex.Match(doc, "<head[^>]*>", RegexOptions.IgnoreCase)

        If headTag.Success Then
            Return doc.Substring(0, headTag.Index + headTag.Length) & vbLf &
                   head.ToString() &
                   doc.Substring(headTag.Index + headTag.Length)
        End If

        Dim htmlTag As Match = Regex.Match(doc, "<html[^>]*>", RegexOptions.IgnoreCase)
        Dim headBlock As String = "<head>" & vbLf & head.ToString() & "</head>"

        If htmlTag.Success Then
            Return doc.Substring(0, htmlTag.Index + htmlTag.Length) & vbLf &
                   headBlock &
                   doc.Substring(htmlTag.Index + htmlTag.Length)
        End If

        ' 连 html 根标签都没有：直接套一个完整的文档骨架
        Return "<!DOCTYPE html>" & vbLf &
               "<html lang=""zh-CN"">" & vbLf &
               headBlock & vbLf &
               "<body>" & vbLf & doc & vbLf & "</body>" & vbLf &
               "</html>"
    End Function

    ''' <summary>
    ''' 生成框架内置的"处理中"页面
    ''' </summary>
    ''' <param name="title">主标题</param>
    ''' <param name="message">副标题说明文本</param>
    ''' <param name="steps">需要展示出来的流程步骤列表</param>
    ''' <returns></returns>
    Public Function LoadingPage(title As String, message As String, Optional steps As String() = Nothing) As String
        Dim html As String = ReadResource("loading.html")
        Dim list As New StringBuilder()

        If Not steps Is Nothing Then
            For i As Integer = 0 To steps.Length - 1
                Call list.AppendLine($"<li><b>{i + 1}</b><span>{WebUtility.HtmlEncode(steps(i))}</span></li>")
            Next
        End If

        html = html.Replace("{{title}}", WebUtility.HtmlEncode(If(title, "处理中")))
        html = html.Replace("{{message}}", WebUtility.HtmlEncode(If(message, "")))
        html = html.Replace("{{steps}}", list.ToString())

        Return Normalize(html)
    End Function

    ''' <summary>
    ''' 生成框架内置的错误重试页面
    ''' </summary>
    ''' <param name="title">错误标题</param>
    ''' <param name="message">错误说明</param>
    ''' <param name="detail">详细的错误堆栈或者模型原始输出</param>
    ''' <returns></returns>
    Public Function ErrorPage(title As String, message As String, Optional detail As String = Nothing) As String
        Dim html As String = ReadResource("error.html")

        html = html.Replace("{{title}}", WebUtility.HtmlEncode(If(title, "出错了")))
        html = html.Replace("{{message}}", WebUtility.HtmlEncode(If(message, "")))
        html = html.Replace("{{detail}}", WebUtility.HtmlEncode(If(detail, "")))

        Return Normalize(html)
    End Function

End Module
