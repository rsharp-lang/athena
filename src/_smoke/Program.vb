Imports System.IO
Imports System.Text
Imports GenerativeUI

Module Program

    Sub Main()
        Dim out As String = Path.Combine(AppContext.BaseDirectory, "pages")

        Directory.CreateDirectory(out)

        ' 1. 框架内置页面的规整结果
        File.WriteAllText(Path.Combine(out, "loading.html"), HtmlPage.LoadingPage("正在构建操作界面", "模型正在分析脚本", {"读取脚本", "归纳参数", "编写界面", "渲染界面"}), New UTF8Encoding(False))
        File.WriteAllText(Path.Combine(out, "error.html"), HtmlPage.ErrorPage("生成式界面构建失败", "模型没有返回可用的 HTML", "detail text"), New UTF8Encoding(False))

        Dim params As String = File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "sample_params.json"))

        File.WriteAllText(Path.Combine(out, "form.html"), HtmlPage.ParameterFormPage(params), New UTF8Encoding(False))

        ' 2. demo 的启动引导页
        Dim launcher As String = File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "launcher.html"))

        File.WriteAllText(Path.Combine(out, "launcher.html"), HtmlPage.Normalize(launcher), New UTF8Encoding(False))

        ' 3. 模拟一段大语言模型输出，验证 html 抽取逻辑
        Dim llm As String = File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "llm_output.txt"))
        Dim html As String = HtmlPage.ExtractHtml(llm)

        Console.WriteLine($"extract html length = {html.Length}, is document = {HtmlPage.IsHtmlDocument(html)}")

        File.WriteAllText(Path.Combine(out, "generated.html"), HtmlPage.Normalize(html, "{""params"": " & params & "}"), New UTF8Encoding(False))

        Console.WriteLine("pages written to: " & out)
    End Sub

End Module
