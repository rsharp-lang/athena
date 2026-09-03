Imports System.Runtime.CompilerServices
Imports Ollama

Module Workbench

    Public Const DefaultRScript As String = "C:\Program Files\R\R-4.5.0\bin\Rscript.exe"

    <MethodImpl(MethodImplOptions.AggressiveInlining)>
    Public Function GetLLmClient() As LLMClient
        Return LLMConfig.LoadDefault.CreateLLm
    End Function

    Public Sub Setup()
        Call LLMConfig.LoadDefault()
    End Sub

End Module
