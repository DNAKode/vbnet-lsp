// Main Language Server class - orchestrates all server components
// Follows the architecture defined in docs/architecture.md

using Microsoft.Extensions.Logging;
using VbNet.LanguageServer.Protocol;

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
    private readonly CancellationTokenSource _shutdownCts = new();

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
        _logger = loggerFactory.CreateLogger<LanguageServer>();
        _dispatcher = new MessageDispatcher(transport, loggerFactory.CreateLogger<MessageDispatcher>());

        RegisterHandlers();
    }

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

        // Text document synchronization (will be connected to DocumentManager in Phase 1)
        _dispatcher.RegisterNotification<DidOpenTextDocumentParams>("textDocument/didOpen", HandleDidOpenAsync);
        _dispatcher.RegisterNotification<DidCloseTextDocumentParams>("textDocument/didClose", HandleDidCloseAsync);
        _dispatcher.RegisterNotification<DidChangeTextDocumentParams>("textDocument/didChange", HandleDidChangeAsync);
        _dispatcher.RegisterNotification<DidSaveTextDocumentParams>("textDocument/didSave", HandleDidSaveAsync);

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

    private Task HandleInitializedAsync(CancellationToken ct)
    {
        if (_state != ServerState.Initializing)
        {
            _logger.LogWarning("Received initialized notification in unexpected state: {State}", _state);
            return Task.CompletedTask;
        }

        _state = ServerState.Running;
        _logger.LogInformation("Server initialized and running");

        // TODO: Trigger workspace loading here (Phase 1)

        return Task.CompletedTask;
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

    #region Text Document Handlers (Stubs for Phase 1)

    private Task HandleDidOpenAsync(DidOpenTextDocumentParams? @params, CancellationToken ct)
    {
        if (@params?.TextDocument == null) return Task.CompletedTask;

        _logger.LogDebug("Document opened: {Uri}", @params.TextDocument.Uri);

        // TODO: Connect to DocumentManager (Phase 1)

        return Task.CompletedTask;
    }

    private Task HandleDidCloseAsync(DidCloseTextDocumentParams? @params, CancellationToken ct)
    {
        if (@params?.TextDocument == null) return Task.CompletedTask;

        _logger.LogDebug("Document closed: {Uri}", @params.TextDocument.Uri);

        // TODO: Connect to DocumentManager (Phase 1)

        return Task.CompletedTask;
    }

    private Task HandleDidChangeAsync(DidChangeTextDocumentParams? @params, CancellationToken ct)
    {
        if (@params?.TextDocument == null) return Task.CompletedTask;

        _logger.LogDebug("Document changed: {Uri} (version {Version})",
            @params.TextDocument.Uri, @params.TextDocument.Version);

        // TODO: Connect to DocumentManager (Phase 1)

        return Task.CompletedTask;
    }

    private Task HandleDidSaveAsync(DidSaveTextDocumentParams? @params, CancellationToken ct)
    {
        if (@params?.TextDocument == null) return Task.CompletedTask;

        _logger.LogDebug("Document saved: {Uri}", @params.TextDocument.Uri);

        // TODO: Trigger diagnostics update (Phase 1)

        return Task.CompletedTask;
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

    public async ValueTask DisposeAsync()
    {
        _shutdownCts.Cancel();
        _shutdownCts.Dispose();
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
