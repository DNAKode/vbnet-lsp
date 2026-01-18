Imports System
Imports System.IO
Imports System.Linq
Imports System.Text.Json
Imports System.Threading
Imports System.Threading.Tasks
Imports Microsoft.Extensions.Logging.Abstractions
Imports VbNet.LanguageServer.Protocol
Imports VbNet.LanguageServer.Services
Imports VbNet.LanguageServer.Workspace
Imports Xunit

Namespace VbNet.LanguageServer.Tests.Integration

    ''' <summary>
    ''' Integration tests for CompletionService with real VB.NET projects.
    ''' </summary>
    <Collection("MSBuild")>
    Public Class CompletionIntegrationTests
        Implements IAsyncLifetime

        Private ReadOnly _workspaceManager As WorkspaceManager
        Private ReadOnly _documentManager As DocumentManager
        Private ReadOnly _completionService As CompletionService

        Private Shared ReadOnly TestProjectsRoot As String = GetTestProjectsRoot()

        Public Sub New()
            _workspaceManager = New WorkspaceManager(NullLogger(Of WorkspaceManager).Instance)
            _documentManager = New DocumentManager(_workspaceManager, NullLogger(Of DocumentManager).Instance)
            _completionService = New CompletionService(
                _workspaceManager,
                _documentManager,
                NullLogger(Of CompletionService).Instance)
        End Sub

        Private Shared Function GetTestProjectsRoot() As String
            Dim assemblyLocation = GetType(CompletionIntegrationTests).Assembly.Location
            Dim assemblyDir = Path.GetDirectoryName(assemblyLocation)
            Dim testProjectsPath = Path.GetFullPath(Path.Combine(assemblyDir, "..", "..", "..", "..", "TestProjects"))
            Return testProjectsPath
        End Function

        Public Function InitializeAsync() As Task Implements IAsyncLifetime.InitializeAsync
            _workspaceManager.Initialize()
            Return Task.CompletedTask
        End Function

        Public Async Function DisposeAsync() As Task Implements IAsyncLifetime.DisposeAsync
            Await _workspaceManager.DisposeAsync()
        End Function

        <Fact>
        Public Async Function GetCompletionAsync_InMethod_ReturnsResult() As Task
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

            Dim lines = text.Split(ControlChars.Lf)
            Dim lineIndex = Array.FindIndex(lines, Function(line) line.Contains("_counter += 1"))

            If lineIndex < 0 Then
                Return
            End If

            Dim request = New CompletionParams With {
                .TextDocument = New TextDocumentIdentifier With {.Uri = helperUri},
                .Position = New Position With {.Line = lineIndex, .Character = 8}
            }

            Dim result = Await _completionService.GetCompletionAsync(request, CancellationToken.None)

            Assert.NotNull(result)
            Assert.False(result.IsIncomplete)
        End Function

        <Fact>
        Public Async Function GetCompletionAsync_AtModuleLevel_ReturnsResult() As Task
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

            Dim lines = text.Split(ControlChars.Lf)
            Dim lineIndex = Array.FindIndex(lines, Function(line) line.Contains("End Function"))

            If lineIndex < 0 Then
                Return
            End If

            Dim request = New CompletionParams With {
                .TextDocument = New TextDocumentIdentifier With {.Uri = helperUri},
                .Position = New Position With {.Line = lineIndex + 1, .Character = 4}
            }

            Dim result = Await _completionService.GetCompletionAsync(request, CancellationToken.None)

            Assert.NotNull(result)
            Assert.False(result.IsIncomplete)
        End Function

        <Fact>
        Public Async Function GetCompletionAsync_CompletionItemsHaveKinds() As Task
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

            Dim request = New CompletionParams With {
                .TextDocument = New TextDocumentIdentifier With {.Uri = helperUri},
                .Position = New Position With {.Line = 5, .Character = 0}
            }

            Dim result = Await _completionService.GetCompletionAsync(request, CancellationToken.None)

            Assert.NotNull(result)

            If result.Items.Length > 0 Then
                Assert.All(result.Items, Sub(item) Assert.NotNull(item.Kind))
            End If
        End Function

        <Fact>
        Public Async Function GetCompletionAsync_CompletionItemsHaveSortText() As Task
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

            Dim request = New CompletionParams With {
                .TextDocument = New TextDocumentIdentifier With {.Uri = helperUri},
                .Position = New Position With {.Line = 5, .Character = 0}
            }

            Dim result = Await _completionService.GetCompletionAsync(request, CancellationToken.None)

            Assert.NotNull(result)

            If result.Items.Length > 0 Then
                Assert.All(result.Items, Sub(item) Assert.NotNull(item.SortText))
            End If
        End Function

        <Fact>
        Public Async Function ResolveCompletionItemAsync_UsesTextEdit() As Task
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

            Dim lines = text.Split(ControlChars.Lf)
            Dim lineIndex = Array.FindIndex(lines, Function(line) line.Contains("_counter += 1"))

            If lineIndex < 0 Then
                Return
            End If

            Dim request = New CompletionParams With {
                .TextDocument = New TextDocumentIdentifier With {.Uri = helperUri},
                .Position = New Position With {.Line = lineIndex, .Character = 8}
            }

            Dim result = Await _completionService.GetCompletionAsync(request, CancellationToken.None)

            If result.Items.Length = 0 Then
                Return
            End If

            Dim serializedItem = JsonSerializer.Serialize(result.Items(0), JsonSerializerOptionsProvider.Options)
            Dim lspItem = JsonSerializer.Deserialize(Of CompletionItem)(serializedItem, JsonSerializerOptionsProvider.Options)
            Dim resolved = Await _completionService.ResolveCompletionItemAsync(lspItem, CancellationToken.None)

            Assert.NotNull(resolved)
            Assert.NotNull(resolved.TextEdit)
        End Function

        <Fact>
        Public Async Function GetCompletionAsync_KeywordCompletion_ReplacesPrefix() As Task
            Dim projectPath = Path.Combine(TestProjectsRoot, "SmallProject", "SmallProject.vbproj")
            Dim helperPath = Path.Combine(TestProjectsRoot, "SmallProject", "Helper.vb")

            If Not File.Exists(projectPath) Then
                Return
            End If

            Await _workspaceManager.LoadProjectAsync(projectPath)

            Dim helperUri = New Uri(helperPath).ToString()
            Dim text = Await File.ReadAllTextAsync(helperPath)
            Dim updatedText = text.Replace("_counter += 1", "Dim x A")

            _documentManager.HandleDidOpen(New DidOpenTextDocumentParams With {
                .TextDocument = New TextDocumentItem With {
                    .Uri = helperUri,
                    .LanguageId = "vb",
                    .Version = 1,
                    .Text = updatedText
                }
            })

            Dim lines = updatedText.Split(ControlChars.Lf)
            Dim lineIndex = Array.FindIndex(lines, Function(lineText) lineText.Contains("Dim x A"))

            If lineIndex < 0 Then
                Return
            End If

            Dim line = lines(lineIndex)
            Dim aIndex = line.IndexOf("A", StringComparison.Ordinal)
            If aIndex < 0 Then
                Return
            End If

            Dim request = New CompletionParams With {
                .TextDocument = New TextDocumentIdentifier With {.Uri = helperUri},
                .Position = New Position With {.Line = lineIndex, .Character = aIndex + 1}
            }

            Dim result = Await _completionService.GetCompletionAsync(request, CancellationToken.None)
            Dim asItem = result.Items.FirstOrDefault(Function(item) item.Label = "As")

            Assert.NotNull(asItem)
            Assert.NotNull(asItem.TextEdit)
            Assert.Equal(lineIndex, asItem.TextEdit.Range.Start.Line)
            Assert.Equal(aIndex, asItem.TextEdit.Range.Start.Character)
            Assert.Equal(lineIndex, asItem.TextEdit.Range.End.Line)
            Assert.Equal(aIndex + 1, asItem.TextEdit.Range.End.Character)
        End Function
    End Class

End Namespace
