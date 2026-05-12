' CodeActionsService - Provides basic and refactor code actions via LSP
' Services Layer as defined in docs/architecture.md Section 5.4

Imports System.Collections
Imports System.Text.Json
Imports System.Text.Json.Serialization
Imports Microsoft.CodeAnalysis
Imports Microsoft.CodeAnalysis.Text
Imports Microsoft.Extensions.Logging
Imports VbNet.LanguageServer.Protocol
Imports VbNet.LanguageServer.Workspace
Imports RoslynApplyChangesOperation = Microsoft.CodeAnalysis.CodeActions.ApplyChangesOperation
Imports RoslynCodeAction = Microsoft.CodeAnalysis.CodeActions.CodeAction
Imports RoslynCodeActionOperation = Microsoft.CodeAnalysis.CodeActions.CodeActionOperation

Namespace Services

    ''' <summary>
    ''' Provides source and refactor code actions for VB.NET documents.
    ''' </summary>
    Public NotInheritable Class CodeActionsService
        Private ReadOnly _workspaceManager As WorkspaceManager
        Private ReadOnly _documentManager As DocumentManager
        Private ReadOnly _logger As ILogger(Of CodeActionsService)

        Private Shared ReadOnly SupportedKinds As String() = {
            CodeActionKind.Source,
            CodeActionKind.Refactor,
            CodeActionKind.RefactorExtract
        }

        Private Const ResolvePayloadVersion As Integer = 1
        Private Const ActionTypeOption As String = "option"
        Private Const ActionTypeExtract As String = "extract"
        Private Const ExtractStrategyRoslyn As String = "roslyn"
        Private Const ExtractStrategySimple As String = "simple"

        Public Sub New(workspaceManager As WorkspaceManager, documentManager As DocumentManager, logger As ILogger(Of CodeActionsService))
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

        Public Shared Function GetDefaultOptions() As CodeActionOptions
            Return New CodeActionOptions With {
                .CodeActionKinds = SupportedKinds,
                .ResolveProvider = True
            }
        End Function

        Public Async Function GetCodeActionsAsync(parameters As CodeActionParams, cancellationToken As CancellationToken) As Task(Of CodeAction())
            If parameters Is Nothing OrElse parameters.TextDocument Is Nothing Then
                Return Array.Empty(Of CodeAction)()
            End If

            Dim uri = parameters.TextDocument.Uri
            Dim sourceText = Await GetSourceTextAsync(uri, cancellationToken).ConfigureAwait(False)
            If sourceText Is Nothing Then
                _logger.LogTrace("No document available for code actions: {Uri}", uri)
                Return Array.Empty(Of CodeAction)()
            End If

            cancellationToken.ThrowIfCancellationRequested()

            Dim actions As New List(Of CodeAction)()
            actions.AddRange(GetOptionActions(uri, sourceText))
            actions.AddRange(Await GetExtractActionsAsync(parameters, sourceText, cancellationToken).ConfigureAwait(False))

            Return actions.ToArray()
        End Function

        Public Async Function ResolveCodeActionAsync(action As CodeAction, cancellationToken As CancellationToken) As Task(Of CodeAction)
            If action Is Nothing Then
                Return Nothing
            End If

            Dim data = ParseResolveData(action.Data)
            If data Is Nothing Then
                Return action
            End If

            If StringComparer.Ordinal.Equals(data.ActionType, ActionTypeOption) Then
                action.Edit = BuildOptionEdit(data.Uri, data.InsertionLine.GetValueOrDefault(), data.OptionText)
                Return action
            End If

            If StringComparer.Ordinal.Equals(data.ActionType, ActionTypeExtract) Then
                Return Await ResolveExtractActionAsync(action, data, cancellationToken).ConfigureAwait(False)
            End If

            Return action
        End Function

        Private Async Function GetSourceTextAsync(uri As String, cancellationToken As CancellationToken) As Task(Of SourceText)
            Dim openDoc = _documentManager.GetOpenDocument(uri)
            If openDoc IsNot Nothing AndAlso openDoc.Text IsNot Nothing Then
                Return openDoc.Text
            End If

            Dim document = _documentManager.GetRoslynDocument(uri)
            If document Is Nothing Then
                Return Nothing
            End If

            Return Await document.GetTextAsync(cancellationToken).ConfigureAwait(False)
        End Function

        Private Function GetOptionActions(uri As String, sourceText As SourceText) As IEnumerable(Of CodeAction)
            Dim actions As New List(Of CodeAction)()
            Dim insertionLine = GetInsertionLine(sourceText)

            If Not ContainsOptionLine(sourceText, "Option Strict") Then
                actions.Add(BuildOptionAction(uri, insertionLine, "Option Strict On"))
            End If

            If Not ContainsOptionLine(sourceText, "Option Explicit") Then
                actions.Add(BuildOptionAction(uri, insertionLine, "Option Explicit On"))
            End If

            If Not ContainsOptionLine(sourceText, "Option Infer") Then
                actions.Add(BuildOptionAction(uri, insertionLine, "Option Infer On"))
            End If

            Return actions
        End Function

        Private Async Function GetExtractActionsAsync(parameters As CodeActionParams, sourceText As SourceText, cancellationToken As CancellationToken) As Task(Of IEnumerable(Of CodeAction))
            Dim uri = parameters.TextDocument.Uri
            Dim document = _documentManager.GetRoslynDocument(uri)
            If document Is Nothing Then
                _logger.LogTrace("Extract skipped: no Roslyn document for {Uri}", uri)
                Return Array.Empty(Of CodeAction)()
            End If

            Dim selection = TryGetTextSpan(parameters.Range, sourceText)
            If selection Is Nothing OrElse selection.Value.Length = 0 Then
                _logger.LogTrace("Extract skipped: zero-length selection for {Uri}", uri)
                Return Array.Empty(Of CodeAction)()
            End If

            _logger.LogTrace("Extract discovery start: {Uri}, span=[{Start},{End}]", uri, selection.Value.Start, selection.Value.End)
            Dim discovered = Await DiscoverExtractRoslynActionsAsync(document, parameters.Range, selection.Value, cancellationToken).ConfigureAwait(False)
            _logger.LogTrace("Extract discovery complete: {Count} Roslyn actions for {Uri}", discovered.Count, uri)
            If discovered.Count > 0 Then
                Return discovered.Select(Function(x) New CodeAction With {
                    .Title = x.Title,
                    .Kind = CodeActionKind.RefactorExtract,
                    .Data = New CodeActionResolveData With {
                        .PayloadVersion = ResolvePayloadVersion,
                        .ActionType = ActionTypeExtract,
                        .Strategy = ExtractStrategyRoslyn,
                        .Uri = uri,
                        .StartLine = parameters.Range.Start.Line,
                        .StartCharacter = parameters.Range.Start.Character,
                        .EndLine = parameters.Range.End.Line,
                        .EndCharacter = parameters.Range.End.Character,
                        .ActionPath = x.Path
                    }
                })
            End If

            If Not CanApplySimpleExtract(parameters.Range, sourceText) Then
                _logger.LogTrace("Extract skipped: simple strategy not applicable for {Uri}", uri)
                Return Array.Empty(Of CodeAction)()
            End If

            _logger.LogTrace("Extract: falling back to simple strategy for {Uri}", uri)
            Return {New CodeAction With {
                .Title = "Extract Method",
                .Kind = CodeActionKind.RefactorExtract,
                .Data = New CodeActionResolveData With {
                    .PayloadVersion = ResolvePayloadVersion,
                    .ActionType = ActionTypeExtract,
                    .Strategy = ExtractStrategySimple,
                    .Uri = uri,
                    .StartLine = parameters.Range.Start.Line,
                    .StartCharacter = parameters.Range.Start.Character,
                    .EndLine = parameters.Range.End.Line,
                    .EndCharacter = parameters.Range.End.Character,
                    .ActionPath = New String() {"Extract Method"}
                }
            }}
        End Function

        Private Async Function ResolveExtractActionAsync(action As CodeAction, data As CodeActionResolveData, cancellationToken As CancellationToken) As Task(Of CodeAction)
            _logger.LogTrace("Resolving extract action: strategy={Strategy} for {Uri}", data.Strategy, data.Uri)
            Dim document = _documentManager.GetRoslynDocument(data.Uri)
            If document Is Nothing Then
                _logger.LogTrace("Resolve extract miss: no Roslyn document for {Uri}", data.Uri)
                Return action
            End If

            Dim sourceText = Await document.GetTextAsync(cancellationToken).ConfigureAwait(False)
            Dim range = New Protocol.Range With {
                .Start = New Position(data.StartLine.GetValueOrDefault(), data.StartCharacter.GetValueOrDefault()),
                .End = New Position(data.EndLine.GetValueOrDefault(), data.EndCharacter.GetValueOrDefault())
            }

            Dim selection = TryGetTextSpan(range, sourceText)
            If selection Is Nothing OrElse selection.Value.Length = 0 Then
                _logger.LogTrace("Resolve extract miss: selection span is empty for {Uri}", data.Uri)
                Return action
            End If

            If StringComparer.Ordinal.Equals(data.Strategy, ExtractStrategySimple) Then
                _logger.LogTrace("Resolve extract: applying simple strategy for {Uri}", data.Uri)
                Dim simpleEdit = BuildSimpleExtractEdit(data, sourceText)
                If simpleEdit IsNot Nothing Then
                    action.Edit = simpleEdit
                End If
                Return action
            End If

            If data.ActionPath Is Nothing OrElse data.ActionPath.Length = 0 Then
                Return action
            End If

            Dim discovered = Await DiscoverExtractRoslynActionsAsync(document, range, selection.Value, cancellationToken).ConfigureAwait(False)
            Dim selected = discovered.FirstOrDefault(Function(x) PathsEqual(x.Path, data.ActionPath)).RoslynAction
            If selected Is Nothing Then
                _logger.LogTrace("Extract action no longer available for {Uri}", data.Uri)
                Return action
            End If

            Dim operations = Await selected.GetOperationsAsync(document.Project.Solution, progress:=Nothing, cancellationToken).ConfigureAwait(False)
            action.Edit = Await BuildWorkspaceEditFromOperationsAsync(document.Project.Solution, operations, cancellationToken).ConfigureAwait(False)
            Return action
        End Function

        Private Shared Function CanApplySimpleExtract([range] As Protocol.Range, sourceText As SourceText) As Boolean
            If [range] Is Nothing OrElse sourceText Is Nothing Then
                Return False
            End If

            If [range].Start.Line < 0 OrElse [range].End.Line < [range].Start.Line Then
                Return False
            End If

            If [range].End.Line >= sourceText.Lines.Count Then
                Return False
            End If

            Return FindEnclosingEndSubLine([range].End.Line, sourceText) >= 0
        End Function

        Private Shared Function BuildSimpleExtractEdit(data As CodeActionResolveData, sourceText As SourceText) As WorkspaceEdit
            Dim source = sourceText.ToString()
            Dim [range] = New Protocol.Range With {
                .Start = New Position(data.StartLine.GetValueOrDefault(), data.StartCharacter.GetValueOrDefault()),
                .End = New Position(data.EndLine.GetValueOrDefault(), data.EndCharacter.GetValueOrDefault())
            }

            Dim selection = TryGetTextSpan([range], sourceText)
            If selection Is Nothing OrElse selection.Value.Length = 0 Then
                Return Nothing
            End If

            Dim span = selection.Value
            Dim startLine = sourceText.Lines(data.StartLine.GetValueOrDefault())
            Dim startLineText = startLine.ToString()
            Dim statementIndentSize = startLineText.Length - startLineText.TrimStart().Length
            Dim statementIndent = New String(" "c, Math.Max(statementIndentSize, 0))
            Dim methodIndent = New String(" "c, Math.Max(statementIndentSize - 4, 0))

            Dim callText = statementIndent & "ExtractedMethod()" & Environment.NewLine
            Dim selectedText = source.Substring(span.Start, span.Length).TrimEnd(ControlChars.Cr, ControlChars.Lf)
            Dim replaced = source.Substring(0, span.Start) & callText & source.Substring(span.End)

            Dim endSubLine = FindEnclosingEndSubLine(data.EndLine.GetValueOrDefault(), sourceText)
            If endSubLine < 0 Then
                Return Nothing
            End If

            Dim endSubPosition = sourceText.Lines(endSubLine).Start
            Dim delta = callText.Length - span.Length
            Dim insertionPosition = Math.Max(0, Math.Min(replaced.Length, endSubPosition + delta))

            Dim extractedMethodText =
                Environment.NewLine &
                methodIndent & "Private Sub ExtractedMethod()" & Environment.NewLine &
                selectedText & Environment.NewLine &
                methodIndent & "End Sub" & Environment.NewLine

            Dim finalText = replaced.Insert(insertionPosition, extractedMethodText)
            Dim lastLine = sourceText.Lines(sourceText.Lines.Count - 1)

            Return New WorkspaceEdit With {
                .Changes = New Dictionary(Of String, TextEdit()) From {
                    {data.Uri, New TextEdit() {
                        New TextEdit With {
                            .Range = New Protocol.Range With {
                                .Start = New Position(0, 0),
                                .[End] = New Position(lastLine.LineNumber, lastLine.Span.Length)
                            },
                            .NewText = finalText
                        }
                    }}
                }
            }
        End Function

        Private Shared Function FindEnclosingEndSubLine(startLine As Integer, sourceText As SourceText) As Integer
            For lineIndex = Math.Max(0, startLine) To sourceText.Lines.Count - 1
                Dim trimmed = sourceText.Lines(lineIndex).ToString().Trim()
                If trimmed.StartsWith("End Sub", StringComparison.OrdinalIgnoreCase) OrElse
                    trimmed.StartsWith("End Function", StringComparison.OrdinalIgnoreCase) Then
                    Return lineIndex
                End If
            Next

            Return -1
        End Function

        Private Async Function DiscoverExtractRoslynActionsAsync(document As Document, selectionRange As Protocol.Range, selection As TextSpan, cancellationToken As CancellationToken) As Task(Of List(Of (Title As String, Path As String(), RoslynAction As RoslynCodeAction)))
            Dim results As New List(Of (Title As String, Path As String(), RoslynAction As RoslynCodeAction))()
            Dim codeRefactorings = Await GetCodeRefactoringsAsync(document, selection, cancellationToken).ConfigureAwait(False)
            If codeRefactorings Is Nothing Then
                Return results
            End If

            For Each codeRefactoring In codeRefactorings
                Dim codeActionsValue = codeRefactoring.GetType().GetProperty("CodeActions", Reflection.BindingFlags.Instance Or Reflection.BindingFlags.Public Or Reflection.BindingFlags.NonPublic)
                If codeActionsValue Is Nothing Then
                    Continue For
                End If

                Dim codeActionEntries = TryCast(codeActionsValue.GetValue(codeRefactoring), IEnumerable)
                If codeActionEntries Is Nothing Then
                    Continue For
                End If

                For Each entry In codeActionEntries
                    Dim roslynAction = ExtractRoslynCodeActionFromTuple(entry)
                    If roslynAction Is Nothing Then
                        Continue For
                    End If

                    For Each flattened In FlattenActions(roslynAction, Array.Empty(Of String)())
                        If IsExtractAction(flattened.Action) AndAlso Not IsUiOptionRequired(flattened.Action) Then
                            results.Add((flattened.Action.Title, flattened.Path, flattened.Action))
                        End If
                    Next
                Next
            Next

            Return results
        End Function

        Private Shared Iterator Function FlattenActions(action As RoslynCodeAction, parentPath As String()) As IEnumerable(Of (Action As RoslynCodeAction, Path As String()))
            Dim path = parentPath.Concat({action.Title}).ToArray()
            If action.NestedActions.IsDefaultOrEmpty Then
                Yield (action, path)
                Return
            End If

            For Each nested In action.NestedActions
                For Each flattened In FlattenActions(nested, path)
                    Yield flattened
                Next
            Next
        End Function

        Private Shared Function IsExtractAction(action As RoslynCodeAction) As Boolean
            If action Is Nothing Then
                Return False
            End If

            If action.Title.IndexOf("Extract Method", StringComparison.OrdinalIgnoreCase) >= 0 Then
                Return True
            End If

            If action.Title.IndexOf("Extract method", StringComparison.OrdinalIgnoreCase) >= 0 Then
                Return True
            End If

            Return False
        End Function

        Private Shared Function IsUiOptionRequired(action As RoslynCodeAction) As Boolean
            If action Is Nothing Then
                Return False
            End If

            Dim t = action.GetType()
            Do While t IsNot Nothing
                If t.Name.IndexOf("WithOptions", StringComparison.OrdinalIgnoreCase) >= 0 Then
                    Return True
                End If
                t = t.BaseType
            Loop

            Return False
        End Function

        Private Shared Function PathsEqual(left As String(), right As String()) As Boolean
            If left Is Nothing OrElse right Is Nothing OrElse left.Length <> right.Length Then
                Return False
            End If

            For i = 0 To left.Length - 1
                If Not StringComparer.Ordinal.Equals(left(i), right(i)) Then
                    Return False
                End If
            Next

            Return True
        End Function

        Private Async Function GetCodeRefactoringsAsync(document As Document, selection As TextSpan, cancellationToken As CancellationToken) As Task(Of IEnumerable)
            Dim featureAssembly = GetType(Microsoft.CodeAnalysis.Completion.CompletionService).Assembly
            Dim serviceType = featureAssembly.GetType("Microsoft.CodeAnalysis.CodeRefactorings.ICodeRefactoringService", throwOnError:=False)
            If serviceType Is Nothing Then
                _logger.LogTrace("ICodeRefactoringService is not available in current host.")
                Return Nothing
            End If

            Dim projectServices = document.Project.Services
            Dim getServiceMethod = projectServices.GetType().GetMethod(
                "GetService",
                Reflection.BindingFlags.Public Or Reflection.BindingFlags.NonPublic Or Reflection.BindingFlags.Instance,
                binder:=Nothing,
                types:={GetType(Type)},
                modifiers:=Nothing)
            If getServiceMethod Is Nothing Then
                Return Nothing
            End If

            Dim service = getServiceMethod.Invoke(projectServices, {serviceType})
            If service Is Nothing Then
                _logger.LogTrace("Project services did not return ICodeRefactoringService for {Language}.", document.Project.Language)
                Return Nothing
            End If

            Dim getRefactoringsMethod = serviceType.GetMethods(Reflection.BindingFlags.Instance Or Reflection.BindingFlags.Public Or Reflection.BindingFlags.NonPublic).
                FirstOrDefault(Function(m) m.Name = "GetRefactoringsAsync" AndAlso m.GetParameters().Length = 4)
            If getRefactoringsMethod Is Nothing Then
                _logger.LogTrace("ICodeRefactoringService.GetRefactoringsAsync was not found.")
                Return Nothing
            End If

            Dim task = DirectCast(getRefactoringsMethod.Invoke(service, {document, selection, Nothing, cancellationToken}), Task)
            Await task.ConfigureAwait(False)

            Dim resultProperty = task.GetType().GetProperty("Result", Reflection.BindingFlags.Instance Or Reflection.BindingFlags.Public)
            If resultProperty Is Nothing Then
                Return Nothing
            End If

            Return TryCast(resultProperty.GetValue(task), IEnumerable)
        End Function

        Private Shared Function ExtractRoslynCodeActionFromTuple(entry As Object) As RoslynCodeAction
            If entry Is Nothing Then
                Return Nothing
            End If

            Dim entryType = entry.GetType()
            Dim item1Field = entryType.GetField("Item1", Reflection.BindingFlags.Instance Or Reflection.BindingFlags.Public)
            If item1Field IsNot Nothing Then
                Return TryCast(item1Field.GetValue(entry), RoslynCodeAction)
            End If

            Dim item1Property = entryType.GetProperty("Item1", Reflection.BindingFlags.Instance Or Reflection.BindingFlags.Public)
            If item1Property IsNot Nothing Then
                Return TryCast(item1Property.GetValue(entry), RoslynCodeAction)
            End If

            Return Nothing
        End Function

        Private Shared Async Function BuildWorkspaceEditFromOperationsAsync(originalSolution As Solution, operations As IEnumerable(Of RoslynCodeActionOperation), cancellationToken As CancellationToken) As Task(Of WorkspaceEdit)
            Dim changesByUri As New Dictionary(Of String, List(Of TextEdit))(StringComparer.OrdinalIgnoreCase)
            Dim seenFilePaths As New HashSet(Of String)(StringComparer.OrdinalIgnoreCase)

            For Each operation In operations
                Dim applyChanges = TryCast(operation, RoslynApplyChangesOperation)
                If applyChanges Is Nothing Then
                    Continue For
                End If

                Dim changedSolution = applyChanges.ChangedSolution
                Dim solutionChanges = changedSolution.GetChanges(originalSolution)

                For Each projectChange In solutionChanges.GetProjectChanges()
                    For Each documentId In projectChange.GetChangedDocuments()
                        Dim oldDocument = originalSolution.GetDocument(documentId)
                        Dim newDocument = changedSolution.GetDocument(documentId)
                        If oldDocument Is Nothing OrElse newDocument Is Nothing OrElse String.IsNullOrWhiteSpace(newDocument.FilePath) Then
                            Continue For
                        End If

                        ' Deduplicate linked documents: only process each file path once
                        If Not seenFilePaths.Add(newDocument.FilePath) Then
                            Continue For
                        End If

                        Dim oldText = Await oldDocument.GetTextAsync(cancellationToken).ConfigureAwait(False)
                        Dim newText = Await newDocument.GetTextAsync(cancellationToken).ConfigureAwait(False)
                        Dim textChanges = newText.GetTextChanges(oldText)
                        If textChanges.Count = 0 Then
                            Continue For
                        End If

                        Dim uri = New Uri(newDocument.FilePath).ToString()
                        Dim edits = GetOrCreateEdits(changesByUri, uri)
                        For Each textChange In textChanges
                            edits.Add(ToTextEdit(textChange, oldText))
                        Next
                    Next
                Next
            Next

            Return New WorkspaceEdit With {
                .Changes = changesByUri.ToDictionary(Function(kvp) kvp.Key, Function(kvp) kvp.Value.ToArray())
            }
        End Function

        Private Shared Function GetOrCreateEdits(changesByUri As Dictionary(Of String, List(Of TextEdit)), uri As String) As List(Of TextEdit)
            Dim edits As List(Of TextEdit) = Nothing
            If changesByUri.TryGetValue(uri, edits) Then
                Return edits
            End If

            edits = New List(Of TextEdit)()
            changesByUri(uri) = edits
            Return edits
        End Function

        Private Shared Function ToTextEdit(textChange As TextChange, originalText As SourceText) As TextEdit
            Dim startLine = originalText.Lines.GetLineFromPosition(textChange.Span.Start)
            Dim endLine = originalText.Lines.GetLineFromPosition(textChange.Span.End)

            Return New TextEdit With {
                .Range = New Protocol.Range With {
                    .Start = New Position(startLine.LineNumber, textChange.Span.Start - startLine.Start),
                    .[End] = New Position(endLine.LineNumber, textChange.Span.End - endLine.Start)
                },
                .NewText = textChange.NewText
            }
        End Function

        Private Shared Function TryGetTextSpan([range] As Protocol.Range, sourceText As SourceText) As TextSpan?
            If [range] Is Nothing Then
                Return Nothing
            End If

            If [range].Start.Line < 0 OrElse [range].End.Line < 0 Then
                Return Nothing
            End If

            If [range].Start.Line >= sourceText.Lines.Count OrElse [range].End.Line >= sourceText.Lines.Count Then
                Return Nothing
            End If

            Dim startLine = sourceText.Lines([range].Start.Line)
            Dim endLine = sourceText.Lines([range].End.Line)
            Dim startPosition = startLine.Start + Math.Max(0, [range].Start.Character)
            Dim endPosition = endLine.Start + Math.Max(0, [range].End.Character)

            startPosition = Math.Min(startPosition, sourceText.Length)
            endPosition = Math.Min(endPosition, sourceText.Length)
            If endPosition < startPosition Then
                Return Nothing
            End If

            Return TextSpan.FromBounds(startPosition, endPosition)
        End Function

        Private Shared Function ContainsOptionLine(sourceText As SourceText, optionPrefix As String) As Boolean
            For Each line In sourceText.Lines
                Dim text = line.ToString().TrimStart()
                If text.StartsWith(optionPrefix, StringComparison.OrdinalIgnoreCase) Then
                    Return True
                End If
            Next

            Return False
        End Function

        Private Shared Function GetInsertionLine(sourceText As SourceText) As Integer
            Dim insertionLine = 0
            For Each line In sourceText.Lines
                Dim trimmed = line.ToString().TrimStart()
                If trimmed.Length = 0 OrElse trimmed.StartsWith("'", StringComparison.Ordinal) Then
                    insertionLine = line.LineNumber + 1
                    Continue For
                End If

                If trimmed.StartsWith("Option ", StringComparison.OrdinalIgnoreCase) Then
                    insertionLine = line.LineNumber + 1
                    Continue For
                End If

                Exit For
            Next

            Return Math.Min(insertionLine, sourceText.Lines.Count)
        End Function

        Private Shared Function BuildOptionAction(uri As String, insertionLine As Integer, optionText As String) As CodeAction
            Return New CodeAction With {
                .Title = $"Add {optionText}",
                .Kind = CodeActionKind.Source,
                .IsPreferred = True,
                .Data = New CodeActionResolveData With {
                    .PayloadVersion = ResolvePayloadVersion,
                    .ActionType = ActionTypeOption,
                    .Uri = uri,
                    .InsertionLine = insertionLine,
                    .OptionText = optionText
                }
            }
        End Function

        Private Shared Function BuildOptionEdit(uri As String, insertionLine As Integer, optionText As String) As WorkspaceEdit
            Dim newLine = Environment.NewLine
            Return New WorkspaceEdit With {
                .Changes = New Dictionary(Of String, TextEdit()) From {
                    {uri, New TextEdit() {
                        New TextEdit With {
                            .Range = New Protocol.Range With {
                                .Start = New Position(insertionLine, 0),
                                .[End] = New Position(insertionLine, 0)
                            },
                            .NewText = optionText & newLine
                        }
                    }}
                }
            }
        End Function

        Private Shared Function ParseResolveData(data As Object) As CodeActionResolveData
            If data Is Nothing Then
                Return Nothing
            End If

            Dim resolved = TryCast(data, CodeActionResolveData)
            If resolved IsNot Nothing Then
                Return NormalizeResolveData(resolved)
            End If

            If TypeOf data Is JsonElement Then
                Dim parsed = ParseResolveDataElement(DirectCast(data, JsonElement))
                Return NormalizeResolveData(parsed)
            End If

            Return Nothing
        End Function

        Private Shared Function ParseResolveDataElement(element As JsonElement) As CodeActionResolveData
            If element.ValueKind <> JsonValueKind.Object Then
                Return Nothing
            End If

            Dim result As New CodeActionResolveData()
            result.ActionType = ReadString(element, "actionType")
            result.Uri = ReadString(element, "uri")
            result.OptionText = ReadString(element, "optionText")
            result.InsertionLine = ReadNullableInt(element, "insertionLine")
            result.PayloadVersion = ReadNullableInt(element, "payloadVersion")
            result.Strategy = ReadString(element, "strategy")
            result.StartLine = ReadNullableInt(element, "startLine")
            result.StartCharacter = ReadNullableInt(element, "startCharacter")
            result.EndLine = ReadNullableInt(element, "endLine")
            result.EndCharacter = ReadNullableInt(element, "endCharacter")
            result.ActionPath = ReadStringArray(element, "actionPath")
            Return result
        End Function

        Private Shared Function NormalizeResolveData(data As CodeActionResolveData) As CodeActionResolveData
            If data Is Nothing Then
                Return Nothing
            End If

            If String.IsNullOrWhiteSpace(data.ActionType) Then
                If data.InsertionLine.HasValue AndAlso Not String.IsNullOrWhiteSpace(data.OptionText) Then
                    data.ActionType = ActionTypeOption
                End If
            End If

            Return data
        End Function

        Private Shared Function ReadString(element As JsonElement, propertyName As String) As String
            Dim propertyValue As JsonElement
            If element.TryGetProperty(propertyName, propertyValue) AndAlso propertyValue.ValueKind = JsonValueKind.String Then
                Return propertyValue.GetString()
            End If

            Return Nothing
        End Function

        Private Shared Function ReadNullableInt(element As JsonElement, propertyName As String) As Integer?
            Dim propertyValue As JsonElement
            If element.TryGetProperty(propertyName, propertyValue) AndAlso propertyValue.ValueKind = JsonValueKind.Number Then
                Return propertyValue.GetInt32()
            End If

            Return Nothing
        End Function

        Private Shared Function ReadStringArray(element As JsonElement, propertyName As String) As String()
            Dim propertyValue As JsonElement
            If Not element.TryGetProperty(propertyName, propertyValue) OrElse propertyValue.ValueKind <> JsonValueKind.Array Then
                Return Nothing
            End If

            Dim values As New List(Of String)()
            For Each item In propertyValue.EnumerateArray()
                If item.ValueKind = JsonValueKind.String Then
                    values.Add(item.GetString())
                End If
            Next

            Return values.ToArray()
        End Function

        Private NotInheritable Class CodeActionResolveData
            <JsonPropertyName("payloadVersion")>
            Public Property PayloadVersion As Integer?

            <JsonPropertyName("actionType")>
            Public Property ActionType As String

            <JsonPropertyName("uri")>
            Public Property Uri As String

            <JsonPropertyName("strategy")>
            Public Property Strategy As String

            <JsonPropertyName("insertionLine")>
            Public Property InsertionLine As Integer?

            <JsonPropertyName("optionText")>
            Public Property OptionText As String

            <JsonPropertyName("startLine")>
            Public Property StartLine As Integer?

            <JsonPropertyName("startCharacter")>
            Public Property StartCharacter As Integer?

            <JsonPropertyName("endLine")>
            Public Property EndLine As Integer?

            <JsonPropertyName("endCharacter")>
            Public Property EndCharacter As Integer?

            <JsonPropertyName("actionPath")>
            Public Property ActionPath As String()
        End Class
    End Class

End Namespace
