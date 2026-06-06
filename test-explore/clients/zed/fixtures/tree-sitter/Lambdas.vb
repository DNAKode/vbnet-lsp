Namespace Fixtures
    Public Module LambdaCoverage
        Public Sub CountNames(values As IEnumerable)
            Dim transform = Function(value As Object) value.ToString().Trim()
            Dim count = transform("zed").Length
        End Sub
    End Module
End Namespace
