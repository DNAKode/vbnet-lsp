Imports System.Threading
Imports System.Threading.Tasks
Imports Microsoft.Extensions.Logging.Abstractions
Imports VbNet.LanguageServer.Protocol
Imports VbNet.LanguageServer.Services
Imports VbNet.LanguageServer.Workspace
Imports Xunit

Namespace VbNet.LanguageServer.Tests.Services

    ''' <summary>
    ''' Unit tests for CompletionService.
    ''' </summary>
    Public Class CompletionServiceTests
        Private ReadOnly _workspaceManager As WorkspaceManager
        Private ReadOnly _documentManager As DocumentManager
        Private ReadOnly _completionService As CompletionService

        Public Sub New()
            _workspaceManager = New WorkspaceManager(NullLogger(Of WorkspaceManager).Instance)
            _documentManager = New DocumentManager(_workspaceManager, NullLogger(Of DocumentManager).Instance)
            _completionService = New CompletionService(
                _workspaceManager,
                _documentManager,
                NullLogger(Of CompletionService).Instance)

            _workspaceManager.Initialize()
        End Sub

        <Fact>
        Public Async Function GetCompletionAsync_NoDocument_ReturnsEmptyList() As Task
            Dim request = New CompletionParams With {
                .TextDocument = New TextDocumentIdentifier With {.Uri = "file:///nonexistent.vb"},
                .Position = New Position With {.Line = 0, .Character = 0}
            }

            Dim result = Await _completionService.GetCompletionAsync(request, CancellationToken.None)

            Assert.NotNull(result)
            Assert.False(result.IsIncomplete)
            Assert.Empty(result.Items)
        End Function

        <Fact>
        Public Async Function GetCompletionAsync_StandaloneDocument_ReturnsEmptyList() As Task
            Dim uri = "file:///c:/test/module.vb"
            Dim text = "Module Module1" & vbLf & "    Sub Main()" & vbLf & "        " & vbLf & "    End Sub" & vbLf & "End Module"

            _documentManager.HandleDidOpen(New DidOpenTextDocumentParams With {
                .TextDocument = New TextDocumentItem With {
                    .Uri = uri,
                    .LanguageId = "vb",
                    .Version = 1,
                    .Text = text
                }
            })

            Dim request = New CompletionParams With {
                .TextDocument = New TextDocumentIdentifier With {.Uri = uri},
                .Position = New Position With {.Line = 2, .Character = 8}
            }

            Dim result = Await _completionService.GetCompletionAsync(request, CancellationToken.None)

            Assert.NotNull(result)
            Assert.False(result.IsIncomplete)
            Assert.Empty(result.Items)
        End Function

        <Fact>
        Public Async Function GetCompletionAsync_NullParams_ReturnsEmptyList() As Task
            Dim result = Await _completionService.GetCompletionAsync(Nothing, CancellationToken.None)

            Assert.NotNull(result)
            Assert.Empty(result.Items)
        End Function

        <Fact>
        Public Async Function ResolveCompletionItemAsync_NoData_ReturnsOriginalItem() As Task
            Dim item = New CompletionItem With {
                .Label = "Test",
                .Kind = CompletionItemKind.Method
            }

            Dim result = Await _completionService.ResolveCompletionItemAsync(item, CancellationToken.None)

            Assert.NotNull(result)
            Assert.Equal("Test", result.Label)
            Assert.Equal(CompletionItemKind.Method, result.Kind)
        End Function

        <Fact>
        Public Async Function ResolveCompletionItemAsync_NullItem_ReturnsEmptyItem() As Task
            Dim result = Await _completionService.ResolveCompletionItemAsync(Nothing, CancellationToken.None)

            Assert.NotNull(result)
            Assert.Equal("", result.Label)
        End Function
    End Class

End Namespace
