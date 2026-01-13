// FormattingService - Provides document and range formatting via Roslyn
// Services Layer as defined in docs/architecture.md Section 5.4

using System.Collections.Generic;
using System.Linq;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.Formatting;
using Microsoft.CodeAnalysis.Options;
using Microsoft.CodeAnalysis.Text;
using Microsoft.Extensions.Logging;
using VbNet.LanguageServer.Protocol;
using VbNet.LanguageServer.Workspace;
using LspFormattingOptions = VbNet.LanguageServer.Protocol.FormattingOptions;
using RoslynFormattingOptions = Microsoft.CodeAnalysis.Formatting.FormattingOptions;

namespace VbNet.LanguageServer.Services;

/// <summary>
/// Provides document and range formatting for VB.NET documents.
/// </summary>
public sealed class FormattingService
{
    private readonly WorkspaceManager _workspaceManager;
    private readonly DocumentManager _documentManager;
    private readonly ILogger<FormattingService> _logger;

    public FormattingService(
        WorkspaceManager workspaceManager,
        DocumentManager documentManager,
        ILogger<FormattingService> logger)
    {
        _workspaceManager = workspaceManager ?? throw new ArgumentNullException(nameof(workspaceManager));
        _documentManager = documentManager ?? throw new ArgumentNullException(nameof(documentManager));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    public async Task<TextEdit[]> FormatDocumentAsync(
        DocumentFormattingParams @params,
        CancellationToken cancellationToken)
    {
        if (@params?.TextDocument == null)
        {
            return Array.Empty<TextEdit>();
        }

        return await FormatAsync(@params.TextDocument.Uri, null, @params.Options, cancellationToken);
    }

    public async Task<TextEdit[]> FormatRangeAsync(
        DocumentRangeFormattingParams @params,
        CancellationToken cancellationToken)
    {
        if (@params?.TextDocument == null)
        {
            return Array.Empty<TextEdit>();
        }

        return await FormatAsync(@params.TextDocument.Uri, @params.Range, @params.Options, cancellationToken);
    }

    private async Task<TextEdit[]> FormatAsync(
        string uri,
        Protocol.Range? range,
        LspFormattingOptions options,
        CancellationToken cancellationToken)
    {
        var document = _documentManager.GetRoslynDocument(uri);
        if (document == null)
        {
            _logger.LogTrace("No Roslyn document found for formatting: {Uri}", uri);
            return Array.Empty<TextEdit>();
        }

        var sourceText = await document.GetTextAsync(cancellationToken);
        OptionSet optionSet = await document.GetOptionsAsync(cancellationToken);
        optionSet = ApplyFormattingOptions(optionSet, document.Project.Language, options);

        Document formattedDocument;
        if (range != null)
        {
            var span = GetTextSpan(range, sourceText);
            formattedDocument = await Formatter.FormatAsync(document, span, optionSet, cancellationToken);
        }
        else
        {
            formattedDocument = await Formatter.FormatAsync(document, optionSet, cancellationToken);
        }

        var formattedText = await formattedDocument.GetTextAsync(cancellationToken);
        var finalText = range == null
            ? ApplyPostFormattingOptions(formattedText, options)
            : formattedText;

        if (finalText.ContentEquals(sourceText))
        {
            return Array.Empty<TextEdit>();
        }

        var changes = finalText.GetTextChanges(sourceText);
        return changes.Select(change => CreateTextEdit(change, sourceText)).ToArray();
    }

    private static OptionSet ApplyFormattingOptions(OptionSet optionSet, string language, LspFormattingOptions options)
    {
        var useTabs = !options.InsertSpaces;
        var tabSize = options.TabSize <= 0 ? 4 : options.TabSize;

        optionSet = optionSet.WithChangedOption(RoslynFormattingOptions.UseTabs, language, useTabs);
        optionSet = optionSet.WithChangedOption(RoslynFormattingOptions.TabSize, language, tabSize);
        optionSet = optionSet.WithChangedOption(RoslynFormattingOptions.IndentationSize, language, tabSize);

        return optionSet;
    }

    private static SourceText ApplyPostFormattingOptions(SourceText text, LspFormattingOptions options)
    {
        var current = text.ToString();
        var newline = current.Contains("\r\n", StringComparison.Ordinal) ? "\r\n" : "\n";

        if (options.TrimTrailingWhitespace == true)
        {
            var lines = current.Split(new[] { "\r\n", "\n" }, StringSplitOptions.None);
            for (var i = 0; i < lines.Length; i++)
            {
                lines[i] = lines[i].TrimEnd(' ', '\t');
            }

            current = string.Join(newline, lines);
        }

        if (options.TrimFinalNewlines == true)
        {
            current = current.TrimEnd('\r', '\n');
        }

        if (options.InsertFinalNewline == true && !current.EndsWith(newline, StringComparison.Ordinal))
        {
            current += newline;
        }

        return SourceText.From(current);
    }

    private static TextSpan GetTextSpan(Protocol.Range range, SourceText text)
    {
        var startPosition = GetPosition(range.Start, text);
        var endPosition = GetPosition(range.End, text);
        return TextSpan.FromBounds(startPosition, endPosition);
    }

    private static int GetPosition(Position position, SourceText text)
    {
        var line = Math.Min(position.Line, text.Lines.Count - 1);
        line = Math.Max(0, line);

        var textLine = text.Lines[line];
        var character = Math.Min(position.Character, textLine.End - textLine.Start);
        character = Math.Max(0, character);

        return textLine.Start + character;
    }

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

    private static TextEdit CreateTextEdit(TextChange change, SourceText sourceText)
    {
        return new TextEdit
        {
            Range = GetRange(change.Span, sourceText),
            NewText = change.NewText ?? string.Empty
        };
    }
}
