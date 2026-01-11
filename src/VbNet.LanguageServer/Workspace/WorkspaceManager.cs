// WorkspaceManager - Manages MSBuildWorkspace lifecycle and solution/project loading
// Workspace Layer as defined in docs/architecture.md Section 5.3

using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.MSBuild;
using Microsoft.CodeAnalysis.Text;
using Microsoft.Extensions.Logging;

namespace VbNet.LanguageServer.Workspace;

/// <summary>
/// Manages the Roslyn MSBuildWorkspace lifecycle.
/// Handles solution/project loading, workspace events, and provides access to the current solution.
/// </summary>
public sealed class WorkspaceManager : IAsyncDisposable
{
    private readonly ILogger<WorkspaceManager> _logger;
    private readonly SemaphoreSlim _loadLock = new(1, 1);

    private MSBuildWorkspace? _workspace;
    private string? _loadedSolutionPath;
    private readonly List<string> _loadedProjectPaths = new();

    /// <summary>
    /// Event raised when workspace diagnostics occur during loading.
    /// </summary>
    public event EventHandler<WorkspaceDiagnosticEventArgs>? WorkspaceDiagnostic;

    /// <summary>
    /// Event raised when the solution changes (reload, project added/removed).
    /// </summary>
    public event EventHandler<SolutionChangedEventArgs>? SolutionChanged;

