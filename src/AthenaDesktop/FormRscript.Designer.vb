<Global.Microsoft.VisualBasic.CompilerServices.DesignerGenerated()> _
Partial Class FormRscript
    Inherits System.Windows.Forms.Form

    'Form overrides dispose to clean up the component list.
    <System.Diagnostics.DebuggerNonUserCode()> _
    Protected Overrides Sub Dispose(ByVal disposing As Boolean)
        Try
            If disposing AndAlso components IsNot Nothing Then
                components.Dispose()
            End If
        Finally
            MyBase.Dispose(disposing)
        End Try
    End Sub

    'Required by the Windows Form Designer
    Private components As System.ComponentModel.IContainer

    'NOTE: The following procedure is required by the Windows Form Designer
    'It can be modified using the Windows Form Designer.  
    'Do not modify it using the code editor.
    <System.Diagnostics.DebuggerStepThrough()> _
    Private Sub InitializeComponent()
        Webui1 = New GenerativeUI.WebUI()
        SuspendLayout()
        ' 
        ' Webui1
        ' 
        Webui1.Dock = DockStyle.Fill
        Webui1.Location = New Point(0, 0)
        Webui1.Name = "Webui1"
        Webui1.Size = New Size(1167, 700)
        Webui1.TabIndex = 0
        ' 
        ' FormRscript
        ' 
        AutoScaleDimensions = New SizeF(7F, 15F)
        AutoScaleMode = AutoScaleMode.Font
        ClientSize = New Size(1167, 700)
        Controls.Add(Webui1)
        Name = "FormRscript"
        Text = "Form1"
        ResumeLayout(False)
    End Sub

    Friend WithEvents Webui1 As GenerativeUI.WebUI
End Class
