Imports System.Threading
Imports System.Threading.Tasks
Imports Microsoft.Extensions.Logging.Abstractions
Imports VbNet.LanguageServer.Protocol
Imports VbNet.LanguageServer.Services
Imports VbNet.LanguageServer.Workspace
Imports Xunit

Namespace VbNet.LanguageServer.Tests.Services

    ''' <summary>
    ''' Unit tests for CodeActionsService.
    ''' </summary>
    Public Class CodeActionsServiceTests
        Private ReadOnly _workspaceManager As WorkspaceManager
        Private ReadOnly _documentManager As DocumentManager
        Private ReadOnly _codeActionsService As CodeActionsService

        Public Sub New()
            _workspaceManager = New WorkspaceManager(NullLogger(Of WorkspaceManager).Instance)
            _documentManager = New DocumentManager(_workspaceManager, NullLogger(Of DocumentManager).Instance)
            _codeActionsService = New CodeActionsService(
                _workspaceManager,
                _documentManager,
                NullLogger(Of CodeActionsService).Instance)

            _workspaceManager.Initialize()
        End Sub

        <Fact>
        Public Async Function GetCodeActionsAsync_NoDocument_ReturnsEmpty() As Task
            Dim result = Await _codeActionsService.GetCodeActionsAsync(New CodeActionParams With {
                .TextDocument = New TextDocumentIdentifier With {.Uri = "file:///nonexistent.vb"},
                .Range = New Global.VbNet.LanguageServer.Protocol.Range(),
                .Context = New CodeActionContext()
            }, CancellationToken.None)

            Assert.NotNull(result)
            Assert.Empty(result)
        End Function

        <Fact>
        Public Async Function GetCodeActionsAsync_NullParams_ReturnsEmpty() As Task
            Dim result = Await _codeActionsService.GetCodeActionsAsync(Nothing, CancellationToken.None)

            Assert.NotNull(result)
            Assert.Empty(result)
        End Function
    End Class

End Namespace
