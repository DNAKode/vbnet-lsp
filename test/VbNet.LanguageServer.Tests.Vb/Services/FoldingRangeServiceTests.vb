Imports System.Threading
Imports System.Threading.Tasks
Imports Microsoft.Extensions.Logging.Abstractions
Imports VbNet.LanguageServer.Protocol
Imports VbNet.LanguageServer.Services
Imports VbNet.LanguageServer.Workspace
Imports Xunit

Namespace VbNet.LanguageServer.Tests.Services

    ''' <summary>
    ''' Unit tests for FoldingRangeService.
    ''' </summary>
    Public Class FoldingRangeServiceTests
        Private ReadOnly _workspaceManager As WorkspaceManager
        Private ReadOnly _documentManager As DocumentManager
        Private ReadOnly _foldingRangeService As FoldingRangeService

        Public Sub New()
            _workspaceManager = New WorkspaceManager(NullLogger(Of WorkspaceManager).Instance)
            _documentManager = New DocumentManager(_workspaceManager, NullLogger(Of DocumentManager).Instance)
            _foldingRangeService = New FoldingRangeService(
                _workspaceManager,
                _documentManager,
                NullLogger(Of FoldingRangeService).Instance)

            _workspaceManager.Initialize()
        End Sub

        <Fact>
        Public Async Function GetFoldingRangesAsync_NoDocument_ReturnsEmpty() As Task
            Dim request = New FoldingRangeParams With {
                .TextDocument = New TextDocumentIdentifier With {.Uri = "file:///nonexistent.vb"}
            }

            Dim result = Await _foldingRangeService.GetFoldingRangesAsync(request, CancellationToken.None)

            Assert.NotNull(result)
            Assert.Empty(result)
        End Function

        <Fact>
        Public Async Function GetFoldingRangesAsync_StandaloneDocument_ReturnsRegion() As Task
            Dim uri = "file:///c:/test/module.vb"
            Dim text = String.Join(vbLf,
                "#Region ""Test""",
                "Module Module1",
                "    Sub Main()",
                "        Dim x = 1",
                "    End Sub",
                "End Module",
                "#End Region")

            _documentManager.HandleDidOpen(New DidOpenTextDocumentParams With {
                .TextDocument = New TextDocumentItem With {
                    .Uri = uri,
                    .LanguageId = "vb",
                    .Version = 1,
                    .Text = text
                }
            })

            Dim request = New FoldingRangeParams With {
                .TextDocument = New TextDocumentIdentifier With {.Uri = uri}
            }

            Dim result = Await _foldingRangeService.GetFoldingRangesAsync(request, CancellationToken.None)

            Assert.NotNull(result)
            Assert.Contains(result, Function(range) range.Kind = FoldingRangeKind.Region)
        End Function
    End Class

End Namespace
