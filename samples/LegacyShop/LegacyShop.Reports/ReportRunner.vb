Imports System.Threading
Imports Microsoft.Win32

Public Class ReportRunner
    ' Thread.CurrentThread.Abort() in a comment must not count
    REM BinaryFormatter in REM comment must not count
    Private Const Note As String = "HttpContext.Current ""quoted"" in string"

    Public Sub Run()
        Dim key = Registry.CurrentUser.OpenSubKey("Software\Shop")
        Thread.CurrentThread.Abort()
    End Sub
End Class
