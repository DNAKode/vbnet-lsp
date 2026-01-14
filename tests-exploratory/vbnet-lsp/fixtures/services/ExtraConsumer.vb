Option Strict On
Option Explicit On

Namespace ServicesSample
    Public Module ExtraConsumer
        Public Sub UseExtra()
            Dim extra = New ExtraType("Other")
            Dim title = extra.Title
        End Sub
    End Module
End Namespace
