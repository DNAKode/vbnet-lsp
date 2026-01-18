' CodeActionsService - Provides basic code actions via LSP
' Services Layer as defined in docs/architecture.md Section 5.4

Imports Microsoft.CodeAnalysis.Text
Imports Microsoft.Extensions.Logging
Imports VbNet.LanguageServer.Protocol
Imports VbNet.LanguageServer.Workspace

Namespace Services

    ''' <summary>
    ''' Provides baseline code actions for VB.NET documents.
    ''' </summary>
    Public NotInheritable Class CodeActionsService
        Private ReadOnly _workspaceManager As WorkspaceManager
        Private ReadOnly _documentManager As DocumentManager
        Private ReadOnly _logger As ILogger(Of CodeActionsService)

        Private Shared ReadOnly SupportedKinds As String() = {CodeActionKind.Source}

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
                .ResolveProvider = False
            }
        End Function

        Public Async Function GetCodeActionsAsync(parameters As CodeActionParams, cancellationToken As CancellationToken) As Task(Of CodeAction())
            If parameters Is Nothing OrElse parameters.TextDocument Is Nothing Then
                Return Array.Empty(Of CodeAction)()
            End If

            Dim uri = parameters.TextDocument.Uri
            Dim openDoc = _documentManager.GetOpenDocument(uri)
            Dim sourceText As SourceText = If(openDoc?.Text, Nothing)

            If sourceText Is Nothing Then
                Dim document = _documentManager.GetRoslynDocument(uri)
                If document Is Nothing Then
                    _logger.LogTrace("No document available for code actions: {Uri}", uri)
                    Return Array.Empty(Of CodeAction)()
                End If

                sourceText = Await document.GetTextAsync(cancellationToken).ConfigureAwait(False)
            End If

            cancellationToken.ThrowIfCancellationRequested()

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

            Return actions.ToArray()
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
            Dim newLine = Environment.NewLine
            Dim edit = New WorkspaceEdit With {
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

            Return New CodeAction With {
                .Title = $"Add {optionText}",
                .Kind = CodeActionKind.Source,
                .Edit = edit,
                .IsPreferred = True
            }
        End Function
    End Class

End Namespace
