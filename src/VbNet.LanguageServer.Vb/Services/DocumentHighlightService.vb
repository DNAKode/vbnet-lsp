' DocumentHighlightService - Provides Document Highlight via LSP
' Services Layer as defined in docs/architecture.md Section 5.4

Imports Microsoft.CodeAnalysis
Imports Microsoft.CodeAnalysis.FindSymbols
Imports Microsoft.CodeAnalysis.Text
Imports Microsoft.Extensions.Logging
Imports VbNet.LanguageServer.Protocol
Imports VbNet.LanguageServer.Workspace

Namespace Services

    ''' <summary>
    ''' Provides document highlight functionality for VB.NET documents.
    ''' </summary>
    Public NotInheritable Class DocumentHighlightService
        Private ReadOnly _workspaceManager As WorkspaceManager
        Private ReadOnly _documentManager As DocumentManager
        Private ReadOnly _logger As ILogger(Of DocumentHighlightService)

        Public Sub New(workspaceManager As WorkspaceManager, documentManager As DocumentManager, logger As ILogger(Of DocumentHighlightService))
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

        ''' <summary>
        ''' Gets document highlights for the symbol at the specified position.
        ''' </summary>
        Public Async Function GetDocumentHighlightsAsync(parameters As DocumentHighlightParams, cancellationToken As CancellationToken) As Task(Of DocumentHighlight())
            If parameters Is Nothing OrElse parameters.TextDocument Is Nothing Then
                Return Array.Empty(Of DocumentHighlight)()
            End If

            Dim uri = parameters.TextDocument.Uri
            Dim position = parameters.Position

            _logger.LogDebug("Document highlight requested at {Uri} ({Line}:{Character})", uri, position.Line, position.Character)

            Dim document = _documentManager.GetRoslynDocument(uri)
            If document Is Nothing Then
                _logger.LogTrace("No Roslyn document found for: {Uri}", uri)
                Return Array.Empty(Of DocumentHighlight)()
            End If

            Try
                Dim sourceText = Await document.GetTextAsync(cancellationToken).ConfigureAwait(False)
                Dim offset = GetOffset(position, sourceText)

                Dim symbol = Await FindSymbolAtPositionAsync(document, offset, cancellationToken).ConfigureAwait(False)
                If symbol Is Nothing Then
                    _logger.LogTrace("No symbol found at position for: {Uri}", uri)
                    Return Array.Empty(Of DocumentHighlight)()
                End If

                Dim references = Await SymbolFinder.FindReferencesAsync(symbol, _workspaceManager.CurrentSolution, cancellationToken).ConfigureAwait(False)

                Dim highlights As New List(Of DocumentHighlight)()

                For Each reference In references
                    For Each location In reference.Locations
                        cancellationToken.ThrowIfCancellationRequested()

                        Dim referenceDocument = location.Document
                        If referenceDocument Is Nothing OrElse referenceDocument.Id <> document.Id Then
                            Continue For
                        End If

                        Dim referenceText = Await referenceDocument.GetTextAsync(cancellationToken).ConfigureAwait(False)
                        Dim range = GetRange(location.Location.SourceSpan, referenceText)

                        highlights.Add(New DocumentHighlight With {
                            .Range = range,
                            .Kind = DocumentHighlightKind.Read
                        })
                    Next
                Next

                Dim declaration = symbol.DeclaringSyntaxReferences.FirstOrDefault()
                If declaration IsNot Nothing Then
                    Dim declDocument = document.Project.Solution.GetDocument(declaration.SyntaxTree)
                    If declDocument IsNot Nothing AndAlso declDocument.Id = document.Id Then
                        Dim declText = Await declDocument.GetTextAsync(cancellationToken).ConfigureAwait(False)
                        Dim range = GetRange(declaration.Span, declText)
                        highlights.Add(New DocumentHighlight With {
                            .Range = range,
                            .Kind = DocumentHighlightKind.Write
                        })
                    End If
                End If

                Return highlights _
                    .GroupBy(Function(h) New With {
                        .StartLine = h.Range.Start.Line,
                        .StartCharacter = h.Range.Start.Character,
                        .EndLine = h.Range.End.Line,
                        .EndCharacter = h.Range.End.Character
                    }) _
                    .Select(Function(g) g.First()) _
                    .ToArray()
            Catch ex As OperationCanceledException
                _logger.LogTrace("Document highlight cancelled for: {Uri}", uri)
                Throw
            Catch ex As Exception
                _logger.LogError(ex, "Error getting document highlight for: {Uri}", uri)
                Return Array.Empty(Of DocumentHighlight)()
            End Try
        End Function

        Private Async Function FindSymbolAtPositionAsync(document As Document, position As Integer, cancellationToken As CancellationToken) As Task(Of ISymbol)
            Dim semanticModel = Await document.GetSemanticModelAsync(cancellationToken).ConfigureAwait(False)
            If semanticModel Is Nothing Then
                Return Nothing
            End If

            Dim syntaxRoot = Await document.GetSyntaxRootAsync(cancellationToken).ConfigureAwait(False)
            If syntaxRoot Is Nothing Then
                Return Nothing
            End If

            Dim token = syntaxRoot.FindToken(position)
            If token.Parent Is Nothing Then
                Return Nothing
            End If

            Dim symbolInfo = semanticModel.GetSymbolInfo(token.Parent, cancellationToken)
            Dim symbol = If(symbolInfo.Symbol, symbolInfo.CandidateSymbols.FirstOrDefault())

            If symbol Is Nothing Then
                symbol = semanticModel.GetDeclaredSymbol(token.Parent, cancellationToken)
            End If

            If symbol Is Nothing Then
                Dim typeInfo = semanticModel.GetTypeInfo(token.Parent, cancellationToken)
                symbol = typeInfo.Type
            End If

            Return symbol
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
