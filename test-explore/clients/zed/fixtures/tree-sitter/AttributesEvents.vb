Imports System

Namespace Fixtures
    <Serializable>
    Public Class Repository
        Public Event Changed As EventHandler

        Public Property ItemCount As Integer

        Public Sub Add(item As Object)
            ItemCount += 1
        End Sub

        Public Function Find(selector As Object) As Object
            Return selector
        End Function
    End Class
End Namespace
