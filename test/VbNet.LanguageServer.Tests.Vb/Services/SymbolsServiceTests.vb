Imports System.Threading
Imports System.Threading.Tasks
Imports Microsoft.Extensions.Logging.Abstractions
Imports VbNet.LanguageServer.Protocol
Imports VbNet.LanguageServer.Services
Imports VbNet.LanguageServer.Workspace
Imports Xunit

Namespace VbNet.LanguageServer.Tests.Services

    ''' <summary>
    ''' Unit tests for SymbolsService.
    ''' </summary>
    Public Class SymbolsServiceTests
        Private ReadOnly _workspaceManager As WorkspaceManager
        Private ReadOnly _documentManager As DocumentManager
        Private ReadOnly _symbolsService As SymbolsService

        Public Sub New()
            _workspaceManager = New WorkspaceManager(NullLogger(Of WorkspaceManager).Instance)
            _documentManager = New DocumentManager(_workspaceManager, NullLogger(Of DocumentManager).Instance)
            _symbolsService = New SymbolsService(
                _workspaceManager,
                _documentManager,
                NullLogger(Of SymbolsService).Instance)

            _workspaceManager.Initialize()
        End Sub

        <Fact>
        Public Async Function GetDocumentSymbolsAsync_NoDocument_ReturnsEmpty() As Task
            Dim request = New DocumentSymbolParams With {
                .TextDocument = New TextDocumentIdentifier With {.Uri = "file:///nonexistent.vb"}
            }

            Dim result = Await _symbolsService.GetDocumentSymbolsAsync(request, CancellationToken.None)

            Assert.Empty(result)
        End Function

        <Fact>
        Public Async Function GetDocumentSymbolsAsync_NullParams_ReturnsEmpty() As Task
            Dim result = Await _symbolsService.GetDocumentSymbolsAsync(Nothing, CancellationToken.None)

            Assert.Empty(result)
        End Function

        <Fact>
        Public Async Function GetDocumentSymbolsAsync_NullTextDocument_ReturnsEmpty() As Task
            Dim request = New DocumentSymbolParams With {
                .TextDocument = Nothing
            }

            Dim result = Await _symbolsService.GetDocumentSymbolsAsync(request, CancellationToken.None)

            Assert.Empty(result)
        End Function

        <Fact>
        Public Async Function GetDocumentSymbolsAsync_OpenDocumentWithoutWorkspace_ReturnsSymbols() As Task
            Dim uri = "file:///c:/test/Sample.vb"
            Dim text = "Namespace Test" & vbLf & "    Public Class Sample" & vbLf & "    End Class" & vbLf & "End Namespace"

            _documentManager.HandleDidOpen(New DidOpenTextDocumentParams With {
                .TextDocument = New TextDocumentItem With {
                    .Uri = uri,
                    .LanguageId = "vb",
                    .Version = 1,
                    .Text = text
                }
            })

            Dim request = New DocumentSymbolParams With {
                .TextDocument = New TextDocumentIdentifier With {.Uri = uri}
            }

            Dim result = Await _symbolsService.GetDocumentSymbolsAsync(request, CancellationToken.None)

            Assert.NotEmpty(result)
        End Function

        <Fact>
        Public Async Function GetWorkspaceSymbolsAsync_NoSolution_ReturnsEmpty() As Task
            Dim request = New WorkspaceSymbolParams With {
                .Query = "Test"
            }

            Dim result = Await _symbolsService.GetWorkspaceSymbolsAsync(request, CancellationToken.None)

            Assert.Empty(result)
        End Function

        <Fact>
        Public Async Function GetWorkspaceSymbolsAsync_NullParams_ReturnsEmpty() As Task
            Dim result = Await _symbolsService.GetWorkspaceSymbolsAsync(Nothing, CancellationToken.None)

            Assert.Empty(result)
        End Function

        <Fact>
        Public Async Function GetWorkspaceSymbolsAsync_EmptyQuery_ReturnsEmpty() As Task
            Dim request = New WorkspaceSymbolParams With {
                .Query = ""
            }

            Dim result = Await _symbolsService.GetWorkspaceSymbolsAsync(request, CancellationToken.None)

            Assert.Empty(result)
        End Function
    End Class

End Namespace
