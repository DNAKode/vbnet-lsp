Imports System.Text.Json
Imports System.IO
Imports System.Threading
Imports System.Threading.Tasks
Imports Microsoft.CodeAnalysis
Imports Microsoft.CodeAnalysis.Text
Imports Microsoft.CodeAnalysis.VisualBasic
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
        Public Sub GetDefaultOptions_AdvertisesExtractKinds()
            Dim options = CodeActionsService.GetDefaultOptions()

            Assert.NotNull(options)
            Assert.NotNull(options.CodeActionKinds)
            Assert.Contains(CodeActionKind.Source, options.CodeActionKinds)
            Assert.Contains(CodeActionKind.Refactor, options.CodeActionKinds)
            Assert.Contains("refactor.extract", options.CodeActionKinds)
        End Sub

        ' --- Property tests: range normalization ---

        <Theory>
        <InlineData(-1, 0, 0, 0)>
        <InlineData(0, 0, -1, 0)>
        <InlineData(0, -5, 0, 0)>
        Public Async Function GetCodeActionsAsync_InvalidRange_ReturnsEmpty(startLine As Integer, startChar As Integer, endLine As Integer, endChar As Integer) As Task
            ' Open a minimal in-memory document so source text exists
            Dim uri = "file:///test-range.vb"
            _documentManager.HandleDidOpen(New DidOpenTextDocumentParams With {
                .TextDocument = New TextDocumentItem With {
                    .Uri = uri,
                    .LanguageId = "vb",
                    .Version = 1,
                    .Text = "Public Class C" & Environment.NewLine & "End Class"
                }
            })

            Dim result = Await _codeActionsService.GetCodeActionsAsync(New CodeActionParams With {
                .TextDocument = New TextDocumentIdentifier With {.Uri = uri},
                .Range = New Global.VbNet.LanguageServer.Protocol.Range With {
                    .Start = New Position(startLine, startChar),
                    .End = New Position(endLine, endChar)
                },
                .Context = New CodeActionContext()
            }, CancellationToken.None)

            Assert.NotNull(result)
            ' An invalid range must not produce extract actions
            Assert.DoesNotContain(result, Function(a) String.Equals(a.Kind, "refactor.extract", StringComparison.Ordinal))
        End Function

        ' --- Property tests: deterministic mapping ---

        <Fact>
        Public Async Function GetCodeActionsAsync_Deterministic_ReturnsSameTitles() As Task
            Dim uri = "file:///test-det.vb"
            Dim text = "Option Explicit On" & Environment.NewLine & "Public Class C" & Environment.NewLine & "End Class"
            _documentManager.HandleDidOpen(New DidOpenTextDocumentParams With {
                .TextDocument = New TextDocumentItem With {
                    .Uri = uri,
                    .LanguageId = "vb",
                    .Version = 1,
                    .Text = text
                }
            })

            Dim range = New Global.VbNet.LanguageServer.Protocol.Range With {
                .Start = New Position(0, 0),
                .End = New Position(0, 0)
            }
            Dim params = New CodeActionParams With {
                .TextDocument = New TextDocumentIdentifier With {.Uri = uri},
                .Range = range,
                .Context = New CodeActionContext()
            }

            Dim result1 = Await _codeActionsService.GetCodeActionsAsync(params, CancellationToken.None)
            Dim result2 = Await _codeActionsService.GetCodeActionsAsync(params, CancellationToken.None)

            Assert.Equal(result1.Select(Function(a) a.Title).OrderBy(Function(t) t),
                         result2.Select(Function(a) a.Title).OrderBy(Function(t) t))
        End Function

        ' --- Property tests: total parser — arbitrary payload never crashes ---

        <Theory>
        <InlineData("{}")>
        <InlineData("{""unknownField"":42}")>
        <InlineData("null")>
        <InlineData("[]")>
        <InlineData("""string""")>
        <InlineData("{""actionType"":""extract"",""payloadVersion"":1,""uri"":""file:///x.vb""}")>
        Public Async Function ResolveCodeActionAsync_ArbitraryPayload_NeverCrashes(payloadJson As String) As Task
            Dim action As New CodeAction With {
                .Title = "Test",
                .Kind = "refactor.extract",
                .Data = JsonSerializer.Deserialize(Of JsonElement)(payloadJson)
            }

            ' Must not throw; may return action unchanged or with edit
            Dim result = Await _codeActionsService.ResolveCodeActionAsync(action, CancellationToken.None)
            Assert.NotNull(result)
        End Function

        ' --- Edge: empty selection span produces no extract actions ---

        <Fact>
        Public Async Function GetCodeActionsAsync_ZeroLengthSelection_ReturnsNoExtractActions() As Task
            Dim uri = "file:///test-empty-sel.vb"
            _documentManager.HandleDidOpen(New DidOpenTextDocumentParams With {
                .TextDocument = New TextDocumentItem With {
                    .Uri = uri,
                    .LanguageId = "vb",
                    .Version = 1,
                    .Text = "Public Class C" & Environment.NewLine & "    Public Sub M()" & Environment.NewLine & "    End Sub" & Environment.NewLine & "End Class"
                }
            })

            Dim result = Await _codeActionsService.GetCodeActionsAsync(New CodeActionParams With {
                .TextDocument = New TextDocumentIdentifier With {.Uri = uri},
                .Range = New Global.VbNet.LanguageServer.Protocol.Range With {
                    .Start = New Position(1, 4),
                    .End = New Position(1, 4)
                },
                .Context = New CodeActionContext()
            }, CancellationToken.None)

            Assert.NotNull(result)
            Assert.DoesNotContain(result, Function(a) String.Equals(a.Kind, "refactor.extract", StringComparison.Ordinal))
        End Function

        ' --- Cancellation safety ---

        <Fact>
        Public Async Function GetCodeActionsAsync_PreCancelledToken_DoesNotCrashWithUnexpectedException() As Task
            Using cts = New CancellationTokenSource()
                cts.Cancel()

                Dim uri = "file:///test-cancel.vb"
                _documentManager.HandleDidOpen(New DidOpenTextDocumentParams With {
                    .TextDocument = New TextDocumentItem With {
                        .Uri = uri,
                        .LanguageId = "vb",
                        .Version = 1,
                        .Text = "Public Class C" & Environment.NewLine & "End Class"
                    }
                })

                ' Allow OperationCanceledException; disallow any other exception
                Dim ex = Await Record.ExceptionAsync(Async Function()
                    Await _codeActionsService.GetCodeActionsAsync(New CodeActionParams With {
                        .TextDocument = New TextDocumentIdentifier With {.Uri = uri},
                        .Range = New Global.VbNet.LanguageServer.Protocol.Range With {
                            .Start = New Position(0, 0),
                            .End = New Position(0, 0)
                        },
                        .Context = New CodeActionContext()
                    }, cts.Token)
                End Function)

                If ex IsNot Nothing Then
                    Assert.IsAssignableFrom(Of OperationCanceledException)(ex)
                End If
            End Using
        End Function

        <Fact>
        Public Async Function ResolveCodeActionAsync_PreCancelledToken_DoesNotCrashWithUnexpectedException() As Task
            Using cts = New CancellationTokenSource()
                cts.Cancel()

                Dim action As New CodeAction With {
                    .Title = "Test",
                    .Kind = "refactor.extract",
                    .Data = Nothing
                }

                Dim ex = Await Record.ExceptionAsync(Async Function()
                    Await _codeActionsService.ResolveCodeActionAsync(action, cts.Token)
                End Function)

                ' Null data returns early without hitting cancellation; either null result or no exception is valid
                If ex IsNot Nothing Then
                    Assert.IsAssignableFrom(Of OperationCanceledException)(ex)
                End If
            End Using
        End Function

        ' --- Stale resolve guard: action with unknown path returns unchanged ---

        <Fact>
        Public Async Function ResolveCodeActionAsync_ExtractWithUnknownPath_ReturnsActionUnchanged() As Task
            Dim action As New CodeAction With {
                .Title = "Extract Method",
                .Kind = "refactor.extract",
                .Data = New With {
                    Key .payloadVersion = 1,
                    Key .actionType = "extract",
                    Key .strategy = "roslyn",
                    Key .uri = "file:///nonexistent.vb",
                    Key .startLine = 0,
                    Key .startCharacter = 0,
                    Key .endLine = 1,
                    Key .endCharacter = 10,
                    Key .actionPath = New String() {"Extract Method"}
                }
            }

            Dim result = Await _codeActionsService.ResolveCodeActionAsync(action, CancellationToken.None)

            Assert.NotNull(result)
            Assert.Null(result.Edit)
        End Function

        <Fact>
        Public Async Function GetCodeActionsAsync_SelectionThatCutsIfBoundary_DoesNotReturnExtractAction() As Task
            Dim projectPath = Path.Combine("C:\Code\Repos\vbnet-lsp\test\TestProjects", "MediumProject", "MediumProject.vbproj")
            Dim filePath = Path.Combine("C:\Code\Repos\vbnet-lsp\test\TestProjects", "MediumProject", "Program.vb")

            Await _workspaceManager.LoadProjectAsync(projectPath)

            Dim uri = New Uri(filePath).ToString()
            Dim text = Await File.ReadAllTextAsync(filePath)

            _documentManager.HandleDidOpen(New DidOpenTextDocumentParams With {
                .TextDocument = New TextDocumentItem With {
                    .Uri = uri,
                    .LanguageId = "vb",
                    .Version = 1,
                    .Text = text
                }
            })

            Dim result = Await _codeActionsService.GetCodeActionsAsync(New CodeActionParams With {
                .TextDocument = New TextDocumentIdentifier With {.Uri = uri},
                .Range = New Global.VbNet.LanguageServer.Protocol.Range With {
                    .Start = New Position(5, 0),
                    .End = New Position(6, 99)
                },
                .Context = New CodeActionContext()
            }, CancellationToken.None)

            Assert.NotNull(result)
            Assert.DoesNotContain(result, Function(action) String.Equals(action.Kind, "refactor.extract", StringComparison.Ordinal))
        End Function

        Private Shared ReadOnly ScopeSafetySource As String =
            "Public Class ScopeFixture" & Environment.NewLine &
            "    Public Sub Main()" & Environment.NewLine &
            "        If outerCondition Then" & Environment.NewLine &
            "            If innerCondition Then" & Environment.NewLine &
            "                DoThing()" & Environment.NewLine &
            "            End If" & Environment.NewLine &
            "            DoOtherThing()" & Environment.NewLine &
            "        End If" & Environment.NewLine &
            "        For i As Integer = 0 To 1" & Environment.NewLine &
            "            Console.WriteLine(i)" & Environment.NewLine &
            "        Next" & Environment.NewLine &
            "        Try" & Environment.NewLine &
            "            Risky()" & Environment.NewLine &
            "        Catch ex As Exception" & Environment.NewLine &
            "            Handle()" & Environment.NewLine &
            "        Finally" & Environment.NewLine &
            "            Cleanup()" & Environment.NewLine &
            "        End Try" & Environment.NewLine &
            "    End Sub" & Environment.NewLine &
            "End Class" & Environment.NewLine

        Public Shared Iterator Function GetLexicalScopeCases() As IEnumerable(Of Object())
            Yield New Object() {4, 4, True}
            Yield New Object() {4, 7, False}
            Yield New Object() {2, 7, True}
            Yield New Object() {9, 10, False}
            Yield New Object() {12, 12, True}
            Yield New Object() {13, 14, False}
            Yield New Object() {15, 16, False}
        End Function

        <Theory>
        <MemberData(NameOf(GetLexicalScopeCases))>
        Public Sub IsSelectionLexicallySafe_SelectionVariants_ReturnsExpected(startLine As Integer, endLine As Integer, expected As Boolean)
            Dim text = SourceText.From(ScopeSafetySource)
            Dim root = VisualBasicSyntaxTree.ParseText(text).GetRoot()
            Dim selection = TextSpan.FromBounds(text.Lines(startLine).Start, text.Lines(endLine).End)

            Dim actual = CodeActionsService.IsSelectionLexicallySafe(selection, root)

            Assert.Equal(expected, actual)
        End Sub

        ' --- Null/missing payload ---

        <Fact>
        Public Async Function ResolveCodeActionAsync_NullData_ReturnsActionUnchanged() As Task
            Dim action As New CodeAction With {
                .Title = "No data",
                .Kind = "source",
                .Data = Nothing
            }

            Dim result = Await _codeActionsService.ResolveCodeActionAsync(action, CancellationToken.None)

            Assert.NotNull(result)
            Assert.Null(result.Edit)
        End Function
    End Class

End Namespace
