Module Program
    Sub Main(args As String())
        Console.WriteLine("VB.NET MediumProject")
        Dim sum = 0
        For i = 1 To 5
            sum += i
        Next
        Console.WriteLine($"Sum = {sum}")
    End Sub
End Module
