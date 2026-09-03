Imports System.IO
Imports System.Linq
Imports System.Text
Imports System.Text.Json
Imports GenerativeUI
Imports Ollama

''' <summary>
''' 生成式界面框架的演示窗口：
''' 用户通过文件对话框打开一个 GNU R 脚本 → 交由大语言模型归纳出可调的运行参数 →
''' 再由大语言模型为这些参数编写一个 html 操作界面 → 用户调整参数并点击执行 →
''' 宿主通过 GNU R 执行脚本 → 把结果图片与表格回传到动态生成的界面上。
''' </summary>
Public Class FormRscript

    Dim analyzerLlm As LLMClient
    Dim uiLlm As LLMClient

    Dim analyzer As RScriptAnalyzer
    Dim engine As GenerativeUIEngine
    Dim runner As RScriptRunner
    ''' <summary>
    ''' 把 LLM 的思考过程与输出内容实时推送到网页端的流式泵
    ''' </summary>
    Dim streamPump As LlmStreamPump

    ''' <summary>
    ''' 当前打开的 R 脚本文件路径
    ''' </summary>
    Dim scriptFile As String
    ''' <summary>
    ''' 从脚本之中分析出来的可调参数清单
    ''' </summary>
    Dim parameters As ParameterDescriptor() = {}
    ''' <summary>
    ''' 最近一次运行的结果输出目录
    ''' </summary>
    Dim lastOutDir As String
    ''' <summary>
    ''' 生成界面的流程是否正在运行，用于避免并发触发
    ''' </summary>
    Dim pipelineBusy As Boolean = False

    ''' <summary>
    ''' 诊断日志文件路径。WinExe 项目没有控制台，Console.WriteLine 看不到任何输出，
    ''' 因此把网页侧的宿主调用日志与引擎状态一并落盘，方便排查生成式界面的问题。
    ''' </summary>
    Public Shared ReadOnly LogFile As String =
        Path.Combine(Path.GetTempPath(), "athena_generative_ui.log")

    Private Shared Sub WriteLog(ParamArray lines As String())
        Try
            Dim text As String = String.Join(vbLf, lines.Select(Function(s) $"[{DateTime.Now:HH:mm:ss.fff}] {s}"))

            SyncLock LogFile
                File.AppendAllText(LogFile, text & vbLf, Encoding.UTF8)
            End SyncLock
        Catch ex As Exception
            ' 日志失败不能影响主流程
        End Try
    End Sub

    Private Sub FormRscript_Load(sender As Object, e As EventArgs) Handles MyBase.Load
        Text = "Athena 生成式分析工作台"

        ' 分析参数与编写界面使用两个互相独立的 LLM 客户端，各自持有互不干扰的会话记忆
        analyzerLlm = Workbench.GetLLmClient()
        uiLlm = Workbench.GetLLmClient()

        analyzer = New RScriptAnalyzer(analyzerLlm)
        runner = New RScriptRunner()
        engine = New GenerativeUIEngine(Webui1, uiLlm)

        ' 把两个客户端的输出都接到流式泵之上，网页端就能看见 AI 实时的工作过程
        streamPump = New LlmStreamPump(Webui1) With {.FlushIntervalMs = 100}
        Call streamPump.Attach(analyzerLlm, phase:="analyze", label:="① 归纳可调参数")
        Call streamPump.Attach(uiLlm, phase:="design", label:="② 编写操作界面")

        Call RegisterHostCommands()

        AddHandler engine.Status, AddressOf OnEngineStatus
        AddHandler Webui1.HostLog, AddressOf OnHostLog

        Call Webui1.SetUI(DemoPages.Launcher())
    End Sub

    Private Sub FormRscript_FormClosing(sender As Object, e As FormClosingEventArgs) Handles MyBase.FormClosing
        If Not analyzerLlm Is Nothing Then analyzerLlm.Dispose()
        If Not uiLlm Is Nothing Then uiLlm.Dispose()
    End Sub

    ''' <summary>
    ''' 向网页注册所有可以被生成式界面调用的宿主能力。
    ''' 这些命令的说明文本会被自动写入到大语言模型的系统提示词之中。
    ''' </summary>
    Private Sub RegisterHostCommands()
        Dim host As JavascriptInterop = Webui1.Host

        Call host.Commands.Register(
            name:="open_rscript",
            description:="弹出 Windows 文件对话框让用户选择一个 GNU R 脚本文件；选择完成之后宿主会自动分析脚本参数并生成新的操作界面。返回值是 {cancelled, path}。",
            schema:="{}",
            handler:=AddressOf OpenRScript)

        Call host.Commands.Register(
            name:="load_demo",
            description:="载入程序内置的 Iris 数据集演示 R 脚本，并立即生成对应的操作界面。返回值是 {cancelled, path}。",
            schema:="{}",
            handler:=AddressOf LoadDemoScript)

        Call host.Commands.Register(
            name:="browse_file",
            description:="弹出 Windows 文件选择对话框，返回用户所选择文件的完整路径字符串；用户取消时返回空字符串。",
            schema:="{""filter"":""数据文件|*.csv;*.txt;*.R|所有文件|*.*""}",
            handler:=AddressOf BrowseFile)

        Call host.Commands.Register(
            name:="browse_folder",
            description:="弹出 Windows 目录选择对话框，返回用户所选择目录的完整路径字符串；用户取消时返回空字符串。",
            schema:="{""path"":""C:/Temp""}",
            handler:=AddressOf BrowseFolder)

        Call host.Commands.Register(
            name:="run_script",
            description:="用 GNU R 执行当前打开的 R 脚本。payload 是界面上收集到的运行参数对象 {参数名: 参数值}，宿主会把它们以 key=value 的命令行形式传给 Rscript 并强制注入 out_dir。返回值是本次运行的完整结果：{success, exitCode, elapsed_ms, out_dir, command, stdout, stderr, images:[{name,title,dataUri}], tables:[{name,title,headers,rows}], texts:[{name,title,text}], error}。",
            schema:="{""k"":3,""palette"":""rainbow"",""out_dir"":""""}",
            handler:=AddressOf RunScript)

        Call host.Commands.Register(
            name:="get_context",
            description:="获取当前宿主的上下文信息：{script, parameters, rscript, model, out_dir}。界面初始化的时候可以用它来显示模型名与脚本路径。",
            schema:="{}",
            handler:=AddressOf GetContext)
    End Sub

    ' ---------------------------------------------------------- 宿主命令实现 ----

    ''' <summary>
    ''' 打开一个 R 脚本文件并启动生成式界面构建流程
    ''' </summary>
    Private Function OpenRScript(payload As String) As Task(Of String)
        Dim initial As String = If(
            String.IsNullOrEmpty(scriptFile),
            Workbench.DemoDirectory,
            Path.GetDirectoryName(scriptFile))

        Dim filePath As String = Browse("GNU R 脚本文件|*.R;*.r|R 代码文件|*.r;*.R|所有文件|*.*",
                                        isFolder:=False, initial:=initial)

        If String.IsNullOrEmpty(filePath) Then
            Return Task.FromResult(HostMessage.Success(New With {.cancelled = True, .path = ""}))
        End If

        Call StartPipeline(filePath)

        Return Task.FromResult(HostMessage.Success(New With {.cancelled = False, .path = filePath}))
    End Function

    ''' <summary>
    ''' 载入内置的 Iris 演示脚本
    ''' </summary>
    Private Function LoadDemoScript(payload As String) As Task(Of String)
        Dim filePath As String = Workbench.GetDemoRScriptPath()

        If Not File.Exists(filePath) Then
            Return Task.FromResult(HostMessage.Failure($"没有找到内置的演示脚本文件: {filePath}"))
        End If

        Call StartPipeline(filePath)

        Return Task.FromResult(HostMessage.Success(New With {.cancelled = False, .path = filePath}))
    End Function

    Private Function BrowseFile(payload As String) As Task(Of String)
        Dim filter As String = ReadField(payload, "filter")

        If String.IsNullOrWhiteSpace(filter) Then
            filter = "所有文件|*.*"
        End If

        Return Task.FromResult(HostMessage.Success(Browse(filter, isFolder:=False)))
    End Function

    Private Function BrowseFolder(payload As String) As Task(Of String)
        Dim initial As String = ReadField(payload, "path")

        Return Task.FromResult(HostMessage.Success(Browse(Nothing, isFolder:=True, initial:=initial)))
    End Function

    ''' <summary>
    ''' 执行当前打开的 R 脚本
    ''' </summary>
    Private Async Function RunScript(payload As String) As Task(Of String)
        If String.IsNullOrEmpty(scriptFile) OrElse Not File.Exists(scriptFile) Then
            Return HostMessage.Failure("还没有打开任何 R 脚本，请先打开一个 R 脚本文件。")
        End If

        Dim params As Dictionary(Of String, String)

        Try
            params = ParseParams(payload)
        Catch ex As Exception
            Return HostMessage.Failure($"无法解析运行参数: {ex.Message}")
        End Try

        Try
            Call engine.PushStatus("正在调用 GNU R 执行脚本，请稍候…", "info")

            Dim result As AnalysisResult = Await runner.Run(scriptFile, params, AddressOf OnRunLog)

            lastOutDir = result.out_dir

            If result.success Then
                Call engine.PushStatus(
                    $"分析完成：{result.images.Length} 张图片，{result.tables.Length} 张表格，耗时 {result.elapsed_ms} ms",
                    "success")
            Else
                Call engine.PushStatus(
                    $"脚本运行失败（退出码 {result.exitCode}）：{result.errorMessage}", "error")
            End If

            Return HostMessage.Success(result)
        Catch ex As Exception
            Call App.LogException(ex)
            Return HostMessage.Failure(ex.Message)
        End Try
    End Function

    ''' <summary>
    ''' 返回当前的宿主上下文信息
    ''' </summary>
    Private Function GetContext(payload As String) As Task(Of String)
        Dim ctx = New With {
            .script = If(scriptFile, ""),
            .parameters = parameters,
            .rscript = runner.RScriptPath,
            .rscript_exists = File.Exists(runner.RScriptPath),
            .model = If(uiLlm Is Nothing, "", uiLlm.Model),
            .out_dir = If(lastOutDir, ""),
            .log_file = LogFile
        }

        Return Task.FromResult(HostMessage.Success(ctx))
    End Function

    ' ------------------------------------------------------- 生成式界面流程 ----

    ''' <summary>
    ''' 串起「分析参数 → 生成界面」的完整流程
    ''' </summary>
    Private Async Sub StartPipeline(target As String)
        If pipelineBusy Then
            Call engine.PushStatus("上一次构建任务还没有结束，请稍候…", "warn")
            Return
        End If

        pipelineBusy = True
        scriptFile = target
        parameters = New ParameterDescriptor() {}

        Try
            ' 每一次构建都从全新的会话开始，避免上一次的脚本内容污染上下文
            Call analyzerLlm.Clear()
            Call uiLlm.Clear()

            Call engine.ShowLoading(
                title:="正在构建操作界面",
                message:=$"大语言模型正在分析脚本 {Path.GetFileName(target)}",
                steps:=New String() {
                    "读取脚本源代码",
                    "AI 归纳脚本之中可以调整的运行参数",
                    "AI 为这些参数编写 HTML 操作界面",
                    "渲染界面，等待用户调整参数并执行分析"
                })

            ' 等待加载页面渲染完成之后再推送状态，否则状态消息会丢失
            Await Task.Delay(1200)

            Call engine.PushStatus("正在读取脚本源代码…")
            Call engine.PushStatus($"正在让模型 {analyzerLlm.Model} 归纳可调参数…")

            Call streamPump.Begin("analyze", "① 归纳可调参数")

            parameters = Await analyzer.Analyze(target)

            If parameters.Length = 0 Then
                Call engine.PushStatus("没有从脚本之中归纳出任何可调参数，将使用一个通用的参数集", "warn")
                parameters = DefaultParameters()
            Else
                Call engine.PushStatus(
                    $"共归纳出 {parameters.Length} 个可调参数：{String.Join("、", parameters.Select(Function(p) p.name))}",
                    "success")
            End If

            Await Task.Delay(300)

            Call engine.PushStatus($"正在让模型 {uiLlm.Model} 编写参数操作界面…")

            Call streamPump.Begin("design", "② 编写操作界面")

            Dim ok As Boolean = Await engine.Render(BuildUIRequest(target), errorTitle:="生成式界面构建失败")

            If ok Then
                Call engine.PushStatus("操作界面已经生成完毕，请调整参数之后点击「执行分析」", "success")
            Else
                ' 模型没有产出可用的界面时，回退为框架内置的参数表单，保证 demo 依然可用
                Call engine.PushStatus("模型没有产出可用的界面，已回退为框架内置的参数表单", "warn")

                Await Task.Delay(200)

                Call Webui1.SetUI(HtmlPage.ParameterFormPage(DemoJson.Serialize(parameters)))
            End If
        Catch ex As Exception
            Call App.LogException(ex)
            Call WriteLog("生成式界面构建失败: " & ex.ToString())
            Call Webui1.SetUI(HtmlPage.ErrorPage("生成式界面构建失败", ex.Message, ex.ToString()))
        Finally
            pipelineBusy = False
        End Try
    End Sub

    ''' <summary>
    ''' 构造交给大语言模型的界面生成需求描述
    ''' </summary>
    Private Function BuildUIRequest(scriptPath As String) As String
        Dim sb As New StringBuilder
        Dim code As String = ""

        Try
            code = File.ReadAllText(scriptPath, Encoding.UTF8)
        Catch ex As Exception
            code = "# (脚本源代码读取失败: " & ex.Message & ")"
        End Try

        Dim lines As String() = code.Replace(vbCrLf, vbLf).Replace(vbCr, vbLf).Split(vbLf(0))

        If lines.Length > 220 Then
            code = String.Join(vbLf, lines.Take(220)) & vbLf & "# ... (脚本剩余部分被省略)"
        End If

        engine.StateParams = DemoJson.Serialize(parameters)

        Call sb.AppendLine("请为下面这个 GNU R 脚本编写一个「参数调整 + 运行 + 结果展示」的单文件 HTML 操作界面。")
        Call sb.AppendLine()
        Call sb.AppendLine($"脚本文件名: {Path.GetFileName(scriptPath)}")
        Call sb.AppendLine()
        Call sb.AppendLine("## 脚本源代码")
        Call sb.AppendLine("```r")
        Call sb.AppendLine(code)
        Call sb.AppendLine("```")
        Call sb.AppendLine()
        Call sb.AppendLine("## 已经归纳出来的可调参数清单（json 数组）")
        Call sb.AppendLine("你必须为下面这个数组之中的每一个参数都提供一个对应的输入控件，")
        Call sb.AppendLine("并且控件的 data-gu-param 属性值必须与该参数的 name 严格一致（区分大小写），")
        Call sb.AppendLine("否则宿主无法把用户填写的值传递给 R 脚本：")
        Call sb.AppendLine("```json")
        Call sb.AppendLine(DemoJson.Serialize(parameters))
        Call sb.AppendLine("```")
        Call sb.AppendLine()
        Call sb.AppendLine("## 界面的额外要求")
        Call sb.AppendLine("- 参数请按照清单之中的 group 字段分组放进不同的卡片之中。")
        Call sb.AppendLine("- 文件路径类的参数请在文本框旁边放一个「浏览」按钮，点击时调用 GenUI.browse('file'|'folder', filter) 并把返回的路径写回文本框。")
        Call sb.AppendLine("- 底部需要一个吸底的「执行分析」主按钮，点击之后调用 await GenUI.run() 并在 finally 之中恢复按钮状态。")
        Call sb.AppendLine("- 结果展示区请使用 id 为 result-images / result-tables / result-log 的三个容器：")
        Call sb.AppendLine("  图片用 GenUI.renderResult('result-images', data) 渲染，")
        Call sb.AppendLine("  表格用 GenUI.renderResult('result-tables', {images: [], tables: data.tables, texts: []}) 渲染，")
        Call sb.AppendLine("  日志区展示 data.command / data.stdout / data.stderr / 退出码 / 耗时。")
        Call sb.AppendLine("- 请至少划分出「聚类结果」「PCA 降维」「回归拟合」「结果表格」「运行日志」这几个结果区块或者 Tab。")
        Call sb.AppendLine("- 图片需要支持点击放大查看。")

        Return sb.ToString()
    End Function

    Private Shared Function DefaultParameters() As ParameterDescriptor()
        Return New ParameterDescriptor() {
            New ParameterDescriptor With {
                .name = "input",
                .label = "数据输入文件",
                .uiType = "file",
                .group = "数据输入输出",
                .defaultValue = "",
                .description = "留空则使用脚本内置的数据集"
            },
            New ParameterDescriptor With {
                .name = "out_dir",
                .label = "结果输出目录",
                .uiType = "folder",
                .group = "数据输入输出",
                .defaultValue = "",
                .description = "留空则由宿主自动创建一个临时目录"
            }
        }
    End Function

    ' ------------------------------------------------------------ 工具函数 ----

    ''' <summary>
    ''' 把网页上传过来的参数 json 对象解析为可以直接拼进命令行的字符串字典
    ''' </summary>
    Private Shared Function ParseParams(payload As String) As Dictionary(Of String, String)
        Dim result As New Dictionary(Of String, String)(StringComparer.OrdinalIgnoreCase)

        If String.IsNullOrWhiteSpace(payload) Then
            Return result
        End If

        Using doc As JsonDocument = JsonDocument.Parse(payload)
            For Each prop As JsonProperty In doc.RootElement.EnumerateObject()
                Dim value As String

                Select Case prop.Value.ValueKind
                    Case JsonValueKind.True
                        value = "TRUE"
                    Case JsonValueKind.False
                        value = "FALSE"
                    Case JsonValueKind.Null, JsonValueKind.Undefined
                        value = ""
                    Case JsonValueKind.Number
                        value = prop.Value.ToString()
                    Case Else
                        value = prop.Value.ToString()
                End Select

                If value.StartsWith("""") AndAlso value.EndsWith("""") AndAlso value.Length >= 2 Then
                    value = value.Substring(1, value.Length - 2)
                End If

                result(prop.Name) = value
            Next
        End Using

        Return result
    End Function

    Private Shared Function ReadField(payload As String, field As String) As String
        If String.IsNullOrWhiteSpace(payload) Then
            Return ""
        End If

        Try
            Using doc As JsonDocument = JsonDocument.Parse(payload)
                Dim value As JsonElement

                If doc.RootElement.TryGetProperty(field, value) Then
                    Return If(value.ValueKind = JsonValueKind.String, value.GetString(), value.ToString())
                End If
            End Using
        Catch ex As Exception
            Call Console.WriteLine(ex.Message)
        End Try

        Return ""
    End Function

    ''' <summary>
    ''' 在 UI 线程上弹出文件/目录选择对话框
    ''' </summary>
    Private Function Browse(filter As String, isFolder As Boolean, Optional initial As String = "") As String
        Return UiThread(Function() As String
                            If isFolder Then
                                Using dlg As New FolderBrowserDialog()
                                    dlg.Description = "请选择结果输出目录"

                                    If Not String.IsNullOrEmpty(initial) AndAlso Directory.Exists(initial) Then
                                        dlg.SelectedPath = initial
                                    ElseIf Directory.Exists(lastOutDir) Then
                                        dlg.SelectedPath = lastOutDir
                                    End If

                                    If dlg.ShowDialog(Me) = DialogResult.OK Then
                                        Return dlg.SelectedPath
                                    Else
                                        Return ""
                                    End If
                                End Using
                            Else
                                Using dlg As New OpenFileDialog()
                                    dlg.Filter = If(String.IsNullOrEmpty(filter), "所有文件|*.*", filter)
                                    dlg.Title = "请选择文件"
                                    dlg.CheckFileExists = False
                                    dlg.Multiselect = False

                                    If Not String.IsNullOrEmpty(initial) AndAlso Directory.Exists(initial) Then
                                        dlg.InitialDirectory = initial
                                    End If

                                    If dlg.ShowDialog(Me) = DialogResult.OK Then
                                        Return dlg.FileName
                                    Else
                                        Return ""
                                    End If
                                End Using
                            End If
                        End Function)
    End Function

    ''' <summary>
    ''' 把一段代码封送到 UI 线程之上执行
    ''' </summary>
    Private Function UiThread(Of T)(f As Func(Of T)) As T
        If IsDisposed Then
            Return Nothing
        End If

        If InvokeRequired Then
            Return CType(Invoke(f), T)
        Else
            Return f()
        End If
    End Function

    Private Sub OnEngineStatus(message As String, level As String)
        Call Console.WriteLine($"[{level}] {message}")
        Call WriteLog($"[{level}] {message}")
    End Sub

    Private Sub OnHostLog(message As String)
        Call Console.WriteLine(message)
        Call WriteLog(message)
    End Sub

    Private Sub OnRunLog(message As String)
        Call engine.PushStatus(message)
        Call WriteLog(message)
    End Sub

End Class
