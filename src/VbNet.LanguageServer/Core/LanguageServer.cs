// Main Language Server class - orchestrates all server components
// Follows the architecture defined in docs/architecture.md

using System.Text.Json;
using Microsoft.Extensions.Logging;
using VbNet.LanguageServer.Protocol;
using VbNet.LanguageServer.Services;
using VbNet.LanguageServer.Workspace;

namespace VbNet.LanguageServer.Core;

/// <summary>
/// The main VB.NET Language Server class.
/// Manages the server lifecycle, routes requests, and coordinates language services.
/// </summary>
public sealed class LanguageServer : IAsyncDisposable
{
    private readonly ITransport _transport;
    private readonly MessageDispatcher _dispatcher;
    private readonly ILogger<LanguageServer> _logger;
    private readonly ILoggerFactory _loggerFactory;
    private readonly CancellationTokenSource _shutdownCts = new();

    // Workspace layer components
    private readonly WorkspaceManager _workspaceManager;
    private readonly DocumentManager _documentManager;

    // Services layer components
    private readonly DiagnosticsService _diagnosticsService;
    private readonly CompletionService _completionService;
    private readonly HoverService _hoverService;
    private readonly DefinitionService _definitionService;
    private readonly ReferencesService _referencesService;
    private readonly RenameService _renameService;
    private readonly SymbolsService _symbolsService;

    private ServerState _state = ServerState.NotStarted;
    private InitializeParams? _initializeParams;
    private TaskCompletionSource? _shutdownRequested;
    private bool _diagnosticsEnabled = true;
    private bool _completionEnabled = true;
    private string? _workspaceRootUri;
    private string? _workspaceSolutionPathOverride;
    private string[]? _workspaceProjectPathsOverride;
    private bool _ignoreSolutionFiles;
    private string[]? _workspaceProjectSearchPaths;
    private string[]? _workspaceExcludePaths;
    private const int MaxAncestorSearchDepth = 4;

    /// <summary>
    /// Server name reported in initialize response.
    /// </summary>
    public const string ServerName = "VbNet.LanguageServer";

    /// <summary>
    /// Server version reported in initialize response.
    /// </summary>
    public const string ServerVersion = "0.1.0";

    public LanguageServer(ITransport transport, ILoggerFactory loggerFactory)
    {
        _transport = transport ?? throw new ArgumentNullException(nameof(transport));
        _loggerFactory = loggerFactory ?? throw new ArgumentNullException(nameof(loggerFactory));
        _logger = loggerFactory.CreateLogger<LanguageServer>();
        _dispatcher = new MessageDispatcher(transport, loggerFactory.CreateLogger<MessageDispatcher>());

        // Initialize workspace layer
        _workspaceManager = new WorkspaceManager(loggerFactory.CreateLogger<WorkspaceManager>());
        _documentManager = new DocumentManager(_workspaceManager, loggerFactory.CreateLogger<DocumentManager>());

        // Initialize services layer
        _diagnosticsService = new DiagnosticsService(
            _workspaceManager,
            _documentManager,
            PublishDiagnosticsAsync,
            loggerFactory.CreateLogger<DiagnosticsService>());

        _completionService = new CompletionService(
            _workspaceManager,
            _documentManager,
            loggerFactory.CreateLogger<CompletionService>());

        _hoverService = new HoverService(
            _workspaceManager,
            _documentManager,
            loggerFactory.CreateLogger<HoverService>());

        _definitionService = new DefinitionService(
            _workspaceManager,
            _documentManager,
            loggerFactory.CreateLogger<DefinitionService>());

        _referencesService = new ReferencesService(
            _workspaceManager,
            _documentManager,
            loggerFactory.CreateLogger<ReferencesService>());

        _renameService = new RenameService(
            _workspaceManager,
            _documentManager,
            loggerFactory.CreateLogger<RenameService>());

        _symbolsService = new SymbolsService(
            _workspaceManager,
            _documentManager,
            loggerFactory.CreateLogger<SymbolsService>());

        RegisterHandlers();
    }

    /// <summary>
    /// Gets the workspace manager for project/solution operations.
    /// </summary>
    public WorkspaceManager WorkspaceManager => _workspaceManager;

    /// <summary>
    /// Gets the document manager for open document operations.
    /// </summary>
    public DocumentManager DocumentManager => _documentManager;

    /// <summary>
    /// Gets the current server state.
    /// </summary>
    public ServerState State => _state;

    /// <summary>
    /// Gets the client's initialization parameters (available after initialize).
    /// </summary>
    public InitializeParams? InitializeParams => _initializeParams;

