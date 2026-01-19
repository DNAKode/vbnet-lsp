Imports System
Imports System.Collections.Generic
Imports System.Threading
Imports System.Threading.Tasks
Imports Microsoft.Extensions.Logging.Abstractions
Imports VbNet.LanguageServer.Protocol
Imports VbNet.LanguageServer.Services
Imports VbNet.LanguageServer.Workspace
Imports Xunit

Namespace VbNet.LanguageServer.Tests.Services

    ''' <summary>
    ''' Unit tests for DiagnosticsService.
    ''' </summary>
    Public Class DiagnosticsServiceTests
        Implements IDisposable

        Private ReadOnly _workspaceManager As WorkspaceManager
        Private ReadOnly _documentManager As DocumentManager
        Private ReadOnly _diagnosticsService As DiagnosticsService
        Private ReadOnly _publishedDiagnostics As New List(Of PublishDiagnosticsParams)()
        Private ReadOnly _publishTcs As New TaskCompletionSource(Of PublishDiagnosticsParams)(TaskCreationOptions.RunContinuationsAsynchronously)

        Public Sub New()
            _workspaceManager = New WorkspaceManager(NullLogger(Of WorkspaceManager).Instance)
            _documentManager = New DocumentManager(_workspaceManager, NullLogger(Of DocumentManager).Instance)
            _diagnosticsService = New DiagnosticsService(
                _workspaceManager,
                _documentManager,
                AddressOf PublishDiagnosticsAsync,
                NullLogger(Of DiagnosticsService).Instance)
        End Sub

        Private Function PublishDiagnosticsAsync(method As String, parameters As PublishDiagnosticsParams, ct As CancellationToken) As Task
            _publishedDiagnostics.Add(parameters)
            _publishTcs.TrySetResult(parameters)
            Return Task.CompletedTask
        End Function

        <Fact>
        Public Sub DefaultDebounceDelay_Is300Ms()
            Assert.Equal(300, _diagnosticsService.DebounceDelayMs)
        End Sub

        <Fact>
        Public Sub DefaultMinimumSeverity_IsWarning()
            Assert.Equal(DiagnosticSeverity.Warning, _diagnosticsService.MinimumSeverity)
        End Sub

        <Fact>
        Public Async Function GetDiagnosticsAsync_ReturnsEmptyForUnknownDocument() As Task
            Dim diagnostics = Await _diagnosticsService.GetDiagnosticsAsync("file:///unknown.vb")
            Assert.Empty(diagnostics)
        End Function

        <Fact>
        Public Async Function ClearDiagnosticsAsync_PublishesEmptyDiagnostics() As Task
            Dim uri = "file:///c:/test/module1.vb"

            Await _diagnosticsService.ClearDiagnosticsAsync(uri)

            Assert.Single(_publishedDiagnostics)
            Assert.Equal(uri, _publishedDiagnostics(0).Uri)
            Assert.Empty(_publishedDiagnostics(0).Diagnostics)
        End Function

        <Fact>
        Public Sub TriggerDiagnostics_SchedulesComputation()
            Dim uri = "file:///c:/test/module1.vb"

            _diagnosticsService.TriggerDiagnostics(uri)

            Assert.Empty(_publishedDiagnostics)
        End Sub

        <Fact>
        Public Sub TriggerDiagnostics_DoesNothingWhenDisabled()
            Dim uri = "file:///c:/test/module1.vb"

            _diagnosticsService.Enabled = False

            _diagnosticsService.TriggerDiagnostics(uri)

            Assert.Empty(_publishedDiagnostics)
        End Sub

        <Fact>
        Public Async Function ComputeAndPublishDiagnosticsAsync_ForStandaloneDocument_PublishesDiagnostics() As Task
            Dim uri = "file:///c:/test/module1.vb"

            _documentManager.HandleDidOpen(New DidOpenTextDocumentParams With {
                .TextDocument = New TextDocumentItem With {
                    .Uri = uri,
                    .LanguageId = "vb",
                    .Version = 1,
                    .Text = "Module Module1" & vbLf & "End Module"
                }
            })

            Await _diagnosticsService.ComputeAndPublishDiagnosticsAsync(uri)
            Dim published = Await WaitForPublishAsync()

            Assert.NotEmpty(_publishedDiagnostics)
            Assert.Equal(uri, published.Uri)
        End Function

        <Fact>
        Public Async Function ComputeAndPublishDiagnosticsAsync_DoesNothingWhenDisabled() As Task
            Dim uri = "file:///c:/test/module1.vb"

            _diagnosticsService.Enabled = False

            _documentManager.HandleDidOpen(New DidOpenTextDocumentParams With {
                .TextDocument = New TextDocumentItem With {
                    .Uri = uri,
                    .LanguageId = "vb",
                    .Version = 1,
                    .Text = "Module Module1" & vbLf & "End Module"
                }
            })

            Await _diagnosticsService.ComputeAndPublishDiagnosticsAsync(uri)

            Assert.Empty(_publishedDiagnostics)
        End Function

        <Fact>
        Public Sub Dispose_CancelsAllPendingOperations()
            Dim uri = "file:///c:/test/module1.vb"

            _diagnosticsService.TriggerDiagnostics(uri)

            _diagnosticsService.Dispose()
            _diagnosticsService.Dispose()
        End Sub

        <Fact>
        Public Sub DebounceDelayMs_CanBeConfigured()
            _diagnosticsService.DebounceDelayMs = 500
            Assert.Equal(500, _diagnosticsService.DebounceDelayMs)
        End Sub

        <Fact>
        Public Sub MinimumSeverity_CanBeConfigured()
            _diagnosticsService.MinimumSeverity = DiagnosticSeverity.Error
            Assert.Equal(DiagnosticSeverity.Error, _diagnosticsService.MinimumSeverity)
        End Sub

        Private Async Function WaitForPublishAsync() As Task(Of PublishDiagnosticsParams)
            Dim completed = Await Task.WhenAny(_publishTcs.Task, Task.Delay(TimeSpan.FromSeconds(5)))
            If completed Is _publishTcs.Task Then
                Return Await _publishTcs.Task
            End If

            Throw New TimeoutException("Timed out waiting for diagnostics publish")
        End Function

        Public Sub Dispose() Implements IDisposable.Dispose
            _diagnosticsService.Dispose()
        End Sub
    End Class

End Namespace
