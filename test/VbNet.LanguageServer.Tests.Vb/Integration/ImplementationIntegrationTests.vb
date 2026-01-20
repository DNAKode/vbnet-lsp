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
    ''' Integration tests for ImplementationService with real VB.NET projects.
    ''' </summary>
    <Collection("MSBuild")>
    Public Class ImplementationIntegrationTests
        Implements IAsyncLifetime

        Private ReadOnly _workspaceManager As WorkspaceManager
        Private ReadOnly _documentManager As DocumentManager
        Private ReadOnly _implementationService As ImplementationService

        Private Shared ReadOnly TestProjectsRoot As String = GetTestProjectsRoot()

        Public Sub New()
            _workspaceManager = New WorkspaceManager(NullLogger(Of WorkspaceManager).Instance)
            _documentManager = New DocumentManager(_workspaceManager, NullLogger(Of DocumentManager).Instance)
            _implementationService = New ImplementationService(
                _workspaceManager,
                _documentManager,
                NullLogger(Of ImplementationService).Instance)
        End Sub

        Private Shared Function GetTestProjectsRoot() As String
            Dim assemblyLocation = GetType(ImplementationIntegrationTests).Assembly.Location
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
        Public Async Function GetImplementationAsync_ReturnsDerivedHelper() As Task
            Dim projectPath = Path.Combine(TestProjectsRoot, "SmallProject", "SmallProject.vbproj")
            Dim helperPath = Path.Combine(TestProjectsRoot, "SmallProject", "Helper.vb")

            If Not File.Exists(projectPath) OrElse Not File.Exists(helperPath) Then
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
            Dim lineIndex = Array.FindIndex(lines, Function(line) line.Contains("Class BaseHelper"))

            If lineIndex < 0 Then
                Return
            End If

            Dim nameIndex = lines(lineIndex).IndexOf("BaseHelper", StringComparison.Ordinal)

            Dim result = Await _implementationService.GetImplementationAsync(New ImplementationParams With {
                .TextDocument = New TextDocumentIdentifier With {.Uri = helperUri},
                .Position = New Position With {.Line = lineIndex, .Character = nameIndex + 2}
            }, CancellationToken.None)

            Assert.NotEmpty(result)
            Assert.Contains(result, Function(location) location.Uri.Contains("Helper.vb"))
        End Function
    End Class

End Namespace
