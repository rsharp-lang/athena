Public Class FormMain

    Private Sub FormMain_Load(sender As Object, e As EventArgs) Handles Me.Load
        Call Workbench.Setup()
        Call New FormRscript().ShowDialog()
    End Sub
End Class
