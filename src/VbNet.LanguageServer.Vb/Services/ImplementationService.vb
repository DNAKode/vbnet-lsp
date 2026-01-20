' ImplementationService - Provides Go to Implementation via LSP
' Services Layer as defined in docs/architecture.md Section 5.4

Imports Microsoft.CodeAnalysis
Imports Microsoft.CodeAnalysis.FindSymbols
Imports Microsoft.CodeAnalysis.Text
Imports Microsoft.Extensions.Logging
Imports VbNet.LanguageServer.Protocol
Imports VbNet.LanguageServer.Workspace

Namespace Services

    ''' <summary>
    ''' Provides implementation location functionality for VB.NET documents.
    ''' </summary>
    Public NotInheritable Class ImplementationService
        Private ReadOnly _workspaceManager As WorkspaceManager
        Private ReadOnly _documentManager As DocumentManager
        Private ReadOnly _logger As ILogger(Of ImplementationService)

        Public Sub New(workspaceManager As WorkspaceManager, documentManager As DocumentManager, logger As ILogger(Of ImplementationService))
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
        ''' Gets implementation location(s) for the symbol at the specified position.
        ''' </summary>
        Public Async Function GetImplementationAsync(parameters As ImplementationParams, cancellationToken As CancellationToken) As Task(Of Protocol.Location())
            If parameters Is Nothing OrElse parameters.TextDocument Is Nothing Then
                Return Array.Empty(Of Protocol.Location)()
            End If

            Dim uri = parameters.TextDocument.Uri
            Dim position = parameters.Position

            _logger.LogDebug("Implementation requested at {Uri} ({Line}:{Character})", uri, position.Line, position.Character)

            Dim document = _documentManager.GetRoslynDocument(uri)
            If document Is Nothing Then
                _logger.LogTrace("No Roslyn document found for: {Uri}", uri)
                Return Array.Empty(Of Protocol.Location)()
            End If

            Try
                Dim sourceText = Await document.GetTextAsync(cancellationToken).ConfigureAwait(False)
                Dim offset = GetOffset(position, sourceText)

                Dim semanticModel = Await document.GetSemanticModelAsync(cancellationToken).ConfigureAwait(False)
                If semanticModel Is Nothing Then
                    Return Array.Empty(Of Protocol.Location)()
                End If

                Dim syntaxRoot = Await document.GetSyntaxRootAsync(cancellationToken).ConfigureAwait(False)
                If syntaxRoot Is Nothing Then
                    Return Array.Empty(Of Protocol.Location)()
                End If

                Dim token = syntaxRoot.FindToken(offset)
                If token.Span.Length = 0 OrElse token.Parent Is Nothing Then
                    Return Array.Empty(Of Protocol.Location)()
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

                If symbol Is Nothing Then
                    Return Array.Empty(Of Protocol.Location)()
                End If

                Dim implementationSymbols As IEnumerable(Of ISymbol) = Nothing

                If TypeOf symbol Is INamedTypeSymbol Then
                    Dim typeSymbol = DirectCast(symbol, INamedTypeSymbol)
                    Dim derivedSymbols As New List(Of INamedTypeSymbol)()

                    If typeSymbol.TypeKind = TypeKind.Class Then
                        Dim derived = Await SymbolFinder.FindDerivedClassesAsync(
                            typeSymbol,
                            _workspaceManager.CurrentSolution,
                            cancellationToken:=cancellationToken).ConfigureAwait(False)
                        derivedSymbols.AddRange(derived)
                    ElseIf typeSymbol.TypeKind = TypeKind.Interface Then
                        Dim derivedInterfaces = Await SymbolFinder.FindDerivedInterfacesAsync(
                            typeSymbol,
                            _workspaceManager.CurrentSolution,
                            cancellationToken:=cancellationToken).ConfigureAwait(False)
                        derivedSymbols.AddRange(derivedInterfaces)

                        Dim implementations = Await SymbolFinder.FindImplementationsAsync(
                            typeSymbol,
                            _workspaceManager.CurrentSolution,
                            cancellationToken:=cancellationToken).ConfigureAwait(False)
                        derivedSymbols.AddRange(implementations)
                    End If

                    implementationSymbols = derivedSymbols
                Else
                    implementationSymbols = Await SymbolFinder.FindImplementationsAsync(
                        symbol,
                        _workspaceManager.CurrentSolution,
                        cancellationToken:=cancellationToken).ConfigureAwait(False)
                End If

                Dim locations As New List(Of Protocol.Location)()

                For Each implSymbol In implementationSymbols
                    cancellationToken.ThrowIfCancellationRequested()

                    Dim definitionSymbol = If(implSymbol.OriginalDefinition, implSymbol)
                    For Each syntaxRef In definitionSymbol.DeclaringSyntaxReferences
                        cancellationToken.ThrowIfCancellationRequested()

                        Dim location = Await CreateLocationFromSyntaxReferenceAsync(syntaxRef, cancellationToken).ConfigureAwait(False)
                        If location IsNot Nothing Then
                            locations.Add(location)
                        End If
                    Next
                Next

                Return locations.ToArray()
            Catch ex As OperationCanceledException
                _logger.LogTrace("Implementation request cancelled for: {Uri}", uri)
                Throw
            Catch ex As Exception
                _logger.LogError(ex, "Error getting implementation for: {Uri}", uri)
                Return Array.Empty(Of Protocol.Location)()
            End Try
        End Function

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

        Private Shared Function GetIdentifierSpan(node As SyntaxNode) As TextSpan?
            For Each child In node.ChildTokens()
                If child.IsKind(Microsoft.CodeAnalysis.VisualBasic.SyntaxKind.IdentifierToken) Then
                    Return child.Span
                End If
            Next

            Dim firstToken = node.GetFirstToken()
            If firstToken.IsKind(Microsoft.CodeAnalysis.VisualBasic.SyntaxKind.IdentifierToken) Then
                Return firstToken.Span
            End If

            Return Nothing
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
