''' <summary>
''' A helper class for testing language features.
''' </summary>
Public Class Helper

    Private _counter As Integer = 0

    ''' <summary>
    ''' Gets the current counter value.
    ''' </summary>
    Public ReadOnly Property Counter As Integer
        Get
            Return _counter
        End Get
    End Property

    ''' <summary>
    ''' Performs some work and increments the counter.
    ''' </summary>
    Public Sub DoWork()
        _counter += 1
        Console.WriteLine($"Work done. Counter: {_counter}")
    End Sub

    ''' <summary>
    ''' Adds two numbers together.
    ''' </summary>
    ''' <param name="a">First number.</param>
    ''' <param name="b">Second number.</param>
    ''' <returns>The sum of a and b.</returns>
    Public Function Add(a As Integer, b As Integer) As Integer
        Return a + b
    End Function

    ''' <summary>
    ''' Calculates the factorial of a number.
    ''' </summary>
    ''' <param name="n">The number to calculate factorial for.</param>
    ''' <returns>The factorial of n.</returns>
    Public Function Factorial(n As Integer) As Long
        If n <= 1 Then
            Return 1
        End If
        Return n * Factorial(n - 1)
    End Function

End Class
