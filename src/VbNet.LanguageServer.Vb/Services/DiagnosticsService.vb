' DiagnosticsService - Provides compiler diagnostics via LSP
' Services Layer as defined in docs/architecture.md Section 5.4

Imports System.Collections.Concurrent
Imports System.Collections.Immutable
Imports Microsoft.CodeAnalysis
Imports Microsoft.CodeAnalysis.Text
Imports Microsoft.CodeAnalysis.VisualBasic
Imports Microsoft.Extensions.Logging
Imports VbNet.LanguageServer.Protocol
Imports VbNet.LanguageServer.Workspace

Namespace Services

    ''' <summary>
    ''' Provides compiler diagnostics for VB.NET documents.
    ''' Uses a push model with debouncing as per Architecture Decision 14.8.
    ''' </summary>
    Public NotInheritable Class DiagnosticsService
        Implements IDisposable

        Private Shared ReadOnly DefaultReferences As Lazy(Of ImmutableArray(Of MetadataReference)) = New Lazy(Of ImmutableArray(Of MetadataReference))(AddressOf BuildDefaultReferences)
        Private ReadOnly _workspaceManager As WorkspaceManager
        Private ReadOnly _documentManager As DocumentManager
        Private ReadOnly _logger As ILogger(Of DiagnosticsService)
        Private ReadOnly _publishDiagnostics As Func(Of String, PublishDiagnosticsParams, CancellationToken, Task)

        ''' <summary>
        ''' Debounce timers per document URI.
        ''' </summary>
        Private ReadOnly _debounceTimers As ConcurrentDictionary(Of String, Timer) = New ConcurrentDictionary(Of String, Timer)()

        ''' <summary>
        ''' Cancellation tokens for ongoing diagnostic computations.
        ''' </summary>
        Private ReadOnly _pendingComputations As ConcurrentDictionary(Of String, CancellationTokenSource) = New ConcurrentDictionary(Of String, CancellationTokenSource)()

        ''' <summary>
        ''' Debounce delay in milliseconds. Default 300ms per architecture.
        ''' </summary>
        Public Property DebounceDelayMs As Integer = 300

        ''' <summary>
        ''' Minimum severity to report. Default is Warning (includes Error and Warning).
        ''' </summary>
        Public Property MinimumSeverity As Protocol.DiagnosticSeverity = Protocol.DiagnosticSeverity.Warning

        ''' <summary>
        ''' Enables or disables diagnostics publishing.
        ''' </summary>
        Public Property Enabled As Boolean = True

        ''' <summary>
        ''' Controls when diagnostics are computed.
        ''' </summary>
        Public Property Mode As DiagnosticsMode = DiagnosticsMode.OpenChange

        Public Sub New(workspaceManager As WorkspaceManager, documentManager As DocumentManager, publishDiagnostics As Func(Of String, PublishDiagnosticsParams, CancellationToken, Task), logger As ILogger(Of DiagnosticsService))
            If workspaceManager Is Nothing Then
                Throw New ArgumentNullException(NameOf(workspaceManager))
            End If
            If documentManager Is Nothing Then
                Throw New ArgumentNullException(NameOf(documentManager))
            End If
            If publishDiagnostics Is Nothing Then
                Throw New ArgumentNullException(NameOf(publishDiagnostics))
            End If
            If logger Is Nothing Then
                Throw New ArgumentNullException(NameOf(logger))
            End If

            _workspaceManager = workspaceManager
            _documentManager = documentManager
            _publishDiagnostics = publishDiagnostics
            _logger = logger

            AddHandler _documentManager.DocumentChanged, AddressOf OnDocumentChanged
        End Sub

        ''' <summary>
        ''' Triggers diagnostics for a document with debouncing.
        ''' </summary>
        Public Sub TriggerDiagnostics(uri As String)
            If Not Enabled Then
                Return
            End If

            _logger.LogTrace("Diagnostics triggered for: {Uri}", uri)

            If DebounceDelayMs <= 0 Then
                Dim ignored = ComputeAndPublishDiagnosticsAsync(uri)
                Return
            End If

            Dim existingTimer As Timer = Nothing
            If _debounceTimers.TryRemove(uri, existingTimer) Then
                existingTimer.Dispose()
            End If

            Dim timer = New Timer(
                Sub(state)
                    Dim ignored = ComputeAndPublishDiagnosticsAsync(uri)
                End Sub,
                Nothing,
                DebounceDelayMs,
                Timeout.Infinite)

            _debounceTimers(uri) = timer
        End Sub

        ''' <summary>
        ''' Computes and publishes diagnostics for a document immediately (no debouncing).
        ''' </summary>
        Public Async Function ComputeAndPublishDiagnosticsAsync(uri As String, Optional cancellationToken As CancellationToken = Nothing) As Task
            If Not Enabled Then
                Return
            End If

            Dim existingCts As CancellationTokenSource = Nothing
            If _pendingComputations.TryRemove(uri, existingCts) Then
                existingCts.Cancel()
                existingCts.Dispose()
            End If

            Dim cts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken)
            _pendingComputations(uri) = cts

            Dim clearDiagnostics As Boolean = False
            Try
                Dim diagnostics = Await GetDiagnosticsAsync(uri, cts.Token).ConfigureAwait(False)

                If cts.Token.IsCancellationRequested Then
                    Return
                End If

                Dim openDoc = _documentManager.GetOpenDocument(uri)
                Dim version As Integer? = If(openDoc Is Nothing, Nothing, openDoc.Version)
                Dim params = New PublishDiagnosticsParams With {
                    .Uri = uri,
                    .Version = version,
                    .Diagnostics = diagnostics
                }

                Try
                    Await _publishDiagnostics("textDocument/publishDiagnostics", params, cts.Token).ConfigureAwait(False)
                    _logger.LogDebug("Published {Count} diagnostics for: {Uri}", diagnostics.Length, uri)
                Catch ex As IOException
                    _logger.LogDebug(ex, "Diagnostics publish skipped because transport closed for: {Uri}", uri)
                    Return
                Catch ex As ObjectDisposedException
                    _logger.LogDebug(ex, "Diagnostics publish skipped because transport disposed for: {Uri}", uri)
                    Return
                End Try
            Catch ex As OperationCanceledException
                _logger.LogTrace("Diagnostics computation cancelled for: {Uri}", uri)
            Catch ex As Exception
                _logger.LogError(ex, "Error computing diagnostics for: {Uri}", uri)
                clearDiagnostics = True
            Finally
                _pendingComputations.TryRemove(uri, existingCts)
                cts.Dispose()
            End Try

            If clearDiagnostics Then
                Try
                    Dim params = New PublishDiagnosticsParams With {
                        .Uri = uri,
                        .Diagnostics = Array.Empty(Of Protocol.Diagnostic)()
                    }
                    Await _publishDiagnostics("textDocument/publishDiagnostics", params, CancellationToken.None).ConfigureAwait(False)
                Catch
                    ' Ignore errors when clearing diagnostics
                End Try
            End If
        End Function

        ''' <summary>
        ''' Gets diagnostics for a document from Roslyn.
        ''' </summary>
        Public Async Function GetDiagnosticsAsync(uri As String, Optional cancellationToken As CancellationToken = Nothing) As Task(Of Protocol.Diagnostic())
            Dim document = _documentManager.GetRoslynDocument(uri)
            If document Is Nothing Then
                _logger.LogTrace("No Roslyn document found for: {Uri}. Falling back to standalone diagnostics.", uri)
                Return Await GetStandaloneDiagnosticsAsync(uri, cancellationToken).ConfigureAwait(False)
            End If

            Dim semanticModel = Await document.GetSemanticModelAsync(cancellationToken).ConfigureAwait(False)
            If semanticModel Is Nothing Then
                _logger.LogWarning("Failed to get semantic model for: {Uri}. Falling back to project diagnostics.", uri)
                Return Await GetProjectDiagnosticsAsync(document, cancellationToken).ConfigureAwait(False)
            End If

            cancellationToken.ThrowIfCancellationRequested()

            Dim roslynDiagnostics = semanticModel.GetDiagnostics(cancellationToken:=cancellationToken)

            Dim syntaxTree = Await document.GetSyntaxTreeAsync(cancellationToken).ConfigureAwait(False)
            If syntaxTree IsNot Nothing Then
                Dim syntaxDiagnostics = syntaxTree.GetDiagnostics(cancellationToken)
                roslynDiagnostics = roslynDiagnostics.AddRange(syntaxDiagnostics)
            End If

            Dim sourceText = Await document.GetTextAsync(cancellationToken).ConfigureAwait(False)

            Dim lspDiagnostics = roslynDiagnostics _
                .Where(Function(d) Not d.IsSuppressed) _
                .Where(Function(d) ShouldIncludeDiagnostic(d)) _
                .Select(Function(d) TranslateDiagnostic(d, sourceText)) _
                .Where(Function(d) d IsNot Nothing) _
                .Cast(Of Protocol.Diagnostic)() _
                .ToArray()

            Return lspDiagnostics
        End Function

        ''' <summary>
        ''' Gets pull diagnostics for a single document.
        ''' </summary>
        Public Async Function GetDocumentDiagnosticsReportAsync(parameters As TextDocumentDiagnosticParams, Optional cancellationToken As CancellationToken = Nothing) As Task(Of DocumentDiagnosticReport)
            If parameters Is Nothing OrElse parameters.TextDocument Is Nothing Then
                Return New DocumentDiagnosticReport()
            End If

            Dim uri = parameters.TextDocument.Uri
            Dim diagnostics = Await GetDiagnosticsAsync(uri, cancellationToken).ConfigureAwait(False)

            Return New DocumentDiagnosticReport With {
                .Kind = "full",
                .Items = diagnostics
            }
        End Function

        ''' <summary>
        ''' Gets pull diagnostics for all documents in the workspace.
        ''' </summary>
        Public Async Function GetWorkspaceDiagnosticsReportAsync(parameters As WorkspaceDiagnosticParams, Optional cancellationToken As CancellationToken = Nothing) As Task(Of WorkspaceDiagnosticReport)
            Dim solution = _workspaceManager.CurrentSolution
            If solution Is Nothing Then
                Return New WorkspaceDiagnosticReport()
            End If

            Dim items As New List(Of WorkspaceDocumentDiagnosticReport)()

            For Each project In _workspaceManager.GetVbNetProjects()
                cancellationToken.ThrowIfCancellationRequested()

                For Each document In project.Documents
                    cancellationToken.ThrowIfCancellationRequested()

                    If String.IsNullOrEmpty(document.FilePath) Then
                        Continue For
                    End If

                    Dim uri = PathToUri(document.FilePath)
                    Dim diagnostics = Await GetDiagnosticsAsync(uri, cancellationToken).ConfigureAwait(False)

                    Dim openDoc = _documentManager.GetOpenDocument(uri)
                    Dim version As Integer? = If(openDoc Is Nothing, Nothing, openDoc.Version)

                    items.Add(New WorkspaceDocumentDiagnosticReport With {
                        .Uri = uri,
                        .Version = version,
                        .Kind = "full",
                        .Items = diagnostics
                    })
                Next
            Next

            Return New WorkspaceDiagnosticReport With {
                .Items = items.ToArray()
            }
        End Function
        Private Async Function GetProjectDiagnosticsAsync(document As Document, cancellationToken As CancellationToken) As Task(Of Protocol.Diagnostic())
            Dim syntaxTree = Await document.GetSyntaxTreeAsync(cancellationToken).ConfigureAwait(False)
            If syntaxTree Is Nothing Then
                Return Array.Empty(Of Protocol.Diagnostic)()
            End If

            Dim compilation = Await document.Project.GetCompilationAsync(cancellationToken).ConfigureAwait(False)
            If compilation Is Nothing Then
                Return Array.Empty(Of Protocol.Diagnostic)()
            End If

            Dim sourceText = Await syntaxTree.GetTextAsync(cancellationToken).ConfigureAwait(False)
            Dim diagnostics = compilation.GetDiagnostics(cancellationToken) _
                .Where(Function(d) d.Location.Kind = LocationKind.SourceFile AndAlso d.Location.SourceTree Is syntaxTree)

            Return diagnostics _
                .Where(Function(d) Not d.IsSuppressed) _
                .Where(Function(d) ShouldIncludeDiagnostic(d)) _
                .Select(Function(d) TranslateDiagnostic(d, sourceText)) _
                .Where(Function(d) d IsNot Nothing) _
                .Cast(Of Protocol.Diagnostic)() _
                .ToArray()
        End Function

        Private Async Function GetStandaloneDiagnosticsAsync(uri As String, cancellationToken As CancellationToken) As Task(Of Protocol.Diagnostic())
            Dim sourceText = Await _documentManager.GetSourceTextAsync(uri, cancellationToken).ConfigureAwait(False)
            If sourceText Is Nothing Then
                Return Array.Empty(Of Protocol.Diagnostic)()
            End If

            Dim filePath = TryGetFilePath(uri)
            Dim syntaxTree = VisualBasicSyntaxTree.ParseText(sourceText, path:=filePath)
            Dim compilation = VisualBasicCompilation.Create(
                "VbNetStandaloneDiagnostics",
                New SyntaxTree() {syntaxTree},
                DefaultReferences.Value,
                New VisualBasicCompilationOptions(OutputKind.DynamicallyLinkedLibrary))

            Dim diagnostics = compilation.GetDiagnostics(cancellationToken) _
                .Where(Function(d) d.Location.Kind = LocationKind.SourceFile AndAlso d.Location.SourceTree Is syntaxTree)

            Return diagnostics _
                .Where(Function(d) Not d.IsSuppressed) _
                .Where(Function(d) ShouldIncludeDiagnostic(d)) _
                .Select(Function(d) TranslateDiagnostic(d, sourceText)) _
                .Where(Function(d) d IsNot Nothing) _
                .Cast(Of Protocol.Diagnostic)() _
                .ToArray()
        End Function

        Private Shared Function BuildDefaultReferences() As ImmutableArray(Of MetadataReference)
            Dim builder = ImmutableArray.CreateBuilder(Of MetadataReference)()
            Dim trustedAssemblies = TryCast(AppContext.GetData("TRUSTED_PLATFORM_ASSEMBLIES"), String)
            If String.IsNullOrWhiteSpace(trustedAssemblies) Then
                Return builder.ToImmutable()
            End If

            For Each assemblyPath In trustedAssemblies.Split(System.IO.Path.PathSeparator)
                Try
                    builder.Add(MetadataReference.CreateFromFile(assemblyPath))
                Catch
                    ' Ignore invalid reference paths.
                End Try
            Next

            Return builder.ToImmutable()
        End Function

        Private Shared Function TryGetFilePath(uri As String) As String
            If String.IsNullOrWhiteSpace(uri) Then
                Return Nothing
            End If

            Try
                Dim parsedUri As New Uri(uri)
                If Not parsedUri.IsFile Then
                    Return uri
                End If

                Dim localPath = parsedUri.LocalPath
                If localPath.Length >= 3 AndAlso localPath(0) = "/"c AndAlso Char.IsLetter(localPath(1)) AndAlso localPath(2) = ":"c Then
                    localPath = localPath.Substring(1)
                End If

                Return localPath
            Catch ex As UriFormatException
                Return uri
            End Try
        End Function

        ''' <summary>
        ''' Clears diagnostics for a document (e.g., when closed).
        ''' </summary>
        Public Async Function ClearDiagnosticsAsync(uri As String, Optional cancellationToken As CancellationToken = Nothing) As Task
            Dim cts As CancellationTokenSource = Nothing
            If _pendingComputations.TryRemove(uri, cts) Then
                cts.Cancel()
                cts.Dispose()
            End If

            Dim timer As Timer = Nothing
            If _debounceTimers.TryRemove(uri, timer) Then
                timer.Dispose()
            End If

            Dim params = New PublishDiagnosticsParams With {
                .Uri = uri,
                .Diagnostics = Array.Empty(Of Protocol.Diagnostic)()
            }

            Try
                Await _publishDiagnostics("textDocument/publishDiagnostics", params, cancellationToken).ConfigureAwait(False)
                _logger.LogDebug("Cleared diagnostics for: {Uri}", uri)
            Catch ex As IOException
                _logger.LogDebug(ex, "Diagnostics clear skipped because transport closed for: {Uri}", uri)
            Catch ex As ObjectDisposedException
                _logger.LogDebug(ex, "Diagnostics clear skipped because transport disposed for: {Uri}", uri)
            End Try
        End Function

        ''' <summary>
        ''' Determines if a diagnostic should be included based on severity.
        ''' </summary>
        Private Function ShouldIncludeDiagnostic(diagnostic As Microsoft.CodeAnalysis.Diagnostic) As Boolean
            Dim severity = TranslateSeverity(diagnostic.Severity)
            Return severity <= MinimumSeverity
        End Function

        ''' <summary>
        ''' Translates a Roslyn Diagnostic to an LSP Diagnostic.
        ''' </summary>
        Private Function TranslateDiagnostic(diagnostic As Microsoft.CodeAnalysis.Diagnostic, sourceText As SourceText) As Protocol.Diagnostic
            If diagnostic.Location.Kind <> LocationKind.SourceFile Then
                Return Nothing
            End If

            Dim span = diagnostic.Location.SourceSpan
            Dim range = GetRange(span, sourceText)

            Return New Protocol.Diagnostic With {
                .Range = range,
                .Severity = TranslateSeverity(diagnostic.Severity),
                .Code = diagnostic.Id,
                .Source = "vbnet",
                .Message = diagnostic.GetMessage(),
                .CodeDescription = GetCodeDescription(diagnostic),
                .RelatedInformation = GetRelatedInformation(diagnostic)
            }
        End Function

        ''' <summary>
        ''' Translates Roslyn DiagnosticSeverity to LSP DiagnosticSeverity.
        ''' </summary>
        Private Shared Function TranslateSeverity(severity As Microsoft.CodeAnalysis.DiagnosticSeverity) As Protocol.DiagnosticSeverity
            Select Case severity
                Case Microsoft.CodeAnalysis.DiagnosticSeverity.Error
                    Return Protocol.DiagnosticSeverity.[Error]
                Case Microsoft.CodeAnalysis.DiagnosticSeverity.Warning
                    Return Protocol.DiagnosticSeverity.Warning
                Case Microsoft.CodeAnalysis.DiagnosticSeverity.Info
                    Return Protocol.DiagnosticSeverity.Information
                Case Microsoft.CodeAnalysis.DiagnosticSeverity.Hidden
                    Return Protocol.DiagnosticSeverity.Hint
                Case Else
                    Return Protocol.DiagnosticSeverity.Information
            End Select
        End Function

        ''' <summary>
        ''' Gets a code description link for a diagnostic if available.
        ''' </summary>
        Private Shared Function GetCodeDescription(diagnostic As Microsoft.CodeAnalysis.Diagnostic) As CodeDescription
            Dim helpLink = diagnostic.Descriptor.HelpLinkUri
            If String.IsNullOrEmpty(helpLink) Then
                If diagnostic.Id.StartsWith("BC", StringComparison.OrdinalIgnoreCase) Then
                    helpLink = $"https://learn.microsoft.com/en-us/dotnet/visual-basic/misc/{diagnostic.Id.ToLowerInvariant()}"
                End If
            End If

            If Not String.IsNullOrEmpty(helpLink) Then
                Return New CodeDescription With {.Href = helpLink}
            End If

            Return Nothing
        End Function

        ''' <summary>
        ''' Gets related information for a diagnostic (additional locations).
        ''' </summary>
        Private Function GetRelatedInformation(diagnostic As Microsoft.CodeAnalysis.Diagnostic) As DiagnosticRelatedInformation()
            Dim additionalLocations = diagnostic.AdditionalLocations
            If additionalLocations.Count = 0 Then
                Return Nothing
            End If

            Dim related As New List(Of DiagnosticRelatedInformation)()
            For Each location In additionalLocations
                If location.Kind <> LocationKind.SourceFile OrElse location.SourceTree Is Nothing Then
                    Continue For
                End If

                Dim filePath = location.SourceTree.FilePath
                Dim uri = PathToUri(filePath)
                Dim relatedText = location.SourceTree.GetText()
                Dim range = GetRange(location.SourceSpan, relatedText)

                related.Add(New DiagnosticRelatedInformation With {
                    .Location = New Protocol.Location With {
                        .Uri = uri,
                        .Range = range
                    },
                    .Message = "Related location"
                })
            Next

            Return If(related.Count > 0, related.ToArray(), Nothing)
        End Function

        ''' <summary>
        ''' Converts a TextSpan to an LSP Range.
        ''' </summary>
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

        ''' <summary>
        ''' Converts a file path to a file:// URI.
        ''' </summary>
        Private Shared Function PathToUri(path As String) As String
            Return New Uri(path).ToString()
        End Function

        Private Sub OnDocumentChanged(sender As Object, e As DocumentChangedEventArgs)
            If Not Enabled Then
                Return
            End If

            Select Case e.Kind
                Case DocumentChangeKind.Opened
                    If Mode <> DiagnosticsMode.SaveOnly Then
                        Dim ignored = ComputeAndPublishDiagnosticsAsync(e.Uri)
                    End If
                Case DocumentChangeKind.Changed
                    If Mode = DiagnosticsMode.OpenChange Then
                        TriggerDiagnostics(e.Uri)
                    End If
                Case DocumentChangeKind.Saved
                    If Mode = DiagnosticsMode.OpenSave OrElse Mode = DiagnosticsMode.SaveOnly Then
                        TriggerDiagnostics(e.Uri)
                    End If
            End Select
        End Sub

        Public Sub Dispose() Implements IDisposable.Dispose
            RemoveHandler _documentManager.DocumentChanged, AddressOf OnDocumentChanged

            For Each timer In _debounceTimers.Values
                timer.Dispose()
            Next
            _debounceTimers.Clear()

            For Each cts In _pendingComputations.Values
                cts.Cancel()
                cts.Dispose()
            Next
            _pendingComputations.Clear()
        End Sub

        ''' <summary>
        ''' Cancels any pending diagnostic work without publishing.
        ''' </summary>
        Public Sub CancelPendingWork()
            For Each timer In _debounceTimers.Values
                timer.Dispose()
            Next
            _debounceTimers.Clear()

            For Each cts In _pendingComputations.Values
                cts.Cancel()
                cts.Dispose()
            Next
            _pendingComputations.Clear()
        End Sub
    End Class

    ''' <summary>
    ''' Controls when diagnostics are computed.
    ''' </summary>
    Public Enum DiagnosticsMode
        OpenChange
        OpenSave
        SaveOnly
    End Enum

End Namespace
