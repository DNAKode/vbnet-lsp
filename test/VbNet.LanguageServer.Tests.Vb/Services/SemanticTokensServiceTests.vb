Imports System.Threading
Imports System.Threading.Tasks
Imports Microsoft.Extensions.Logging.Abstractions
Imports VbNet.LanguageServer.Protocol
Imports VbNet.LanguageServer.Services
Imports VbNet.LanguageServer.Workspace
Imports Xunit

Namespace VbNet.LanguageServer.Tests.Services

    ''' <summary>
    ''' Unit tests for SemanticTokensService.
    ''' </summary>
    Public Class SemanticTokensServiceTests
        Private ReadOnly _workspaceManager As WorkspaceManager
        Private ReadOnly _documentManager As DocumentManager
        Private ReadOnly _semanticTokensService As SemanticTokensService

        Public Sub New()
            _workspaceManager = New WorkspaceManager(NullLogger(Of WorkspaceManager).Instance)
            _documentManager = New DocumentManager(_workspaceManager, NullLogger(Of DocumentManager).Instance)
            _semanticTokensService = New SemanticTokensService(
                _workspaceManager,
                _documentManager,
                NullLogger(Of SemanticTokensService).Instance)

            _workspaceManager.Initialize()
        End Sub

        <Fact>
        Public Async Function GetSemanticTokensAsync_NoDocument_ReturnsEmpty() As Task
            Dim result = Await _semanticTokensService.GetSemanticTokensAsync(New SemanticTokensParams With {
                .TextDocument = New TextDocumentIdentifier With {.Uri = "file:///nonexistent.vb"}
            }, CancellationToken.None)

            Assert.NotNull(result)
            Assert.Empty(result.Data)
        End Function

        <Fact>
        Public Async Function GetSemanticTokensAsync_NullParams_ReturnsEmpty() As Task
            Dim result = Await _semanticTokensService.GetSemanticTokensAsync(Nothing, CancellationToken.None)

            Assert.NotNull(result)
            Assert.Empty(result.Data)
        End Function
    End Class

End Namespace
