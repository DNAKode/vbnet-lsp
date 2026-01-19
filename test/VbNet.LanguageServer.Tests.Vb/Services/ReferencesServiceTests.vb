Imports System.Threading
Imports System.Threading.Tasks
Imports Microsoft.Extensions.Logging.Abstractions
Imports VbNet.LanguageServer.Protocol
Imports VbNet.LanguageServer.Services
Imports VbNet.LanguageServer.Workspace
Imports Xunit

Namespace VbNet.LanguageServer.Tests.Services

    ''' <summary>
    ''' Unit tests for ReferencesService.
    ''' </summary>
    Public Class ReferencesServiceTests
        Private ReadOnly _workspaceManager As WorkspaceManager
        Private ReadOnly _documentManager As DocumentManager
        Private ReadOnly _referencesService As ReferencesService

        Public Sub New()
            _workspaceManager = New WorkspaceManager(NullLogger(Of WorkspaceManager).Instance)
            _documentManager = New DocumentManager(_workspaceManager, NullLogger(Of DocumentManager).Instance)
            _referencesService = New ReferencesService(
                _workspaceManager,
                _documentManager,
                NullLogger(Of ReferencesService).Instance)

            _workspaceManager.Initialize()
        End Sub

        <Fact>
        Public Async Function GetReferencesAsync_NoDocument_ReturnsEmpty() As Task
            Dim request = New ReferenceParams With {
                .TextDocument = New TextDocumentIdentifier With {.Uri = "file:///nonexistent.vb"},
                .Position = New Position With {.Line = 0, .Character = 0},
                .Context = New ReferenceContext With {.IncludeDeclaration = True}
            }

            Dim result = Await _referencesService.GetReferencesAsync(request, CancellationToken.None)

            Assert.NotNull(result)
            Assert.Empty(result)
        End Function

        <Fact>
        Public Async Function GetReferencesAsync_StandaloneDocument_ReturnsEmpty() As Task
            Dim uri = "file:///c:/test/module.vb"
            Dim text = "Module Module1" & vbLf & "    Sub Main()" & vbLf & "    End Sub" & vbLf & "End Module"

            _documentManager.HandleDidOpen(New DidOpenTextDocumentParams With {
                .TextDocument = New TextDocumentItem With {
                    .Uri = uri,
                    .LanguageId = "vb",
                    .Version = 1,
                    .Text = text
                }
            })

            Dim request = New ReferenceParams With {
                .TextDocument = New TextDocumentIdentifier With {.Uri = uri},
                .Position = New Position With {.Line = 1, .Character = 8},
                .Context = New ReferenceContext With {.IncludeDeclaration = True}
            }

            Dim result = Await _referencesService.GetReferencesAsync(request, CancellationToken.None)

            Assert.Empty(result)
        End Function

        <Fact>
        Public Async Function GetReferencesAsync_NullParams_ReturnsEmpty() As Task
            Dim result = Await _referencesService.GetReferencesAsync(Nothing, CancellationToken.None)

            Assert.NotNull(result)
            Assert.Empty(result)
        End Function

        <Fact>
        Public Async Function GetReferencesAsync_NullTextDocument_ReturnsEmpty() As Task
            Dim request = New ReferenceParams With {
                .TextDocument = Nothing,
                .Position = New Position With {.Line = 0, .Character = 0},
                .Context = New ReferenceContext With {.IncludeDeclaration = True}
            }

            Dim result = Await _referencesService.GetReferencesAsync(request, CancellationToken.None)

            Assert.NotNull(result)
            Assert.Empty(result)
        End Function
    End Class

End Namespace
