' SelectionRangeService - Provides Selection Range via LSP
' Services Layer as defined in docs/architecture.md Section 5.4

Imports Microsoft.CodeAnalysis
Imports Microsoft.CodeAnalysis.Text
Imports Microsoft.Extensions.Logging
Imports VbNet.LanguageServer.Protocol
Imports VbNet.LanguageServer.Workspace

Namespace Services

    ''' <summary>
    ''' Provides selection range functionality for VB.NET documents.
    ''' </summary>
    Public NotInheritable Class SelectionRangeService
        Private ReadOnly _documentManager As DocumentManager
        Private ReadOnly _logger As ILogger(Of SelectionRangeService)

        Public Sub New(documentManager As DocumentManager, logger As ILogger(Of SelectionRangeService))
            If documentManager Is Nothing Then
                Throw New ArgumentNullException(NameOf(documentManager))
            End If
            If logger Is Nothing Then
                Throw New ArgumentNullException(NameOf(logger))
            End If

            _documentManager = documentManager
            _logger = logger
        End Sub

        Public Async Function GetSelectionRangesAsync(parameters As SelectionRangeParams, cancellationToken As CancellationToken) As Task(Of SelectionRange())
            If parameters Is Nothing OrElse parameters.TextDocument Is Nothing Then
                Return Array.Empty(Of SelectionRange)()
            End If

            Dim uri = parameters.TextDocument.Uri
            Dim positions = parameters.Positions
            If positions Is Nothing OrElse positions.Length = 0 Then
                Return Array.Empty(Of SelectionRange)()
            End If

            Dim document = _documentManager.GetRoslynDocument(uri)
            If document Is Nothing Then
                _logger.LogTrace("No Roslyn document found for: {Uri}", uri)
                Return Array.Empty(Of SelectionRange)()
            End If

            Dim sourceText = Await document.GetTextAsync(cancellationToken).ConfigureAwait(False)
            Dim root = Await document.GetSyntaxRootAsync(cancellationToken).ConfigureAwait(False)
            If root Is Nothing Then
                Return Array.Empty(Of SelectionRange)()
            End If

            Dim results As New List(Of SelectionRange)()

            For Each position In positions
                cancellationToken.ThrowIfCancellationRequested()

                Dim offset = GetOffset(position, sourceText)
                Dim token = root.FindToken(offset)
                Dim node = token.Parent

                If node Is Nothing Then
                    results.Add(New SelectionRange With {.Range = New Protocol.Range()})
                    Continue For
                End If

                Dim current As SelectionRange = Nothing
                Dim currentNode As SyntaxNode = node

                While currentNode IsNot Nothing
                    Dim range = GetRange(currentNode.Span, sourceText)
                    Dim nextRange = New SelectionRange With {
                        .Range = range,
                        .Parent = current
                    }

                    current = nextRange
                    currentNode = currentNode.Parent
                End While

                results.Add(current)
            Next

            Return results.ToArray()
        End Function

        Private Shared Function GetOffset(position As Position, text As SourceText) As Integer
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
    End Class

End Namespace
