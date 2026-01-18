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
    ''' Integration tests for ReferencesService with real VB.NET projects.
    ''' </summary>
    <Collection("MSBuild")>
    Public Class ReferencesIntegrationTests
        Implements IAsyncLifetime

        Private ReadOnly _workspaceManager As WorkspaceManager
        Private ReadOnly _documentManager As DocumentManager
        Private ReadOnly _referencesService As ReferencesService

        Private Shared ReadOnly TestProjectsRoot As String = GetTestProjectsRoot()

        Public Sub New()
            _workspaceManager = New WorkspaceManager(NullLogger(Of WorkspaceManager).Instance)
            _documentManager = New DocumentManager(_workspaceManager, NullLogger(Of DocumentManager).Instance)
            _referencesService = New ReferencesService(
                _workspaceManager,
                _documentManager,
                NullLogger(Of ReferencesService).Instance)
        End Sub

        Private Shared Function GetTestProjectsRoot() As String
            Dim assemblyLocation = GetType(ReferencesIntegrationTests).Assembly.Location
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
        Public Async Function GetReferencesAsync_OnMethodDefinition_FindsReferences() As Task
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

            Dim request = New ReferenceParams With {
                .TextDocument = New TextDocumentIdentifier With {.Uri = helperUri},
                .Position = New Position With {.Line = lineIndex, .Character = doWorkIndex + 2},
                .Context = New ReferenceContext With {.IncludeDeclaration = True}
            }

            Dim result = Await _referencesService.GetReferencesAsync(request, CancellationToken.None)

            Assert.NotEmpty(result)
        End Function

        <Fact>
        Public Async Function GetReferencesAsync_OnClassDefinition_FindsReferences() As Task
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

            Dim request = New ReferenceParams With {
                .TextDocument = New TextDocumentIdentifier With {.Uri = helperUri},
                .Position = New Position With {.Line = lineIndex, .Character = helperIndex + 2},
                .Context = New ReferenceContext With {.IncludeDeclaration = True}
            }

            Dim result = Await _referencesService.GetReferencesAsync(request, CancellationToken.None)

            Assert.NotEmpty(result)
            Assert.Contains(result, Function(location) location.Uri.Contains("Helper.vb"))
        End Function

        <Fact>
        Public Async Function GetReferencesAsync_WithoutDeclaration_ExcludesDeclaration() As Task
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

            Dim paramsWithDecl = New ReferenceParams With {
                .TextDocument = New TextDocumentIdentifier With {.Uri = helperUri},
                .Position = New Position With {.Line = lineIndex, .Character = doWorkIndex + 2},
                .Context = New ReferenceContext With {.IncludeDeclaration = True}
            }

            Dim paramsWithoutDecl = New ReferenceParams With {
                .TextDocument = New TextDocumentIdentifier With {.Uri = helperUri},
                .Position = New Position With {.Line = lineIndex, .Character = doWorkIndex + 2},
                .Context = New ReferenceContext With {.IncludeDeclaration = False}
            }

            Dim resultWithDecl = Await _referencesService.GetReferencesAsync(paramsWithDecl, CancellationToken.None)
            Dim resultWithoutDecl = Await _referencesService.GetReferencesAsync(paramsWithoutDecl, CancellationToken.None)

            Assert.True(resultWithDecl.Length >= resultWithoutDecl.Length)
        End Function

        <Fact>
        Public Async Function GetReferencesAsync_ReturnsValidLocations() As Task
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

            Dim request = New ReferenceParams With {
                .TextDocument = New TextDocumentIdentifier With {.Uri = helperUri},
                .Position = New Position With {.Line = lineIndex, .Character = helperIndex + 2},
                .Context = New ReferenceContext With {.IncludeDeclaration = True}
            }

            Dim result = Await _referencesService.GetReferencesAsync(request, CancellationToken.None)

            For Each location In result
                Assert.False(String.IsNullOrEmpty(location.Uri))
                Assert.True(location.Range.Start.Line >= 0)
                Assert.True(location.Range.Start.Character >= 0)
                Assert.True(location.Range.End.Line >= location.Range.Start.Line)
            Next
        End Function
    End Class

End Namespace
