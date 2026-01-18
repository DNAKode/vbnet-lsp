' ReferencesService - Provides Find All References via LSP
' Services Layer as defined in docs/architecture.md Section 5.4

Imports Microsoft.CodeAnalysis
Imports Microsoft.CodeAnalysis.FindSymbols
Imports Microsoft.CodeAnalysis.Text
Imports Microsoft.Extensions.Logging
Imports VbNet.LanguageServer.Protocol
Imports VbNet.LanguageServer.Workspace

Namespace Services

    ''' <summary>
    ''' Provides Find All References functionality for VB.NET documents.
    ''' Uses Roslyn's SymbolFinder to locate all references to symbols.
    ''' </summary>
    Public NotInheritable Class ReferencesService
        Private ReadOnly _workspaceManager As WorkspaceManager
        Private ReadOnly _documentManager As DocumentManager
        Private ReadOnly _logger As ILogger(Of ReferencesService)

        Public Sub New(workspaceManager As WorkspaceManager, documentManager As DocumentManager, logger As ILogger(Of ReferencesService))
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
        ''' Gets all reference locations for a symbol at the specified position.
        ''' </summary>
        Public Async Function GetReferencesAsync(parameters As ReferenceParams, cancellationToken As CancellationToken) As Task(Of Protocol.Location())
            If parameters Is Nothing OrElse parameters.TextDocument Is Nothing Then
                Return Array.Empty(Of Protocol.Location)()
            End If

            Dim uri = parameters.TextDocument.Uri
            Dim position = parameters.Position
            Dim includeDeclaration = If(parameters.Context?.IncludeDeclaration, True)

            _logger.LogDebug("References requested at {Uri} ({Line}:{Character}), includeDeclaration={IncludeDecl}", uri, position.Line, position.Character, includeDeclaration)

            Dim document = _documentManager.GetRoslynDocument(uri)
            If document Is Nothing Then
                _logger.LogTrace("No Roslyn document found for: {Uri}", uri)
                Return Array.Empty(Of Protocol.Location)()
            End If

            Try
                Dim sourceText = Await document.GetTextAsync(cancellationToken).ConfigureAwait(False)
                Dim offset = GetOffset(position, sourceText)

                cancellationToken.ThrowIfCancellationRequested()

                Dim symbol = Await FindSymbolAtPositionAsync(document, offset, cancellationToken).ConfigureAwait(False)
                If symbol Is Nothing Then
                    _logger.LogTrace("No symbol found at position for: {Uri}", uri)
                    Return Array.Empty(Of Protocol.Location)()
                End If

                cancellationToken.ThrowIfCancellationRequested()

                Dim references = Await SymbolFinder.FindReferencesAsync(symbol, document.Project.Solution, cancellationToken).ConfigureAwait(False)

                Dim locations As New List(Of Protocol.Location)()

                For Each reference In references
                    If includeDeclaration Then
                        For Each declLocation In reference.Definition.Locations
                            cancellationToken.ThrowIfCancellationRequested()

                            Dim location = Await CreateLocationFromRoslynLocationAsync(declLocation, document.Project.Solution, cancellationToken).ConfigureAwait(False)
                            If location IsNot Nothing Then
                                locations.Add(location)
                            End If
                        Next
                    End If

                    For Each refLocation In reference.Locations
                        cancellationToken.ThrowIfCancellationRequested()

                        Dim location = Await CreateLocationFromReferenceLocationAsync(refLocation, cancellationToken).ConfigureAwait(False)
                        If location IsNot Nothing Then
                            locations.Add(location)
                        End If
                    Next
                Next

                Dim distinctLocations = locations _
                    .GroupBy(Function(l) New With {
                        .Uri = l.Uri,
                        .StartLine = l.Range.Start.Line,
                        .StartCharacter = l.Range.Start.Character,
                        .EndLine = l.Range.[End].Line,
                        .EndCharacter = l.Range.[End].Character
                    }) _
                    .Select(Function(g) g.First()) _
                    .ToArray()

                _logger.LogDebug("Found {Count} reference(s) for symbol: {Symbol}", distinctLocations.Length, symbol.Name)

                Return distinctLocations
            Catch ex As OperationCanceledException
                _logger.LogTrace("References request cancelled for: {Uri}", uri)
                Throw
            Catch ex As Exception
                _logger.LogError(ex, "Error getting references for: {Uri}", uri)
                Return Array.Empty(Of Protocol.Location)()
            End Try
        End Function

        ''' <summary>
        ''' Finds the symbol at the specified position in the document.
        ''' </summary>
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

        ''' <summary>
        ''' Creates an LSP Location from a Roslyn Location.
        ''' </summary>
        Private Async Function CreateLocationFromRoslynLocationAsync(roslynLocation As Microsoft.CodeAnalysis.Location, solution As Solution, cancellationToken As CancellationToken) As Task(Of Protocol.Location)
            If Not roslynLocation.IsInSource Then
                Return Nothing
            End If

            Dim syntaxTree = roslynLocation.SourceTree
            If syntaxTree Is Nothing OrElse String.IsNullOrEmpty(syntaxTree.FilePath) Then
                Return Nothing
            End If

            Dim sourceText = Await syntaxTree.GetTextAsync(cancellationToken).ConfigureAwait(False)
            Dim span = roslynLocation.SourceSpan

            Dim range = GetRange(span, sourceText)
            Dim uri = New Uri(syntaxTree.FilePath).ToString()

            Return New Protocol.Location With {
                .Uri = uri,
                .Range = range
            }
        End Function

        ''' <summary>
        ''' Creates an LSP Location from a Roslyn ReferenceLocation.
        ''' </summary>
        Private Async Function CreateLocationFromReferenceLocationAsync(referenceLocation As ReferenceLocation, cancellationToken As CancellationToken) As Task(Of Protocol.Location)
            Dim document = referenceLocation.Document
            If document Is Nothing Then
                Return Nothing
            End If

            Dim syntaxTree = Await document.GetSyntaxTreeAsync(cancellationToken).ConfigureAwait(False)
            If syntaxTree Is Nothing Then
                Return Nothing
            End If

            Dim filePath = syntaxTree.FilePath
            If String.IsNullOrEmpty(filePath) Then
                Return Nothing
            End If

            Dim sourceText = Await syntaxTree.GetTextAsync(cancellationToken).ConfigureAwait(False)
            Dim span = referenceLocation.Location.SourceSpan

            Dim range = GetRange(span, sourceText)
            Dim uri = New Uri(filePath).ToString()

            Return New Protocol.Location With {
                .Uri = uri,
                .Range = range
            }
        End Function

        ''' <summary>
        ''' Converts an LSP Position to a Roslyn offset.
        ''' </summary>
        Private Shared Function GetOffset(position As Position, text As SourceText) As Integer
            Dim line = Math.Min(position.Line, text.Lines.Count - 1)
            line = Math.Max(0, line)

            Dim textLine = text.Lines(line)
            Dim character = Math.Min(position.Character, textLine.End - textLine.Start)
            character = Math.Max(0, character)

            Return textLine.Start + character
        End Function

        ''' <summary>
        ''' Converts a TextSpan to an LSP Range.
        ''' </summary>
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
