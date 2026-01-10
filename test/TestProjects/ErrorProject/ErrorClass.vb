Option Strict On
' This file contains intentional errors for testing diagnostics

Public Class ErrorClass

    ' BC30512: Option Strict On disallows implicit conversions from 'String' to 'Integer'
    Public Sub TypeMismatchError()
        Dim x As Integer = "hello"
    End Sub

    ' BC30451: 'undefinedVariable' is not declared
    Public Sub UndefinedVariableError()
        Console.WriteLine(undefinedVariable)
    End Sub

    ' BC30002: Type 'NonExistentType' is not defined
    Public Sub UndefinedTypeError()
        Dim obj As NonExistentType
    End Sub

    ' This method is correct - no errors
    Public Function ValidMethod(value As Integer) As Integer
        Return value * 2
    End Function

End Class
