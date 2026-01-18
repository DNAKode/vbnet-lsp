Imports System
Imports System.IO
Imports System.Threading
Imports System.Threading.Tasks
Imports Microsoft.Extensions.Logging.Abstractions
Imports VbNet.LanguageServer.Protocol
Imports VbNet.LanguageServer.Services
Imports VbNet.LanguageServer.Workspace
Imports Xunit

Namespace VbNet.LanguageServer.Tests.Integration

    ''' <summary>
    ''' Integration tests for RenameService with real VB.NET projects.
    ''' </summary>
    <Collection("MSBuild")>
    Public Class RenameIntegrationTests
        Implements IAsyncLifetime

        Private ReadOnly _workspaceManager As WorkspaceManager
        Private ReadOnly _documentManager As DocumentManager
        Private ReadOnly _renameService As RenameService

        Private Shared ReadOnly TestProjectsRoot As String = GetTestProjectsRoot()

        Public Sub New()
            _workspaceManager = New WorkspaceManager(NullLogger(Of WorkspaceManager).Instance)
            _documentManager = New DocumentManager(_workspaceManager, NullLogger(Of DocumentManager).Instance)
            _renameService = New RenameService(
                _workspaceManager,
                _documentManager,
                NullLogger(Of RenameService).Instance)
        End Sub

        Private Shared Function GetTestProjectsRoot() As String
            Dim assemblyLocation = GetType(RenameIntegrationTests).Assembly.Location
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
        Public Async Function PrepareRenameAsync_OnMethodName_ReturnsRangeAndPlaceholder() As Task
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
            Dim lineIndex = Array.FindIndex(lines, Function(line) line.Contains("Public Sub DoWork"))

            If lineIndex < 0 Then
                Return
            End If

            Dim doWorkIndex = lines(lineIndex).IndexOf("DoWork", StringComparison.Ordinal)

            Dim request = New PrepareRenameParams With {
                .TextDocument = New TextDocumentIdentifier With {.Uri = helperUri},
                .Position = New Position With {.Line = lineIndex, .Character = doWorkIndex + 2}
            }

            Dim result = Await _renameService.PrepareRenameAsync(request, CancellationToken.None)

            Assert.NotNull(result)
            Assert.Equal("DoWork", result.Placeholder)
            Assert.Equal(lineIndex, result.Range.Start.Line)
        End Function

        <Fact>
        Public Async Function PrepareRenameAsync_OnClassName_ReturnsRangeAndPlaceholder() As Task
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
            Dim lineIndex = Array.FindIndex(lines, Function(line) line.Contains("Public Class Helper"))

            If lineIndex < 0 Then
                Return
            End If

            Dim helperIndex = lines(lineIndex).IndexOf("Helper", StringComparison.Ordinal)

            Dim request = New PrepareRenameParams With {
                .TextDocument = New TextDocumentIdentifier With {.Uri = helperUri},
                .Position = New Position With {.Line = lineIndex, .Character = helperIndex + 2}
            }

            Dim result = Await _renameService.PrepareRenameAsync(request, CancellationToken.None)

            Assert.NotNull(result)
            Assert.Equal("Helper", result.Placeholder)
        End Function

        <Fact>
        Public Async Function RenameAsync_OnMethodName_ReturnsWorkspaceEdit() As Task
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
            Dim lineIndex = Array.FindIndex(lines, Function(line) line.Contains("Public Sub DoWork"))

            If lineIndex < 0 Then
                Return
            End If

            Dim doWorkIndex = lines(lineIndex).IndexOf("DoWork", StringComparison.Ordinal)

            Dim request = New RenameParams With {
                .TextDocument = New TextDocumentIdentifier With {.Uri = helperUri},
                .Position = New Position With {.Line = lineIndex, .Character = doWorkIndex + 2},
                .NewName = "DoWorkRenamed"
            }

            Dim result = Await _renameService.RenameAsync(request, CancellationToken.None)

            Assert.NotNull(result)
            Assert.NotNull(result.Changes)
            Assert.NotEmpty(result.Changes)
            Assert.Contains(result.Changes.Keys, Function(key) key.Contains("Helper.vb"))
        End Function

        <Fact>
        Public Async Function RenameAsync_ReturnsValidTextEdits() As Task
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
            Dim lineIndex = Array.FindIndex(lines, Function(line) line.Contains("Public Function Add"))

            If lineIndex < 0 Then
                Return
            End If

            Dim addIndex = lines(lineIndex).IndexOf("Add", StringComparison.Ordinal)

            Dim request = New RenameParams With {
                .TextDocument = New TextDocumentIdentifier With {.Uri = helperUri},
                .Position = New Position With {.Line = lineIndex, .Character = addIndex + 1},
                .NewName = "AddNumbers"
            }

            Dim result = Await _renameService.RenameAsync(request, CancellationToken.None)

            If result IsNot Nothing AndAlso result.Changes IsNot Nothing Then
                For Each entry In result.Changes
                    For Each edit In entry.Value
                        Assert.True(edit.Range.Start.Line >= 0)
                        Assert.True(edit.Range.Start.Character >= 0)
                        Assert.True(edit.Range.End.Line >= edit.Range.Start.Line)
                        Assert.Contains("AddNumbers", edit.NewText)
                    Next
                Next
            End If
        End Function
    End Class

End Namespace
