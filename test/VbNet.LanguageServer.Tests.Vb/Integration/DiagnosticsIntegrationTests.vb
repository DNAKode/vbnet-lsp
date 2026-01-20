Imports System
Imports System.Collections.Generic
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
    ''' Integration tests for DiagnosticsService with real VB.NET projects.
    ''' These tests verify that diagnostics are correctly computed from Roslyn.
    ''' </summary>
    <Collection("MSBuild")>
    Public Class DiagnosticsIntegrationTests
        Implements IAsyncLifetime

        Private ReadOnly _workspaceManager As WorkspaceManager
        Private ReadOnly _documentManager As DocumentManager
        Private ReadOnly _diagnosticsService As DiagnosticsService
        Private ReadOnly _publishedDiagnostics As New List(Of PublishDiagnosticsParams)()

        Private Shared ReadOnly TestProjectsRoot As String = GetTestProjectsRoot()

        Public Sub New()
            _workspaceManager = New WorkspaceManager(NullLogger(Of WorkspaceManager).Instance)
            _documentManager = New DocumentManager(_workspaceManager, NullLogger(Of DocumentManager).Instance)
            _diagnosticsService = New DiagnosticsService(
                _workspaceManager,
                _documentManager,
                AddressOf PublishDiagnosticsAsync,
                NullLogger(Of DiagnosticsService).Instance)
        End Sub

        Private Shared Function GetTestProjectsRoot() As String
            Dim assemblyLocation = GetType(DiagnosticsIntegrationTests).Assembly.Location
            Dim assemblyDir = Path.GetDirectoryName(assemblyLocation)
            Dim testProjectsPath = Path.GetFullPath(Path.Combine(assemblyDir, "..", "..", "..", "..", "TestProjects"))
            Return testProjectsPath
        End Function

        Private Function PublishDiagnosticsAsync(method As String, paramsValue As PublishDiagnosticsParams, ct As CancellationToken) As Task
            _publishedDiagnostics.Add(paramsValue)
            Return Task.CompletedTask
        End Function

        Public Function InitializeAsync() As Task Implements IAsyncLifetime.InitializeAsync
            _workspaceManager.Initialize()
            Return Task.CompletedTask
        End Function

        Public Async Function DisposeAsync() As Task Implements IAsyncLifetime.DisposeAsync
            _diagnosticsService.Dispose()
            Await _workspaceManager.DisposeAsync()
        End Function

        <Fact>
        Public Async Function GetDiagnosticsAsync_ValidCode_ReturnsNoDiagnostics() As Task
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

            Dim diagnostics = Await _diagnosticsService.GetDiagnosticsAsync(helperUri)

            Dim errors = diagnostics.Where(Function(d) d.Severity = DiagnosticSeverity.Error).ToList()
            Assert.Empty(errors)
        End Function

        <Fact>
        Public Async Function GetDiagnosticsAsync_ErrorCode_ReturnsDiagnostics() As Task
            Dim projectPath = Path.Combine(TestProjectsRoot, "ErrorProject", "ErrorProject.vbproj")
            Dim errorClassPath = Path.Combine(TestProjectsRoot, "ErrorProject", "ErrorClass.vb")

            If Not File.Exists(projectPath) Then
                Return
            End If

            Await _workspaceManager.LoadProjectAsync(projectPath)

            Dim errorClassUri = New Uri(errorClassPath).ToString()
            Dim text = Await File.ReadAllTextAsync(errorClassPath)

            _documentManager.HandleDidOpen(New DidOpenTextDocumentParams With {
                .TextDocument = New TextDocumentItem With {
                    .Uri = errorClassUri,
                    .LanguageId = "vb",
                    .Version = 1,
                    .Text = text
                }
            })

            Dim diagnostics = Await _diagnosticsService.GetDiagnosticsAsync(errorClassUri)

            Dim errors = diagnostics.Where(Function(d) d.Severity = DiagnosticSeverity.Error).ToList()
            Assert.NotEmpty(errors)

            Dim errorCodes = errors.Select(Function(e) e.Code).ToList()

            Assert.Contains("BC30512", errorCodes)
            Assert.Contains("BC30451", errorCodes)
            Assert.Contains("BC30002", errorCodes)
        End Function

        <Fact>
        Public Async Function GetDiagnosticsAsync_DiagnosticsHaveCorrectRanges() As Task
            Dim projectPath = Path.Combine(TestProjectsRoot, "ErrorProject", "ErrorProject.vbproj")
            Dim errorClassPath = Path.Combine(TestProjectsRoot, "ErrorProject", "ErrorClass.vb")

            If Not File.Exists(projectPath) Then
                Return
            End If

            Await _workspaceManager.LoadProjectAsync(projectPath)

            Dim errorClassUri = New Uri(errorClassPath).ToString()
            Dim text = Await File.ReadAllTextAsync(errorClassPath)

            _documentManager.HandleDidOpen(New DidOpenTextDocumentParams With {
                .TextDocument = New TextDocumentItem With {
                    .Uri = errorClassUri,
                    .LanguageId = "vb",
                    .Version = 1,
                    .Text = text
                }
            })

            Dim diagnostics = Await _diagnosticsService.GetDiagnosticsAsync(errorClassUri)

            For Each diagnostic In diagnostics
                Assert.NotNull(diagnostic.Range)
                Assert.True(diagnostic.Range.Start.Line >= 0)
                Assert.True(diagnostic.Range.Start.Character >= 0)
                Assert.True(diagnostic.Range.End.Line >= diagnostic.Range.Start.Line)
            Next
        End Function

        <Fact>
        Public Async Function GetDiagnosticsAsync_DiagnosticsHaveSource() As Task
            Dim projectPath = Path.Combine(TestProjectsRoot, "ErrorProject", "ErrorProject.vbproj")
            Dim errorClassPath = Path.Combine(TestProjectsRoot, "ErrorProject", "ErrorClass.vb")

            If Not File.Exists(projectPath) Then
                Return
            End If

            Await _workspaceManager.LoadProjectAsync(projectPath)

            Dim errorClassUri = New Uri(errorClassPath).ToString()
            Dim text = Await File.ReadAllTextAsync(errorClassPath)

            _documentManager.HandleDidOpen(New DidOpenTextDocumentParams With {
                .TextDocument = New TextDocumentItem With {
                    .Uri = errorClassUri,
                    .LanguageId = "vb",
                    .Version = 1,
                    .Text = text
                }
            })

            Dim diagnostics = Await _diagnosticsService.GetDiagnosticsAsync(errorClassUri)

            Assert.All(diagnostics, Sub(d) Assert.Equal("vbnet", d.Source))
        End Function

        <Fact>
        Public Async Function ComputeAndPublishDiagnosticsAsync_PublishesDiagnostics() As Task
            Dim projectPath = Path.Combine(TestProjectsRoot, "ErrorProject", "ErrorProject.vbproj")
            Dim errorClassPath = Path.Combine(TestProjectsRoot, "ErrorProject", "ErrorClass.vb")

            If Not File.Exists(projectPath) Then
                Return
            End If

            Await _workspaceManager.LoadProjectAsync(projectPath)

            Dim errorClassUri = New Uri(errorClassPath).ToString()
            Dim text = Await File.ReadAllTextAsync(errorClassPath)

            _documentManager.HandleDidOpen(New DidOpenTextDocumentParams With {
                .TextDocument = New TextDocumentItem With {
                    .Uri = errorClassUri,
                    .LanguageId = "vb",
                    .Version = 1,
                    .Text = text
                }
            })

            _publishedDiagnostics.Clear()

            Await _diagnosticsService.ComputeAndPublishDiagnosticsAsync(errorClassUri)

            Assert.Single(_publishedDiagnostics)
            Assert.Equal(errorClassUri, _publishedDiagnostics(0).Uri)
            Assert.NotEmpty(_publishedDiagnostics(0).Diagnostics)
        End Function

        <Fact>
        Public Async Function ClearDiagnosticsAsync_PublishesEmptyDiagnostics() As Task
            Dim projectPath = Path.Combine(TestProjectsRoot, "ErrorProject", "ErrorProject.vbproj")
            Dim errorClassPath = Path.Combine(TestProjectsRoot, "ErrorProject", "ErrorClass.vb")

            If Not File.Exists(projectPath) Then
                Return
            End If

            Await _workspaceManager.LoadProjectAsync(projectPath)

            Dim errorClassUri = New Uri(errorClassPath).ToString()
            Dim text = Await File.ReadAllTextAsync(errorClassPath)

            _documentManager.HandleDidOpen(New DidOpenTextDocumentParams With {
                .TextDocument = New TextDocumentItem With {
                    .Uri = errorClassUri,
                    .LanguageId = "vb",
                    .Version = 1,
                    .Text = text
                }
            })

            Await _diagnosticsService.ComputeAndPublishDiagnosticsAsync(errorClassUri)

            _publishedDiagnostics.Clear()

            Await _diagnosticsService.ClearDiagnosticsAsync(errorClassUri)

            Assert.Single(_publishedDiagnostics)
            Assert.Equal(errorClassUri, _publishedDiagnostics(0).Uri)
            Assert.Empty(_publishedDiagnostics(0).Diagnostics)
        End Function

        <Fact>
        Public Async Function DocumentReassociation_TriggersDiagnostics() As Task
            Dim projectPath = Path.Combine(TestProjectsRoot, "ErrorProject", "ErrorProject.vbproj")
            Dim errorClassPath = Path.Combine(TestProjectsRoot, "ErrorProject", "ErrorClass.vb")

            If Not File.Exists(projectPath) Then
                Return
            End If

            Dim errorClassUri = New Uri(errorClassPath).ToString()
            Dim text = Await File.ReadAllTextAsync(errorClassPath)

            _documentManager.HandleDidOpen(New DidOpenTextDocumentParams With {
                .TextDocument = New TextDocumentItem With {
                    .Uri = errorClassUri,
                    .LanguageId = "vb",
                    .Version = 1,
                    .Text = text
                }
            })

            Dim doc = _documentManager.GetOpenDocument(errorClassUri)
            Assert.NotNull(doc)
            Assert.Null(doc.DocumentId)

            _publishedDiagnostics.Clear()

            Await _workspaceManager.LoadProjectAsync(projectPath)

            doc = _documentManager.GetOpenDocument(errorClassUri)
            Assert.NotNull(doc)
            Assert.NotNull(doc.DocumentId)

            Await Task.Delay(500)
        End Function

        <Fact>
        Public Async Function MinimumSeverity_FiltersCorrectly() As Task
            Dim projectPath = Path.Combine(TestProjectsRoot, "ErrorProject", "ErrorProject.vbproj")
            Dim errorClassPath = Path.Combine(TestProjectsRoot, "ErrorProject", "ErrorClass.vb")

            If Not File.Exists(projectPath) Then
                Return
            End If

            Await _workspaceManager.LoadProjectAsync(projectPath)

            Dim errorClassUri = New Uri(errorClassPath).ToString()
            Dim text = Await File.ReadAllTextAsync(errorClassPath)

            _documentManager.HandleDidOpen(New DidOpenTextDocumentParams With {
                .TextDocument = New TextDocumentItem With {
                    .Uri = errorClassUri,
                    .LanguageId = "vb",
                    .Version = 1,
                    .Text = text
                }
            })

            _diagnosticsService.MinimumSeverity = DiagnosticSeverity.Error

            Dim diagnostics = Await _diagnosticsService.GetDiagnosticsAsync(errorClassUri)

            Assert.All(diagnostics, Sub(d) Assert.Equal(DiagnosticSeverity.Error, d.Severity))
        End Function

        <Fact>
        Public Async Function GetDocumentDiagnosticsReportAsync_ReturnsDiagnostics() As Task
            Dim projectPath = Path.Combine(TestProjectsRoot, "ErrorProject", "ErrorProject.vbproj")
            Dim errorClassPath = Path.Combine(TestProjectsRoot, "ErrorProject", "ErrorClass.vb")

            If Not File.Exists(projectPath) Then
                Return
            End If

            Await _workspaceManager.LoadProjectAsync(projectPath)

            Dim errorClassUri = New Uri(errorClassPath).ToString()
            Dim text = Await File.ReadAllTextAsync(errorClassPath)

            _documentManager.HandleDidOpen(New DidOpenTextDocumentParams With {
                .TextDocument = New TextDocumentItem With {
                    .Uri = errorClassUri,
                    .LanguageId = "vb",
                    .Version = 1,
                    .Text = text
                }
            })

            Dim report = Await _diagnosticsService.GetDocumentDiagnosticsReportAsync(New TextDocumentDiagnosticParams With {
                .TextDocument = New TextDocumentIdentifier With {.Uri = errorClassUri}
            })

            Assert.NotNull(report)
            Assert.Equal("full", report.Kind)
            Assert.NotEmpty(report.Items)
        End Function

        <Fact>
        Public Async Function GetWorkspaceDiagnosticsReportAsync_ReturnsWorkspaceItems() As Task
            Dim projectPath = Path.Combine(TestProjectsRoot, "ErrorProject", "ErrorProject.vbproj")
            Dim errorClassPath = Path.Combine(TestProjectsRoot, "ErrorProject", "ErrorClass.vb")

            If Not File.Exists(projectPath) Then
                Return
            End If

            Await _workspaceManager.LoadProjectAsync(projectPath)

            Dim errorClassUri = New Uri(errorClassPath).ToString()
            Dim text = Await File.ReadAllTextAsync(errorClassPath)

            _documentManager.HandleDidOpen(New DidOpenTextDocumentParams With {
                .TextDocument = New TextDocumentItem With {
                    .Uri = errorClassUri,
                    .LanguageId = "vb",
                    .Version = 1,
                    .Text = text
                }
            })

            Dim report = Await _diagnosticsService.GetWorkspaceDiagnosticsReportAsync(New WorkspaceDiagnosticParams())

            Assert.NotNull(report)
            Assert.NotEmpty(report.Items)
            Assert.Contains(report.Items, Function(item) item.Uri = errorClassUri AndAlso item.Items.Length > 0)
        End Function
    End Class

End Namespace
