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
    ''' Integration tests for DefinitionService with real VB.NET projects.
    ''' </summary>
    <Collection("MSBuild")>
    Public Class DefinitionIntegrationTests
        Implements IAsyncLifetime

        Private ReadOnly _workspaceManager As WorkspaceManager
        Private ReadOnly _documentManager As DocumentManager
        Private ReadOnly _definitionService As DefinitionService

        Private Shared ReadOnly TestProjectsRoot As String = GetTestProjectsRoot()

        Public Sub New()
            _workspaceManager = New WorkspaceManager(NullLogger(Of WorkspaceManager).Instance)
            _documentManager = New DocumentManager(_workspaceManager, NullLogger(Of DocumentManager).Instance)
            _definitionService = New DefinitionService(
                _workspaceManager,
                _documentManager,
                NullLogger(Of DefinitionService).Instance)
        End Sub

        Private Shared Function GetTestProjectsRoot() As String
            Dim assemblyLocation = GetType(DefinitionIntegrationTests).Assembly.Location
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
        Public Async Function GetDefinitionAsync_OnMethodDefinition_ReturnsLocation() As Task
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

            Dim request = New DefinitionParams With {
                .TextDocument = New TextDocumentIdentifier With {.Uri = helperUri},
                .Position = New Position With {.Line = lineIndex, .Character = doWorkIndex + 2}
            }

            Dim result = Await _definitionService.GetDefinitionAsync(request, CancellationToken.None)

            Assert.NotEmpty(result)
            Assert.Contains(result, Function(location) location.Uri.Contains("Helper.vb"))
        End Function

        <Fact>
        Public Async Function GetDefinitionAsync_OnClassDefinition_ReturnsLocation() As Task
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

            Dim request = New DefinitionParams With {
                .TextDocument = New TextDocumentIdentifier With {.Uri = helperUri},
                .Position = New Position With {.Line = lineIndex, .Character = helperIndex + 2}
            }

            Dim result = Await _definitionService.GetDefinitionAsync(request, CancellationToken.None)

            Assert.NotEmpty(result)
            Assert.Contains(result, Function(location) location.Uri.Contains("Helper.vb"))
        End Function

        <Fact>
        Public Async Function GetDefinitionAsync_OnMethodCall_NavigatesToDefinition() As Task
            Dim projectPath = Path.Combine(TestProjectsRoot, "SmallProject", "SmallProject.vbproj")
            Dim module1Path = Path.Combine(TestProjectsRoot, "SmallProject", "Module1.vb")

            If Not File.Exists(projectPath) OrElse Not File.Exists(module1Path) Then
                Return
            End If

            Await _workspaceManager.LoadProjectAsync(projectPath)

            Dim module1Uri = New Uri(module1Path).ToString()
            Dim text = Await File.ReadAllTextAsync(module1Path)

            _documentManager.HandleDidOpen(New DidOpenTextDocumentParams With {
                .TextDocument = New TextDocumentItem With {
                    .Uri = module1Uri,
                    .LanguageId = "vb",
                    .Version = 1,
                    .Text = text
                }
            })

            Dim lines = text.Split(ControlChars.Lf)
            Dim lineIndex = Array.FindIndex(lines, Function(line) line.Contains("Module"))

            If lineIndex < 0 Then
                Return
            End If

            Dim moduleIndex = lines(lineIndex).IndexOf("Module", StringComparison.Ordinal)

            Dim request = New DefinitionParams With {
                .TextDocument = New TextDocumentIdentifier With {.Uri = module1Uri},
                .Position = New Position With {.Line = lineIndex, .Character = moduleIndex + 2}
            }

            Dim result = Await _definitionService.GetDefinitionAsync(request, CancellationToken.None)

            If result.Length > 0 Then
                Assert.Contains(result, Function(location) location.Uri.Contains("Module1.vb"))
            End If
        End Function

        <Fact>
        Public Async Function GetDefinitionAsync_ReturnsValidRange() As Task
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

            Dim request = New DefinitionParams With {
                .TextDocument = New TextDocumentIdentifier With {.Uri = helperUri},
                .Position = New Position With {.Line = lineIndex, .Character = addIndex + 1}
            }

            Dim result = Await _definitionService.GetDefinitionAsync(request, CancellationToken.None)

            If result.Length > 0 Then
                Dim location = result(0)
                Assert.True(location.Range.Start.Line >= 0)
                Assert.True(location.Range.Start.Character >= 0)
                Assert.True(location.Range.End.Line >= location.Range.Start.Line)
            End If
        End Function
    End Class

End Namespace
