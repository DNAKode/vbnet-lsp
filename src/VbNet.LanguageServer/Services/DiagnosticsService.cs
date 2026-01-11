// DiagnosticsService - Provides compiler diagnostics via LSP
// Services Layer as defined in docs/architecture.md Section 5.4

using System.Collections.Concurrent;
using System.Collections.Immutable;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.Text;
using Microsoft.CodeAnalysis.VisualBasic;
using Microsoft.Extensions.Logging;
using VbNet.LanguageServer.Protocol;
using VbNet.LanguageServer.Workspace;

namespace VbNet.LanguageServer.Services;

/// <summary>
/// Provides compiler diagnostics for VB.NET documents.
/// Uses a push model with debouncing as per Architecture Decision 14.8.
/// </summary>
public sealed class DiagnosticsService : IDisposable
{
    private static readonly Lazy<ImmutableArray<MetadataReference>> DefaultReferences = new(BuildDefaultReferences);
    private readonly WorkspaceManager _workspaceManager;
    private readonly DocumentManager _documentManager;
    private readonly ILogger<DiagnosticsService> _logger;
    private readonly Func<string, PublishDiagnosticsParams, CancellationToken, Task> _publishDiagnostics;

    /// <summary>
    /// Debounce timers per document URI.
    /// </summary>
    private readonly ConcurrentDictionary<string, Timer> _debounceTimers = new();

    /// <summary>
    /// Cancellation tokens for ongoing diagnostic computations.
    /// </summary>
    private readonly ConcurrentDictionary<string, CancellationTokenSource> _pendingComputations = new();

    /// <summary>
    /// Debounce delay in milliseconds. Default 300ms per architecture.
    /// </summary>
    public int DebounceDelayMs { get; set; } = 300;

    /// <summary>
    /// Minimum severity to report. Default is Warning (includes Error and Warning).
    /// </summary>
    public Protocol.DiagnosticSeverity MinimumSeverity { get; set; } = Protocol.DiagnosticSeverity.Warning;

    /// <summary>
    /// Enables or disables diagnostics publishing.
    /// </summary>
    public bool Enabled { get; set; } = true;

