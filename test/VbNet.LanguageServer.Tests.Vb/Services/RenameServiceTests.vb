Imports System.Threading
Imports System.Threading.Tasks
Imports Microsoft.Extensions.Logging.Abstractions
Imports VbNet.LanguageServer.Protocol
Imports VbNet.LanguageServer.Services
Imports VbNet.LanguageServer.Workspace
Imports Xunit

Namespace VbNet.LanguageServer.Tests.Services

    ''' <summary>
    ''' Unit tests for RenameService.
    ''' </summary>
    Public Class RenameServiceTests
        Private ReadOnly _workspaceManager As WorkspaceManager
        Private ReadOnly _documentManager As DocumentManager
        Private ReadOnly _renameService As RenameService

        Public Sub New()
            _workspaceManager = New WorkspaceManager(NullLogger(Of WorkspaceManager).Instance)
            _documentManager = New DocumentManager(_workspaceManager, NullLogger(Of DocumentManager).Instance)
            _renameService = New RenameService(
                _workspaceManager,
                _documentManager,
                NullLogger(Of RenameService).Instance)

            _workspaceManager.Initialize()
        End Sub

        <Fact>
        Public Async Function RenameSymbolAsync_NoDocument_ReturnsNull() As Task
            Dim request = New RenameParams With {
                .TextDocument = New TextDocumentIdentifier With {.Uri = "file:///nonexistent.vb"},
                .Position = New Position With {.Line = 0, .Character = 0},
                .NewName = "Renamed"
            }

            Dim result = Await _renameService.RenameAsync(request, CancellationToken.None)

            Assert.Null(result)
        End Function

        <Fact>
        Public Async Function PrepareRenameAsync_NoDocument_ReturnsNull() As Task
            Dim request = New PrepareRenameParams With {
                .TextDocument = New TextDocumentIdentifier With {.Uri = "file:///nonexistent.vb"},
                .Position = New Position With {.Line = 0, .Character = 0}
            }

            Dim result = Await _renameService.PrepareRenameAsync(request, CancellationToken.None)

            Assert.Null(result)
        End Function
    End Class

End Namespace
