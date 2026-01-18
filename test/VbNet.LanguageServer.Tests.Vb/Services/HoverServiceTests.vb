Imports System.Threading
Imports System.Threading.Tasks
Imports Microsoft.Extensions.Logging.Abstractions
Imports VbNet.LanguageServer.Protocol
Imports VbNet.LanguageServer.Services
Imports VbNet.LanguageServer.Workspace
Imports Xunit

Namespace VbNet.LanguageServer.Tests.Services

    ''' <summary>
    ''' Unit tests for HoverService.
    ''' </summary>
    Public Class HoverServiceTests
        Private ReadOnly _workspaceManager As WorkspaceManager
        Private ReadOnly _documentManager As DocumentManager
        Private ReadOnly _hoverService As HoverService

        Public Sub New()
            _workspaceManager = New WorkspaceManager(NullLogger(Of WorkspaceManager).Instance)
            _documentManager = New DocumentManager(_workspaceManager, NullLogger(Of DocumentManager).Instance)
            _hoverService = New HoverService(
                _workspaceManager,
                _documentManager,
                NullLogger(Of HoverService).Instance)

            _workspaceManager.Initialize()
        End Sub

        <Fact>
        Public Async Function GetHoverAsync_NoDocument_ReturnsNull() As Task
            Dim request = New HoverParams With {
                .TextDocument = New TextDocumentIdentifier With {.Uri = "file:///nonexistent.vb"},
                .Position = New Position With {.Line = 0, .Character = 0}
            }

            Dim result = Await _hoverService.GetHoverAsync(request, CancellationToken.None)

            Assert.Null(result)
        End Function

        <Fact>
        Public Async Function GetHoverAsync_NullParams_ReturnsNull() As Task
            Dim result = Await _hoverService.GetHoverAsync(Nothing, CancellationToken.None)

            Assert.Null(result)
        End Function
    End Class

End Namespace
