' DefinitionService - Provides Go to Definition via LSP
' Services Layer as defined in docs/architecture.md Section 5.4

Imports Microsoft.CodeAnalysis
Imports Microsoft.CodeAnalysis.FindSymbols
Imports Microsoft.CodeAnalysis.Text
Imports Microsoft.Extensions.Logging
Imports VbNet.LanguageServer.Protocol
Imports VbNet.LanguageServer.Workspace

Namespace Services

    ''' <summary>
    ''' Provides Go to Definition functionality for VB.NET documents.
    ''' Uses Roslyn's symbol finding capabilities to locate definitions.
    ''' </summary>
    Public NotInheritable Class DefinitionService
        Private ReadOnly _workspaceManager As WorkspaceManager
        Private ReadOnly _documentManager As DocumentManager
        Private ReadOnly _logger As ILogger(Of DefinitionService)

        Public Sub New(workspaceManager As WorkspaceManager, documentManager As DocumentManager, logger As ILogger(Of DefinitionService))
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
        ''' Gets the definition location(s) for a symbol at the specified position.
        ''' </summary>
        Public Async Function GetDefinitionAsync(parameters As DefinitionParams, cancellationToken As CancellationToken) As Task(Of Protocol.Location())
            If parameters Is Nothing OrElse parameters.TextDocument Is Nothing Then
                Return Array.Empty(Of Protocol.Location)()
            End If

            Dim uri = parameters.TextDocument.Uri
            Dim position = parameters.Position

            _logger.LogDebug("Definition requested at {Uri} ({Line}:{Character})", uri, position.Line, position.Character)

            Dim document = _documentManager.GetRoslynDocument(uri)
            If document Is Nothing Then
                _logger.LogTrace("No Roslyn document found for: {Uri}", uri)
                Return Array.Empty(Of Protocol.Location)()
            End If

            Try
                Dim sourceText = Await document.GetTextAsync(cancellationToken).ConfigureAwait(False)
                Dim offset = GetOffset(position, sourceText)

                cancellationToken.ThrowIfCancellationRequested()

                Dim semanticModel = Await document.GetSemanticModelAsync(cancellationToken).ConfigureAwait(False)
                If semanticModel Is Nothing Then
                    _logger.LogWarning("Could not get semantic model for: {Uri}", uri)
                    Return Array.Empty(Of Protocol.Location)()
                End If

                Dim syntaxRoot = Await document.GetSyntaxRootAsync(cancellationToken).ConfigureAwait(False)
                If syntaxRoot Is Nothing Then
                    Return Array.Empty(Of Protocol.Location)()
                End If

                Dim token = syntaxRoot.FindToken(offset)
                If token.Span.Length = 0 Then
                    Return Array.Empty(Of Protocol.Location)()
                End If

                cancellationToken.ThrowIfCancellationRequested()

                Dim symbol = Await FindSymbolAtPositionAsync(document, offset, cancellationToken).ConfigureAwait(False)
                If symbol Is Nothing Then
                    _logger.LogTrace("No symbol found at position for: {Uri}", uri)
                    Return Array.Empty(Of Protocol.Location)()
                End If

                Dim locations = Await GetSymbolDefinitionLocationsAsync(symbol, document.Project.Solution, cancellationToken).ConfigureAwait(False)

                _logger.LogDebug("Found {Count} definition location(s) for symbol: {Symbol}", locations.Length, symbol.Name)

                Return locations
            Catch ex As OperationCanceledException
                _logger.LogTrace("Definition request cancelled for: {Uri}", uri)
                Throw
            Catch ex As Exception
                _logger.LogError(ex, "Error getting definition for: {Uri}", uri)
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
        ''' Gets the definition locations for a symbol.
        ''' </summary>
        Private Async Function GetSymbolDefinitionLocationsAsync(symbol As ISymbol, solution As Solution, cancellationToken As CancellationToken) As Task(Of Protocol.Location())
            Dim locations As New List(Of Protocol.Location)()

            Dim definitionSymbol = If(symbol.OriginalDefinition, symbol)

            Await AddLocationsForSymbolAsync(definitionSymbol, locations, cancellationToken).ConfigureAwait(False)

            If locations.Count = 0 AndAlso definitionSymbol.Locations.Any(Function(l) l.IsInMetadata) Then
                Dim sourceSymbol = Await SymbolFinder.FindSourceDefinitionAsync(definitionSymbol, solution, cancellationToken).ConfigureAwait(False)
                If sourceSymbol Is Nothing Then
                    sourceSymbol = Await FindMatchingSourceSymbolAsync(definitionSymbol, solution, cancellationToken).ConfigureAwait(False)
                End If

                If sourceSymbol IsNot Nothing Then
                    Await AddLocationsForSymbolAsync(sourceSymbol, locations, cancellationToken).ConfigureAwait(False)
                End If
            End If

            If locations.Count = 0 AndAlso definitionSymbol.Locations.Any(Function(l) l.IsInMetadata) Then
                _logger.LogTrace("Symbol {Symbol} is defined in metadata, no source location available", definitionSymbol.Name)
            End If

            Return locations.ToArray()
        End Function

        Private Async Function AddLocationsForSymbolAsync(symbol As ISymbol, locations As IList(Of Protocol.Location), cancellationToken As CancellationToken) As Task
            Dim definitionSymbol = If(symbol.OriginalDefinition, symbol)

            For Each syntaxRef In definitionSymbol.DeclaringSyntaxReferences
                cancellationToken.ThrowIfCancellationRequested()

                Dim location = Await CreateLocationFromSyntaxReferenceAsync(syntaxRef, cancellationToken).ConfigureAwait(False)
                If location IsNot Nothing Then
                    locations.Add(location)
                End If
            Next
        End Function

        Private Shared Async Function FindMatchingSourceSymbolAsync(symbol As ISymbol, solution As Solution, cancellationToken As CancellationToken) As Task(Of ISymbol)
            Dim documentationId = DocumentationCommentId.CreateDeclarationId(symbol)
            If String.IsNullOrWhiteSpace(documentationId) Then
                Return Nothing
            End If

            Dim containingAssemblyName = symbol.ContainingAssembly?.Name

            For Each project In solution.Projects
                cancellationToken.ThrowIfCancellationRequested()

                If Not String.IsNullOrWhiteSpace(containingAssemblyName) AndAlso
                    Not String.Equals(project.AssemblyName, containingAssemblyName, StringComparison.OrdinalIgnoreCase) Then
                    Continue For
                End If

                Dim compilation = Await project.GetCompilationAsync(cancellationToken).ConfigureAwait(False)
                If compilation Is Nothing Then
                    Continue For
                End If

                Dim sourceSymbol = DocumentationCommentId.GetFirstSymbolForDeclarationId(documentationId, compilation)
                If sourceSymbol IsNot Nothing AndAlso sourceSymbol.DeclaringSyntaxReferences.Length > 0 Then
                    Return sourceSymbol
                End If
            Next

            Return Nothing
        End Function

        ''' <summary>
        ''' Creates an LSP Location from a Roslyn SyntaxReference.
        ''' </summary>
        Private Async Function CreateLocationFromSyntaxReferenceAsync(syntaxRef As SyntaxReference, cancellationToken As CancellationToken) As Task(Of Protocol.Location)
            Dim syntaxTree = syntaxRef.SyntaxTree
            Dim filePath = syntaxTree.FilePath

            If String.IsNullOrEmpty(filePath) Then
                Return Nothing
            End If

            Dim sourceText = Await syntaxTree.GetTextAsync(cancellationToken).ConfigureAwait(False)
            Dim span = syntaxRef.Span

            Dim syntax = Await syntaxRef.GetSyntaxAsync(cancellationToken).ConfigureAwait(False)
            Dim identifierSpan = GetIdentifierSpan(syntax)
            If identifierSpan Is Nothing Then
                identifierSpan = span
            End If

            Dim range = GetRange(identifierSpan.Value, sourceText)
            Dim uri = New Uri(filePath).ToString()

            Return New Protocol.Location With {
                .Uri = uri,
                .Range = range
            }
        End Function

        ''' <summary>
        ''' Gets the span of the identifier within a declaration syntax node.
        ''' </summary>
        Private Shared Function GetIdentifierSpan(node As SyntaxNode) As TextSpan?
            For Each child In node.ChildTokens()
                If IsIdentifierToken(child) Then
                    Return child.Span
                End If
            Next

            Dim firstToken = node.GetFirstToken()
            If IsIdentifierToken(firstToken) Then
                Return firstToken.Span
            End If

            Return Nothing
        End Function

        Private Shared Function IsIdentifierToken(token As SyntaxToken) As Boolean
            Return token.RawKind = CInt(Microsoft.CodeAnalysis.VisualBasic.SyntaxKind.IdentifierToken) OrElse
                token.RawKind = CInt(Microsoft.CodeAnalysis.CSharp.SyntaxKind.IdentifierToken)
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
