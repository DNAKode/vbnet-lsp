Imports System.Threading
Imports System.Threading.Tasks
Imports Microsoft.Extensions.Logging.Abstractions
Imports VbNet.LanguageServer.Protocol
Imports VbNet.LanguageServer.Services
Imports VbNet.LanguageServer.Workspace
Imports Xunit

Namespace VbNet.LanguageServer.Tests.Services

    ''' <summary>
    ''' Unit tests for DefinitionService.
    ''' </summary>
    Public Class DefinitionServiceTests
        Private ReadOnly _workspaceManager As WorkspaceManager
        Private ReadOnly _documentManager As DocumentManager
        Private ReadOnly _definitionService As DefinitionService

        Public Sub New()
            _workspaceManager = New WorkspaceManager(NullLogger(Of WorkspaceManager).Instance)
            _documentManager = New DocumentManager(_workspaceManager, NullLogger(Of DocumentManager).Instance)
            _definitionService = New DefinitionService(
                _workspaceManager,
                _documentManager,
                NullLogger(Of DefinitionService).Instance)

            _workspaceManager.Initialize()
        End Sub

        <Fact>
        Public Async Function FindDefinitionAsync_NoDocument_ReturnsEmptyArray() As Task
            Dim request = New DefinitionParams With {
                .TextDocument = New TextDocumentIdentifier With {.Uri = "file:///nonexistent.vb"},
                .Position = New Position With {.Line = 0, .Character = 0}
            }

            Dim result = Await _definitionService.GetDefinitionAsync(request, CancellationToken.None)

            Assert.NotNull(result)
            Assert.Empty(result)
        End Function

        <Fact>
        Public Async Function FindDefinitionAsync_NullParams_ReturnsEmptyArray() As Task
            Dim result = Await _definitionService.GetDefinitionAsync(Nothing, CancellationToken.None)

            Assert.NotNull(result)
            Assert.Empty(result)
        End Function
    End Class

End Namespace
