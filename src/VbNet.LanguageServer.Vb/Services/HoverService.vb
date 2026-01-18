' HoverService - Provides symbol hover information via LSP
' Services Layer as defined in docs/architecture.md Section 5.4

Imports Microsoft.CodeAnalysis
Imports Microsoft.CodeAnalysis.Text
Imports Microsoft.Extensions.Logging
Imports VbNet.LanguageServer.Protocol
Imports VbNet.LanguageServer.Workspace

Namespace Services

    ''' <summary>
    ''' Provides hover information (quick info) for VB.NET documents.
    ''' Uses Roslyn semantic model to get symbol information.
    ''' </summary>
    Public NotInheritable Class HoverService
        Private ReadOnly _workspaceManager As WorkspaceManager
        Private ReadOnly _documentManager As DocumentManager
        Private ReadOnly _logger As ILogger(Of HoverService)

        Public Sub New(workspaceManager As WorkspaceManager, documentManager As DocumentManager, logger As ILogger(Of HoverService))
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
        ''' Gets hover information for a document at the specified position.
        ''' </summary>
        Public Async Function GetHoverAsync(parameters As HoverParams, cancellationToken As CancellationToken) As Task(Of Hover)
            If parameters Is Nothing OrElse parameters.TextDocument Is Nothing Then
                Return Nothing
            End If

            Dim uri = parameters.TextDocument.Uri
            Dim position = parameters.Position

            _logger.LogDebug("Hover requested at {Uri} ({Line}:{Character})", uri, position.Line, position.Character)

            Dim document = _documentManager.GetRoslynDocument(uri)
            If document Is Nothing Then
                _logger.LogTrace("No Roslyn document found for: {Uri}", uri)
                Return Nothing
            End If

            Try
                Dim sourceText = Await document.GetTextAsync(cancellationToken).ConfigureAwait(False)
                Dim offset = GetOffset(position, sourceText)

                cancellationToken.ThrowIfCancellationRequested()

                Dim semanticModel = Await document.GetSemanticModelAsync(cancellationToken).ConfigureAwait(False)
                If semanticModel Is Nothing Then
                    _logger.LogWarning("Could not get semantic model for: {Uri}", uri)
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

                Dim parentNode = token.Parent
                If parentNode Is Nothing Then
                    Return Nothing
                End If

                cancellationToken.ThrowIfCancellationRequested()

                Dim symbolInfo = semanticModel.GetSymbolInfo(parentNode, cancellationToken)
                Dim symbol = If(symbolInfo.Symbol, symbolInfo.CandidateSymbols.FirstOrDefault())

                If symbol Is Nothing Then
                    symbol = semanticModel.GetDeclaredSymbol(parentNode, cancellationToken)
                End If

                If symbol Is Nothing Then
                    Dim typeInfo = semanticModel.GetTypeInfo(parentNode, cancellationToken)
                    symbol = typeInfo.Type
                End If

                If symbol Is Nothing Then
                    _logger.LogTrace("No symbol found at position for: {Uri}", uri)
                    Return Nothing
                End If

                Dim hoverContent = BuildHoverContent(symbol, semanticModel, offset)
                If String.IsNullOrEmpty(hoverContent) Then
                    Return Nothing
                End If

                Dim range = GetRange(token.Span, sourceText)

                _logger.LogDebug("Returning hover for symbol: {Symbol}", symbol.Name)

                Return New Hover With {
                    .Contents = New MarkupContent With {
                        .Kind = MarkupKind.Markdown,
                        .Value = hoverContent
                    },
                    .Range = range
                }
            Catch ex As OperationCanceledException
                _logger.LogTrace("Hover request cancelled for: {Uri}", uri)
                Throw
            Catch ex As Exception
                _logger.LogError(ex, "Error getting hover for: {Uri}", uri)
                Return Nothing
            End Try
        End Function

        ''' <summary>
        ''' Builds markdown content for hover display.
        ''' </summary>
        Private Function BuildHoverContent(symbol As ISymbol, semanticModel As SemanticModel, position As Integer) As String
            Dim sb As New System.Text.StringBuilder()

            Dim signature = GetSymbolSignature(symbol)
            If Not String.IsNullOrEmpty(signature) Then
                sb.AppendLine("```vb")
                sb.AppendLine(signature)
                sb.AppendLine("```")
            End If

            Dim documentation = GetDocumentation(symbol)
            If Not String.IsNullOrEmpty(documentation) Then
                sb.AppendLine()
                sb.AppendLine(documentation)
            End If

            Dim containerInfo = GetContainerInfo(symbol)
            If Not String.IsNullOrEmpty(containerInfo) Then
                sb.AppendLine()
                sb.AppendLine($"*{containerInfo}*")
            End If

            Return sb.ToString().Trim()
        End Function

        ''' <summary>
        ''' Gets a human-readable signature for a symbol.
        ''' </summary>
        Private Shared Function GetSymbolSignature(symbol As ISymbol) As String
            If TypeOf symbol Is IMethodSymbol Then
                Return GetMethodSignature(DirectCast(symbol, IMethodSymbol))
            End If
            If TypeOf symbol Is IPropertySymbol Then
                Return GetPropertySignature(DirectCast(symbol, IPropertySymbol))
            End If
            If TypeOf symbol Is IFieldSymbol Then
                Return GetFieldSignature(DirectCast(symbol, IFieldSymbol))
            End If
            If TypeOf symbol Is ILocalSymbol Then
                Return GetLocalSignature(DirectCast(symbol, ILocalSymbol))
            End If
            If TypeOf symbol Is IParameterSymbol Then
                Return GetParameterSignature(DirectCast(symbol, IParameterSymbol))
            End If
            If TypeOf symbol Is INamedTypeSymbol Then
                Return GetTypeSignature(DirectCast(symbol, INamedTypeSymbol))
            End If
            If TypeOf symbol Is INamespaceSymbol Then
                Dim ns = DirectCast(symbol, INamespaceSymbol)
                Return $"Namespace {ns.ToDisplayString()}"
            End If
            If TypeOf symbol Is IEventSymbol Then
                Return GetEventSignature(DirectCast(symbol, IEventSymbol))
            End If

            Return symbol.ToDisplayString(SymbolDisplayFormat.MinimallyQualifiedFormat)
        End Function

        Private Shared Function GetMethodSignature(methodSymbol As IMethodSymbol) As String
            Dim returnType = If(methodSymbol.ReturnsVoid, "", $" As {methodSymbol.ReturnType.ToDisplayString(SymbolDisplayFormat.MinimallyQualifiedFormat)}")
            Dim parameters = String.Join(", ", methodSymbol.Parameters.Select(Function(p) $"{p.Name} As {p.Type.ToDisplayString(SymbolDisplayFormat.MinimallyQualifiedFormat)}"))

            Dim accessibility = GetAccessibilityString(methodSymbol.DeclaredAccessibility)
            Dim modifiers = GetMethodModifiers(methodSymbol)

            If methodSymbol.MethodKind = MethodKind.Constructor Then
                Return $"{accessibility}{modifiers}Sub New({parameters})"
            End If

            Dim keyword = If(methodSymbol.ReturnsVoid, "Sub", "Function")
            Return $"{accessibility}{modifiers}{keyword} {methodSymbol.Name}({parameters}){returnType}"
        End Function

        Private Shared Function GetPropertySignature(propertySymbol As IPropertySymbol) As String
            Dim accessibility = GetAccessibilityString(propertySymbol.DeclaredAccessibility)
            Dim modifiers = If(propertySymbol.IsReadOnly, "ReadOnly ", If(propertySymbol.IsWriteOnly, "WriteOnly ", ""))
            Dim typeName = propertySymbol.Type.ToDisplayString(SymbolDisplayFormat.MinimallyQualifiedFormat)

            Return $"{accessibility}{modifiers}Property {propertySymbol.Name} As {typeName}"
        End Function

        Private Shared Function GetFieldSignature(fieldSymbol As IFieldSymbol) As String
            Dim accessibility = GetAccessibilityString(fieldSymbol.DeclaredAccessibility)
            Dim modifiers As String = ""
            If fieldSymbol.IsConst Then
                modifiers = "Const "
            ElseIf fieldSymbol.IsReadOnly Then
                modifiers = "ReadOnly "
            ElseIf fieldSymbol.IsStatic Then
                modifiers = "Shared "
            End If

            Dim typeName = fieldSymbol.Type.ToDisplayString(SymbolDisplayFormat.MinimallyQualifiedFormat)

            Return $"{accessibility}{modifiers}{fieldSymbol.Name} As {typeName}"
        End Function

        Private Shared Function GetLocalSignature(localSymbol As ILocalSymbol) As String
            Dim modifiers = If(localSymbol.IsConst, "Const ", "")
            Dim typeName = localSymbol.Type.ToDisplayString(SymbolDisplayFormat.MinimallyQualifiedFormat)
            Return $"{modifiers}Dim {localSymbol.Name} As {typeName}"
        End Function

        Private Shared Function GetParameterSignature(param As IParameterSymbol) As String
            Dim typeName = param.Type.ToDisplayString(SymbolDisplayFormat.MinimallyQualifiedFormat)
            Dim modifier As String
            Select Case param.RefKind
                Case RefKind.Ref, RefKind.Out
                    modifier = "ByRef "
                Case Else
                    modifier = ""
            End Select
            Return $"{modifier}{param.Name} As {typeName}"
        End Function

        Private Shared Function GetTypeSignature(typeSymbol As INamedTypeSymbol) As String
            Dim accessibility = GetAccessibilityString(typeSymbol.DeclaredAccessibility)
            Dim keyword As String
            Select Case typeSymbol.TypeKind
                Case TypeKind.Class
                    keyword = "Class"
                Case TypeKind.Interface
                    keyword = "Interface"
                Case TypeKind.Struct
                    keyword = "Structure"
                Case TypeKind.Enum
                    keyword = "Enum"
                Case TypeKind.Module
                    keyword = "Module"
                Case TypeKind.Delegate
                    keyword = "Delegate"
                Case Else
                    keyword = "Type"
            End Select

            Dim typeParams = ""
            If typeSymbol.TypeParameters.Length > 0 Then
                typeParams = $"(Of {String.Join(", ", typeSymbol.TypeParameters.Select(Function(tp) tp.Name))})"
            End If

            Return $"{accessibility}{keyword} {typeSymbol.Name}{typeParams}"
        End Function

        Private Shared Function GetEventSignature(evt As IEventSymbol) As String
            Dim accessibility = GetAccessibilityString(evt.DeclaredAccessibility)
            Dim typeName = evt.Type.ToDisplayString(SymbolDisplayFormat.MinimallyQualifiedFormat)
            Return $"{accessibility}Event {evt.Name} As {typeName}"
        End Function

        Private Shared Function GetAccessibilityString(accessibility As Accessibility) As String
            Select Case accessibility
                Case Accessibility.Public
                    Return "Public "
                Case Accessibility.Private
                    Return "Private "
                Case Accessibility.Protected
                    Return "Protected "
                Case Accessibility.Internal
                    Return "Friend "
                Case Accessibility.ProtectedOrInternal
                    Return "Protected Friend "
                Case Accessibility.ProtectedAndInternal
                    Return "Private Protected "
                Case Else
                    Return ""
            End Select
        End Function

        Private Shared Function GetMethodModifiers(methodSymbol As IMethodSymbol) As String
            Dim modifiers As New List(Of String)()

            If methodSymbol.IsStatic Then modifiers.Add("Shared")
            If methodSymbol.IsOverride Then modifiers.Add("Overrides")
            If methodSymbol.IsVirtual AndAlso Not methodSymbol.IsOverride Then modifiers.Add("Overridable")
            If methodSymbol.IsAbstract Then modifiers.Add("MustOverride")
            If methodSymbol.IsSealed AndAlso methodSymbol.IsOverride Then modifiers.Add("NotOverridable")
            If methodSymbol.IsAsync Then modifiers.Add("Async")

            Return If(modifiers.Count > 0, String.Join(" ", modifiers) & " ", "")
        End Function

        ''' <summary>
        ''' Gets XML documentation for a symbol.
        ''' </summary>
        Private Shared Function GetDocumentation(symbol As ISymbol) As String
            Dim xml = symbol.GetDocumentationCommentXml()
            If String.IsNullOrEmpty(xml) Then
                Return String.Empty
            End If

            Dim summaryStart = xml.IndexOf("<summary>", StringComparison.OrdinalIgnoreCase)
            Dim summaryEnd = xml.IndexOf("</summary>", StringComparison.OrdinalIgnoreCase)

            If summaryStart >= 0 AndAlso summaryEnd > summaryStart Then
                Dim summary = xml.Substring(summaryStart + 9, summaryEnd - summaryStart - 9)
                Return CleanXmlContent(summary)
            End If

            Return String.Empty
        End Function

        Private Shared Function CleanXmlContent(content As String) As String
            content = System.Text.RegularExpressions.Regex.Replace(content, "<[^>]+>", "")
            content = System.Text.RegularExpressions.Regex.Replace(content, "\s+", " ")
            Return content.Trim()
        End Function

        ''' <summary>
        ''' Gets container (namespace/type) information for a symbol.
        ''' </summary>
        Private Shared Function GetContainerInfo(symbol As ISymbol) As String
            Dim container As ISymbol = If(symbol.ContainingType, DirectCast(symbol.ContainingNamespace, ISymbol))
            If container Is Nothing OrElse (TypeOf container Is INamespaceSymbol AndAlso DirectCast(container, INamespaceSymbol).IsGlobalNamespace) Then
                Return String.Empty
            End If

            Return $"In {container.ToDisplayString(SymbolDisplayFormat.MinimallyQualifiedFormat)}"
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