    public DiagnosticsService(
        WorkspaceManager workspaceManager,
        DocumentManager documentManager,
        Func<string, PublishDiagnosticsParams, CancellationToken, Task> publishDiagnostics,
        ILogger<DiagnosticsService> logger)
    {
        _workspaceManager = workspaceManager ?? throw new ArgumentNullException(nameof(workspaceManager));
        _documentManager = documentManager ?? throw new ArgumentNullException(nameof(documentManager));
        _publishDiagnostics = publishDiagnostics ?? throw new ArgumentNullException(nameof(publishDiagnostics));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));

        // Subscribe to document changes
        _documentManager.DocumentChanged += OnDocumentChanged;
    }

    /// <summary>
    /// Triggers diagnostics for a document with debouncing.
    /// </summary>
    public void TriggerDiagnostics(string uri)
    {
        if (!Enabled)
        {
            return;
        }

        _logger.LogTrace("Diagnostics triggered for: {Uri}", uri);

        // Cancel any existing timer for this document
        if (_debounceTimers.TryRemove(uri, out var existingTimer))
        {
            existingTimer.Dispose();
        }

        // Create new debounce timer
        var timer = new Timer(
            callback: _ => _ = ComputeAndPublishDiagnosticsAsync(uri),
            state: null,
            dueTime: DebounceDelayMs,
            period: Timeout.Infinite);

        _debounceTimers[uri] = timer;
    }

    /// <summary>
    /// Computes and publishes diagnostics for a document immediately (no debouncing).
    /// </summary>
    public async Task ComputeAndPublishDiagnosticsAsync(string uri, CancellationToken cancellationToken = default)
    {
        if (!Enabled)
        {
            return;
        }

        // Cancel any previous computation for this document
        if (_pendingComputations.TryRemove(uri, out var existingCts))
        {
            existingCts.Cancel();
            existingCts.Dispose();
        }

        // Create new cancellation token source
        var cts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        _pendingComputations[uri] = cts;

        try
        {
            var diagnostics = await GetDiagnosticsAsync(uri, cts.Token);

            // Check if cancelled
            if (cts.Token.IsCancellationRequested)
            {
                return;
            }

            var openDoc = _documentManager.GetOpenDocument(uri);
            var @params = new PublishDiagnosticsParams
            {
                Uri = uri,
                Version = openDoc?.Version,
                Diagnostics = diagnostics
            };

            await _publishDiagnostics("textDocument/publishDiagnostics", @params, cts.Token);
            _logger.LogDebug("Published {Count} diagnostics for: {Uri}", diagnostics.Length, uri);
        }
        catch (OperationCanceledException)
        {
            _logger.LogTrace("Diagnostics computation cancelled for: {Uri}", uri);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error computing diagnostics for: {Uri}", uri);

            // Publish empty diagnostics on error to clear stale ones
            try
            {
                var @params = new PublishDiagnosticsParams
                {
                    Uri = uri,
                    Diagnostics = Array.Empty<Protocol.Diagnostic>()
                };
                await _publishDiagnostics("textDocument/publishDiagnostics", @params, CancellationToken.None);
            }
            catch
            {
                // Ignore errors when clearing diagnostics
            }
        }
        finally
        {
            _pendingComputations.TryRemove(uri, out _);
            cts.Dispose();
        }
    }

    /// <summary>
    /// Gets diagnostics for a document from Roslyn.
    /// </summary>
    public async Task<Protocol.Diagnostic[]> GetDiagnosticsAsync(string uri, CancellationToken cancellationToken = default)
    {
        var document = _documentManager.GetRoslynDocument(uri);
        if (document == null)
        {
            _logger.LogTrace("No Roslyn document found for: {Uri}. Falling back to standalone diagnostics.", uri);
            return await GetStandaloneDiagnosticsAsync(uri, cancellationToken);
        }

        // Get semantic model which includes all diagnostics
        var semanticModel = await document.GetSemanticModelAsync(cancellationToken);
        if (semanticModel == null)
        {
            _logger.LogWarning("Failed to get semantic model for: {Uri}. Falling back to project diagnostics.", uri);
            return await GetProjectDiagnosticsAsync(document, cancellationToken);
        }

        cancellationToken.ThrowIfCancellationRequested();

        // Get all diagnostics from the semantic model
        var roslynDiagnostics = semanticModel.GetDiagnostics(cancellationToken: cancellationToken);

        // Also get syntax diagnostics (parse errors)
        var syntaxTree = await document.GetSyntaxTreeAsync(cancellationToken);
        if (syntaxTree != null)
        {
            var syntaxDiagnostics = syntaxTree.GetDiagnostics(cancellationToken);
            roslynDiagnostics = roslynDiagnostics.AddRange(syntaxDiagnostics);
        }

        // Get source text for position translation
        var sourceText = await document.GetTextAsync(cancellationToken);

        // Filter and translate diagnostics
        var lspDiagnostics = roslynDiagnostics
            .Where(d => !d.IsSuppressed)
            .Where(d => ShouldIncludeDiagnostic(d))
            .Select(d => TranslateDiagnostic(d, sourceText))
            .Where(d => d != null)
            .Cast<Protocol.Diagnostic>()
            .ToArray();

        return lspDiagnostics;
    }

    private async Task<Protocol.Diagnostic[]> GetProjectDiagnosticsAsync(Document document, CancellationToken cancellationToken)
    {
        var syntaxTree = await document.GetSyntaxTreeAsync(cancellationToken);
        if (syntaxTree == null)
        {
            return Array.Empty<Protocol.Diagnostic>();
        }

        var compilation = await document.Project.GetCompilationAsync(cancellationToken);
        if (compilation == null)
        {
            return Array.Empty<Protocol.Diagnostic>();
        }

        var sourceText = await syntaxTree.GetTextAsync(cancellationToken);
        var diagnostics = compilation.GetDiagnostics(cancellationToken)
            .Where(d => d.Location.Kind == LocationKind.SourceFile && d.Location.SourceTree == syntaxTree);

        return diagnostics
            .Where(d => !d.IsSuppressed)
            .Where(d => ShouldIncludeDiagnostic(d))
            .Select(d => TranslateDiagnostic(d, sourceText))
            .Where(d => d != null)
            .Cast<Protocol.Diagnostic>()
            .ToArray();
    }

    private async Task<Protocol.Diagnostic[]> GetStandaloneDiagnosticsAsync(string uri, CancellationToken cancellationToken)
    {
        var sourceText = await _documentManager.GetSourceTextAsync(uri, cancellationToken);
        if (sourceText == null)
        {
            return Array.Empty<Protocol.Diagnostic>();
        }

        var filePath = TryGetFilePath(uri);
        var syntaxTree = VisualBasicSyntaxTree.ParseText(sourceText, path: filePath);
        var compilation = VisualBasicCompilation.Create(
            "VbNetStandaloneDiagnostics",
            new[] { syntaxTree },
            DefaultReferences.Value,
            new VisualBasicCompilationOptions(OutputKind.DynamicallyLinkedLibrary));

        var diagnostics = compilation.GetDiagnostics(cancellationToken)
            .Where(d => d.Location.Kind == LocationKind.SourceFile && d.Location.SourceTree == syntaxTree);

        return diagnostics
            .Where(d => !d.IsSuppressed)
            .Where(d => ShouldIncludeDiagnostic(d))
            .Select(d => TranslateDiagnostic(d, sourceText))
            .Where(d => d != null)
            .Cast<Protocol.Diagnostic>()
            .ToArray();
    }

    private static ImmutableArray<MetadataReference> BuildDefaultReferences()
    {
        var builder = ImmutableArray.CreateBuilder<MetadataReference>();
        var trustedAssemblies = AppContext.GetData("TRUSTED_PLATFORM_ASSEMBLIES") as string;
        if (string.IsNullOrWhiteSpace(trustedAssemblies))
        {
            return builder.ToImmutable();
        }

        foreach (var path in trustedAssemblies.Split(Path.PathSeparator))
        {
            try
            {
                builder.Add(MetadataReference.CreateFromFile(path));
            }
            catch
            {
                // Ignore invalid reference paths.
            }
        }

        return builder.ToImmutable();
    }

    private static string? TryGetFilePath(string uri)
    {
        if (string.IsNullOrWhiteSpace(uri))
        {
            return null;
        }

        try
        {
            var parsedUri = new Uri(uri);
            return parsedUri.IsFile ? parsedUri.LocalPath : uri;
        }
        catch (UriFormatException)
        {
            return uri;
        }
    }

    /// <summary>
    /// Clears diagnostics for a document (e.g., when closed).
    /// </summary>
    public async Task ClearDiagnosticsAsync(string uri, CancellationToken cancellationToken = default)
    {
        // Cancel any pending computation
        if (_pendingComputations.TryRemove(uri, out var cts))
        {
            cts.Cancel();
            cts.Dispose();
        }

        // Cancel any pending timer
        if (_debounceTimers.TryRemove(uri, out var timer))
        {
            timer.Dispose();
        }

        // Publish empty diagnostics
        var @params = new PublishDiagnosticsParams
        {
            Uri = uri,
            Diagnostics = Array.Empty<Protocol.Diagnostic>()
        };

        await _publishDiagnostics("textDocument/publishDiagnostics", @params, cancellationToken);
        _logger.LogDebug("Cleared diagnostics for: {Uri}", uri);
    }

    /// <summary>
    /// Determines if a diagnostic should be included based on severity.
    /// </summary>
    private bool ShouldIncludeDiagnostic(Microsoft.CodeAnalysis.Diagnostic diagnostic)
    {
        var severity = TranslateSeverity(diagnostic.Severity);
        return severity <= MinimumSeverity; // Lower value = higher severity
    }

    /// <summary>
    /// Translates a Roslyn Diagnostic to an LSP Diagnostic.
    /// </summary>
    private Protocol.Diagnostic? TranslateDiagnostic(Microsoft.CodeAnalysis.Diagnostic diagnostic, SourceText sourceText)
    {
        // Skip diagnostics without a location in the source
        if (diagnostic.Location.Kind != LocationKind.SourceFile)
        {
            return null;
        }

        var span = diagnostic.Location.SourceSpan;
        var range = GetRange(span, sourceText);

        return new Protocol.Diagnostic
        {
            Range = range,
            Severity = TranslateSeverity(diagnostic.Severity),
            Code = diagnostic.Id,
            Source = "vbnet",
            Message = diagnostic.GetMessage(),
            CodeDescription = GetCodeDescription(diagnostic),
            RelatedInformation = GetRelatedInformation(diagnostic)
        };
    }

    /// <summary>
    /// Translates Roslyn DiagnosticSeverity to LSP DiagnosticSeverity.
    /// </summary>
    private static Protocol.DiagnosticSeverity TranslateSeverity(Microsoft.CodeAnalysis.DiagnosticSeverity severity)
    {
        return severity switch
        {
            Microsoft.CodeAnalysis.DiagnosticSeverity.Error => Protocol.DiagnosticSeverity.Error,
            Microsoft.CodeAnalysis.DiagnosticSeverity.Warning => Protocol.DiagnosticSeverity.Warning,
            Microsoft.CodeAnalysis.DiagnosticSeverity.Info => Protocol.DiagnosticSeverity.Information,
            Microsoft.CodeAnalysis.DiagnosticSeverity.Hidden => Protocol.DiagnosticSeverity.Hint,
            _ => Protocol.DiagnosticSeverity.Information
        };
    }

    /// <summary>
    /// Gets a code description link for a diagnostic if available.
    /// </summary>
    private static CodeDescription? GetCodeDescription(Microsoft.CodeAnalysis.Diagnostic diagnostic)
    {
        var helpLink = diagnostic.Descriptor.HelpLinkUri;
        if (string.IsNullOrEmpty(helpLink))
        {
            // Generate a link to MS docs for VB errors
            if (diagnostic.Id.StartsWith("BC", StringComparison.OrdinalIgnoreCase))
            {
                helpLink = $"https://learn.microsoft.com/en-us/dotnet/visual-basic/misc/{diagnostic.Id.ToLowerInvariant()}";
            }
        }

        if (!string.IsNullOrEmpty(helpLink))
        {
            return new CodeDescription { Href = helpLink };
        }

        return null;
    }

    /// <summary>
    /// Gets related information for a diagnostic (additional locations).
    /// </summary>
    private DiagnosticRelatedInformation[]? GetRelatedInformation(
        Microsoft.CodeAnalysis.Diagnostic diagnostic)
    {
        var additionalLocations = diagnostic.AdditionalLocations;
        if (additionalLocations.Count == 0)
        {
            return null;
        }

        var related = new List<DiagnosticRelatedInformation>();
        foreach (var location in additionalLocations)
        {
            if (location.Kind != LocationKind.SourceFile || location.SourceTree == null)
            {
                continue;
            }

            var filePath = location.SourceTree.FilePath;
            var uri = PathToUri(filePath);
            var relatedText = location.SourceTree.GetText();
            var range = GetRange(location.SourceSpan, relatedText);

            related.Add(new DiagnosticRelatedInformation
            {
                Location = new Protocol.Location
                {
                    Uri = uri,
                    Range = range
                },
                Message = "Related location"
            });
        }

        return related.Count > 0 ? related.ToArray() : null;
    }

    /// <summary>
    /// Converts a TextSpan to an LSP Range.
    /// </summary>
    private static Protocol.Range GetRange(TextSpan span, SourceText sourceText)
    {
        var startLine = sourceText.Lines.GetLineFromPosition(span.Start);
        var endLine = sourceText.Lines.GetLineFromPosition(span.End);

        return new Protocol.Range
        {
            Start = new Position
            {
                Line = startLine.LineNumber,
                Character = span.Start - startLine.Start
            },
            End = new Position
            {
                Line = endLine.LineNumber,
                Character = span.End - endLine.Start
            }
        };
    }

    /// <summary>
    /// Converts a file path to a file:// URI.
    /// </summary>
    private static string PathToUri(string path)
    {
        return new Uri(path).ToString();
    }

    private void OnDocumentChanged(object? sender, DocumentChangedEventArgs e)
    {
        if (!Enabled)
        {
            return;
        }

        if (e.Kind == DocumentChangeKind.Opened)
        {
            _ = ComputeAndPublishDiagnosticsAsync(e.Uri);
            return;
        }

        TriggerDiagnostics(e.Uri);
    }

    public void Dispose()
    {
        _documentManager.DocumentChanged -= OnDocumentChanged;

        // Dispose all timers
        foreach (var timer in _debounceTimers.Values)
        {
            timer.Dispose();
        }
        _debounceTimers.Clear();

        // Cancel and dispose all pending computations
        foreach (var cts in _pendingComputations.Values)
        {
            cts.Cancel();
            cts.Dispose();
        }
        _pendingComputations.Clear();
    }
}
