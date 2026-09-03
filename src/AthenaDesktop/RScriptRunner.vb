Imports System.Diagnostics
Imports System.Text
Imports System.Text.Json.Serialization
Imports System.Text.RegularExpressions
Imports System.Threading.Tasks
Imports Microsoft.VisualBasic.FileIO
Imports SysIO = System.IO

''' <summary>
''' 一张图片形式的运行结果
''' </summary>
Public Class ResultImage

    Public Property name As String
    Public Property title As String
    ''' <summary>
    ''' data URI 形式的图片内容（data:image/png;base64,...），
    ''' 由于页面运行在随机虚拟主机之上，无法通过 file:// 直接加载本地图片。
    ''' </summary>
    ''' <returns></returns>
    Public Property dataUri As String
    Public Property bytes As Long

End Class

''' <summary>
''' 一张表格形式的运行结果
''' </summary>
Public Class ResultTable

    Public Property name As String
    Public Property title As String
    Public Property headers As String()
    Public Property rows As String()()
    ''' <summary>
    ''' 表格是否因为体积过大而被截断
    ''' </summary>
    ''' <returns></returns>
    Public Property truncated As Boolean
    Public Property totalRows As Integer

End Class

''' <summary>
''' 一段文本形式的运行结果
''' </summary>
Public Class ResultText

    Public Property name As String
    Public Property title As String
    Public Property text As String

End Class

