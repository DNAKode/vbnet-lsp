' SignatureHelpService - Provides parameter hints via LSP
' Services Layer as defined in docs/architecture.md Section 5.4

Imports System.Collections
Imports System.Reflection
Imports System.Text
Imports System.Xml.Linq
Imports Microsoft.CodeAnalysis
Imports Microsoft.CodeAnalysis.Text
Imports Microsoft.CodeAnalysis.VisualBasic.Syntax
Imports Microsoft.Extensions.Logging
Imports VbNet.LanguageServer.Protocol
Imports VbNet.LanguageServer.Workspace

Namespace Services

    ''' <summary>
    ''' Provides signature help (parameter hints) for VB.NET documents.
    ''' Uses Roslyn's SignatureHelpService for accurate overload data.
    ''' </summary>
    Public NotInheritable Class SignatureHelpService
        Private ReadOnly _workspaceManager As WorkspaceManager
        Private ReadOnly _documentManager As DocumentManager
        Private ReadOnly _logger As ILogger(Of SignatureHelpService)

        Private Shared ReadOnly DefaultTriggerCharacters As String() = {"(", ","}
        Private Shared ReadOnly DefaultRetriggerCharacters As String() = {")"}

        Public Sub New(workspaceManager As WorkspaceManager, documentManager As DocumentManager, logger As ILogger(Of SignatureHelpService))
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

        Public Shared Function GetDefaultOptions() As SignatureHelpOptions
            Return New SignatureHelpOptions With {
                .TriggerCharacters = DefaultTriggerCharacters,
                .RetriggerCharacters = DefaultRetriggerCharacters
            }
        End Function

        ''' <summary>
        ''' Gets signature help for a document at the specified position.
        ''' </summary>
        Public Async Function GetSignatureHelpAsync(parameters As SignatureHelpParams, cancellationToken As CancellationToken) As Task(Of Protocol.SignatureHelp)
            If parameters Is Nothing OrElse parameters.TextDocument Is Nothing Then
                Return Nothing
            End If

            Dim uri = parameters.TextDocument.Uri
            Dim position = parameters.Position

            _logger.LogDebug("Signature help requested at {Uri} ({Line}:{Character})", uri, position.Line, position.Character)

            Dim document = _documentManager.GetRoslynDocument(uri)
            If document Is Nothing Then
                _logger.LogTrace("No Roslyn document found for: {Uri}", uri)
                Return Nothing
            End If

            Dim sourceText As SourceText = Nothing
            Dim offset = 0

            Try
                sourceText = Await document.GetTextAsync(cancellationToken).ConfigureAwait(False)
                offset = GetOffset(position, sourceText)

                cancellationToken.ThrowIfCancellationRequested()

                Dim signatureHelpService = GetSignatureHelpService(document)
                If signatureHelpService Is Nothing Then
                    _logger.LogDebug("Signature help service not available for document: {Uri}", uri)
                    Return Await GetFallbackSignatureHelpAsync(document, offset, cancellationToken).ConfigureAwait(False)
                End If

                Dim triggerInfo = CreateTriggerInfo(signatureHelpService, parameters.Context)
                If triggerInfo Is Nothing Then
                    _logger.LogDebug("Signature help trigger info could not be created for: {Uri}", uri)
                    Return Await GetFallbackSignatureHelpAsync(document, offset, cancellationToken).ConfigureAwait(False)
                End If

                Dim signatureHelp = Await InvokeSignatureHelpAsync(signatureHelpService, document, offset, triggerInfo, cancellationToken).ConfigureAwait(False)

                Dim lspHelp = TryTranslateSignatureHelp(signatureHelp, cancellationToken)
                If lspHelp IsNot Nothing Then
                    If lspHelp.Signatures.Length > 1 Then
                        _logger.LogDebug("Returning {Count} signature help items for: {Uri}", lspHelp.Signatures.Length, uri)
                        Return lspHelp
                    End If

                    Dim fallback = Await GetFallbackSignatureHelpAsync(document, offset, cancellationToken).ConfigureAwait(False)
                    If fallback IsNot Nothing AndAlso fallback.Signatures.Length > lspHelp.Signatures.Length Then
                        _logger.LogDebug("Signature help fallback expanded results for: {Uri} ({Original} -> {Expanded})", uri, lspHelp.Signatures.Length, fallback.Signatures.Length)
                        Return fallback
                    End If

                    _logger.LogDebug("Returning {Count} signature help items for: {Uri}", lspHelp.Signatures.Length, uri)
                    Return lspHelp
                End If

                Return Await GetFallbackSignatureHelpAsync(document, offset, cancellationToken).ConfigureAwait(False)
            Catch ex As OperationCanceledException
                _logger.LogTrace("Signature help request cancelled for: {Uri}", uri)
                Throw
            Catch ex As Exception
                _logger.LogWarning(ex, "Error getting signature help for: {Uri}", uri)
            End Try

            Dim fallbackText = sourceText
            If fallbackText Is Nothing Then
                fallbackText = Await document.GetTextAsync(cancellationToken).ConfigureAwait(False)
            End If

            Dim fallbackOffset = If(sourceText IsNot Nothing, offset, GetOffset(position, fallbackText))
            Return Await GetFallbackSignatureHelpAsync(document, fallbackOffset, cancellationToken).ConfigureAwait(False)
        End Function

        Private Shared Async Function InvokeSignatureHelpAsync(signatureHelpService As Object, document As Document, offset As Integer, triggerInfo As Object, cancellationToken As CancellationToken) As Task(Of Object)
            Try
                Dim method = signatureHelpService.GetType().GetMethods(BindingFlags.Instance Or BindingFlags.Public).
                    FirstOrDefault(Function(m) m.Name = "GetSignatureHelpAsync" AndAlso m.GetParameters().Length = 4)

                If method Is Nothing Then
                    Return Nothing
                End If

                Dim task = TryCast(method.Invoke(signatureHelpService, New Object() {document, offset, triggerInfo, cancellationToken}), Task)
                If task Is Nothing Then
                    Return Nothing
                End If

                Await task.ConfigureAwait(False)

                Dim resultProperty = task.GetType().GetProperty("Result", BindingFlags.Public Or BindingFlags.Instance)
                Return If(resultProperty Is Nothing, Nothing, resultProperty.GetValue(task))
            Catch
                Return Nothing
            End Try
        End Function

        Private Shared Function TryTranslateSignatureHelp(signatureHelp As Object, cancellationToken As CancellationToken) As Protocol.SignatureHelp
            If signatureHelp Is Nothing Then
                Return Nothing
            End If

            Dim items = GetEnumerable(GetPropertyValue(signatureHelp, "Items")).ToArray()
            If items.Length = 0 Then
                Return Nothing
            End If

            Dim activeSignature = ReadOptionalInt(GetPropertyValue(signatureHelp, "SelectedItemIndex"))
            If Not activeSignature.HasValue Then
                activeSignature = 0
            End If
            If activeSignature.Value < 0 OrElse activeSignature.Value >= items.Length Then
                activeSignature = 0
            End If

            Dim activeParameter = ReadOptionalInt(GetPropertyValue(signatureHelp, "SemanticParameterIndex"))
            If activeParameter.HasValue AndAlso activeParameter.Value < 0 Then
                activeParameter = Nothing
            End If

            Dim signatures = items.Select(Function(item) ToSignatureInformation(item, activeParameter, cancellationToken)).ToArray()

            Return New Protocol.SignatureHelp With {
                .Signatures = signatures,
                .ActiveSignature = activeSignature,
                .ActiveParameter = activeParameter
            }
        End Function

        Private Shared Async Function GetFallbackSignatureHelpAsync(document As Document, offset As Integer, cancellationToken As CancellationToken) As Task(Of Protocol.SignatureHelp)
            Dim root = Await document.GetSyntaxRootAsync(cancellationToken).ConfigureAwait(False)
            If root Is Nothing Then
                Return Nothing
            End If

            Dim adjustedOffset = Math.Max(0, Math.Min(offset - 1, root.FullSpan.End - 1))
            Dim invocation = root.DescendantNodes().OfType(Of InvocationExpressionSyntax)().FirstOrDefault(Function(node) node.Span.Contains(adjustedOffset))
            Dim objectCreation = root.DescendantNodes().OfType(Of ObjectCreationExpressionSyntax)().FirstOrDefault(Function(node) node.Span.Contains(adjustedOffset))

            If invocation Is Nothing AndAlso objectCreation Is Nothing Then
                Return Nothing
            End If

            Dim argumentList = If(invocation?.ArgumentList, objectCreation?.ArgumentList)
            If argumentList Is Nothing Then
                Return Nothing
            End If

            Dim semanticModel = Await document.GetSemanticModelAsync(cancellationToken).ConfigureAwait(False)
            If semanticModel Is Nothing Then
                Return Nothing
            End If

            Dim symbolInfo = If(invocation IsNot Nothing, semanticModel.GetSymbolInfo(invocation, cancellationToken), semanticModel.GetSymbolInfo(objectCreation, cancellationToken))

            Dim methods As New List(Of IMethodSymbol)()
            If TypeOf symbolInfo.Symbol Is IMethodSymbol Then
                methods.Add(DirectCast(symbolInfo.Symbol, IMethodSymbol))
            End If

            methods.AddRange(symbolInfo.CandidateSymbols.OfType(Of IMethodSymbol)())
            If invocation IsNot Nothing Then
                Dim memberGroup = semanticModel.GetMemberGroup(invocation.Expression, cancellationToken)
                If memberGroup.Length > 0 Then
                    methods.AddRange(memberGroup.OfType(Of IMethodSymbol)())
                End If

                If TypeOf invocation.Expression Is MemberAccessExpressionSyntax Then
                    Dim memberAccess = DirectCast(invocation.Expression, MemberAccessExpressionSyntax)
                    Dim memberName = memberAccess.Name.Identifier.ValueText
                    Dim receiverType = TryCast(semanticModel.GetTypeInfo(memberAccess.Expression, cancellationToken).Type, INamedTypeSymbol)
                    If receiverType IsNot Nothing Then
                        methods.AddRange(receiverType.GetMembers(memberName).OfType(Of IMethodSymbol)())
                    End If
                    methods.AddRange(semanticModel.LookupSymbols(offset, name:=memberName).OfType(Of IMethodSymbol)())
                ElseIf TypeOf invocation.Expression Is IdentifierNameSyntax Then
                    Dim memberName = DirectCast(invocation.Expression, IdentifierNameSyntax).Identifier.ValueText
                    methods.AddRange(semanticModel.LookupSymbols(offset, name:=memberName).OfType(Of IMethodSymbol)())
                End If
            ElseIf objectCreation IsNot Nothing Then
                Dim typeInfo = semanticModel.GetTypeInfo(objectCreation.Type, cancellationToken)
                Dim namedType = TryCast(typeInfo.Type, INamedTypeSymbol)
                If namedType IsNot Nothing Then
                    methods.AddRange(namedType.Constructors)
                End If
            End If
            methods = methods.Distinct(New MethodSymbolComparer()).ToList()

            If methods.Count = 0 Then
                Return Nothing
            End If

            Dim activeParameter = GetActiveParameterIndex(argumentList, offset)

            Dim signatures = methods.Select(Function(m) ToSignatureInformation(m, activeParameter)).ToArray()

            Return New Protocol.SignatureHelp With {
                .Signatures = signatures,
                .ActiveSignature = 0,
                .ActiveParameter = activeParameter
            }
        End Function

        Private Shared Function GetActiveParameterIndex(argumentList As ArgumentListSyntax, offset As Integer) As Integer?
            If argumentList.Arguments.Count = 0 Then
                Return 0
            End If

            Dim index = 0
            For Each argument In argumentList.Arguments
                If offset <= argument.Span.[End] Then
                    Exit For
                End If

                index += 1
            Next

            If index >= argumentList.Arguments.Count Then
                index = argumentList.Arguments.Count - 1
            End If

            Return index
        End Function

        Private Shared Function ToSignatureInformation(method As IMethodSymbol, activeParameter As Integer?) As Protocol.SignatureInformation
            Dim documentation = ParseDocumentationParts(method.GetDocumentationCommentXml())
            Dim parameters = method.Parameters.Select(Function(p)
                                                         Dim paramDoc = documentation?.GetParamDocumentation(p.Name)
                                                         Return New Protocol.ParameterInformation With {
                                                             .Label = BuildParameterLabel(p),
                                                             .Documentation = If(String.IsNullOrEmpty(paramDoc), Nothing, New MarkupContent With {
                                                                 .Kind = MarkupKind.Markdown,
                                                                 .Value = paramDoc
                                                             })
                                                         }
                                                     End Function).ToArray()

            Return New Protocol.SignatureInformation With {
                .Label = BuildMethodSignature(method, parameters),
                .Documentation = BuildDocumentation(documentation),
                .Parameters = parameters,
                .ActiveParameter = activeParameter
            }
        End Function

        Private Shared Function BuildMethodSignature(method As IMethodSymbol, parameters As Protocol.ParameterInformation()) As String
            Dim name = If(method.MethodKind = MethodKind.Constructor, "New", method.Name)
            Dim typeParams = ""
            If method.TypeParameters.Length > 0 Then
                typeParams = $"(Of {String.Join(", ", method.TypeParameters.Select(Function(tp) tp.Name))})"
            End If
            Dim parameterList = String.Join(", ", parameters.Select(Function(p) p.Label))
            Dim returnType = If(method.ReturnsVoid, String.Empty, $" As {method.ReturnType.ToDisplayString(SymbolDisplayFormat.MinimallyQualifiedFormat)}")

            Return $"{name}{typeParams}({parameterList}){returnType}"
        End Function

        Private Shared Function BuildParameterLabel(parameter As IParameterSymbol) As String
            Dim modifier As String
            Select Case parameter.RefKind
                Case RefKind.Ref, RefKind.Out
                    modifier = "ByRef "
                Case Else
                    modifier = String.Empty
            End Select

            Dim optionalModifier = If(parameter.IsOptional, "Optional ", String.Empty)
            Dim typeName = parameter.Type.ToDisplayString(SymbolDisplayFormat.MinimallyQualifiedFormat)
            Return $"{optionalModifier}{modifier}{parameter.Name} As {typeName}"
        End Function

        Private Shared Function BuildDocumentation(documentation As DocumentationParts) As MarkupContent
            If documentation Is Nothing Then
                Return Nothing
            End If

            Dim sb As New StringBuilder()
            If Not String.IsNullOrWhiteSpace(documentation.Summary) Then
                sb.AppendLine(documentation.Summary)
            End If

            If documentation.ParamInfo.Count > 0 Then
                If sb.Length > 0 Then
                    sb.AppendLine()
                End If
                sb.AppendLine("**Parameters**")
                For Each paramInfo In documentation.ParamInfo
                    sb.AppendLine($"- `{paramInfo.Name}`: {paramInfo.Description}")
                Next
            End If

            If documentation.TypeParamInfo.Count > 0 Then
                If sb.Length > 0 Then
                    sb.AppendLine()
                End If
                sb.AppendLine("**Type Parameters**")
                For Each paramInfo In documentation.TypeParamInfo
                    sb.AppendLine($"- `{paramInfo.Name}`: {paramInfo.Description}")
                Next
            End If

            If Not String.IsNullOrWhiteSpace(documentation.Returns) Then
                If sb.Length > 0 Then
                    sb.AppendLine()
                End If
                sb.AppendLine($"**Returns**: {documentation.Returns}")
            End If

            If Not String.IsNullOrWhiteSpace(documentation.Value) Then
                If sb.Length > 0 Then
                    sb.AppendLine()
                End If
                sb.AppendLine($"**Value**: {documentation.Value}")
            End If

            Dim value = sb.ToString().Trim()
            If String.IsNullOrWhiteSpace(value) Then
                Return Nothing
            End If

            Return New MarkupContent With {
                .Kind = MarkupKind.Markdown,
                .Value = value
            }
        End Function

        Private Shared Function ParseDocumentationParts(xml As String) As DocumentationParts
            If String.IsNullOrWhiteSpace(xml) Then
                Return Nothing
            End If

            Try
                Dim doc = XDocument.Parse(xml)
                Dim root = doc.Root
                If root Is Nothing Then
                    Return Nothing
                End If

                Dim parts = New DocumentationParts()

                Dim summaryNode = root.Element("summary")
                If summaryNode IsNot Nothing Then
                    parts.Summary = CleanXmlContent(summaryNode.Value)
                End If

                For Each paramNode In root.Elements("param")
                    Dim name = paramNode.Attribute("name")?.Value
                    Dim description = CleanXmlContent(paramNode.Value)
                    If Not String.IsNullOrEmpty(name) AndAlso Not String.IsNullOrEmpty(description) Then
                        parts.ParamInfo.Add(New DocumentationParam With {.Name = name, .Description = description})
                    End If
                Next

                For Each typeParamNode In root.Elements("typeparam")
                    Dim name = typeParamNode.Attribute("name")?.Value
                    Dim description = CleanXmlContent(typeParamNode.Value)
                    If Not String.IsNullOrEmpty(name) AndAlso Not String.IsNullOrEmpty(description) Then
                        parts.TypeParamInfo.Add(New DocumentationParam With {.Name = name, .Description = description})
                    End If
                Next

                Dim returnsNode = root.Element("returns")
                If returnsNode IsNot Nothing Then
                    parts.Returns = CleanXmlContent(returnsNode.Value)
                End If

                Dim valueNode = root.Element("value")
                If valueNode IsNot Nothing Then
                    parts.Value = CleanXmlContent(valueNode.Value)
                End If

                If String.IsNullOrWhiteSpace(parts.Summary) AndAlso
                    parts.ParamInfo.Count = 0 AndAlso
                    parts.TypeParamInfo.Count = 0 AndAlso
                    String.IsNullOrWhiteSpace(parts.Returns) AndAlso
                    String.IsNullOrWhiteSpace(parts.Value) Then
                    Return Nothing
                End If

                Return parts
            Catch
                Return Nothing
            End Try
        End Function

        Private Shared Function CleanXmlContent(content As String) As String
            Dim noTags = System.Text.RegularExpressions.Regex.Replace(content, "<[^>]+>", "")
            Return System.Text.RegularExpressions.Regex.Replace(noTags, "\s+", " ").Trim()
        End Function

        Private NotInheritable Class DocumentationParts
            Public Property Summary As String
            Public Property Returns As String
            Public Property Value As String
            Public ReadOnly Property ParamInfo As New List(Of DocumentationParam)()
            Public ReadOnly Property TypeParamInfo As New List(Of DocumentationParam)()

            Public Function GetParamDocumentation(name As String) As String
                Dim param = ParamInfo.FirstOrDefault(Function(p) String.Equals(p.Name, name, StringComparison.OrdinalIgnoreCase))
                Return param?.Description
            End Function
        End Class

        Private NotInheritable Class DocumentationParam
            Public Property Name As String
            Public Property Description As String
        End Class

        Private NotInheritable Class MethodSymbolComparer
            Implements IEqualityComparer(Of IMethodSymbol)

            Public Overloads Function Equals(x As IMethodSymbol, y As IMethodSymbol) As Boolean Implements IEqualityComparer(Of IMethodSymbol).Equals
                Return SymbolEqualityComparer.Default.Equals(x, y)
            End Function

            Public Overloads Function GetHashCode(obj As IMethodSymbol) As Integer Implements IEqualityComparer(Of IMethodSymbol).GetHashCode
                Return SymbolEqualityComparer.Default.GetHashCode(obj)
            End Function
        End Class
        Private Shared Function GetSignatureHelpService(document As Document) As Object
            Dim featureAssembly = GetType(Microsoft.CodeAnalysis.Completion.CompletionService).Assembly
            Dim signatureHelpServiceType = featureAssembly.GetType("Microsoft.CodeAnalysis.SignatureHelp.SignatureHelpService", throwOnError:=False)

            If signatureHelpServiceType Is Nothing Then
                Return Nothing
            End If

            Dim services = document.Project.Services
            Dim getServiceByType = services.GetType().GetMethod(
                "GetService",
                BindingFlags.Public Or BindingFlags.NonPublic Or BindingFlags.Instance,
                binder:=Nothing,
                types:=New Type() {GetType(Type)},
                modifiers:=Nothing)

            If getServiceByType IsNot Nothing Then
                Return getServiceByType.Invoke(services, New Object() {signatureHelpServiceType})
            End If

            Return Nothing
        End Function

        Private Shared Function ToSignatureInformation(item As Object, activeParameter As Integer?, cancellationToken As CancellationToken) As Protocol.SignatureInformation
            Dim parameters = GetEnumerable(GetPropertyValue(item, "Parameters")) _
                .Select(Function(param) New Protocol.ParameterInformation With {
                            .Label = BuildParameterLabel(param),
                            .Documentation = BuildDocumentation(GetPropertyValue(param, "DocumentationFactory"), cancellationToken)
                        }) _
                .ToArray()

            Return New Protocol.SignatureInformation With {
                .Label = BuildSignatureLabel(item, parameters),
                .Documentation = BuildDocumentation(GetPropertyValue(item, "DocumentationFactory"), cancellationToken),
                .Parameters = parameters,
                .ActiveParameter = activeParameter
            }
        End Function

        Private Shared Function BuildSignatureLabel(item As Object, parameters As Protocol.ParameterInformation()) As String
            Dim prefix = TaggedPartsToString(GetTaggedParts(item, "PrefixDisplayParts"))
            Dim suffix = TaggedPartsToString(GetTaggedParts(item, "SuffixDisplayParts"))
            Dim separator = TaggedPartsToString(GetTaggedParts(item, "SeparatorDisplayParts"))
            Dim parameterLabels = parameters.Select(Function(p) p.Label)

            Return $"{prefix}{String.Join(separator, parameterLabels)}{suffix}"
        End Function

        Private Shared Function BuildParameterLabel(parameter As Object) As String
            Dim prefix = TaggedPartsToString(GetTaggedParts(parameter, "PrefixDisplayParts"))
            Dim display = TaggedPartsToString(GetTaggedParts(parameter, "DisplayParts"))
            Dim suffix = TaggedPartsToString(GetTaggedParts(parameter, "SuffixDisplayParts"))

            Return $"{prefix}{display}{suffix}"
        End Function

        Private Shared Function BuildDocumentation(documentationFactory As Object, cancellationToken As CancellationToken) As MarkupContent
            Dim del = TryCast(documentationFactory, [Delegate])
            If del Is Nothing Then
                Return Nothing
            End If

            Dim result As Object = Nothing
            Try
                result = del.DynamicInvoke(cancellationToken)
            Catch
                Return Nothing
            End Try

            Dim parts = TryCast(result, IEnumerable(Of TaggedText))
            If parts Is Nothing Then
                Dim enumerable = TryCast(result, IEnumerable)
                If enumerable IsNot Nothing Then
                    parts = enumerable.OfType(Of TaggedText)().ToArray()
                End If
            End If

            If parts Is Nothing Then
                Return Nothing
            End If

            Dim text = TaggedPartsToString(parts)
            If String.IsNullOrWhiteSpace(text) Then
                Return Nothing
            End If

            Return New MarkupContent With {
                .Kind = MarkupKind.Markdown,
                .Value = text.Trim()
            }
        End Function

        Private Shared Function TaggedPartsToString(parts As IEnumerable(Of TaggedText)) As String
            If parts Is Nothing Then
                Return String.Empty
            End If

            Dim sb As New StringBuilder()
            For Each part In parts
                sb.Append(part.Text)
            Next

            Return sb.ToString()
        End Function

        Private Shared Function GetTaggedParts(instance As Object, propertyName As String) As IEnumerable(Of TaggedText)
            If instance Is Nothing Then
                Return Array.Empty(Of TaggedText)()
            End If

            Dim value = GetPropertyValue(instance, propertyName)
            Dim taggedParts = TryCast(value, IEnumerable(Of TaggedText))
            If taggedParts IsNot Nothing Then
                Return taggedParts
            End If

            Dim enumerable = TryCast(value, IEnumerable)
            If enumerable IsNot Nothing Then
                Return enumerable.OfType(Of TaggedText)().ToArray()
            End If

            Return Array.Empty(Of TaggedText)()
        End Function

        Private Shared Function GetPropertyValue(instance As Object, propertyName As String) As Object
            Return instance.GetType().GetProperty(propertyName, BindingFlags.Public Or BindingFlags.Instance)?.GetValue(instance)
        End Function

        Private Shared Iterator Function GetEnumerable(value As Object) As IEnumerable(Of Object)
            Dim enumerable = TryCast(value, IEnumerable)
            If enumerable IsNot Nothing Then
                For Each item In enumerable
                    If item IsNot Nothing Then
                        Yield item
                    End If
                Next
            End If
        End Function

        Private Shared Function ReadOptionalInt(value As Object) As Integer?
            If value Is Nothing Then
                Return Nothing
            End If

            If TypeOf value Is Integer Then
                Return DirectCast(value, Integer)
            End If

            Return Nothing
        End Function

        Private Shared Function CreateTriggerInfo(signatureHelpService As Object, context As SignatureHelpContext) As Object
            Dim assembly = signatureHelpService.GetType().Assembly
            Dim triggerInfoType = assembly.GetType("Microsoft.CodeAnalysis.SignatureHelp.SignatureHelpTriggerInfo")
            Dim triggerReasonType = assembly.GetType("Microsoft.CodeAnalysis.SignatureHelp.SignatureHelpTriggerReason")

            If triggerInfoType Is Nothing OrElse triggerReasonType Is Nothing Then
                Return Nothing
            End If

            Dim reasonName As String
            Select Case context?.TriggerKind
                Case SignatureHelpTriggerKind.TriggerCharacter
                    reasonName = "TypeCharCommand"
                Case SignatureHelpTriggerKind.ContentChange
                    reasonName = "RetriggerCommand"
                Case Else
                    reasonName = "InvokeSignatureHelpCommand"
            End Select

            Dim reasonValue = [Enum].Parse(triggerReasonType, reasonName)
            Dim triggerChar = GetTriggerCharacter(context?.TriggerCharacter)

            For Each ctor In triggerInfoType.GetConstructors(BindingFlags.Public Or BindingFlags.NonPublic Or BindingFlags.Instance)
                Dim parameters = ctor.GetParameters()
                If parameters.Length = 2 Then
                    Dim charParam = parameters(1).ParameterType
                    Dim charValue As Object = triggerChar
                    If charParam Is GetType(Char) AndAlso triggerChar Is Nothing Then
                        charValue = ChrW(0)
                    End If

                    Return ctor.Invoke(New Object() {reasonValue, charValue})
                End If

                If parameters.Length = 1 Then
                    Return ctor.Invoke(New Object() {reasonValue})
                End If
            Next

            Return Nothing
        End Function

        Private Shared Function GetTriggerCharacter(triggerCharacter As String) As Char?
            If String.IsNullOrEmpty(triggerCharacter) Then
                Return Nothing
            End If

            Return triggerCharacter(0)
        End Function

        Private Shared Function GetOffset(position As Position, text As SourceText) As Integer
            Dim line = Math.Min(position.Line, text.Lines.Count - 1)
            line = Math.Max(0, line)

            Dim textLine = text.Lines(line)
            Dim character = Math.Min(position.Character, textLine.End - textLine.Start)
            character = Math.Max(0, character)

            Return textLine.Start + character
        End Function
    End Class

End Namespace
