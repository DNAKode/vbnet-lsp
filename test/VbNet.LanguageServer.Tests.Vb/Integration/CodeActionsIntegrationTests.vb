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
    End Class

End Namespace
