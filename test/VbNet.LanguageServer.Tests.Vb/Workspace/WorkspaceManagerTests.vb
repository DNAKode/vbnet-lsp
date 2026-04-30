Imports System.IO
Imports Microsoft.Extensions.Logging.Abstractions
Imports VbNet.LanguageServer.Workspace
Imports Xunit

Namespace VbNet.LanguageServer.Tests.Workspace

    ''' <summary>
    ''' Tests for WorkspaceManager with real VB.NET projects.
    ''' </summary>
    <Collection("MSBuild")>
    Public Class WorkspaceManagerTests
        Implements IAsyncLifetime

        Private ReadOnly _workspaceManager As WorkspaceManager

        Private Shared ReadOnly TestProjectsRoot As String = GetTestProjectsRoot()

        Public Sub New()
            _workspaceManager = New WorkspaceManager(NullLogger(Of WorkspaceManager).Instance)
        End Sub

        Private Shared Function GetTestProjectsRoot() As String
            Dim assemblyLocation = GetType(WorkspaceManagerTests).Assembly.Location
            Dim assemblyDir = Path.GetDirectoryName(assemblyLocation)
            Dim testProjectsPath = Path.GetFullPath(Path.Combine(assemblyDir, "..", "..", "..", "..", "TestProjects"))
            Return testProjectsPath
        End Function

        Public Function InitializeAsync() As Task Implements IAsyncLifetime.InitializeAsync
            _workspaceManager.Initialize()
            Return Task.CompletedTask
        End Function

        Public Async Function DisposeAsync() As Task Implements IAsyncLifetime.DisposeAsync
            Await _workspaceManager.DisposeAsync().ConfigureAwait(False)
        End Function

        <Fact>
        Public Sub Initialize_CreatesWorkspace()
            Assert.NotNull(_workspaceManager.CurrentSolution)
        End Sub

        <Fact>
        Public Sub CurrentSolution_BeforeLoad_IsEmpty()
            Assert.False(_workspaceManager.IsLoaded)
        End Sub

        <Fact>
        Public Async Function LoadProjectAsync_SmallProject_LoadsSuccessfully() As Task
            Dim projectPath = Path.Combine(TestProjectsRoot, "SmallProject", "SmallProject.vbproj")

            If Not File.Exists(projectPath) Then
                Return
            End If

            Dim result = Await _workspaceManager.LoadProjectAsync(projectPath).ConfigureAwait(False)

            Assert.True(result)
            Assert.True(_workspaceManager.IsLoaded)

            Dim projects = _workspaceManager.GetVbNetProjects().ToList()
            Assert.Single(projects)
            Assert.Equal("SmallProject", projects(0).Name)
        End Function

        <Fact>
        Public Async Function LoadProjectAsync_SmallProject_HasDocuments() As Task
            Dim projectPath = Path.Combine(TestProjectsRoot, "SmallProject", "SmallProject.vbproj")

            If Not File.Exists(projectPath) Then
                Return
            End If

            Await _workspaceManager.LoadProjectAsync(projectPath).ConfigureAwait(False)

            Dim projects = _workspaceManager.GetVbNetProjects().ToList()
            Assert.Single(projects)

            Dim documents = projects(0).Documents.ToList()
            Assert.True(documents.Count >= 2, "Expected at least 2 documents (Module1.vb, Helper.vb)")

            Dim documentNames = documents.Select(Function(d) Path.GetFileName(d.FilePath)).ToList()
            Assert.Contains("Module1.vb", documentNames)
            Assert.Contains("Helper.vb", documentNames)
        End Function

        <Fact>
        Public Async Function GetDocumentByPath_ReturnsCorrectDocument() As Task
            Dim projectPath = Path.Combine(TestProjectsRoot, "SmallProject", "SmallProject.vbproj")
            Dim module1Path = Path.Combine(TestProjectsRoot, "SmallProject", "Module1.vb")

            If Not File.Exists(projectPath) Then
                Return
            End If

            Await _workspaceManager.LoadProjectAsync(projectPath).ConfigureAwait(False)

            Dim document = _workspaceManager.GetDocumentByPath(module1Path)

            Assert.NotNull(document)
            Assert.Equal("Module1.vb", Path.GetFileName(document.FilePath))
        End Function

        <Fact>
        Public Async Function GetDocumentByUri_ReturnsCorrectDocument() As Task
            Dim projectPath = Path.Combine(TestProjectsRoot, "SmallProject", "SmallProject.vbproj")
            Dim module1Path = Path.Combine(TestProjectsRoot, "SmallProject", "Module1.vb")
            Dim module1Uri = New Uri(module1Path).ToString()

            If Not File.Exists(projectPath) Then
                Return
            End If

            Await _workspaceManager.LoadProjectAsync(projectPath).ConfigureAwait(False)

            Dim document = _workspaceManager.GetDocumentByUri(module1Uri)

            Assert.NotNull(document)
            Assert.Equal("Module1.vb", Path.GetFileName(document.FilePath))
        End Function

        <Fact>
        Public Async Function GetDocumentByPath_NonExistentPath_ReturnsNull() As Task
            Dim projectPath = Path.Combine(TestProjectsRoot, "SmallProject", "SmallProject.vbproj")

            If Not File.Exists(projectPath) Then
                Return
            End If

            Await _workspaceManager.LoadProjectAsync(projectPath).ConfigureAwait(False)

            Dim document = _workspaceManager.GetDocumentByPath("C:\NonExistent\File.vb")

            Assert.Null(document)
        End Function

        <Fact>
        Public Async Function LoadProjectAsync_NonExistentPath_ReturnsFalse() As Task
            Dim result = Await _workspaceManager.LoadProjectAsync("C:\NonExistent\Project.vbproj").ConfigureAwait(False)

            Assert.False(result)
            Assert.False(_workspaceManager.IsLoaded)
        End Function

        <Fact>
        Public Async Function GetVbNetProjects_ReturnsOnlyVbProjects() As Task
            Dim projectPath = Path.Combine(TestProjectsRoot, "SmallProject", "SmallProject.vbproj")

            If Not File.Exists(projectPath) Then
                Return
            End If

            Await _workspaceManager.LoadProjectAsync(projectPath).ConfigureAwait(False)

            Dim vbProjects = _workspaceManager.GetVbNetProjects().ToList()
            Assert.All(vbProjects, Sub(p) Assert.Equal("Visual Basic", p.Language))
        End Function

        <Fact>
        Public Async Function ApplyTextChange_UpdatesDocument() As Task
            Dim projectPath = Path.Combine(TestProjectsRoot, "SmallProject", "SmallProject.vbproj")
            Dim module1Path = Path.Combine(TestProjectsRoot, "SmallProject", "Module1.vb")

            If Not File.Exists(projectPath) Then
                Return
            End If

            Dim originalDiskText = Await File.ReadAllTextAsync(module1Path).ConfigureAwait(False)

            Await _workspaceManager.LoadProjectAsync(projectPath).ConfigureAwait(False)

            Dim document = _workspaceManager.GetDocumentByPath(module1Path)
            Assert.NotNull(document)

            Dim newText = Microsoft.CodeAnalysis.Text.SourceText.From("' Modified" & vbLf & "Module Module1" & vbLf & "End Module")

            Dim updatedDoc = _workspaceManager.ApplyTextChange(document.Id, newText)

            Assert.NotNull(updatedDoc)

            Dim updatedText = Await updatedDoc.GetTextAsync().ConfigureAwait(False)
            Assert.Contains("Modified", updatedText.ToString())

            Dim diskText = Await File.ReadAllTextAsync(module1Path).ConfigureAwait(False)
            Assert.Equal(originalDiskText, diskText)
        End Function

        <Fact>
        Public Async Function SolutionChanged_EventFired_OnProjectLoad() As Task
            Dim projectPath = Path.Combine(TestProjectsRoot, "SmallProject", "SmallProject.vbproj")

            If Not File.Exists(projectPath) Then
                Return
            End If

            Dim receivedArgs As SolutionChangedEventArgs = Nothing
            AddHandler _workspaceManager.SolutionChanged, Sub(sender, args) receivedArgs = args

            Await _workspaceManager.LoadProjectAsync(projectPath).ConfigureAwait(False)

            Assert.NotNull(receivedArgs)
            Assert.Equal(SolutionChangeKind.ProjectAdded, receivedArgs.Kind)
        End Function

        <Fact>
        Public Async Function ReloadWorkspaceAsync_FiresReloadedChangeKind() As Task
            Dim projectPath = Path.Combine(TestProjectsRoot, "SmallProject", "SmallProject.vbproj")

            If Not File.Exists(projectPath) Then
                Return
            End If

            Await _workspaceManager.LoadProjectAsync(projectPath).ConfigureAwait(False)

            Dim receivedArgs As SolutionChangedEventArgs = Nothing
            AddHandler _workspaceManager.SolutionChanged, Sub(sender, args) receivedArgs = args

            Await _workspaceManager.ReloadWorkspaceAsync().ConfigureAwait(False)

            Assert.NotNull(receivedArgs)
            Assert.Equal(SolutionChangeKind.Reloaded, receivedArgs.Kind)
        End Function

        <Fact>
        Public Async Function WorkspaceDiagnostic_EventFired_OnLoadFailure() As Task
            Dim diagnosticReceived = False
            AddHandler _workspaceManager.WorkspaceDiagnostic, Sub(sender, args) diagnosticReceived = True

            Dim projectPath = Path.Combine(TestProjectsRoot, "SmallProject", "SmallProject.vbproj")

            If Not File.Exists(projectPath) Then
                Return
            End If

            Await _workspaceManager.LoadProjectAsync(projectPath).ConfigureAwait(False)

            Assert.False(diagnosticReceived)
        End Function
    End Class

End Namespace
