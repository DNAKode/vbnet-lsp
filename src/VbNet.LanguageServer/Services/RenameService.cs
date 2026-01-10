// RenameService - Provides symbol renaming via LSP
// Services Layer as defined in docs/architecture.md Section 5.4

using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.Rename;
using Microsoft.CodeAnalysis.Text;
using Microsoft.Extensions.Logging;
using VbNet.LanguageServer.Protocol;
using VbNet.LanguageServer.Workspace;

namespace VbNet.LanguageServer.Services;

/// <summary>
/// Provides symbol renaming functionality for VB.NET documents.
/// Uses Roslyn's Renamer for semantic-aware renaming across the solution.
/// </summary>
public sealed class RenameService
{
    private readonly WorkspaceManager _workspaceManager;
    private readonly DocumentManager _documentManager;
    private readonly ILogger<RenameService> _logger;

    public RenameService(
        WorkspaceManager workspaceManager,
        DocumentManager documentManager,
        ILogger<RenameService> logger)
    {
        _workspaceManager = workspaceManager ?? throw new ArgumentNullException(nameof(workspaceManager));
        _documentManager = documentManager ?? throw new ArgumentNullException(nameof(documentManager));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    /// <summary>
    /// Prepares for a rename operation by validating the position and returning
    /// the range and placeholder text for the symbol.
    /// </summary>
    public async Task<PrepareRenameResult?> PrepareRenameAsync(
        PrepareRenameParams @params,
        CancellationToken cancellationToken)
    {
        if (@params?.TextDocument == null)
        {
            return null;
        }

        var uri = @params.TextDocument.Uri;
        var position = @params.Position;

        _logger.LogDebug("PrepareRename requested at {Uri} ({Line}:{Character})",
            uri, position.Line, position.Character);

        var document = _documentManager.GetRoslynDocument(uri);
        if (document == null)
        {
            _logger.LogTrace("No Roslyn document found for: {Uri}", uri);
            return null;
        }

        try
        {
            var sourceText = await document.GetTextAsync(cancellationToken);
            var offset = GetOffset(position, sourceText);

            cancellationToken.ThrowIfCancellationRequested();

            // Find the symbol at this position
            var symbol = await FindSymbolAtPositionAsync(document, offset, cancellationToken);
            if (symbol == null)
            {
                _logger.LogTrace("No symbol found at position for: {Uri}", uri);
                return null;
            }

            // Check if symbol can be renamed
            if (!CanRenameSymbol(symbol))
            {
                _logger.LogTrace("Symbol cannot be renamed: {Symbol}", symbol.Name);
                return null;
            }

            // Get the token at the position for the range
            var syntaxRoot = await document.GetSyntaxRootAsync(cancellationToken);
            if (syntaxRoot == null)
            {
                return null;
            }

            var token = syntaxRoot.FindToken(offset);
            if (token.Span.Length == 0)
            {
                return null;
            }

            var range = GetRange(token.Span, sourceText);

            _logger.LogDebug("PrepareRename succeeded for symbol: {Symbol}", symbol.Name);

            return new PrepareRenameResult
            {
                Range = range,
                Placeholder = symbol.Name
            };
        }
        catch (OperationCanceledException)
        {
            _logger.LogTrace("PrepareRename request cancelled for: {Uri}", uri);
            throw;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error preparing rename for: {Uri}", uri);
            return null;
        }
    }

    /// <summary>
    /// Performs a rename operation across the solution.
    /// </summary>
    public async Task<WorkspaceEdit?> RenameAsync(
        RenameParams @params,
        CancellationToken cancellationToken)
    {
        if (@params?.TextDocument == null || string.IsNullOrEmpty(@params.NewName))
        {
            return null;
        }

        var uri = @params.TextDocument.Uri;
        var position = @params.Position;
        var newName = @params.NewName;

        _logger.LogDebug("Rename requested at {Uri} ({Line}:{Character}) to '{NewName}'",
            uri, position.Line, position.Character, newName);

        var document = _documentManager.GetRoslynDocument(uri);
        if (document == null)
        {
            _logger.LogTrace("No Roslyn document found for: {Uri}", uri);
            return null;
        }

        try
        {
            var sourceText = await document.GetTextAsync(cancellationToken);
            var offset = GetOffset(position, sourceText);

            cancellationToken.ThrowIfCancellationRequested();

            // Find the symbol at this position
            var symbol = await FindSymbolAtPositionAsync(document, offset, cancellationToken);
            if (symbol == null)
            {
                _logger.LogTrace("No symbol found at position for: {Uri}", uri);
                return null;
            }

            // Check if symbol can be renamed
            if (!CanRenameSymbol(symbol))
            {
                _logger.LogTrace("Symbol cannot be renamed: {Symbol}", symbol.Name);
                return null;
            }

            cancellationToken.ThrowIfCancellationRequested();

            // Perform the rename using Roslyn's Renamer
            var solution = document.Project.Solution;
            var newSolution = await Renamer.RenameSymbolAsync(
                solution,
                symbol,
                new SymbolRenameOptions(),
                newName,
                cancellationToken);

            // Get the changes
            var changes = newSolution.GetChanges(solution);
            var workspaceEdit = await BuildWorkspaceEditAsync(changes, solution, newSolution, cancellationToken);

            _logger.LogDebug("Rename completed for symbol: {OldName} -> {NewName}",
                symbol.Name, newName);

            return workspaceEdit;
        }
        catch (OperationCanceledException)
        {
            _logger.LogTrace("Rename request cancelled for: {Uri}", uri);
            throw;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error performing rename for: {Uri}", uri);
            return null;
        }
    }

    /// <summary>
    /// Checks if a symbol can be renamed.
    /// </summary>
    private static bool CanRenameSymbol(ISymbol symbol)
    {
        // Can't rename implicit symbols
        if (symbol.IsImplicitlyDeclared)
        {
            return false;
        }

        // Can't rename symbols from metadata
        if (symbol.Locations.All(l => l.IsInMetadata))
        {
            return false;
        }

        // Can't rename namespaces (complex operation)
        if (symbol is INamespaceSymbol)
        {
            return false;
        }

        return true;
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

        return symbol;
    }

    /// <summary>
    /// Builds a WorkspaceEdit from solution changes.
    /// </summary>
    private async Task<WorkspaceEdit> BuildWorkspaceEditAsync(
        SolutionChanges changes,
        Solution oldSolution,
        Solution newSolution,
        CancellationToken cancellationToken)
    {
        var documentChanges = new Dictionary<string, List<TextEdit>>();

        foreach (var projectChanges in changes.GetProjectChanges())
        {
            foreach (var documentId in projectChanges.GetChangedDocuments())
            {
                cancellationToken.ThrowIfCancellationRequested();

                var oldDocument = oldSolution.GetDocument(documentId);
                var newDocument = newSolution.GetDocument(documentId);

                if (oldDocument == null || newDocument == null)
                {
                    continue;
                }

                var oldText = await oldDocument.GetTextAsync(cancellationToken);
                var newText = await newDocument.GetTextAsync(cancellationToken);

                var textChanges = newText.GetTextChanges(oldText);
                if (textChanges.Count == 0)
                {
                    continue;
                }

                var filePath = oldDocument.FilePath;
                if (string.IsNullOrEmpty(filePath))
                {
                    continue;
                }

                var uri = new Uri(filePath).ToString();

                if (!documentChanges.TryGetValue(uri, out var edits))
                {
                    edits = new List<TextEdit>();
                    documentChanges[uri] = edits;
                }

                foreach (var change in textChanges)
                {
                    var range = GetRange(change.Span, oldText);
                    edits.Add(new TextEdit
                    {
                        Range = range,
                        NewText = change.NewText ?? string.Empty
                    });
                }
            }
        }

        return new WorkspaceEdit
        {
            Changes = documentChanges.ToDictionary(
                kvp => kvp.Key,
                kvp => kvp.Value.ToArray())
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

/// <summary>
/// Result of prepareRename request.
/// </summary>
public class PrepareRenameResult
{
    public Protocol.Range Range { get; set; } = new();
    public string Placeholder { get; set; } = string.Empty;
}
