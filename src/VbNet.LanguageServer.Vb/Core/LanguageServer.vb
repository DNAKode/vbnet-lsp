' Main Language Server class - orchestrates all server components
' Follows the architecture defined in docs/architecture.md

Imports System.Text.Json
Imports Microsoft.Extensions.Logging
Imports VbNet.LanguageServer.Protocol
Imports VbNet.LanguageServer.Services
Imports VbNet.LanguageServer.Workspace

Namespace Core

    ''' <summary>
    ''' The main VB.NET Language Server class.
    ''' Manages the server lifecycle, routes requests, and coordinates language services.
    ''' </summary>
    Public NotInheritable Class LanguageServer
        Implements IAsyncDisposable

        Private ReadOnly _transport As ITransport
        Private ReadOnly _dispatcher As MessageDispatcher
        Private ReadOnly _logger As ILogger(Of LanguageServer)
        Private ReadOnly _loggerFactory As ILoggerFactory
        Private ReadOnly _shutdownCts As CancellationTokenSource = New CancellationTokenSource()

        ' Workspace layer components
        Private ReadOnly _workspaceManager As WorkspaceManager
        Private ReadOnly _documentManager As DocumentManager

        ' Services layer components
        Private ReadOnly _diagnosticsService As DiagnosticsService
        Private ReadOnly _completionService As CompletionService
        Private ReadOnly _hoverService As HoverService
        Private ReadOnly _definitionService As DefinitionService
        Private ReadOnly _referencesService As ReferencesService
        Private ReadOnly _renameService As RenameService
        Private ReadOnly _symbolsService As SymbolsService
        Private ReadOnly _foldingRangeService As FoldingRangeService
        Private ReadOnly _formattingService As FormattingService
        Private ReadOnly _signatureHelpService As SignatureHelpService
        Private ReadOnly _semanticTokensService As SemanticTokensService
        Private ReadOnly _codeActionsService As CodeActionsService
        Private ReadOnly _callHierarchyService As CallHierarchyService
        Private ReadOnly _typeHierarchyService As TypeHierarchyService
        Private ReadOnly _documentHighlightService As DocumentHighlightService
        Private ReadOnly _selectionRangeService As SelectionRangeService
        Private ReadOnly _typeDefinitionService As TypeDefinitionService
        Private ReadOnly _implementationService As ImplementationService
        Private ReadOnly _documentLinkService As DocumentLinkService

        Private _state As ServerState = ServerState.NotStarted
        Private _initializeParams As InitializeParams
        Private _shutdownRequested As TaskCompletionSource
        Private _diagnosticsEnabled As Boolean = True
        Private _completionEnabled As Boolean = True
        Private _workspaceRootUri As String
        Private _workspaceSolutionPathOverride As String
        Private _workspaceProjectPathsOverride As String()
        Private _ignoreSolutionFiles As Boolean
        Private _workspaceProjectSearchPaths As String()
        Private _workspaceExcludePaths As String()
        Private _workspaceMaxProjectResults As Integer = 250
        Private Const MaxAncestorSearchDepth As Integer = 4
        Private ReadOnly _reportedNetFxProjects As New HashSet(Of String)(StringComparer.OrdinalIgnoreCase)
        Private _netFxWarningSent As Boolean
        Private _restoreHintSent As Boolean

        ''' <summary>
        ''' Server name reported in initialize response.
        ''' </summary>
        Public Const ServerName As String = "VbNet.LanguageServer"

        ''' <summary>
        ''' Server version reported in initialize response.
        ''' </summary>
        Public Const ServerVersion As String = "0.1.7"

        Public Sub New(transport As ITransport, loggerFactory As ILoggerFactory)
            If transport Is Nothing Then
                Throw New ArgumentNullException(NameOf(transport))
            End If
            If loggerFactory Is Nothing Then
                Throw New ArgumentNullException(NameOf(loggerFactory))
            End If

            _transport = transport
            _loggerFactory = loggerFactory
            _logger = loggerFactory.CreateLogger(Of LanguageServer)()
            _dispatcher = New MessageDispatcher(transport, loggerFactory.CreateLogger(Of MessageDispatcher)())

            ' Initialize workspace layer
            _workspaceManager = New WorkspaceManager(loggerFactory.CreateLogger(Of WorkspaceManager)())
            _documentManager = New DocumentManager(_workspaceManager, loggerFactory.CreateLogger(Of DocumentManager)())
            AddHandler _workspaceManager.WorkspaceDiagnostic, AddressOf OnWorkspaceDiagnostic

            ' Initialize services layer
            _diagnosticsService = New DiagnosticsService(
                _workspaceManager,
                _documentManager,
                AddressOf PublishDiagnosticsAsync,
                loggerFactory.CreateLogger(Of DiagnosticsService)())

            _completionService = New CompletionService(
                _workspaceManager,
                _documentManager,
                loggerFactory.CreateLogger(Of CompletionService)())

            _hoverService = New HoverService(
                _workspaceManager,
                _documentManager,
                loggerFactory.CreateLogger(Of HoverService)())

            _definitionService = New DefinitionService(
                _workspaceManager,
                _documentManager,
                loggerFactory.CreateLogger(Of DefinitionService)())

            _referencesService = New ReferencesService(
                _workspaceManager,
                _documentManager,
                loggerFactory.CreateLogger(Of ReferencesService)())

            _renameService = New RenameService(
                _workspaceManager,
                _documentManager,
                loggerFactory.CreateLogger(Of RenameService)())

            _symbolsService = New SymbolsService(
                _workspaceManager,
                _documentManager,
                loggerFactory.CreateLogger(Of SymbolsService)())

            _foldingRangeService = New FoldingRangeService(
                _workspaceManager,
                _documentManager,
                loggerFactory.CreateLogger(Of FoldingRangeService)())

            _formattingService = New FormattingService(
                _workspaceManager,
                _documentManager,
                loggerFactory.CreateLogger(Of FormattingService)())

            _signatureHelpService = New SignatureHelpService(
                _workspaceManager,
                _documentManager,
                loggerFactory.CreateLogger(Of SignatureHelpService)())

            _semanticTokensService = New SemanticTokensService(
                _workspaceManager,
                _documentManager,
                loggerFactory.CreateLogger(Of SemanticTokensService)())

            _codeActionsService = New CodeActionsService(
                _workspaceManager,
                _documentManager,
                loggerFactory.CreateLogger(Of CodeActionsService)())

            _callHierarchyService = New CallHierarchyService(
                _workspaceManager,
                _documentManager,
                loggerFactory.CreateLogger(Of CallHierarchyService)())

            _typeHierarchyService = New TypeHierarchyService(
                _workspaceManager,
                _documentManager,
                loggerFactory.CreateLogger(Of TypeHierarchyService)())

            _documentHighlightService = New DocumentHighlightService(
                _workspaceManager,
                _documentManager,
                loggerFactory.CreateLogger(Of DocumentHighlightService)())

            _selectionRangeService = New SelectionRangeService(
                _documentManager,
                loggerFactory.CreateLogger(Of SelectionRangeService)())

            _typeDefinitionService = New TypeDefinitionService(
                _workspaceManager,
                _documentManager,
                loggerFactory.CreateLogger(Of TypeDefinitionService)())

            _implementationService = New ImplementationService(
                _workspaceManager,
                _documentManager,
                loggerFactory.CreateLogger(Of ImplementationService)())

            _documentLinkService = New DocumentLinkService(
                _documentManager,
                loggerFactory.CreateLogger(Of DocumentLinkService)())

            RegisterHandlers()
        End Sub

        ''' <summary>
        ''' Gets the workspace manager for project/solution operations.
        ''' </summary>
        Public ReadOnly Property WorkspaceManager As WorkspaceManager
            Get
                Return _workspaceManager
            End Get
        End Property

        ''' <summary>
        ''' Gets the document manager for open document operations.
        ''' </summary>
        Public ReadOnly Property DocumentManager As DocumentManager
            Get
                Return _documentManager
            End Get
        End Property

        ''' <summary>
        ''' Gets the current server state.
        ''' </summary>
        Public ReadOnly Property State As ServerState
            Get
                Return _state
            End Get
        End Property

        ''' <summary>
        ''' Gets the client's initialization parameters (available after initialize).
        ''' </summary>
        Public ReadOnly Property InitializeParams As InitializeParams
            Get
                Return _initializeParams
            End Get
        End Property

        ''' <summary>
        ''' Sends a notification to the client.
        ''' </summary>
        Public Function SendNotificationAsync(Of TParams)(method As String, parameters As TParams, Optional ct As CancellationToken = Nothing) As Task
            Return _dispatcher.SendNotificationAsync(method, parameters, ct)
        End Function

        ''' <summary>
        ''' Gets the diagnostics service.
        ''' </summary>
        Public ReadOnly Property DiagnosticsService As DiagnosticsService
            Get
                Return _diagnosticsService
            End Get
        End Property

        ''' <summary>
        ''' Gets the completion service.
        ''' </summary>
        Public ReadOnly Property CompletionService As CompletionService
            Get
                Return _completionService
            End Get
        End Property

        ''' <summary>
        ''' Gets the hover service.
        ''' </summary>
        Public ReadOnly Property HoverService As HoverService
            Get
                Return _hoverService
            End Get
        End Property

        ''' <summary>
        ''' Gets the definition service.
        ''' </summary>
        Public ReadOnly Property DefinitionService As DefinitionService
            Get
                Return _definitionService
            End Get
        End Property

        ''' <summary>
        ''' Gets the references service.
        ''' </summary>
        Public ReadOnly Property ReferencesService As ReferencesService
            Get
                Return _referencesService
            End Get
        End Property

        ''' <summary>
        ''' Gets the rename service.
        ''' </summary>
        Public ReadOnly Property RenameService As RenameService
            Get
                Return _renameService
            End Get
        End Property

        ''' <summary>
        ''' Gets the symbols service.
        ''' </summary>
        Public ReadOnly Property SymbolsService As SymbolsService
            Get
                Return _symbolsService
            End Get
        End Property

        ''' <summary>
        ''' Gets the folding range service.
        ''' </summary>
        Public ReadOnly Property FoldingRangeService As FoldingRangeService
            Get
                Return _foldingRangeService
            End Get
        End Property

        ''' <summary>
        ''' Gets the formatting service.
        ''' </summary>
        Public ReadOnly Property FormattingService As FormattingService
            Get
                Return _formattingService
            End Get
        End Property

        ''' <summary>
        ''' Gets the signature help service.
        ''' </summary>
        Public ReadOnly Property SignatureHelpService As SignatureHelpService
            Get
                Return _signatureHelpService
            End Get
        End Property

        ''' <summary>
        ''' Gets the semantic tokens service.
        ''' </summary>
        Public ReadOnly Property SemanticTokensService As SemanticTokensService
            Get
                Return _semanticTokensService
            End Get
        End Property

        ''' <summary>
        ''' Gets the code actions service.
        ''' </summary>
        Public ReadOnly Property CodeActionsService As CodeActionsService
            Get
                Return _codeActionsService
            End Get
        End Property

        Friend ReadOnly Property Dispatcher As MessageDispatcher
            Get
                Return _dispatcher
            End Get
        End Property

        ''' <summary>
        ''' Registers all LSP request and notification handlers.
        ''' </summary>
        Private Sub RegisterHandlers()
            ' Lifecycle
            _dispatcher.RegisterRequest(Of InitializeParams, InitializeResult)("initialize", AddressOf HandleInitializeAsync)
            _dispatcher.RegisterNotification("initialized", AddressOf HandleInitializedAsync)
            _dispatcher.RegisterRequest(Of Object, Object)("shutdown", AddressOf HandleShutdownAsync)
            _dispatcher.RegisterNotification("exit", AddressOf HandleExitAsync)

            ' Text document synchronization
            _dispatcher.RegisterNotification(Of DidOpenTextDocumentParams)("textDocument/didOpen", AddressOf HandleDidOpenAsync)
            _dispatcher.RegisterNotification(Of DidCloseTextDocumentParams)("textDocument/didClose", AddressOf HandleDidCloseAsync)
            _dispatcher.RegisterNotification(Of DidChangeTextDocumentParams)("textDocument/didChange", AddressOf HandleDidChangeAsync)
            _dispatcher.RegisterNotification(Of DidSaveTextDocumentParams)("textDocument/didSave", AddressOf HandleDidSaveAsync)

            ' Workspace notifications
            _dispatcher.RegisterNotification(Of DidChangeConfigurationParams)("workspace/didChangeConfiguration", AddressOf HandleDidChangeConfigurationAsync)
            _dispatcher.RegisterNotification(Of DidChangeWatchedFilesParams)("workspace/didChangeWatchedFiles", AddressOf HandleDidChangeWatchedFilesAsync)
            _dispatcher.RegisterNotification("vbnet/reloadWorkspace", AddressOf HandleReloadWorkspaceAsync)

            ' Language features
            _dispatcher.RegisterRequest(Of CompletionParams, CompletionList)("textDocument/completion", AddressOf HandleCompletionAsync)
            _dispatcher.RegisterRequest(Of CompletionItem, CompletionItem)("completionItem/resolve", AddressOf HandleCompletionResolveAsync)
            _dispatcher.RegisterRequest(Of HoverParams, Hover)("textDocument/hover", AddressOf HandleHoverAsync)
            _dispatcher.RegisterRequest(Of DefinitionParams, Location())("textDocument/definition", AddressOf HandleDefinitionAsync)
            _dispatcher.RegisterRequest(Of ReferenceParams, Location())("textDocument/references", AddressOf HandleReferencesAsync)
            _dispatcher.RegisterRequest(Of PrepareRenameParams, PrepareRenameResult)("textDocument/prepareRename", AddressOf HandlePrepareRenameAsync)
            _dispatcher.RegisterRequest(Of RenameParams, WorkspaceEdit)("textDocument/rename", AddressOf HandleRenameAsync)
            _dispatcher.RegisterRequest(Of DocumentSymbolParams, DocumentSymbol())("textDocument/documentSymbol", AddressOf HandleDocumentSymbolAsync)
            _dispatcher.RegisterRequest(Of WorkspaceSymbolParams, SymbolInformation())("workspace/symbol", AddressOf HandleWorkspaceSymbolAsync)
            _dispatcher.RegisterRequest(Of FoldingRangeParams, FoldingRange())("textDocument/foldingRange", AddressOf HandleFoldingRangeAsync)
            _dispatcher.RegisterRequest(Of DocumentFormattingParams, TextEdit())("textDocument/formatting", AddressOf HandleDocumentFormattingAsync)
            _dispatcher.RegisterRequest(Of DocumentRangeFormattingParams, TextEdit())("textDocument/rangeFormatting", AddressOf HandleDocumentRangeFormattingAsync)
            _dispatcher.RegisterRequest(Of SignatureHelpParams, SignatureHelp)("textDocument/signatureHelp", AddressOf HandleSignatureHelpAsync)
            _dispatcher.RegisterRequest(Of SemanticTokensParams, SemanticTokens)("textDocument/semanticTokens/full", AddressOf HandleSemanticTokensAsync)
            _dispatcher.RegisterRequest(Of SemanticTokensRangeParams, SemanticTokens)("textDocument/semanticTokens/range", AddressOf HandleSemanticTokensRangeAsync)
            _dispatcher.RegisterRequest(Of CodeActionParams, CodeAction())("textDocument/codeAction", AddressOf HandleCodeActionAsync)
            _dispatcher.RegisterRequest(Of CodeAction, CodeAction)("codeAction/resolve", AddressOf HandleCodeActionResolveAsync)
            _dispatcher.RegisterRequest(Of DocumentHighlightParams, DocumentHighlight())("textDocument/documentHighlight", AddressOf HandleDocumentHighlightAsync)
            _dispatcher.RegisterRequest(Of SelectionRangeParams, SelectionRange())("textDocument/selectionRange", AddressOf HandleSelectionRangeAsync)
            _dispatcher.RegisterRequest(Of TextDocumentDiagnosticParams, DocumentDiagnosticReport)("textDocument/diagnostic", AddressOf HandleTextDocumentDiagnosticAsync)
            _dispatcher.RegisterRequest(Of WorkspaceDiagnosticParams, WorkspaceDiagnosticReport)("workspace/diagnostic", AddressOf HandleWorkspaceDiagnosticAsync)
            _dispatcher.RegisterRequest(Of TypeDefinitionParams, Location())("textDocument/typeDefinition", AddressOf HandleTypeDefinitionAsync)
            _dispatcher.RegisterRequest(Of ImplementationParams, Location())("textDocument/implementation", AddressOf HandleImplementationAsync)
            _dispatcher.RegisterRequest(Of DocumentLinkParams, DocumentLink())("textDocument/documentLink", AddressOf HandleDocumentLinkAsync)
            _dispatcher.RegisterRequest(Of CallHierarchyPrepareParams, CallHierarchyItem())("textDocument/prepareCallHierarchy", AddressOf HandlePrepareCallHierarchyAsync)
            _dispatcher.RegisterRequest(Of CallHierarchyIncomingCallsParams, CallHierarchyIncomingCall())("callHierarchy/incomingCalls", AddressOf HandleIncomingCallsAsync)
            _dispatcher.RegisterRequest(Of CallHierarchyOutgoingCallsParams, CallHierarchyOutgoingCall())("callHierarchy/outgoingCalls", AddressOf HandleOutgoingCallsAsync)
            _dispatcher.RegisterRequest(Of TypeHierarchyPrepareParams, TypeHierarchyItem())("textDocument/prepareTypeHierarchy", AddressOf HandlePrepareTypeHierarchyAsync)
            _dispatcher.RegisterRequest(Of TypeHierarchySupertypesParams, TypeHierarchyItem())("typeHierarchy/supertypes", AddressOf HandleTypeHierarchySupertypesAsync)
            _dispatcher.RegisterRequest(Of TypeHierarchySubtypesParams, TypeHierarchyItem())("typeHierarchy/subtypes", AddressOf HandleTypeHierarchySubtypesAsync)

            _logger.LogDebug("All LSP handlers registered")
        End Sub

        ''' <summary>
        ''' Starts the language server and begins processing messages.
        ''' </summary>
        Public Async Function RunAsync(Optional cancellationToken As CancellationToken = Nothing) As Task
            If _state <> ServerState.NotStarted Then
                Throw New InvalidOperationException($"Server cannot be started in state: {_state}")
            End If

            _state = ServerState.Starting
            _logger.LogInformation("VB.NET Language Server starting...")

            Await _transport.StartAsync(cancellationToken).ConfigureAwait(False)
            _logger.LogInformation("Transport ready, waiting for initialize request")

            Using linkedCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, _shutdownCts.Token)
                Try
                    Await _dispatcher.RunAsync(linkedCts.Token).ConfigureAwait(False)
                Catch ex As OperationCanceledException When _shutdownCts.IsCancellationRequested
                    _logger.LogInformation("Server shutdown completed")
                End Try
            End Using

            _state = ServerState.Stopped
            _logger.LogInformation("VB.NET Language Server stopped")
        End Function

#Region "Lifecycle Handlers"

        Private Function HandleInitializeAsync(parameters As InitializeParams, ct As CancellationToken) As Task(Of InitializeResult)
            If _state <> ServerState.Starting Then
                Throw New InvalidOperationException($"Initialize not allowed in state: {_state}")
            End If

            _initializeParams = parameters
            _state = ServerState.Initializing

            ApplyInitializationOptions(If(parameters Is Nothing, Nothing, parameters.InitializationOptions))

            _logger.LogInformation("Initialize request received from client: {ClientName} {ClientVersion}",
                If(parameters?.ClientInfo?.Name, "unknown"),
                If(parameters?.ClientInfo?.Version, "unknown"))

            If parameters?.RootUri IsNot Nothing Then
                _logger.LogInformation("Workspace root: {RootUri}", parameters.RootUri)
            End If

            Dim capabilities = BuildServerCapabilities()

            Dim result = New InitializeResult With {
                .Capabilities = capabilities,
                .ServerInfo = New ServerInfo With {
                    .Name = ServerName,
                    .Version = ServerVersion
                }
            }

            Return Task.FromResult(result)
        End Function

        Private Async Function HandleInitializedAsync(ct As CancellationToken) As Task
            If _state <> ServerState.Initializing Then
                _logger.LogWarning("Received initialized notification in unexpected state: {State}", _state)
                Return
            End If

            _state = ServerState.Running
            _logger.LogInformation("Server initialized and running")

            _workspaceManager.Initialize()

            If _initializeParams?.RootUri IsNot Nothing Then
                _workspaceRootUri = _initializeParams.RootUri
                Await LoadWorkspaceAsync(_initializeParams.RootUri, ct).ConfigureAwait(False)
            ElseIf _initializeParams?.WorkspaceFolders IsNot Nothing AndAlso _initializeParams.WorkspaceFolders.Length > 0 Then
                _workspaceRootUri = _initializeParams.WorkspaceFolders(0).Uri
                Await LoadWorkspaceAsync(_initializeParams.WorkspaceFolders(0).Uri, ct).ConfigureAwait(False)
            Else
                _logger.LogWarning("No workspace root provided, operating in single-file mode")
            End If
        End Function

        ''' <summary>
        ''' Loads a workspace from the given root URI.
        ''' Searches for .sln files first, then .vbproj files.
        ''' </summary>
        Private Async Function LoadWorkspaceAsync(rootUri As String, ct As CancellationToken) As Task
            Dim loadSucceeded = False
            Try
                Dim rootPath = UriToLocalPath(rootUri)

                If Not Directory.Exists(rootPath) Then
                    _logger.LogWarning("Workspace root does not exist: {Path}", rootPath)
                    Return
                End If

                Dim projectSearchRoots = GetProjectSearchRoots(rootPath)
                Dim vbprojFiles = CollectVbProjFiles(
                    projectSearchRoots,
                    _workspaceExcludePaths,
                    _workspaceMaxProjectResults,
                    ct)

                Await ReportNetFxSupportWarningsAsync(vbprojFiles, ct).ConfigureAwait(False)

                If Not String.IsNullOrWhiteSpace(_workspaceSolutionPathOverride) Then
                    Dim explicitSolutionPath = ResolvePath(_workspaceSolutionPathOverride, rootPath)
                    If Not String.IsNullOrEmpty(explicitSolutionPath) Then
                        Dim explicitProjects = GetSolutionProjectPaths(explicitSolutionPath)
                        Await ReportNetFxSupportWarningsAsync(explicitProjects, ct).ConfigureAwait(False)
                        loadSucceeded = Await _workspaceManager.LoadSolutionAsync(explicitSolutionPath, ct).ConfigureAwait(False)
                        Return
                    End If
                End If

                If _workspaceProjectPathsOverride IsNot Nothing AndAlso _workspaceProjectPathsOverride.Length > 0 Then
                    Dim anyLoaded = False
                    Await ReportNetFxSupportWarningsAsync(_workspaceProjectPathsOverride, ct).ConfigureAwait(False)
                    For Each projectPath In _workspaceProjectPathsOverride
                        If String.IsNullOrWhiteSpace(projectPath) OrElse Not projectPath.EndsWith(".vbproj", StringComparison.OrdinalIgnoreCase) Then
                            Continue For
                        End If

                        Dim resolved = ResolvePath(projectPath, rootPath)
                        If String.IsNullOrEmpty(resolved) Then
                            Continue For
                        End If

                        anyLoaded = anyLoaded OrElse Await _workspaceManager.LoadProjectAsync(resolved, ct).ConfigureAwait(False)
                    Next

                    loadSucceeded = anyLoaded
                    If anyLoaded Then
                        Return
                    End If
                End If

                If Not _ignoreSolutionFiles Then
                    Dim solutionCandidates = FindSolutionCandidates(rootPath)
                    If solutionCandidates.Count > 0 Then
                        For Each candidate In solutionCandidates
                            If SolutionContainsVbProject(candidate) Then
                                Dim solutionProjects = GetSolutionProjectPaths(candidate)
                                Await ReportNetFxSupportWarningsAsync(solutionProjects, ct).ConfigureAwait(False)
                                Dim loadedVb = Await _workspaceManager.LoadSolutionAsync(candidate, ct).ConfigureAwait(False)
                                loadSucceeded = loadedVb
                                If loadedVb Then
                                    Return
                                End If
                            Else
                                _logger.LogInformation("Skipping solution without VB.NET projects: {Path}", candidate)
                            End If
                        Next
                    End If
                End If

                If _workspaceMaxProjectResults > 0 AndAlso vbprojFiles.Count >= _workspaceMaxProjectResults Then
                    _logger.LogInformation("Project search capped at {Max} results", _workspaceMaxProjectResults)
                End If

                If vbprojFiles.Count > 0 Then
                    _logger.LogInformation("No solution found, loading {Count} VB.NET project(s)", vbprojFiles.Count)
                    For Each projectPath In vbprojFiles
                        Dim loaded = Await _workspaceManager.LoadProjectAsync(projectPath, ct).ConfigureAwait(False)
                        loadSucceeded = loadSucceeded OrElse loaded
                    Next
                    Return
                End If

                _logger.LogInformation("No solution or VB.NET projects found in workspace")
            Catch ex As Exception
                _logger.LogError(ex, "Failed to load workspace from: {Uri}", rootUri)
            Finally
                _workspaceManager.SignalInitialLoadCompleted(loadSucceeded)
            End Try
        End Function

        Private Function HandleShutdownAsync(parameters As Object, ct As CancellationToken) As Task(Of Object)
            _logger.LogInformation("Shutdown request received")
            _state = ServerState.ShuttingDown
            _shutdownRequested = New TaskCompletionSource()

            Return Task.FromResult(Of Object)(Nothing)
        End Function

        Private Function HandleExitAsync(ct As CancellationToken) As Task
            _logger.LogInformation("Exit notification received")

            _shutdownCts.Cancel()
            _shutdownRequested?.TrySetResult()

            Return Task.CompletedTask
        End Function

#End Region
#Region "Text Document Handlers"

        Private Function HandleDidOpenAsync(parameters As DidOpenTextDocumentParams, ct As CancellationToken) As Task
            If parameters Is Nothing Then
                Return Task.CompletedTask
            End If

            _documentManager.HandleDidOpen(parameters)
            Return Task.CompletedTask
        End Function

        Private Async Function HandleDidCloseAsync(parameters As DidCloseTextDocumentParams, ct As CancellationToken) As Task
            If parameters Is Nothing Then
                Return
            End If

            _documentManager.HandleDidClose(parameters)
            Await _diagnosticsService.ClearDiagnosticsAsync(parameters.TextDocument.Uri, ct).ConfigureAwait(False)
        End Function

        Private Function HandleDidChangeAsync(parameters As DidChangeTextDocumentParams, ct As CancellationToken) As Task
            If parameters Is Nothing Then
                Return Task.CompletedTask
            End If

            _documentManager.HandleDidChange(parameters)
            Return Task.CompletedTask
        End Function

        Private Function HandleDidSaveAsync(parameters As DidSaveTextDocumentParams, ct As CancellationToken) As Task
            If parameters Is Nothing Then
                Return Task.CompletedTask
            End If

            _documentManager.HandleDidSave(parameters)
            Return Task.CompletedTask
        End Function

#End Region

#Region "Workspace Notifications"

        Private Async Function HandleDidChangeConfigurationAsync(parameters As DidChangeConfigurationParams, ct As CancellationToken) As Task
            If parameters Is Nothing OrElse parameters.Settings Is Nothing Then
                Return
            End If

            Dim settingsElement As JsonElement
            If TypeOf parameters.Settings Is JsonElement Then
                settingsElement = DirectCast(parameters.Settings, JsonElement)
            Else
                settingsElement = JsonSerializer.SerializeToElement(parameters.Settings, JsonSerializerOptionsProvider.Options)
            End If

            Dim enableDiagnostics = GetRootStringSetting(settingsElement, "diagnostics.enable")
            If enableDiagnostics IsNot Nothing Then
                _diagnosticsEnabled = String.Equals(enableDiagnostics, "true", StringComparison.OrdinalIgnoreCase)
                If _diagnosticsEnabled Then
                    TriggerDiagnosticsForOpenDocuments()
                Else
                    Await ClearDiagnosticsForOpenDocumentsAsync(ct).ConfigureAwait(False)
                End If
            End If

            Dim enableCompletion = GetRootStringSetting(settingsElement, "completion.enable")
            If enableCompletion IsNot Nothing Then
                _completionEnabled = String.Equals(enableCompletion, "true", StringComparison.OrdinalIgnoreCase)
            End If

            Dim solutionPathOverride = GetStringSetting(settingsElement, "workspace", "solutionPath")
            Dim legacySolutionPath = GetRootStringSetting(settingsElement, "solutionPath")
            If String.IsNullOrWhiteSpace(solutionPathOverride) AndAlso legacySolutionPath IsNot Nothing Then
                solutionPathOverride = legacySolutionPath
            End If
            Dim projectPathsOverride = GetStringArraySetting(settingsElement, "workspace", "projectPaths")
            Dim projectSearchPaths = GetStringArraySetting(settingsElement, "workspace", "projectSearchPaths")
            Dim excludePaths = GetStringArraySetting(settingsElement, "workspace", "excludePaths")
            Dim maxProjectResults = GetIntSetting(settingsElement, "workspace", "maxProjectResults")
            Dim ignoreSolutionFiles = GetRootStringSetting(settingsElement, "workspace.ignoreSolutionFiles")

            Dim needReload = False
            If solutionPathOverride IsNot Nothing AndAlso Not String.Equals(solutionPathOverride, _workspaceSolutionPathOverride, StringComparison.OrdinalIgnoreCase) Then
                _workspaceSolutionPathOverride = solutionPathOverride
                needReload = True
            End If

            If projectPathsOverride IsNot Nothing AndAlso Not AreEquivalent(projectPathsOverride, _workspaceProjectPathsOverride) Then
                _workspaceProjectPathsOverride = projectPathsOverride
                needReload = True
            End If

            If projectSearchPaths IsNot Nothing AndAlso Not AreEquivalent(projectSearchPaths, _workspaceProjectSearchPaths) Then
                _workspaceProjectSearchPaths = projectSearchPaths
                needReload = True
            End If

            If excludePaths IsNot Nothing AndAlso Not AreEquivalent(excludePaths, _workspaceExcludePaths) Then
                _workspaceExcludePaths = excludePaths
                needReload = True
            End If

            If maxProjectResults.HasValue AndAlso maxProjectResults.Value <> _workspaceMaxProjectResults Then
                _workspaceMaxProjectResults = maxProjectResults.Value
                needReload = True
            End If

            If ignoreSolutionFiles IsNot Nothing Then
                Dim ignoreValue = String.Equals(ignoreSolutionFiles, "true", StringComparison.OrdinalIgnoreCase)
                If ignoreValue <> _ignoreSolutionFiles Then
                    _ignoreSolutionFiles = ignoreValue
                    needReload = True
                End If
            End If

            If needReload AndAlso _workspaceRootUri IsNot Nothing Then
                _logger.LogInformation("Workspace configuration changed; reloading workspace")
                Await LoadWorkspaceAsync(_workspaceRootUri, ct).ConfigureAwait(False)
            End If
        End Function

        Private Async Function HandleDidChangeWatchedFilesAsync(parameters As DidChangeWatchedFilesParams, ct As CancellationToken) As Task
            If parameters Is Nothing OrElse parameters.Changes Is Nothing OrElse parameters.Changes.Length = 0 Then
                Return
            End If

            Dim reloadWorkspace = False

            For Each change In parameters.Changes
                If String.IsNullOrWhiteSpace(change.Uri) Then
                    Continue For
                End If

                Dim filePath = TryGetFilePath(change.Uri)
                If String.IsNullOrWhiteSpace(filePath) Then
                    Continue For
                End If

                If IsWorkspaceDefinitionFile(filePath) Then
                    If ShouldReloadForWorkspaceFile(filePath) Then
                        reloadWorkspace = True
                        Exit For
                    End If

                    Continue For
                End If

                If IsVbFile(filePath) Then
                    If change.Type = FileChangeType.Deleted Then
                        If _workspaceManager.GetDocumentByUri(change.Uri) IsNot Nothing Then
                            reloadWorkspace = True
                            Exit For
                        End If
                    Else
                        Dim updated = Await _documentManager.TryUpdateClosedDocumentFromDiskAsync(change.Uri, ct).ConfigureAwait(False)
                        If Not updated AndAlso change.Type = FileChangeType.Created Then
                            reloadWorkspace = True
                            Exit For
                        End If
                    End If
                End If
            Next

            If reloadWorkspace Then
                Await _workspaceManager.ReloadWorkspaceAsync(ct).ConfigureAwait(False)
                _documentManager.ReassociateDocumentsWithWorkspace()
            End If
        End Function

        Private Async Function HandleReloadWorkspaceAsync(ct As CancellationToken) As Task
            If String.IsNullOrEmpty(_workspaceRootUri) Then
                _logger.LogWarning("Workspace reload requested but no workspace root is set.")
                Return
            End If

            _logger.LogInformation("Workspace reload requested by client.")
            Dim reloaded = Await _workspaceManager.ReloadWorkspaceAsync(ct).ConfigureAwait(False)
            _documentManager.ReassociateDocumentsWithWorkspace()

            If reloaded AndAlso _diagnosticsEnabled Then
                TriggerDiagnosticsForOpenDocuments()
            End If
        End Function

        Private Sub OnWorkspaceDiagnostic(sender As Object, args As Microsoft.CodeAnalysis.WorkspaceDiagnosticEventArgs)
            Dim message = args?.Diagnostic?.Message
            If String.IsNullOrWhiteSpace(message) Then
                Return
            End If

            If Not _restoreHintSent AndAlso IsRestoreRelatedMessage(message) Then
                _restoreHintSent = True
                Dim hint = "Restore appears incomplete. Run 'VB.NET: Restore Workspace' or 'VB.NET: Restore Project' and reopen the solution."
                Dim ignore = SendWindowMessageAsync(MessageType.Warning, hint)
            End If
        End Sub

        Private Shared Function IsRestoreRelatedMessage(message As String) As Boolean
            Dim lowered = message.ToLowerInvariant()
            Return lowered.Contains("restore") OrElse
                lowered.Contains("project.assets.json") OrElse
                lowered.Contains("assets file") OrElse
                lowered.Contains("nu1301") OrElse
                lowered.Contains("nu1101")
        End Function

#End Region

#Region "Language Feature Handlers"

        Private Async Function HandleCompletionAsync(parameters As CompletionParams, ct As CancellationToken) As Task(Of CompletionList)
            If parameters Is Nothing OrElse Not _completionEnabled Then
                Return New CompletionList()
            End If

            Return Await _completionService.GetCompletionAsync(parameters, ct).ConfigureAwait(False)
        End Function

        Private Async Function HandleCompletionResolveAsync(parameters As CompletionItem, ct As CancellationToken) As Task(Of CompletionItem)
            If parameters Is Nothing OrElse Not _completionEnabled Then
                Return parameters
            End If

            Return Await _completionService.ResolveCompletionItemAsync(parameters, ct).ConfigureAwait(False)
        End Function

        Private Async Function HandleHoverAsync(parameters As HoverParams, ct As CancellationToken) As Task(Of Hover)
            If parameters Is Nothing Then
                Return Nothing
            End If

            Return Await _hoverService.GetHoverAsync(parameters, ct).ConfigureAwait(False)
        End Function

        Private Async Function HandleDefinitionAsync(parameters As DefinitionParams, ct As CancellationToken) As Task(Of Location())
            If parameters Is Nothing Then
                Return Array.Empty(Of Location)()
            End If

            Return Await _definitionService.GetDefinitionAsync(parameters, ct).ConfigureAwait(False)
        End Function

        Private Async Function HandleReferencesAsync(parameters As ReferenceParams, ct As CancellationToken) As Task(Of Location())
            If parameters Is Nothing Then
                Return Array.Empty(Of Location)()
            End If

            Return Await _referencesService.GetReferencesAsync(parameters, ct).ConfigureAwait(False)
        End Function

        Private Async Function HandlePrepareRenameAsync(parameters As PrepareRenameParams, ct As CancellationToken) As Task(Of PrepareRenameResult)
            If parameters Is Nothing Then
                Return Nothing
            End If

            Return Await _renameService.PrepareRenameAsync(parameters, ct).ConfigureAwait(False)
        End Function

        Private Async Function HandleRenameAsync(parameters As RenameParams, ct As CancellationToken) As Task(Of WorkspaceEdit)
            If parameters Is Nothing Then
                Return Nothing
            End If

            Return Await _renameService.RenameAsync(parameters, ct).ConfigureAwait(False)
        End Function

        Private Async Function HandleDocumentSymbolAsync(parameters As DocumentSymbolParams, ct As CancellationToken) As Task(Of DocumentSymbol())
            If parameters Is Nothing Then
                Return Array.Empty(Of DocumentSymbol)()
            End If

            Return Await _symbolsService.GetDocumentSymbolsAsync(parameters, ct).ConfigureAwait(False)
        End Function

        Private Async Function HandleWorkspaceSymbolAsync(parameters As WorkspaceSymbolParams, ct As CancellationToken) As Task(Of SymbolInformation())
            If parameters Is Nothing Then
                Return Array.Empty(Of SymbolInformation)()
            End If

            Return Await _symbolsService.GetWorkspaceSymbolsAsync(parameters, ct).ConfigureAwait(False)
        End Function

        Private Async Function HandleFoldingRangeAsync(parameters As FoldingRangeParams, ct As CancellationToken) As Task(Of FoldingRange())
            If parameters Is Nothing Then
                Return Array.Empty(Of FoldingRange)()
            End If

            Return Await _foldingRangeService.GetFoldingRangesAsync(parameters, ct).ConfigureAwait(False)
        End Function

        Private Async Function HandleDocumentFormattingAsync(parameters As DocumentFormattingParams, ct As CancellationToken) As Task(Of TextEdit())
            If parameters Is Nothing Then
                Return Array.Empty(Of TextEdit)()
            End If

            Return Await _formattingService.FormatDocumentAsync(parameters, ct).ConfigureAwait(False)
        End Function

        Private Async Function HandleDocumentRangeFormattingAsync(parameters As DocumentRangeFormattingParams, ct As CancellationToken) As Task(Of TextEdit())
            If parameters Is Nothing Then
                Return Array.Empty(Of TextEdit)()
            End If

            Return Await _formattingService.FormatRangeAsync(parameters, ct).ConfigureAwait(False)
        End Function

        Private Async Function HandleSignatureHelpAsync(parameters As SignatureHelpParams, ct As CancellationToken) As Task(Of SignatureHelp)
            If parameters Is Nothing Then
                Return Nothing
            End If

            Return Await _signatureHelpService.GetSignatureHelpAsync(parameters, ct).ConfigureAwait(False)
        End Function

        Private Async Function HandleSemanticTokensAsync(parameters As SemanticTokensParams, ct As CancellationToken) As Task(Of SemanticTokens)
            If parameters Is Nothing Then
                Return New SemanticTokens()
            End If

            Return Await _semanticTokensService.GetSemanticTokensAsync(parameters, ct).ConfigureAwait(False)
        End Function

        Private Async Function HandleSemanticTokensRangeAsync(parameters As SemanticTokensRangeParams, ct As CancellationToken) As Task(Of SemanticTokens)
            If parameters Is Nothing Then
                Return New SemanticTokens()
            End If

            Return Await _semanticTokensService.GetSemanticTokensRangeAsync(parameters, ct).ConfigureAwait(False)
        End Function

        Private Async Function HandleCodeActionAsync(parameters As CodeActionParams, ct As CancellationToken) As Task(Of CodeAction())
            If parameters Is Nothing Then
                Return Array.Empty(Of CodeAction)()
            End If

            Return Await _codeActionsService.GetCodeActionsAsync(parameters, ct).ConfigureAwait(False)
        End Function

        Private Async Function HandleCodeActionResolveAsync(parameters As CodeAction, ct As CancellationToken) As Task(Of CodeAction)
            If parameters Is Nothing Then
                Return Nothing
            End If

            Return Await _codeActionsService.ResolveCodeActionAsync(parameters, ct).ConfigureAwait(False)
        End Function

        Private Async Function HandleDocumentHighlightAsync(parameters As DocumentHighlightParams, ct As CancellationToken) As Task(Of DocumentHighlight())
            If parameters Is Nothing Then
                Return Array.Empty(Of DocumentHighlight)()
            End If

            Return Await _documentHighlightService.GetDocumentHighlightsAsync(parameters, ct).ConfigureAwait(False)
        End Function

        Private Async Function HandleSelectionRangeAsync(parameters As SelectionRangeParams, ct As CancellationToken) As Task(Of SelectionRange())
            If parameters Is Nothing Then
                Return Array.Empty(Of SelectionRange)()
            End If

            Return Await _selectionRangeService.GetSelectionRangesAsync(parameters, ct).ConfigureAwait(False)
        End Function

        Private Async Function HandleTextDocumentDiagnosticAsync(parameters As TextDocumentDiagnosticParams, ct As CancellationToken) As Task(Of DocumentDiagnosticReport)
            If parameters Is Nothing Then
                Return New DocumentDiagnosticReport()
            End If

            Return Await _diagnosticsService.GetDocumentDiagnosticsReportAsync(parameters, ct).ConfigureAwait(False)
        End Function

        Private Async Function HandleWorkspaceDiagnosticAsync(parameters As WorkspaceDiagnosticParams, ct As CancellationToken) As Task(Of WorkspaceDiagnosticReport)
            Return Await _diagnosticsService.GetWorkspaceDiagnosticsReportAsync(parameters, ct).ConfigureAwait(False)
        End Function

        Private Async Function HandleTypeDefinitionAsync(parameters As TypeDefinitionParams, ct As CancellationToken) As Task(Of Location())
            If parameters Is Nothing Then
                Return Array.Empty(Of Location)()
            End If

            Return Await _typeDefinitionService.GetTypeDefinitionAsync(parameters, ct).ConfigureAwait(False)
        End Function

        Private Async Function HandleImplementationAsync(parameters As ImplementationParams, ct As CancellationToken) As Task(Of Location())
            If parameters Is Nothing Then
                Return Array.Empty(Of Location)()
            End If

            Return Await _implementationService.GetImplementationAsync(parameters, ct).ConfigureAwait(False)
        End Function

        Private Async Function HandleDocumentLinkAsync(parameters As DocumentLinkParams, ct As CancellationToken) As Task(Of DocumentLink())
            If parameters Is Nothing Then
                Return Array.Empty(Of DocumentLink)()
            End If

            Return Await _documentLinkService.GetDocumentLinksAsync(parameters, ct).ConfigureAwait(False)
        End Function

        Private Async Function HandlePrepareCallHierarchyAsync(parameters As CallHierarchyPrepareParams, ct As CancellationToken) As Task(Of CallHierarchyItem())
            If parameters Is Nothing Then
                Return Array.Empty(Of CallHierarchyItem)()
            End If

            Return Await _callHierarchyService.PrepareCallHierarchyAsync(parameters, ct).ConfigureAwait(False)
        End Function

        Private Async Function HandleIncomingCallsAsync(parameters As CallHierarchyIncomingCallsParams, ct As CancellationToken) As Task(Of CallHierarchyIncomingCall())
            If parameters Is Nothing Then
                Return Array.Empty(Of CallHierarchyIncomingCall)()
            End If

            Return Await _callHierarchyService.GetIncomingCallsAsync(parameters, ct).ConfigureAwait(False)
        End Function

        Private Async Function HandleOutgoingCallsAsync(parameters As CallHierarchyOutgoingCallsParams, ct As CancellationToken) As Task(Of CallHierarchyOutgoingCall())
            If parameters Is Nothing Then
                Return Array.Empty(Of CallHierarchyOutgoingCall)()
            End If

            Return Await _callHierarchyService.GetOutgoingCallsAsync(parameters, ct).ConfigureAwait(False)
        End Function

        Private Async Function HandlePrepareTypeHierarchyAsync(parameters As TypeHierarchyPrepareParams, ct As CancellationToken) As Task(Of TypeHierarchyItem())
            If parameters Is Nothing Then
                Return Array.Empty(Of TypeHierarchyItem)()
            End If

            Return Await _typeHierarchyService.PrepareTypeHierarchyAsync(parameters, ct).ConfigureAwait(False)
        End Function

        Private Async Function HandleTypeHierarchySupertypesAsync(parameters As TypeHierarchySupertypesParams, ct As CancellationToken) As Task(Of TypeHierarchyItem())
            If parameters Is Nothing Then
                Return Array.Empty(Of TypeHierarchyItem)()
            End If

            Return Await _typeHierarchyService.GetSupertypesAsync(parameters, ct).ConfigureAwait(False)
        End Function

        Private Async Function HandleTypeHierarchySubtypesAsync(parameters As TypeHierarchySubtypesParams, ct As CancellationToken) As Task(Of TypeHierarchyItem())
            If parameters Is Nothing Then
                Return Array.Empty(Of TypeHierarchyItem)()
            End If

            Return Await _typeHierarchyService.GetSubtypesAsync(parameters, ct).ConfigureAwait(False)
        End Function

#End Region

#Region "Diagnostics"

        Private Async Function PublishDiagnosticsAsync(method As String, parameters As PublishDiagnosticsParams, ct As CancellationToken) As Task
            If Not _diagnosticsEnabled Then
                Return
            End If

            Await _dispatcher.SendNotificationAsync(method, parameters, ct).ConfigureAwait(False)
        End Function

        Private Async Function SendWindowMessageAsync(messageType As MessageType, message As String, Optional ct As CancellationToken = Nothing) As Task
            If String.IsNullOrWhiteSpace(message) Then
                Return
            End If

            Dim parameters = New ShowMessageParams With {
                .Type = messageType,
                .Message = message
            }

            Await _dispatcher.SendNotificationAsync("window/showMessage", parameters, ct).ConfigureAwait(False)
        End Function

#End Region

#Region "Capabilities"

        Private Function BuildServerCapabilities() As ServerCapabilities
            Dim capabilities = New ServerCapabilities With {
                .PositionEncoding = "utf-16",
                .TextDocumentSync = New TextDocumentSyncOptions With {
                    .OpenClose = True,
                    .Change = TextDocumentSyncKind.Incremental,
                    .Save = New SaveOptions With {.IncludeText = False}
                },
                .CompletionProvider = New CompletionOptions With {
                    .TriggerCharacters = New String() {".", "(", "<", " ", """"},
                    .ResolveProvider = True
                },
                .HoverProvider = True,
                .DefinitionProvider = True,
                .ReferencesProvider = True,
                .RenameProvider = New RenameOptions With {.PrepareProvider = True},
                .DocumentSymbolProvider = True,
                .WorkspaceSymbolProvider = True,
                .SignatureHelpProvider = New SignatureHelpOptions With {
                    .TriggerCharacters = New String() {"(", ","},
                    .RetriggerCharacters = New String() {","}
                },
                .SemanticTokensProvider = SemanticTokensService.GetDefaultOptions(),
                .CodeActionProvider = CodeActionsService.GetDefaultOptions(),
                .DocumentHighlightProvider = True,
                .SelectionRangeProvider = True,
                .DiagnosticProvider = New DiagnosticOptions With {
                    .Identifier = "vbnet",
                    .InterFileDependencies = True,
                    .WorkspaceDiagnostics = True
                },
                .CallHierarchyProvider = True,
                .TypeHierarchyProvider = True,
                .TypeDefinitionProvider = True,
                .ImplementationProvider = True,
                .DocumentLinkProvider = New DocumentLinkOptions With {.ResolveProvider = False},
                .FoldingRangeProvider = True,
                .DocumentFormattingProvider = True,
                .DocumentRangeFormattingProvider = True
            }

            Return capabilities
        End Function

#End Region
#Region "Workspace Helpers"

        Private Shared Function UriToLocalPath(uriValue As String) As String
            Dim unescaped = System.Uri.UnescapeDataString(uriValue)
            If unescaped.StartsWith("file://", StringComparison.OrdinalIgnoreCase) Then
                unescaped = unescaped.Substring("file://".Length)
            End If

            unescaped = unescaped.Replace("/", Path.DirectorySeparatorChar.ToString())
            If unescaped.StartsWith(Path.DirectorySeparatorChar) Then
                If unescaped.Length >= 3 AndAlso Char.IsLetter(unescaped(1)) AndAlso unescaped(2) = ":"c Then
                    unescaped = unescaped.Substring(1)
                End If
            End If

            Return unescaped
        End Function

        Private Shared Function TryGetFilePath(uriValue As String) As String
            If String.IsNullOrWhiteSpace(uriValue) Then
                Return Nothing
            End If

            Try
                Return UriToLocalPath(uriValue)
            Catch ex As UriFormatException
                Return Nothing
            End Try
        End Function

        Private Shared Function IsWorkspaceDefinitionFile(filePath As String) As Boolean
            Dim fileName = Path.GetFileName(filePath)
            Return filePath.EndsWith(".sln", StringComparison.OrdinalIgnoreCase) OrElse
                filePath.EndsWith(".slnf", StringComparison.OrdinalIgnoreCase) OrElse
                filePath.EndsWith(".slnx", StringComparison.OrdinalIgnoreCase) OrElse
                filePath.EndsWith(".vbproj", StringComparison.OrdinalIgnoreCase) OrElse
                String.Equals(fileName, "Directory.Build.props", StringComparison.OrdinalIgnoreCase) OrElse
                String.Equals(fileName, "Directory.Build.targets", StringComparison.OrdinalIgnoreCase)
        End Function

        Private Function ShouldReloadForWorkspaceFile(filePath As String) As Boolean
            If filePath.EndsWith(".sln", StringComparison.OrdinalIgnoreCase) OrElse
                filePath.EndsWith(".slnf", StringComparison.OrdinalIgnoreCase) OrElse
                filePath.EndsWith(".slnx", StringComparison.OrdinalIgnoreCase) Then
                Return Not String.IsNullOrEmpty(_workspaceManager.LoadedSolutionPath) AndAlso
                    String.Equals(Path.GetFullPath(_workspaceManager.LoadedSolutionPath), Path.GetFullPath(filePath), StringComparison.OrdinalIgnoreCase)
            End If

            If filePath.EndsWith(".vbproj", StringComparison.OrdinalIgnoreCase) Then
                If Not String.IsNullOrEmpty(_workspaceManager.LoadedSolutionPath) Then
                    Return True
                End If

                Return _workspaceManager.IsProjectLoaded(filePath)
            End If

            Return _workspaceManager.IsLoaded
        End Function

        Private Shared Function IsVbFile(filePath As String) As Boolean
            Return filePath.EndsWith(".vb", StringComparison.OrdinalIgnoreCase)
        End Function

        Private Sub ApplyInitializationOptions(initializationOptions As JsonElement?)
            If Not initializationOptions.HasValue OrElse initializationOptions.Value.ValueKind <> JsonValueKind.Object Then
                Return
            End If

            Dim root = initializationOptions.Value
            Dim workspaceOptions As JsonElement
            If root.TryGetProperty("workspace", workspaceOptions) AndAlso workspaceOptions.ValueKind = JsonValueKind.Object Then
                Dim solutionPath As String = Nothing
                If TryGetStringValue(workspaceOptions, "solutionPath", solutionPath) Then
                    _workspaceSolutionPathOverride = solutionPath
                End If

                Dim projectPaths As String() = Nothing
                If TryGetStringArrayValue(workspaceOptions, "projectPaths", projectPaths) Then
                    _workspaceProjectPathsOverride = projectPaths
                End If

                Dim projectSearchPaths As String() = Nothing
                If TryGetStringArrayValue(workspaceOptions, "projectSearchPaths", projectSearchPaths) Then
                    _workspaceProjectSearchPaths = projectSearchPaths
                End If

                Dim excludePaths As String() = Nothing
                If TryGetStringArrayValue(workspaceOptions, "excludePaths", excludePaths) Then
                    _workspaceExcludePaths = excludePaths
                End If

                Dim maxProjectResults As JsonElement
                If workspaceOptions.TryGetProperty("maxProjectResults", maxProjectResults) Then
                    Dim maxResults As Integer
                    If TryGetIntValue(maxProjectResults, maxResults) Then
                        _workspaceMaxProjectResults = maxResults
                    End If
                End If

                Dim ignoreSolutionElement As JsonElement
                If workspaceOptions.TryGetProperty("ignoreSolutionFiles", ignoreSolutionElement) Then
                    Dim ignoreSolution As Boolean
                    If TryGetBooleanValue(ignoreSolutionElement, ignoreSolution) Then
                        _ignoreSolutionFiles = ignoreSolution
                    End If
                End If
            End If
        End Sub

        Private Shared Function TryGetStringValue(root As JsonElement, name As String, ByRef value As String) As Boolean
            value = Nothing
            If root.ValueKind <> JsonValueKind.Object Then
                Return False
            End If

            Dim element As JsonElement
            If Not root.TryGetProperty(name, element) Then
                Return False
            End If

            If element.ValueKind <> JsonValueKind.String Then
                Return False
            End If

            value = element.GetString()
            Return Not String.IsNullOrWhiteSpace(value)
        End Function

        Private Shared Function TryGetStringArrayValue(root As JsonElement, name As String, ByRef value As String()) As Boolean
            value = Nothing
            If root.ValueKind <> JsonValueKind.Object Then
                Return False
            End If

            Dim element As JsonElement
            If Not root.TryGetProperty(name, element) Then
                Return False
            End If

            Return TryGetStringArrayValue(element, value)
        End Function

        Private Shared Function GetStringSetting(settings As JsonElement, section As String, name As String) As String
            Dim root = settings
            Dim vbnetSettings As JsonElement
            If settings.ValueKind = JsonValueKind.Object AndAlso settings.TryGetProperty("vbnet", vbnetSettings) AndAlso vbnetSettings.ValueKind = JsonValueKind.Object Then
                root = vbnetSettings
            End If

            Dim value As String = Nothing
            If TryGetStringSetting(root, section, name, value) Then
                Return value
            End If

            If TryGetStringSetting(root, $"{section}.{name}", Nothing, value) Then
                Return value
            End If

            Return Nothing
        End Function

        Private Shared Function TryGetStringSetting(root As JsonElement, section As String, name As String, ByRef value As String) As Boolean
            value = Nothing

            If root.ValueKind <> JsonValueKind.Object Then
                Return False
            End If

            Dim sectionElement As JsonElement
            If Not root.TryGetProperty(section, sectionElement) Then
                Return False
            End If

            If name Is Nothing Then
                Return TryGetStringValue(sectionElement, value)
            End If

            Dim valueElement As JsonElement
            If sectionElement.ValueKind <> JsonValueKind.Object OrElse Not sectionElement.TryGetProperty(name, valueElement) Then
                Return False
            End If

            Return TryGetStringValue(valueElement, value)
        End Function

        Private Shared Function TryGetStringValue(element As JsonElement, ByRef value As String) As Boolean
            value = Nothing
            If element.ValueKind = JsonValueKind.String Then
                value = element.GetString()
                Return Not String.IsNullOrWhiteSpace(value)
            End If

            Return False
        End Function

        Private Shared Function ResolvePath(pathValue As String, rootPath As String) As String
            If String.IsNullOrWhiteSpace(pathValue) Then
                Return Nothing
            End If

            Dim trimmed = pathValue.Trim()
            Dim resolved = If(Path.IsPathRooted(trimmed), trimmed, Path.Combine(rootPath, trimmed))

            If File.Exists(resolved) Then
                Return resolved
            End If

            Return Nothing
        End Function

        Private Shared Function ResolveDirectoryPath(pathValue As String, rootPath As String) As String
            If String.IsNullOrWhiteSpace(pathValue) Then
                Return Nothing
            End If

            Dim trimmed = pathValue.Trim()
            Dim resolved = If(Path.IsPathRooted(trimmed), trimmed, Path.Combine(rootPath, trimmed))

            If Directory.Exists(resolved) Then
                Return resolved
            End If

            Return Nothing
        End Function

        Private Shared Function SolutionContainsVbProject(solutionPath As String) As Boolean
            Try
                If solutionPath.EndsWith(".slnx", StringComparison.OrdinalIgnoreCase) Then
                    Return True
                End If
                Dim content = File.ReadAllText(solutionPath)
                Return content.IndexOf(".vbproj", StringComparison.OrdinalIgnoreCase) >= 0
            Catch
                Return True
            End Try
        End Function

        Private Shared Function GetSolutionProjectPaths(solutionPath As String) As List(Of String)
            Dim results As New List(Of String)()
            If String.IsNullOrWhiteSpace(solutionPath) OrElse Not File.Exists(solutionPath) Then
                Return results
            End If

            Dim extension = Path.GetExtension(solutionPath).ToLowerInvariant()
            Dim resolvedSolutionPath = solutionPath

            If extension = ".slnf" Then
                Dim filterSolutionPath = TryResolveSolutionPathFromFilter(solutionPath)
                If Not String.IsNullOrWhiteSpace(filterSolutionPath) Then
                    resolvedSolutionPath = filterSolutionPath
                End If
            ElseIf extension = ".slnx" Then
                Return results
            End If

            If String.IsNullOrWhiteSpace(resolvedSolutionPath) OrElse Not File.Exists(resolvedSolutionPath) Then
                Return results
            End If

            Dim solutionDir = Path.GetDirectoryName(resolvedSolutionPath)
            For Each line In File.ReadLines(resolvedSolutionPath)
                If line.IndexOf(".vbproj", StringComparison.OrdinalIgnoreCase) < 0 Then
                    Continue For
                End If

                Dim parts = line.Split(","c)
                If parts.Length < 2 Then
                    Continue For
                End If

                Dim relativePath = parts(1).Trim().Trim(""""c)
                If Not relativePath.EndsWith(".vbproj", StringComparison.OrdinalIgnoreCase) Then
                    Continue For
                End If

                Dim projectPath = If(Path.IsPathRooted(relativePath), relativePath, Path.Combine(solutionDir, relativePath))
                results.Add(projectPath)
            Next

            Return results
        End Function

        Private Shared Function TryResolveSolutionPathFromFilter(solutionFilterPath As String) As String
            Try
                Dim json = File.ReadAllText(solutionFilterPath)
                Dim document = JsonDocument.Parse(json)
                Using document
                    Dim root = document.RootElement
                    Dim solutionElement As JsonElement
                    If root.ValueKind <> JsonValueKind.Object OrElse Not root.TryGetProperty("solution", solutionElement) Then
                        Return Nothing
                    End If

                    Dim pathElement As JsonElement
                    If solutionElement.ValueKind <> JsonValueKind.Object OrElse Not solutionElement.TryGetProperty("path", pathElement) Then
                        Return Nothing
                    End If

                    Dim relativePath = pathElement.GetString()
                    If String.IsNullOrWhiteSpace(relativePath) Then
                        Return Nothing
                    End If

                    Dim filterDir = Path.GetDirectoryName(solutionFilterPath)
                    Dim resolved = If(Path.IsPathRooted(relativePath), relativePath, Path.Combine(filterDir, relativePath))
                    Return resolved
                End Using
            Catch
                Return Nothing
            End Try
        End Function

        Private Function GetProjectSearchRoots(rootPath As String) As IEnumerable(Of String)
            If _workspaceProjectSearchPaths Is Nothing OrElse _workspaceProjectSearchPaths.Length = 0 Then
                Return GetAncestorRoots(rootPath)
            End If

            Dim resolvedRoots As New List(Of String)()
            For Each path In _workspaceProjectSearchPaths
                Dim resolved = ResolveDirectoryPath(path, rootPath)
                If Not String.IsNullOrEmpty(resolved) Then
                    resolvedRoots.Add(resolved)
                End If
            Next

            If resolvedRoots.Count > 0 Then
                Return resolvedRoots
            End If

            Return New String() {rootPath}
        End Function

        Private Function GetAncestorRoots(rootPath As String) As IEnumerable(Of String)
            Dim roots As New List(Of String) From {rootPath}

            Dim current = Directory.GetParent(rootPath)
            Dim depth = 0
            While current IsNot Nothing AndAlso depth < MaxAncestorSearchDepth
                roots.Add(current.FullName)
                current = current.Parent
                depth += 1
            End While

            Return roots
        End Function

        Private Function FindSolutionCandidates(rootPath As String) As List(Of String)
            Dim candidates As New List(Of String)()
            Dim seen As New HashSet(Of String)(StringComparer.OrdinalIgnoreCase)

            For Each searchRoot In GetAncestorRoots(rootPath)
                If Not Directory.Exists(searchRoot) Then
                    Continue For
                End If

                Dim solutionCandidates = Directory.EnumerateFiles(searchRoot, "*.sln", SearchOption.TopDirectoryOnly) _
                    .Concat(Directory.EnumerateFiles(searchRoot, "*.slnf", SearchOption.TopDirectoryOnly)) _
                    .Concat(Directory.EnumerateFiles(searchRoot, "*.slnx", SearchOption.TopDirectoryOnly)) _
                    .OrderBy(Function(path) path, StringComparer.OrdinalIgnoreCase) _
                    .ToList()

                If solutionCandidates.Count = 0 Then
                    Continue For
                End If

                If Not String.Equals(searchRoot, rootPath, StringComparison.OrdinalIgnoreCase) Then
                    _logger.LogInformation("Workspace root is subfolder; scanning ancestor solutions in: {Path}", searchRoot)
                End If

                If solutionCandidates.Count > 1 Then
                    _logger.LogInformation("Multiple solutions found in {Root}", searchRoot)
                End If

                For Each candidate In solutionCandidates
                    If seen.Add(candidate) Then
                        candidates.Add(candidate)
                    End If
                Next
            Next

            Return candidates
        End Function

        Private Shared Function ShouldExcludePath(pathValue As String, excludePaths As String()) As Boolean
            If excludePaths Is Nothing OrElse excludePaths.Length = 0 Then
                Return False
            End If

            Dim fullPath = Path.GetFullPath(pathValue)
            Dim segments = fullPath.Split(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar) _
                .Where(Function(segment) Not String.IsNullOrWhiteSpace(segment)) _
                .ToArray()

            For Each exclude In excludePaths
                If String.IsNullOrWhiteSpace(exclude) Then
                    Continue For
                End If

                Dim trimmed = exclude.Trim()
                If segments.Any(Function(segment) String.Equals(segment, trimmed, StringComparison.OrdinalIgnoreCase)) Then
                    Return True
                End If
            Next

            Return False
        End Function

        Private Shared Function CollectVbProjFiles(roots As IEnumerable(Of String), excludePaths As String(), maxResults As Integer, cancellationToken As CancellationToken) As List(Of String)
            Dim results As New List(Of String)()
            Dim seen As New HashSet(Of String)(StringComparer.OrdinalIgnoreCase)

            For Each root In roots
                For Each file In EnumerateVbProjFiles(root, excludePaths, cancellationToken)
                    If seen.Add(file) Then
                        results.Add(file)
                    End If

                    If maxResults > 0 AndAlso results.Count >= maxResults Then
                        Return results
                    End If
                Next
            Next

            Return results
        End Function

        Private Shared Iterator Function EnumerateVbProjFiles(root As String, excludePaths As String(), cancellationToken As CancellationToken) As IEnumerable(Of String)
            If String.IsNullOrWhiteSpace(root) OrElse Not Directory.Exists(root) Then
                Return
            End If

            Dim stack As New Stack(Of String)()
            stack.Push(root)

            While stack.Count > 0
                cancellationToken.ThrowIfCancellationRequested()

                Dim current = stack.Pop()
                If ShouldExcludePath(current, excludePaths) Then
                    Continue While
                End If

                Dim files As IEnumerable(Of String)
                Dim directories As IEnumerable(Of String)

                Try
                    files = Directory.EnumerateFiles(current, "*.vbproj", SearchOption.TopDirectoryOnly)
                    directories = Directory.EnumerateDirectories(current)
                Catch ex As IOException
                    Continue While
                Catch ex As UnauthorizedAccessException
                    Continue While
                End Try

                For Each file In files
                    If Not ShouldExcludePath(file, excludePaths) Then
                        Yield file
                    End If
                Next

                For Each directory In directories
                    stack.Push(directory)
                Next
            End While
        End Function

        Private Async Function ReportNetFxSupportWarningsAsync(projectPaths As IEnumerable(Of String), ct As CancellationToken) As Task
            If projectPaths Is Nothing Then
                Return
            End If

            Dim netFxTargets As New SortedSet(Of String)(StringComparer.OrdinalIgnoreCase)
            Dim netFxProjects As New List(Of String)()

            For Each projectPath In projectPaths
                If String.IsNullOrWhiteSpace(projectPath) Then
                    Continue For
                End If

                Dim fullPath = Path.GetFullPath(projectPath)
                If _reportedNetFxProjects.Contains(fullPath) Then
                    Continue For
                End If

                Dim targets = NetFxProjectInspector.GetSdkStyleNetFxTargets(fullPath)
                If targets.Length = 0 Then
                    Continue For
                End If

                _reportedNetFxProjects.Add(fullPath)
                netFxProjects.Add(Path.GetFileName(fullPath))
                For Each target In targets
                    netFxTargets.Add(target)
                Next
            Next

            If netFxTargets.Count = 0 OrElse _netFxWarningSent Then
                Return
            End If

            _netFxWarningSent = True

            Dim targetList = String.Join(", ", netFxTargets)
            Dim projectList = String.Join(", ", netFxProjects.Take(3))
            If netFxProjects.Count > 3 Then
                projectList &= ", ..."
            End If

            If Not OperatingSystem.IsWindows() Then
                Dim message = $"SDK-style .NET Framework targets ({targetList}) are not supported on non-Windows hosts. Projects: {projectList}."
                message &= " Use Windows or retarget to a modern .NET runtime."
                Await SendWindowMessageAsync(MessageType.Warning, message, ct).ConfigureAwait(False)
                Return
            End If

            Dim missingTargets = netFxTargets.Where(Function(target) Not HasNetFxReferenceAssemblies(target)).ToList()
            Dim needsFullMsbuild = IsDotnetSdkMsbuild()

            If missingTargets.Count = 0 AndAlso Not needsFullMsbuild Then
                Return
            End If

            Dim messageParts As New List(Of String)()
            If missingTargets.Count > 0 Then
                messageParts.Add($"Missing .NET Framework reference assemblies for {String.Join(", ", missingTargets)}.")
                messageParts.Add("Install the .NET Framework targeting pack or Visual Studio Build Tools.")
                messageParts.Add("Alternatively add Microsoft.NETFramework.ReferenceAssemblies to the project.")
            End If

            If needsFullMsbuild Then
                messageParts.Add("For best compatibility with net4x, set vbnet.msbuildPath to a full MSBuild (VS Build Tools).")
            End If

            If messageParts.Count > 0 Then
                Await SendWindowMessageAsync(MessageType.Warning, String.Join(" ", messageParts), ct).ConfigureAwait(False)
            End If
        End Function

        Private Shared Function HasNetFxReferenceAssemblies(targetFramework As String) As Boolean
            Dim folderName = NetFxProjectInspector.GetNetFxReferenceFolderName(targetFramework)
            If String.IsNullOrWhiteSpace(folderName) Then
                Return False
            End If

            Dim programFilesX86 = Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86)
            If String.IsNullOrWhiteSpace(programFilesX86) Then
                Return False
            End If

            Dim referencePath = Path.Combine(
                programFilesX86,
                "Reference Assemblies",
                "Microsoft",
                "Framework",
                ".NETFramework",
                folderName)

            Return Directory.Exists(referencePath)
        End Function

        Private Shared Function IsDotnetSdkMsbuild() As Boolean
            Dim msbuildPath = Environment.GetEnvironmentVariable("VBNET_MSBUILD_PATH_ACTIVE")
            If String.IsNullOrWhiteSpace(msbuildPath) Then
                Return False
            End If

            Dim normalized = msbuildPath.Replace(Path.AltDirectorySeparatorChar, Path.DirectorySeparatorChar).ToLowerInvariant()
            Dim token = $"{Path.DirectorySeparatorChar}dotnet{Path.DirectorySeparatorChar}sdk"
            Return normalized.Contains(token)
        End Function

        Private Shared Function GetStringArraySetting(settings As JsonElement, section As String, name As String) As String()
            Dim root = settings
            Dim vbnetSettings As JsonElement
            If settings.ValueKind = JsonValueKind.Object AndAlso settings.TryGetProperty("vbnet", vbnetSettings) AndAlso vbnetSettings.ValueKind = JsonValueKind.Object Then
                root = vbnetSettings
            End If

            Dim value As String() = Nothing
            If TryGetStringArraySetting(root, section, name, value) Then
                Return value
            End If

            If TryGetStringArraySetting(root, $"{section}.{name}", Nothing, value) Then
                Return value
            End If

            Return Nothing
        End Function

        Private Shared Function GetRootStringSetting(settings As JsonElement, name As String) As String
            Dim root = settings
            Dim vbnetSettings As JsonElement
            If settings.ValueKind = JsonValueKind.Object AndAlso settings.TryGetProperty("vbnet", vbnetSettings) AndAlso vbnetSettings.ValueKind = JsonValueKind.Object Then
                root = vbnetSettings
            End If

            Dim valueElement As JsonElement
            If root.ValueKind <> JsonValueKind.Object OrElse Not root.TryGetProperty(name, valueElement) Then
                Return Nothing
            End If

            Dim value As String = Nothing
            Return If(TryGetStringValue(valueElement, value), value, Nothing)
        End Function

        Private Shared Function GetRootIntSetting(settings As JsonElement, name As String) As Integer?
            Dim root = settings
            Dim vbnetSettings As JsonElement
            If settings.ValueKind = JsonValueKind.Object AndAlso settings.TryGetProperty("vbnet", vbnetSettings) AndAlso vbnetSettings.ValueKind = JsonValueKind.Object Then
                root = vbnetSettings
            End If

            Dim valueElement As JsonElement
            If root.ValueKind <> JsonValueKind.Object OrElse Not root.TryGetProperty(name, valueElement) Then
                Return Nothing
            End If

            Dim value As Integer
            If TryGetIntValue(valueElement, value) Then
                Return value
            End If

            Return Nothing
        End Function

        Private Shared Function GetIntSetting(settings As JsonElement, section As String, name As String) As Integer?
            Dim root = settings
            Dim vbnetSettings As JsonElement
            If settings.ValueKind = JsonValueKind.Object AndAlso settings.TryGetProperty("vbnet", vbnetSettings) AndAlso vbnetSettings.ValueKind = JsonValueKind.Object Then
                root = vbnetSettings
            End If

            Dim value As Integer
            If TryGetIntSetting(root, section, name, value) Then
                Return value
            End If

            If TryGetIntSetting(root, $"{section}.{name}", Nothing, value) Then
                Return value
            End If

            Return Nothing
        End Function

        Private Shared Function TryGetStringArraySetting(root As JsonElement, section As String, name As String, ByRef value As String()) As Boolean
            value = Nothing

            If root.ValueKind <> JsonValueKind.Object Then
                Return False
            End If

            Dim sectionElement As JsonElement
            If Not root.TryGetProperty(section, sectionElement) Then
                Return False
            End If

            If name Is Nothing Then
                Return TryGetStringArrayValue(sectionElement, value)
            End If

            Dim valueElement As JsonElement
            If sectionElement.ValueKind <> JsonValueKind.Object OrElse Not sectionElement.TryGetProperty(name, valueElement) Then
                Return False
            End If

            Return TryGetStringArrayValue(valueElement, value)
        End Function

        Private Shared Function TryGetIntSetting(root As JsonElement, section As String, name As String, ByRef value As Integer) As Boolean
            value = 0

            If root.ValueKind <> JsonValueKind.Object Then
                Return False
            End If

            Dim sectionElement As JsonElement
            If Not root.TryGetProperty(section, sectionElement) Then
                Return False
            End If

            If name Is Nothing Then
                Return TryGetIntValue(sectionElement, value)
            End If

            Dim valueElement As JsonElement
            If sectionElement.ValueKind <> JsonValueKind.Object OrElse Not sectionElement.TryGetProperty(name, valueElement) Then
                Return False
            End If

            Return TryGetIntValue(valueElement, value)
        End Function

        Private Shared Function TryGetStringArrayValue(element As JsonElement, ByRef value As String()) As Boolean
            value = Nothing

            If element.ValueKind = JsonValueKind.Array Then
                Dim list As New List(Of String)()
                For Each item In element.EnumerateArray()
                    If item.ValueKind = JsonValueKind.String Then
                        Dim text = item.GetString()
                        If Not String.IsNullOrWhiteSpace(text) Then
                            list.Add(text.Trim())
                        End If
                    End If
                Next

                value = list.ToArray()
                Return True
            End If

            Return False
        End Function

        Private Shared Function TryGetIntValue(element As JsonElement, ByRef value As Integer) As Boolean
            value = 0

            If element.ValueKind = JsonValueKind.Number AndAlso element.TryGetInt32(value) Then
                Return True
            End If

            Return False
        End Function

        Private Shared Function AreEquivalent(leftValues As String(), rightValues As String()) As Boolean
            If leftValues Is Nothing AndAlso rightValues Is Nothing Then
                Return True
            End If

            If leftValues Is Nothing OrElse rightValues Is Nothing Then
                Return False
            End If

            Return leftValues.SequenceEqual(rightValues, StringComparer.OrdinalIgnoreCase)
        End Function

        Private Shared Function TryGetBooleanSetting(root As JsonElement, section As String, name As String, ByRef value As Boolean) As Boolean
            value = False

            If root.ValueKind <> JsonValueKind.Object Then
                Return False
            End If

            Dim sectionElement As JsonElement
            If Not root.TryGetProperty(section, sectionElement) Then
                Return False
            End If

            If name Is Nothing Then
                Return TryGetBooleanValue(sectionElement, value)
            End If

            Dim valueElement As JsonElement
            If sectionElement.ValueKind <> JsonValueKind.Object OrElse Not sectionElement.TryGetProperty(name, valueElement) Then
                Return False
            End If

            Return TryGetBooleanValue(valueElement, value)
        End Function

        Private Shared Function TryGetBooleanValue(element As JsonElement, ByRef value As Boolean) As Boolean
            value = False
            If element.ValueKind = JsonValueKind.True Then
                value = True
                Return True
            End If

            If element.ValueKind = JsonValueKind.False Then
                value = False
                Return True
            End If

            Return False
        End Function

        Private Shared Function TryParseDiagnosticsMode(value As String, ByRef mode As DiagnosticsMode) As Boolean
            mode = DiagnosticsMode.OpenChange
            If String.IsNullOrWhiteSpace(value) Then
                Return False
            End If

            Select Case value.Trim().ToLowerInvariant()
                Case "openchange"
                    mode = DiagnosticsMode.OpenChange
                    Return True
                Case "opensave"
                    mode = DiagnosticsMode.OpenSave
                    Return True
                Case "saveonly"
                    mode = DiagnosticsMode.SaveOnly
                    Return True
                Case Else
                    Return False
            End Select
        End Function

        Private Sub TriggerDiagnosticsForOpenDocuments()
            For Each uri In _documentManager.OpenDocumentUris
                _diagnosticsService.TriggerDiagnostics(uri)
            Next
        End Sub

        Private Async Function ClearDiagnosticsForOpenDocumentsAsync(ct As CancellationToken) As Task
            For Each uri In _documentManager.OpenDocumentUris
                Await _diagnosticsService.ClearDiagnosticsAsync(uri, ct).ConfigureAwait(False)
            Next
        End Function

        Public Function DisposeAsync() As ValueTask Implements IAsyncDisposable.DisposeAsync
            Return New ValueTask(DisposeAsyncCore())
        End Function

        Private Async Function DisposeAsyncCore() As Task
            _shutdownCts.Cancel()
            _shutdownCts.Dispose()
            _diagnosticsService.Dispose()
            Await _workspaceManager.DisposeAsync().ConfigureAwait(False)
            Await _transport.DisposeAsync().ConfigureAwait(False)
        End Function

#End Region

    End Class

    ''' <summary>
    ''' Represents the state of the language server.
    ''' </summary>
    Public Enum ServerState
        ''' <summary>Server has not been started.</summary>
        NotStarted

        ''' <summary>Server is starting, waiting for transport connection.</summary>
        Starting

        ''' <summary>Server received initialize, processing.</summary>
        Initializing

        ''' <summary>Server is fully initialized and running.</summary>
        Running

        ''' <summary>Server received shutdown request.</summary>
        ShuttingDown

        ''' <summary>Server has stopped.</summary>
        Stopped
    End Enum

End Namespace
