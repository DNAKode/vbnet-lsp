// SemanticTokensService - Provides semantic tokens via LSP
// Services Layer as defined in docs/architecture.md Section 5.4

using System.Linq;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.Classification;
using Microsoft.CodeAnalysis.Text;
using Microsoft.Extensions.Logging;
using VbNet.LanguageServer.Protocol;
using VbNet.LanguageServer.Workspace;

namespace VbNet.LanguageServer.Services;

/// <summary>
/// Provides semantic tokens for VB.NET documents using Roslyn classifiers.
/// </summary>
public sealed class SemanticTokensService
{
    private readonly WorkspaceManager _workspaceManager;
    private readonly DocumentManager _documentManager;
    private readonly ILogger<SemanticTokensService> _logger;

    private static readonly string[] TokenTypes = new[]
    {
        "namespace",
        "type",
        "class",
        "struct",
        "interface",
        "enum",
        "typeParameter",
        "function",
        "method",
        "property",
        "variable",
        "parameter",
        "field",
        "event",
        "keyword",
        "comment",
        "string",
        "number",
        "operator"
    };

    private static readonly string[] TokenModifiers = new[]
    {
        "declaration",
        "static",
        "readonly"
    };

    private static readonly Dictionary<string, TokenInfo> ClassificationMap = new(StringComparer.Ordinal)
    {
        [ClassificationTypeNames.NamespaceName] = new TokenInfo("namespace"),
        [ClassificationTypeNames.ClassName] = new TokenInfo("class"),
        [ClassificationTypeNames.StructName] = new TokenInfo("struct"),
        [ClassificationTypeNames.InterfaceName] = new TokenInfo("interface"),
        [ClassificationTypeNames.EnumName] = new TokenInfo("enum"),
        [ClassificationTypeNames.TypeParameterName] = new TokenInfo("typeParameter"),
        [ClassificationTypeNames.DelegateName] = new TokenInfo("type"),
        [ClassificationTypeNames.ModuleName] = new TokenInfo("type"),
        [ClassificationTypeNames.MethodName] = new TokenInfo("method"),
        [ClassificationTypeNames.ExtensionMethodName] = new TokenInfo("method"),
        [ClassificationTypeNames.PropertyName] = new TokenInfo("property"),
        [ClassificationTypeNames.FieldName] = new TokenInfo("field"),
        [ClassificationTypeNames.EventName] = new TokenInfo("event"),
        [ClassificationTypeNames.ParameterName] = new TokenInfo("parameter"),
        [ClassificationTypeNames.LocalName] = new TokenInfo("variable"),
        [ClassificationTypeNames.ConstantName] = new TokenInfo("variable", modifier: TokenModifier.Readonly),
        [ClassificationTypeNames.Keyword] = new TokenInfo("keyword"),
        [ClassificationTypeNames.ControlKeyword] = new TokenInfo("keyword"),
        [ClassificationTypeNames.Comment] = new TokenInfo("comment"),
        [ClassificationTypeNames.StringLiteral] = new TokenInfo("string"),
        [ClassificationTypeNames.VerbatimStringLiteral] = new TokenInfo("string"),
        [ClassificationTypeNames.NumericLiteral] = new TokenInfo("number"),
        [ClassificationTypeNames.Operator] = new TokenInfo("operator"),
        [ClassificationTypeNames.OperatorOverloaded] = new TokenInfo("operator")
    };

