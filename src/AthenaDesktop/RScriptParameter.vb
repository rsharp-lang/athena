Imports System.Text
Imports System.Text.Json.Serialization
Imports Ollama

''' <summary>
''' 大语言模型从 R 脚本之中分析出来的一个可调整的运行参数
''' </summary>
Public Class ParameterDescriptor

    ''' <summary>
    ''' 参数名：必须与 R 脚本之中从命令行读取该参数时所使用的键名完全一致
    ''' </summary>
    ''' <returns></returns>
    Public Property name As String
    ''' <summary>
    ''' 界面上显示的中文名称
    ''' </summary>
    ''' <returns></returns>
    Public Property label As String
    ''' <summary>
    ''' 控件类型：number / integer / text / textarea / select / color / file / folder / bool
    ''' </summary>
    ''' <returns></returns>
    <JsonPropertyName("type")>
    Public Property uiType As String
    ''' <summary>
    ''' 参数分组名称，界面上会按照分组放进不同的卡片之中
    ''' </summary>
    ''' <returns></returns>
    Public Property group As String
    ''' <summary>
    ''' 参数默认值
    ''' </summary>
    ''' <returns></returns>
    <JsonPropertyName("default")>
    Public Property defaultValue As String
    ''' <summary>
    ''' 数值型参数的最小值
    ''' </summary>
    ''' <returns></returns>
    Public Property min As String
    ''' <summary>
    ''' 数值型参数的最大值
    ''' </summary>
    ''' <returns></returns>
    Public Property max As String
    ''' <summary>
    ''' 数值型参数的步进值
    ''' </summary>
    ''' <returns></returns>
    <JsonPropertyName("step")>
    Public Property stepValue As String
    ''' <summary>
    ''' 枚举型参数的可选值列表
    ''' </summary>
    ''' <returns></returns>
    Public Property options As String()
    ''' <summary>
    ''' 这个参数的作用说明，会显示在界面上作为提示信息
    ''' </summary>
    ''' <returns></returns>
    Public Property description As String

    Public Overrides Function ToString() As String
        Return $"[{uiType}] {name} = {defaultValue}"
    End Function

End Class

''' <summary>
''' R 脚本可调参数分析器：把脚本原文交给大语言模型，由模型归纳出
''' 所有可以在图形界面上调整的运行参数。
''' </summary>
Public Class RScriptAnalyzer

    ReadOnly llm As LLMClient

    ''' <summary>
    ''' 最近一次大语言模型返回的原始输出，用于错误诊断
    ''' </summary>
    ''' <returns></returns>
    Public Property LastOutput As String

    Sub New(llm As LLMClient)
        Me.llm = llm
        Me.llm.system_message = SystemPrompt
    End Sub

    ''' <summary>
    ''' 交给大语言模型的参数分析系统提示词
    ''' </summary>
    ''' <returns></returns>
    Public Shared ReadOnly Property SystemPrompt As String
        Get
            Dim sb As New StringBuilder

            Call sb.AppendLine("你是一个 R 语言脚本分析专家，负责从 GNU R 脚本之中归纳出所有可以被终端用户在图形界面上调整的运行参数。")
            Call sb.AppendLine()
            Call sb.AppendLine("## 脚本的运行参数约定")
            Call sb.AppendLine("- 宿主程序会通过命令行以 `key=value` 的形式把参数传递给脚本，例如 `Rscript demo.R k=3 palette=rainbow`。")
            Call sb.AppendLine("- 脚本内部通过 `commandArgs(trailingOnly = TRUE)` 读取这些 `key=value` 参数。")
            Call sb.AppendLine("- 因此，参数的 name 必须与脚本之中读取该参数时所使用的键名**完全一致**（区分大小写）。")
            Call sb.AppendLine()
            Call sb.AppendLine("## 需要找出来的参数类型")
            Call sb.AppendLine("- 数据文件输入路径（type=file）、结果输出目录（type=folder）")
            Call sb.AppendLine("- 颜色调色板选择（type=select，options 列出 R 的调色板函数名）、颜色选择（type=color，默认值为 #RRGGBB）")
            Call sb.AppendLine("- 数值设置（type=number 或 integer，同时给出 min / max / step）")
            Call sb.AppendLine("- 词条设置（type=text 或 textarea，例如物种名列表、分组标签）")
            Call sb.AppendLine("- 开关设置（type=bool，例如是否标准化数据）")
            Call sb.AppendLine("- 枚举选择（type=select，同时给出 options 数组）")
            Call sb.AppendLine()
            Call sb.AppendLine("## 输出格式（强制）")
            Call sb.AppendLine("只输出一个 ```json 代码块，代码块内是一个 json 数组，数组的每一个元素描述一个参数：")
            Call sb.AppendLine("[")
            Call sb.AppendLine("  {""name"":""k"",""label"":""聚类数量"",""type"":""integer"",""group"":""聚类分析"",""default"":3,""min"":2,""max"":10,""step"":1,""options"":[],""description"":""kmeans 聚类算法所使用的簇数量""},")
            Call sb.AppendLine("  {""name"":""palette"",""label"":""配色方案"",""type"":""select"",""group"":""可视化样式"",""default"":""rainbow"",""options"":[""rainbow"",""heat.colors""],""description"":""绘图所使用的调色板""}")
            Call sb.AppendLine("]")
            Call sb.AppendLine()
            Call sb.AppendLine("## 约束")
            Call sb.AppendLine("- 不要输出任何解释文字，代码块之外不要写任何内容。")
            Call sb.AppendLine("- 最多输出 20 个参数，优先挑选对分析结果影响最大的参数。")
            Call sb.AppendLine("- 如果脚本之中没有任何可调整的参数，就输出一个空数组 `[]`。")
            Call sb.AppendLine("- type 只能取：number、integer、text、textarea、select、color、file、folder、bool 之一。")
            Call sb.AppendLine("- label 与 description 一律使用简体中文。")

            Return sb.ToString()
        End Get
    End Property

    ''' <summary>
    ''' 分析目标 R 脚本之中所有可以被调整的运行参数
    ''' </summary>
    ''' <param name="scriptFile">目标 R 脚本的文件路径</param>
    ''' <returns>参数描述数组；分析失败时返回空数组</returns>
    Public Async Function Analyze(scriptFile As String) As Task(Of ParameterDescriptor())
        If Not IO.File.Exists(scriptFile) Then
            Throw New IO.FileNotFoundException($"目标 R 脚本文件不存在: {scriptFile}", scriptFile)
        End If

        Dim code As String = IO.File.ReadAllText(scriptFile, Encoding.UTF8)
        Dim prompt As String = BuildPrompt(scriptFile, code)
        Dim response As LLMsResponse = Await llm.Chat(prompt)

        LastOutput = If(response Is Nothing, "", response.output)

        Dim json As String = LlmJsonExtractor.ExtractJsonFromLlmResponse(LastOutput)

        If String.IsNullOrWhiteSpace(json) Then
            Return New ParameterDescriptor() {}
        End If

        Dim list As ParameterDescriptor()

        Try
            list = DemoJson.Deserialize(Of ParameterDescriptor())(json)
        Catch ex As Exception
            Call App.LogException(ex)
            Return New ParameterDescriptor() {}
        End Try

        Return Normalize(list)
    End Function

    Private Shared Function BuildPrompt(scriptFile As String, code As String) As String
        Dim sb As New StringBuilder
        Dim lines As String() = code.LineTokens

        If lines.Length > 600 Then
            code = lines.Take(600).JoinBy(vbCrLf) & vbCrLf & "# ... (脚本剩余部分被省略)"
        End If

        Call sb.AppendLine("请分析下面这个 GNU R 脚本，归纳出所有可以被终端用户在图形界面上调整的运行参数，然后按照系统提示词之中约定的 json 格式输出。")
        Call sb.AppendLine()
        Call sb.AppendLine($"脚本文件名: {IO.Path.GetFileName(scriptFile)}")
        Call sb.AppendLine($"脚本总行数: {lines.Length}")
        Call sb.AppendLine()
        Call sb.AppendLine("----- 脚本源代码开始 -----")
        Call sb.AppendLine(code)
        Call sb.AppendLine("----- 脚本源代码结束 -----")

        Return sb.ToString()
    End Function

    ''' <summary>
    ''' 清洗大语言模型给出的参数描述：补齐缺失字段、剔除非法条目
    ''' </summary>
    Private Shared Function Normalize(list As ParameterDescriptor()) As ParameterDescriptor()
        If list Is Nothing Then
            Return New ParameterDescriptor() {}
        End If

        Dim result As New List(Of ParameterDescriptor)
        Dim exists As New HashSet(Of String)(StringComparer.OrdinalIgnoreCase)

        For Each p As ParameterDescriptor In list
            If p Is Nothing OrElse String.IsNullOrWhiteSpace(p.name) Then
                Continue For
            End If

            p.name = p.name.Trim()

            ' 参数名只允许出现字母、数字与下划线，避免污染命令行
            If Not System.Text.RegularExpressions.Regex.IsMatch(p.name, "^[A-Za-z][A-Za-z0-9_]*$") Then
                Continue For
            End If
            If Not exists.Add(p.name) Then
                Continue For
            End If

            p.uiType = If(p.uiType, "").Trim().ToLower()

            If String.IsNullOrEmpty(p.uiType) Then
                p.uiType = "text"
            End If
            If String.IsNullOrWhiteSpace(p.label) Then
                p.label = p.name
            End If
            If String.IsNullOrWhiteSpace(p.group) Then
                p.group = "运行参数"
            End If
            If p.options Is Nothing Then
                p.options = New String() {}
            End If

            p.defaultValue = If(p.defaultValue, "")

            If (p.uiType = "select") AndAlso p.options.Length = 0 Then
                p.uiType = "text"
            End If

            Call result.Add(p)
        Next

        Return result.ToArray()
    End Function

End Class
