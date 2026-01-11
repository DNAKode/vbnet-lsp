Option Strict On
Option Explicit On

Namespace ServicesSample
    Public Interface IGreeter
        Function Greet(name As String) As String
    End Interface

    Public Class Greeter
        Implements IGreeter

        Public Function Greet(name As String) As String Implements IGreeter.Greet
            Return $"Hello, {name}"
        End Function
    End Class

    Public Class Calculator
        Public Function Add(a As Integer, b As Integer) As Integer
            Return a + b
        End Function
    End Class

    Public Module Extensions
        <System.Runtime.CompilerServices.Extension>
        Public Function DoubleIt(value As Integer) As Integer
            Return value * 2
        End Function
    End Module

    Public Class Program
        Public Shared Sub Main()
            Dim calc As New Calculator()
            Dim sum = calc.Add(1, 2) ' MARKER: definition_add
            Dim sum2 = calc.Add(3, 4) ' MARKER: completion_calc
            Dim text As String = sum.ToString() ' MARKER: hover_text
            Dim length = text.Length ' MARKER: completion_text

            Dim greeter As IGreeter = New Greeter() ' MARKER: rename_greeter
            Dim message = greeter.Greet("VB") ' MARKER: references_greet
            Dim greeterType = New Greeter() ' MARKER: definition_greeter
            Dim greeterAgain = New Greeter() ' MARKER: references_greeter_class

            Dim extra = New ExtraType("Sample") ' MARKER: hover_extratype
            Dim title = extra.Title ' MARKER: references_title

            Dim doubled = sum.DoubleIt() ' MARKER: completion_extension
        End Sub
    End Class

    Public Class ExtraType
        Public Property Title As String
        Public Sub New(title As String)
            Me.Title = title
        End Sub
    End Class
End Namespace
