Option Strict On
Option Explicit On

Namespace ServicesSample
    Public Module GreeterConsumer
        Public Sub UseGreeter()
            Dim greeter As New Greeter()
            Dim message = greeter.Greet("World")
        End Sub
    End Module
End Namespace
