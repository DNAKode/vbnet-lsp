' SymbolsService - Provides Document and Workspace symbols via LSP
' Services Layer as defined in docs/architecture.md Section 5.4

Imports Microsoft.CodeAnalysis
Imports Microsoft.CodeAnalysis.FindSymbols
Imports Microsoft.CodeAnalysis.Text
Imports Microsoft.CodeAnalysis.VisualBasic
Imports Microsoft.CodeAnalysis.VisualBasic.Syntax
Imports Microsoft.Extensions.Logging
Imports VbNet.LanguageServer.Protocol
Imports VbNet.LanguageServer.Workspace

Namespace Services

    ''' <summary>
    ''' Provides document and workspace symbol navigation for VB.NET.
    ''' Uses Roslyn to extract symbol hierarchies and search across solutions.
    ''' </summary>
    Public NotInheritable Class SymbolsService
        Private ReadOnly _workspaceManager As WorkspaceManager
        Private ReadOnly _documentManager As DocumentManager
        Private ReadOnly _logger As ILogger(Of SymbolsService)

        Public Sub New(workspaceManager As WorkspaceManager, documentManager As DocumentManager, logger As ILogger(Of SymbolsService))
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
        ''' Gets the document symbols (outline) for a document.
        ''' Returns a hierarchical structure of symbols in the document.
        ''' </summary>
        Public Async Function GetDocumentSymbolsAsync(parameters As DocumentSymbolParams, cancellationToken As CancellationToken) As Task(Of DocumentSymbol())
            If parameters Is Nothing OrElse parameters.TextDocument Is Nothing Then
                Return Array.Empty(Of DocumentSymbol)()
            End If

            Dim uri = parameters.TextDocument.Uri

            _logger.LogDebug("Document symbols requested for: {Uri}", uri)

            Dim document = _documentManager.GetRoslynDocument(uri)
            If document Is Nothing Then
                _logger.LogTrace("No Roslyn document found for: {Uri}", uri)
                Return Await GetDocumentSymbolsFromOpenDocumentAsync(uri, cancellationToken).ConfigureAwait(False)
            End If

            Try
                Dim sourceText = Await document.GetTextAsync(cancellationToken).ConfigureAwait(False)
                Dim syntaxRoot = Await document.GetSyntaxRootAsync(cancellationToken).ConfigureAwait(False)
                Dim semanticModel = Await document.GetSemanticModelAsync(cancellationToken).ConfigureAwait(False)

                If syntaxRoot Is Nothing OrElse semanticModel Is Nothing Then
                    _logger.LogTrace("Roslyn model not ready for: {Uri}, using syntax-only symbols", uri)
                    Return Await GetDocumentSymbolsFromOpenDocumentAsync(uri, cancellationToken).ConfigureAwait(False)
                End If

                cancellationToken.ThrowIfCancellationRequested()

                Dim symbols As New List(Of DocumentSymbol)()

                Dim typeDeclarations = syntaxRoot.DescendantNodes().Where(Function(n) TypeOf n Is TypeBlockSyntax OrElse TypeOf n Is ModuleBlockSyntax OrElse TypeOf n Is EnumBlockSyntax).ToList()

                For Each node In typeDeclarations
                    cancellationToken.ThrowIfCancellationRequested()

                    Dim declaredSymbol = semanticModel.GetDeclaredSymbol(node, cancellationToken)
                    Dim typeSymbol = TryCast(declaredSymbol, INamedTypeSymbol)
                    If typeSymbol Is Nothing Then
                        Continue For
                    End If

                    If declaredSymbol.ContainingType IsNot Nothing Then
                        Continue For
                    End If

                    Dim docSymbol = Await CreateDocumentSymbolAsync(typeSymbol, node, sourceText, semanticModel, cancellationToken).ConfigureAwait(False)
                    If docSymbol IsNot Nothing Then
                        symbols.Add(docSymbol)
                    End If
                Next

                _logger.LogDebug("Found {Count} top-level symbols in: {Uri}", symbols.Count, uri)

                Return symbols.ToArray()
            Catch ex As OperationCanceledException
                _logger.LogTrace("Document symbols request cancelled for: {Uri}", uri)
                Throw
            Catch ex As Exception
                _logger.LogError(ex, "Error getting document symbols for: {Uri}", uri)
                Return Array.Empty(Of DocumentSymbol)()
            End Try
        End Function

        Private Async Function GetDocumentSymbolsFromOpenDocumentAsync(uri As String, cancellationToken As CancellationToken) As Task(Of DocumentSymbol())
            Dim openDoc = _documentManager.GetOpenDocument(uri)
            If openDoc Is Nothing Then
                Return Array.Empty(Of DocumentSymbol)()
            End If

            Dim syntaxTree = VisualBasicSyntaxTree.ParseText(openDoc.Text)
            Dim syntaxRoot = Await syntaxTree.GetRootAsync(cancellationToken).ConfigureAwait(False)
            Dim sourceText = Await syntaxTree.GetTextAsync(cancellationToken).ConfigureAwait(False)

            Dim symbols As New List(Of DocumentSymbol)()
            For Each node In GetTopLevelTypeBlocks(syntaxRoot)
                Dim name = GetTypeName(node)
                If String.IsNullOrEmpty(name) Then
                    Continue For
                End If

                Dim kind = GetTypeSymbolKind(node)
                If Not kind.HasValue Then
                    Continue For
                End If

                Dim children As New List(Of DocumentSymbol)()
                For Each member In GetTypeMembers(node)
                    children.Add(New DocumentSymbol With {
                        .Name = member.Name,
                        .Kind = member.Kind,
                        .Range = GetRange(member.Span, sourceText),
                        .SelectionRange = GetRange(member.Span, sourceText),
                        .Children = Nothing
                    })
                Next

                symbols.Add(New DocumentSymbol With {
                    .Name = name,
                    .Kind = kind.Value,
                    .Range = GetRange(node.Span, sourceText),
                    .SelectionRange = GetSelectionRangeFromSyntax(node, sourceText),
                    .Children = If(children.Count > 0, children.ToArray(), Nothing)
                })
            Next

            Return symbols.ToArray()
        End Function

        Private Shared Iterator Function GetTopLevelTypeBlocks(root As SyntaxNode) As IEnumerable(Of SyntaxNode)
            Dim typeBlocks = root.DescendantNodes().Where(Function(node)
                                                             Return TypeOf node Is ClassBlockSyntax OrElse
                                                                    TypeOf node Is StructureBlockSyntax OrElse
                                                                    TypeOf node Is InterfaceBlockSyntax OrElse
                                                                    TypeOf node Is ModuleBlockSyntax OrElse
                                                                    TypeOf node Is EnumBlockSyntax
                                                         End Function)

            For Each node In typeBlocks
                If node.Ancestors().Any(Function(ancestor)
                                            Return TypeOf ancestor Is TypeBlockSyntax OrElse
                                                   TypeOf ancestor Is ModuleBlockSyntax OrElse
                                                   TypeOf ancestor Is EnumBlockSyntax
                                        End Function) Then
                    Continue For
                End If

                Yield node
            Next
        End Function

        Private Shared Function GetTypeName(node As SyntaxNode) As String
            If TypeOf node Is ClassBlockSyntax Then
                Return DirectCast(node, ClassBlockSyntax).ClassStatement.Identifier.Text
            End If
            If TypeOf node Is StructureBlockSyntax Then
                Return DirectCast(node, StructureBlockSyntax).StructureStatement.Identifier.Text
            End If
            If TypeOf node Is InterfaceBlockSyntax Then
                Return DirectCast(node, InterfaceBlockSyntax).InterfaceStatement.Identifier.Text
            End If
            If TypeOf node Is ModuleBlockSyntax Then
                Return DirectCast(node, ModuleBlockSyntax).ModuleStatement.Identifier.Text
            End If
            If TypeOf node Is EnumBlockSyntax Then
                Return DirectCast(node, EnumBlockSyntax).EnumStatement.Identifier.Text
            End If

            Return Nothing
        End Function

        Private Shared Function GetTypeSymbolKind(node As SyntaxNode) As Protocol.SymbolKind?
            If TypeOf node Is ClassBlockSyntax Then
                Return Protocol.SymbolKind.Class
            End If
            If TypeOf node Is StructureBlockSyntax Then
                Return Protocol.SymbolKind.Struct
            End If
            If TypeOf node Is InterfaceBlockSyntax Then
                Return Protocol.SymbolKind.Interface
            End If
            If TypeOf node Is ModuleBlockSyntax Then
                Return Protocol.SymbolKind.Module
            End If
            If TypeOf node Is EnumBlockSyntax Then
                Return Protocol.SymbolKind.Enum
            End If

            Return Nothing
        End Function

        Private Shared Function GetSelectionRangeFromSyntax(node As SyntaxNode, sourceText As SourceText) As Protocol.Range
            Dim token As SyntaxToken
            Dim hasToken = True

            If TypeOf node Is ClassBlockSyntax Then
                token = DirectCast(node, ClassBlockSyntax).ClassStatement.Identifier
            ElseIf TypeOf node Is StructureBlockSyntax Then
                token = DirectCast(node, StructureBlockSyntax).StructureStatement.Identifier
            ElseIf TypeOf node Is InterfaceBlockSyntax Then
                token = DirectCast(node, InterfaceBlockSyntax).InterfaceStatement.Identifier
            ElseIf TypeOf node Is ModuleBlockSyntax Then
                token = DirectCast(node, ModuleBlockSyntax).ModuleStatement.Identifier
            ElseIf TypeOf node Is EnumBlockSyntax Then
                token = DirectCast(node, EnumBlockSyntax).EnumStatement.Identifier
            Else
                hasToken = False
            End If

            If Not hasToken Then
                Return GetRange(node.Span, sourceText)
            End If

            Return GetRange(token.Span, sourceText)
        End Function

        ''' <summary>
        ''' Searches for symbols across the workspace matching the query.
        ''' </summary>
        Public Async Function GetWorkspaceSymbolsAsync(parameters As WorkspaceSymbolParams, cancellationToken As CancellationToken) As Task(Of SymbolInformation())
            If parameters Is Nothing Then
                Return Array.Empty(Of SymbolInformation)()
            End If

            Dim query = If(parameters.Query, String.Empty)

            _logger.LogDebug("Workspace symbols requested with query: '{Query}'", query)

            Dim solution = _workspaceManager.CurrentSolution
            If solution Is Nothing Then
                _logger.LogTrace("No solution available; waiting for initial load")
                Await _workspaceManager.WaitForInitialLoadAsync(TimeSpan.FromSeconds(5), cancellationToken).ConfigureAwait(False)
                solution = _workspaceManager.CurrentSolution
                If solution Is Nothing Then
                    _logger.LogTrace("No solution available after initial load wait; using open documents only")
                    Return Await GetWorkspaceSymbolsFromOpenDocumentsAsync(query, cancellationToken).ConfigureAwait(False)
                End If
            End If

            Try
                Dim results As New List(Of SymbolInformation)()

                If String.IsNullOrWhiteSpace(query) Then
                    Return Array.Empty(Of SymbolInformation)()
                End If

                cancellationToken.ThrowIfCancellationRequested()

                Dim symbols = Await SymbolFinder.FindSourceDeclarationsWithPatternAsync(solution, query, SymbolFilter.TypeAndMember, cancellationToken).ConfigureAwait(False)

                For Each symbol In symbols
                    cancellationToken.ThrowIfCancellationRequested()

                    If symbol.IsImplicitlyDeclared Then
                        Continue For
                    End If

                    Dim location = symbol.Locations.FirstOrDefault(Function(l) l.IsInSource)
                    If location Is Nothing Then
                        Continue For
                    End If

                    Dim syntaxTree = location.SourceTree
                    If syntaxTree Is Nothing OrElse String.IsNullOrEmpty(syntaxTree.FilePath) Then
                        Continue For
                    End If

                    If Not syntaxTree.FilePath.EndsWith(".vb", StringComparison.OrdinalIgnoreCase) Then
                        Continue For
                    End If

                    Dim sourceText = Await syntaxTree.GetTextAsync(cancellationToken).ConfigureAwait(False)
                    Dim range = GetRange(location.SourceSpan, sourceText)
                    Dim uri = New Uri(syntaxTree.FilePath).ToString()

                    results.Add(New SymbolInformation With {
                        .Name = symbol.Name,
                        .Kind = GetSymbolKind(symbol),
                        .Location = New Protocol.Location With {
                            .Uri = uri,
                            .Range = range
                        },
                        .ContainerName = If(symbol.ContainingType?.Name, symbol.ContainingNamespace?.ToDisplayString())
                    })

                    If results.Count >= 100 Then
                        Exit For
                    End If
                Next

                _logger.LogDebug("Found {Count} workspace symbols for query: '{Query}'", results.Count, query)

                Return results.ToArray()
            Catch ex As OperationCanceledException
                _logger.LogTrace("Workspace symbols request cancelled for query: '{Query}'", query)
                Throw
            Catch ex As Exception
                _logger.LogError(ex, "Error getting workspace symbols for query: '{Query}'", query)
                Return Array.Empty(Of SymbolInformation)()
            End Try
        End Function
        Private Async Function GetWorkspaceSymbolsFromOpenDocumentsAsync(query As String, cancellationToken As CancellationToken) As Task(Of SymbolInformation())
            If String.IsNullOrWhiteSpace(query) Then
                Return Array.Empty(Of SymbolInformation)()
            End If

            Dim results As New List(Of SymbolInformation)()

            For Each uri In _documentManager.OpenDocumentUris
                cancellationToken.ThrowIfCancellationRequested()

                Dim openDoc = _documentManager.GetOpenDocument(uri)
                If openDoc Is Nothing Then
                    Continue For
                End If

                If Not String.Equals(openDoc.LanguageId, "vb", StringComparison.OrdinalIgnoreCase) Then
                    Continue For
                End If

                Dim syntaxTree = VisualBasicSyntaxTree.ParseText(openDoc.Text)
                Dim syntaxRoot = Await syntaxTree.GetRootAsync(cancellationToken).ConfigureAwait(False)
                Dim sourceText = openDoc.Text

                For Each typeNode In GetTopLevelTypeBlocks(syntaxRoot)
                    Dim typeName = GetTypeName(typeNode)
                    If MatchesQuery(typeName, query) Then
                        Dim kind = GetTypeSymbolKind(typeNode)
                        If kind.HasValue Then
                            results.Add(CreateSymbolInformation(typeName, kind.Value, uri, typeNode.Span, sourceText, Nothing))
                        End If
                    End If

                    For Each member In GetTypeMembers(typeNode)
                        If MatchesQuery(member.Name, query) Then
                            results.Add(CreateSymbolInformation(member.Name, member.Kind, uri, member.Span, sourceText, typeName))
                        End If
                    Next

                    If results.Count >= 100 Then
                        Return results.ToArray()
                    End If
                Next
            Next

            Return results.ToArray()
        End Function

        Private Shared Function MatchesQuery(name As String, query As String) As Boolean
            If String.IsNullOrWhiteSpace(name) Then
                Return False
            End If

            Return name.IndexOf(query, StringComparison.OrdinalIgnoreCase) >= 0
        End Function

        Private Shared Function CreateSymbolInformation(name As String, kind As Protocol.SymbolKind, uri As String, span As TextSpan, sourceText As SourceText, containerName As String) As SymbolInformation
            Return New SymbolInformation With {
                .Name = name,
                .Kind = kind,
                .Location = New Protocol.Location With {
                    .Uri = uri,
                    .Range = GetRange(span, sourceText)
                },
                .ContainerName = containerName
            }
        End Function

        Private Shared Iterator Function GetTypeMembers(typeNode As SyntaxNode) As IEnumerable(Of (Name As String, Kind As Protocol.SymbolKind, Span As TextSpan))
            Dim members As IEnumerable(Of SyntaxNode) = Nothing
            If TypeOf typeNode Is ClassBlockSyntax Then
                members = DirectCast(typeNode, ClassBlockSyntax).Members
            ElseIf TypeOf typeNode Is StructureBlockSyntax Then
                members = DirectCast(typeNode, StructureBlockSyntax).Members
            ElseIf TypeOf typeNode Is InterfaceBlockSyntax Then
                members = DirectCast(typeNode, InterfaceBlockSyntax).Members
            ElseIf TypeOf typeNode Is ModuleBlockSyntax Then
                members = DirectCast(typeNode, ModuleBlockSyntax).Members
            ElseIf TypeOf typeNode Is EnumBlockSyntax Then
                members = DirectCast(typeNode, EnumBlockSyntax).Members
            End If

            If members Is Nothing Then
                Return
            End If

            For Each member In members
                If TypeOf member Is MethodBlockSyntax Then
                    Dim methodBlock = DirectCast(member, MethodBlockSyntax)
                    Dim methodName = methodBlock.SubOrFunctionStatement.Identifier.Text
                    If Not String.IsNullOrWhiteSpace(methodName) Then
                        Yield (methodName, Protocol.SymbolKind.Method, methodBlock.SubOrFunctionStatement.Span)
                    End If
                ElseIf TypeOf member Is PropertyBlockSyntax Then
                    Dim propertyBlock = DirectCast(member, PropertyBlockSyntax)
                    Dim propertyName = propertyBlock.PropertyStatement.Identifier.Text
                    If Not String.IsNullOrWhiteSpace(propertyName) Then
                        Yield (propertyName, Protocol.SymbolKind.Property, propertyBlock.PropertyStatement.Span)
                    End If
                ElseIf TypeOf member Is EventBlockSyntax Then
                    Dim eventBlock = DirectCast(member, EventBlockSyntax)
                    Dim eventName = eventBlock.EventStatement.Identifier.Text
                    If Not String.IsNullOrWhiteSpace(eventName) Then
                        Yield (eventName, Protocol.SymbolKind.Event, eventBlock.EventStatement.Span)
                    End If
                ElseIf TypeOf member Is FieldDeclarationSyntax Then
                    Dim fieldDecl = DirectCast(member, FieldDeclarationSyntax)
                    For Each declarator In fieldDecl.Declarators
                        For Each name In declarator.Names
                            Dim fieldName = name.Identifier.Text
                            If Not String.IsNullOrWhiteSpace(fieldName) Then
                                Yield (fieldName, Protocol.SymbolKind.Field, name.Span)
                            End If
                        Next
                    Next
                ElseIf TypeOf member Is EnumMemberDeclarationSyntax Then
                    Dim enumMember = DirectCast(member, EnumMemberDeclarationSyntax)
                    Dim enumName = enumMember.Identifier.Text
                    If Not String.IsNullOrWhiteSpace(enumName) Then
                        Yield (enumName, Protocol.SymbolKind.EnumMember, enumMember.Span)
                    End If
                ElseIf TypeOf member Is TypeBlockSyntax Then
                    Dim typeBlock = DirectCast(member, TypeBlockSyntax)
                    Dim typeName = GetTypeName(typeBlock)
                    Dim typeKind = GetTypeSymbolKind(typeBlock)
                    If Not String.IsNullOrEmpty(typeName) AndAlso typeKind.HasValue Then
                        Yield (typeName, typeKind.Value, typeBlock.Span)
                    End If
                End If
            Next
        End Function

        ''' <summary>
        ''' Creates a DocumentSymbol for a type symbol including its members.
        ''' </summary>
        Private Async Function CreateDocumentSymbolAsync(typeSymbol As INamedTypeSymbol, node As SyntaxNode, sourceText As SourceText, semanticModel As SemanticModel, cancellationToken As CancellationToken) As Task(Of DocumentSymbol)
            Dim range = GetRange(node.Span, sourceText)
            Dim selectionRange = GetSelectionRange(typeSymbol, node, sourceText)

            Dim children As New List(Of DocumentSymbol)()

            For Each member In typeSymbol.GetMembers()
                cancellationToken.ThrowIfCancellationRequested()

                If member.IsImplicitlyDeclared Then
                    Continue For
                End If

                If TypeOf member Is INamedTypeSymbol Then
                    Continue For
                End If

                Dim memberLocation = member.Locations.FirstOrDefault(Function(l) l.IsInSource)
                If memberLocation Is Nothing Then
                    Continue For
                End If

                Dim memberNode = node.FindNode(memberLocation.SourceSpan)
                If memberNode Is Nothing Then
                    Continue For
                End If

                Dim memberSymbol = CreateMemberSymbol(member, memberNode, sourceText)
                If memberSymbol IsNot Nothing Then
                    children.Add(memberSymbol)
                End If
            Next

            For Each nestedType In typeSymbol.GetTypeMembers()
                cancellationToken.ThrowIfCancellationRequested()

                If nestedType.IsImplicitlyDeclared Then
                    Continue For
                End If

                Dim nestedLocation = nestedType.Locations.FirstOrDefault(Function(l) l.IsInSource)
                If nestedLocation Is Nothing Then
                    Continue For
                End If

                Dim nestedNode = node.FindNode(nestedLocation.SourceSpan)
                If nestedNode Is Nothing Then
                    Continue For
                End If

                Dim nestedSymbol = Await CreateDocumentSymbolAsync(nestedType, nestedNode, sourceText, semanticModel, cancellationToken).ConfigureAwait(False)
                If nestedSymbol IsNot Nothing Then
                    children.Add(nestedSymbol)
                End If
            Next

            Return New DocumentSymbol With {
                .Name = typeSymbol.Name,
                .Detail = GetTypeDetail(typeSymbol),
                .Kind = GetSymbolKind(typeSymbol),
                .Range = range,
                .SelectionRange = selectionRange,
                .Children = If(children.Count > 0, children.ToArray(), Nothing)
            }
        End Function

        ''' <summary>
        ''' Creates a DocumentSymbol for a member (method, property, field, etc.).
        ''' </summary>
        Private Function CreateMemberSymbol(member As ISymbol, node As SyntaxNode, sourceText As SourceText) As DocumentSymbol
            Dim range = GetRange(node.Span, sourceText)
            Dim selectionRange = GetSelectionRange(member, node, sourceText)

            Return New DocumentSymbol With {
                .Name = member.Name,
                .Detail = GetMemberDetail(member),
                .Kind = GetSymbolKind(member),
                .Range = range,
                .SelectionRange = selectionRange,
                .Children = Nothing
            }
        End Function

        ''' <summary>
        ''' Gets the selection range (identifier span) for a symbol.
        ''' </summary>
        Private Function GetSelectionRange(symbol As ISymbol, node As SyntaxNode, sourceText As SourceText) As Protocol.Range
            For Each token In node.ChildTokens()
                If token.IsKind(Microsoft.CodeAnalysis.VisualBasic.SyntaxKind.IdentifierToken) AndAlso token.Text = symbol.Name Then
                    Return GetRange(token.Span, sourceText)
                End If
            Next

            Return GetRange(node.Span, sourceText)
        End Function

        ''' <summary>
        ''' Gets detail text for a type symbol.
        ''' </summary>
        Private Shared Function GetTypeDetail(typeSymbol As INamedTypeSymbol) As String
            If typeSymbol.TypeParameters.Length > 0 Then
                Return $"(Of {String.Join(", ", typeSymbol.TypeParameters.Select(Function(tp) tp.Name))})"
            End If
            Return Nothing
        End Function

        ''' <summary>
        ''' Gets detail text for a member symbol.
        ''' </summary>
        Private Shared Function GetMemberDetail(member As ISymbol) As String
            If TypeOf member Is IMethodSymbol Then
                Dim methodSymbol = DirectCast(member, IMethodSymbol)
                If methodSymbol.ReturnsVoid Then
                    Return Nothing
                End If
                Return $"As {methodSymbol.ReturnType.ToDisplayString(SymbolDisplayFormat.MinimallyQualifiedFormat)}"
            End If

            If TypeOf member Is IPropertySymbol Then
                Dim propertySymbol = DirectCast(member, IPropertySymbol)
                Return $"As {propertySymbol.Type.ToDisplayString(SymbolDisplayFormat.MinimallyQualifiedFormat)}"
            End If

            If TypeOf member Is IFieldSymbol Then
                Dim fieldSymbol = DirectCast(member, IFieldSymbol)
                Return $"As {fieldSymbol.Type.ToDisplayString(SymbolDisplayFormat.MinimallyQualifiedFormat)}"
            End If

            If TypeOf member Is IEventSymbol Then
                Dim eventSymbol = DirectCast(member, IEventSymbol)
                Return $"As {eventSymbol.Type.ToDisplayString(SymbolDisplayFormat.MinimallyQualifiedFormat)}"
            End If

            Return Nothing
        End Function

        ''' <summary>
        ''' Maps a Roslyn symbol to an LSP SymbolKind.
        ''' </summary>
        Private Shared Function GetSymbolKind(symbol As ISymbol) As Protocol.SymbolKind
            If TypeOf symbol Is INamedTypeSymbol Then
                Dim typeSymbol = DirectCast(symbol, INamedTypeSymbol)
                Select Case typeSymbol.TypeKind
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
                    Case TypeKind.Delegate
                        Return Protocol.SymbolKind.Function
                    Case Else
                        Return Protocol.SymbolKind.Class
                End Select
            End If

            If TypeOf symbol Is IMethodSymbol Then
                Dim methodSymbol = DirectCast(symbol, IMethodSymbol)
                Select Case methodSymbol.MethodKind
                    Case MethodKind.Constructor
                        Return Protocol.SymbolKind.Constructor
                    Case MethodKind.Destructor
                        Return Protocol.SymbolKind.Method
                    Case MethodKind.PropertyGet, MethodKind.PropertySet
                        Return Protocol.SymbolKind.Property
                    Case MethodKind.EventAdd, MethodKind.EventRemove
                        Return Protocol.SymbolKind.Event
                    Case Else
                        Return Protocol.SymbolKind.Method
                End Select
            End If

            If TypeOf symbol Is IPropertySymbol Then
                Return Protocol.SymbolKind.Property
            End If
            If TypeOf symbol Is IFieldSymbol Then
                Dim fieldSymbol = DirectCast(symbol, IFieldSymbol)
                Return If(fieldSymbol.IsConst, Protocol.SymbolKind.Constant, Protocol.SymbolKind.Field)
            End If
            If TypeOf symbol Is IEventSymbol Then
                Return Protocol.SymbolKind.Event
            End If
            If TypeOf symbol Is INamespaceSymbol Then
                Return Protocol.SymbolKind.Namespace
            End If
            If TypeOf symbol Is IParameterSymbol Then
                Return Protocol.SymbolKind.Variable
            End If
            If TypeOf symbol Is ILocalSymbol Then
                Return Protocol.SymbolKind.Variable
            End If
            If TypeOf symbol Is ITypeParameterSymbol Then
                Return Protocol.SymbolKind.TypeParameter
            End If

            Return Protocol.SymbolKind.Variable
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
