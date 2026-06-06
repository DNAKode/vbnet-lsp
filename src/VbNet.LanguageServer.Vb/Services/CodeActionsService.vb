' CodeActionsService - Provides source and refactor code actions via LSP
' Services Layer as defined in docs/architecture.md Section 5.4

Imports System.Text.Json
Imports System.Text.Json.Serialization
Imports Microsoft.CodeAnalysis
Imports Microsoft.CodeAnalysis.CodeRefactorings
Imports Microsoft.CodeAnalysis.Text
Imports Microsoft.Extensions.Logging
Imports VbNet.LanguageServer.Protocol
Imports VbNet.LanguageServer.Workspace
Imports RoslynApplyChangesOperation = Microsoft.CodeAnalysis.CodeActions.ApplyChangesOperation
Imports RoslynCodeAction = Microsoft.CodeAnalysis.CodeActions.CodeAction
Imports RoslynCodeActionOperation = Microsoft.CodeAnalysis.CodeActions.CodeActionOperation

Namespace Services

    ''' <summary>
    ''' Provides source actions and Roslyn-backed refactor actions for VB.NET documents.
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
        Private Const ExtractMethodProviderTypeName As String = "Microsoft.CodeAnalysis.CodeRefactorings.ExtractMethod.ExtractMethodCodeRefactoringProvider"

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
            If ShouldIncludeKind(parameters.Context, CodeActionKind.Source) Then
                actions.AddRange(GetOptionActions(uri, sourceText))
            End If

            If ShouldIncludeKind(parameters.Context, CodeActionKind.RefactorExtract) Then
                actions.AddRange(Await GetExtractActionsAsync(parameters, sourceText, cancellationToken).ConfigureAwait(False))
            End If

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

            If StringComparer.Ordinal.Equals(data.ActionType, ActionTypeExtract) Then
                Return Await ResolveExtractActionAsync(action, data, cancellationToken).ConfigureAwait(False)
            End If

            If StringComparer.Ordinal.Equals(data.ActionType, ActionTypeOption) Then
                If String.IsNullOrWhiteSpace(data.Uri) OrElse
                   Not data.InsertionLine.HasValue OrElse
                   String.IsNullOrWhiteSpace(data.OptionText) Then
                    Return action
                End If

                action.Edit = BuildOptionEdit(data.Uri, data.InsertionLine.Value, data.OptionText)
                Return action
            End If

            Return action
        End Function

        Private Async Function GetSourceTextAsync(uri As String, cancellationToken As CancellationToken) As Task(Of SourceText)
            Dim openDoc = _documentManager.GetOpenDocument(uri)
            If openDoc?.Text IsNot Nothing Then
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
                _logger.LogTrace("Extract Method unavailable: no Roslyn document for {Uri}", uri)
                Return Array.Empty(Of CodeAction)()
            End If

            Dim selection = TryGetTextSpan(parameters.Range, sourceText)
            If Not selection.HasValue OrElse selection.Value.Length = 0 Then
                _logger.LogTrace("Extract Method unavailable: invalid or empty range for {Uri}", uri)
                Return Array.Empty(Of CodeAction)()
            End If

            Dim discovered = Await DiscoverExtractRoslynActionsAsync(document, selection.Value, cancellationToken).ConfigureAwait(False)
            If discovered.Count = 0 Then
                _logger.LogTrace("Extract Method unavailable: Roslyn returned no extract actions for {Uri}", uri)
                Return Array.Empty(Of CodeAction)()
            End If

            Return discovered.Select(Function(item) New CodeAction With {
                .Title = item.Title,
                .Kind = CodeActionKind.RefactorExtract,
                .Data = New CodeActionResolveData With {
                    .PayloadVersion = ResolvePayloadVersion,
                    .ActionType = ActionTypeExtract,
                    .Uri = uri,
                    .StartLine = parameters.Range.Start.Line,
                    .StartCharacter = parameters.Range.Start.Character,
                    .EndLine = parameters.Range.End.Line,
                    .EndCharacter = parameters.Range.End.Character,
                    .ActionPath = item.Path
                }
            }).ToArray()
        End Function

        Private Async Function ResolveExtractActionAsync(action As CodeAction, data As CodeActionResolveData, cancellationToken As CancellationToken) As Task(Of CodeAction)
            If String.IsNullOrWhiteSpace(data.Uri) OrElse
               Not data.StartLine.HasValue OrElse Not data.StartCharacter.HasValue OrElse
               Not data.EndLine.HasValue OrElse Not data.EndCharacter.HasValue OrElse
               data.ActionPath Is Nothing OrElse data.ActionPath.Length = 0 Then
                Return action
            End If

            Dim document = _documentManager.GetRoslynDocument(data.Uri)
            If document Is Nothing Then
                _logger.LogTrace("Resolve Extract Method unavailable: no Roslyn document for {Uri}", data.Uri)
                Return action
            End If

            Dim sourceText = Await document.GetTextAsync(cancellationToken).ConfigureAwait(False)
            Dim range = New Protocol.Range With {
                .Start = New Position(data.StartLine.Value, data.StartCharacter.Value),
                .[End] = New Position(data.EndLine.Value, data.EndCharacter.Value)
            }
            Dim selection = TryGetTextSpan(range, sourceText)
            If Not selection.HasValue OrElse selection.Value.Length = 0 Then
                _logger.LogTrace("Resolve Extract Method unavailable: invalid or empty range for {Uri}", data.Uri)
                Return action
            End If

            Dim discovered = Await DiscoverExtractRoslynActionsAsync(document, selection.Value, cancellationToken).ConfigureAwait(False)
            Dim roslynAction = discovered.
                Where(Function(item) PathsEqual(item.Path, data.ActionPath)).
                Select(Function(item) item.RoslynAction).
                FirstOrDefault()

            If roslynAction Is Nothing Then
                _logger.LogTrace("Resolve Extract Method unavailable: action path no longer available for {Uri}", data.Uri)
                Return action
            End If

            Dim operations = Await roslynAction.GetOperationsAsync(cancellationToken).ConfigureAwait(False)
            action.Edit = Await BuildWorkspaceEditFromOperationsAsync(document.Project.Solution, operations, cancellationToken).ConfigureAwait(False)
            Return action
        End Function

        Private Async Function DiscoverExtractRoslynActionsAsync(document As Document, selection As TextSpan, cancellationToken As CancellationToken) As Task(Of List(Of (Title As String, Path As String(), RoslynAction As RoslynCodeAction)))
            Dim results As New List(Of (Title As String, Path As String(), RoslynAction As RoslynCodeAction))()
            Dim provider = CreateExtractMethodProvider()
            If provider Is Nothing Then
                _logger.LogTrace("Extract Method unavailable: Roslyn ExtractMethodCodeRefactoringProvider not found.")
                Return results
            End If

            Dim registeredActions As New List(Of RoslynCodeAction)()
            Dim context = New CodeRefactoringContext(
                document,
                selection,
                Sub(roslynAction)
                    If roslynAction IsNot Nothing Then
                        registeredActions.Add(roslynAction)
                    End If
                End Sub,
                cancellationToken)

            Await provider.ComputeRefactoringsAsync(context).ConfigureAwait(False)

            For Each roslynAction In registeredActions
                cancellationToken.ThrowIfCancellationRequested()
                For Each flattened In FlattenActions(roslynAction, Array.Empty(Of String)())
                    If IsExtractAction(flattened.Action) AndAlso Not IsUiOptionRequired(flattened.Action) Then
                        results.Add((flattened.Action.Title, flattened.Path, flattened.Action))
                    End If
                Next
            Next

            Return results
        End Function

        Private Shared Function CreateExtractMethodProvider() As CodeRefactoringProvider
            Dim featureAssembly = GetType(Microsoft.CodeAnalysis.Completion.CompletionService).Assembly
            Dim providerType = featureAssembly.GetType(ExtractMethodProviderTypeName, throwOnError:=False)
            If providerType Is Nothing Then
                Return Nothing
            End If

            Return TryCast(Activator.CreateInstance(providerType, nonPublic:=True), CodeRefactoringProvider)
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
            Return action IsNot Nothing AndAlso
                action.Title.IndexOf("Extract Method", StringComparison.OrdinalIgnoreCase) >= 0
        End Function

        Private Shared Function IsUiOptionRequired(action As RoslynCodeAction) As Boolean
            If action Is Nothing Then
                Return False
            End If

            Dim actionType = action.GetType()
            Do While actionType IsNot Nothing
                If actionType.Name.IndexOf("WithOptions", StringComparison.OrdinalIgnoreCase) >= 0 Then
                    Return True
                End If

                actionType = actionType.BaseType
            Loop

            Return False
        End Function

        Private Shared Async Function BuildWorkspaceEditFromOperationsAsync(originalSolution As Solution, operations As IEnumerable(Of RoslynCodeActionOperation), cancellationToken As CancellationToken) As Task(Of WorkspaceEdit)
            Dim changesByUri As New Dictionary(Of String, List(Of TextEdit))(StringComparer.OrdinalIgnoreCase)
            If operations Is Nothing Then
                Return New WorkspaceEdit With {
                    .Changes = changesByUri.ToDictionary(Function(kvp) kvp.Key, Function(kvp) kvp.Value.ToArray())
                }
            End If

            For Each operation In operations
                cancellationToken.ThrowIfCancellationRequested()

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
                        If oldDocument Is Nothing OrElse newDocument Is Nothing Then
                            Continue For
                        End If

                        Dim filePath = oldDocument.FilePath
                        If String.IsNullOrEmpty(filePath) Then
                            Continue For
                        End If

                        Dim oldText = Await oldDocument.GetTextAsync(cancellationToken).ConfigureAwait(False)
                        Dim newText = Await newDocument.GetTextAsync(cancellationToken).ConfigureAwait(False)
                        Dim textChanges = newText.GetTextChanges(oldText)
                        If textChanges.Count = 0 Then
                            Continue For
                        End If

                        Dim uri = New Uri(filePath).ToString()
                        Dim edits As List(Of TextEdit) = Nothing
                        If Not changesByUri.TryGetValue(uri, edits) Then
                            edits = New List(Of TextEdit)()
                            changesByUri(uri) = edits
                        End If

                        For Each textChange In textChanges
                            edits.Add(New TextEdit With {
                                .Range = GetRange(textChange.Span, oldText),
                                .NewText = If(textChange.NewText, String.Empty)
                            })
                        Next
                    Next
                Next
            Next

            Return New WorkspaceEdit With {
                .Changes = changesByUri.ToDictionary(Function(kvp) kvp.Key, Function(kvp) kvp.Value.ToArray())
            }
        End Function

        Private Shared Function ShouldIncludeKind(context As CodeActionContext, actionKind As String) As Boolean
            Dim onlyKinds = If(context IsNot Nothing, context.Only, Nothing)
            If onlyKinds Is Nothing OrElse onlyKinds.Length = 0 Then
                Return True
            End If

            Return onlyKinds.Any(Function(requestedKind) MatchesCodeActionKind(requestedKind, actionKind))
        End Function

        Private Shared Function MatchesCodeActionKind(requestedKind As String, actionKind As String) As Boolean
            If String.IsNullOrEmpty(requestedKind) Then
                Return True
            End If

            Return StringComparer.Ordinal.Equals(actionKind, requestedKind) OrElse
                actionKind.StartsWith(requestedKind & ".", StringComparison.Ordinal)
        End Function

        Private Shared Function TryGetTextSpan([range] As Protocol.Range, sourceText As SourceText) As TextSpan?
            If [range] Is Nothing OrElse sourceText Is Nothing OrElse sourceText.Lines.Count = 0 Then
                Return Nothing
            End If

            If [range].Start Is Nothing OrElse [range].[End] Is Nothing Then
                Return Nothing
            End If

            If [range].Start.Line < 0 OrElse [range].End.Line < 0 OrElse
               [range].Start.Character < 0 OrElse [range].End.Character < 0 Then
                Return Nothing
            End If

            If [range].Start.Line >= sourceText.Lines.Count OrElse [range].End.Line >= sourceText.Lines.Count Then
                Return Nothing
            End If

            Dim startLine = sourceText.Lines([range].Start.Line)
            Dim endLine = sourceText.Lines([range].End.Line)
            Dim startCharacter = Math.Min([range].Start.Character, startLine.End - startLine.Start)
            Dim endCharacter = Math.Min([range].End.Character, endLine.End - endLine.Start)
            Dim startPosition = startLine.Start + startCharacter
            Dim endPosition = endLine.Start + endCharacter
            If endPosition < startPosition Then
                Return Nothing
            End If

            Return TextSpan.FromBounds(startPosition, endPosition)
        End Function

        Private Shared Function GetRange(span As TextSpan, sourceText As SourceText) As Protocol.Range
            Dim startLine = sourceText.Lines.GetLineFromPosition(span.Start)
            Dim endLine = sourceText.Lines.GetLineFromPosition(span.[End])

            Return New Protocol.Range With {
                .Start = New Position With {.Line = startLine.LineNumber, .Character = span.Start - startLine.Start},
                .[End] = New Position With {.Line = endLine.LineNumber, .Character = span.[End] - endLine.Start}
            }
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

                If trimmed.StartsWith("Imports ", StringComparison.OrdinalIgnoreCase) Then
                    Exit For
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
                Dim element = DirectCast(data, JsonElement)
                Try
                    Dim parsed = JsonSerializer.Deserialize(Of CodeActionResolveData)(element.GetRawText(), JsonSerializerOptionsProvider.Options)
                    Return NormalizeResolveData(parsed)
                Catch
                    Return Nothing
                End Try
            End If

            Return Nothing
        End Function

        Private Shared Function NormalizeResolveData(data As CodeActionResolveData) As CodeActionResolveData
            If data Is Nothing Then
                Return Nothing
            End If

            If String.IsNullOrWhiteSpace(data.ActionType) AndAlso
               data.InsertionLine.HasValue AndAlso
               Not String.IsNullOrWhiteSpace(data.OptionText) Then
                data.ActionType = ActionTypeOption
            End If

            Return data
        End Function

        Private NotInheritable Class CodeActionResolveData
            <JsonPropertyName("payloadVersion")>
            Public Property PayloadVersion As Integer?

            <JsonPropertyName("actionType")>
            Public Property ActionType As String

            <JsonPropertyName("uri")>
            Public Property Uri As String

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
