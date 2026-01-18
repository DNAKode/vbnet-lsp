Imports System.Threading
Imports System.Threading.Tasks
Imports Microsoft.Extensions.Logging.Abstractions
Imports VbNet.LanguageServer.Protocol
Imports VbNet.LanguageServer.Services
Imports VbNet.LanguageServer.Workspace
Imports Xunit

Namespace VbNet.LanguageServer.Tests.Services

    ''' <summary>
    ''' Unit tests for FormattingService.
    ''' </summary>
    Public Class FormattingServiceTests
        Private ReadOnly _workspaceManager As WorkspaceManager
        Private ReadOnly _documentManager As DocumentManager
        Private ReadOnly _formattingService As FormattingService

        Public Sub New()
            _workspaceManager = New WorkspaceManager(NullLogger(Of WorkspaceManager).Instance)
            _documentManager = New DocumentManager(_workspaceManager, NullLogger(Of DocumentManager).Instance)
            _formattingService = New FormattingService(
                _workspaceManager,
                _documentManager,
                NullLogger(Of FormattingService).Instance)

            _workspaceManager.Initialize()
        End Sub

        <Fact>
        Public Async Function FormatDocumentAsync_NoDocument_ReturnsEmpty() As Task
            Dim request = New DocumentFormattingParams With {
                .TextDocument = New TextDocumentIdentifier With {.Uri = "file:///nonexistent.vb"}
            }

            Dim result = Await _formattingService.FormatDocumentAsync(request, CancellationToken.None)

            Assert.NotNull(result)
            Assert.Empty(result)
        End Function

        <Fact>
        Public Async Function FormatRangeAsync_NoDocument_ReturnsEmpty() As Task
            Dim request = New DocumentRangeFormattingParams With {
                .TextDocument = New TextDocumentIdentifier With {.Uri = "file:///nonexistent.vb"},
                .Range = New Global.VbNet.LanguageServer.Protocol.Range With {
                    .Start = New Position(0, 0),
                    .End = New Position(0, 1)
                }
            }

            Dim result = Await _formattingService.FormatRangeAsync(request, CancellationToken.None)

            Assert.NotNull(result)
            Assert.Empty(result)
        End Function
    End Class

End Namespace
