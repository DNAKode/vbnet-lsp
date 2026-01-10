Namespace BasicSample
    Public Class Calculator
        Public Function Add(left As Integer, right As Integer) As Integer
            Return left + right
        End Function
    End Class

    Public Class UseCalculator
        Public Function Compute() As Integer
            Dim calc As New Calculator()
            Dim result As Integer = calc.Add(1, 2)
            Return result
        End Function
    End Class
End Namespace