    /// <summary>
    /// Registers all LSP request and notification handlers.
    /// </summary>
    private void RegisterHandlers()
    {
        // Lifecycle
        _dispatcher.RegisterRequest<InitializeParams, InitializeResult>("initialize", HandleInitializeAsync);
        _dispatcher.RegisterNotification("initialized", HandleInitializedAsync);
        _dispatcher.RegisterRequest<object?, object?>("shutdown", HandleShutdownAsync);
        _dispatcher.RegisterNotification("exit", HandleExitAsync);

        // Text document synchronization
        _dispatcher.RegisterNotification<DidOpenTextDocumentParams>("textDocument/didOpen", HandleDidOpenAsync);
        _dispatcher.RegisterNotification<DidCloseTextDocumentParams>("textDocument/didClose", HandleDidCloseAsync);
        _dispatcher.RegisterNotification<DidChangeTextDocumentParams>("textDocument/didChange", HandleDidChangeAsync);
        _dispatcher.RegisterNotification<DidSaveTextDocumentParams>("textDocument/didSave", HandleDidSaveAsync);

        // Workspace notifications
        _dispatcher.RegisterNotification<DidChangeConfigurationParams>(
            "workspace/didChangeConfiguration",
            HandleDidChangeConfigurationAsync);
        _dispatcher.RegisterNotification<DidChangeWatchedFilesParams>(
            "workspace/didChangeWatchedFiles",
            HandleDidChangeWatchedFilesAsync);

        // Language features
        _dispatcher.RegisterRequest<CompletionParams, CompletionList>("textDocument/completion", HandleCompletionAsync);
        _dispatcher.RegisterRequest<CompletionItem, CompletionItem>("completionItem/resolve", HandleCompletionResolveAsync);
        _dispatcher.RegisterRequest<HoverParams, Hover?>("textDocument/hover", HandleHoverAsync);
        _dispatcher.RegisterRequest<DefinitionParams, Location[]>("textDocument/definition", HandleDefinitionAsync);
        _dispatcher.RegisterRequest<ReferenceParams, Location[]>("textDocument/references", HandleReferencesAsync);
        _dispatcher.RegisterRequest<PrepareRenameParams, PrepareRenameResult?>("textDocument/prepareRename", HandlePrepareRenameAsync);
        _dispatcher.RegisterRequest<RenameParams, WorkspaceEdit?>("textDocument/rename", HandleRenameAsync);
        _dispatcher.RegisterRequest<DocumentSymbolParams, DocumentSymbol[]>("textDocument/documentSymbol", HandleDocumentSymbolAsync);
        _dispatcher.RegisterRequest<WorkspaceSymbolParams, SymbolInformation[]>("workspace/symbol", HandleWorkspaceSymbolAsync);

        _logger.LogDebug("All LSP handlers registered");
    }

    /// <summary>
    /// Starts the language server and begins processing messages.
    /// </summary>
    public async Task RunAsync(CancellationToken cancellationToken = default)
    {
        if (_state != ServerState.NotStarted)
        {
            throw new InvalidOperationException($"Server cannot be started in state: {_state}");
        }

        _state = ServerState.Starting;
        _logger.LogInformation("VB.NET Language Server starting...");

        // Start transport (creates pipe/connects stdio)
        await _transport.StartAsync(cancellationToken);
        _logger.LogInformation("Transport ready, waiting for initialize request");

        // Create a linked token that respects both external cancellation and shutdown
        using var linkedCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, _shutdownCts.Token);

        try
        {
            // Run the message loop until shutdown or cancellation
            await _dispatcher.RunAsync(linkedCts.Token);
        }
        catch (OperationCanceledException) when (_shutdownCts.IsCancellationRequested)
        {
            _logger.LogInformation("Server shutdown completed");
        }

