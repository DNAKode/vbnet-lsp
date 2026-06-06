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
        Public Async Function GetCodeActionsAsync_ValidSelection_ReturnsRoslynExtractRefactor() As Task
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
                    .[End] = New Position(21, 53)
                },
                .Context = New CodeActionContext With {.Only = New String() {CodeActionKind.RefactorExtract}}
            }, CancellationToken.None)

            Assert.Contains(result, Function(action) String.Equals(action.Kind, CodeActionKind.RefactorExtract, StringComparison.Ordinal))
        End Function

        <Fact>
        Public Async Function ResolveCodeActionAsync_RoslynExtractRefactor_ProducesWorkspaceEdit() As Task
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
                    .[End] = New Position(21, 53)
                },
                .Context = New CodeActionContext With {.Only = New String() {CodeActionKind.RefactorExtract}}
            }, CancellationToken.None)

            Dim extractAction = result.FirstOrDefault(Function(action) String.Equals(action.Kind, CodeActionKind.RefactorExtract, StringComparison.Ordinal))
            Assert.NotNull(extractAction)
            Assert.Null(extractAction.Edit)

            Dim resolved = Await _codeActionsService.ResolveCodeActionAsync(extractAction, CancellationToken.None)

            Assert.NotNull(resolved)
            Assert.NotNull(resolved.Edit)

            Dim edits As TextEdit() = Nothing
            Assert.True(resolved.Edit.Changes IsNot Nothing AndAlso resolved.Edit.Changes.TryGetValue(helperUri, edits))
            Assert.NotEmpty(edits)

            Dim updatedText = ApplyTextEdits(text, edits)
            Assert.Contains("Sub", updatedText)
            Assert.Contains("DoWork", updatedText)
            Assert.NotEqual(text, updatedText)
        End Function

        <Fact>
        Public Async Function ResolveCodeActionAsync_RoslynExtractAfterDocumentChange_DoesNotCrash() As Task
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
                    .[End] = New Position(21, 53)
                },
                .Context = New CodeActionContext With {.Only = New String() {CodeActionKind.RefactorExtract}}
            }, CancellationToken.None)

            Dim extractAction = result.FirstOrDefault(Function(action) String.Equals(action.Kind, CodeActionKind.RefactorExtract, StringComparison.Ordinal))
            Assert.NotNull(extractAction)

            _documentManager.HandleDidChange(New DidChangeTextDocumentParams With {
                .TextDocument = New VersionedTextDocumentIdentifier With {
                    .Uri = helperUri,
                    .Version = 2
                },
                .ContentChanges = New TextDocumentContentChangeEvent() {
                    New TextDocumentContentChangeEvent With {
                        .Text = text & vbLf & "' changed"
                    }
                }
            })

            Dim resolved = Await _codeActionsService.ResolveCodeActionAsync(extractAction, CancellationToken.None)

            Assert.NotNull(resolved)
        End Function

        Private Shared Function ApplyTextEdits(text As String, edits As TextEdit()) As String
            Dim result = text
            For Each edit In edits.OrderByDescending(Function(item) item.Range.Start.Line).ThenByDescending(Function(item) item.Range.Start.Character)
                Dim startOffset = GetOffset(result, edit.Range.Start)
                Dim endOffset = GetOffset(result, edit.Range.End)
                result = result.Substring(0, startOffset) & edit.NewText & result.Substring(endOffset)
            Next

            Return result
        End Function

        Private Shared Function GetOffset(text As String, position As Position) As Integer
            Dim line = 0
            Dim offset = 0
            While line < position.Line AndAlso offset < text.Length
                If text(offset) = ChrW(10) Then
                    line += 1
                End If
                offset += 1
            End While

            Return Math.Min(text.Length, offset + position.Character)
        End Function
    End Class

End Namespace
