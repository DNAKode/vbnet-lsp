// FoldingRangeService - Provides folding ranges for VB.NET documents
// Services Layer as defined in docs/architecture.md Section 5.4

using System.Collections.Generic;
using System.Linq;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.Text;
using Microsoft.CodeAnalysis.VisualBasic;
using Microsoft.CodeAnalysis.VisualBasic.Syntax;
using Microsoft.Extensions.Logging;
using VbNet.LanguageServer.Protocol;
using VbNet.LanguageServer.Workspace;

namespace VbNet.LanguageServer.Services;

/// <summary>
/// Provides folding ranges for VB.NET documents.
/// </summary>
public sealed class FoldingRangeService
{
    private readonly WorkspaceManager _workspaceManager;
    private readonly DocumentManager _documentManager;
    private readonly ILogger<FoldingRangeService> _logger;

    public FoldingRangeService(
        WorkspaceManager workspaceManager,
        DocumentManager documentManager,
        ILogger<FoldingRangeService> logger)
    {
        _workspaceManager = workspaceManager ?? throw new ArgumentNullException(nameof(workspaceManager));
        _documentManager = documentManager ?? throw new ArgumentNullException(nameof(documentManager));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    /// <summary>
    /// Gets folding ranges for a document.
    /// </summary>
    public async Task<FoldingRange[]> GetFoldingRangesAsync(
        FoldingRangeParams @params,
        CancellationToken cancellationToken)
    {
        if (@params?.TextDocument == null)
        {
            return Array.Empty<FoldingRange>();
        }

        var uri = @params.TextDocument.Uri;
        var document = _documentManager.GetRoslynDocument(uri);

        SourceText? sourceText;
        SyntaxNode? root;

        if (document != null)
        {
            sourceText = await document.GetTextAsync(cancellationToken);
            root = await document.GetSyntaxRootAsync(cancellationToken);
        }
        else
        {
            sourceText = await _documentManager.GetSourceTextAsync(uri, cancellationToken);
            if (sourceText == null)
            {
                return Array.Empty<FoldingRange>();
            }

            var syntaxTree = VisualBasicSyntaxTree.ParseText(sourceText, cancellationToken: cancellationToken);
            root = await syntaxTree.GetRootAsync(cancellationToken);
        }

        if (root == null || sourceText == null)
        {
            return Array.Empty<FoldingRange>();
        }

        var ranges = new List<FoldingRange>();

        AddRegionRanges(root, sourceText, ranges);
        AddBlockRanges(root, sourceText, ranges);

        return ranges.ToArray();
    }

    private static void AddRegionRanges(SyntaxNode root, SourceText sourceText, List<FoldingRange> ranges)
    {
        var regionStack = new Stack<RegionDirectiveTriviaSyntax>();

        foreach (var trivia in root.DescendantTrivia(descendIntoTrivia: true))
        {
            if (trivia.GetStructure() is RegionDirectiveTriviaSyntax region)
            {
                regionStack.Push(region);
                continue;
            }

            if (trivia.GetStructure() is EndRegionDirectiveTriviaSyntax endRegion)
            {
                if (regionStack.Count == 0)
                {
                    continue;
                }

                var startRegion = regionStack.Pop();
                var span = TextSpan.FromBounds(startRegion.SpanStart, endRegion.Span.End);
                AddRange(span, sourceText, FoldingRangeKind.Region, ranges);
            }
        }
    }

    private static void AddBlockRanges(SyntaxNode root, SourceText sourceText, List<FoldingRange> ranges)
    {
        foreach (var node in root.DescendantNodes())
        {
            switch (node)
            {
                case NamespaceBlockSyntax namespaceBlock:
                    AddRange(namespaceBlock.Span, sourceText, null, ranges);
                    break;
                case ModuleBlockSyntax moduleBlock:
                    AddRange(moduleBlock.Span, sourceText, null, ranges);
                    break;
                case ClassBlockSyntax classBlock:
                    AddRange(classBlock.Span, sourceText, null, ranges);
                    break;
                case StructureBlockSyntax structureBlock:
                    AddRange(structureBlock.Span, sourceText, null, ranges);
                    break;
                case InterfaceBlockSyntax interfaceBlock:
                    AddRange(interfaceBlock.Span, sourceText, null, ranges);
                    break;
                case EnumBlockSyntax enumBlock:
                    AddRange(enumBlock.Span, sourceText, null, ranges);
                    break;
                case MethodBlockSyntax methodBlock:
                    AddRange(methodBlock.Span, sourceText, null, ranges);
                    break;
                case PropertyBlockSyntax propertyBlock:
                    AddRange(propertyBlock.Span, sourceText, null, ranges);
                    break;
                case EventBlockSyntax eventBlock:
                    AddRange(eventBlock.Span, sourceText, null, ranges);
                    break;
                case AccessorBlockSyntax accessorBlock:
                    AddRange(accessorBlock.Span, sourceText, null, ranges);
                    break;
            }
        }
    }

    private static void AddRange(TextSpan span, SourceText sourceText, string? kind, List<FoldingRange> ranges)
    {
        if (span.Length == 0)
        {
            return;
        }

        var startLine = sourceText.Lines.GetLineFromPosition(span.Start);
        var endPosition = Math.Max(span.End - 1, span.Start);
        var endLine = sourceText.Lines.GetLineFromPosition(endPosition);

        if (endLine.LineNumber <= startLine.LineNumber)
        {
            return;
        }

        ranges.Add(new FoldingRange
        {
            StartLine = startLine.LineNumber,
            EndLine = endLine.LineNumber,
            Kind = kind
        });
    }
}