        _state = ServerState.Stopped;
        _logger.LogInformation("VB.NET Language Server stopped");
    }

    #region Lifecycle Handlers

    private Task<InitializeResult> HandleInitializeAsync(InitializeParams? @params, CancellationToken ct)
    {
        if (_state != ServerState.Starting)
        {
            throw new InvalidOperationException($"Initialize not allowed in state: {_state}");
        }

        _initializeParams = @params;
        _state = ServerState.Initializing;

        ApplyInitializationOptions(@params?.InitializationOptions);

        _logger.LogInformation("Initialize request received from client: {ClientName} {ClientVersion}",
            @params?.ClientInfo?.Name ?? "unknown",
            @params?.ClientInfo?.Version ?? "unknown");

        if (@params?.RootUri != null)
        {
            _logger.LogInformation("Workspace root: {RootUri}", @params.RootUri);
        }

        // Build server capabilities - start conservative, expand in later phases
        var capabilities = BuildServerCapabilities();

        var result = new InitializeResult
        {
            Capabilities = capabilities,
            ServerInfo = new ServerInfo
            {
                Name = ServerName,
                Version = ServerVersion
            }
        };

        return Task.FromResult(result);
    }

    private async Task HandleInitializedAsync(CancellationToken ct)
    {
        if (_state != ServerState.Initializing)
        {
            _logger.LogWarning("Received initialized notification in unexpected state: {State}", _state);
            return;
        }

        _state = ServerState.Running;
        _logger.LogInformation("Server initialized and running");

        // Initialize MSBuildWorkspace
        _workspaceManager.Initialize();

        // Try to load workspace from root URI
        if (_initializeParams?.RootUri != null)
        {
            _workspaceRootUri = _initializeParams.RootUri;
            await LoadWorkspaceAsync(_initializeParams.RootUri, ct);
        }
        else if (_initializeParams?.WorkspaceFolders?.Length > 0)
        {
            _workspaceRootUri = _initializeParams.WorkspaceFolders[0].Uri;
            await LoadWorkspaceAsync(_initializeParams.WorkspaceFolders[0].Uri, ct);
        }
        else
        {
            _logger.LogWarning("No workspace root provided, operating in single-file mode");
        }
    }

    /// <summary>
    /// Loads a workspace from the given root URI.
    /// Searches for .sln files first, then .vbproj files.
    /// </summary>
    private async Task LoadWorkspaceAsync(string rootUri, CancellationToken ct)
    {
        var loadSucceeded = false;
        try
        {
            var rootPath = UriToLocalPath(rootUri);

            if (!Directory.Exists(rootPath))
            {
                _logger.LogWarning("Workspace root does not exist: {Path}", rootPath);
                return;
            }

            // Search for solution files (per architecture: search for .sln, if multiple use nearest to root)
            if (!string.IsNullOrWhiteSpace(_workspaceSolutionPathOverride))
            {
                var explicitSolutionPath = ResolvePath(_workspaceSolutionPathOverride, rootPath);
                if (!string.IsNullOrEmpty(explicitSolutionPath))
                {
                    loadSucceeded = await _workspaceManager.LoadSolutionAsync(explicitSolutionPath, ct);
                    return;
                }
            }

            if (_workspaceProjectPathsOverride != null && _workspaceProjectPathsOverride.Length > 0)
            {
                var anyLoaded = false;
                foreach (var projectPath in _workspaceProjectPathsOverride)
                {
                    if (string.IsNullOrWhiteSpace(projectPath) ||
                        !projectPath.EndsWith(".vbproj", StringComparison.OrdinalIgnoreCase))
                    {
                        continue;
                    }

                    var resolved = ResolvePath(projectPath, rootPath);
                    if (string.IsNullOrEmpty(resolved))
                    {
                        continue;
                    }

                    anyLoaded |= await _workspaceManager.LoadProjectAsync(resolved, ct);
                }

                loadSucceeded = anyLoaded;
                if (anyLoaded)
                {
                    return;
                }
            }

            if (!_ignoreSolutionFiles)
            {
                var solutionPath = FindSolutionPath(rootPath);
                if (!string.IsNullOrEmpty(solutionPath))
                {
                    if (SolutionContainsVbProject(solutionPath))
                    {
                        var loadedVb = await _workspaceManager.LoadSolutionAsync(solutionPath, ct);
                        loadSucceeded = loadedVb;
                        if (loadedVb)
                        {
                            return;
                        }
                    }
                    else
                    {
                        _logger.LogInformation("Skipping solution without VB.NET projects: {Path}", solutionPath);
                    }
                }
            }

            // No solution, search for VB.NET projects
            var vbprojFiles = CollectVbProjFiles(
                GetProjectSearchRoots(rootPath),
                _workspaceExcludePaths,
                ct);

            if (vbprojFiles.Count > 0)
            {
                _logger.LogInformation("No solution found, loading {Count} VB.NET project(s)", vbprojFiles.Count);
                foreach (var projectPath in vbprojFiles)
                {
                    var loaded = await _workspaceManager.LoadProjectAsync(projectPath, ct);
                    loadSucceeded |= loaded;
                }
                return;
            }

            _logger.LogInformation("No solution or VB.NET projects found in workspace");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to load workspace from: {Uri}", rootUri);
        }
        finally
        {
            _workspaceManager.SignalInitialLoadCompleted(loadSucceeded);
        }
    }

    private Task<object?> HandleShutdownAsync(object? @params, CancellationToken ct)
    {
        _logger.LogInformation("Shutdown request received");
        _state = ServerState.ShuttingDown;
        _shutdownRequested = new TaskCompletionSource();

        // Return null as per LSP spec
        return Task.FromResult<object?>(null);
    }

    private Task HandleExitAsync(CancellationToken ct)
    {
        _logger.LogInformation("Exit notification received");

        // Signal the main loop to stop
        _shutdownCts.Cancel();

        // Complete the shutdown task if it was requested
        _shutdownRequested?.TrySetResult();

        return Task.CompletedTask;
    }

    #endregion

    #region Text Document Handlers

    private Task HandleDidOpenAsync(DidOpenTextDocumentParams? @params, CancellationToken ct)
    {
        if (@params == null) return Task.CompletedTask;

        _documentManager.HandleDidOpen(@params);
        return Task.CompletedTask;
    }

    private async Task HandleDidCloseAsync(DidCloseTextDocumentParams? @params, CancellationToken ct)
    {
        if (@params == null)
        {
            return;
        }

        _documentManager.HandleDidClose(@params);
        await _diagnosticsService.ClearDiagnosticsAsync(@params.TextDocument.Uri, ct);
    }

    private Task HandleDidChangeAsync(DidChangeTextDocumentParams? @params, CancellationToken ct)
    {
        if (@params == null) return Task.CompletedTask;

        _documentManager.HandleDidChange(@params);
        return Task.CompletedTask;
    }

    private Task HandleDidSaveAsync(DidSaveTextDocumentParams? @params, CancellationToken ct)
    {
        if (@params == null) return Task.CompletedTask;

        _documentManager.HandleDidSave(@params);
        return Task.CompletedTask;
    }

    private async Task HandleDidChangeConfigurationAsync(DidChangeConfigurationParams? @params, CancellationToken ct)
    {
        if (@params?.Settings == null)
        {
            return;
        }

        var settingsElement = ExtractSettingsElement(@params.Settings);
        var diagnosticsEnabled = GetBooleanSetting(settingsElement, "diagnostics", "enable");
        var completionEnabled = GetBooleanSetting(settingsElement, "completion", "enable");
        var solutionPathOverride = GetStringSetting(settingsElement, "workspace", "solutionPath");
        var ignoreSolutionFiles = GetBooleanSetting(settingsElement, "workspace", "ignoreSolutionFiles");
        var projectSearchPaths = GetStringArraySetting(settingsElement, "workspace", "projectSearchPaths");
        var excludePaths = GetStringArraySetting(settingsElement, "workspace", "excludePaths");

        var reloadWorkspace = false;

        if (diagnosticsEnabled.HasValue && diagnosticsEnabled.Value != _diagnosticsEnabled)
        {
            _diagnosticsEnabled = diagnosticsEnabled.Value;
            _diagnosticsService.Enabled = diagnosticsEnabled.Value;

            if (!_diagnosticsEnabled)
            {
                await ClearDiagnosticsForOpenDocumentsAsync(ct);
            }
            else
            {
                TriggerDiagnosticsForOpenDocuments();
            }

            _logger.LogInformation("Diagnostics enabled: {Enabled}", _diagnosticsEnabled);
        }

        if (completionEnabled.HasValue && completionEnabled.Value != _completionEnabled)
        {
            _completionEnabled = completionEnabled.Value;
            _logger.LogInformation("Completion enabled: {Enabled}", _completionEnabled);
        }

        if (solutionPathOverride != null && !string.Equals(solutionPathOverride, _workspaceSolutionPathOverride, StringComparison.OrdinalIgnoreCase))
        {
            _workspaceSolutionPathOverride = solutionPathOverride;
            reloadWorkspace = true;
        }

        if (ignoreSolutionFiles.HasValue && ignoreSolutionFiles.Value != _ignoreSolutionFiles)
        {
            _ignoreSolutionFiles = ignoreSolutionFiles.Value;
            reloadWorkspace = true;
        }

        var projectPathsOverride = GetStringArraySetting(settingsElement, "workspace", "projectPaths");
        if (projectPathsOverride != null && !AreEquivalent(projectPathsOverride, _workspaceProjectPathsOverride))
        {
            _workspaceProjectPathsOverride = projectPathsOverride;
            reloadWorkspace = true;
        }

        if (projectSearchPaths != null && !AreEquivalent(projectSearchPaths, _workspaceProjectSearchPaths))
        {
            _workspaceProjectSearchPaths = projectSearchPaths;
            reloadWorkspace = true;
        }

        if (excludePaths != null && !AreEquivalent(excludePaths, _workspaceExcludePaths))
        {
            _workspaceExcludePaths = excludePaths;
            reloadWorkspace = true;
        }

        if (reloadWorkspace && !string.IsNullOrWhiteSpace(_workspaceRootUri))
        {
            await LoadWorkspaceAsync(_workspaceRootUri, ct);
        }
    }

    private async Task HandleDidChangeWatchedFilesAsync(DidChangeWatchedFilesParams? @params, CancellationToken ct)
    {
        if (@params?.Changes == null || @params.Changes.Length == 0)
        {
            return;
        }

        var reloadWorkspace = false;

        foreach (var change in @params.Changes)
        {
            if (string.IsNullOrWhiteSpace(change.Uri))
            {
                continue;
            }

            var filePath = TryGetFilePath(change.Uri);
            if (string.IsNullOrEmpty(filePath))
            {
                continue;
            }

            if (IsWorkspaceDefinitionFile(filePath))
            {
                if (ShouldReloadForWorkspaceFile(filePath))
                {
                    reloadWorkspace = true;
                    break;
                }

                continue;
            }

            if (IsVbFile(filePath))
            {
                if (change.Type == FileChangeType.Deleted)
                {
                    if (_workspaceManager.GetDocumentByUri(change.Uri) != null)
                    {
                        reloadWorkspace = true;
                        break;
                    }
                }
                else
                {
                    var updated = await _documentManager.TryUpdateClosedDocumentFromDiskAsync(change.Uri, ct);
                    if (!updated && change.Type == FileChangeType.Created)
                    {
                        reloadWorkspace = true;
                        break;
                    }
                }
            }
        }

        if (reloadWorkspace)
        {
            await _workspaceManager.ReloadWorkspaceAsync(ct);
            _documentManager.ReassociateDocumentsWithWorkspace();
        }
    }

    #endregion

    #region Language Features

    private async Task<CompletionList> HandleCompletionAsync(CompletionParams? @params, CancellationToken ct)
    {
        if (@params == null)
        {
            return new CompletionList { IsIncomplete = false, Items = Array.Empty<CompletionItem>() };
        }

        if (!_completionEnabled)
        {
            return new CompletionList { IsIncomplete = false, Items = Array.Empty<CompletionItem>() };
        }

        return await _completionService.GetCompletionAsync(@params, ct);
    }

    private async Task<CompletionItem> HandleCompletionResolveAsync(CompletionItem? item, CancellationToken ct)
    {
        if (item == null)
        {
            return new CompletionItem { Label = "" };
        }

        if (!_completionEnabled)
        {
            return item;
        }

        return await _completionService.ResolveCompletionItemAsync(item, ct);
    }

    private async Task<Hover?> HandleHoverAsync(HoverParams? @params, CancellationToken ct)
    {
        if (@params == null)
        {
            return null;
        }

        return await _hoverService.GetHoverAsync(@params, ct);
    }

    private async Task<Location[]> HandleDefinitionAsync(DefinitionParams? @params, CancellationToken ct)
    {
        if (@params == null)
        {
            return Array.Empty<Location>();
        }

        return await _definitionService.GetDefinitionAsync(@params, ct);
    }

    private async Task<Location[]> HandleReferencesAsync(ReferenceParams? @params, CancellationToken ct)
    {
        if (@params == null)
        {
            return Array.Empty<Location>();
        }

        return await _referencesService.GetReferencesAsync(@params, ct);
    }

    private async Task<PrepareRenameResult?> HandlePrepareRenameAsync(PrepareRenameParams? @params, CancellationToken ct)
    {
        if (@params == null)
        {
            return null;
        }

        return await _renameService.PrepareRenameAsync(@params, ct);
    }

    private async Task<WorkspaceEdit?> HandleRenameAsync(RenameParams? @params, CancellationToken ct)
    {
        if (@params == null)
        {
            return null;
        }

        return await _renameService.RenameAsync(@params, ct);
    }

    private async Task<DocumentSymbol[]> HandleDocumentSymbolAsync(DocumentSymbolParams? @params, CancellationToken ct)
    {
        if (@params == null)
        {
            return Array.Empty<DocumentSymbol>();
        }

        return await _symbolsService.GetDocumentSymbolsAsync(@params, ct);
    }

    private async Task<SymbolInformation[]> HandleWorkspaceSymbolAsync(WorkspaceSymbolParams? @params, CancellationToken ct)
    {
        if (@params == null)
        {
            return Array.Empty<SymbolInformation>();
        }

        return await _symbolsService.GetWorkspaceSymbolsAsync(@params, ct);
    }

    #endregion

    /// <summary>
    /// Builds the server capabilities based on what we support.
    /// Conservative in MVP - only advertise what's implemented and tested.
    /// </summary>
    private static ServerCapabilities BuildServerCapabilities()
    {
        return new ServerCapabilities
        {
            // UTF-16 encoding (Roslyn default) - Architecture Decision 14.6
            PositionEncoding = "utf-16",

            // Text document synchronization - incremental per Architecture Decision 14.7
            TextDocumentSync = new TextDocumentSyncOptions
            {
                OpenClose = true,
                Change = TextDocumentSyncKind.Incremental,
                Save = new SaveOptions { IncludeText = false }
            },

            // Completion support
            CompletionProvider = new CompletionOptions
            {
                TriggerCharacters = new[] { "." },
                ResolveProvider = true
            },

            // Core navigation features
            HoverProvider = true,
            DefinitionProvider = true,
            ReferencesProvider = true,

            // Rename support with prepare
            RenameProvider = new RenameOptions
            {
                PrepareProvider = true
            },

            // Symbol navigation
            DocumentSymbolProvider = true,
            WorkspaceSymbolProvider = true
        };
    }

    /// <summary>
    /// Sends a notification to the client.
    /// </summary>
    public Task SendNotificationAsync<TParams>(string method, TParams parameters, CancellationToken ct = default)
    {
        return _dispatcher.SendNotificationAsync(method, parameters, ct);
    }

    /// <summary>
    /// Helper method for publishing diagnostics (passed to DiagnosticsService).
    /// </summary>
    private Task PublishDiagnosticsAsync(string method, PublishDiagnosticsParams parameters, CancellationToken ct)
    {
        return _dispatcher.SendNotificationAsync(method, parameters, ct);
    }

    /// <summary>
    /// Gets the diagnostics service.
    /// </summary>
    public DiagnosticsService DiagnosticsService => _diagnosticsService;

    /// <summary>
    /// Gets the completion service.
    /// </summary>
    public CompletionService CompletionService => _completionService;

    /// <summary>
    /// Gets the hover service.
    /// </summary>
    public HoverService HoverService => _hoverService;

    /// <summary>
    /// Gets the definition service.
    /// </summary>
    public DefinitionService DefinitionService => _definitionService;

    /// <summary>
    /// Gets the references service.
    /// </summary>
    public ReferencesService ReferencesService => _referencesService;

    /// <summary>
    /// Gets the rename service.
    /// </summary>
    public RenameService RenameService => _renameService;

    /// <summary>
    /// Gets the symbols service.
    /// </summary>
    public SymbolsService SymbolsService => _symbolsService;

    internal MessageDispatcher Dispatcher => _dispatcher;

    /// <summary>
    /// Converts a file URI to a local file path, handling Windows path quirks.
    /// </summary>
    private static string UriToLocalPath(string uri)
    {
        var parsedUri = new Uri(uri);
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

    private static string? TryGetFilePath(string uri)
    {
        if (string.IsNullOrWhiteSpace(uri))
        {
            return null;
        }

        try
        {
            return UriToLocalPath(uri);
        }
        catch (UriFormatException)
        {
            return null;
        }
    }

    private static bool IsWorkspaceDefinitionFile(string filePath)
    {
        var fileName = Path.GetFileName(filePath);
        return filePath.EndsWith(".sln", StringComparison.OrdinalIgnoreCase) ||
               filePath.EndsWith(".slnf", StringComparison.OrdinalIgnoreCase) ||
               filePath.EndsWith(".vbproj", StringComparison.OrdinalIgnoreCase) ||
               string.Equals(fileName, "Directory.Build.props", StringComparison.OrdinalIgnoreCase) ||
               string.Equals(fileName, "Directory.Build.targets", StringComparison.OrdinalIgnoreCase);
    }

    private bool ShouldReloadForWorkspaceFile(string filePath)
    {
        if (filePath.EndsWith(".sln", StringComparison.OrdinalIgnoreCase) ||
            filePath.EndsWith(".slnf", StringComparison.OrdinalIgnoreCase))
        {
            return !string.IsNullOrEmpty(_workspaceManager.LoadedSolutionPath) &&
                string.Equals(Path.GetFullPath(_workspaceManager.LoadedSolutionPath), Path.GetFullPath(filePath), StringComparison.OrdinalIgnoreCase);
        }

        if (filePath.EndsWith(".vbproj", StringComparison.OrdinalIgnoreCase))
        {
            if (!string.IsNullOrEmpty(_workspaceManager.LoadedSolutionPath))
            {
                return true;
            }

            return _workspaceManager.IsProjectLoaded(filePath);
        }

        return _workspaceManager.IsLoaded;
    }

    private static bool IsVbFile(string filePath)
    {
        return filePath.EndsWith(".vb", StringComparison.OrdinalIgnoreCase);
    }

    private static JsonElement ExtractSettingsElement(object settings)
    {
        if (settings is JsonElement element)
        {
            return element;
        }

        var json = JsonSerializer.Serialize(settings, JsonSerializerOptionsProvider.Options);
        return JsonSerializer.Deserialize<JsonElement>(json, JsonSerializerOptionsProvider.Options);
    }

    private static bool? GetBooleanSetting(JsonElement settings, string section, string name)
    {
        var root = settings;
        if (settings.ValueKind == JsonValueKind.Object &&
            settings.TryGetProperty("vbnet", out var vbnetSettings) &&
            vbnetSettings.ValueKind == JsonValueKind.Object)
        {
            root = vbnetSettings;
        }

        if (TryGetBooleanSetting(root, section, name, out var value))
        {
            return value;
        }

        if (TryGetBooleanSetting(root, $"{section}.{name}", null, out value))
        {
            return value;
        }

        return null;
    }

    private void ApplyInitializationOptions(JsonElement? initializationOptions)
    {
        if (initializationOptions == null || initializationOptions.Value.ValueKind != JsonValueKind.Object)
        {
            return;
        }

        var root = initializationOptions.Value;
        if (root.TryGetProperty("workspace", out var workspaceOptions) &&
            workspaceOptions.ValueKind == JsonValueKind.Object)
        {
            if (TryGetStringValue(workspaceOptions, "solutionPath", out var solutionPath))
            {
                _workspaceSolutionPathOverride = solutionPath;
            }

            if (TryGetStringArrayValue(workspaceOptions, "projectPaths", out var projectPaths))
            {
                _workspaceProjectPathsOverride = projectPaths;
            }

            if (TryGetStringArrayValue(workspaceOptions, "projectSearchPaths", out var projectSearchPaths))
            {
                _workspaceProjectSearchPaths = projectSearchPaths;
            }

            if (TryGetStringArrayValue(workspaceOptions, "excludePaths", out var excludePaths))
            {
                _workspaceExcludePaths = excludePaths;
            }

            if (workspaceOptions.TryGetProperty("ignoreSolutionFiles", out var ignoreSolutionElement) &&
                TryGetBooleanValue(ignoreSolutionElement, out var ignoreSolution))
            {
                _ignoreSolutionFiles = ignoreSolution;
            }
        }
    }

    private static bool TryGetStringValue(JsonElement root, string name, out string? value)
    {
        value = null;
        if (root.ValueKind != JsonValueKind.Object || !root.TryGetProperty(name, out var element))
        {
            return false;
        }

        if (element.ValueKind != JsonValueKind.String)
        {
            return false;
        }

        value = element.GetString();
        return !string.IsNullOrWhiteSpace(value);
    }

    private static bool TryGetStringArrayValue(JsonElement root, string name, out string[]? value)
    {
        value = null;
        if (root.ValueKind != JsonValueKind.Object || !root.TryGetProperty(name, out var element))
        {
            return false;
        }

        return TryGetStringArrayValue(element, out value);
    }

    private static string? GetStringSetting(JsonElement settings, string section, string name)
    {
        var root = settings;
        if (settings.ValueKind == JsonValueKind.Object &&
            settings.TryGetProperty("vbnet", out var vbnetSettings) &&
            vbnetSettings.ValueKind == JsonValueKind.Object)
        {
            root = vbnetSettings;
        }

        if (TryGetStringSetting(root, section, name, out var value))
        {
            return value;
        }

        if (TryGetStringSetting(root, $"{section}.{name}", null, out value))
        {
            return value;
        }

        return null;
    }

    private static bool TryGetStringSetting(
        JsonElement root,
        string section,
        string? name,
        out string? value)
    {
        value = null;

        if (root.ValueKind != JsonValueKind.Object)
        {
            return false;
        }

        if (!root.TryGetProperty(section, out var sectionElement))
        {
            return false;
        }

        if (name == null)
        {
            return TryGetStringValue(sectionElement, out value);
        }

        if (sectionElement.ValueKind != JsonValueKind.Object ||
            !sectionElement.TryGetProperty(name, out var valueElement))
        {
            return false;
        }

        return TryGetStringValue(valueElement, out value);
    }

    private static bool TryGetStringValue(JsonElement element, out string? value)
    {
        value = null;
        if (element.ValueKind == JsonValueKind.String)
        {
            value = element.GetString();
            return !string.IsNullOrWhiteSpace(value);
        }

        return false;
    }

    private static string? ResolvePath(string? pathValue, string rootPath)
    {
        if (string.IsNullOrWhiteSpace(pathValue))
        {
            return null;
        }

        var trimmed = pathValue.Trim();
        var resolved = Path.IsPathRooted(trimmed)
            ? trimmed
            : Path.Combine(rootPath, trimmed);

        return File.Exists(resolved) ? resolved : null;
    }

    private static string? ResolveDirectoryPath(string pathValue, string rootPath)
    {
        if (string.IsNullOrWhiteSpace(pathValue))
        {
            return null;
        }

        var trimmed = pathValue.Trim();
        var resolved = Path.IsPathRooted(trimmed)
            ? trimmed
            : Path.Combine(rootPath, trimmed);

        return Directory.Exists(resolved) ? resolved : null;
    }

    private static bool SolutionContainsVbProject(string solutionPath)
    {
        try
        {
            var content = File.ReadAllText(solutionPath);
            return content.IndexOf(".vbproj", StringComparison.OrdinalIgnoreCase) >= 0;
        }
        catch
        {
            return true;
        }
    }

    private IEnumerable<string> GetProjectSearchRoots(string rootPath)
    {
        if (_workspaceProjectSearchPaths == null || _workspaceProjectSearchPaths.Length == 0)
        {
            return GetAncestorRoots(rootPath);
        }

        var resolvedRoots = new List<string>();
        foreach (var path in _workspaceProjectSearchPaths)
        {
            var resolved = ResolveDirectoryPath(path, rootPath);
            if (!string.IsNullOrEmpty(resolved))
            {
                resolvedRoots.Add(resolved);
            }
        }

        return resolvedRoots.Count > 0 ? resolvedRoots : new[] { rootPath };
    }

    private IEnumerable<string> GetAncestorRoots(string rootPath)
    {
        yield return rootPath;

        var current = Directory.GetParent(rootPath);
        var depth = 0;
        while (current != null && depth < MaxAncestorSearchDepth)
        {
            yield return current.FullName;
            current = current.Parent;
            depth++;
        }
    }

    private string? FindSolutionPath(string rootPath)
    {
        foreach (var searchRoot in GetAncestorRoots(rootPath))
        {
            if (!Directory.Exists(searchRoot))
            {
                continue;
            }

            var solutionCandidates = Directory.EnumerateFiles(searchRoot, "*.sln", SearchOption.TopDirectoryOnly)
                .Concat(Directory.EnumerateFiles(searchRoot, "*.slnf", SearchOption.TopDirectoryOnly))
                .OrderBy(path => path, StringComparer.OrdinalIgnoreCase)
                .ToList();

            if (solutionCandidates.Count == 0)
            {
                continue;
            }

            var solutionPath = solutionCandidates[0];
            if (!string.Equals(searchRoot, rootPath, StringComparison.OrdinalIgnoreCase))
            {
                _logger.LogInformation("Workspace root is subfolder; using ancestor solution: {Path}", solutionPath);
            }

            if (solutionCandidates.Count > 1)
            {
                _logger.LogInformation("Multiple solutions found in {Root}, using: {Path}", searchRoot, solutionPath);
            }

            return solutionPath;
        }

        return null;
    }

    private static bool ShouldExcludePath(string path, string[]? excludePaths)
    {
        if (excludePaths == null || excludePaths.Length == 0)
        {
            return false;
        }

        var fullPath = Path.GetFullPath(path);
        var segments = fullPath.Split(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar)
            .Where(segment => !string.IsNullOrWhiteSpace(segment))
            .ToArray();

        foreach (var exclude in excludePaths)
        {
            if (string.IsNullOrWhiteSpace(exclude))
            {
                continue;
            }

            var trimmed = exclude.Trim();
            if (segments.Any(segment => string.Equals(segment, trimmed, StringComparison.OrdinalIgnoreCase)))
            {
                return true;
            }
        }

        return false;
    }

    private static List<string> CollectVbProjFiles(
        IEnumerable<string> roots,
        string[]? excludePaths,
        CancellationToken cancellationToken)
    {
        var results = new List<string>();
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (var root in roots)
        {
            foreach (var file in EnumerateVbProjFiles(root, excludePaths, cancellationToken))
            {
                if (seen.Add(file))
                {
                    results.Add(file);
                }
            }
        }

        return results;
    }

    private static IEnumerable<string> EnumerateVbProjFiles(
        string root,
        string[]? excludePaths,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(root) || !Directory.Exists(root))
        {
            yield break;
        }

        var stack = new Stack<string>();
        stack.Push(root);

        while (stack.Count > 0)
        {
            cancellationToken.ThrowIfCancellationRequested();

            var current = stack.Pop();
            if (ShouldExcludePath(current, excludePaths))
            {
                continue;
            }

            IEnumerable<string> files;
            IEnumerable<string> directories;

            try
            {
                files = Directory.EnumerateFiles(current, "*.vbproj", SearchOption.TopDirectoryOnly);
                directories = Directory.EnumerateDirectories(current);
            }
            catch (IOException)
            {
                continue;
            }
            catch (UnauthorizedAccessException)
            {
                continue;
            }

            foreach (var file in files)
            {
                if (!ShouldExcludePath(file, excludePaths))
                {
                    yield return file;
                }
            }

            foreach (var directory in directories)
            {
                stack.Push(directory);
            }
        }
    }

    private static string[]? GetStringArraySetting(JsonElement settings, string section, string name)
    {
        var root = settings;
        if (settings.ValueKind == JsonValueKind.Object &&
            settings.TryGetProperty("vbnet", out var vbnetSettings) &&
            vbnetSettings.ValueKind == JsonValueKind.Object)
        {
            root = vbnetSettings;
        }

        if (TryGetStringArraySetting(root, section, name, out var value))
        {
            return value;
        }

        if (TryGetStringArraySetting(root, $"{section}.{name}", null, out value))
        {
            return value;
        }

        return null;
    }

    private static bool TryGetStringArraySetting(
        JsonElement root,
        string section,
        string? name,
        out string[]? value)
    {
        value = null;

        if (root.ValueKind != JsonValueKind.Object)
        {
            return false;
        }

        if (!root.TryGetProperty(section, out var sectionElement))
        {
            return false;
        }

        if (name == null)
        {
            return TryGetStringArrayValue(sectionElement, out value);
        }

        if (sectionElement.ValueKind != JsonValueKind.Object ||
            !sectionElement.TryGetProperty(name, out var valueElement))
        {
            return false;
        }

        return TryGetStringArrayValue(valueElement, out value);
    }

    private static bool TryGetStringArrayValue(JsonElement element, out string[]? value)
    {
        value = null;

        if (element.ValueKind == JsonValueKind.Array)
        {
            var list = new List<string>();
            foreach (var item in element.EnumerateArray())
            {
                if (item.ValueKind == JsonValueKind.String)
                {
                    var text = item.GetString();
                    if (!string.IsNullOrWhiteSpace(text))
                    {
                        list.Add(text.Trim());
                    }
                }
            }

            value = list.ToArray();
            return true;
        }

        return false;
    }

    private static bool AreEquivalent(string[]? left, string[]? right)
    {
        if (left == null && right == null)
        {
            return true;
        }

        if (left == null || right == null)
        {
            return false;
        }

        return left.SequenceEqual(right, StringComparer.OrdinalIgnoreCase);
    }

    private static bool TryGetBooleanSetting(
        JsonElement root,
        string section,
        string? name,
        out bool value)
    {
        value = false;

        if (root.ValueKind != JsonValueKind.Object)
        {
            return false;
        }

        if (!root.TryGetProperty(section, out var sectionElement))
        {
            return false;
        }

        if (name == null)
        {
            return TryGetBooleanValue(sectionElement, out value);
        }

        if (sectionElement.ValueKind != JsonValueKind.Object ||
            !sectionElement.TryGetProperty(name, out var valueElement))
        {
            return false;
        }

        return TryGetBooleanValue(valueElement, out value);
    }

    private static bool TryGetBooleanValue(JsonElement element, out bool value)
    {
        value = false;
        if (element.ValueKind == JsonValueKind.True)
        {
            value = true;
            return true;
        }

        if (element.ValueKind == JsonValueKind.False)
        {
            value = false;
            return true;
        }

        return false;
    }

    private void TriggerDiagnosticsForOpenDocuments()
    {
        foreach (var uri in _documentManager.OpenDocumentUris)
        {
            _diagnosticsService.TriggerDiagnostics(uri);
        }
    }

    private async Task ClearDiagnosticsForOpenDocumentsAsync(CancellationToken ct)
    {
        foreach (var uri in _documentManager.OpenDocumentUris)
        {
            await _diagnosticsService.ClearDiagnosticsAsync(uri, ct);
        }
    }

    public async ValueTask DisposeAsync()
    {
        _shutdownCts.Cancel();
        _shutdownCts.Dispose();
        _diagnosticsService.Dispose();
        await _workspaceManager.DisposeAsync();
        await _transport.DisposeAsync();
    }
}

/// <summary>
/// Represents the state of the language server.
/// </summary>
public enum ServerState
{
    /// <summary>Server has not been started.</summary>
    NotStarted,

    /// <summary>Server is starting, waiting for transport connection.</summary>
    Starting,

    /// <summary>Server received initialize, processing.</summary>
    Initializing,

    /// <summary>Server is fully initialized and running.</summary>
    Running,

    /// <summary>Server received shutdown request.</summary>
    ShuttingDown,

    /// <summary>Server has stopped.</summary>
    Stopped
}
