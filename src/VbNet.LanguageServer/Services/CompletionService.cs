// CompletionService - Provides IntelliSense completion via LSP
// Services Layer as defined in docs/architecture.md Section 5.4

using System.Collections.Immutable;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.Text;
using Microsoft.Extensions.Logging;
using VbNet.LanguageServer.Protocol;
using VbNet.LanguageServer.Workspace;

// Roslyn Completion namespace (to avoid conflicts with Protocol types)
using RoslynCompletion = Microsoft.CodeAnalysis.Completion;

namespace VbNet.LanguageServer.Services;

/// <summary>
/// Provides IntelliSense completion for VB.NET documents.
/// Uses Roslyn's CompletionService for accurate suggestions.
/// </summary>
public sealed class CompletionService
{
    private readonly WorkspaceManager _workspaceManager;
    private readonly DocumentManager _documentManager;
    private readonly ILogger<CompletionService> _logger;

    /// <summary>
    /// Commit characters that should trigger completion acceptance.
    /// </summary>
    private static readonly string[] DefaultCommitCharacters = new[] { ".", "(", "[", " " };

    public CompletionService(
        WorkspaceManager workspaceManager,
        DocumentManager documentManager,
        ILogger<CompletionService> logger)
    {
        _workspaceManager = workspaceManager ?? throw new ArgumentNullException(nameof(workspaceManager));
        _documentManager = documentManager ?? throw new ArgumentNullException(nameof(documentManager));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    /// <summary>
    /// Gets completion items for a document at the specified position.
    /// </summary>
    public async Task<CompletionList> GetCompletionAsync(
        CompletionParams @params,
        CancellationToken cancellationToken)
    {
        if (@params?.TextDocument == null)
        {
            return new CompletionList { IsIncomplete = false, Items = Array.Empty<Protocol.CompletionItem>() };
        }

        var uri = @params.TextDocument.Uri;
        var position = @params.Position;

        _logger.LogDebug("Completion requested at {Uri} ({Line}:{Character})",
            uri, position.Line, position.Character);

        var document = _documentManager.GetRoslynDocument(uri);
        if (document == null)
        {
            _logger.LogTrace("No Roslyn document found for: {Uri}", uri);
            return new CompletionList { IsIncomplete = false, Items = Array.Empty<Protocol.CompletionItem>() };
        }

        try
        {
            // Get the source text and calculate offset
            var sourceText = await document.GetTextAsync(cancellationToken);
            var offset = GetOffset(position, sourceText);

            cancellationToken.ThrowIfCancellationRequested();

            // Get Roslyn completion service
            var completionService = RoslynCompletion.CompletionService.GetService(document);
            if (completionService == null)
            {
                _logger.LogWarning("CompletionService not available for document: {Uri}", uri);
                return new CompletionList { IsIncomplete = false, Items = Array.Empty<Protocol.CompletionItem>() };
            }

            // Get completions from Roslyn
            var completions = await completionService.GetCompletionsAsync(
                document,
                offset,
                cancellationToken: cancellationToken);

            if (completions == null || completions.ItemsList.Count == 0)
            {
                _logger.LogTrace("No completions returned for: {Uri}", uri);
                return new CompletionList { IsIncomplete = false, Items = Array.Empty<Protocol.CompletionItem>() };
            }

            cancellationToken.ThrowIfCancellationRequested();

            // Translate to LSP CompletionItems
            var items = completions.ItemsList
                .Select((item, index) => TranslateCompletionItem(item, index, uri))
                .ToArray();

            _logger.LogDebug("Returning {Count} completion items for: {Uri}", items.Length, uri);

            return new CompletionList
            {
                IsIncomplete = false, // We return all items, let client filter
                Items = items
            };
        }
        catch (OperationCanceledException)
        {
            _logger.LogTrace("Completion request cancelled for: {Uri}", uri);
            throw;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error getting completions for: {Uri}", uri);
            return new CompletionList { IsIncomplete = false, Items = Array.Empty<Protocol.CompletionItem>() };
        }
    }

    /// <summary>
    /// Resolves additional details for a completion item.
    /// Called when client needs documentation or other expensive details.
    /// </summary>
    public async Task<Protocol.CompletionItem> ResolveCompletionItemAsync(
        Protocol.CompletionItem item,
        CancellationToken cancellationToken)
    {
        if (item == null)
        {
            return new Protocol.CompletionItem { Label = "" };
        }

        _logger.LogDebug("Resolving completion item: {Label}", item.Label);

        // Extract stored data for resolution
        if (item.Data is not System.Text.Json.JsonElement jsonData)
        {
            return item;
        }

        try
        {
            // Parse the stored data
            var uri = jsonData.GetProperty("uri").GetString();
            var displayText = jsonData.GetProperty("displayText").GetString();

            if (string.IsNullOrEmpty(uri) || string.IsNullOrEmpty(displayText))
            {
                return item;
            }

            var document = _documentManager.GetRoslynDocument(uri);
            if (document == null)
            {
                return item;
            }

            var completionService = RoslynCompletion.CompletionService.GetService(document);
            if (completionService == null)
            {
                return item;
            }

            // Get the source text to find the position
            var sourceText = await document.GetTextAsync(cancellationToken);

            // Try to get description for the item
            // Note: This requires finding the original completion item, which we approximate
            var completions = await completionService.GetCompletionsAsync(
                document,
                sourceText.Length > 0 ? sourceText.Length - 1 : 0, // Use end of document as approximation
                cancellationToken: cancellationToken);

            if (completions == null)
            {
                return item;
            }

            var matchingItem = completions.ItemsList
                .FirstOrDefault(c => c.DisplayText == displayText);

            if (matchingItem == null)
            {
                return item;
            }

            cancellationToken.ThrowIfCancellationRequested();

            // Get description
            var description = await completionService.GetDescriptionAsync(
                document,
                matchingItem,
                cancellationToken);

            if (description != null)
            {
                var docText = string.Join("\n", description.TaggedParts.Select(p => p.Text));
                if (!string.IsNullOrWhiteSpace(docText))
                {
                    item.Documentation = new MarkupContent
                    {
                        Kind = "markdown",
                        Value = FormatDocumentation(description)
                    };
                }
            }

            return item;
        }
        catch (OperationCanceledException)
        {
            _logger.LogTrace("Completion resolve cancelled for: {Label}", item.Label);
            throw;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error resolving completion item: {Label}", item.Label);
            return item;
        }
    }

    /// <summary>
    /// Translates a Roslyn CompletionItem to an LSP CompletionItem.
    /// </summary>
    private Protocol.CompletionItem TranslateCompletionItem(
        RoslynCompletion.CompletionItem roslynItem,
        int index,
        string uri)
    {
        var kind = TranslateCompletionKind(roslynItem.Tags);

        var item = new Protocol.CompletionItem
        {
            Label = roslynItem.DisplayText,
            Kind = kind,
            Detail = GetDetail(roslynItem),
            InsertText = roslynItem.DisplayText,
            InsertTextFormat = InsertTextFormat.PlainText,
            SortText = index.ToString("D5"), // Preserve Roslyn's ordering
            FilterText = roslynItem.FilterText,
            CommitCharacters = DefaultCommitCharacters,
            // Store data for resolve
            Data = new
            {
                uri,
                displayText = roslynItem.DisplayText,
                index
            }
        };

        return item;
    }

    /// <summary>
    /// Translates Roslyn completion tags to LSP CompletionItemKind.
    /// </summary>
    private static CompletionItemKind TranslateCompletionKind(ImmutableArray<string> tags)
    {
        // Check tags in priority order
        foreach (var tag in tags)
        {
            switch (tag)
            {
                case "Class":
                    return CompletionItemKind.Class;
                case "Structure":
                case "Struct":
                    return CompletionItemKind.Struct;
                case "Interface":
                    return CompletionItemKind.Interface;
                case "Enum":
                    return CompletionItemKind.Enum;
                case "EnumMember":
                    return CompletionItemKind.EnumMember;
                case "Module":
                    return CompletionItemKind.Module;
                case "Method":
                case "ExtensionMethod":
                    return CompletionItemKind.Method;
                case "Function":
                    return CompletionItemKind.Function;
                case "Property":
                    return CompletionItemKind.Property;
                case "Field":
                    return CompletionItemKind.Field;
                case "Event":
                    return CompletionItemKind.Event;
                case "Constant":
                    return CompletionItemKind.Constant;
                case "Local":
                case "Parameter":
                    return CompletionItemKind.Variable;
                case "Keyword":
                    return CompletionItemKind.Keyword;
                case "Namespace":
                    return CompletionItemKind.Module;
                case "TypeParameter":
                    return CompletionItemKind.TypeParameter;
                case "Operator":
                    return CompletionItemKind.Operator;
                case "Snippet":
                    return CompletionItemKind.Snippet;
            }
        }

        return CompletionItemKind.Text;
    }

    /// <summary>
    /// Gets detail text for a completion item.
    /// </summary>
    private static string? GetDetail(RoslynCompletion.CompletionItem item)
    {
        // Use InlineDescription if available
        if (!string.IsNullOrEmpty(item.InlineDescription))
        {
            return item.InlineDescription;
        }

        // Check for namespace in properties
        if (item.Properties.TryGetValue("SymbolName", out var symbolName) &&
            item.Properties.TryGetValue("ContainingNamespace", out var containingNamespace) &&
            !string.IsNullOrEmpty(containingNamespace))
        {
            return containingNamespace;
        }

        return null;
    }

    /// <summary>
    /// Formats completion description as markdown.
    /// </summary>
    private static string FormatDocumentation(RoslynCompletion.CompletionDescription description)
    {
        var sb = new System.Text.StringBuilder();
        var inCode = false;

        foreach (var part in description.TaggedParts)
        {
            switch (part.Tag)
            {
                case TextTags.Keyword:
                case TextTags.Class:
                case TextTags.Struct:
                case TextTags.Interface:
                case TextTags.Enum:
                case TextTags.Module:
                case TextTags.Method:
                case TextTags.Property:
                case TextTags.Field:
                case TextTags.Local:
                case TextTags.Parameter:
                case TextTags.Namespace:
                case TextTags.TypeParameter:
                    if (!inCode)
                    {
                        sb.Append('`');
                        inCode = true;
                    }
                    sb.Append(part.Text);
                    break;

                case TextTags.Punctuation:
                case TextTags.Operator:
                    sb.Append(part.Text);
                    break;

                case TextTags.LineBreak:
                    if (inCode)
                    {
                        sb.Append('`');
                        inCode = false;
                    }
                    sb.AppendLine();
                    sb.AppendLine();
                    break;

                default:
                    if (inCode)
                    {
                        sb.Append('`');
                        inCode = false;
                    }
                    sb.Append(part.Text);
                    break;
            }
        }

        if (inCode)
        {
            sb.Append('`');
        }

        return sb.ToString().Trim();
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
}
