' SemanticTokensService - Provides semantic tokens via LSP
' Services Layer as defined in docs/architecture.md Section 5.4

Imports System.Linq
Imports Microsoft.CodeAnalysis
Imports Microsoft.CodeAnalysis.Classification
Imports Microsoft.CodeAnalysis.Text
Imports Microsoft.Extensions.Logging
Imports VbNet.LanguageServer.Protocol
Imports VbNet.LanguageServer.Workspace

Namespace Services

    ''' <summary>
    ''' Provides semantic tokens for VB.NET documents using Roslyn classifiers.
    ''' </summary>
    Public NotInheritable Class SemanticTokensService
        Private ReadOnly _workspaceManager As WorkspaceManager
        Private ReadOnly _documentManager As DocumentManager
        Private ReadOnly _logger As ILogger(Of SemanticTokensService)

        Private Shared ReadOnly TokenTypes As String() = {
            "namespace",
            "type",
            "class",
            "struct",
            "interface",
            "enum",
            "typeParameter",
            "function",
            "method",
            "property",
            "variable",
            "parameter",
            "field",
            "event",
            "keyword",
            "comment",
            "string",
            "number",
            "operator"
        }

        Private Shared ReadOnly TokenModifiers As String() = {
            "declaration",
            "static",
            "readonly"
        }

        Private Shared ReadOnly ClassificationMap As Dictionary(Of String, TokenInfo) = New Dictionary(Of String, TokenInfo)(StringComparer.Ordinal) From {
            {ClassificationTypeNames.NamespaceName, New TokenInfo("namespace")},
            {ClassificationTypeNames.ClassName, New TokenInfo("class")},
            {ClassificationTypeNames.StructName, New TokenInfo("struct")},
            {ClassificationTypeNames.InterfaceName, New TokenInfo("interface")},
            {ClassificationTypeNames.EnumName, New TokenInfo("enum")},
            {ClassificationTypeNames.TypeParameterName, New TokenInfo("typeParameter")},
            {ClassificationTypeNames.DelegateName, New TokenInfo("type")},
            {ClassificationTypeNames.ModuleName, New TokenInfo("type")},
            {ClassificationTypeNames.MethodName, New TokenInfo("method")},
            {ClassificationTypeNames.ExtensionMethodName, New TokenInfo("method")},
            {ClassificationTypeNames.PropertyName, New TokenInfo("property")},
            {ClassificationTypeNames.FieldName, New TokenInfo("field")},
            {ClassificationTypeNames.EventName, New TokenInfo("event")},
            {ClassificationTypeNames.ParameterName, New TokenInfo("parameter")},
            {ClassificationTypeNames.LocalName, New TokenInfo("variable")},
            {ClassificationTypeNames.ConstantName, New TokenInfo("variable", TokenModifier.[ReadOnly])},
            {ClassificationTypeNames.Keyword, New TokenInfo("keyword")},
            {ClassificationTypeNames.ControlKeyword, New TokenInfo("keyword")},
            {ClassificationTypeNames.Comment, New TokenInfo("comment")},
            {ClassificationTypeNames.StringLiteral, New TokenInfo("string")},
            {ClassificationTypeNames.VerbatimStringLiteral, New TokenInfo("string")},
            {ClassificationTypeNames.NumericLiteral, New TokenInfo("number")},
            {ClassificationTypeNames.Operator, New TokenInfo("operator")},
            {ClassificationTypeNames.OperatorOverloaded, New TokenInfo("operator")}
        }

        Public Sub New(workspaceManager As WorkspaceManager, documentManager As DocumentManager, logger As ILogger(Of SemanticTokensService))
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

        Public Shared Function GetDefaultOptions() As SemanticTokensOptions
            Return New SemanticTokensOptions With {
                .Legend = GetLegend(),
                .Full = True,
                .Range = True
            }
        End Function

        Public Shared Function GetLegend() As SemanticTokensLegend
            Return New SemanticTokensLegend With {
                .TokenTypes = TokenTypes,
                .TokenModifiers = TokenModifiers
            }
        End Function

        Public Async Function GetSemanticTokensAsync(parameters As SemanticTokensParams, cancellationToken As CancellationToken) As Task(Of SemanticTokens)
            If parameters Is Nothing OrElse parameters.TextDocument Is Nothing Then
                Return New SemanticTokens()
            End If

            Dim uri = parameters.TextDocument.Uri
            _logger.LogDebug("Semantic tokens requested for {Uri}", uri)

            Dim document = _documentManager.GetRoslynDocument(uri)
            If document Is Nothing Then
                _logger.LogTrace("No Roslyn document found for: {Uri}", uri)
                Return New SemanticTokens()
            End If

            Dim sourceText = Await document.GetTextAsync(cancellationToken).ConfigureAwait(False)
            Dim span As New TextSpan(0, sourceText.Length)
            Return Await BuildTokensAsync(document, sourceText, span, cancellationToken).ConfigureAwait(False)
        End Function

        Public Async Function GetSemanticTokensRangeAsync(parameters As SemanticTokensRangeParams, cancellationToken As CancellationToken) As Task(Of SemanticTokens)
            If parameters Is Nothing OrElse parameters.TextDocument Is Nothing OrElse parameters.Range Is Nothing Then
                Return New SemanticTokens()
            End If

            Dim uri = parameters.TextDocument.Uri
            Dim document = _documentManager.GetRoslynDocument(uri)
            If document Is Nothing Then
                _logger.LogTrace("No Roslyn document found for: {Uri}", uri)
                Return New SemanticTokens()
            End If

            Dim sourceText = Await document.GetTextAsync(cancellationToken).ConfigureAwait(False)
            Dim span = ToTextSpan(parameters.Range, sourceText)
            Return Await BuildTokensAsync(document, sourceText, span, cancellationToken).ConfigureAwait(False)
        End Function

        Private Shared Function ToTextSpan(range As Protocol.Range, text As SourceText) As TextSpan
            Dim start = GetOffset(range.Start, text)
            Dim [end] = GetOffset(range.End, text)
            If [end] < start Then
                Dim tmp = start
                start = [end]
                [end] = tmp
            End If

            Return TextSpan.FromBounds(start, [end])
        End Function

        Private Async Function BuildTokensAsync(document As Document, sourceText As SourceText, span As TextSpan, cancellationToken As CancellationToken) As Task(Of SemanticTokens)
            Dim classified = Await Classifier.GetClassifiedSpansAsync(document, span, cancellationToken).ConfigureAwait(False)

            If Not classified.Any() Then
                Return New SemanticTokens()
            End If

            Dim tokens As New List(Of SemanticTokenData)()

            For Each item In classified
                Dim info As TokenInfo = Nothing
                If Not ClassificationMap.TryGetValue(item.ClassificationType, info) Then
                    Continue For
                End If

                Dim tokenTypeIndex As UInteger
                If Not TryGetTokenTypeIndex(info.TokenType, tokenTypeIndex) Then
                    Continue For
                End If

                AppendTokenSegments(tokens, item.TextSpan, tokenTypeIndex, info.Modifier, sourceText)
            Next

            tokens.Sort(SemanticTokenDataComparer.Instance)

            Dim data = EncodeTokens(tokens)
            Return New SemanticTokens With {.Data = data}
        End Function

        Private Shared Sub AppendTokenSegments(tokens As List(Of SemanticTokenData), span As TextSpan, tokenType As UInteger, modifier As TokenModifier, sourceText As SourceText)
            Dim startLine = sourceText.Lines.GetLineFromPosition(span.Start)
            Dim endLine = sourceText.Lines.GetLineFromPosition(span.[End])

            For line = startLine.LineNumber To endLine.LineNumber
                Dim textLine = sourceText.Lines(line)
                Dim lineStart = textLine.Start
                Dim segmentStart = If(line = startLine.LineNumber, span.Start - lineStart, 0)
                Dim segmentEnd = If(line = endLine.LineNumber, span.[End] - lineStart, textLine.End - lineStart)
                Dim length = segmentEnd - segmentStart
                If length <= 0 Then
                    Continue For
                End If

                tokens.Add(New SemanticTokenData With {
                    .Line = line,
                    .StartChar = segmentStart,
                    .Length = length,
                    .TokenType = tokenType,
                    .TokenModifiers = EncodeModifiers(modifier)
                })
            Next
        End Sub

        Private Shared Function EncodeTokens(tokens As IReadOnlyList(Of SemanticTokenData)) As UInteger()
            Dim data As New List(Of UInteger)(tokens.Count * 5)
            Dim prevLine = 0
            Dim prevStart = 0

            For Each token In tokens
                Dim deltaLine = token.Line - prevLine
                Dim deltaStart = If(deltaLine = 0, token.StartChar - prevStart, token.StartChar)

                data.Add(CUInt(deltaLine))
                data.Add(CUInt(deltaStart))
                data.Add(CUInt(token.Length))
                data.Add(token.TokenType)
                data.Add(token.TokenModifiers)

                prevLine = token.Line
                prevStart = token.StartChar
            Next

            Return data.ToArray()
        End Function

        Private Shared Function EncodeModifiers(modifier As TokenModifier) As UInteger
            Select Case modifier
                Case TokenModifier.[ReadOnly]
                    Return CUInt(1) << 2
                Case Else
                    Return 0
            End Select
        End Function

        Private Shared Function TryGetTokenTypeIndex(tokenType As String, ByRef index As UInteger) As Boolean
            Dim idx = Array.IndexOf(TokenTypes, tokenType)
            If idx < 0 Then
                index = 0
                Return False
            End If

            index = CUInt(idx)
            Return True
        End Function

        Private Shared Function GetOffset(position As Position, text As SourceText) As Integer
            Dim line = Math.Min(position.Line, text.Lines.Count - 1)
            line = Math.Max(0, line)

            Dim textLine = text.Lines(line)
            Dim character = Math.Min(position.Character, textLine.End - textLine.Start)
            character = Math.Max(0, character)

            Return textLine.Start + character
        End Function

        Private NotInheritable Class SemanticTokenDataComparer
            Implements IComparer(Of SemanticTokenData)

            Public Shared ReadOnly Instance As SemanticTokenDataComparer = New SemanticTokenDataComparer()

            Public Function Compare(x As SemanticTokenData, y As SemanticTokenData) As Integer Implements IComparer(Of SemanticTokenData).Compare
                Dim line = x.Line.CompareTo(y.Line)
                If line <> 0 Then
                    Return line
                End If

                Return x.StartChar.CompareTo(y.StartChar)
            End Function
        End Class

        Private Structure SemanticTokenData
            Public Property Line As Integer
            Public Property StartChar As Integer
            Public Property Length As Integer
            Public Property TokenType As UInteger
            Public Property TokenModifiers As UInteger
        End Structure

        Private Structure TokenInfo
            Public Sub New(tokenType As String, Optional modifier As TokenModifier = TokenModifier.None)
                Me.TokenType = tokenType
                Me.Modifier = modifier
            End Sub

            Public ReadOnly Property TokenType As String
            Public ReadOnly Property Modifier As TokenModifier
        End Structure

        Private Enum TokenModifier
            None
            [ReadOnly]
        End Enum
    End Class

End Namespace
