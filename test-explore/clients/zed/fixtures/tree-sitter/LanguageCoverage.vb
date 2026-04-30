Imports System

Namespace Fixtures
    Public Interface IGreeter
        Function Greet(name As String) As String
    End Interface

    Public Class Greeter
        Implements IGreeter

        Public Function Greet(name As String) As String Implements IGreeter.Greet
            If String.IsNullOrWhiteSpace(name) Then
                Return "Hello"
            End If

            Return $"Hello, {name}"
        End Function
    End Class
End Namespace
