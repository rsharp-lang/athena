Imports System.IO
Imports System.Runtime.CompilerServices
Imports System.Windows.Forms
Imports Ollama

Module Workbench

    ''' <summary>
    ''' GNU R 的 Rscript 可执行程序路径
    ''' </summary>
    Public Const DefaultRScript As String = "C:\Program Files\R\R-4.5.0\bin\Rscript.exe"

    ''' <summary>
    ''' 内置的演示 R 脚本相对于输出目录的路径
    ''' </summary>
    Public Const DemoRScriptRelative As String = "Demo\iris_analysis.R"

    ''' <summary>
    ''' 构造一个已经根据默认配置构造好参数的 LLM 客户端
    ''' </summary>
    ''' <returns></returns>
    <MethodImpl(MethodImplOptions.AggressiveInlining)>
    Public Function GetLLmClient() As LLMClient
        Return LLMConfig.LoadDefault.CreateLLm
    End Function

    Public Sub Setup()
        Call LLMConfig.LoadDefault()
    End Sub

    ''' <summary>
    ''' 获取内置的 Iris 演示 R 脚本的完整路径
    ''' </summary>
    Public Function GetDemoRScriptPath() As String
        Dim p As String = Path.Combine(Application.StartupPath, "Demo", "iris_analysis.R")

        If Not File.Exists(p) Then
            p = Path.Combine(Application.StartupPath, DemoRScriptRelative)
        End If

        Return p
    End Function

End Module
