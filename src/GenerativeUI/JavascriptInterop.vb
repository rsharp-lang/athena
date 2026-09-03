

Imports System.Runtime.InteropServices

<ClassInterface(ClassInterfaceType.AutoDual)>
<ComVisible(True)>
Public Class JavascriptInterop

    Dim host As WebUI

    Sub New(host As WebUI)
        Me.host = host
    End Sub

End Class
