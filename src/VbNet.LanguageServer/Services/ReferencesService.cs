// ReferencesService - Provides Find All References via LSP
// Services Layer as defined in docs/architecture.md Section 5.4

using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.FindSymbols;
using Microsoft.CodeAnalysis.Text;
using Microsoft.Extensions.Logging;
using VbNet.LanguageServer.Protocol;
using VbNet.LanguageServer.Workspace;

namespace VbNet.LanguageServer.Services;

/// <summary>
/// Provides Find All References functionality for VB.NET documents.
/// Uses Roslyn's SymbolFinder to locate all references to symbols.
/// </summary>
public sealed class ReferencesService
{
    private readonly WorkspaceManager _workspaceManager;
    private readonly DocumentManager _documentManager;
    private readonly ILogger<ReferencesService> _logger;

    public ReferencesService(
        WorkspaceManager workspaceManager,
        DocumentManager documentManager,
        ILogger<ReferencesService> logger)
    {
        _workspaceManager = workspaceManager ?? throw new ArgumentNullException(nameof(workspaceManager));
        _documentManager = documentManager ?? throw new ArgumentNullException(nameof(documentManager));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    /// <summary>
    /// Gets all reference locations for a symbol at the specified position.
    /// </summary>
    public async Task<Protocol.Location[]> GetReferencesAsync(
        ReferenceParams @params,
        CancellationToken cancellationToken)
    {
        if (@params?.TextDocument == null)
        {
            return Array.Empty<Protocol.Location>();
        }

        var uri = @params.TextDocument.Uri;
        var position = @params.Position;
        var includeDeclaration = @params.Context?.IncludeDeclaration ?? true;

        _logger.LogDebug("References requested at {Uri} ({Line}:{Character}), includeDeclaration={IncludeDecl}",
            uri, position.Line, position.Character, includeDeclaration);

        var document = _documentManager.GetRoslynDocument(uri);
        if (document == null)
        {
            _logger.LogTrace("No Roslyn document found for: {Uri}", uri);
            return Array.Empty<Protocol.Location>();
        }

        try
        {
            // Get the source text and calculate offset
            var sourceText = await document.GetTextAsync(cancellationToken);
            var offset = GetOffset(position, sourceText);

            cancellationToken.ThrowIfCancellationRequested();

            // Find the symbol at this position
            var symbol = await FindSymbolAtPositionAsync(document, offset, cancellationToken);
            if (symbol == null)
            {
                _logger.LogTrace("No symbol found at position for: {Uri}", uri);
                return Array.Empty<Protocol.Location>();
            }

            cancellationToken.ThrowIfCancellationRequested();

            // Find all references
            var references = await SymbolFinder.FindReferencesAsync(
                symbol,
                document.Project.Solution,
                cancellationToken);

            var locations = new List<Protocol.Location>();

            foreach (var reference in references)
            {
                // Add declaration locations if requested
                if (includeDeclaration)
                {
                    foreach (var declLocation in reference.Definition.Locations)
                    {
                        cancellationToken.ThrowIfCancellationRequested();

                        var location = await CreateLocationFromRoslynLocationAsync(
                            declLocation,
                            document.Project.Solution,
                            cancellationToken);

                        if (location != null)
                        {
                            locations.Add(location);
                        }
                    }
                }

                // Add reference locations
                foreach (var refLocation in reference.Locations)
                {
                    cancellationToken.ThrowIfCancellationRequested();

                    var location = await CreateLocationFromReferenceLocationAsync(
                        refLocation,
                        cancellationToken);

                    if (location != null)
                    {
                        locations.Add(location);
                    }
                }
            }

            // Remove duplicates (same uri and range)
            var distinctLocations = locations
                .GroupBy(l => (l.Uri, l.Range.Start.Line, l.Range.Start.Character, l.Range.End.Line, l.Range.End.Character))
                .Select(g => g.First())
                .ToArray();

            _logger.LogDebug("Found {Count} reference(s) for symbol: {Symbol}",
                distinctLocations.Length, symbol.Name);

            return distinctLocations;
        }
        catch (OperationCanceledException)
        {
            _logger.LogTrace("References request cancelled for: {Uri}", uri);
            throw;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error getting references for: {Uri}", uri);
            return Array.Empty<Protocol.Location>();
        }
    }

    /// <summary>
    /// Finds the symbol at the specified position in the document.
    /// </summary>
    private async Task<ISymbol?> FindSymbolAtPositionAsync(
        Document document,
        int position,
        CancellationToken cancellationToken)
    {
        var semanticModel = await document.GetSemanticModelAsync(cancellationToken);
        if (semanticModel == null)
        {
            return null;
        }

        var syntaxRoot = await document.GetSyntaxRootAsync(cancellationToken);
        if (syntaxRoot == null)
        {
            return null;
        }

        var token = syntaxRoot.FindToken(position);
        if (token.Parent == null)
        {
            return null;
        }

        // Try to get symbol info
        var symbolInfo = semanticModel.GetSymbolInfo(token.Parent, cancellationToken);
        var symbol = symbolInfo.Symbol ?? symbolInfo.CandidateSymbols.FirstOrDefault();

        // If no symbol, try getting declared symbol
        if (symbol == null)
        {
            symbol = semanticModel.GetDeclaredSymbol(token.Parent, cancellationToken);
        }

        // If still no symbol, try type info
        if (symbol == null)
        {
            var typeInfo = semanticModel.GetTypeInfo(token.Parent, cancellationToken);
            symbol = typeInfo.Type;
        }

        return symbol;
    }

    /// <summary>
    /// Creates an LSP Location from a Roslyn Location.
    /// </summary>
    private async Task<Protocol.Location?> CreateLocationFromRoslynLocationAsync(
        Microsoft.CodeAnalysis.Location roslynLocation,
        Solution solution,
        CancellationToken cancellationToken)
    {
        if (!roslynLocation.IsInSource)
        {
            return null;
        }

        var syntaxTree = roslynLocation.SourceTree;
        if (syntaxTree == null || string.IsNullOrEmpty(syntaxTree.FilePath))
        {
            return null;
        }

        var sourceText = await syntaxTree.GetTextAsync(cancellationToken);
        var span = roslynLocation.SourceSpan;

        var range = GetRange(span, sourceText);
        var uri = new Uri(syntaxTree.FilePath).ToString();

        return new Protocol.Location
        {
            Uri = uri,
            Range = range
        };
    }

    /// <summary>
    /// Creates an LSP Location from a Roslyn ReferenceLocation.
    /// </summary>
    private async Task<Protocol.Location?> CreateLocationFromReferenceLocationAsync(
        ReferenceLocation referenceLocation,
        CancellationToken cancellationToken)
    {
        var document = referenceLocation.Document;
        if (document == null)
        {
            return null;
        }

        var syntaxTree = await document.GetSyntaxTreeAsync(cancellationToken);
        if (syntaxTree == null)
        {
            return null;
        }

        var filePath = syntaxTree.FilePath;
        if (string.IsNullOrEmpty(filePath))
        {
            return null;
        }

        var sourceText = await syntaxTree.GetTextAsync(cancellationToken);
        var span = referenceLocation.Location.SourceSpan;

        var range = GetRange(span, sourceText);
        var uri = new Uri(filePath).ToString();

        return new Protocol.Location
        {
            Uri = uri,
            Range = range
        };
    }

    /// <summary>
    /// Converts an LSP Position to a Roslyn offset.
    /// </summary>
    private static int GetOffset(Position position, SourceText text)
    {
        var line = Math.Min(position.Line, text.Lines.Count - 1);
        line = Math.Max(0, line);

        var textLine = text.Lines[line];
        var character = Math.Min(position.Character, textLine.End - textLine.Start);
        character = Math.Max(0, character);

        return textLine.Start + character;
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
}
