' TypeHierarchyService - Provides Type Hierarchy via LSP
' Services Layer as defined in docs/architecture.md Section 5.4

Imports Microsoft.CodeAnalysis
Imports Microsoft.CodeAnalysis.FindSymbols
Imports Microsoft.CodeAnalysis.Text
Imports Microsoft.Extensions.Logging
Imports VbNet.LanguageServer.Protocol
Imports VbNet.LanguageServer.Workspace

Namespace Services

    ''' <summary>
    ''' Provides Type Hierarchy functionality for VB.NET documents.
    ''' </summary>
    Public NotInheritable Class TypeHierarchyService
        Private ReadOnly _workspaceManager As WorkspaceManager
        Private ReadOnly _documentManager As DocumentManager
        Private ReadOnly _logger As ILogger(Of TypeHierarchyService)

        Public Sub New(workspaceManager As WorkspaceManager, documentManager As DocumentManager, logger As ILogger(Of TypeHierarchyService))
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
        ''' Prepares type hierarchy items for the type at the specified position.
        ''' </summary>
        Public Async Function PrepareTypeHierarchyAsync(parameters As TypeHierarchyPrepareParams, cancellationToken As CancellationToken) As Task(Of TypeHierarchyItem())
            If parameters Is Nothing OrElse parameters.TextDocument Is Nothing Then
                Return Array.Empty(Of TypeHierarchyItem)()
            End If

            Dim uri = parameters.TextDocument.Uri
            Dim position = parameters.Position

            _logger.LogDebug("Type hierarchy prepare requested at {Uri} ({Line}:{Character})", uri, position.Line, position.Character)

            Dim document = _documentManager.GetRoslynDocument(uri)
            If document Is Nothing Then
                _logger.LogTrace("No Roslyn document found for: {Uri}", uri)
                Return Array.Empty(Of TypeHierarchyItem)()
            End If

            Try
                Dim sourceText = Await document.GetTextAsync(cancellationToken).ConfigureAwait(False)
                Dim offset = GetOffset(position, sourceText)

                Dim symbol = Await FindSymbolAtPositionAsync(document, offset, cancellationToken).ConfigureAwait(False)
                Dim namedType = TryCast(symbol, INamedTypeSymbol)
                If namedType Is Nothing Then
                    _logger.LogTrace("No type symbol found at position for: {Uri}", uri)
                    Return Array.Empty(Of TypeHierarchyItem)()
                End If

                Dim item = Await CreateTypeHierarchyItemAsync(namedType, document.Project.Solution, cancellationToken).ConfigureAwait(False)
                If item Is Nothing Then
                    Return Array.Empty(Of TypeHierarchyItem)()
                End If

                Return New TypeHierarchyItem() {item}
            Catch ex As OperationCanceledException
                _logger.LogTrace("Type hierarchy prepare cancelled for: {Uri}", uri)
                Throw
            Catch ex As Exception
                _logger.LogError(ex, "Error preparing type hierarchy for: {Uri}", uri)
                Return Array.Empty(Of TypeHierarchyItem)()
            End Try
        End Function

        ''' <summary>
        ''' Gets supertypes for the specified type hierarchy item.
        ''' </summary>
        Public Async Function GetSupertypesAsync(parameters As TypeHierarchySupertypesParams, cancellationToken As CancellationToken) As Task(Of TypeHierarchyItem())
            If parameters Is Nothing OrElse parameters.Item Is Nothing Then
                Return Array.Empty(Of TypeHierarchyItem)()
            End If

            Dim item = parameters.Item
            Dim symbol = Await ResolveSymbolFromItemAsync(item, cancellationToken).ConfigureAwait(False)
            Dim namedType = TryCast(symbol, INamedTypeSymbol)
            If namedType Is Nothing Then
                Return Array.Empty(Of TypeHierarchyItem)()
            End If

            Try
                Dim results As New List(Of TypeHierarchyItem)()

                If namedType.BaseType IsNot Nothing Then
                    Dim baseItem = Await CreateTypeHierarchyItemAsync(namedType.BaseType, _workspaceManager.CurrentSolution, cancellationToken).ConfigureAwait(False)
                    If baseItem IsNot Nothing Then
                        results.Add(baseItem)
                    End If
                End If

                For Each iface In namedType.Interfaces
                    cancellationToken.ThrowIfCancellationRequested()

                    Dim ifaceItem = Await CreateTypeHierarchyItemAsync(iface, _workspaceManager.CurrentSolution, cancellationToken).ConfigureAwait(False)
                    If ifaceItem IsNot Nothing Then
                        results.Add(ifaceItem)
                    End If
                Next

                Return results _
                    .GroupBy(Function(i) i.Uri & "|" & i.Name) _
                    .Select(Function(g) g.First()) _
                    .ToArray()
            Catch ex As OperationCanceledException
                _logger.LogTrace("Type hierarchy supertypes cancelled for: {Uri}", item.Uri)
                Throw
            Catch ex As Exception
                _logger.LogError(ex, "Error getting type hierarchy supertypes for: {Uri}", item.Uri)
                Return Array.Empty(Of TypeHierarchyItem)()
            End Try
        End Function

        ''' <summary>
        ''' Gets subtypes for the specified type hierarchy item.
        ''' </summary>
        Public Async Function GetSubtypesAsync(parameters As TypeHierarchySubtypesParams, cancellationToken As CancellationToken) As Task(Of TypeHierarchyItem())
            If parameters Is Nothing OrElse parameters.Item Is Nothing Then
                Return Array.Empty(Of TypeHierarchyItem)()
            End If

            Dim item = parameters.Item
            Dim symbol = Await ResolveSymbolFromItemAsync(item, cancellationToken).ConfigureAwait(False)
            Dim namedType = TryCast(symbol, INamedTypeSymbol)
            If namedType Is Nothing Then
                Return Array.Empty(Of TypeHierarchyItem)()
            End If

            Try
                Dim derivedSymbols As New List(Of INamedTypeSymbol)()

                If namedType.TypeKind = TypeKind.Class Then
                    Dim derived = Await SymbolFinder.FindDerivedClassesAsync(
                        namedType,
                        _workspaceManager.CurrentSolution,
                        cancellationToken:=cancellationToken).ConfigureAwait(False)
                    derivedSymbols.AddRange(derived)
                ElseIf namedType.TypeKind = TypeKind.Interface Then
                    Dim derivedInterfaces = Await SymbolFinder.FindDerivedInterfacesAsync(
                        namedType,
                        _workspaceManager.CurrentSolution,
                        cancellationToken:=cancellationToken).ConfigureAwait(False)
                    derivedSymbols.AddRange(derivedInterfaces)

                    Dim implementations = Await SymbolFinder.FindImplementationsAsync(
                        namedType,
                        _workspaceManager.CurrentSolution,
                        cancellationToken:=cancellationToken).ConfigureAwait(False)
                    derivedSymbols.AddRange(implementations)
                End If

                Dim results As New List(Of TypeHierarchyItem)()
                For Each derivedSymbol In derivedSymbols.Distinct(SymbolEqualityComparer.Default)
                    cancellationToken.ThrowIfCancellationRequested()

                    Dim derivedItem = Await CreateTypeHierarchyItemAsync(derivedSymbol, _workspaceManager.CurrentSolution, cancellationToken).ConfigureAwait(False)
                    If derivedItem IsNot Nothing Then
                        results.Add(derivedItem)
                    End If
                Next

                Return results.ToArray()
            Catch ex As OperationCanceledException
                _logger.LogTrace("Type hierarchy subtypes cancelled for: {Uri}", item.Uri)
                Throw
            Catch ex As Exception
                _logger.LogError(ex, "Error getting type hierarchy subtypes for: {Uri}", item.Uri)
                Return Array.Empty(Of TypeHierarchyItem)()
            End Try
        End Function

        Private Async Function ResolveSymbolFromItemAsync(item As TypeHierarchyItem, cancellationToken As CancellationToken) As Task(Of ISymbol)
            If item Is Nothing OrElse String.IsNullOrEmpty(item.Uri) Then
                Return Nothing
            End If

            Dim document = _documentManager.GetRoslynDocument(item.Uri)
            If document Is Nothing Then
                Return Nothing
            End If

            Dim sourceText = Await document.GetTextAsync(cancellationToken).ConfigureAwait(False)
            Dim position = If(item.SelectionRange?.Start, item.Range?.Start)
            If position Is Nothing Then
                Return Nothing
            End If

            Dim offset = GetOffset(position, sourceText)
            Return Await FindSymbolAtPositionAsync(document, offset, cancellationToken).ConfigureAwait(False)
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

        Private Async Function CreateTypeHierarchyItemAsync(symbol As INamedTypeSymbol, solution As Solution, cancellationToken As CancellationToken) As Task(Of TypeHierarchyItem)
            If symbol Is Nothing Then
                Return Nothing
            End If

            Dim definitionSymbol = If(symbol.OriginalDefinition, symbol)
            Dim syntaxRef = definitionSymbol.DeclaringSyntaxReferences.FirstOrDefault()
            If syntaxRef Is Nothing Then
                Return Nothing
            End If

            Dim syntaxTree = syntaxRef.SyntaxTree
            If syntaxTree Is Nothing OrElse String.IsNullOrEmpty(syntaxTree.FilePath) Then
                Return Nothing
            End If

            Dim sourceText = Await syntaxTree.GetTextAsync(cancellationToken).ConfigureAwait(False)
            Dim syntax = Await syntaxRef.GetSyntaxAsync(cancellationToken).ConfigureAwait(False)
            Dim span = syntax.Span
            Dim identifierSpan = GetIdentifierSpan(syntax)
            If identifierSpan Is Nothing Then
                identifierSpan = span
            End If

            Dim range = GetRange(span, sourceText)
            Dim selectionRange = GetRange(identifierSpan.Value, sourceText)

            Dim detail As String = Nothing
            If definitionSymbol.ContainingNamespace IsNot Nothing Then
                detail = definitionSymbol.ContainingNamespace.ToDisplayString()
            End If

            Return New TypeHierarchyItem With {
                .Name = definitionSymbol.Name,
                .Kind = GetSymbolKind(definitionSymbol),
                .Detail = detail,
                .Uri = New Uri(syntaxTree.FilePath).ToString(),
                .Range = range,
                .SelectionRange = selectionRange
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

        Private Shared Function GetSymbolKind(symbol As INamedTypeSymbol) As Protocol.SymbolKind
            Select Case symbol.TypeKind
                Case TypeKind.Class
                    Return Protocol.SymbolKind.Class
                Case TypeKind.Interface
                    Return Protocol.SymbolKind.Interface
                Case TypeKind.Struct
                    Return Protocol.SymbolKind.Struct
                Case TypeKind.Enum
                    Return Protocol.SymbolKind.Enum
                Case TypeKind.Module
                    Return Protocol.SymbolKind.Module
                Case Else
                    Return Protocol.SymbolKind.Class
            End Select
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
