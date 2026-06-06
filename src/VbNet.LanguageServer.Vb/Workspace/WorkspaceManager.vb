' WorkspaceManager - Manages MSBuildWorkspace lifecycle and solution/project loading
' Workspace Layer as defined in docs/architecture.md Section 5.3

Imports Microsoft.CodeAnalysis
Imports Microsoft.CodeAnalysis.MSBuild
Imports Microsoft.CodeAnalysis.Text
Imports Microsoft.CodeAnalysis.VisualBasic
Imports Microsoft.Extensions.Logging

Namespace Workspace

    ''' <summary>
    ''' Manages the Roslyn MSBuildWorkspace lifecycle.
    ''' Handles solution/project loading, workspace events, and provides access to the current solution.
    ''' </summary>
    Public NotInheritable Class WorkspaceManager
        Implements IAsyncDisposable

        Private ReadOnly _logger As ILogger(Of WorkspaceManager)
        Private ReadOnly _loadLock As SemaphoreSlim = New SemaphoreSlim(1, 1)
        Private ReadOnly _initialLoadTcs As TaskCompletionSource(Of Boolean) =
            New TaskCompletionSource(Of Boolean)(TaskCreationOptions.RunContinuationsAsynchronously)
        Private _initialLoadSignaled As Integer

        Private _workspace As MSBuildWorkspace
        Private _currentSolution As Solution
        Private _loadedSolutionPath As String
        Private ReadOnly _loadedProjectPaths As New List(Of String)()

        ''' <summary>
        ''' Event raised when workspace diagnostics occur during loading.
        ''' </summary>
        Public Event WorkspaceDiagnostic As EventHandler(Of WorkspaceDiagnosticEventArgs)

        ''' <summary>
        ''' Event raised when the solution changes (reload, project added/removed).
        ''' </summary>
        Public Event SolutionChanged As EventHandler(Of SolutionChangedEventArgs)

        Public Sub New(logger As ILogger(Of WorkspaceManager))
            If logger Is Nothing Then
                Throw New ArgumentNullException(NameOf(logger))
            End If

            _logger = logger
        End Sub

        ''' <summary>
        ''' Gets the current solution snapshot. Thread-safe due to immutability.
        ''' </summary>
        Public ReadOnly Property CurrentSolution As Solution
            Get
                Return If(_currentSolution, _workspace?.CurrentSolution)
            End Get
        End Property

        ''' <summary>
        ''' Gets whether a solution or project is currently loaded.
        ''' </summary>
        Public ReadOnly Property IsLoaded As Boolean
            Get
                Dim solution = CurrentSolution
                Return solution IsNot Nothing AndAlso solution.ProjectIds.Count > 0
            End Get
        End Property

        ''' <summary>
        ''' Gets the path of the loaded solution, if any.
        ''' </summary>
        Public ReadOnly Property LoadedSolutionPath As String
            Get
                Return _loadedSolutionPath
            End Get
        End Property

        ''' <summary>
        ''' Checks whether a project path is currently loaded.
        ''' </summary>
        Public Function IsProjectLoaded(projectPath As String) As Boolean
            Dim normalizedPath = NormalizePath(projectPath)
            Return _loadedProjectPaths.Any(Function(p) String.Equals(NormalizePath(p), normalizedPath, StringComparison.OrdinalIgnoreCase))
        End Function

        ''' <summary>
        ''' Initializes the MSBuildWorkspace. Must be called before loading solutions/projects.
        ''' </summary>
        Public Sub Initialize()
            If _workspace IsNot Nothing Then
                _logger.LogWarning("WorkspaceManager already initialized")
                Return
            End If

            _logger.LogDebug("Creating MSBuildWorkspace")

            Dim properties As New Dictionary(Of String, String) From {
                {"DesignTimeBuild", "true"},
                {"BuildingInsideVisualStudio", "true"},
                {"BuildProjectReferences", "false"},
                {"SkipCompilerExecution", "true"},
                {"ProvideCommandLineArgs", "true"}
            }

            If Not OperatingSystem.IsWindows() Then
                Environment.SetEnvironmentVariable("NUGET_FALLBACK_PACKAGES", String.Empty)
                properties("RestoreFallbackFolders") = String.Empty
                _logger.LogInformation("Disabled NuGet fallback package folders for non-Windows hosts.")
            End If

            _workspace = MSBuildWorkspace.Create(properties)
            _currentSolution = _workspace.CurrentSolution
            AddHandler _workspace.WorkspaceFailed, AddressOf OnWorkspaceFailed

            _logger.LogInformation("MSBuildWorkspace created successfully")
        End Sub

        ''' <summary>
        ''' Loads a solution file.
        ''' </summary>
        Public Async Function LoadSolutionAsync(solutionPath As String, Optional cancellationToken As CancellationToken = Nothing, Optional changeKind As SolutionChangeKind = SolutionChangeKind.Loaded) As Task(Of Boolean)
            If _workspace Is Nothing Then
                Throw New InvalidOperationException("WorkspaceManager not initialized. Call Initialize() first.")
            End If

            If Not File.Exists(solutionPath) Then
                _logger.LogError("Solution file not found: {Path}", solutionPath)
                Return False
            End If

            Await _loadLock.WaitAsync(cancellationToken).ConfigureAwait(False)
            Try
                _logger.LogInformation("Loading solution: {Path}", solutionPath)

                Dim solution = Await _workspace.OpenSolutionAsync(solutionPath, cancellationToken:=cancellationToken).ConfigureAwait(False)
                _currentSolution = solution

                _loadedSolutionPath = solutionPath
                _loadedProjectPaths.Clear()

                Dim vbProjects = solution.Projects.Where(Function(p) p.Language = LanguageNames.VisualBasic).ToList()
                Dim csharpProjects = solution.Projects.Where(Function(p) p.Language = LanguageNames.CSharp).ToList()

                _logger.LogInformation("Solution loaded: {VbCount} VB.NET projects, {CsCount} C# projects", vbProjects.Count, csharpProjects.Count)

                For Each project In vbProjects
                    _logger.LogDebug("  VB.NET project: {Name}", project.Name)
                    If project.FilePath IsNot Nothing Then
                        _loadedProjectPaths.Add(project.FilePath)
                    End If
                Next

                If csharpProjects.Count > 0 Then
                    _logger.LogInformation("Note: C# projects loaded but not served (VB.NET only in current phase)")
                End If

                RaiseEvent SolutionChanged(Me, New SolutionChangedEventArgs(_currentSolution, changeKind))

                Return vbProjects.Count > 0
            Catch ex As Exception
                _logger.LogError(ex, "Failed to load solution: {Path}", solutionPath)
                If solutionPath.EndsWith(".slnx", StringComparison.OrdinalIgnoreCase) Then
                    Return LoadLegacyProjectsFromSolution(solutionPath, changeKind)
                End If

                Return False
            Finally
                _loadLock.Release()
            End Try
        End Function

        ''' <summary>
        ''' Loads a single project file.
        ''' </summary>
        Public Async Function LoadProjectAsync(projectPath As String, Optional cancellationToken As CancellationToken = Nothing, Optional changeKind As SolutionChangeKind = SolutionChangeKind.ProjectAdded) As Task(Of Boolean)
            If _workspace Is Nothing Then
                Throw New InvalidOperationException("WorkspaceManager not initialized. Call Initialize() first.")
            End If

            If Not File.Exists(projectPath) Then
                _logger.LogError("Project file not found: {Path}", projectPath)
                Return False
            End If

            Await _loadLock.WaitAsync(cancellationToken).ConfigureAwait(False)
            Try
                If IsProjectLoaded(projectPath) Then
                    _logger.LogDebug("Project already loaded, skipping: {Path}", projectPath)
                    If changeKind = SolutionChangeKind.Reloaded Then
                        RaiseEvent SolutionChanged(Me, New SolutionChangedEventArgs(CurrentSolution, changeKind))
                    End If
                    Return True
                End If

                _logger.LogInformation("Loading project: {Path}", projectPath)

                Dim project = Await _workspace.OpenProjectAsync(projectPath, cancellationToken:=cancellationToken).ConfigureAwait(False)
                _currentSolution = _workspace.CurrentSolution

                If project.Language <> LanguageNames.VisualBasic Then
                    _logger.LogWarning("Project is not VB.NET: {Name} ({Language})", project.Name, project.Language)
                    Return False
                End If

                If Not IsProjectLoaded(projectPath) Then
                    _loadedProjectPaths.Add(projectPath)
                End If

                _logger.LogInformation("Project loaded: {Name} ({DocumentCount} documents)", project.Name, project.DocumentIds.Count)

                RaiseEvent SolutionChanged(Me, New SolutionChangedEventArgs(CurrentSolution, changeKind))

                Return True
            Catch ex As Exception
                If TypeOf ex Is ArgumentException AndAlso ex.Message.Contains("already part of the workspace", StringComparison.OrdinalIgnoreCase) Then
                    _logger.LogDebug(ex, "Project already part of workspace: {Path}", projectPath)
                Else
                    _logger.LogError(ex, "Failed to load project: {Path}", projectPath)
                End If
                Return LoadLegacyProject(projectPath, changeKind)
            Finally
                _loadLock.Release()
            End Try
        End Function

        Private Function LoadLegacyProjectsFromSolution(solutionPath As String, changeKind As SolutionChangeKind) As Boolean
            Dim projectPaths = LegacyVbProjectReader.GetProjectPathsFromSlnx(solutionPath)
            If projectPaths.Length = 0 Then
                Return False
            End If

            Dim loaded = False
            For Each projectPath In projectPaths
                loaded = LoadLegacyProject(projectPath, changeKind, raiseEventOnLoad:=False) OrElse loaded
            Next

            If loaded Then
                _loadedSolutionPath = solutionPath
                RaiseEvent SolutionChanged(Me, New SolutionChangedEventArgs(CurrentSolution, changeKind))
            End If

            Return loaded
        End Function

        Private Function LoadLegacyProject(projectPath As String, changeKind As SolutionChangeKind, Optional raiseEventOnLoad As Boolean = True) As Boolean
            Dim legacyProject = LegacyVbProjectReader.TryRead(projectPath)
            If legacyProject Is Nothing Then
                Return False
            End If

            If IsProjectLoaded(projectPath) Then
                Return True
            End If

            Dim newProjectId As ProjectId = ProjectId.CreateNewId(debugName:=legacyProject.AssemblyName)
            Dim compilationOptions = New VisualBasicCompilationOptions(legacyProject.OutputKind).
                WithRootNamespace(If(legacyProject.RootNamespace, String.Empty)).
                WithOptionStrict(legacyProject.OptionStrict).
                WithOptionInfer(legacyProject.OptionInfer).
                WithOptionExplicit(legacyProject.OptionExplicit).
                WithOptionCompareText(legacyProject.OptionCompareText).
                WithGlobalImports(legacyProject.GlobalImports)

            Dim projectInfo As ProjectInfo = ProjectInfo.Create(
                newProjectId,
                VersionStamp.Create(),
                legacyProject.AssemblyName,
                legacyProject.AssemblyName,
                LanguageNames.VisualBasic,
                filePath:=legacyProject.ProjectPath,
                compilationOptions:=compilationOptions,
                metadataReferences:=legacyProject.References)

            Dim solution = CurrentSolution.AddProject(projectInfo)

            For Each documentPath In legacyProject.Documents
                Dim sourceText As SourceText = SourceText.From(File.ReadAllText(documentPath))
                solution = solution.AddDocument(
                    DocumentId.CreateNewId(newProjectId, debugName:=Path.GetFileName(documentPath)),
                    Path.GetFileName(documentPath),
                    sourceText,
                    filePath:=documentPath)
            Next

            If Not String.IsNullOrWhiteSpace(legacyProject.MyType) Then
                solution = solution.AddDocument(
                    DocumentId.CreateNewId(newProjectId, debugName:="LegacyMyApplication.g.vb"),
                    "LegacyMyApplication.g.vb",
                    SourceText.From(GetLegacyMyApplicationSource()))
            End If

            _currentSolution = solution
            _loadedProjectPaths.Add(legacyProject.ProjectPath)

            For Each referencedProjectPath In legacyProject.ProjectReferences
                If Not IsProjectLoaded(referencedProjectPath) Then
                    LoadLegacyProject(referencedProjectPath, changeKind, raiseEventOnLoad:=False)
                End If

                Dim referencedProject = GetProjectByPath(referencedProjectPath)
                If referencedProject IsNot Nothing Then
                    _currentSolution = CurrentSolution.AddProjectReference(newProjectId, New ProjectReference(referencedProject.Id))
                End If
            Next

            _logger.LogInformation("Legacy VB.NET project loaded: {Name} ({DocumentCount} documents)", legacyProject.AssemblyName, legacyProject.Documents.Length)
            ReportLegacyProjectWarnings(legacyProject)

            If raiseEventOnLoad Then
                RaiseEvent SolutionChanged(Me, New SolutionChangedEventArgs(CurrentSolution, changeKind))
            End If

            Return True
        End Function

        Private Sub ReportLegacyProjectWarnings(legacyProject As LegacyVbProjectInfo)
            Dim projectName = Path.GetFileName(legacyProject.ProjectPath)
            RaiseWorkspaceWarning(
                $"Loaded legacy non-SDK VB.NET project '{projectName}' in limited fallback mode. For best language-server support, consider converting to an SDK-style project file; SDK-style projects can still target .NET Framework, for example TargetFramework=net48.")

            For Each warning In legacyProject.Warnings
                RaiseWorkspaceWarning($"Legacy project '{projectName}': {warning}")
            Next
        End Sub

        Private Sub RaiseWorkspaceWarning(message As String)
            _logger.LogWarning("{Message}", message)
            RaiseEvent WorkspaceDiagnostic(
                Me,
                New WorkspaceDiagnosticEventArgs(New WorkspaceDiagnostic(WorkspaceDiagnosticKind.Warning, message)))
        End Sub

        Private Shared Function GetLegacyMyApplicationSource() As String
            Return String.Join(
                Environment.NewLine,
                "Namespace Global.My",
                "    Friend Module MyProject",
                "        Friend ReadOnly Property Application As LegacyApplicationShim",
                "            Get",
                "                Return Nothing",
                "            End Get",
                "        End Property",
                "    End Module",
                "",
                "    Friend Class LegacyApplicationShim",
                "        Friend ReadOnly Property Info As LegacyAssemblyInfoShim",
                "            Get",
                "                Return Nothing",
                "            End Get",
                "        End Property",
                "    End Class",
                "",
                "    Friend Class LegacyAssemblyInfoShim",
                "        Friend ReadOnly Property Title As String",
                "            Get",
                "                Return Nothing",
                "            End Get",
                "        End Property",
                "",
                "        Friend ReadOnly Property Version As System.Version",
                "            Get",
                "                Return Nothing",
                "            End Get",
                "        End Property",
                "",
                "        Friend ReadOnly Property AssemblyName As String",
                "            Get",
                "                Return Nothing",
                "            End Get",
                "        End Property",
                "",
                "        Friend ReadOnly Property CompanyName As String",
                "            Get",
                "                Return Nothing",
                "            End Get",
                "        End Property",
                "",
                "        Friend ReadOnly Property Copyright As String",
                "            Get",
                "                Return Nothing",
                "            End Get",
                "        End Property",
                "",
                "        Friend ReadOnly Property Description As String",
                "            Get",
                "                Return Nothing",
                "            End Get",
                "        End Property",
                "",
                "        Friend ReadOnly Property ProductName As String",
                "            Get",
                "                Return Nothing",
                "            End Get",
                "        End Property",
                "",
                "        Friend ReadOnly Property Trademark As String",
                "            Get",
                "                Return Nothing",
                "            End Get",
                "        End Property",
                "    End Class",
                "End Namespace")
        End Function

        ''' <summary>
        ''' Signals that the initial workspace load attempt has completed.
        ''' </summary>
        Public Sub SignalInitialLoadCompleted(loaded As Boolean)
            If Interlocked.Exchange(_initialLoadSignaled, 1) = 1 Then
                Return
            End If

            _initialLoadTcs.TrySetResult(loaded)
        End Sub

        ''' <summary>
        ''' Waits for the initial workspace load attempt to complete or time out.
        ''' </summary>
        Public Async Function WaitForInitialLoadAsync(timeout As TimeSpan, cancellationToken As CancellationToken) As Task(Of Boolean)
            If _initialLoadTcs.Task.IsCompleted Then
                Return Await _initialLoadTcs.Task.ConfigureAwait(False)
            End If

            Dim completed = Await Task.WhenAny(_initialLoadTcs.Task, Task.Delay(timeout, cancellationToken)).ConfigureAwait(False)
            If completed Is _initialLoadTcs.Task Then
                Return Await _initialLoadTcs.Task.ConfigureAwait(False)
            End If

            Return False
        End Function

        ''' <summary>
        ''' Gets a document by its file path URI.
        ''' </summary>
        Public Function GetDocumentByUri(uri As String) As Document
            If _workspace Is Nothing Then
                Return Nothing
            End If

            Dim filePath = UriToFilePath(uri)
            If filePath Is Nothing Then
                Return Nothing
            End If

            Return GetDocumentByPath(filePath)
        End Function

        ''' <summary>
        ''' Gets a document by its file path.
        ''' </summary>
        Public Function GetDocumentByPath(filePath As String) As Document
            If _workspace Is Nothing Then
                Return Nothing
            End If

            Dim solution = CurrentSolution
            Dim normalizedPath = Path.GetFullPath(filePath)

            For Each projectId In solution.ProjectIds
                Dim project = solution.GetProject(projectId)
                If project Is Nothing Then
                    Continue For
                End If

                For Each documentId In project.DocumentIds
                    Dim document = project.GetDocument(documentId)
                    If document?.FilePath IsNot Nothing AndAlso
                        String.Equals(Path.GetFullPath(document.FilePath), normalizedPath, StringComparison.OrdinalIgnoreCase) Then
                        Return document
                    End If
                Next
            Next

            Return Nothing
        End Function

        ''' <summary>
        ''' Gets a project by its file path.
        ''' </summary>
        Public Function GetProjectByPath(projectPath As String) As Project
            If _workspace Is Nothing Then
                Return Nothing
            End If

            Dim solution = CurrentSolution
            Dim normalizedPath = Path.GetFullPath(projectPath)

            For Each projectId In solution.ProjectIds
                Dim project = solution.GetProject(projectId)
                If project?.FilePath IsNot Nothing AndAlso
                    String.Equals(Path.GetFullPath(project.FilePath), normalizedPath, StringComparison.OrdinalIgnoreCase) Then
                    Return project
                End If
            Next

            Return Nothing
        End Function

        ''' <summary>
        ''' Gets all VB.NET projects in the workspace.
        ''' </summary>
        Public Function GetVbNetProjects() As IEnumerable(Of Project)
            If _workspace Is Nothing Then
                Return Enumerable.Empty(Of Project)()
            End If

            Return CurrentSolution.Projects.Where(Function(p) p.Language = LanguageNames.VisualBasic)
        End Function

        ''' <summary>
        ''' Applies a text change to a document and returns the updated document.
        ''' </summary>
        Public Function ApplyTextChange(documentId As DocumentId, newText As SourceText) As Document
            If _workspace Is Nothing Then
                Return Nothing
            End If

            Dim solution = CurrentSolution
            Dim document = solution.GetDocument(documentId)
            If document Is Nothing Then
                Return Nothing
            End If

            ' Keep editor buffer changes in-memory. MSBuildWorkspace.TryApplyChanges can write back to disk.
            _currentSolution = solution.WithDocumentText(documentId, newText)
            Return _currentSolution.GetDocument(documentId)
        End Function

        ''' <summary>
        ''' Converts a file:// URI to a file path.
        ''' </summary>
        Private Shared Function UriToFilePath(uri As String) As String
            If String.IsNullOrEmpty(uri) Then
                Return Nothing
            End If

            Try
                Dim parsedUri As New Uri(uri)
                If parsedUri.IsFile Then
                    Dim localPath = parsedUri.LocalPath

                    If localPath.Length >= 3 AndAlso localPath(0) = "/"c AndAlso Char.IsLetter(localPath(1)) AndAlso localPath(2) = ":"c Then
                        localPath = localPath.Substring(1)
                    End If

                    Return localPath
                End If
            Catch ex As UriFormatException
                Return uri
            End Try

            Return Nothing
        End Function

        ''' <summary>
        ''' Reloads the currently loaded solution or projects.
        ''' </summary>
        Public Async Function ReloadWorkspaceAsync(Optional cancellationToken As CancellationToken = Nothing) As Task(Of Boolean)
            If Not String.IsNullOrEmpty(_loadedSolutionPath) Then
                Return Await LoadSolutionAsync(_loadedSolutionPath, cancellationToken, SolutionChangeKind.Reloaded).ConfigureAwait(False)
            End If

            If _loadedProjectPaths.Count = 0 Then
                Return False
            End If

            Dim reloadedAny = False
            For Each projectPath In _loadedProjectPaths.ToList()
                Dim reloaded = Await LoadProjectAsync(projectPath, cancellationToken, SolutionChangeKind.Reloaded).ConfigureAwait(False)
                reloadedAny = reloadedAny OrElse reloaded
            Next

            If Not reloadedAny AndAlso CurrentSolution?.ProjectIds.Count > 0 Then
                RaiseEvent SolutionChanged(Me, New SolutionChangedEventArgs(CurrentSolution, SolutionChangeKind.Reloaded))
            End If

            Return reloadedAny
        End Function

        Private Shared Function NormalizePath(inputPath As String) As String
            Return System.IO.Path.GetFullPath(inputPath)
        End Function

        Private Sub OnWorkspaceFailed(sender As Object, e As WorkspaceDiagnosticEventArgs)
            Dim level = If(e.Diagnostic.Kind = WorkspaceDiagnosticKind.Failure, LogLevel.Error, LogLevel.Warning)
            _logger.Log(level, "Workspace diagnostic: {Message}", e.Diagnostic.Message)

            RaiseEvent WorkspaceDiagnostic(Me, e)
        End Sub

        Public Function DisposeAsync() As ValueTask Implements IAsyncDisposable.DisposeAsync
            _loadLock.Dispose()

            If _workspace IsNot Nothing Then
                RemoveHandler _workspace.WorkspaceFailed, AddressOf OnWorkspaceFailed
                _workspace.Dispose()
                _workspace = Nothing
            End If
            _currentSolution = Nothing
            Return ValueTask.CompletedTask
        End Function
    End Class

    ''' <summary>
    ''' Event args for solution changes.
    ''' </summary>
    Public Class SolutionChangedEventArgs
        Inherits EventArgs

        Public ReadOnly Property Solution As Solution
        Public ReadOnly Property Kind As SolutionChangeKind

        Public Sub New(solution As Solution, kind As SolutionChangeKind)
            Me.Solution = solution
            Me.Kind = kind
        End Sub
    End Class

    ''' <summary>
    ''' Types of solution changes.
    ''' </summary>
    Public Enum SolutionChangeKind
        Loaded
        Reloaded
        ProjectAdded
        ProjectRemoved
        DocumentChanged
    End Enum

End Namespace
