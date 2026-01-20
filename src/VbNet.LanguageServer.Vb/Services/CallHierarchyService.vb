' CallHierarchyService - Provides Call Hierarchy via LSP
' Services Layer as defined in docs/architecture.md Section 5.4

Imports Microsoft.CodeAnalysis
Imports Microsoft.CodeAnalysis.FindSymbols
Imports Microsoft.CodeAnalysis.Operations
Imports Microsoft.CodeAnalysis.Text
Imports Microsoft.CodeAnalysis.VisualBasic.Syntax
Imports Microsoft.Extensions.Logging
Imports VbNet.LanguageServer.Protocol
Imports VbNet.LanguageServer.Workspace

Namespace Services

    ''' <summary>
    ''' Provides Call Hierarchy functionality for VB.NET documents.
    ''' </summary>
    Public NotInheritable Class CallHierarchyService
        Private ReadOnly _workspaceManager As WorkspaceManager
        Private ReadOnly _documentManager As DocumentManager
        Private ReadOnly _logger As ILogger(Of CallHierarchyService)

        Public Sub New(workspaceManager As WorkspaceManager, documentManager As DocumentManager, logger As ILogger(Of CallHierarchyService))
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
        ''' Prepares call hierarchy items for the symbol at the specified position.
        ''' </summary>
        Public Async Function PrepareCallHierarchyAsync(parameters As CallHierarchyPrepareParams, cancellationToken As CancellationToken) As Task(Of CallHierarchyItem())
            If parameters Is Nothing OrElse parameters.TextDocument Is Nothing Then
                Return Array.Empty(Of CallHierarchyItem)()
            End If

            Dim uri = parameters.TextDocument.Uri
            Dim position = parameters.Position

            _logger.LogDebug("Call hierarchy prepare requested at {Uri} ({Line}:{Character})", uri, position.Line, position.Character)

            Dim document = _documentManager.GetRoslynDocument(uri)
            If document Is Nothing Then
                _logger.LogTrace("No Roslyn document found for: {Uri}", uri)
                Return Array.Empty(Of CallHierarchyItem)()
            End If

            Try
                Dim sourceText = Await document.GetTextAsync(cancellationToken).ConfigureAwait(False)
                Dim offset = GetOffset(position, sourceText)

                Dim symbol = Await FindSymbolAtPositionAsync(document, offset, cancellationToken).ConfigureAwait(False)
                If symbol Is Nothing Then
                    _logger.LogTrace("No symbol found at position for: {Uri}", uri)
                    Return Array.Empty(Of CallHierarchyItem)()
                End If

                Dim item = Await CreateCallHierarchyItemAsync(symbol, document.Project.Solution, cancellationToken).ConfigureAwait(False)
                If item Is Nothing Then
                    Return Array.Empty(Of CallHierarchyItem)()
                End If

                Return New CallHierarchyItem() {item}
            Catch ex As OperationCanceledException
                _logger.LogTrace("Call hierarchy prepare cancelled for: {Uri}", uri)
                Throw
            Catch ex As Exception
                _logger.LogError(ex, "Error preparing call hierarchy for: {Uri}", uri)
                Return Array.Empty(Of CallHierarchyItem)()
            End Try
        End Function

        ''' <summary>
        ''' Gets incoming calls for the specified call hierarchy item.
        ''' </summary>
        Public Async Function GetIncomingCallsAsync(parameters As CallHierarchyIncomingCallsParams, cancellationToken As CancellationToken) As Task(Of CallHierarchyIncomingCall())
            If parameters Is Nothing OrElse parameters.Item Is Nothing Then
                Return Array.Empty(Of CallHierarchyIncomingCall)()
            End If

            Dim item = parameters.Item
            Dim symbol = Await ResolveSymbolFromItemAsync(item, cancellationToken).ConfigureAwait(False)
            If symbol Is Nothing Then
                Return Array.Empty(Of CallHierarchyIncomingCall)()
            End If

            Try
                Dim callers = Await SymbolFinder.FindCallersAsync(symbol, _workspaceManager.CurrentSolution, cancellationToken).ConfigureAwait(False)

                Dim results As New List(Of CallHierarchyIncomingCall)()

                For Each caller In callers
                    cancellationToken.ThrowIfCancellationRequested()

                    Dim callerItem = Await CreateCallHierarchyItemAsync(caller.CallingSymbol, _workspaceManager.CurrentSolution, cancellationToken).ConfigureAwait(False)
                    If callerItem Is Nothing Then
                        Continue For
                    End If

                    Dim ranges As New List(Of Protocol.Range)()

                    For Each location In caller.Locations
                        cancellationToken.ThrowIfCancellationRequested()

                        Dim range = Await CreateRangeFromLocationAsync(location, cancellationToken).ConfigureAwait(False)
                        If range IsNot Nothing Then
                            ranges.Add(range)
                        End If
                    Next

                    results.Add(New CallHierarchyIncomingCall With {
                        .From = callerItem,
                        .FromRanges = ranges.ToArray()
                    })
                Next

                Return results.ToArray()
            Catch ex As OperationCanceledException
                _logger.LogTrace("Call hierarchy incoming cancelled for: {Uri}", item.Uri)
                Throw
            Catch ex As Exception
                _logger.LogError(ex, "Error getting call hierarchy incoming for: {Uri}", item.Uri)
                Return Array.Empty(Of CallHierarchyIncomingCall)()
            End Try
        End Function

        ''' <summary>
        ''' Gets outgoing calls for the specified call hierarchy item.
        ''' </summary>
        Public Async Function GetOutgoingCallsAsync(parameters As CallHierarchyOutgoingCallsParams, cancellationToken As CancellationToken) As Task(Of CallHierarchyOutgoingCall())
            If parameters Is Nothing OrElse parameters.Item Is Nothing Then
                Return Array.Empty(Of CallHierarchyOutgoingCall)()
            End If

            Dim item = parameters.Item
            Dim symbol = Await ResolveSymbolFromItemAsync(item, cancellationToken).ConfigureAwait(False)
            If symbol Is Nothing Then
                Return Array.Empty(Of CallHierarchyOutgoingCall)()
            End If

            Try
                Dim outgoing = Await CollectOutgoingCallsAsync(symbol, cancellationToken).ConfigureAwait(False)
                Dim results As New List(Of CallHierarchyOutgoingCall)()

                For Each entry In outgoing
                    cancellationToken.ThrowIfCancellationRequested()

                    Dim targetItem = Await CreateCallHierarchyItemAsync(entry.Key, _workspaceManager.CurrentSolution, cancellationToken).ConfigureAwait(False)
                    If targetItem Is Nothing Then
                        Continue For
                    End If

                    results.Add(New CallHierarchyOutgoingCall With {
                        .[To] = targetItem,
                        .FromRanges = entry.Value.ToArray()
                    })
                Next

                Return results.ToArray()
            Catch ex As OperationCanceledException
                _logger.LogTrace("Call hierarchy outgoing cancelled for: {Uri}", item.Uri)
                Throw
            Catch ex As Exception
                _logger.LogError(ex, "Error getting call hierarchy outgoing for: {Uri}", item.Uri)
                Return Array.Empty(Of CallHierarchyOutgoingCall)()
            End Try
        End Function

        Private Async Function CollectOutgoingCallsAsync(symbol As ISymbol, cancellationToken As CancellationToken) As Task(Of Dictionary(Of ISymbol, List(Of Protocol.Range)))
            Dim results As New Dictionary(Of ISymbol, List(Of Protocol.Range))(SymbolEqualityComparer.Default)

            For Each syntaxRef In symbol.DeclaringSyntaxReferences
                cancellationToken.ThrowIfCancellationRequested()

                Dim syntax = Await syntaxRef.GetSyntaxAsync(cancellationToken).ConfigureAwait(False)
                Dim bodySyntax As SyntaxNode = syntax

                If TypeOf syntax Is MethodStatementSyntax AndAlso syntax.Parent IsNot Nothing Then
                    bodySyntax = syntax.Parent
                End If
                Dim document = _workspaceManager.CurrentSolution.GetDocument(syntaxRef.SyntaxTree)
                If document Is Nothing Then
                    Continue For
                End If

                Dim semanticModel = Await document.GetSemanticModelAsync(cancellationToken).ConfigureAwait(False)
                If semanticModel Is Nothing Then
                    Continue For
                End If

                Dim sourceText = Await document.GetTextAsync(cancellationToken).ConfigureAwait(False)

                Dim operation = semanticModel.GetOperation(bodySyntax, cancellationToken)
                If operation IsNot Nothing Then
                    AddOutgoingCallsFromOperation(operation, sourceText, results, cancellationToken)
                Else
                    AddOutgoingCallsFromSyntax(bodySyntax, semanticModel, sourceText, results, cancellationToken)
                End If
            Next

            Return results
        End Function

        Private Sub AddOutgoingCallsFromOperation(operation As IOperation, sourceText As SourceText, results As Dictionary(Of ISymbol, List(Of Protocol.Range)), cancellationToken As CancellationToken)
            For Each callInfo In EnumerateOutgoingCalls(operation, cancellationToken)
                cancellationToken.ThrowIfCancellationRequested()

                Dim targetSymbol = callInfo.Target
                If targetSymbol Is Nothing Then
                    Continue For
                End If

                Dim range = GetRange(callInfo.Span, sourceText)

                Dim list As List(Of Protocol.Range) = Nothing
                If Not results.TryGetValue(targetSymbol, list) Then
                    list = New List(Of Protocol.Range)()
                    results.Add(targetSymbol, list)
                End If

                list.Add(range)
            Next
        End Sub

        Private Sub AddOutgoingCallsFromSyntax(syntax As SyntaxNode, semanticModel As SemanticModel, sourceText As SourceText, results As Dictionary(Of ISymbol, List(Of Protocol.Range)), cancellationToken As CancellationToken)
            For Each node In syntax.DescendantNodes()
                cancellationToken.ThrowIfCancellationRequested()

                If TypeOf node Is InvocationExpressionSyntax OrElse TypeOf node Is ObjectCreationExpressionSyntax Then
                    Dim operation = semanticModel.GetOperation(node, cancellationToken)
                    If operation IsNot Nothing Then
                        AddOutgoingCallsFromOperation(operation, sourceText, results, cancellationToken)
                    End If
                End If
            Next
        End Sub

        Private Iterator Function EnumerateOutgoingCalls(operation As IOperation, cancellationToken As CancellationToken) As IEnumerable(Of OutgoingCallInfo)
            If operation Is Nothing Then
                Return
            End If

            Dim stack As New Stack(Of IOperation)()
            stack.Push(operation)

            While stack.Count > 0
                cancellationToken.ThrowIfCancellationRequested()

                Dim current = stack.Pop()

                If TypeOf current Is IInvocationOperation Then
                    Dim invocation = DirectCast(current, IInvocationOperation)
                    Dim targetSymbol = invocation.TargetMethod
                    If targetSymbol IsNot Nothing Then
                        Yield New OutgoingCallInfo(DirectCast(targetSymbol, ISymbol), invocation.Syntax.Span)
                    End If
                ElseIf TypeOf current Is IObjectCreationOperation Then
                    Dim creation = DirectCast(current, IObjectCreationOperation)
                    Dim targetSymbol = creation.Constructor
                    If targetSymbol IsNot Nothing Then
                        Yield New OutgoingCallInfo(DirectCast(targetSymbol, ISymbol), creation.Syntax.Span)
                    End If
                End If

                For Each child In current.ChildOperations
                    stack.Push(child)
                Next
            End While
        End Function

        Private Structure OutgoingCallInfo
            Public Sub New(target As ISymbol, span As TextSpan)
                Me.Target = target
                Me.Span = span
            End Sub

            Public ReadOnly Target As ISymbol
            Public ReadOnly Span As TextSpan
        End Structure

        Private Async Function ResolveSymbolFromItemAsync(item As CallHierarchyItem, cancellationToken As CancellationToken) As Task(Of ISymbol)
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

        Private Async Function CreateCallHierarchyItemAsync(symbol As ISymbol, solution As Solution, cancellationToken As CancellationToken) As Task(Of CallHierarchyItem)
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
            Dim containingType = definitionSymbol.ContainingType
            If containingType IsNot Nothing Then
                detail = containingType.ToDisplayString()
            ElseIf definitionSymbol.ContainingNamespace IsNot Nothing Then
                detail = definitionSymbol.ContainingNamespace.ToDisplayString()
            End If

            Return New CallHierarchyItem With {
                .Name = definitionSymbol.Name,
                .Kind = GetSymbolKind(definitionSymbol),
                .Detail = detail,
                .Uri = New Uri(syntaxTree.FilePath).ToString(),
                .Range = range,
                .SelectionRange = selectionRange
            }
        End Function

        Private Async Function CreateRangeFromLocationAsync(location As Microsoft.CodeAnalysis.Location, cancellationToken As CancellationToken) As Task(Of Protocol.Range)
            If location Is Nothing OrElse Not location.IsInSource Then
                Return Nothing
            End If

            Dim syntaxTree = location.SourceTree
            If syntaxTree Is Nothing Then
                Return Nothing
            End If

            Dim sourceText = Await syntaxTree.GetTextAsync(cancellationToken).ConfigureAwait(False)
            Dim span = location.SourceSpan

            Return GetRange(span, sourceText)
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

        Private Shared Function GetSymbolKind(symbol As ISymbol) As Protocol.SymbolKind
            Select Case symbol.Kind
                Case Microsoft.CodeAnalysis.SymbolKind.NamedType
                    Dim namedType = DirectCast(symbol, INamedTypeSymbol)
                    Select Case namedType.TypeKind
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
                Case Microsoft.CodeAnalysis.SymbolKind.Method
                    Dim methodSymbol = DirectCast(symbol, IMethodSymbol)
                    Select Case methodSymbol.MethodKind
                        Case MethodKind.Constructor, MethodKind.SharedConstructor
                            Return Protocol.SymbolKind.Constructor
                        Case MethodKind.PropertyGet, MethodKind.PropertySet
                            Return Protocol.SymbolKind.Property
                        Case MethodKind.EventAdd, MethodKind.EventRemove, MethodKind.EventRaise
                            Return Protocol.SymbolKind.Event
                        Case Else
                            Return Protocol.SymbolKind.Method
                    End Select
                Case Microsoft.CodeAnalysis.SymbolKind.Property
                    Return Protocol.SymbolKind.Property
                Case Microsoft.CodeAnalysis.SymbolKind.Field
                    Dim fieldSymbol = DirectCast(symbol, IFieldSymbol)
                    Return If(fieldSymbol.IsConst, Protocol.SymbolKind.Constant, Protocol.SymbolKind.Field)
                Case Microsoft.CodeAnalysis.SymbolKind.Event
                    Return Protocol.SymbolKind.Event
                Case Microsoft.CodeAnalysis.SymbolKind.Namespace
                    Return Protocol.SymbolKind.Namespace
                Case Microsoft.CodeAnalysis.SymbolKind.Local
                    Return Protocol.SymbolKind.Variable
                Case Microsoft.CodeAnalysis.SymbolKind.Parameter
                    Return Protocol.SymbolKind.Variable
                Case Microsoft.CodeAnalysis.SymbolKind.TypeParameter
                    Return Protocol.SymbolKind.TypeParameter
                Case Else
                    Return Protocol.SymbolKind.Variable
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
