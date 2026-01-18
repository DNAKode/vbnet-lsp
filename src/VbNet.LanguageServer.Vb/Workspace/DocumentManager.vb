' DocumentManager - Manages open document buffers and synchronization with Roslyn
' Workspace Layer as defined in docs/architecture.md Section 5.3

Imports System.Collections.Concurrent
Imports System.IO
Imports Microsoft.CodeAnalysis
Imports Microsoft.CodeAnalysis.Text
Imports Microsoft.Extensions.Logging
Imports VbNet.LanguageServer.Protocol

Namespace Workspace

    ''' <summary>
    ''' Manages open document buffers and synchronizes them with the Roslyn workspace.
    ''' Handles LSP text document sync operations (didOpen, didChange, didClose).
    ''' </summary>
    Public NotInheritable Class DocumentManager
        Private ReadOnly _workspaceManager As WorkspaceManager
        Private ReadOnly _logger As ILogger(Of DocumentManager)

        ''' <summary>
        ''' Tracks open documents by URI with their current text and version.
        ''' </summary>
        Private ReadOnly _openDocuments As ConcurrentDictionary(Of String, OpenDocument) = New ConcurrentDictionary(Of String, OpenDocument)(StringComparer.OrdinalIgnoreCase)

        ''' <summary>
        ''' Event raised when a document's content changes (for triggering diagnostics).
        ''' </summary>
        Public Event DocumentChanged As EventHandler(Of DocumentChangedEventArgs)

        Public Sub New(workspaceManager As WorkspaceManager, logger As ILogger(Of DocumentManager))
            If workspaceManager Is Nothing Then
                Throw New ArgumentNullException(NameOf(workspaceManager))
            End If
            If logger Is Nothing Then
                Throw New ArgumentNullException(NameOf(logger))
            End If

            _workspaceManager = workspaceManager
            _logger = logger

            AddHandler _workspaceManager.SolutionChanged, AddressOf OnSolutionChanged
        End Sub

        ''' <summary>
        ''' Gets all currently open document URIs.
        ''' </summary>
        Public ReadOnly Property OpenDocumentUris As IEnumerable(Of String)
            Get
                Return _openDocuments.Keys
            End Get
        End Property

        ''' <summary>
        ''' Gets an open document by URI.
        ''' </summary>
        Public Function GetOpenDocument(uri As String) As OpenDocument
            Dim doc As OpenDocument = Nothing
            _openDocuments.TryGetValue(uri, doc)
            Return doc
        End Function

        ''' <summary>
        ''' Checks if a document is currently open.
        ''' </summary>
        Public Function IsDocumentOpen(uri As String) As Boolean
            Return _openDocuments.ContainsKey(uri)
        End Function

        ''' <summary>
        ''' Handles textDocument/didOpen notification.
        ''' Creates a document buffer and potentially an ad-hoc document in workspace.
        ''' </summary>
        Public Sub HandleDidOpen(parameters As DidOpenTextDocumentParams)
            Dim uri = parameters.TextDocument.Uri
            Dim text = parameters.TextDocument.Text
            Dim version = parameters.TextDocument.Version
            Dim languageId = parameters.TextDocument.LanguageId

            _logger.LogDebug("Opening document: {Uri} (version {Version}, language {LanguageId})", uri, version, languageId)

            Dim sourceText As SourceText = SourceText.From(text)

            Dim document = _workspaceManager.GetDocumentByUri(uri)
            Dim documentId As DocumentId = Nothing
            If document IsNot Nothing Then
                documentId = document.Id
            End If

            Dim openDoc As New OpenDocument(uri, languageId, version, sourceText, documentId)
            _openDocuments(uri) = openDoc

            If document IsNot Nothing Then
                _workspaceManager.ApplyTextChange(document.Id, sourceText)
                _logger.LogDebug("Document found in workspace, text synchronized: {Uri}", uri)
            Else
                _logger.LogDebug("Document not in workspace (standalone file): {Uri}", uri)
            End If

            RaiseEvent DocumentChanged(Me, New DocumentChangedEventArgs(uri, sourceText, version, DocumentChangeKind.Opened))
        End Sub

        ''' <summary>
        ''' Handles textDocument/didChange notification.
        ''' Applies incremental or full text changes.
        ''' </summary>
        Public Sub HandleDidChange(parameters As DidChangeTextDocumentParams)
            Dim uri = parameters.TextDocument.Uri
            Dim version = parameters.TextDocument.Version

            Dim openDoc As OpenDocument = Nothing
            If Not _openDocuments.TryGetValue(uri, openDoc) Then
                _logger.LogWarning("Received didChange for unopened document: {Uri}", uri)
                Return
            End If

            If parameters.ContentChanges Is Nothing OrElse parameters.ContentChanges.Length = 0 Then
                _logger.LogTrace("No content changes provided for: {Uri}", uri)
                Return
            End If

            _logger.LogTrace("Document changed: {Uri} (version {OldVersion} -> {NewVersion})", uri, openDoc.Version, version)

            Dim newText = ApplyChanges(openDoc.Text, parameters.ContentChanges)

            openDoc.Text = newText
            openDoc.Version = version

            If openDoc.DocumentId IsNot Nothing Then
                _workspaceManager.ApplyTextChange(openDoc.DocumentId, newText)
            Else
                Dim document = _workspaceManager.GetDocumentByUri(uri)
                If document IsNot Nothing Then
                    openDoc.DocumentId = document.Id
                    _workspaceManager.ApplyTextChange(document.Id, newText)
                    _logger.LogDebug("Late-associated document on change: {Uri} -> {DocumentId}", uri, document.Id)
                End If
            End If

            RaiseEvent DocumentChanged(Me, New DocumentChangedEventArgs(uri, newText, version, DocumentChangeKind.Changed))
        End Sub

        ''' <summary>
        ''' Handles textDocument/didClose notification.
        ''' Removes the document buffer.
        ''' </summary>
        Public Sub HandleDidClose(parameters As DidCloseTextDocumentParams)
            Dim uri = parameters.TextDocument.Uri

            Dim openDoc As OpenDocument = Nothing
            If _openDocuments.TryRemove(uri, openDoc) Then
                _logger.LogDebug("Closed document: {Uri}", uri)
            Else
                _logger.LogWarning("Received didClose for already closed document: {Uri}", uri)
            End If
        End Sub

        ''' <summary>
        ''' Handles textDocument/didSave notification.
        ''' </summary>
        Public Sub HandleDidSave(parameters As DidSaveTextDocumentParams)
            Dim uri = parameters.TextDocument.Uri

            _logger.LogDebug("Document saved: {Uri}", uri)

            Dim openDoc As OpenDocument = Nothing
            If Not _openDocuments.TryGetValue(uri, openDoc) Then
                _logger.LogWarning("Received didSave for unopened document: {Uri}", uri)
                Return
            End If

            If parameters.Text IsNot Nothing Then
                Dim newText = SourceText.From(parameters.Text)
                openDoc.Text = newText

                If openDoc.DocumentId IsNot Nothing Then
                    _workspaceManager.ApplyTextChange(openDoc.DocumentId, newText)
                End If
            End If

            RaiseEvent DocumentChanged(Me, New DocumentChangedEventArgs(uri, openDoc.Text, openDoc.Version, DocumentChangeKind.Saved))
        End Sub

        ''' <summary>
        ''' Gets the current Roslyn Document for an open document.
        ''' Returns the document with the latest text from the buffer.
        ''' </summary>
        Public Function GetRoslynDocument(uri As String) As Document
            Dim openDoc As OpenDocument = Nothing
            If Not _openDocuments.TryGetValue(uri, openDoc) Then
                Return _workspaceManager.GetDocumentByUri(uri)
            End If

            If openDoc.DocumentId Is Nothing Then
                Dim document = _workspaceManager.GetDocumentByUri(uri)
                If document IsNot Nothing Then
                    openDoc.DocumentId = document.Id
                    _workspaceManager.ApplyTextChange(document.Id, openDoc.Text)

                    _logger.LogDebug("Late-associated document: {Uri} -> {DocumentId}", uri, document.Id)
                    Return _workspaceManager.CurrentSolution?.GetDocument(document.Id)
                End If

                Return Nothing
            End If

            Return _workspaceManager.CurrentSolution?.GetDocument(openDoc.DocumentId)
        End Function

        ''' <summary>
        ''' Gets the current source text for a document.
        ''' Prefers the open buffer, falls back to workspace.
        ''' </summary>
        Public Async Function GetSourceTextAsync(uri As String, Optional ct As CancellationToken = Nothing) As Task(Of SourceText)
            Dim openDoc As OpenDocument = Nothing
            If _openDocuments.TryGetValue(uri, openDoc) Then
                Return openDoc.Text
            End If

            Dim document = _workspaceManager.GetDocumentByUri(uri)
            If document IsNot Nothing Then
                Return Await document.GetTextAsync(ct).ConfigureAwait(False)
            End If

            Return Nothing
        End Function

        ''' <summary>
        ''' Updates a closed document from the file system, if it exists in the workspace.
        ''' </summary>
        Public Async Function TryUpdateClosedDocumentFromDiskAsync(uri As String, Optional ct As CancellationToken = Nothing) As Task(Of Boolean)
            If IsDocumentOpen(uri) Then
                Return False
            End If

            Dim document = _workspaceManager.GetDocumentByUri(uri)
            If document Is Nothing OrElse document.Id Is Nothing Then
                Return False
            End If

            Dim filePath = UriToFilePath(uri)
            If String.IsNullOrEmpty(filePath) OrElse Not File.Exists(filePath) Then
                Return False
            End If

            Try
                Dim content = Await File.ReadAllTextAsync(filePath, ct).ConfigureAwait(False)
                Dim sourceText As SourceText = SourceText.From(content)
                _workspaceManager.ApplyTextChange(document.Id, sourceText)
            Catch ex As IOException
                Return False
            Catch ex As UnauthorizedAccessException
                Return False
            End Try

            Return True
        End Function

        ''' <summary>
        ''' Applies a list of content changes to source text.
        ''' Supports both incremental and full document changes.
        ''' </summary>
        Private Function ApplyChanges(text As SourceText, changes As TextDocumentContentChangeEvent()) As SourceText
            For Each change In changes
                If change.Range Is Nothing Then
                    text = SourceText.From(change.Text)
                Else
                    Dim span = GetTextSpan(change.Range, text)
                    Dim textChange = New TextChange(span, change.Text)
                    text = text.WithChanges(textChange)
                End If
            Next

            Return text
        End Function

        ''' <summary>
        ''' Converts an LSP Range to a Roslyn TextSpan.
        ''' Uses UTF-16 positions as per Architecture Decision 14.6.
        ''' </summary>
        Private Shared Function GetTextSpan(range As Protocol.Range, text As SourceText) As TextSpan
            Dim startPosition = GetPosition(range.Start, text)
            Dim endPosition = GetPosition(range.End, text)
            Return TextSpan.FromBounds(startPosition, endPosition)
        End Function

        ''' <summary>
        ''' Converts an LSP Position to a Roslyn offset.
        ''' </summary>
        Private Shared Function GetPosition(position As Position, text As SourceText) As Integer
            Dim line = Math.Min(position.Line, text.Lines.Count - 1)
            line = Math.Max(0, line)

            Dim textLine = text.Lines(line)

            Dim character = Math.Min(position.Character, textLine.End - textLine.Start)
            character = Math.Max(0, character)

            Return textLine.Start + character
        End Function

        Private Shared Function UriToFilePath(uri As String) As String
            If String.IsNullOrEmpty(uri) Then
                Return Nothing
            End If

            Try
                Dim parsedUri As New Uri(uri)
                If parsedUri.IsFile Then
                    Dim localPath = parsedUri.LocalPath

                    If localPath.Length >= 3 AndAlso localPath(0) = "/"c AndAlso Char.IsLetter(localPath(1)) AndAlso localPath(2) = ":"c Then
                        localPath = localPath.Substring(1)
                    End If

                    Return localPath
                End If
            Catch ex As UriFormatException
                Return uri
            End Try

            Return Nothing
        End Function

        ''' <summary>
        ''' Handles solution change events from the workspace.
        ''' Reassociates open documents with workspace documents after workspace loads.
        ''' </summary>
        Private Sub OnSolutionChanged(sender As Object, e As SolutionChangedEventArgs)
            If e.Kind = SolutionChangeKind.Loaded OrElse e.Kind = SolutionChangeKind.Reloaded OrElse e.Kind = SolutionChangeKind.ProjectAdded Then
                ReassociateDocumentsWithWorkspace()
            End If
        End Sub

        ''' <summary>
        ''' Reassociates open documents with their corresponding workspace documents.
        ''' This is called after the workspace loads to fix documents that were opened
        ''' before the workspace was ready.
        ''' </summary>
        Public Sub ReassociateDocumentsWithWorkspace()
            _logger.LogDebug("Reassociating open documents with workspace")
            Dim reassociatedCount = 0

            For Each kvp In _openDocuments
                Dim uri = kvp.Key
                Dim openDoc = kvp.Value

                If openDoc.DocumentId IsNot Nothing Then
                    Continue For
                End If

                Dim document = _workspaceManager.GetDocumentByUri(uri)
                If document IsNot Nothing Then
                    openDoc.DocumentId = document.Id

                    _workspaceManager.ApplyTextChange(document.Id, openDoc.Text)

                    _logger.LogDebug("Reassociated document: {Uri} -> {DocumentId}", uri, document.Id)
                    reassociatedCount += 1

                    RaiseEvent DocumentChanged(Me, New DocumentChangedEventArgs(uri, openDoc.Text, openDoc.Version, DocumentChangeKind.Changed))
                End If
            Next

            If reassociatedCount > 0 Then
                _logger.LogInformation("Reassociated {Count} open document(s) with workspace", reassociatedCount)
            End If
        End Sub
    End Class

    ''' <summary>
    ''' Represents an open document with its current buffer state.
    ''' </summary>
    Public Class OpenDocument
        Public Sub New(uri As String, languageId As String, version As Integer, text As SourceText, documentId As DocumentId)
            If String.IsNullOrWhiteSpace(uri) Then
                Throw New ArgumentException("Uri is required.", NameOf(uri))
            End If
            If String.IsNullOrWhiteSpace(languageId) Then
                Throw New ArgumentException("LanguageId is required.", NameOf(languageId))
            End If

            Me.Uri = uri
            Me.LanguageId = languageId
            Me.Version = version
            Me.Text = If(text, SourceText.From(String.Empty))
            Me.DocumentId = documentId
        End Sub

        ''' <summary>
        ''' The document URI.
        ''' </summary>
        Public Property Uri As String

        ''' <summary>
        ''' The language ID (e.g., "vb").
        ''' </summary>
        Public Property LanguageId As String

        ''' <summary>
        ''' The current document version.
        ''' </summary>
        Public Property Version As Integer

        ''' <summary>
        ''' The current source text.
        ''' </summary>
        Public Property Text As SourceText

        ''' <summary>
        ''' The Roslyn DocumentId if this document is in the workspace.
        ''' Null for standalone files not part of a project.
        ''' </summary>
        Public Property DocumentId As DocumentId
    End Class

    ''' <summary>
    ''' Event args for document changes.
    ''' </summary>
    Public Class DocumentChangedEventArgs
        Inherits EventArgs

        Public Sub New(uri As String, text As SourceText, version As Integer, kind As DocumentChangeKind)
            Me.Uri = uri
            Me.Text = text
            Me.Version = version
            Me.Kind = kind
        End Sub

        Public ReadOnly Property Uri As String
        Public ReadOnly Property Text As SourceText
        Public ReadOnly Property Version As Integer
        Public ReadOnly Property Kind As DocumentChangeKind
    End Class

    Public Enum DocumentChangeKind
        Opened
        Changed
        Saved
    End Enum

End Namespace
