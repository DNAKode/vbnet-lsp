// DefinitionService - Provides Go to Definition via LSP
// Services Layer as defined in docs/architecture.md Section 5.4

using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.FindSymbols;
using Microsoft.CodeAnalysis.Text;
using Microsoft.Extensions.Logging;
using VbNet.LanguageServer.Protocol;
using VbNet.LanguageServer.Workspace;

namespace VbNet.LanguageServer.Services;

/// <summary>
/// Provides Go to Definition functionality for VB.NET documents.
/// Uses Roslyn's symbol finding capabilities to locate definitions.
/// </summary>
public sealed class DefinitionService
{
    private readonly WorkspaceManager _workspaceManager;
    private readonly DocumentManager _documentManager;
    private readonly ILogger<DefinitionService> _logger;

    public DefinitionService(
        WorkspaceManager workspaceManager,
        DocumentManager documentManager,
        ILogger<DefinitionService> logger)
    {
        _workspaceManager = workspaceManager ?? throw new ArgumentNullException(nameof(workspaceManager));
        _documentManager = documentManager ?? throw new ArgumentNullException(nameof(documentManager));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    /// <summary>
    /// Gets the definition location(s) for a symbol at the specified position.
    /// </summary>
    public async Task<Protocol.Location[]> GetDefinitionAsync(
        DefinitionParams @params,
        CancellationToken cancellationToken)
    {
        if (@params?.TextDocument == null)
        {
            return Array.Empty<Protocol.Location>();
        }

        var uri = @params.TextDocument.Uri;
        var position = @params.Position;

        _logger.LogDebug("Definition requested at {Uri} ({Line}:{Character})",
            uri, position.Line, position.Character);

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

            // Get semantic model
            var semanticModel = await document.GetSemanticModelAsync(cancellationToken);
            if (semanticModel == null)
            {
                _logger.LogWarning("Could not get semantic model for: {Uri}", uri);
                return Array.Empty<Protocol.Location>();
            }

            // Get syntax root and find token at position
            var syntaxRoot = await document.GetSyntaxRootAsync(cancellationToken);
            if (syntaxRoot == null)
            {
                return Array.Empty<Protocol.Location>();
            }

            var token = syntaxRoot.FindToken(offset);
            if (token.Span.Length == 0)
            {
                return Array.Empty<Protocol.Location>();
            }

            cancellationToken.ThrowIfCancellationRequested();

            // Find the symbol at this position
            var symbol = await FindSymbolAtPositionAsync(document, offset, cancellationToken);
            if (symbol == null)
            {
                _logger.LogTrace("No symbol found at position for: {Uri}", uri);
                return Array.Empty<Protocol.Location>();
            }

            // Get definition locations
            var locations = await GetSymbolDefinitionLocationsAsync(symbol, document.Project.Solution, cancellationToken);

            _logger.LogDebug("Found {Count} definition location(s) for symbol: {Symbol}",
                locations.Length, symbol.Name);

            return locations;
        }
        catch (OperationCanceledException)
        {
            _logger.LogTrace("Definition request cancelled for: {Uri}", uri);
            throw;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error getting definition for: {Uri}", uri);
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
    /// Gets the definition locations for a symbol.
    /// </summary>
    private async Task<Protocol.Location[]> GetSymbolDefinitionLocationsAsync(
        ISymbol symbol,
        Solution solution,
        CancellationToken cancellationToken)
    {
        var locations = new List<Protocol.Location>();

        // Get the original definition for symbols like methods/properties
        var definitionSymbol = symbol.OriginalDefinition ?? symbol;

        // Handle partial classes - get all locations
        foreach (var syntaxRef in definitionSymbol.DeclaringSyntaxReferences)
        {
            cancellationToken.ThrowIfCancellationRequested();

            var location = await CreateLocationFromSyntaxReferenceAsync(syntaxRef, solution, cancellationToken);
            if (location != null)
            {
                locations.Add(location);
            }
        }

        // If no locations found and symbol is from metadata, try to find source definition
        if (locations.Count == 0 && definitionSymbol.Locations.Any(l => l.IsInMetadata))
        {
            // For metadata symbols, we could potentially decompile or show metadata view
            // For now, we just return empty - could be enhanced later
            _logger.LogTrace("Symbol {Symbol} is defined in metadata, no source location available",
                definitionSymbol.Name);
        }

        return locations.ToArray();
    }

    /// <summary>
    /// Creates an LSP Location from a Roslyn SyntaxReference.
    /// </summary>
    private async Task<Protocol.Location?> CreateLocationFromSyntaxReferenceAsync(
        SyntaxReference syntaxRef,
        Solution solution,
        CancellationToken cancellationToken)
    {
        var syntaxTree = syntaxRef.SyntaxTree;
        var filePath = syntaxTree.FilePath;

        if (string.IsNullOrEmpty(filePath))
        {
            return null;
        }

        var sourceText = await syntaxTree.GetTextAsync(cancellationToken);
        var span = syntaxRef.Span;

        // Find the identifier span within the declaration
        var syntax = await syntaxRef.GetSyntaxAsync(cancellationToken);
        var identifierSpan = GetIdentifierSpan(syntax) ?? span;

        var range = GetRange(identifierSpan, sourceText);
        var uri = new Uri(filePath).ToString();

        return new Protocol.Location
        {
            Uri = uri,
            Range = range
        };
    }

    /// <summary>
    /// Gets the span of the identifier within a declaration syntax node.
    /// </summary>
    private static TextSpan? GetIdentifierSpan(SyntaxNode node)
    {
        // Try to find the identifier token in the declaration
        // This varies by syntax node type, but we look for common patterns
        foreach (var child in node.ChildTokens())
        {
            if (child.IsKind(Microsoft.CodeAnalysis.VisualBasic.SyntaxKind.IdentifierToken))
            {
                return child.Span;
            }
        }

        // For some nodes, the first token might be what we want
        var firstToken = node.GetFirstToken();
        if (firstToken.IsKind(Microsoft.CodeAnalysis.VisualBasic.SyntaxKind.IdentifierToken))
        {
            return firstToken.Span;
        }

        return null;
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
