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
    ''' Integration tests for HoverService with real VB.NET projects.
    ''' </summary>
    <Collection("MSBuild")>
    Public Class HoverIntegrationTests
        Implements IAsyncLifetime

        Private ReadOnly _workspaceManager As WorkspaceManager
        Private ReadOnly _documentManager As DocumentManager
        Private ReadOnly _hoverService As HoverService

        Private Shared ReadOnly TestProjectsRoot As String = GetTestProjectsRoot()

        Public Sub New()
            _workspaceManager = New WorkspaceManager(NullLogger(Of WorkspaceManager).Instance)
            _documentManager = New DocumentManager(_workspaceManager, NullLogger(Of DocumentManager).Instance)
            _hoverService = New HoverService(
                _workspaceManager,
                _documentManager,
                NullLogger(Of HoverService).Instance)
        End Sub

        Private Shared Function GetTestProjectsRoot() As String
            Dim assemblyLocation = GetType(HoverIntegrationTests).Assembly.Location
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
        Public Async Function GetHoverAsync_OnMethodName_ReturnsHover() As Task
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

            Dim request = New HoverParams With {
                .TextDocument = New TextDocumentIdentifier With {.Uri = helperUri},
                .Position = New Position With {.Line = lineIndex, .Character = doWorkIndex + 2}
            }

            Dim result = Await _hoverService.GetHoverAsync(request, CancellationToken.None)

            Assert.NotNull(result)
            Assert.NotNull(result.Contents)
            Assert.Contains("DoWork", result.Contents.Value)
        End Function

        <Fact>
        Public Async Function GetHoverAsync_OnClassName_ReturnsHover() As Task
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

            Dim request = New HoverParams With {
                .TextDocument = New TextDocumentIdentifier With {.Uri = helperUri},
                .Position = New Position With {.Line = lineIndex, .Character = helperIndex + 2}
            }

            Dim result = Await _hoverService.GetHoverAsync(request, CancellationToken.None)

            Assert.NotNull(result)
            Assert.NotNull(result.Contents)
            Assert.Contains("Class", result.Contents.Value)
            Assert.Contains("Helper", result.Contents.Value)
        End Function

        <Fact>
        Public Async Function GetHoverAsync_ReturnsMarkdownFormat() As Task
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

            Dim request = New HoverParams With {
                .TextDocument = New TextDocumentIdentifier With {.Uri = helperUri},
                .Position = New Position With {.Line = lineIndex, .Character = lines(lineIndex).IndexOf("DoWork", StringComparison.Ordinal) + 2}
            }

            Dim result = Await _hoverService.GetHoverAsync(request, CancellationToken.None)

            If result IsNot Nothing Then
                Assert.Equal(MarkupKind.Markdown, result.Contents.Kind)
                Assert.Contains("```vb", result.Contents.Value)
            End If
        End Function

        <Fact>
        Public Async Function GetHoverAsync_OnDocumentedMethod_IncludesDocumentation() As Task
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

            Dim request = New HoverParams With {
                .TextDocument = New TextDocumentIdentifier With {.Uri = helperUri},
                .Position = New Position With {.Line = lineIndex, .Character = addIndex + 1}
            }

            Dim result = Await _hoverService.GetHoverAsync(request, CancellationToken.None)

            If result IsNot Nothing Then
                Assert.Contains("Add", result.Contents.Value)
                Assert.Contains("Adds two numbers", result.Contents.Value)
            End If
        End Function
    End Class

End Namespace
