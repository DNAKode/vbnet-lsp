' FormattingService - Provides document and range formatting via Roslyn
' Services Layer as defined in docs/architecture.md Section 5.4

Imports System.Collections.Generic
Imports System.Linq
Imports Microsoft.CodeAnalysis
Imports Microsoft.CodeAnalysis.Formatting
Imports Microsoft.CodeAnalysis.Options
Imports Microsoft.CodeAnalysis.Text
Imports Microsoft.Extensions.Logging
Imports VbNet.LanguageServer.Protocol
Imports VbNet.LanguageServer.Workspace
Imports LspFormattingOptions = VbNet.LanguageServer.Protocol.FormattingOptions
Imports RoslynFormattingOptions = Microsoft.CodeAnalysis.Formatting.FormattingOptions

Namespace Services

    ''' <summary>
    ''' Provides document and range formatting for VB.NET documents.
    ''' </summary>
    Public NotInheritable Class FormattingService
        Private ReadOnly _workspaceManager As WorkspaceManager
        Private ReadOnly _documentManager As DocumentManager
        Private ReadOnly _logger As ILogger(Of FormattingService)

        Public Sub New(workspaceManager As WorkspaceManager, documentManager As DocumentManager, logger As ILogger(Of FormattingService))
            If workspaceManager Is Nothing Then
                Throw New ArgumentNullException(NameOf(workspaceManager))
            End If
            If documentManager Is Nothing Then
                Throw New ArgumentNullException(NameOf(documentManager))
            End If
            If logger Is Nothing Then
                Throw New ArgumentNullException(NameOf(logger))
            End If

            _workspaceManager = workspaceManager
            _documentManager = documentManager
            _logger = logger
        End Sub

        Public Async Function FormatDocumentAsync(parameters As DocumentFormattingParams, cancellationToken As CancellationToken) As Task(Of TextEdit())
            If parameters Is Nothing OrElse parameters.TextDocument Is Nothing Then
                Return Array.Empty(Of TextEdit)()
            End If

            Return Await FormatAsync(parameters.TextDocument.Uri, Nothing, parameters.Options, cancellationToken).ConfigureAwait(False)
        End Function

        Public Async Function FormatRangeAsync(parameters As DocumentRangeFormattingParams, cancellationToken As CancellationToken) As Task(Of TextEdit())
            If parameters Is Nothing OrElse parameters.TextDocument Is Nothing Then
                Return Array.Empty(Of TextEdit)()
            End If

            Return Await FormatAsync(parameters.TextDocument.Uri, parameters.Range, parameters.Options, cancellationToken).ConfigureAwait(False)
        End Function

        Private Async Function FormatAsync(uri As String, range As Protocol.Range, options As LspFormattingOptions, cancellationToken As CancellationToken) As Task(Of TextEdit())
            Dim document = _documentManager.GetRoslynDocument(uri)
            If document Is Nothing Then
                _logger.LogTrace("No Roslyn document found for formatting: {Uri}", uri)
                Return Array.Empty(Of TextEdit)()
            End If

            Dim sourceText = Await document.GetTextAsync(cancellationToken).ConfigureAwait(False)
            Dim optionSet As OptionSet = Await document.GetOptionsAsync(cancellationToken).ConfigureAwait(False)
            optionSet = ApplyFormattingOptions(optionSet, document.Project.Language, options)

            Dim formattedDocument As Document
            If range IsNot Nothing Then
                Dim span = GetTextSpan(range, sourceText)
                formattedDocument = Await Formatter.FormatAsync(document, span, optionSet, cancellationToken).ConfigureAwait(False)
            Else
                formattedDocument = Await Formatter.FormatAsync(document, optionSet, cancellationToken).ConfigureAwait(False)
            End If

            Dim formattedText = Await formattedDocument.GetTextAsync(cancellationToken).ConfigureAwait(False)
            Dim finalText = If(range Is Nothing, ApplyPostFormattingOptions(formattedText, options), formattedText)

            If finalText.ContentEquals(sourceText) Then
                Return Array.Empty(Of TextEdit)()
            End If

            Dim changes = finalText.GetTextChanges(sourceText)
            Return changes.Select(Function(change) CreateTextEdit(change, sourceText)).ToArray()
        End Function

        Private Shared Function ApplyFormattingOptions(optionSet As OptionSet, language As String, options As LspFormattingOptions) As OptionSet
            Dim useTabs = Not options.InsertSpaces
            Dim tabSize = If(options.TabSize <= 0, 4, options.TabSize)

            optionSet = optionSet.WithChangedOption(RoslynFormattingOptions.UseTabs, language, useTabs)
            optionSet = optionSet.WithChangedOption(RoslynFormattingOptions.TabSize, language, tabSize)
            optionSet = optionSet.WithChangedOption(RoslynFormattingOptions.IndentationSize, language, tabSize)

            Return optionSet
        End Function

        Private Shared Function ApplyPostFormattingOptions(text As SourceText, options As LspFormattingOptions) As SourceText
            Dim current = text.ToString()
            Dim newline = If(current.Contains(vbCrLf, StringComparison.Ordinal), vbCrLf, vbLf)

            If options.TrimTrailingWhitespace = True Then
                Dim lines = current.Split(New String() {vbCrLf, vbLf}, StringSplitOptions.None)
                For i = 0 To lines.Length - 1
                    lines(i) = lines(i).TrimEnd(" "c, ControlChars.Tab)
                Next

                current = String.Join(newline, lines)
            End If

            If options.TrimFinalNewlines = True Then
                current = current.TrimEnd(ChrW(13), ChrW(10))
            End If

            If options.InsertFinalNewline = True AndAlso Not current.EndsWith(newline, StringComparison.Ordinal) Then
                current &= newline
            End If

            Return SourceText.From(current)
        End Function

        Private Shared Function GetTextSpan(range As Protocol.Range, text As SourceText) As TextSpan
            Dim startPosition = GetPosition(range.Start, text)
            Dim endPosition = GetPosition(range.End, text)
            Return TextSpan.FromBounds(startPosition, endPosition)
        End Function

        Private Shared Function GetPosition(position As Position, text As SourceText) As Integer
            Dim line = Math.Min(position.Line, text.Lines.Count - 1)
            line = Math.Max(0, line)

            Dim textLine = text.Lines(line)
            Dim character = Math.Min(position.Character, textLine.End - textLine.Start)
            character = Math.Max(0, character)

            Return textLine.Start + character
        End Function

        Private Shared Function GetRange(span As TextSpan, sourceText As SourceText) As Protocol.Range
            Dim startLine = sourceText.Lines.GetLineFromPosition(span.Start)
            Dim endLine = sourceText.Lines.GetLineFromPosition(span.[End])

            Return New Protocol.Range With {
                .Start = New Position With {.Line = startLine.LineNumber, .Character = span.Start - startLine.Start},
                .[End] = New Position With {.Line = endLine.LineNumber, .Character = span.[End] - endLine.Start}
            }
        End Function

        Private Shared Function CreateTextEdit(change As TextChange, sourceText As SourceText) As TextEdit
            Return New TextEdit With {
                .Range = GetRange(change.Span, sourceText),
                .NewText = If(change.NewText, String.Empty)
            }
        End Function
    End Class

End Namespace
