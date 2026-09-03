Imports Microsoft.Web.WebView2.Core
Imports Ollama

Public Class WebUI

    Dim js As JavascriptInterop
    Dim llm As LLMClient

    Public Function SetLLM(llm As LLMClient) As WebUI
        Me.llm = llm
        Return Me
    End Function

    Private Async Sub WebUI_Load(sender As Object, e As EventArgs) Handles Me.Load
        js = New JavascriptInterop(host:=Me)
        Await WebViewLoader.Init(WebView21)
    End Sub

    Public Sub SetUI(html As String)
        Call WebViewLoader.NavigateToLargeString(WebView21, value:=html)
    End Sub

    Private Sub WebView21_CoreWebView2InitializationCompleted(sender As Object, e As CoreWebView2InitializationCompletedEventArgs) Handles WebView21.CoreWebView2InitializationCompleted
        Call WebView21.CoreWebView2.AddHostObjectToScript("host", js)
    End Sub

    Private Sub WebView21_NavigationCompleted(sender As Object, e As CoreWebView2NavigationCompletedEventArgs) Handles WebView21.NavigationCompleted

    End Sub
End Class
