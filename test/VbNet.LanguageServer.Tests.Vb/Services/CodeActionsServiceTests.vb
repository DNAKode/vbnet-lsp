Imports System.Threading
Imports System.Threading.Tasks
Imports System.Text.Json
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

        <Fact>
        Public Sub GetDefaultOptions_AdvertisesSourceAndExtractKinds()
            Dim options = CodeActionsService.GetDefaultOptions()

            Assert.True(options.ResolveProvider)
            Assert.Contains(CodeActionKind.Source, options.CodeActionKinds)
            Assert.Contains(CodeActionKind.Refactor, options.CodeActionKinds)
            Assert.Contains(CodeActionKind.RefactorExtract, options.CodeActionKinds)
        End Sub

        <Fact>
        Public Async Function GetCodeActionsAsync_SourceOnly_ReturnsOnlySourceActions() As Task
            Dim uri = "file:///source-only.vb"
            _documentManager.HandleDidOpen(New DidOpenTextDocumentParams With {
                .TextDocument = New TextDocumentItem With {
                    .Uri = uri,
                    .LanguageId = "vb",
                    .Version = 1,
                    .Text = "Public Class C" & vbLf & "End Class"
                }
            })

            Dim result = Await _codeActionsService.GetCodeActionsAsync(New CodeActionParams With {
                .TextDocument = New TextDocumentIdentifier With {.Uri = uri},
                .Range = New Global.VbNet.LanguageServer.Protocol.Range With {
                    .Start = New Position(0, 0),
                    .[End] = New Position(0, 0)
                },
                .Context = New CodeActionContext With {.Only = New String() {CodeActionKind.Source}}
            }, CancellationToken.None)

            Assert.NotEmpty(result)
            Assert.All(result, Sub(action) Assert.Equal(CodeActionKind.Source, action.Kind))
        End Function

        <Fact>
        Public Async Function GetCodeActionsAsync_RefactorOnlyWithoutRoslynDocument_ReturnsNoSourceActions() As Task
            Dim uri = "file:///refactor-only.vb"
            _documentManager.HandleDidOpen(New DidOpenTextDocumentParams With {
                .TextDocument = New TextDocumentItem With {
                    .Uri = uri,
                    .LanguageId = "vb",
                    .Version = 1,
                    .Text = "Public Class C" & vbLf & "End Class"
                }
            })

            Dim result = Await _codeActionsService.GetCodeActionsAsync(New CodeActionParams With {
                .TextDocument = New TextDocumentIdentifier With {.Uri = uri},
                .Range = New Global.VbNet.LanguageServer.Protocol.Range With {
                    .Start = New Position(0, 0),
                    .[End] = New Position(0, 0)
                },
                .Context = New CodeActionContext With {.Only = New String() {CodeActionKind.Refactor}}
            }, CancellationToken.None)

            Assert.NotNull(result)
            Assert.Empty(result)
        End Function

        <Fact>
        Public Async Function ResolveCodeActionAsync_MalformedExtractPayload_ReturnsActionUnchanged() As Task
            Dim action = New CodeAction With {
                .Title = "Extract Method",
                .Kind = CodeActionKind.RefactorExtract,
                .Data = JsonSerializer.SerializeToElement(New With {
                    Key .actionType = "extract",
                    Key .uri = "file:///missing.vb"
                })
            }

            Dim resolved = Await _codeActionsService.ResolveCodeActionAsync(action, CancellationToken.None)

            Assert.Same(action, resolved)
            Assert.Null(resolved.Edit)
        End Function
    End Class

End Namespace