''' <summary>
''' 一次 R 脚本运行的完整结果，这个对象会被直接序列化之后回传给网页
''' </summary>
Public Class AnalysisResult

    Public Property script As String
    Public Property out_dir As String
    Public Property command As String
    Public Property exitCode As Integer
    Public Property success As Boolean
    Public Property timedOut As Boolean
    Public Property elapsed_ms As Long
    Public Property stdout As String
    Public Property stderr As String
    Public Property images As ResultImage()
    Public Property tables As ResultTable()
    Public Property texts As ResultText()

    ''' <summary>
    ''' 运行失败时的错误信息
    ''' </summary>
    ''' <returns></returns>
    <JsonPropertyName("error")>
    Public Property errorMessage As String

End Class

''' <summary>
''' GNU R 脚本执行器：把界面上收集到的参数以 key=value 的命令行形式传递给 Rscript，
''' 然后扫描脚本的输出目录，把图片、表格与文本结果收集起来回传给网页。
''' </summary>
Public Class RScriptRunner

    ReadOnly rscript As String

    ''' <summary>
    ''' 单次运行的最长时间（分钟）
    ''' </summary>
    ''' <returns></returns>
    Public Property TimeoutMinutes As Integer = 10
    ''' <summary>
    ''' 单次运行最多回传多少张图片
    ''' </summary>
    ''' <returns></returns>
    Public Property MaxImageCount As Integer = 12
    ''' <summary>
    ''' 单张图片的最大字节数，超过这个大小的图片会被跳过
    ''' </summary>
    ''' <returns></returns>
    Public Property MaxImageBytes As Long = 8 * 1024 * 1024
    ''' <summary>
    ''' 单张表格最多回传多少行
    ''' </summary>
    ''' <returns></returns>
    Public Property MaxTableRows As Integer = 500
    ''' <summary>
    ''' 单张表格最多回传多少列
    ''' </summary>
    ''' <returns></returns>
    Public Property MaxTableCols As Integer = 60
    ''' <summary>
    ''' 单段文本结果的最大字符数
    ''' </summary>
    ''' <returns></returns>
    Public Property MaxTextChars As Integer = 20000

    ''' <summary>
    ''' 没有指定输出目录时，运行结果文件的默认存放根目录
    ''' </summary>
    ''' <returns></returns>
    Public Property WorkspaceRoot As String =
        SysIO.Path.Combine(SysIO.Path.GetTempPath(), "athena_rscript_runs")

    Sub New(Optional rscript As String = Nothing)
        If String.IsNullOrWhiteSpace(rscript) Then
            Me.rscript = Workbench.DefaultRScript
        Else
            Me.rscript = rscript
        End If
    End Sub

    ''' <summary>
    ''' 当前所使用的 Rscript 可执行文件路径
    ''' </summary>
    ''' <returns></returns>
    Public ReadOnly Property RScriptPath As String
        Get
            Return rscript
        End Get
    End Property

    ''' <summary>
    ''' 执行目标 R 脚本
    ''' </summary>
    ''' <param name="scriptFile">目标 R 脚本的文件路径</param>
    ''' <param name="params">界面上收集到的运行参数</param>
    ''' <param name="onLog">可选的运行状态回调</param>
    ''' <returns></returns>
    Public Async Function Run(scriptFile As String,
                              params As Dictionary(Of String, String),
                              Optional onLog As Action(Of String) = Nothing) As Task(Of AnalysisResult)

        Dim result As New AnalysisResult With {
            .script = scriptFile,
            .exitCode = -1,
            .success = False,
            .images = New ResultImage() {},
            .tables = New ResultTable() {},
            .texts = New ResultText() {}
        }

        If String.IsNullOrWhiteSpace(scriptFile) OrElse Not SysIO.File.Exists(scriptFile) Then
            result.errorMessage = $"目标 R 脚本文件不存在: {scriptFile}"
            Return result
        End If
        If Not SysIO.File.Exists(rscript) Then
            result.errorMessage = $"没有找到 GNU R 的 Rscript 可执行程序: {rscript}"
            Return result
        End If

        Dim outDir As String = ResolveOutDir(params)
        Dim cli As String = BuildCommandLine(scriptFile, params, outDir)

        result.out_dir = outDir
        result.command = $"""{rscript}"" {cli}"

        Try
            SysIO.Directory.CreateDirectory(outDir)
        Catch ex As Exception
            result.errorMessage = $"无法创建结果输出目录 '{outDir}': {ex.Message}"
            Return result
        End Try

        If Not onLog Is Nothing Then
            Call onLog($"正在启动 GNU R: {SysIO.Path.GetFileName(scriptFile)}")
        End If

        Dim psi As New ProcessStartInfo(rscript) With {
            .UseShellExecute = False,
            .CreateNoWindow = True,
            .RedirectStandardOutput = True,
            .RedirectStandardError = True,
            .RedirectStandardInput = False,
            .WorkingDirectory = SysIO.Path.GetDirectoryName(scriptFile),
            .StandardOutputEncoding = Encoding.UTF8,
            .StandardErrorEncoding = Encoding.UTF8
        }

        psi.ArgumentList.Add(scriptFile)

        For Each key As String In params.Keys
            psi.ArgumentList.Add($"{key}={Sanitize(params(key))}")
        Next

        ' 结果输出目录始终由宿主强制注入，保证结果文件一定可以被回收
        psi.ArgumentList.Add($"out_dir={outDir}")

        Dim stdout As New StringBuilder
        Dim stderr As New StringBuilder
        Dim sw As Stopwatch = Stopwatch.StartNew()
        Dim exitCode As Integer = -1
        Dim timedOut As Boolean = False

        Using p As New Process()
            p.StartInfo = psi

            AddHandler p.OutputDataReceived, Sub(sender, e)
                                                 If Not e.Data Is Nothing Then
                                                     stdout.AppendLine(e.Data)
                                                 End If
                                             End Sub
            AddHandler p.ErrorDataReceived, Sub(sender, e)
                                                If Not e.Data Is Nothing Then
                                                    stderr.AppendLine(e.Data)
                                                End If
                                            End Sub

            p.Start()
            p.BeginOutputReadLine()
            p.BeginErrorReadLine()

            Dim exitTask As Task = p.WaitForExitAsync()
            Dim delay As Task = Task.Delay(TimeSpan.FromMinutes(Math.Max(1, TimeoutMinutes)))

            If Await Task.WhenAny(exitTask, delay) Is delay Then
                timedOut = True

                Try
                    Call p.Kill(entireProcessTree:=True)
                Catch ex As Exception
                    Call Console.WriteLine(ex.Message)
                End Try
            Else
                Await exitTask
            End If

            exitCode = p.ExitCode
        End Using

        sw.Stop()

        result.exitCode = exitCode
        result.timedOut = timedOut
        result.elapsed_ms = sw.ElapsedMilliseconds
        result.stdout = TrimOutput(stdout.ToString(), MaxTextChars)
        result.stderr = TrimOutput(stderr.ToString(), MaxTextChars)
        result.success = (exitCode = 0) AndAlso Not timedOut

        If timedOut Then
            result.errorMessage = $"脚本运行超时（超过 {TimeoutMinutes} 分钟），已经被强制终止。"
        ElseIf exitCode <> 0 Then
            result.errorMessage = $"GNU R 以非零退出码 {exitCode} 结束运行。"
        End If

        If Not onLog Is Nothing Then
            Call onLog($"GNU R 运行结束，退出码 {exitCode}，耗时 {result.elapsed_ms} ms")
        End If

        Call CollectResults(outDir, result)

        Return result
    End Function

    Private Function ResolveOutDir(params As Dictionary(Of String, String)) As String
        Dim dir As String = Nothing

        If Not params Is Nothing AndAlso params.ContainsKey("out_dir") Then
            dir = params("out_dir")
        End If

        If String.IsNullOrWhiteSpace(dir) Then
            dir = SysIO.Path.Combine(
                WorkspaceRoot,
                DateTime.Now.ToString("yyyyMMdd_HHmmss") & "_" & Guid.NewGuid().ToString("N").Substring(0, 8))
        End If

        Return dir.Trim().Trim(""""c)
    End Function

    Private Function BuildCommandLine(scriptFile As String, params As Dictionary(Of String, String), outDir As String) As String
        Dim sb As New StringBuilder

        Call sb.Append($"""{scriptFile}""")

        If Not params Is Nothing Then
            For Each key As String In params.Keys
                Call sb.Append($" {key}=""{Sanitize(params(key))}""")
            Next
        End If

        Call sb.Append($" out_dir=""{outDir}""")

        Return sb.ToString()
    End Function

    ''' <summary>
    ''' 去掉参数值之中会破坏命令行解析的字符
    ''' </summary>
    Private Shared Function Sanitize(value As String) As String
        If String.IsNullOrEmpty(value) Then
            Return ""
        End If

        Return value.Replace("""", "").Replace(vbCr, " ").Replace(vbLf, " ").Trim()
    End Function

    Private Shared Function TrimOutput(text As String, maxChars As Integer) As String
        If String.IsNullOrEmpty(text) Then
            Return ""
        ElseIf text.Length <= maxChars Then
            Return text
        Else
            Return text.Substring(0, maxChars) & vbCrLf & "... (输出内容过长，已截断)"
        End If
    End Function

    ''' <summary>
    ''' 扫描结果输出目录，把其中的结果文件分类收集为图片、表格与文本
    ''' </summary>
    Private Sub CollectResults(outDir As String, result As AnalysisResult)
        Dim files As String()

        Try
            files = SysIO.Directory.GetFiles(outDir)
        Catch ex As Exception
            result.errorMessage = If(result.errorMessage, "") & $" 无法读取结果目录: {ex.Message}"
            Return
        End Try

        Dim images As New List(Of ResultImage)
        Dim tables As New List(Of ResultTable)
        Dim texts As New List(Of ResultText)

        Array.Sort(files, StringComparer.OrdinalIgnoreCase)

        For Each filePath As String In files
            Dim ext As String = SysIO.Path.GetExtension(filePath).ToLower()
            Dim name As String = SysIO.Path.GetFileName(filePath)
            Dim title As String = Prettify(SysIO.Path.GetFileNameWithoutExtension(filePath))

            Select Case ext
                Case ".png", ".jpg", ".jpeg", ".bmp", ".gif", ".webp"
                    Dim img As ResultImage = ReadImage(filePath, ext, name, title)

                    If Not img Is Nothing AndAlso images.Count < MaxImageCount Then
                        Call images.Add(img)
                    End If
                Case ".svg"
                    Dim svg As String = TryReadText(filePath)

                    If Not svg Is Nothing Then
                        Call images.Add(New ResultImage With {
                            .name = name,
                            .title = title,
                            .bytes = svg.Length,
                            .dataUri = "data:image/svg+xml;base64," & Convert.ToBase64String(Encoding.UTF8.GetBytes(svg))
                        })
                    End If
                Case ".csv"
                    Call tables.Add(ReadTable(filePath, name, title, ","c))
                Case ".tsv"
                    Call tables.Add(ReadTable(filePath, name, title, vbTab(0)))
                Case ".txt", ".log", ".out", ".md", ".json"
                    Dim text As String = TryReadText(filePath)

                    If Not text Is Nothing Then
                        Call texts.Add(New ResultText With {
                            .name = name,
                            .title = title,
                            .text = TrimOutput(text, MaxTextChars)
                        })
                    End If
            End Select
        Next

        result.images = images.ToArray()
        result.tables = tables.ToArray()
        result.texts = texts.ToArray()
    End Sub

    Private Function ReadImage(filePath As String, ext As String, name As String, title As String) As ResultImage
        Try
            Dim info As New SysIO.FileInfo(filePath)

            If info.Length <= 0 Then
                Return Nothing
            End If
            If info.Length > MaxImageBytes Then
                Return Nothing
            End If

            Dim mime As String

            Select Case ext
                Case ".jpg", ".jpeg" : mime = "image/jpeg"
                Case ".bmp" : mime = "image/bmp"
                Case ".gif" : mime = "image/gif"
                Case ".webp" : mime = "image/webp"
                Case Else : mime = "image/png"
            End Select

            Return New ResultImage With {
                .name = name,
                .title = title,
                .bytes = info.Length,
                .dataUri = $"data:{mime};base64," & Convert.ToBase64String(SysIO.File.ReadAllBytes(filePath))
            }
        Catch ex As Exception
            Call App.LogException(ex)
            Return Nothing
        End Try
    End Function

    Private Function ReadTable(filePath As String, name As String, title As String, delimiter As Char) As ResultTable
        Dim table As New ResultTable With {
            .name = name,
            .title = title,
            .headers = New String() {},
            .rows = New String()() {},
            .truncated = False,
            .totalRows = 0
        }

        Dim rows As New List(Of String())

        Try
            Using parser As New TextFieldParser(filePath)
                parser.TextFieldType = FieldType.Delimited
                parser.SetDelimiters(delimiter)
                parser.HasFieldsEnclosedInQuotes = True
                parser.TrimWhiteSpace = False

                Do While Not parser.EndOfData
                    Dim fields As String() = parser.ReadFields()

                    If fields Is Nothing Then
                        Continue Do
                    End If
                    If fields.Length > MaxTableCols Then
                        fields = fields.Take(MaxTableCols).ToArray()
                    End If

                    Call rows.Add(fields)
                Loop
            End Using
        Catch ex As Exception
            Call App.LogException(ex)
        End Try

        If rows.Count > 0 Then
            table.headers = rows(0)
            rows.RemoveAt(0)
        End If

        table.totalRows = rows.Count

        If rows.Count > MaxTableRows Then
            rows = rows.Take(MaxTableRows).ToList()
            table.truncated = True
        End If

        table.rows = rows.ToArray()

        Return table
    End Function

    Private Shared Function TryReadText(filePath As String) As String
        Try
            Return SysIO.File.ReadAllText(filePath, Encoding.UTF8)
        Catch ex As Exception
            Return Nothing
        End Try
    End Function

    ''' <summary>
    ''' 把结果文件名转换为适合展示的标题：去掉排序前缀、下划线替换为空格
    ''' </summary>
    Public Shared Function Prettify(name As String) As String
        If String.IsNullOrWhiteSpace(name) Then
            Return name
        End If

        Dim text As String = Regex.Replace(name, "^\d+[_\-\s]*", "")
        text = text.Replace("_"c, " "c).Replace("-"c, " "c).Trim()

        If text.Length = 0 Then
            Return name
        End If

        Return text
    End Function

End Class
