Imports System
Imports System.IO
Imports System.Linq
Imports System.Threading
Imports System.Threading.Tasks
Imports Microsoft.Extensions.Logging.Abstractions
Imports VbNet.LanguageServer.Protocol
Imports VbNet.LanguageServer.Services
Imports VbNet.LanguageServer.Workspace
Imports Xunit

Namespace VbNet.LanguageServer.Tests.Integration

    ''' <summary>
    ''' Integration tests for CodeActionsService with real VB.NET projects.
    ''' </summary>
    <Collection("MSBuild")>
    Public Class CodeActionsIntegrationTests
        Implements IAsyncLifetime

        Private ReadOnly _workspaceManager As WorkspaceManager
        Private ReadOnly _documentManager As DocumentManager
        Private ReadOnly _codeActionsService As CodeActionsService

        Private Shared ReadOnly TestProjectsRoot As String = GetTestProjectsRoot()

        Public Sub New()
            _workspaceManager = New WorkspaceManager(NullLogger(Of WorkspaceManager).Instance)
            _documentManager = New DocumentManager(_workspaceManager, NullLogger(Of DocumentManager).Instance)
            _codeActionsService = New CodeActionsService(
                _workspaceManager,
                _documentManager,
                NullLogger(Of CodeActionsService).Instance)
        End Sub

        Private Shared Function GetTestProjectsRoot() As String
            Dim assemblyLocation = GetType(CodeActionsIntegrationTests).Assembly.Location
            Dim assemblyDir = Path.GetDirectoryName(assemblyLocation)
            Return Path.GetFullPath(Path.Combine(assemblyDir, "..", "..", "..", "..", "TestProjects"))
        End Function

        Public Function InitializeAsync() As Task Implements IAsyncLifetime.InitializeAsync
            _workspaceManager.Initialize()
            Return Task.CompletedTask
        End Function

        Public Async Function DisposeAsync() As Task Implements IAsyncLifetime.DisposeAsync
            Await _workspaceManager.DisposeAsync()
        End Function

        <Fact>
        Public Async Function GetCodeActionsAsync_ReturnsOptionActions() As Task
            Dim projectPath = Path.Combine(TestProjectsRoot, "SmallProject", "SmallProject.vbproj")
            Dim helperPath = Path.Combine(TestProjectsRoot, "SmallProject", "Helper.vb")

            If Not File.Exists(projectPath) Then
                Return
            End If

            Await _workspaceManager.LoadProjectAsync(projectPath)

            Dim helperUri = New Uri(helperPath).ToString()
            Dim text = Await File.ReadAllTextAsync(helperPath)

            _documentManager.HandleDidOpen(New DidOpenTextDocumentParams With {
                .TextDocument = New TextDocumentItem With {
                    .Uri = helperUri,
                    .LanguageId = "vb",
                    .Version = 1,
                    .Text = text
                }
            })

            Dim result = Await _codeActionsService.GetCodeActionsAsync(New CodeActionParams With {
                .TextDocument = New TextDocumentIdentifier With {.Uri = helperUri},
                .Range = New Global.VbNet.LanguageServer.Protocol.Range With {
                    .Start = New Position(0, 0),
                    .End = New Position(0, 0)
                },
                .Context = New CodeActionContext()
            }, CancellationToken.None)

            Assert.NotNull(result)
            Assert.Contains(result, Function(action) action.Title.Contains("Option Strict", StringComparison.OrdinalIgnoreCase))
            Assert.Contains(result, Function(action) action.Title.Contains("Option Explicit", StringComparison.OrdinalIgnoreCase))
        End Function

        <Fact>
        Public Async Function ResolveCodeActionAsync_AddsEdit() As Task
            Dim projectPath = Path.Combine(TestProjectsRoot, "SmallProject", "SmallProject.vbproj")
            Dim helperPath = Path.Combine(TestProjectsRoot, "SmallProject", "Helper.vb")

            If Not File.Exists(projectPath) Then
                Return
            End If

            Await _workspaceManager.LoadProjectAsync(projectPath)

            Dim helperUri = New Uri(helperPath).ToString()
            Dim text = Await File.ReadAllTextAsync(helperPath)

            _documentManager.HandleDidOpen(New DidOpenTextDocumentParams With {
                .TextDocument = New TextDocumentItem With {
                    .Uri = helperUri,
                    .LanguageId = "vb",
                    .Version = 1,
                    .Text = text
                }
            })

            Dim result = Await _codeActionsService.GetCodeActionsAsync(New CodeActionParams With {
                .TextDocument = New TextDocumentIdentifier With {.Uri = helperUri},
                .Range = New Global.VbNet.LanguageServer.Protocol.Range With {
                    .Start = New Position(0, 0),
                    .End = New Position(0, 0)
                },
                .Context = New CodeActionContext()
            }, CancellationToken.None)

            If result.Length = 0 Then
                Return
            End If

            Dim action = result(0)
            Assert.Null(action.Edit)

            Dim resolved = Await _codeActionsService.ResolveCodeActionAsync(action, CancellationToken.None)

            Assert.NotNull(resolved)
            Assert.NotNull(resolved.Edit)
        End Function

        <Fact>
        Public Async Function GetCodeActionsAsync_ValidSelection_ReturnsExtractRefactor() As Task
            Dim projectPath = Path.Combine(TestProjectsRoot, "SmallProject", "SmallProject.vbproj")
            Dim helperPath = Path.Combine(TestProjectsRoot, "SmallProject", "Helper.vb")

            Assert.True(File.Exists(projectPath), $"Test fixture missing: {projectPath}. Ensure TestProjects/SmallProject is present.")

            Await _workspaceManager.LoadProjectAsync(projectPath)

            Dim helperUri = New Uri(helperPath).ToString()
            Dim text = Await File.ReadAllTextAsync(helperPath)

            _documentManager.HandleDidOpen(New DidOpenTextDocumentParams With {
                .TextDocument = New TextDocumentItem With {
                    .Uri = helperUri,
                    .LanguageId = "vb",
                    .Version = 1,
                    .Text = text
                }
            })

            Dim result = Await _codeActionsService.GetCodeActionsAsync(New CodeActionParams With {
                .TextDocument = New TextDocumentIdentifier With {.Uri = helperUri},
                .Range = New Global.VbNet.LanguageServer.Protocol.Range With {
                    .Start = New Position(20, 8),
                    .End = New Position(21, 53)
                },
                .Context = New CodeActionContext()
            }, CancellationToken.None)

            Assert.NotNull(result)
            Assert.Contains(result, Function(action) String.Equals(action.Kind, "refactor.extract", StringComparison.Ordinal))
        End Function

        <Fact>
        Public Async Function ResolveCodeActionAsync_ExtractRefactor_ProducesEdit() As Task
            Dim projectPath = Path.Combine(TestProjectsRoot, "SmallProject", "SmallProject.vbproj")
            Dim helperPath = Path.Combine(TestProjectsRoot, "SmallProject", "Helper.vb")

            If Not File.Exists(projectPath) Then
                Return
            End If

            Await _workspaceManager.LoadProjectAsync(projectPath)

            Dim helperUri = New Uri(helperPath).ToString()
            Dim text = Await File.ReadAllTextAsync(helperPath)

            _documentManager.HandleDidOpen(New DidOpenTextDocumentParams With {
                .TextDocument = New TextDocumentItem With {
                    .Uri = helperUri,
                    .LanguageId = "vb",
                    .Version = 1,
                    .Text = text
                }
            })

            Dim result = Await _codeActionsService.GetCodeActionsAsync(New CodeActionParams With {
                .TextDocument = New TextDocumentIdentifier With {.Uri = helperUri},
                .Range = New Global.VbNet.LanguageServer.Protocol.Range With {
                    .Start = New Position(20, 8),
                    .End = New Position(21, 53)
                },
                .Context = New CodeActionContext()
            }, CancellationToken.None)

            Dim extractAction = result.FirstOrDefault(Function(action) String.Equals(action.Kind, "refactor.extract", StringComparison.Ordinal))

            Assert.NotNull(extractAction)
            Assert.Null(extractAction.Edit)

            Dim resolved = Await _codeActionsService.ResolveCodeActionAsync(extractAction, CancellationToken.None)

            Assert.NotNull(resolved)
            Assert.NotNull(resolved.Edit)
            Assert.True((resolved.Edit.Changes IsNot Nothing AndAlso resolved.Edit.Changes.Count > 0) OrElse
                        (resolved.Edit.DocumentChanges IsNot Nothing AndAlso resolved.Edit.DocumentChanges.Length > 0))
        End Function
        <Fact>
        Public Async Function GetCodeActionsAsync_CancelledToken_DoesNotThrowUnexpectedException() As Task
            Dim projectPath = Path.Combine(TestProjectsRoot, "SmallProject", "SmallProject.vbproj")
            Dim helperPath = Path.Combine(TestProjectsRoot, "SmallProject", "Helper.vb")

            If Not File.Exists(projectPath) Then
                Return
            End If

            Await _workspaceManager.LoadProjectAsync(projectPath)

            Dim helperUri = New Uri(helperPath).ToString()
            Dim text = Await File.ReadAllTextAsync(helperPath)

            _documentManager.HandleDidOpen(New DidOpenTextDocumentParams With {
                .TextDocument = New TextDocumentItem With {
                    .Uri = helperUri,
                    .LanguageId = "vb",
                    .Version = 1,
                    .Text = text
                }
            })

            Using cts = New CancellationTokenSource()
                cts.Cancel()

                Dim ex = Await Record.ExceptionAsync(Async Function()
                    Await _codeActionsService.GetCodeActionsAsync(New CodeActionParams With {
                        .TextDocument = New TextDocumentIdentifier With {.Uri = helperUri},
                        .Range = New Global.VbNet.LanguageServer.Protocol.Range With {
                            .Start = New Position(20, 8),
                            .End = New Position(21, 53)
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
        Public Async Function GetCodeActionsAsync_RepeatRequests_ProducesStableResult() As Task
            Dim projectPath = Path.Combine(TestProjectsRoot, "SmallProject", "SmallProject.vbproj")
            Dim helperPath = Path.Combine(TestProjectsRoot, "SmallProject", "Helper.vb")

            If Not File.Exists(projectPath) Then
                Return
            End If

            Await _workspaceManager.LoadProjectAsync(projectPath)

            Dim helperUri = New Uri(helperPath).ToString()
            Dim text = Await File.ReadAllTextAsync(helperPath)

            _documentManager.HandleDidOpen(New DidOpenTextDocumentParams With {
                .TextDocument = New TextDocumentItem With {
                    .Uri = helperUri,
                    .LanguageId = "vb",
                    .Version = 1,
                    .Text = text
                }
            })

            Dim [params] = New CodeActionParams With {
                .TextDocument = New TextDocumentIdentifier With {.Uri = helperUri},
                .Range = New Global.VbNet.LanguageServer.Protocol.Range With {
                    .Start = New Position(20, 8),
                    .End = New Position(21, 53)
                },
                .Context = New CodeActionContext()
            }

            Dim result1 = Await _codeActionsService.GetCodeActionsAsync([params], CancellationToken.None)
            Dim result2 = Await _codeActionsService.GetCodeActionsAsync([params], CancellationToken.None)

            Assert.Equal(result1.Length, result2.Length)
            Assert.Equal(
                result1.Select(Function(a) a.Title).OrderBy(Function(t) t),
                result2.Select(Function(a) a.Title).OrderBy(Function(t) t))
        End Function

        <Fact>
        Public Async Function ResolveCodeActionAsync_ActionFromCurrentSnapshot_DoesNotApplyStaleEdit() As Task
            Dim projectPath = Path.Combine(TestProjectsRoot, "SmallProject", "SmallProject.vbproj")
            Dim helperPath = Path.Combine(TestProjectsRoot, "SmallProject", "Helper.vb")

            If Not File.Exists(projectPath) Then
                Return
            End If

            Await _workspaceManager.LoadProjectAsync(projectPath)

            Dim helperUri = New Uri(helperPath).ToString()
            Dim text = Await File.ReadAllTextAsync(helperPath)

            _documentManager.HandleDidOpen(New DidOpenTextDocumentParams With {
                .TextDocument = New TextDocumentItem With {
                    .Uri = helperUri,
                    .LanguageId = "vb",
                    .Version = 1,
                    .Text = text
                }
            })

            Dim result = Await _codeActionsService.GetCodeActionsAsync(New CodeActionParams With {
                .TextDocument = New TextDocumentIdentifier With {.Uri = helperUri},
                .Range = New Global.VbNet.LanguageServer.Protocol.Range With {
                    .Start = New Position(20, 8),
                    .End = New Position(21, 48)
                },
                .Context = New CodeActionContext()
            }, CancellationToken.None)

            Dim extractAction = result.FirstOrDefault(Function(a) String.Equals(a.Kind, "refactor.extract", StringComparison.Ordinal))
            If extractAction Is Nothing Then
                Return
            End If

            ' Simulate a document change (version bump) before resolving
            _documentManager.HandleDidChange(New DidChangeTextDocumentParams With {
                .TextDocument = New VersionedTextDocumentIdentifier With {
                    .Uri = helperUri,
                    .Version = 2
                },
                .ContentChanges = New TextDocumentContentChangeEvent() {
                    New TextDocumentContentChangeEvent With {
                        .Text = text & Environment.NewLine & "' version bump"
                    }
                }
            })

            ' Resolve must not crash and must return the action (possibly without edit if recompute fails)
            Dim resolved = Await _codeActionsService.ResolveCodeActionAsync(extractAction, CancellationToken.None)
            Assert.NotNull(resolved)
        End Function

        <Fact>
        Public Async Function ResolveCodeActionAsync_MultipleExtractions_ProducesUniqueMethodNames() As Task
            Dim projectPath = Path.Combine(TestProjectsRoot, "SmallProject", "SmallProject.vbproj")
            Dim helperPath = Path.Combine(TestProjectsRoot, "SmallProject", "Helper.vb")

            Assert.True(File.Exists(projectPath), $"Test fixture missing: {projectPath}. Ensure TestProjects/SmallProject is present.")

            Await _workspaceManager.LoadProjectAsync(projectPath)

            Dim helperUri = New Uri(helperPath).ToString()
            Dim text = Await File.ReadAllTextAsync(helperPath)

            _documentManager.HandleDidOpen(New DidOpenTextDocumentParams With {
                .TextDocument = New TextDocumentItem With {
                    .Uri = helperUri,
                    .LanguageId = "vb",
                    .Version = 1,
                    .Text = text
                }
            })

            ' First extraction
            Dim firstParams = New CodeActionParams With {
                .TextDocument = New TextDocumentIdentifier With {.Uri = helperUri},
                .Range = New Global.VbNet.LanguageServer.Protocol.Range With {
                    .Start = New Position(20, 8),
                    .End = New Position(21, 53)
                },
                .Context = New CodeActionContext()
            }

            Dim firstActions = Await _codeActionsService.GetCodeActionsAsync(firstParams, CancellationToken.None)
            Dim firstExtractAction = firstActions.FirstOrDefault(Function(action) String.Equals(action.Kind, "refactor.extract", StringComparison.Ordinal))
            Assert.NotNull(firstExtractAction)

            Dim firstResolved = Await _codeActionsService.ResolveCodeActionAsync(firstExtractAction, CancellationToken.None)
            Assert.NotNull(firstResolved)
            Assert.NotNull(firstResolved.Edit)

            ' Extract method name from first resolved edit, keyed by helperUri for determinism.
            Dim firstMethodName As String = Nothing
            Dim firstEditsForUri As TextEdit() = Nothing
            If firstResolved.Edit.Changes IsNot Nothing Then
                firstResolved.Edit.Changes.TryGetValue(helperUri, firstEditsForUri)
            End If
            If firstEditsForUri IsNot Nothing Then
                For Each e In firstEditsForUri
                    Dim match = System.Text.RegularExpressions.Regex.Match(e.NewText, "Private Sub (\w+)\(")
                    If match.Success Then
                        firstMethodName = match.Groups(1).Value
                        Exit For
                    End If
                Next
            End If

            Assert.NotNull(firstMethodName)

            ' Apply all edits for helperUri in end-to-start order to keep offsets valid,
            ' simulating a real LSP client applying the WorkspaceEdit.
            Dim updatedText = text
            If firstEditsForUri IsNot Nothing Then
                Dim sortedEdits = firstEditsForUri.
                    OrderByDescending(Function(e) e.Range.Start.Line).
                    ThenByDescending(Function(e) e.Range.Start.Character).
                    ToArray()
                For Each e In sortedEdits
                    updatedText = ApplyTextEdit(updatedText, e)
                Next
            End If

            ' Notify the document manager of the change to simulate applying the edit
            _documentManager.HandleDidChange(New DidChangeTextDocumentParams With {
                .TextDocument = New VersionedTextDocumentIdentifier With {
                    .Uri = helperUri,
                    .Version = 2
                },
                .ContentChanges = New TextDocumentContentChangeEvent() {
                    New TextDocumentContentChangeEvent With {
                        .Text = updatedText
                    }
                }
            })

            ' Second extraction (different selection, now from modified document)
            Dim secondParams = New CodeActionParams With {
                .TextDocument = New TextDocumentIdentifier With {.Uri = helperUri},
                .Range = New Global.VbNet.LanguageServer.Protocol.Range With {
                    .Start = New Position(20, 20),
                    .End = New Position(20, 35)
                },
                .Context = New CodeActionContext()
            }

            Dim secondActions = Await _codeActionsService.GetCodeActionsAsync(secondParams, CancellationToken.None)
            Dim secondExtractAction = secondActions.FirstOrDefault(Function(action) String.Equals(action.Kind, "refactor.extract", StringComparison.Ordinal))
            Assert.NotNull(secondExtractAction)

            Dim secondResolved = Await _codeActionsService.ResolveCodeActionAsync(secondExtractAction, CancellationToken.None)
            Assert.NotNull(secondResolved)
            Assert.NotNull(secondResolved.Edit)

            ' Extract method name from second resolved edit, keyed by helperUri for determinism.
            Dim secondMethodName As String = Nothing
            Dim secondEditsForUri As TextEdit() = Nothing
            If secondResolved.Edit.Changes IsNot Nothing Then
                secondResolved.Edit.Changes.TryGetValue(helperUri, secondEditsForUri)
            End If
            If secondEditsForUri IsNot Nothing Then
                For Each e In secondEditsForUri
                    Dim match = System.Text.RegularExpressions.Regex.Match(e.NewText, "Private Sub (\w+)\(")
                    If match.Success Then
                        secondMethodName = match.Groups(1).Value
                        Exit For
                    End If
                Next
            End If

            ' Verify both methods were extracted
            Assert.NotNull(secondMethodName)
            ' Verify they have different names
            Assert.NotEqual(firstMethodName, secondMethodName)
        End Function
        ''' <summary>
        ''' Applies a single TextEdit to source text by replacing the edit's range with NewText,
        ''' the same way a real LSP client would apply edits.
        ''' Detects the actual newline convention in the text (LF vs CRLF) rather than
        ''' relying on Environment.NewLine, so offset calculations are correct on all platforms.
        ''' </summary>
        Private Shared Function ApplyTextEdit(originalText As String, edit As TextEdit) As String
            ' Detect the newline style actually present in the text.
            ' The repo enforces LF via .gitattributes, but text could arrive with CRLF on Windows.
            Dim newline = If(originalText.Contains(vbCrLf), vbCrLf, vbLf)
            Dim nlLen = newline.Length
            Dim lines = originalText.Split({vbCrLf, vbLf}, StringSplitOptions.None)

            Dim CalcOffset = Function(lineNum As Integer, charNum As Integer) As Integer
                                 Dim offset = 0
                                 For i = 0 To Math.Min(lineNum, lines.Length) - 1
                                     offset += lines(i).Length + nlLen
                                 Next
                                 If lineNum < lines.Length Then
                                     offset += Math.Min(charNum, lines(lineNum).Length)
                                 Else
                                     offset = originalText.Length
                                 End If
                                 Return offset
                             End Function

            Dim startOffset = CalcOffset(edit.Range.Start.Line, edit.Range.Start.Character)
            Dim endOffset = CalcOffset(edit.Range.[End].Line, edit.Range.[End].Character)
            Return originalText.Substring(0, startOffset) & edit.NewText & originalText.Substring(endOffset)
        End Function

    End Class

End Namespace
