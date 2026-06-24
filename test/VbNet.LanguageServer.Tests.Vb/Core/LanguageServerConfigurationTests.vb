Imports System.Reflection
Imports Xunit

Namespace VbNet.LanguageServer.Tests.Configuration

    Public Class LanguageServerConfigurationTests

        <Fact>
        Public Sub WorkspacePathArrays_TreatMissingAndEmptySettingsAsEquivalent()
            Assert.True(AreWorkspacePathArraysEquivalent(Nothing, Array.Empty(Of String)()))
            Assert.True(AreWorkspacePathArraysEquivalent(Array.Empty(Of String)(), Nothing))
            Assert.True(AreWorkspacePathArraysEquivalent(Nothing, Nothing))
        End Sub

        <Fact>
        Public Sub WorkspacePathArrays_StillDetectRealChanges()
            Assert.True(AreWorkspacePathArraysEquivalent(
                New String() {"ProjectA.vbproj"},
                New String() {"projecta.vbproj"}))
            Assert.False(AreWorkspacePathArraysEquivalent(
                New String() {"ProjectA.vbproj"},
                Array.Empty(Of String)()))
        End Sub

        Private Shared Function AreWorkspacePathArraysEquivalent(leftValues As String(), rightValues As String()) As Boolean
            Dim method = GetType(Global.VbNet.LanguageServer.Core.LanguageServer).GetMethod(
                "AreEquivalent",
                BindingFlags.NonPublic Or BindingFlags.Static)

            Assert.NotNull(method)

            Return CBool(method.Invoke(Nothing, New Object() {leftValues, rightValues}))
        End Function
    End Class

End Namespace
