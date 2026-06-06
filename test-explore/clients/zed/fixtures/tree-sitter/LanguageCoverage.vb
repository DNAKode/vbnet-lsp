Imports System

Namespace Fixtures
    Public Class Greeter
        Public Function Greet(name As String) As String
            If String.IsNullOrWhiteSpace(name) Then
                Return "Hello"
            End If

            Return $"Hello, {name}"
        End Function
    End Class
End Namespace