    public SemanticTokensService(
        WorkspaceManager workspaceManager,
        DocumentManager documentManager,
        ILogger<SemanticTokensService> logger)
    {
        _workspaceManager = workspaceManager ?? throw new ArgumentNullException(nameof(workspaceManager));
        _documentManager = documentManager ?? throw new ArgumentNullException(nameof(documentManager));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    public static SemanticTokensOptions GetDefaultOptions()
    {
        return new SemanticTokensOptions
        {
            Legend = GetLegend(),
            Full = true,
            Range = true
        };
    }

    public static SemanticTokensLegend GetLegend()
    {
        return new SemanticTokensLegend
        {
            TokenTypes = TokenTypes,
            TokenModifiers = TokenModifiers
        };
    }

    public async Task<SemanticTokens> GetSemanticTokensAsync(SemanticTokensParams @params, CancellationToken cancellationToken)
    {
        if (@params?.TextDocument == null)
        {
            return new SemanticTokens();
        }

        var uri = @params.TextDocument.Uri;
        _logger.LogDebug("Semantic tokens requested for {Uri}", uri);

        var document = _documentManager.GetRoslynDocument(uri);
        if (document == null)
        {
            _logger.LogTrace("No Roslyn document found for: {Uri}", uri);
            return new SemanticTokens();
        }

        var sourceText = await document.GetTextAsync(cancellationToken);
        var span = new TextSpan(0, sourceText.Length);
        return await BuildTokensAsync(document, sourceText, span, cancellationToken);
    }

    public async Task<SemanticTokens> GetSemanticTokensRangeAsync(
        SemanticTokensRangeParams @params,
        CancellationToken cancellationToken)
    {
        if (@params?.TextDocument == null || @params.Range == null)
        {
            return new SemanticTokens();
        }

        var uri = @params.TextDocument.Uri;
        var document = _documentManager.GetRoslynDocument(uri);
        if (document == null)
        {
            _logger.LogTrace("No Roslyn document found for: {Uri}", uri);
            return new SemanticTokens();
        }

        var sourceText = await document.GetTextAsync(cancellationToken);
        var span = ToTextSpan(@params.Range, sourceText);
        return await BuildTokensAsync(document, sourceText, span, cancellationToken);
    }

    private static TextSpan ToTextSpan(Protocol.Range range, SourceText text)
    {
        var start = GetOffset(range.Start, text);
        var end = GetOffset(range.End, text);
        if (end < start)
        {
            (start, end) = (end, start);
        }

        return TextSpan.FromBounds(start, end);
    }

    private async Task<SemanticTokens> BuildTokensAsync(
        Document document,
        SourceText sourceText,
        TextSpan span,
        CancellationToken cancellationToken)
    {
        var classified = await Classifier.GetClassifiedSpansAsync(
            document,
            span,
            cancellationToken);

        if (!classified.Any())
        {
            return new SemanticTokens();
        }

        var tokens = new List<SemanticTokenData>();

        foreach (var item in classified)
        {
            if (!ClassificationMap.TryGetValue(item.ClassificationType, out var info))
            {
                continue;
            }

            if (!TryGetTokenTypeIndex(info.TokenType, out var tokenTypeIndex))
            {
                continue;
            }

            AppendTokenSegments(tokens, item.TextSpan, tokenTypeIndex, info.Modifier, sourceText);
        }

        tokens.Sort(SemanticTokenDataComparer.Instance);

        var data = EncodeTokens(tokens);
        return new SemanticTokens { Data = data };
    }

    private static void AppendTokenSegments(
        List<SemanticTokenData> tokens,
        TextSpan span,
        uint tokenType,
        TokenModifier modifier,
        SourceText sourceText)
    {
        var startLine = sourceText.Lines.GetLineFromPosition(span.Start);
        var endLine = sourceText.Lines.GetLineFromPosition(span.End);

        for (var line = startLine.LineNumber; line <= endLine.LineNumber; line++)
        {
            var textLine = sourceText.Lines[line];
            var lineStart = textLine.Start;
            var segmentStart = line == startLine.LineNumber ? span.Start - lineStart : 0;
            var segmentEnd = line == endLine.LineNumber ? span.End - lineStart : textLine.End - lineStart;
            var length = segmentEnd - segmentStart;
            if (length <= 0)
            {
                continue;
            }

            tokens.Add(new SemanticTokenData
            {
                Line = line,
                StartChar = segmentStart,
                Length = length,
                TokenType = tokenType,
                TokenModifiers = EncodeModifiers(modifier)
            });
        }
    }

    private static uint[] EncodeTokens(IReadOnlyList<SemanticTokenData> tokens)
    {
        var data = new List<uint>(tokens.Count * 5);
        var prevLine = 0;
        var prevStart = 0;

        foreach (var token in tokens)
        {
            var deltaLine = token.Line - prevLine;
            var deltaStart = deltaLine == 0 ? token.StartChar - prevStart : token.StartChar;

            data.Add((uint)deltaLine);
            data.Add((uint)deltaStart);
            data.Add((uint)token.Length);
            data.Add(token.TokenType);
            data.Add(token.TokenModifiers);

            prevLine = token.Line;
            prevStart = token.StartChar;
        }

        return data.ToArray();
    }

    private static uint EncodeModifiers(TokenModifier modifier)
    {
        return modifier switch
        {
            TokenModifier.Readonly => 1u << 2,
            _ => 0
        };
    }

    private static bool TryGetTokenTypeIndex(string tokenType, out uint index)
    {
        var idx = Array.IndexOf(TokenTypes, tokenType);
        if (idx < 0)
        {
            index = 0;
            return false;
        }

        index = (uint)idx;
        return true;
    }

    private static int GetOffset(Position position, SourceText text)
    {
        var line = Math.Min(position.Line, text.Lines.Count - 1);
        line = Math.Max(0, line);

        var textLine = text.Lines[line];
        var character = Math.Min(position.Character, textLine.End - textLine.Start);
        character = Math.Max(0, character);

        return textLine.Start + character;
    }

    private sealed class SemanticTokenDataComparer : IComparer<SemanticTokenData>
    {
        public static readonly SemanticTokenDataComparer Instance = new();

        public int Compare(SemanticTokenData x, SemanticTokenData y)
        {
            var line = x.Line.CompareTo(y.Line);
            if (line != 0)
            {
                return line;
            }

            return x.StartChar.CompareTo(y.StartChar);
        }
    }

    private readonly struct SemanticTokenData
    {
        public int Line { get; init; }
        public int StartChar { get; init; }
        public int Length { get; init; }
        public uint TokenType { get; init; }
        public uint TokenModifiers { get; init; }
    }

    private readonly struct TokenInfo
    {
        public TokenInfo(string tokenType, TokenModifier modifier = TokenModifier.None)
        {
            TokenType = tokenType;
            Modifier = modifier;
        }

        public string TokenType { get; }
        public TokenModifier Modifier { get; }
    }

    private enum TokenModifier
    {
        None,
        Readonly
    }
}
