' DocumentLinkService - Provides Document Link via LSP
' Services Layer as defined in docs/architecture.md Section 5.4

Imports System.Text.RegularExpressions
Imports Microsoft.CodeAnalysis.Text
Imports Microsoft.Extensions.Logging
Imports VbNet.LanguageServer.Protocol
Imports VbNet.LanguageServer.Workspace

Namespace Services

    ''' <summary>
    ''' Provides document link functionality for VB.NET documents.
    ''' </summary>
    Public NotInheritable Class DocumentLinkService
        Private ReadOnly _documentManager As DocumentManager
        Private ReadOnly _logger As ILogger(Of DocumentLinkService)

        Private Shared ReadOnly LinkRegex As New Regex("https?://[^\s<>\)\]\}]+", RegexOptions.IgnoreCase Or RegexOptions.Compiled)

        Public Sub New(documentManager As DocumentManager, logger As ILogger(Of DocumentLinkService))
            If documentManager Is Nothing Then
                Throw New ArgumentNullException(NameOf(documentManager))
            End If
            If logger Is Nothing Then
                Throw New ArgumentNullException(NameOf(logger))
            End If

            _documentManager = documentManager
            _logger = logger
        End Sub

        Public Async Function GetDocumentLinksAsync(parameters As DocumentLinkParams, cancellationToken As CancellationToken) As Task(Of DocumentLink())
            If parameters Is Nothing OrElse parameters.TextDocument Is Nothing Then
                Return Array.Empty(Of DocumentLink)()
            End If

            Dim uri = parameters.TextDocument.Uri
            Dim sourceText = Await _documentManager.GetSourceTextAsync(uri, cancellationToken).ConfigureAwait(False)
            If sourceText Is Nothing Then
                _logger.LogTrace("No text available for document links: {Uri}", uri)
                Return Array.Empty(Of DocumentLink)()
            End If

            Dim links As New List(Of DocumentLink)()

            For Each line In sourceText.Lines
                cancellationToken.ThrowIfCancellationRequested()

                Dim text = line.ToString()
                If String.IsNullOrEmpty(text) Then
                    Continue For
                End If

                For Each match As Match In LinkRegex.Matches(text)
                    If Not match.Success Then
                        Continue For
                    End If

                    Dim start = line.Start + match.Index
                    Dim span = New TextSpan(start, match.Length)
                    Dim range = GetRange(span, sourceText)

                    links.Add(New DocumentLink With {
                        .Range = range,
                        .Target = match.Value
                    })
                Next
            Next

            Return links.ToArray()
        End Function

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
