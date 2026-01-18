Imports Microsoft.Build.Locator
Imports Xunit

Namespace VbNet.LanguageServer.Tests.Integration

    ''' <summary>
    ''' Shared fixture that ensures MSBuild is registered once for all integration tests.
    ''' </summary>
    Public Class MSBuildFixture
        Private Shared _initialized As Boolean = False
        Private Shared ReadOnly _lockObject As New Object()

        Public Sub New()
            SyncLock _lockObject
                If Not _initialized Then
                    If Not MSBuildLocator.IsRegistered Then
                        MSBuildLocator.RegisterDefaults()
                    End If
                    _initialized = True
                End If
            End SyncLock
        End Sub
    End Class

    ''' <summary>
    ''' Collection definition for integration tests that need MSBuild.
    ''' All integration tests should use [Collection("MSBuild")] attribute.
    ''' </summary>
    <CollectionDefinition("MSBuild")>
    Public Class MSBuildCollection
        Implements ICollectionFixture(Of MSBuildFixture)
    End Class

End Namespace
