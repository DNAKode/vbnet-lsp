// CodeActionsService - Provides basic code actions via LSP
// Services Layer as defined in docs/architecture.md Section 5.4

using Microsoft.CodeAnalysis.Text;
using Microsoft.Extensions.Logging;
using VbNet.LanguageServer.Protocol;
using VbNet.LanguageServer.Workspace;

namespace VbNet.LanguageServer.Services;

/// <summary>
/// Provides baseline code actions for VB.NET documents.
/// </summary>
public sealed class CodeActionsService
{
    private readonly WorkspaceManager _workspaceManager;
    private readonly DocumentManager _documentManager;
    private readonly ILogger<CodeActionsService> _logger;

    private static readonly string[] SupportedKinds = new[]
    {
        CodeActionKind.Source
    };

    public CodeActionsService(
        WorkspaceManager workspaceManager,
        DocumentManager documentManager,
        ILogger<CodeActionsService> logger)
    {
        _workspaceManager = workspaceManager ?? throw new ArgumentNullException(nameof(workspaceManager));
        _documentManager = documentManager ?? throw new ArgumentNullException(nameof(documentManager));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    public static CodeActionOptions GetDefaultOptions()
    {
        return new CodeActionOptions
        {
            CodeActionKinds = SupportedKinds,
            ResolveProvider = false
        };
    }

    public async Task<CodeAction[]> GetCodeActionsAsync(CodeActionParams @params, CancellationToken cancellationToken)
    {
        if (@params?.TextDocument == null)
        {
            return Array.Empty<CodeAction>();
        }

        var uri = @params.TextDocument.Uri;
        var openDoc = _documentManager.GetOpenDocument(uri);
        SourceText? sourceText = openDoc?.Text;

        if (sourceText == null)
        {
            var document = _documentManager.GetRoslynDocument(uri);
            if (document == null)
            {
                _logger.LogTrace("No document available for code actions: {Uri}", uri);
                return Array.Empty<CodeAction>();
            }

            sourceText = await document.GetTextAsync(cancellationToken);
        }

        cancellationToken.ThrowIfCancellationRequested();

        var actions = new List<CodeAction>();
        var insertionLine = GetInsertionLine(sourceText);

        if (!ContainsOptionLine(sourceText, "Option Strict"))
        {
            actions.Add(BuildOptionAction(uri, insertionLine, "Option Strict On"));
        }

        if (!ContainsOptionLine(sourceText, "Option Explicit"))
        {
            actions.Add(BuildOptionAction(uri, insertionLine, "Option Explicit On"));
        }

        if (!ContainsOptionLine(sourceText, "Option Infer"))
        {
            actions.Add(BuildOptionAction(uri, insertionLine, "Option Infer On"));
        }

        return actions.ToArray();
    }

    private static bool ContainsOptionLine(SourceText sourceText, string optionPrefix)
    {
        foreach (var line in sourceText.Lines)
        {
            var text = line.ToString().TrimStart();
            if (text.StartsWith(optionPrefix, StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }
        }

        return false;
    }

    private static int GetInsertionLine(SourceText sourceText)
    {
        var insertionLine = 0;
        foreach (var line in sourceText.Lines)
        {
            var trimmed = line.ToString().TrimStart();
            if (trimmed.Length == 0 || trimmed.StartsWith("'", StringComparison.Ordinal))
            {
                insertionLine = line.LineNumber + 1;
                continue;
            }

            if (trimmed.StartsWith("Option ", StringComparison.OrdinalIgnoreCase))
            {
                insertionLine = line.LineNumber + 1;
                continue;
            }

            if (trimmed.StartsWith("Imports ", StringComparison.OrdinalIgnoreCase))
            {
                break;
            }

            break;
        }

        return Math.Min(insertionLine, sourceText.Lines.Count);
    }

    private static CodeAction BuildOptionAction(string uri, int insertionLine, string optionText)
    {
        var newLine = Environment.NewLine;
        var edit = new WorkspaceEdit
        {
            Changes = new Dictionary<string, TextEdit[]>
            {
                [uri] = new[]
                {
                    new TextEdit
                    {
                        Range = new Protocol.Range
                        {
                            Start = new Position(insertionLine, 0),
                            End = new Position(insertionLine, 0)
                        },
                        NewText = optionText + newLine
                    }
                }
            }
        };

        return new CodeAction
        {
            Title = $"Add {optionText}",
            Kind = CodeActionKind.Source,
            Edit = edit,
            IsPreferred = true
        };
    }
}
