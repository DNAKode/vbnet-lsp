// Main Language Server class - orchestrates all server components
// Follows the architecture defined in docs/architecture.md

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

    private ServerState _state = ServerState.NotStarted;
    private InitializeParams? _initializeParams;
    private TaskCompletionSource? _shutdownRequested;

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

        // Language features
        _dispatcher.RegisterRequest<CompletionParams, CompletionList>("textDocument/completion", HandleCompletionAsync);
        _dispatcher.RegisterRequest<CompletionItem, CompletionItem>("completionItem/resolve", HandleCompletionResolveAsync);
        _dispatcher.RegisterRequest<HoverParams, Hover?>("textDocument/hover", HandleHoverAsync);

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
            await LoadWorkspaceAsync(_initializeParams.RootUri, ct);
        }
        else if (_initializeParams?.WorkspaceFolders?.Length > 0)
        {
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
        try
        {
            var rootPath = new Uri(rootUri).LocalPath;

            if (!Directory.Exists(rootPath))
            {
                _logger.LogWarning("Workspace root does not exist: {Path}", rootPath);
                return;
            }

            // Search for solution files (per architecture: search for .sln, if multiple use nearest to root)
            var slnFiles = Directory.GetFiles(rootPath, "*.sln", SearchOption.AllDirectories)
                .OrderBy(f => f.Split(Path.DirectorySeparatorChar).Length)
                .ToList();

            if (slnFiles.Count > 0)
            {
                var solutionPath = slnFiles[0];
                if (slnFiles.Count > 1)
                {
                    _logger.LogInformation("Multiple solutions found, using nearest to root: {Path}", solutionPath);
                }
                await _workspaceManager.LoadSolutionAsync(solutionPath, ct);
                return;
            }

            // No solution, search for VB.NET projects
            var vbprojFiles = Directory.GetFiles(rootPath, "*.vbproj", SearchOption.AllDirectories).ToList();

            if (vbprojFiles.Count > 0)
            {
                _logger.LogInformation("No solution found, loading {Count} VB.NET project(s)", vbprojFiles.Count);
                foreach (var projectPath in vbprojFiles)
                {
                    await _workspaceManager.LoadProjectAsync(projectPath, ct);
                }
                return;
            }

            _logger.LogInformation("No solution or VB.NET projects found in workspace");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to load workspace from: {Uri}", rootUri);
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

    private Task HandleDidCloseAsync(DidCloseTextDocumentParams? @params, CancellationToken ct)
    {
        if (@params == null) return Task.CompletedTask;

        _documentManager.HandleDidClose(@params);
        return Task.CompletedTask;
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

    #endregion

    #region Language Features

    private async Task<CompletionList> HandleCompletionAsync(CompletionParams? @params, CancellationToken ct)
    {
        if (@params == null)
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
