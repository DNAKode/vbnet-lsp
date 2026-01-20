' RenameService - Provides symbol renaming via LSP
' Services Layer as defined in docs/architecture.md Section 5.4

Imports Microsoft.CodeAnalysis
Imports Microsoft.CodeAnalysis.Rename
Imports Microsoft.CodeAnalysis.Text
Imports Microsoft.Extensions.Logging
Imports VbNet.LanguageServer.Protocol
Imports VbNet.LanguageServer.Workspace

Namespace Services

    ''' <summary>
    ''' Provides symbol renaming functionality for VB.NET documents.
    ''' Uses Roslyn's Renamer for semantic-aware renaming across the solution.
    ''' </summary>
    Public NotInheritable Class RenameService
        Private ReadOnly _workspaceManager As WorkspaceManager
        Private ReadOnly _documentManager As DocumentManager
        Private ReadOnly _logger As ILogger(Of RenameService)

        Public Sub New(workspaceManager As WorkspaceManager, documentManager As DocumentManager, logger As ILogger(Of RenameService))
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
        ''' Prepares for a rename operation by validating the position and returning
        ''' the range and placeholder text for the symbol.
        ''' </summary>
        Public Async Function PrepareRenameAsync(parameters As PrepareRenameParams, cancellationToken As CancellationToken) As Task(Of PrepareRenameResult)
            If parameters Is Nothing OrElse parameters.TextDocument Is Nothing Then
                Return Nothing
            End If

            Dim uri = parameters.TextDocument.Uri
            Dim position = parameters.Position

            _logger.LogDebug("PrepareRename requested at {Uri} ({Line}:{Character})", uri, position.Line, position.Character)

            Dim document = _documentManager.GetRoslynDocument(uri)
            If document Is Nothing Then
                _logger.LogTrace("No Roslyn document found for: {Uri}", uri)
                Return Nothing
            End If

            Try
                Dim sourceText = Await document.GetTextAsync(cancellationToken).ConfigureAwait(False)
                Dim offset = GetOffset(position, sourceText)

                cancellationToken.ThrowIfCancellationRequested()

                Dim symbol = Await FindSymbolAtPositionAsync(document, offset, cancellationToken).ConfigureAwait(False)
                If symbol Is Nothing Then
                    _logger.LogTrace("No symbol found at position for: {Uri}", uri)
                    Return Nothing
                End If

                If Not CanRenameSymbol(symbol) Then
                    _logger.LogTrace("Symbol cannot be renamed: {Symbol}", symbol.Name)
                    Return Nothing
                End If

                Dim syntaxRoot = Await document.GetSyntaxRootAsync(cancellationToken).ConfigureAwait(False)
                If syntaxRoot Is Nothing Then
                    Return Nothing
                End If

                Dim token = syntaxRoot.FindToken(offset)
                If token.Span.Length = 0 Then
                    Return Nothing
                End If

                Dim identifierSpan = GetIdentifierSpan(token.Parent)
                If identifierSpan Is Nothing Then
                    identifierSpan = token.Span
                End If

                Dim range = GetRange(identifierSpan.Value, sourceText)

                _logger.LogDebug("PrepareRename succeeded for symbol: {Symbol}", symbol.Name)

                Return New PrepareRenameResult With {
                    .Range = range,
                    .Placeholder = symbol.Name
                }
            Catch ex As OperationCanceledException
                _logger.LogTrace("PrepareRename request cancelled for: {Uri}", uri)
                Throw
            Catch ex As Exception
                _logger.LogError(ex, "Error preparing rename for: {Uri}", uri)
                Return Nothing
            End Try
        End Function

        ''' <summary>
        ''' Performs a rename operation across the solution.
        ''' </summary>
        Public Async Function RenameAsync(parameters As RenameParams, cancellationToken As CancellationToken) As Task(Of WorkspaceEdit)
            If parameters Is Nothing OrElse parameters.TextDocument Is Nothing OrElse String.IsNullOrEmpty(parameters.NewName) Then
                Return Nothing
            End If

            Dim uri = parameters.TextDocument.Uri
            Dim position = parameters.Position
            Dim newName = parameters.NewName

            _logger.LogDebug("Rename requested at {Uri} ({Line}:{Character}) to '{NewName}'", uri, position.Line, position.Character, newName)

            Dim document = _documentManager.GetRoslynDocument(uri)
            If document Is Nothing Then
                _logger.LogTrace("No Roslyn document found for: {Uri}", uri)
                Return Nothing
            End If

            Try
                Dim sourceText = Await document.GetTextAsync(cancellationToken).ConfigureAwait(False)
                Dim offset = GetOffset(position, sourceText)

                cancellationToken.ThrowIfCancellationRequested()

                Dim symbol = Await FindSymbolAtPositionAsync(document, offset, cancellationToken).ConfigureAwait(False)
                If symbol Is Nothing Then
                    _logger.LogTrace("No symbol found at position for: {Uri}", uri)
                    Return Nothing
                End If

                If Not CanRenameSymbol(symbol) Then
                    _logger.LogTrace("Symbol cannot be renamed: {Symbol}", symbol.Name)
                    Return Nothing
                End If

                cancellationToken.ThrowIfCancellationRequested()

                Dim solution = document.Project.Solution
                Dim newSolution = Await Renamer.RenameSymbolAsync(solution, symbol, New SymbolRenameOptions(), newName, cancellationToken).ConfigureAwait(False)

                Dim changes = newSolution.GetChanges(solution)
                Dim workspaceEdit = Await BuildWorkspaceEditAsync(changes, solution, newSolution, cancellationToken).ConfigureAwait(False)

                _logger.LogDebug("Rename completed for symbol: {OldName} -> {NewName}", symbol.Name, newName)

                Return workspaceEdit
            Catch ex As OperationCanceledException
                _logger.LogTrace("Rename request cancelled for: {Uri}", uri)
                Throw
            Catch ex As Exception
                _logger.LogError(ex, "Error performing rename for: {Uri}", uri)
                Return Nothing
            End Try
        End Function

        ''' <summary>
        ''' Checks if a symbol can be renamed.
        ''' </summary>
        Private Shared Function CanRenameSymbol(symbol As ISymbol) As Boolean
            If symbol.IsImplicitlyDeclared Then
                Return False
            End If

            If symbol.Locations.All(Function(l) l.IsInMetadata) Then
                Return False
            End If

            If TypeOf symbol Is INamespaceSymbol Then
                Return False
            End If

            Return True
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

            Return symbol
        End Function

        ''' <summary>
        ''' Builds a WorkspaceEdit from solution changes.
        ''' </summary>
        Private Async Function BuildWorkspaceEditAsync(changes As SolutionChanges, oldSolution As Solution, newSolution As Solution, cancellationToken As CancellationToken) As Task(Of WorkspaceEdit)
            Dim documentChanges As New Dictionary(Of String, List(Of TextEdit))()

            For Each projectChanges In changes.GetProjectChanges()
                For Each documentId In projectChanges.GetChangedDocuments()
                    cancellationToken.ThrowIfCancellationRequested()

                    Dim oldDocument = oldSolution.GetDocument(documentId)
                    Dim newDocument = newSolution.GetDocument(documentId)

                    If oldDocument Is Nothing OrElse newDocument Is Nothing Then
                        Continue For
                    End If

                    Dim oldText = Await oldDocument.GetTextAsync(cancellationToken).ConfigureAwait(False)
                    Dim newText = Await newDocument.GetTextAsync(cancellationToken).ConfigureAwait(False)

                    Dim textChanges = newText.GetTextChanges(oldText)
                    If textChanges.Count = 0 Then
                        Continue For
                    End If

                    Dim filePath = oldDocument.FilePath
                    If String.IsNullOrEmpty(filePath) Then
                        Continue For
                    End If

                    Dim uri = New Uri(filePath).ToString()

                    Dim edits As List(Of TextEdit) = Nothing
                    If Not documentChanges.TryGetValue(uri, edits) Then
                        edits = New List(Of TextEdit)()
                        documentChanges(uri) = edits
                    End If

                    For Each change In textChanges
                        Dim range = GetRange(change.Span, oldText)
                        edits.Add(New TextEdit With {
                            .Range = range,
                            .NewText = If(change.NewText, String.Empty)
                        })
                    Next
                Next
            Next

            Return New WorkspaceEdit With {
                .Changes = documentChanges.ToDictionary(Function(kvp) kvp.Key, Function(kvp) kvp.Value.ToArray())
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
    End Class

    ''' <summary>
    ''' Result of prepareRename request.
    ''' </summary>
    Public Class PrepareRenameResult
        Public Property Range As Protocol.Range = New Protocol.Range()
        Public Property Placeholder As String = String.Empty
    End Class

End Namespace
