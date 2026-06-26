' WorkspaceManager - Manages MSBuildWorkspace lifecycle and solution/project loading
' Workspace Layer as defined in docs/architecture.md Section 5.3

Imports Microsoft.CodeAnalysis
Imports Microsoft.CodeAnalysis.Emit
Imports Microsoft.CodeAnalysis.Host.Mef
Imports Microsoft.CodeAnalysis.MSBuild
Imports Microsoft.CodeAnalysis.Text
Imports Microsoft.CodeAnalysis.VisualBasic
Imports Microsoft.Extensions.Logging
Imports System.Collections.Immutable
Imports System.Reflection

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

            CreateWorkspace()
        End Sub

        Private Sub CreateWorkspace()
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

            _workspace = MSBuildWorkspace.Create(properties, CreateHostServices())
            _currentSolution = _workspace.CurrentSolution
            AddHandler _workspace.WorkspaceFailed, AddressOf OnWorkspaceFailed

            _logger.LogInformation("MSBuildWorkspace created successfully")
        End Sub

        Private Shared Function CreateHostServices() As MefHostServices
            Dim assemblies = MefHostServices.DefaultAssemblies.ToList()
            ' Mixed VB/C# solutions need C# workspace services even though only VB documents are served.
            AddAssemblyIfAvailable(assemblies, "Microsoft.CodeAnalysis.CSharp.Workspaces")

            Return MefHostServices.Create(assemblies)
        End Function

        Private Shared Sub AddAssemblyIfAvailable(assemblies As List(Of Assembly), assemblyName As String)
            If assemblies.Any(Function(assembly) String.Equals(assembly.GetName().Name, assemblyName, StringComparison.OrdinalIgnoreCase)) Then
                Return
            End If

            Try
                assemblies.Add(Assembly.Load(New AssemblyName(assemblyName)))
            Catch
            End Try
        End Sub

        Private Sub RecreateWorkspace()
            If _workspace IsNot Nothing Then
                RemoveHandler _workspace.WorkspaceFailed, AddressOf OnWorkspaceFailed
                _workspace.Dispose()
                _workspace = Nothing
            End If

            _currentSolution = Nothing
            _loadedSolutionPath = Nothing
            _loadedProjectPaths.Clear()

            CreateWorkspace()
        End Sub

        ''' <summary>
        ''' Clears Roslyn workspace state so the next load evaluates projects from scratch.
        ''' </summary>
        Public Async Function ResetWorkspaceAsync(Optional cancellationToken As CancellationToken = Nothing) As Task
            If _workspace Is Nothing Then
                Throw New InvalidOperationException("WorkspaceManager not initialized. Call Initialize() first.")
            End If

            Await _loadLock.WaitAsync(cancellationToken).ConfigureAwait(False)
            Try
                _logger.LogInformation("Recreating MSBuildWorkspace")
                RecreateWorkspace()
            Finally
                _loadLock.Release()
            End Try
        End Function

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

            Dim fallbackProjectPaths = LegacyVbProjectReader.GetProjectPathsFromSolution(solutionPath)

            Await _loadLock.WaitAsync(cancellationToken).ConfigureAwait(False)
            Try
                _logger.LogInformation("Loading solution: {Path}", solutionPath)

                Try
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

                    Dim missingFallbackProjectPaths = fallbackProjectPaths.
                        Where(Function(projectPath) Not IsProjectLoaded(projectPath)).
                        ToList()

                    If missingFallbackProjectPaths.Count = 0 Then
                        RaiseEvent SolutionChanged(Me, New SolutionChangedEventArgs(_currentSolution, changeKind))

                        Return vbProjects.Count > 0
                    End If

                    _logger.LogWarning(
                        "Solution loaded but {Count} VB.NET project(s) were missing; trying project-level fallback: {Path}",
                        missingFallbackProjectPaths.Count,
                        solutionPath)
                Catch ex As OperationCanceledException When cancellationToken.IsCancellationRequested
                    Throw
                Catch ex As Exception
                    _logger.LogError(ex, "Failed to load solution: {Path}", solutionPath)
                    If fallbackProjectPaths.Length = 0 Then
                        Return False
                    End If
                End Try

                If fallbackProjectPaths.Length = 0 Then
                    Return False
                End If

                _logger.LogInformation("Loading solution projects through fallback: {Path}", solutionPath)
                RecreateWorkspace()
                Return Await LoadProjectsFromSolutionFallbackAsync(solutionPath, fallbackProjectPaths, changeKind, cancellationToken).ConfigureAwait(False)
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
                Dim loadLegacyFallback = False

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
                Catch ex As OperationCanceledException When cancellationToken.IsCancellationRequested
                    Throw
                Catch ex As Exception
                    If TypeOf ex Is ArgumentException AndAlso ex.Message.Contains("already part of the workspace", StringComparison.OrdinalIgnoreCase) Then
                        _logger.LogDebug(ex, "Project already part of workspace: {Path}", projectPath)
                    Else
                        _logger.LogError(ex, "Failed to load project: {Path}", projectPath)
                    End If

                    loadLegacyFallback = True
                End Try

                If loadLegacyFallback Then
                    Await PreloadReferencedNonLegacyProjectsAsync({projectPath}, cancellationToken).ConfigureAwait(False)
                    Return LoadLegacyProject(projectPath, changeKind)
                End If

                Return False
            Finally
                _loadLock.Release()
            End Try
        End Function

        Private Async Function LoadProjectsFromSolutionFallbackAsync(solutionPath As String, projectPaths As ImmutableArray(Of String), changeKind As SolutionChangeKind, cancellationToken As CancellationToken) As Task(Of Boolean)
            If projectPaths.Length = 0 Then
                Return False
            End If

            Dim loaded = False
            Dim legacyProjectPaths = ImmutableArray.CreateBuilder(Of String)()
            Dim sdkStyleProjectPaths = ImmutableArray.CreateBuilder(Of String)()

            For Each projectPath In projectPaths
                If LegacyVbProjectReader.TryRead(projectPath) Is Nothing Then
                    sdkStyleProjectPaths.Add(projectPath)
                Else
                    legacyProjectPaths.Add(projectPath)
                End If
            Next

            Await PreloadReferencedNonLegacyProjectsAsync(legacyProjectPaths, cancellationToken).ConfigureAwait(False)

            For Each projectPath In sdkStyleProjectPaths
                Try
                    Dim project = Await _workspace.OpenProjectAsync(projectPath, cancellationToken:=cancellationToken).ConfigureAwait(False)
                    If project.Language <> LanguageNames.VisualBasic Then
                        Continue For
                    End If

                    _currentSolution = _workspace.CurrentSolution
                    If Not IsProjectLoaded(projectPath) Then
                        _loadedProjectPaths.Add(projectPath)
                    End If

                    _logger.LogInformation("SDK-style VB.NET project loaded during solution fallback: {Name} ({DocumentCount} documents)", project.Name, project.DocumentIds.Count)
                    loaded = True
                Catch ex As OperationCanceledException When cancellationToken.IsCancellationRequested
                    Throw
                Catch ex As Exception
                    _logger.LogWarning(ex, "Failed to load project during solution fallback: {Path}", projectPath)
                End Try
            Next

            For Each projectPath In legacyProjectPaths
                loaded = LoadLegacyProject(projectPath, changeKind, raiseEventOnLoad:=False) OrElse loaded
            Next

            If loaded Then
                _loadedSolutionPath = solutionPath
                RaiseEvent SolutionChanged(Me, New SolutionChangedEventArgs(CurrentSolution, changeKind))
            End If

            Return loaded
        End Function

        Private Async Function PreloadReferencedNonLegacyProjectsAsync(projectPaths As IEnumerable(Of String), cancellationToken As CancellationToken) As Task
            Dim referencedProjectPaths As New SortedSet(Of String)(StringComparer.OrdinalIgnoreCase)
            Dim visitedLegacyProjectPaths As New HashSet(Of String)(StringComparer.OrdinalIgnoreCase)

            For Each projectPath In projectPaths
                CollectReferencedNonLegacyProjectPaths(projectPath, referencedProjectPaths, visitedLegacyProjectPaths)
            Next

            For Each referencedProjectPath In referencedProjectPaths
                If GetProjectByPath(referencedProjectPath) IsNot Nothing Then
                    Continue For
                End If

                Try
                    Dim project = Await _workspace.OpenProjectAsync(referencedProjectPath, cancellationToken:=cancellationToken).ConfigureAwait(False)
                    _currentSolution = _workspace.CurrentSolution
                    _logger.LogInformation("Referenced non-legacy project loaded during legacy fallback: {Name} ({Language})", project.Name, project.Language)
                Catch ex As OperationCanceledException When cancellationToken.IsCancellationRequested
                    Throw
                Catch ex As Exception
                    _logger.LogWarning(ex, "Failed to load referenced non-legacy project during legacy fallback: {Path}", referencedProjectPath)
                End Try
            Next
        End Function

        Private Shared Sub CollectReferencedNonLegacyProjectPaths(projectPath As String, referencedProjectPaths As ISet(Of String), visitedLegacyProjectPaths As ISet(Of String))
            If String.IsNullOrWhiteSpace(projectPath) OrElse Not File.Exists(projectPath) Then
                Return
            End If

            Dim fullProjectPath = Path.GetFullPath(projectPath)
            If Not visitedLegacyProjectPaths.Add(fullProjectPath) Then
                Return
            End If

            Dim legacyProject = LegacyVbProjectReader.TryRead(fullProjectPath)
            If legacyProject Is Nothing Then
                Return
            End If

            For Each referencedProjectPath In legacyProject.ProjectReferences
                If String.IsNullOrWhiteSpace(referencedProjectPath) OrElse Not File.Exists(referencedProjectPath) Then
                    Continue For
                End If

                Dim fullReferencedProjectPath = Path.GetFullPath(referencedProjectPath)
                If LegacyVbProjectReader.TryRead(fullReferencedProjectPath) Is Nothing Then
                    referencedProjectPaths.Add(fullReferencedProjectPath)
                Else
                    CollectReferencedNonLegacyProjectPaths(fullReferencedProjectPath, referencedProjectPaths, visitedLegacyProjectPaths)
                End If
            Next
        End Sub

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
            If Not String.IsNullOrWhiteSpace(legacyProject.MainTypeName) Then
                compilationOptions = compilationOptions.WithMainTypeName(legacyProject.MainTypeName)
            End If

            Dim loadedReferencedProjects = legacyProject.ProjectReferences.
                Select(Function(referencedProjectPath) GetProjectByPath(referencedProjectPath)).
                Where(Function(referencedProject) referencedProject IsNot Nothing).
                ToList()
            Dim loadedProjectReferences = loadedReferencedProjects.
                Select(Function(referencedProject) New ProjectReference(referencedProject.Id)).
                ToImmutableArray()
            ' Manual legacy projects do not always materialize project references into compilations.
            Dim metadataReferences = legacyProject.References.AddRange(
                loadedReferencedProjects.
                    Select(Function(referencedProject) CreateCompilationReference(referencedProject, legacyProject.References)).
                    Where(Function(reference) reference IsNot Nothing))

            Dim projectInfo As ProjectInfo = ProjectInfo.Create(
                newProjectId,
                VersionStamp.Create(),
                legacyProject.AssemblyName,
                legacyProject.AssemblyName,
                LanguageNames.VisualBasic,
                filePath:=legacyProject.ProjectPath,
                compilationOptions:=compilationOptions,
                projectReferences:=loadedProjectReferences,
                metadataReferences:=metadataReferences)

            Dim solution = CurrentSolution.AddProject(projectInfo)

            For Each documentPath In legacyProject.Documents
                Dim sourceText As SourceText = SourceText.From(File.ReadAllText(documentPath))
                solution = solution.AddDocument(
                    DocumentId.CreateNewId(newProjectId, debugName:=Path.GetFileName(documentPath)),
                    Path.GetFileName(documentPath),
                    sourceText,
                    filePath:=documentPath)
            Next

            For Each generatedSource In legacyProject.GeneratedSources
                solution = solution.AddDocument(
                    DocumentId.CreateNewId(newProjectId, debugName:=generatedSource.FileName),
                    generatedSource.FileName,
                    SourceText.From(generatedSource.Source))
            Next

            _currentSolution = solution
            _loadedProjectPaths.Add(legacyProject.ProjectPath)

            For Each referencedProjectPath In legacyProject.ProjectReferences
                If Not IsProjectLoaded(referencedProjectPath) Then
                    LoadLegacyProject(referencedProjectPath, changeKind, raiseEventOnLoad:=False)
                End If

                Dim referencedProject = GetProjectByPath(referencedProjectPath)
                Dim currentProject = CurrentSolution.GetProject(newProjectId)
                If referencedProject IsNot Nothing AndAlso
                    currentProject IsNot Nothing AndAlso
                    Not currentProject.ProjectReferences.Any(Function(reference) reference.ProjectId = referencedProject.Id) Then
                    _currentSolution = CurrentSolution.AddProjectReference(newProjectId, New ProjectReference(referencedProject.Id))
                    Dim compilationReference = CreateCompilationReference(referencedProject, currentProject.MetadataReferences)
                    If compilationReference IsNot Nothing Then
                        _currentSolution = CurrentSolution.AddMetadataReference(newProjectId, compilationReference)
                    End If
                End If
            Next

            _logger.LogInformation("Legacy VB.NET project loaded: {Name} ({DocumentCount} documents)", legacyProject.AssemblyName, legacyProject.Documents.Length)
            ReportLegacyProjectWarnings(legacyProject)

            If raiseEventOnLoad Then
                RaiseEvent SolutionChanged(Me, New SolutionChangedEventArgs(CurrentSolution, changeKind))
            End If

            Return True
        End Function

        Private Function CreateCompilationReference(referencedProject As Project, fallbackReferences As IEnumerable(Of MetadataReference)) As MetadataReference
            Try
                Dim compilation = referencedProject.GetCompilationAsync().GetAwaiter().GetResult()
                If compilation Is Nothing Then
                    Return Nothing
                End If

                Dim emittedReference = TryEmitCompilationReference(compilation, referencedProject.FilePath, logFailure:=False)
                If emittedReference IsNot Nothing Then
                    Return emittedReference
                End If

                Dim referencesToAdd = fallbackReferences.
                    Where(Function(reference) reference IsNot Nothing).
                    Where(Function(reference) Not compilation.References.Any(Function(existing) String.Equals(existing.Display, reference.Display, StringComparison.OrdinalIgnoreCase))).
                    ToArray()
                If referencesToAdd.Length > 0 Then
                    emittedReference = TryEmitCompilationReference(compilation.AddReferences(referencesToAdd), referencedProject.FilePath)
                    If emittedReference IsNot Nothing Then
                        Return emittedReference
                    End If
                End If
            Catch ex As Exception
                _logger.LogWarning(ex, "Failed to create compilation reference for project: {Path}", referencedProject.FilePath)
            End Try

            Return Nothing
        End Function

        Private Function TryEmitCompilationReference(compilation As Compilation, projectPath As String, Optional logFailure As Boolean = True) As MetadataReference
            Using stream As New MemoryStream()
                Dim emitResult = compilation.Emit(stream, options:=New EmitOptions(metadataOnly:=True))
                If emitResult.Success Then
                    Return MetadataReference.CreateFromImage(stream.ToArray())
                End If

                If Not logFailure Then
                    Return Nothing
                End If

                Dim errors = String.Join(
                    "; ",
                    emitResult.Diagnostics.
                        Where(Function(diagnostic) diagnostic.Severity = DiagnosticSeverity.Error).
                        Take(3).
                        Select(Function(diagnostic) diagnostic.ToString()))
                _logger.LogWarning("Failed to emit metadata reference for project: {Path}. {Diagnostics}", projectPath, errors)
            End Using

            Return Nothing
        End Function

        Private Sub ReportLegacyProjectWarnings(legacyProject As LegacyVbProjectProjection)
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
            Dim loadedSolutionPath = _loadedSolutionPath
            Dim loadedProjectPaths = _loadedProjectPaths.ToList()

            If String.IsNullOrEmpty(loadedSolutionPath) AndAlso loadedProjectPaths.Count = 0 Then
                Return False
            End If

            Await ResetWorkspaceAsync(cancellationToken).ConfigureAwait(False)

            If Not String.IsNullOrEmpty(loadedSolutionPath) Then
                Return Await LoadSolutionAsync(loadedSolutionPath, cancellationToken, SolutionChangeKind.Reloaded).ConfigureAwait(False)
            End If

            Dim reloadedAny = False
            For Each projectPath In loadedProjectPaths
                Dim reloaded = Await LoadProjectAsync(projectPath, cancellationToken, SolutionChangeKind.Reloaded).ConfigureAwait(False)
                reloadedAny = reloadedAny OrElse reloaded
            Next

            If Not reloadedAny Then
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