    public WorkspaceManager(ILogger<WorkspaceManager> logger)
    {
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    /// <summary>
    /// Gets the current solution snapshot. Thread-safe due to immutability.
    /// </summary>
    public Solution? CurrentSolution => _workspace?.CurrentSolution;

    /// <summary>
    /// Gets whether a solution or project is currently loaded.
    /// </summary>
    public bool IsLoaded => _workspace?.CurrentSolution.ProjectIds.Count > 0;

    /// <summary>
    /// Gets the path of the loaded solution, if any.
    /// </summary>
    public string? LoadedSolutionPath => _loadedSolutionPath;

    /// <summary>
    /// Checks whether a project path is currently loaded.
    /// </summary>
    public bool IsProjectLoaded(string projectPath)
    {
        var normalizedPath = NormalizePath(projectPath);
        return _loadedProjectPaths.Any(p => string.Equals(NormalizePath(p), normalizedPath, StringComparison.OrdinalIgnoreCase));
    }

    /// <summary>
    /// Initializes the MSBuildWorkspace. Must be called before loading solutions/projects.
    /// </summary>
    public void Initialize()
    {
        if (_workspace != null)
        {
            _logger.LogWarning("WorkspaceManager already initialized");
            return;
        }

        _logger.LogDebug("Creating MSBuildWorkspace");

        // Create workspace with default properties
        var properties = new Dictionary<string, string>
        {
            // Ensure we get design-time build information
            ["DesignTimeBuild"] = "true",
            ["BuildingInsideVisualStudio"] = "true",
            ["BuildProjectReferences"] = "false",
            // Skip targets that aren't needed for intellisense
            ["SkipCompilerExecution"] = "true",
            ["ProvideCommandLineArgs"] = "true"
        };

        _workspace = MSBuildWorkspace.Create(properties);
        _workspace.WorkspaceFailed += OnWorkspaceFailed;

        _logger.LogInformation("MSBuildWorkspace created successfully");
    }

    /// <summary>
    /// Loads a solution file.
    /// </summary>
    public async Task<bool> LoadSolutionAsync(
        string solutionPath,
        CancellationToken cancellationToken = default,
        SolutionChangeKind changeKind = SolutionChangeKind.Loaded)
    {
        if (_workspace == null)
        {
            throw new InvalidOperationException("WorkspaceManager not initialized. Call Initialize() first.");
        }

        if (!File.Exists(solutionPath))
        {
            _logger.LogError("Solution file not found: {Path}", solutionPath);
            return false;
        }

        await _loadLock.WaitAsync(cancellationToken);
        try
        {
            _logger.LogInformation("Loading solution: {Path}", solutionPath);

            var solution = await _workspace.OpenSolutionAsync(solutionPath, cancellationToken: cancellationToken);

            _loadedSolutionPath = solutionPath;
            _loadedProjectPaths.Clear();

            // Log loaded projects
            var vbProjects = solution.Projects
                .Where(p => p.Language == LanguageNames.VisualBasic)
                .ToList();

            var csharpProjects = solution.Projects
                .Where(p => p.Language == LanguageNames.CSharp)
                .ToList();

            _logger.LogInformation("Solution loaded: {VbCount} VB.NET projects, {CsCount} C# projects",
                vbProjects.Count, csharpProjects.Count);

            foreach (var project in vbProjects)
            {
                _logger.LogDebug("  VB.NET project: {Name}", project.Name);
                if (project.FilePath != null)
                {
                    _loadedProjectPaths.Add(project.FilePath);
                }
            }

            // Per architecture decision 14.3, we only serve VB.NET in Phase 1-3
            if (csharpProjects.Count > 0)
            {
                _logger.LogInformation("Note: C# projects loaded but not served (VB.NET only in current phase)");
            }

            SolutionChanged?.Invoke(this, new SolutionChangedEventArgs(solution, changeKind));

            return vbProjects.Count > 0;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to load solution: {Path}", solutionPath);
            return false;
        }
        finally
        {
            _loadLock.Release();
        }
    }

    /// <summary>
    /// Loads a single project file.
    /// </summary>
    public async Task<bool> LoadProjectAsync(
        string projectPath,
        CancellationToken cancellationToken = default,
        SolutionChangeKind changeKind = SolutionChangeKind.ProjectAdded)
    {
        if (_workspace == null)
        {
            throw new InvalidOperationException("WorkspaceManager not initialized. Call Initialize() first.");
        }

        if (!File.Exists(projectPath))
        {
            _logger.LogError("Project file not found: {Path}", projectPath);
            return false;
        }

        await _loadLock.WaitAsync(cancellationToken);
        try
        {
            _logger.LogInformation("Loading project: {Path}", projectPath);

            var project = await _workspace.OpenProjectAsync(projectPath, cancellationToken: cancellationToken);

            if (project.Language != LanguageNames.VisualBasic)
            {
                _logger.LogWarning("Project is not VB.NET: {Name} ({Language})", project.Name, project.Language);
                return false;
            }

            if (!IsProjectLoaded(projectPath))
            {
                _loadedProjectPaths.Add(projectPath);
            }
            _logger.LogInformation("Project loaded: {Name} ({DocumentCount} documents)",
                project.Name, project.DocumentIds.Count);

            SolutionChanged?.Invoke(this, new SolutionChangedEventArgs(
                _workspace.CurrentSolution, changeKind));

            return true;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to load project: {Path}", projectPath);
            return false;
        }
        finally
        {
            _loadLock.Release();
        }
    }

    /// <summary>
    /// Gets a document by its file path URI.
    /// </summary>
    public Document? GetDocumentByUri(string uri)
    {
        if (_workspace == null) return null;

        var filePath = UriToFilePath(uri);
        if (filePath == null) return null;

        return GetDocumentByPath(filePath);
    }

    /// <summary>
    /// Gets a document by its file path.
    /// </summary>
    public Document? GetDocumentByPath(string filePath)
    {
        if (_workspace == null) return null;

        var solution = _workspace.CurrentSolution;
        var normalizedPath = Path.GetFullPath(filePath);

        foreach (var projectId in solution.ProjectIds)
        {
            var project = solution.GetProject(projectId);
            if (project == null) continue;

            foreach (var documentId in project.DocumentIds)
            {
                var document = project.GetDocument(documentId);
                if (document?.FilePath != null &&
                    string.Equals(Path.GetFullPath(document.FilePath), normalizedPath, StringComparison.OrdinalIgnoreCase))
                {
                    return document;
                }
            }
        }

        return null;
    }

    /// <summary>
    /// Gets a project by its file path.
    /// </summary>
    public Project? GetProjectByPath(string projectPath)
    {
        if (_workspace == null) return null;

        var solution = _workspace.CurrentSolution;
        var normalizedPath = Path.GetFullPath(projectPath);

        foreach (var projectId in solution.ProjectIds)
        {
            var project = solution.GetProject(projectId);
            if (project?.FilePath != null &&
                string.Equals(Path.GetFullPath(project.FilePath), normalizedPath, StringComparison.OrdinalIgnoreCase))
            {
                return project;
            }
        }

        return null;
    }

    /// <summary>
    /// Gets all VB.NET projects in the workspace.
    /// </summary>
    public IEnumerable<Project> GetVbNetProjects()
    {
        if (_workspace == null) return Enumerable.Empty<Project>();

        return _workspace.CurrentSolution.Projects
            .Where(p => p.Language == LanguageNames.VisualBasic);
    }

    /// <summary>
    /// Applies a text change to a document and returns the updated document.
    /// </summary>
    public Document? ApplyTextChange(DocumentId documentId, SourceText newText)
    {
        if (_workspace == null) return null;

        var solution = _workspace.CurrentSolution;
        var document = solution.GetDocument(documentId);
        if (document == null) return null;

        var newSolution = solution.WithDocumentText(documentId, newText);

        if (_workspace.TryApplyChanges(newSolution))
        {
            return _workspace.CurrentSolution.GetDocument(documentId);
        }

        _logger.LogWarning("Failed to apply text change to document: {Id}", documentId);
        return null;
    }

    /// <summary>
    /// Converts a file:// URI to a file path.
    /// </summary>
    private static string? UriToFilePath(string uri)
    {
        if (string.IsNullOrEmpty(uri)) return null;

        try
        {
            var parsedUri = new Uri(uri);
            if (parsedUri.IsFile)
            {
                var localPath = parsedUri.LocalPath;

                // On Windows, Uri.LocalPath returns paths like "/c:/foo" for file:///c:/foo
                // We need to remove the leading slash
                if (localPath.Length >= 3 &&
                    localPath[0] == '/' &&
                    char.IsLetter(localPath[1]) &&
                    localPath[2] == ':')
                {
                    localPath = localPath.Substring(1);
                }

                return localPath;
            }
        }
        catch (UriFormatException)
        {
            // If it's not a valid URI, treat it as a path
            return uri;
        }

        return null;
    }

    /// <summary>
    /// Reloads the currently loaded solution or projects.
    /// </summary>
    public async Task<bool> ReloadWorkspaceAsync(CancellationToken cancellationToken = default)
    {
        if (!string.IsNullOrEmpty(_loadedSolutionPath))
        {
            return await LoadSolutionAsync(_loadedSolutionPath, cancellationToken, SolutionChangeKind.Reloaded);
        }

        if (_loadedProjectPaths.Count == 0)
        {
            return false;
        }

        var reloadedAny = false;
        foreach (var projectPath in _loadedProjectPaths.ToList())
        {
            var reloaded = await LoadProjectAsync(projectPath, cancellationToken, SolutionChangeKind.Reloaded);
            reloadedAny |= reloaded;
        }

        if (!reloadedAny && _workspace?.CurrentSolution.ProjectIds.Count > 0)
        {
            SolutionChanged?.Invoke(this, new SolutionChangedEventArgs(
                _workspace.CurrentSolution, SolutionChangeKind.Reloaded));
        }

        return reloadedAny;
    }

    private static string NormalizePath(string path)
    {
        return Path.GetFullPath(path);
    }

    private void OnWorkspaceFailed(object? sender, WorkspaceDiagnosticEventArgs e)
    {
        var level = e.Diagnostic.Kind == WorkspaceDiagnosticKind.Failure
            ? LogLevel.Error
            : LogLevel.Warning;

        _logger.Log(level, "Workspace diagnostic: {Message}", e.Diagnostic.Message);

        WorkspaceDiagnostic?.Invoke(this, e);
    }

    public async ValueTask DisposeAsync()
    {
        _loadLock.Dispose();

        if (_workspace != null)
        {
            _workspace.WorkspaceFailed -= OnWorkspaceFailed;
            _workspace.Dispose();
            _workspace = null;
        }

        await Task.CompletedTask;
    }
}

/// <summary>
/// Event args for solution changes.
/// </summary>
public class SolutionChangedEventArgs : EventArgs
{
    public Solution Solution { get; }
    public SolutionChangeKind Kind { get; }

    public SolutionChangedEventArgs(Solution solution, SolutionChangeKind kind)
    {
        Solution = solution;
        Kind = kind;
    }
}

/// <summary>
/// Types of solution changes.
/// </summary>
public enum SolutionChangeKind
{
    Loaded,
    Reloaded,
    ProjectAdded,
    ProjectRemoved,
    DocumentChanged
}
