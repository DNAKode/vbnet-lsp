' CodeActionsService - Provides basic and refactor code actions via LSP
' Services Layer as defined in docs/architecture.md Section 5.4

Imports System.Collections
Imports System.Text.Json
Imports System.Text.RegularExpressions
Imports System.Text.Json.Serialization
Imports Microsoft.CodeAnalysis
Imports Microsoft.CodeAnalysis.Text
Imports Microsoft.CodeAnalysis.VisualBasic
Imports Microsoft.CodeAnalysis.VisualBasic.Syntax
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

        ' ─── Simple-extraction analysis helpers ─────────────────────────────
        Private Shared ReadOnly _dimDeclRe As New Regex(
            "(?m)^\s*Dim\s+(\w+)(?:\(\s*\))?\s+As\s+(\w+(?:\.\w+)*(?:\(\s*\))?)",
            RegexOptions.IgnoreCase)

        Private Shared ReadOnly _forEachDeclRe As New Regex(
            "(?m)For\s+Each\s+(\w+)\s+As\s+(\w+(?:\.\w+)*(?:\(\s*\))?)",
            RegexOptions.IgnoreCase)

        Private Shared ReadOnly _forLoopDeclRe As New Regex(
            "(?m)For\s+(\w+)\s+As\s+(\w+(?:\.\w+)*(?:\(\s*\))?)\s*=",
            RegexOptions.IgnoreCase)

        Private Shared ReadOnly _catchDeclRe As New Regex(
            "(?m)Catch\s+(\w+)\s+As\s+(\w+(?:\.\w+)*(?:\(\s*\))?)",
            RegexOptions.IgnoreCase)

        Private Shared ReadOnly _methodSigRe As New Regex(
            "(?m)(?:Sub|Function|Property)\s+\w+\s*\(([^)]*)\)",
            RegexOptions.IgnoreCase)

        Private Shared ReadOnly _paramNameRe As New Regex(
            "(\w+)\s+As\s+(\w+(?:\.\w+)*(?:\(\s*\))?)",
            RegexOptions.IgnoreCase)

        Private Shared ReadOnly _identRe As New Regex("\b([A-Za-z_]\w*)\b")

        Private Shared ReadOnly _vbKeywords As New HashSet(Of String)(StringComparer.OrdinalIgnoreCase) From {
            "And", "AndAlso", "As", "Boolean", "ByRef", "ByVal", "Byte", "Catch",
            "CBool", "CByte", "CChar", "CDate", "CDbl", "CDec", "Char", "CInt",
            "Class", "CLng", "CObj", "Const", "CShort", "CSng", "CStr", "CType",
            "CUInt", "CULng", "CUShort", "Date", "Decimal", "Dim", "DirectCast",
            "Do", "Double", "Each", "Else", "ElseIf", "End", "Enum", "Exit",
            "False", "Finally", "For", "Friend", "Function", "Get", "GetType",
            "Global", "GoTo", "Handles", "If", "Implements", "Imports", "In",
            "Inherits", "Integer", "Interface", "Is", "IsNot", "Let", "Like",
            "Long", "Loop", "Me", "Mod", "Module", "MustInherit", "MustOverride",
            "MyBase", "MyClass", "Namespace", "NameOf", "New", "Next", "Not",
            "Nothing", "Object", "Of", "On", "Option", "Optional", "Or", "OrElse",
            "Overloads", "Overridable", "Overrides", "ParamArray", "Partial",
            "Private", "Property", "Protected", "Public", "RaiseEvent",
            "ReadOnly", "ReDim", "Return", "SByte", "Select", "Set", "Shadows",
            "Shared", "Short", "Single", "Static", "Step", "Stop", "String",
            "Structure", "Sub", "SyncLock", "Then", "Throw", "To", "True",
            "Try", "TryCast", "TypeOf", "UInteger", "ULong", "UShort", "Using",
            "While", "With", "WriteOnly", "Xor",
            "Math", "Environment", "Console", "ControlChars",
            "Abs", "Asc", "AscW", "Ceiling", "Chr", "ChrW", "Floor", "Format",
            "InStr", "Left", "Len", "LTrim", "Max", "Mid", "Min", "Pow", "Replace",
            "Right", "Round", "RTrim", "Sign", "Split", "Sqrt", "ToString", "Trim"
        }

        Public Sub New(workspaceManager As WorkspaceManager, documentManager As DocumentManager, logger As ILogger(Of CodeActionsService))
            If documentManager Is Nothing Then
                Throw New ArgumentNullException(NameOf(documentManager))
            End If
            If logger Is Nothing Then
                Throw New ArgumentNullException(NameOf(logger))
            End If

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
                _logger.LogTrace("Extract: no Roslyn document for {Uri}, will attempt simple strategy only", uri)
            End If

            Dim selection = TryGetTextSpan(parameters.Range, sourceText)
            If selection Is Nothing OrElse selection.Value.Length = 0 Then
                _logger.LogTrace("Extract skipped: zero-length selection for {Uri}", uri)
                Return Array.Empty(Of CodeAction)()
            End If

            Dim syntaxTree = VisualBasicSyntaxTree.ParseText(sourceText, cancellationToken:=cancellationToken)
            Dim root = Await syntaxTree.GetRootAsync(cancellationToken).ConfigureAwait(False)
            If Not IsSelectionLexicallySafe(selection.Value, root) Then
                _logger.LogTrace("Extract skipped: selection crosses a lexical boundary for {Uri}", uri)
                Return Array.Empty(Of CodeAction)()
            End If

            _logger.LogTrace("Extract discovery start: {Uri}, span=[{Start},{End}]", uri, selection.Value.Start, selection.Value.End)

            ' Try Roslyn extraction if document is available
            Dim discovered As New List(Of (Title As String, Path As String(), RoslynAction As RoslynCodeAction))()
            If document IsNot Nothing Then
                discovered = Await DiscoverExtractRoslynActionsAsync(document, selection.Value, cancellationToken).ConfigureAwait(False)
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
            End If

            ' Fall back to simple strategy regardless of Roslyn availability
            If Not CanApplySimpleExtract(parameters.Range, sourceText) Then
                _logger.LogTrace("Extract skipped: simple strategy not applicable for {Uri}", uri)
                Return Array.Empty(Of CodeAction)()
            End If

            _logger.LogTrace("Extract: offering simple strategy for {Uri}", uri)
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
            
            ' For simple extraction, we don't need Roslyn document
            If StringComparer.Ordinal.Equals(data.Strategy, ExtractStrategySimple) Then
                Dim sourceText = Await _documentManager.GetSourceTextAsync(data.Uri, cancellationToken).ConfigureAwait(False)
                If sourceText Is Nothing Then
                    _logger.LogTrace("Resolve extract miss: could not read source text for {Uri}", data.Uri)
                    Return action
                End If

                Dim range = New Protocol.Range With {
                    .Start = New Position(data.StartLine.GetValueOrDefault(), data.StartCharacter.GetValueOrDefault()),
                    .End = New Position(data.EndLine.GetValueOrDefault(), data.EndCharacter.GetValueOrDefault())
                }

                Dim selection = TryGetTextSpan(range, sourceText)
                If selection Is Nothing OrElse selection.Value.Length = 0 Then
                    _logger.LogTrace("Resolve extract miss: selection span is empty for {Uri}", data.Uri)
                    Return action
                End If

                Dim syntaxTree = VisualBasicSyntaxTree.ParseText(sourceText, cancellationToken:=cancellationToken)
                Dim root = Await syntaxTree.GetRootAsync(cancellationToken).ConfigureAwait(False)
                If Not IsSelectionLexicallySafe(selection.Value, root) Then
                    _logger.LogTrace("Resolve extract miss: selection crosses a lexical boundary for {Uri}", data.Uri)
                    Return action
                End If

                _logger.LogTrace("Resolve extract: applying simple strategy for {Uri}", data.Uri)
                Dim simpleEdit = BuildSimpleExtractEdit(data, sourceText)
                If simpleEdit IsNot Nothing Then
                    action.Edit = simpleEdit
                End If
                Return action
            End If

            ' For Roslyn strategy, we require a Document
            Dim document = _documentManager.GetRoslynDocument(data.Uri)
            If document Is Nothing Then
                _logger.LogTrace("Resolve extract miss: no Roslyn document for {Uri}", data.Uri)
                Return action
            End If

            Dim sourceTextForRoslyn = Await document.GetTextAsync(cancellationToken).ConfigureAwait(False)
            Dim rangeForRoslyn = New Protocol.Range With {
                .Start = New Position(data.StartLine.GetValueOrDefault(), data.StartCharacter.GetValueOrDefault()),
                .End = New Position(data.EndLine.GetValueOrDefault(), data.EndCharacter.GetValueOrDefault())
            }

            Dim selectionForRoslyn = TryGetTextSpan(rangeForRoslyn, sourceTextForRoslyn)
            If selectionForRoslyn Is Nothing OrElse selectionForRoslyn.Value.Length = 0 Then
                _logger.LogTrace("Resolve extract miss: selection span is empty for {Uri}", data.Uri)
                Return action
            End If

            Dim syntaxTreeForRoslyn = VisualBasicSyntaxTree.ParseText(sourceTextForRoslyn, cancellationToken:=cancellationToken)
            Dim rootForRoslyn = Await syntaxTreeForRoslyn.GetRootAsync(cancellationToken).ConfigureAwait(False)
            If Not IsSelectionLexicallySafe(selectionForRoslyn.Value, rootForRoslyn) Then
                _logger.LogTrace("Resolve extract miss: selection crosses a lexical boundary for {Uri}", data.Uri)
                Return action
            End If

            If data.ActionPath Is Nothing OrElse data.ActionPath.Length = 0 Then
                Return action
            End If

            Dim discovered = Await DiscoverExtractRoslynActionsAsync(document, selectionForRoslyn.Value, cancellationToken).ConfigureAwait(False)
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

            Dim endSubLine = FindEnclosingEndSubLine([range].End.Line, sourceText)
            If endSubLine < 0 Then
                Return False
            End If

            ' Ensure selection is inside a method body: find matching Sub/Function header
            ' by searching backward from selection start for 'Sub' or 'Function' declaration
            Dim subLine = -1
            For i = [range].Start.Line To 0 Step -1
                Dim line = sourceText.Lines(i).ToString()
                Dim trimmed = line.TrimStart()
                If trimmed.StartsWith("Private Sub ", StringComparison.OrdinalIgnoreCase) OrElse
                   trimmed.StartsWith("Public Sub ", StringComparison.OrdinalIgnoreCase) OrElse
                   trimmed.StartsWith("Protected Sub ", StringComparison.OrdinalIgnoreCase) OrElse
                   trimmed.StartsWith("Friend Sub ", StringComparison.OrdinalIgnoreCase) OrElse
                   trimmed.StartsWith("Private Function ", StringComparison.OrdinalIgnoreCase) OrElse
                   trimmed.StartsWith("Public Function ", StringComparison.OrdinalIgnoreCase) OrElse
                   trimmed.StartsWith("Protected Function ", StringComparison.OrdinalIgnoreCase) OrElse
                   trimmed.StartsWith("Friend Function ", StringComparison.OrdinalIgnoreCase) Then
                    subLine = i
                    Exit For
                End If
            Next

            ' Selection must be inside a method body (between Sub/Function and End Sub/Function)
            Return subLine >= 0 AndAlso subLine <= [range].Start.Line AndAlso endSubLine > [range].End.Line
        End Function

        Friend Shared Function IsSelectionLexicallySafe(selection As TextSpan, root As SyntaxNode) As Boolean
            If root Is Nothing Then
                Return False
            End If

            For Each node In root.DescendantNodesAndSelf()
                If Not node.Span.IntersectsWith(selection) Then
                    Continue For
                End If

                If RequiresContainingBlockBoundary(node) AndAlso Not IsContainingBlockFullySelected(selection, node) Then
                    Return False
                End If

                Dim boundarySpans = GetSelectionBoundarySpans(node).ToArray()
                If boundarySpans.Length >= 2 Then
                    Dim containedCount = boundarySpans.Count(Function(span) selection.Contains(span))
                    If containedCount > 0 AndAlso containedCount < boundarySpans.Length Then
                        Return False
                    End If
                End If

                For Each boundary In boundarySpans
                    If selection.IntersectsWith(boundary) AndAlso Not selection.Contains(boundary) Then
                        Return False
                    End If
                Next
            Next

            Return True
        End Function

        Private Shared Function RequiresContainingBlockBoundary(node As SyntaxNode) As Boolean
            Return TypeOf node Is ElseIfBlockSyntax OrElse
                   TypeOf node Is ElseBlockSyntax OrElse
                   TypeOf node Is CatchBlockSyntax OrElse
                   TypeOf node Is FinallyBlockSyntax OrElse
                   TypeOf node Is CaseBlockSyntax
        End Function

        Private Shared Function IsContainingBlockFullySelected(selection As TextSpan, node As SyntaxNode) As Boolean
            Dim current = node.Parent
            While current IsNot Nothing
                Dim boundarySpans = GetSelectionBoundarySpans(current).ToArray()
                If boundarySpans.Length >= 2 Then
                    Return boundarySpans.All(Function(span) selection.Contains(span))
                End If

                current = current.Parent
            End While

            Return False
        End Function

        Private Shared Iterator Function GetSelectionBoundarySpans(node As SyntaxNode) As IEnumerable(Of TextSpan)
            Select Case True
                Case TypeOf node Is MultiLineIfBlockSyntax
                    Dim block = DirectCast(node, MultiLineIfBlockSyntax)
                    If block.IfStatement IsNot Nothing Then Yield block.IfStatement.Span
                    If block.EndIfStatement IsNot Nothing Then Yield block.EndIfStatement.Span
                Case TypeOf node Is ElseIfBlockSyntax
                    Dim block = DirectCast(node, ElseIfBlockSyntax)
                    If block.ElseIfStatement IsNot Nothing Then Yield block.ElseIfStatement.Span
                Case TypeOf node Is ElseBlockSyntax
                    Dim block = DirectCast(node, ElseBlockSyntax)
                    If block.ElseStatement IsNot Nothing Then Yield block.ElseStatement.Span
                Case TypeOf node Is TryBlockSyntax
                    Dim block = DirectCast(node, TryBlockSyntax)
                    If block.TryStatement IsNot Nothing Then Yield block.TryStatement.Span
                    If block.EndTryStatement IsNot Nothing Then Yield block.EndTryStatement.Span
                Case TypeOf node Is CatchBlockSyntax
                    Dim block = DirectCast(node, CatchBlockSyntax)
                    If block.CatchStatement IsNot Nothing Then Yield block.CatchStatement.Span
                Case TypeOf node Is FinallyBlockSyntax
                    Dim block = DirectCast(node, FinallyBlockSyntax)
                    If block.FinallyStatement IsNot Nothing Then Yield block.FinallyStatement.Span
                Case TypeOf node Is SelectBlockSyntax
                    Dim block = DirectCast(node, SelectBlockSyntax)
                    If block.SelectStatement IsNot Nothing Then Yield block.SelectStatement.Span
                    If block.EndSelectStatement IsNot Nothing Then Yield block.EndSelectStatement.Span
                Case TypeOf node Is CaseBlockSyntax
                    Dim block = DirectCast(node, CaseBlockSyntax)
                    If block.CaseStatement IsNot Nothing Then Yield block.CaseStatement.Span
                Case TypeOf node Is WhileBlockSyntax
                    Dim block = DirectCast(node, WhileBlockSyntax)
                    If block.WhileStatement IsNot Nothing Then Yield block.WhileStatement.Span
                    If block.EndWhileStatement IsNot Nothing Then Yield block.EndWhileStatement.Span
                Case TypeOf node Is UsingBlockSyntax
                    Dim block = DirectCast(node, UsingBlockSyntax)
                    If block.UsingStatement IsNot Nothing Then Yield block.UsingStatement.Span
                    If block.EndUsingStatement IsNot Nothing Then Yield block.EndUsingStatement.Span
                Case TypeOf node Is SyncLockBlockSyntax
                    Dim block = DirectCast(node, SyncLockBlockSyntax)
                    If block.SyncLockStatement IsNot Nothing Then Yield block.SyncLockStatement.Span
                    If block.EndSyncLockStatement IsNot Nothing Then Yield block.EndSyncLockStatement.Span
                Case TypeOf node Is WithBlockSyntax
                    Dim block = DirectCast(node, WithBlockSyntax)
                    If block.WithStatement IsNot Nothing Then Yield block.WithStatement.Span
                    If block.EndWithStatement IsNot Nothing Then Yield block.EndWithStatement.Span
                Case TypeOf node Is DoLoopBlockSyntax
                    Dim block = DirectCast(node, DoLoopBlockSyntax)
                    If block.DoStatement IsNot Nothing Then Yield block.DoStatement.Span
                    If block.LoopStatement IsNot Nothing Then Yield block.LoopStatement.Span
                Case TypeOf node Is ForOrForEachBlockSyntax
                    Dim block = DirectCast(node, ForOrForEachBlockSyntax)
                    If block.ForOrForEachStatement IsNot Nothing Then Yield block.ForOrForEachStatement.Span
                    If block.NextStatement IsNot Nothing Then Yield block.NextStatement.Span
            End Select
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
            Dim selectedText = source.Substring(span.Start, span.Length).TrimEnd(ControlChars.Cr, ControlChars.Lf)
            Dim preText = source.Substring(0, span.Start)
            Dim postText = source.Substring(span.End)

            ' Find indent from first non-blank line in the selection
            Dim statementIndentSize = 0
            For lineIndex = data.StartLine.GetValueOrDefault() To data.EndLine.GetValueOrDefault()
                If lineIndex >= sourceText.Lines.Count Then Exit For
                Dim lineText = sourceText.Lines(lineIndex).ToString()
                If lineText.Trim().Length > 0 Then
                    statementIndentSize = lineText.Length - lineText.TrimStart().Length
                    Exit For
                End If
            Next
            Dim statementIndent = New String(" "c, Math.Max(statementIndentSize, 0))

            ' Use the enclosing End Sub/Function indent as the method declaration indent
            Dim endSubLine = FindEnclosingEndSubLine(data.EndLine.GetValueOrDefault(), sourceText)
            If endSubLine < 0 Then
                Return Nothing
            End If

            Dim endSubLineText = sourceText.Lines(endSubLine).ToString()
            Dim methodIndentSize = endSubLineText.Length - endSubLineText.TrimStart().Length
            Dim methodIndent = New String(" "c, Math.Max(methodIndentSize, 0))
            Dim methodBodyIndentSize = methodIndentSize + 2

            ' Analyse variable flow
            Dim localDims = ParseLocalDims(selectedText)
            Dim capturedParams = FindCapturedParams(selectedText, localDims, preText)
            Dim escapedVars = FindEscapedVars(localDims, postText)

            Dim paramList = String.Join(", ", capturedParams.Select(Function(kvp) kvp.Key & " As " & kvp.Value))
            Dim argList = String.Join(", ", capturedParams.Keys)

            Dim normalizedBody = NormalizeIndent(selectedText, statementIndentSize, methodBodyIndentSize)
            Dim nl = Environment.NewLine
            Dim methodName = GenerateUniqueMethodName("ExtractedMethod", source)

            Dim callText As String
            Dim methodText As String

            If escapedVars.Count = 0 Then
                callText = statementIndent & methodName & "(" & argList & ")" & nl
                methodText =
                    nl &
                    methodIndent & "Private Sub " & methodName & "(" & paramList & ")" & nl &
                    normalizedBody & nl &
                    methodIndent & "End Sub" & nl

            ElseIf escapedVars.Count = 1 Then
                Dim esc = escapedVars.First()
                callText = statementIndent & "Dim " & esc.Key & " As " & esc.Value &
                           " = " & methodName & "(" & argList & ")" & nl
                Dim returnLine = New String(" "c, methodBodyIndentSize) & "Return " & esc.Key & nl
                methodText =
                    nl &
                    methodIndent & "Private Function " & methodName & "(" & paramList & ") As " & esc.Value & nl &
                    normalizedBody & nl &
                    returnLine &
                    methodIndent & "End Function" & nl

            Else
                ' Multiple escaped vars: use ByRef parameters
                Dim byRefParts = escapedVars.Select(Function(kvp) "ByRef " & kvp.Key & " As " & kvp.Value)
                Dim fullParamList = If(paramList.Length > 0,
                    paramList & ", " & String.Join(", ", byRefParts),
                    String.Join(", ", byRefParts))
                Dim escapedArgInit = String.Join(nl,
                    escapedVars.Select(Function(kvp) statementIndent & "Dim " & kvp.Key & " As " & kvp.Value))
                Dim escapedArgNames = String.Join(", ", escapedVars.Keys)
                Dim fullArgList = If(argList.Length > 0, argList & ", " & escapedArgNames, escapedArgNames)
                callText = escapedArgInit & nl & statementIndent & methodName & "(" & fullArgList & ")" & nl
                ' Remove conflicting Dim declarations from method body (they're now ByRef parameters)
                Dim cleanedBody = RemoveConflictingDimDeclarations(normalizedBody, escapedVars.Keys)
                methodText =
                    nl &
                    methodIndent & "Private Sub " & methodName & "(" & fullParamList & ")" & nl &
                    cleanedBody & nl &
                    methodIndent & "End Sub" & nl
            End If

            ' Stitch: replace selected span with callText, insert method AFTER End Sub
            Dim replaced = source.Substring(0, span.Start) & callText & source.Substring(span.End)
            Dim delta = callText.Length - span.Length
            ' Use SpanIncludingLineBreak so we insert after the End Sub line (including its newline)
            Dim endSubSpan = sourceText.Lines(endSubLine).SpanIncludingLineBreak
            Dim insertionPosition = Math.Max(0, Math.Min(replaced.Length, endSubSpan.Start + endSubSpan.Length + delta))
            Dim finalText = replaced.Insert(insertionPosition, methodText)

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

        ''' <summary>
        ''' Removes Dim declarations for variables that are being passed as ByRef parameters.
        ''' This prevents duplicate variable declarations in the extracted method body.
        ''' </summary>
        Private Shared Function RemoveConflictingDimDeclarations(body As String, byRefVariables As IEnumerable(Of String)) As String
            If body Is Nothing OrElse byRefVariables Is Nothing Then
                Return body
            End If

            Dim result = body
            For Each varName In byRefVariables
                ' Match "Dim varName As ..." pattern (case-insensitive), capturing optional full line for removal
                Dim pattern = "^\s*Dim\s+" & System.Text.RegularExpressions.Regex.Escape(varName) & "\s+As\s+.*?$"
                result = System.Text.RegularExpressions.Regex.Replace(
                    result,
                    pattern,
                    "",
                    System.Text.RegularExpressions.RegexOptions.IgnoreCase Or System.Text.RegularExpressions.RegexOptions.Multiline)
            Next

            ' Remove any blank lines that were left behind
            result = System.Text.RegularExpressions.Regex.Replace(result, "^\s*" & Environment.NewLine, "", System.Text.RegularExpressions.RegexOptions.Multiline)

            Return result
        End Function

        ''' <summary>
        ''' Generates a unique method name by finding existing methods in the source and appending
        ''' a numeric suffix if the base name already exists.
        ''' </summary>
        Private Shared Function GenerateUniqueMethodName(baseName As String, sourceText As String) As String
            ' Pattern to find all method declarations (Sub/Function)
            Dim methodPattern = "^\s*(Private|Public|Protected|Friend|Async)?\s*(Sub|Function)\s+(\w+)\s*\("
            Dim existingNames = New HashSet(Of String)(StringComparer.OrdinalIgnoreCase)

            For Each m As Match In System.Text.RegularExpressions.Regex.Matches(
                sourceText,
                methodPattern,
                System.Text.RegularExpressions.RegexOptions.IgnoreCase Or System.Text.RegularExpressions.RegexOptions.Multiline)
                existingNames.Add(m.Groups(3).Value)
            Next

            ' If base name doesn't exist, use it
            If Not existingNames.Contains(baseName) Then
                Return baseName
            End If

            ' Otherwise, append numeric suffix until we find an unused name
            Dim suffix = 1
            While existingNames.Contains(baseName & suffix)
                suffix += 1
            End While

            Return baseName & suffix
        End Function

        ''' <summary>
        ''' Parses all typed local bindings from VB.NET source text:
        ''' Dim, For Each, For, and Catch declarations.
        ''' Returns a case-insensitive map of variable name to declared type name.
        ''' </summary>
        Private Shared Function ParseLocalDims(text As String) As Dictionary(Of String, String)
            Dim result As New Dictionary(Of String, String)(StringComparer.OrdinalIgnoreCase)

            Dim addIfAbsent = Sub(name As String, typeName As String)
                                  If Not result.ContainsKey(name) Then result.Add(name, typeName)
                              End Sub

            For Each m As Match In _dimDeclRe.Matches(text)
                addIfAbsent(m.Groups(1).Value, m.Groups(2).Value)
            Next
            For Each m As Match In _forEachDeclRe.Matches(text)
                addIfAbsent(m.Groups(1).Value, m.Groups(2).Value)
            Next
            For Each m As Match In _forLoopDeclRe.Matches(text)
                addIfAbsent(m.Groups(1).Value, m.Groups(2).Value)
            Next
            For Each m As Match In _catchDeclRe.Matches(text)
                addIfAbsent(m.Groups(1).Value, m.Groups(2).Value)
            Next

            Return result
        End Function

        ''' <summary>
        ''' Finds identifiers used in the selection that are declared only in
        ''' the pre-selection text — these are captured from the outer scope and become parameters.
        ''' Covers: Dim, For Each, For, Catch bindings, and enclosing method parameters.
        ''' Only scans the enclosing method body to avoid picking up bindings from other methods.
        ''' </summary>
        Private Shared Function FindCapturedParams(
            selectedText As String,
            localDims As Dictionary(Of String, String),
            preText As String) As Dictionary(Of String, String)

            Dim result As New Dictionary(Of String, String)(StringComparer.OrdinalIgnoreCase)

            ' Scope the search to only the enclosing method body.
            ' Find the last Sub/Function/Property signature before the selection.
            Dim lastSig As Match = Nothing
            For Each m As Match In _methodSigRe.Matches(preText)
                lastSig = m
            Next

            ' Only scan from the enclosing method declaration onward (not the whole file).
            Dim scopedPre = If(lastSig IsNot Nothing, preText.Substring(lastSig.Index), preText)
            Dim preDims = ParseLocalDims(scopedPre)

            ' Also include the enclosing method's own parameters.
            If lastSig IsNot Nothing Then
                For Each pm As Match In _paramNameRe.Matches(lastSig.Groups(1).Value)
                    Dim pName = pm.Groups(1).Value
                    If Not _vbKeywords.Contains(pName) AndAlso Not preDims.ContainsKey(pName) Then
                        preDims.Add(pName, pm.Groups(2).Value)
                    End If
                Next
            End If

            ' Strip string literals and comments before scanning for identifiers to avoid
            ' false positives from identifier-like words inside strings or comments.
            Dim strippedSelected = StripStringsAndComments(selectedText)
            For Each m As Match In _identRe.Matches(strippedSelected)
                Dim name = m.Groups(1).Value
                If _vbKeywords.Contains(name) Then Continue For
                If localDims.ContainsKey(name) Then Continue For
                If result.ContainsKey(name) Then Continue For
                If preDims.ContainsKey(name) Then
                    result.Add(name, preDims(name))
                End If
            Next
            Return result
        End Function

        ''' <summary>
        ''' Finds variables declared inside the selection whose names also appear after the
        ''' selection — these escape and must be returned or passed ByRef.
        ''' Only scans within the current method body (stops at the next method declaration)
        ''' to avoid false positives from identically-named vars in other methods.
        ''' </summary>
        Private Shared Function FindEscapedVars(
            localDims As Dictionary(Of String, String),
            postText As String) As Dictionary(Of String, String)

            ' Scope to the current method only: stop at the next Sub/Function/Property declaration.
            Dim nextSig = _methodSigRe.Match(postText)
            Dim scopedPost = If(nextSig.Success, postText.Substring(0, nextSig.Index), postText)
            ' Also strip string literals and comments from the scope.
            scopedPost = StripStringsAndComments(scopedPost)

            Dim result As New Dictionary(Of String, String)(StringComparer.OrdinalIgnoreCase)
            For Each kvp In localDims
                Dim pattern As New Regex(
                    "\b" & Regex.Escape(kvp.Key) & "\b",
                    RegexOptions.IgnoreCase)
                If pattern.IsMatch(scopedPost) Then
                    result.Add(kvp.Key, kvp.Value)
                End If
            Next
            Return result
        End Function

        ''' <summary>
        ''' Returns a copy of <paramref name="text"/> with string literal contents
        ''' and line comments replaced by spaces, so that identifier scanning does
        ''' not produce false positives from text inside literals or comments.
        ''' </summary>
        Private Shared Function StripStringsAndComments(text As String) As String
            Dim sb As New System.Text.StringBuilder(text.Length)
            Dim i = 0
            While i < text.Length
                Dim c = text(i)
                If c = """"c Then
                    ' String literal: skip content until closing quote.
                    i += 1
                    While i < text.Length
                        If text(i) = """"c Then
                            If i + 1 < text.Length AndAlso text(i + 1) = """"c Then
                                i += 2 ' skip "" escape inside string
                            Else
                                i += 1 ' skip closing quote
                                Exit While
                            End If
                        Else
                            sb.Append(" "c)
                            i += 1
                        End If
                    End While
                ElseIf c = "'"c Then
                    ' Line comment: skip to end of line, preserving newline chars.
                    While i < text.Length AndAlso text(i) <> ChrW(13) AndAlso text(i) <> ChrW(10)
                        i += 1
                    End While
                Else
                    sb.Append(c)
                    i += 1
                End If
            End While
            Return sb.ToString()
        End Function

        ''' <summary>
        ''' Re-indents each line of <paramref name="text"/> from <paramref name="fromSize"/>
        ''' leading spaces to <paramref name="toSize"/> leading spaces.
        ''' Blank lines are preserved as-is.
        ''' </summary>
        Private Shared Function NormalizeIndent(text As String, fromSize As Integer, toSize As Integer) As String
            If fromSize = toSize Then Return text

            Dim strip = Math.Max(0, fromSize - toSize)
            Dim addCount = Math.Max(0, toSize - fromSize)
            Dim prefix = New String(" "c, addCount)
            Dim separator = If(text.Contains(vbCrLf), vbCrLf, vbLf)
            Dim lines = text.Split(New String() {vbCrLf, vbLf}, StringSplitOptions.None)

            For i = 0 To lines.Length - 1
                Dim line = lines(i)
                If line.Length = 0 Then Continue For
                If strip > 0 Then
                    Dim stripped = line.TrimStart()
                    Dim actualIndent = line.Length - stripped.Length
                    Dim toRemove = Math.Min(strip, actualIndent)
                    lines(i) = prefix & line.Substring(toRemove)
                Else
                    lines(i) = prefix & line
                End If
            Next
            Return String.Join(separator, lines)
        End Function

        Private Async Function DiscoverExtractRoslynActionsAsync(document As Document, selection As TextSpan, cancellationToken As CancellationToken) As Task(Of List(Of (Title As String, Path As String(), RoslynAction As RoslynCodeAction)))
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
            ' Clip character offsets to the line boundary so that large sentinel values
            ' (e.g. character=99 meaning "end of line") do not overrun into the next line.
            Dim startPosition = Math.Min(startLine.Start + Math.Max(0, [range].Start.Character), startLine.EndIncludingLineBreak)
            Dim endPosition = Math.Min(endLine.Start + Math.Max(0, [range].End.Character), endLine.EndIncludingLineBreak)

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
                Try
                    Return propertyValue.GetInt32()
                Catch ex As OverflowException
                    ' JSON number is out of Int32 range; return Nothing instead of crashing
                    Return Nothing
                End Try
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
