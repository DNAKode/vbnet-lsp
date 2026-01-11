// DocumentManager - Manages open document buffers and synchronization with Roslyn
// Workspace Layer as defined in docs/architecture.md Section 5.3

using System.Collections.Concurrent;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.Text;
using Microsoft.Extensions.Logging;
using VbNet.LanguageServer.Protocol;

namespace VbNet.LanguageServer.Workspace;

/// <summary>
/// Manages open document buffers and synchronizes them with the Roslyn workspace.
/// Handles LSP text document sync operations (didOpen, didChange, didClose).
/// </summary>
public sealed class DocumentManager
{
    private readonly WorkspaceManager _workspaceManager;
    private readonly ILogger<DocumentManager> _logger;

    /// <summary>
    /// Tracks open documents by URI with their current text and version.
    /// </summary>
    private readonly ConcurrentDictionary<string, OpenDocument> _openDocuments = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>
    /// Event raised when a document's content changes (for triggering diagnostics).
    /// </summary>
    public event EventHandler<DocumentChangedEventArgs>? DocumentChanged;

    public DocumentManager(WorkspaceManager workspaceManager, ILogger<DocumentManager> logger)
    {
        _workspaceManager = workspaceManager ?? throw new ArgumentNullException(nameof(workspaceManager));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));

        // Subscribe to workspace changes to reassociate documents after load
        _workspaceManager.SolutionChanged += OnSolutionChanged;
    }

    /// <summary>
    /// Gets all currently open document URIs.
    /// </summary>
    public IEnumerable<string> OpenDocumentUris => _openDocuments.Keys;

    /// <summary>
    /// Gets an open document by URI.
    /// </summary>
    public OpenDocument? GetOpenDocument(string uri)
    {
        _openDocuments.TryGetValue(uri, out var doc);
        return doc;
    }

    /// <summary>
    /// Checks if a document is currently open.
    /// </summary>
    public bool IsDocumentOpen(string uri) => _openDocuments.ContainsKey(uri);

    /// <summary>
    /// Handles textDocument/didOpen notification.
    /// Creates a document buffer and potentially an ad-hoc document in workspace.
    /// </summary>
    public void HandleDidOpen(DidOpenTextDocumentParams @params)
    {
        var uri = @params.TextDocument.Uri;
        var text = @params.TextDocument.Text;
        var version = @params.TextDocument.Version;
        var languageId = @params.TextDocument.LanguageId;

        _logger.LogDebug("Opening document: {Uri} (version {Version}, language {LanguageId})",
            uri, version, languageId);

        // Create source text from content
        var sourceText = SourceText.From(text);

        // Try to find existing document in workspace
        var document = _workspaceManager.GetDocumentByUri(uri);

        var openDoc = new OpenDocument
        {
            Uri = uri,
            LanguageId = languageId,
            Version = version,
            Text = sourceText,
            DocumentId = document?.Id
        };

        _openDocuments[uri] = openDoc;

        // If document exists in workspace, update its text
        if (document != null)
        {
            _workspaceManager.ApplyTextChange(document.Id, sourceText);
            _logger.LogDebug("Document found in workspace, text synchronized: {Uri}", uri);
        }
        else
        {
            _logger.LogDebug("Document not in workspace (standalone file): {Uri}", uri);
        }

        DocumentChanged?.Invoke(this, new DocumentChangedEventArgs(uri, sourceText, version));
    }

    /// <summary>
    /// Handles textDocument/didChange notification.
    /// Applies incremental or full text changes.
    /// </summary>
    public void HandleDidChange(DidChangeTextDocumentParams @params)
    {
        var uri = @params.TextDocument.Uri;
        var version = @params.TextDocument.Version;

        if (!_openDocuments.TryGetValue(uri, out var openDoc))
        {
            _logger.LogWarning("Received didChange for unopened document: {Uri}", uri);
            return;
        }

        _logger.LogTrace("Document changed: {Uri} (version {OldVersion} -> {NewVersion})",
            uri, openDoc.Version, version);

        // Apply changes to the current text
        var newText = ApplyChanges(openDoc.Text, @params.ContentChanges);

        // Update the open document record
        openDoc.Text = newText;
        openDoc.Version = version;

        // If document is in workspace, update Roslyn
        if (openDoc.DocumentId != null)
        {
            _workspaceManager.ApplyTextChange(openDoc.DocumentId, newText);
        }

        DocumentChanged?.Invoke(this, new DocumentChangedEventArgs(uri, newText, version));
    }

    /// <summary>
    /// Handles textDocument/didClose notification.
    /// Removes the document buffer.
    /// </summary>
    public void HandleDidClose(DidCloseTextDocumentParams @params)
    {
        var uri = @params.TextDocument.Uri;

        if (_openDocuments.TryRemove(uri, out var openDoc))
        {
            _logger.LogDebug("Closed document: {Uri}", uri);

            // If document is in workspace, we could reload from disk here
            // For now, we just remove from tracking
        }
        else
        {
            _logger.LogWarning("Received didClose for already closed document: {Uri}", uri);
        }
    }

    /// <summary>
    /// Handles textDocument/didSave notification.
    /// </summary>
    public void HandleDidSave(DidSaveTextDocumentParams @params)
    {
        var uri = @params.TextDocument.Uri;

        _logger.LogDebug("Document saved: {Uri}", uri);

        if (!_openDocuments.TryGetValue(uri, out var openDoc))
        {
            _logger.LogWarning("Received didSave for unopened document: {Uri}", uri);
            return;
        }

        // If save includes text, update our buffer (shouldn't normally happen with our save options)
        if (@params.Text != null)
        {
            var newText = SourceText.From(@params.Text);
            openDoc.Text = newText;

            if (openDoc.DocumentId != null)
            {
                _workspaceManager.ApplyTextChange(openDoc.DocumentId, newText);
            }
        }

        // Trigger diagnostics refresh on save
        DocumentChanged?.Invoke(this, new DocumentChangedEventArgs(uri, openDoc.Text, openDoc.Version));
    }

    /// <summary>
    /// Gets the current Roslyn Document for an open document.
    /// Returns the document with the latest text from the buffer.
    /// </summary>
    public Document? GetRoslynDocument(string uri)
    {
        if (!_openDocuments.TryGetValue(uri, out var openDoc))
        {
            // Not open, try to get from workspace directly
            return _workspaceManager.GetDocumentByUri(uri);
        }

        if (openDoc.DocumentId == null)
        {
            // DocumentId is null - try to find it in workspace now
            // (workspace may have loaded after the document was opened)
            var document = _workspaceManager.GetDocumentByUri(uri);
            if (document != null)
            {
                // Found it! Update the DocumentId for future calls
                openDoc.DocumentId = document.Id;

                // Sync the current text to the workspace
                _workspaceManager.ApplyTextChange(document.Id, openDoc.Text);

                _logger.LogDebug("Late-associated document: {Uri} -> {DocumentId}", uri, document.Id);
                return _workspaceManager.CurrentSolution?.GetDocument(document.Id);
            }

            // Truly a standalone document not in workspace
            return null;
        }

        return _workspaceManager.CurrentSolution?.GetDocument(openDoc.DocumentId);
    }

    /// <summary>
    /// Gets the current source text for a document.
    /// Prefers the open buffer, falls back to workspace.
    /// </summary>
    public async Task<SourceText?> GetSourceTextAsync(string uri, CancellationToken ct = default)
    {
        if (_openDocuments.TryGetValue(uri, out var openDoc))
        {
            return openDoc.Text;
        }

        var document = _workspaceManager.GetDocumentByUri(uri);
        if (document != null)
        {
            return await document.GetTextAsync(ct);
        }

        return null;
    }

    /// <summary>
    /// Applies a list of content changes to source text.
    /// Supports both incremental and full document changes.
    /// </summary>
    private SourceText ApplyChanges(SourceText text, TextDocumentContentChangeEvent[] changes)
    {
        foreach (var change in changes)
        {
            if (change.Range == null)
            {
                // Full document replacement
                text = SourceText.From(change.Text);
            }
            else
            {
                // Incremental change
                var span = GetTextSpan(change.Range, text);
                var textChange = new TextChange(span, change.Text);
                text = text.WithChanges(textChange);
            }
        }

        return text;
    }

    /// <summary>
    /// Converts an LSP Range to a Roslyn TextSpan.
    /// Uses UTF-16 positions as per Architecture Decision 14.6.
    /// </summary>
    private static TextSpan GetTextSpan(Protocol.Range range, SourceText text)
    {
        var startPosition = GetPosition(range.Start, text);
        var endPosition = GetPosition(range.End, text);
        return TextSpan.FromBounds(startPosition, endPosition);
    }

    /// <summary>
    /// Converts an LSP Position to a Roslyn offset.
    /// </summary>
    private static int GetPosition(Position position, SourceText text)
    {
        // Clamp line number to valid range
        var line = Math.Min(position.Line, text.Lines.Count - 1);
        line = Math.Max(0, line);

        var textLine = text.Lines[line];

        // Clamp character to valid range for this line
        var character = Math.Min(position.Character, textLine.End - textLine.Start);
        character = Math.Max(0, character);

        return textLine.Start + character;
    }

    /// <summary>
    /// Handles solution change events from the workspace.
    /// Reassociates open documents with workspace documents after workspace loads.
    /// </summary>
    private void OnSolutionChanged(object? sender, SolutionChangedEventArgs e)
    {
        if (e.Kind == SolutionChangeKind.Loaded || e.Kind == SolutionChangeKind.Reloaded || e.Kind == SolutionChangeKind.ProjectAdded)
        {
            ReassociateDocumentsWithWorkspace();
        }
    }

    /// <summary>
    /// Reassociates open documents with their corresponding workspace documents.
    /// This is called after the workspace loads to fix documents that were opened
    /// before the workspace was ready.
    /// </summary>
    public void ReassociateDocumentsWithWorkspace()
    {
        _logger.LogDebug("Reassociating open documents with workspace");
        var reassociatedCount = 0;

        foreach (var kvp in _openDocuments)
        {
            var uri = kvp.Key;
            var openDoc = kvp.Value;

            // Skip if already associated
            if (openDoc.DocumentId != null)
            {
                continue;
            }

            // Try to find the document in the workspace
            var document = _workspaceManager.GetDocumentByUri(uri);
            if (document != null)
            {
                openDoc.DocumentId = document.Id;

                // Sync the current text to the workspace
                _workspaceManager.ApplyTextChange(document.Id, openDoc.Text);

                _logger.LogDebug("Reassociated document: {Uri} -> {DocumentId}", uri, document.Id);
                reassociatedCount++;

                // Trigger diagnostics for this document
                DocumentChanged?.Invoke(this, new DocumentChangedEventArgs(uri, openDoc.Text, openDoc.Version));
            }
        }

        if (reassociatedCount > 0)
        {
            _logger.LogInformation("Reassociated {Count} open document(s) with workspace", reassociatedCount);
        }
    }
}

/// <summary>
/// Represents an open document with its current buffer state.
/// </summary>
public class OpenDocument
{
    /// <summary>
    /// The document URI.
    /// </summary>
    public required string Uri { get; init; }

    /// <summary>
    /// The language ID (e.g., "vb").
    /// </summary>
    public required string LanguageId { get; init; }

    /// <summary>
    /// The current document version.
    /// </summary>
    public int Version { get; set; }

    /// <summary>
    /// The current source text.
    /// </summary>
    public SourceText Text { get; set; } = SourceText.From("");

    /// <summary>
    /// The Roslyn DocumentId if this document is in the workspace.
    /// Null for standalone files not part of a project.
    /// </summary>
    public DocumentId? DocumentId { get; set; }
}

/// <summary>
/// Event args for document changes.
/// </summary>
public class DocumentChangedEventArgs : EventArgs
{
    public string Uri { get; }
    public SourceText Text { get; }
    public int Version { get; }

    public DocumentChangedEventArgs(string uri, SourceText text, int version)
    {
        Uri = uri;
        Text = text;
        Version = version;
    }
}
