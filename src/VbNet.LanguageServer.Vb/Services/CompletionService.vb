' CompletionService - Provides IntelliSense completion via LSP
' Services Layer as defined in docs/architecture.md Section 5.4

Imports System.Collections.Generic
Imports System.Collections.Immutable
Imports System.Linq
Imports Microsoft.CodeAnalysis
Imports Microsoft.CodeAnalysis.Text
Imports Microsoft.Extensions.Logging
Imports VbNet.LanguageServer.Protocol
Imports VbNet.LanguageServer.Workspace
Imports RoslynCompletion = Microsoft.CodeAnalysis.Completion

Namespace Services

    ''' <summary>
    ''' Provides IntelliSense completion for VB.NET documents.
    ''' Uses Roslyn's CompletionService for accurate suggestions.
    ''' </summary>
    Public NotInheritable Class CompletionService
        Friend Shared TestDelayAsync As Func(Of CancellationToken, Task)

        Private ReadOnly _workspaceManager As WorkspaceManager
        Private ReadOnly _documentManager As DocumentManager
        Private ReadOnly _logger As ILogger(Of CompletionService)

        ''' <summary>
        ''' Commit characters that should trigger completion acceptance.
        ''' </summary>
        ' Avoid space as a commit character to prevent duplicate keyword insertion (e.g., "AAs").
        Private Shared ReadOnly DefaultCommitCharacters As String() = {".", "(", "["}

        Public Sub New(workspaceManager As WorkspaceManager, documentManager As DocumentManager, logger As ILogger(Of CompletionService))
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
        ''' Gets completion items for a document at the specified position.
        ''' </summary>
        Public Async Function GetCompletionAsync(parameters As CompletionParams, cancellationToken As CancellationToken) As Task(Of CompletionList)
            If TestDelayAsync IsNot Nothing Then
                Await TestDelayAsync(cancellationToken).ConfigureAwait(False)
            End If

            If parameters Is Nothing OrElse parameters.TextDocument Is Nothing Then
                Return New CompletionList With {.IsIncomplete = False, .Items = Array.Empty(Of Protocol.CompletionItem)()}
            End If

            Dim uri = parameters.TextDocument.Uri
            Dim position = parameters.Position

            _logger.LogDebug("Completion requested at {Uri} ({Line}:{Character})", uri, position.Line, position.Character)

            Dim document = _documentManager.GetRoslynDocument(uri)
            If document Is Nothing Then
                _logger.LogTrace("No Roslyn document found for: {Uri}", uri)
                Return New CompletionList With {.IsIncomplete = False, .Items = Array.Empty(Of Protocol.CompletionItem)()}
            End If

            Try
                Dim sourceText = Await document.GetTextAsync(cancellationToken).ConfigureAwait(False)
                Dim offset = GetOffset(position, sourceText)

                cancellationToken.ThrowIfCancellationRequested()

                Dim completionService = RoslynCompletion.CompletionService.GetService(document)
                If completionService Is Nothing Then
                    _logger.LogWarning("CompletionService not available for document: {Uri}", uri)
                    Return New CompletionList With {.IsIncomplete = False, .Items = Array.Empty(Of Protocol.CompletionItem)()}
                End If

                Dim completions = Await completionService.GetCompletionsAsync(document, offset, cancellationToken:=cancellationToken).ConfigureAwait(False)

                If completions Is Nothing OrElse completions.ItemsList.Count = 0 Then
                    _logger.LogTrace("No completions returned for: {Uri}", uri)
                    Return New CompletionList With {.IsIncomplete = False, .Items = Array.Empty(Of Protocol.CompletionItem)()}
                End If

                cancellationToken.ThrowIfCancellationRequested()

                Dim items = completions.ItemsList _
                    .Select(Function(item, index) TranslateCompletionItem(item, index, uri, position, sourceText, offset)) _
                    .ToArray()

                _logger.LogDebug("Returning {Count} completion items for: {Uri}", items.Length, uri)

                Return New CompletionList With {
                    .IsIncomplete = False,
                    .Items = items
                }
            Catch ex As OperationCanceledException
                _logger.LogTrace("Completion request cancelled for: {Uri}", uri)
                Throw
            Catch ex As Exception
                _logger.LogError(ex, "Error getting completions for: {Uri}", uri)
                Return New CompletionList With {.IsIncomplete = False, .Items = Array.Empty(Of Protocol.CompletionItem)()}
            End Try
        End Function

        ''' <summary>
        ''' Resolves additional details for a completion item.
        ''' Called when client needs documentation or other expensive details.
        ''' </summary>
        Public Async Function ResolveCompletionItemAsync(item As Protocol.CompletionItem, cancellationToken As CancellationToken) As Task(Of Protocol.CompletionItem)
            If item Is Nothing Then
                Return New Protocol.CompletionItem With {.Label = ""}
            End If

            _logger.LogDebug("Resolving completion item: {Label}", item.Label)

            Dim jsonData As System.Text.Json.JsonElement
            If item.Data Is Nothing OrElse Not TypeOf item.Data Is System.Text.Json.JsonElement Then
                Return item
            End If

            jsonData = DirectCast(item.Data, System.Text.Json.JsonElement)

            Try
                Dim uriElement As System.Text.Json.JsonElement
                Dim displayTextElement As System.Text.Json.JsonElement
                Dim positionElement As System.Text.Json.JsonElement
                If Not jsonData.TryGetProperty("uri", uriElement) OrElse
                   Not jsonData.TryGetProperty("displayText", displayTextElement) OrElse
                   Not jsonData.TryGetProperty("position", positionElement) Then
                    Return item
                End If

                Dim uri = uriElement.GetString()
                Dim displayText = displayTextElement.GetString()
                Dim line = positionElement.GetProperty("line").GetInt32()
                Dim character = positionElement.GetProperty("character").GetInt32()
                Dim filterText As String = Nothing
                Dim sortText As String = Nothing
                Dim index = -1

                Dim filterTextElement As System.Text.Json.JsonElement
                If jsonData.TryGetProperty("filterText", filterTextElement) Then
                    filterText = filterTextElement.GetString()
                End If

                Dim sortTextElement As System.Text.Json.JsonElement
                If jsonData.TryGetProperty("sortText", sortTextElement) Then
                    sortText = sortTextElement.GetString()
                End If

                Dim indexElement As System.Text.Json.JsonElement
                If jsonData.TryGetProperty("index", indexElement) Then
                    index = indexElement.GetInt32()
                End If

                If String.IsNullOrEmpty(uri) OrElse String.IsNullOrEmpty(displayText) Then
                    Return item
                End If

                Dim document = _documentManager.GetRoslynDocument(uri)
                If document Is Nothing Then
                    Return item
                End If

                Dim completionService = RoslynCompletion.CompletionService.GetService(document)
                If completionService Is Nothing Then
                    Return item
                End If

                Dim sourceText = Await document.GetTextAsync(cancellationToken).ConfigureAwait(False)

                Dim offset = GetOffset(New Position(line, character), sourceText)
                Dim completions = Await completionService.GetCompletionsAsync(document, offset, cancellationToken:=cancellationToken).ConfigureAwait(False)

                If completions Is Nothing Then
                    Return item
                End If

                Dim matchingItem = FindMatchingCompletionItem(completions.ItemsList, displayText, filterText, sortText, index)
                If matchingItem Is Nothing Then
                    Return item
                End If

                cancellationToken.ThrowIfCancellationRequested()

                Dim description = Await completionService.GetDescriptionAsync(document, matchingItem, cancellationToken).ConfigureAwait(False)

                If description IsNot Nothing Then
                    Dim docText = String.Join(Environment.NewLine, description.TaggedParts.Select(Function(p) p.Text))
                    If Not String.IsNullOrWhiteSpace(docText) Then
                        item.Documentation = New MarkupContent With {
                            .Kind = "markdown",
                            .Value = FormatDocumentation(description)
                        }
                    End If
                End If

                Dim change = Await completionService.GetChangeAsync(document, matchingItem, cancellationToken:=cancellationToken).ConfigureAwait(False)
                item.TextEdit = CreateTextEdit(change.TextChange, sourceText)

                Dim additionalChanges = GetAdditionalTextChanges(change)
                If additionalChanges IsNot Nothing AndAlso additionalChanges.Count > 0 Then
                    Dim filteredChanges = additionalChanges _
                        .Where(Function(textChange) textChange.Span <> change.TextChange.Span OrElse Not String.Equals(textChange.NewText, change.TextChange.NewText, StringComparison.Ordinal)) _
                        .ToList()

                    If filteredChanges.Count > 0 Then
                        item.AdditionalTextEdits = filteredChanges.Select(Function(textChange) CreateTextEdit(textChange, sourceText)).ToArray()
                    End If
                End If

                item.InsertText = Nothing

                Return item
            Catch ex As OperationCanceledException
                _logger.LogTrace("Completion resolve cancelled for: {Label}", item.Label)
                Throw
            Catch ex As Exception
                _logger.LogError(ex, "Error resolving completion item: {Label}", item.Label)
                Return item
            End Try
        End Function
        ''' <summary>
        ''' Translates a Roslyn CompletionItem to an LSP CompletionItem.
        ''' </summary>
        Private Function TranslateCompletionItem(roslynItem As RoslynCompletion.CompletionItem, index As Integer, uri As String, position As Position, sourceText As SourceText, offset As Integer) As Protocol.CompletionItem
            Dim kind = TranslateCompletionKind(roslynItem.Tags)
            Dim displayText = roslynItem.DisplayText

            Dim sortText = roslynItem.SortText
            If String.IsNullOrEmpty(sortText) Then
                sortText = index.ToString("D5")
            End If

            Dim filterText = roslynItem.FilterText
            If String.IsNullOrEmpty(filterText) Then
                filterText = displayText
            End If

            Dim item = New Protocol.CompletionItem With {
                .Label = displayText,
                .Kind = kind,
                .Detail = GetDetail(roslynItem),
                .TextEdit = CreateDefaultTextEdit(displayText, sourceText, offset, position),
                .InsertTextFormat = InsertTextFormat.PlainText,
                .SortText = sortText,
                .FilterText = filterText,
                .CommitCharacters = GetCommitCharacters(roslynItem),
                .Data = New With {
                    .uri = uri,
                    .displayText = displayText,
                    .filterText = filterText,
                    .sortText = sortText,
                    .index = index,
                    .position = New With {
                        .line = position.Line,
                        .character = position.Character
                    }
                }
            }

            Return item
        End Function

        ''' <summary>
        ''' Translates Roslyn completion tags to LSP CompletionItemKind.
        ''' </summary>
        Private Shared Function TranslateCompletionKind(tags As ImmutableArray(Of String)) As CompletionItemKind
            For Each tag In tags
                Select Case tag
                    Case "Class"
                        Return CompletionItemKind.Class
                    Case "Structure", "Struct"
                        Return CompletionItemKind.Struct
                    Case "Interface"
                        Return CompletionItemKind.Interface
                    Case "Enum"
                        Return CompletionItemKind.Enum
                    Case "EnumMember"
                        Return CompletionItemKind.EnumMember
                    Case "Module"
                        Return CompletionItemKind.Module
                    Case "Method", "ExtensionMethod"
                        Return CompletionItemKind.Method
                    Case "Function"
                        Return CompletionItemKind.Function
                    Case "Property"
                        Return CompletionItemKind.Property
                    Case "Field"
                        Return CompletionItemKind.Field
                    Case "Event"
                        Return CompletionItemKind.Event
                    Case "Constant"
                        Return CompletionItemKind.Constant
                    Case "Local", "Parameter"
                        Return CompletionItemKind.Variable
                    Case "Keyword"
                        Return CompletionItemKind.Keyword
                    Case "Namespace"
                        Return CompletionItemKind.Module
                    Case "TypeParameter"
                        Return CompletionItemKind.TypeParameter
                    Case "Operator"
                        Return CompletionItemKind.Operator
                    Case "Snippet"
                        Return CompletionItemKind.Snippet
                End Select
            Next

            Return CompletionItemKind.Text
        End Function

        ''' <summary>
        ''' Gets detail text for a completion item.
        ''' </summary>
        Private Shared Function GetDetail(item As RoslynCompletion.CompletionItem) As String
            If Not String.IsNullOrEmpty(item.InlineDescription) Then
                Return item.InlineDescription
            End If

            Dim symbolName As String = Nothing
            Dim containingNamespace As String = Nothing
            If item.Properties.TryGetValue("SymbolName", symbolName) AndAlso
               item.Properties.TryGetValue("ContainingNamespace", containingNamespace) AndAlso
               Not String.IsNullOrEmpty(containingNamespace) Then
                Return containingNamespace
            End If

            Return Nothing
        End Function

        ''' <summary>
        ''' Formats completion description as markdown.
        ''' </summary>
        Private Shared Function FormatDocumentation(description As RoslynCompletion.CompletionDescription) As String
            Dim sb As New System.Text.StringBuilder()
            Dim inCode = False

            For Each part In description.TaggedParts
                Select Case part.Tag
                    Case TextTags.Keyword, TextTags.Class, TextTags.Struct, TextTags.Interface, TextTags.Enum, TextTags.Module, TextTags.Method, TextTags.Property, TextTags.Field, TextTags.Local, TextTags.Parameter, TextTags.Namespace, TextTags.TypeParameter
                        If Not inCode Then
                            sb.Append("`"c)
                            inCode = True
                        End If
                        sb.Append(part.Text)
                    Case TextTags.Punctuation, TextTags.Operator
                        sb.Append(part.Text)
                    Case TextTags.LineBreak
                        If inCode Then
                            sb.Append("`"c)
                            inCode = False
                        End If
                        sb.AppendLine()
                        sb.AppendLine()
                    Case Else
                        If inCode Then
                            sb.Append("`"c)
                            inCode = False
                        End If
                        sb.Append(part.Text)
                End Select
            Next

            If inCode Then
                sb.Append("`"c)
            End If

            Return sb.ToString().Trim()
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

        Private Shared Function GetRange(span As TextSpan, sourceText As SourceText) As Protocol.Range
            Dim startLine = sourceText.Lines.GetLineFromPosition(span.Start)
            Dim endLine = sourceText.Lines.GetLineFromPosition(span.[End])

            Return New Protocol.Range With {
                .Start = New Position With {
                    .Line = startLine.LineNumber,
                    .Character = span.Start - startLine.Start
                },
                .[End] = New Position With {
                    .Line = endLine.LineNumber,
                    .Character = span.[End] - endLine.Start
                }
            }
        End Function

        Private Shared Function CreateTextEdit(change As TextChange, sourceText As SourceText) As TextEdit
            Return New TextEdit With {
                .Range = GetRange(change.Span, sourceText),
                .NewText = If(change.NewText, String.Empty)
            }
        End Function

        Private Shared Function CreateDefaultTextEdit(insertText As String, sourceText As SourceText, offset As Integer, position As Position) As TextEdit
            If position.Line >= 0 AndAlso position.Line < sourceText.Lines.Count Then
                Dim textLine = sourceText.Lines(position.Line)
                Dim lineLength = textLine.End - textLine.Start
                If position.Character > lineLength Then
                    Dim fallbackStart = Math.Max(0, position.Character - 1)
                    Return New TextEdit With {
                        .Range = New Protocol.Range With {
                            .Start = New Position With {.Line = position.Line, .Character = fallbackStart},
                            .[End] = New Position With {.Line = position.Line, .Character = position.Character}
                        },
                        .NewText = insertText
                    }
                End If
            End If

            Dim start = offset
            While start > 0
                Dim ch = sourceText(start - 1)
                If Not IsWordCharacter(ch) Then
                    Exit While
                End If
                start -= 1
            End While

            Dim span = TextSpan.FromBounds(start, offset)
            Return New TextEdit With {
                .Range = GetRange(span, sourceText),
                .NewText = insertText
            }
        End Function

        Private Shared Function IsWordCharacter(ch As Char) As Boolean
            Return Char.IsLetterOrDigit(ch) OrElse ch = "_"c
        End Function

        Private Shared Function GetCommitCharacters(item As RoslynCompletion.CompletionItem) As String()
            Dim rules = item.Rules.CommitCharacterRules
            If rules.IsDefaultOrEmpty Then
                Return DefaultCommitCharacters
            End If

            Dim commitCharacters As New List(Of String)(DefaultCommitCharacters)

            For Each rule In rules
                Select Case rule.Kind
                    Case RoslynCompletion.CharacterSetModificationKind.Add
                        For Each character In rule.Characters
                            Dim value = character.ToString()
                            If Not commitCharacters.Contains(value) Then
                                commitCharacters.Add(value)
                            End If
                        Next
                    Case RoslynCompletion.CharacterSetModificationKind.Remove
                        For Each character In rule.Characters
                            commitCharacters.Remove(character.ToString())
                        Next
                    Case RoslynCompletion.CharacterSetModificationKind.Replace
                        commitCharacters = rule.Characters.Select(Function(character) character.ToString()).ToList()
                End Select
            Next

            Return If(commitCharacters.Count = 0, Nothing, commitCharacters.ToArray())
        End Function

        Private Shared Function FindMatchingCompletionItem(items As IReadOnlyList(Of RoslynCompletion.CompletionItem), displayText As String, filterText As String, sortText As String, index As Integer) As RoslynCompletion.CompletionItem
            If String.IsNullOrEmpty(displayText) Then
                Return Nothing
            End If

            If index >= 0 AndAlso index < items.Count Then
                Dim indexedItem = items(index)
                If IsMatch(indexedItem, displayText, filterText, sortText) Then
                    Return indexedItem
                End If
            End If

            Dim match = items.FirstOrDefault(Function(item) IsMatch(item, displayText, filterText, sortText))
            If match IsNot Nothing Then
                Return match
            End If

            Return items.FirstOrDefault(Function(item) item.DisplayText = displayText)
        End Function

        Private Shared Function IsMatch(item As RoslynCompletion.CompletionItem, displayText As String, filterText As String, sortText As String) As Boolean
            If Not String.Equals(item.DisplayText, displayText, StringComparison.Ordinal) Then
                Return False
            End If

            If Not String.IsNullOrEmpty(filterText) AndAlso Not String.Equals(item.FilterText, filterText, StringComparison.Ordinal) Then
                Return False
            End If

            If Not String.IsNullOrEmpty(sortText) AndAlso Not String.Equals(item.SortText, sortText, StringComparison.Ordinal) Then
                Return False
            End If

            Return True
        End Function

        Private Shared Function GetAdditionalTextChanges(change As Object) As IReadOnlyList(Of TextChange)
            Dim changeType = change.GetType()
            Dim [property] = changeType.GetProperty("AdditionalTextChanges")
            If [property] Is Nothing Then
                [property] = changeType.GetProperty("TextChanges")
            End If

            If [property] Is Nothing Then
                Return Nothing
            End If

            Dim value = [property].GetValue(change)
            If TypeOf value Is IReadOnlyList(Of TextChange) Then
                Return DirectCast(value, IReadOnlyList(Of TextChange))
            End If

            If TypeOf value Is IEnumerable(Of TextChange) Then
                Return DirectCast(value, IEnumerable(Of TextChange)).ToList()
            End If

            Return Nothing
        End Function
    End Class

End Namespace
