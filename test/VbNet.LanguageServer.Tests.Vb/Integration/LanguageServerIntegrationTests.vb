Imports System
Imports System.Collections.Generic
Imports System.IO
Imports System.Linq
Imports System.Text.Json
Imports System.Text.RegularExpressions
Imports System.Threading
Imports System.Threading.Channels
Imports System.Threading.Tasks
Imports Microsoft.Extensions.Logging.Abstractions
Imports Xunit
Imports VbNet.LanguageServer.Core
Imports LspServer = VbNet.LanguageServer.Core.LanguageServer
Imports LspProtocol = VbNet.LanguageServer.Protocol

Namespace VbNet.LanguageServer.Tests.Integration

    ''' <summary>
    ''' Integration tests for the full LanguageServer lifecycle.
    ''' Tests server initialization, document handling, and diagnostics publishing.
    ''' </summary>
    <Collection("MSBuild")>
    Public Class LanguageServerIntegrationTests
        Implements IAsyncDisposable

        Private ReadOnly _transport As MockTransport
        Private ReadOnly _server As LspServer

        Private Shared ReadOnly TestProjectsRoot As String = GetTestProjectsRoot()

        Public Sub New()
            _transport = New MockTransport()
            Dim loggerFactory = NullLoggerFactory.Instance
            _server = New LspServer(_transport, loggerFactory)
        End Sub

        Private Shared Function GetTestProjectsRoot() As String
            Dim assemblyLocation = GetType(LanguageServerIntegrationTests).Assembly.Location
            Dim assemblyDir = Path.GetDirectoryName(assemblyLocation)
            Dim testProjectsPath = Path.GetFullPath(Path.Combine(assemblyDir, "..", "..", "..", "..", "TestProjects"))
            Return testProjectsPath
        End Function

        Public Function DisposeAsync() As ValueTask Implements IAsyncDisposable.DisposeAsync
            Return _server.DisposeAsync()
        End Function

        <Fact>
        Public Sub Server_InitialState_IsNotStarted()
            Assert.Equal(ServerState.NotStarted, _server.State)
        End Sub

        <Fact>
        Public Sub Server_HasCorrectServerInfo()
            Assert.Equal("VbNet.LanguageServer", LspServer.ServerName)
            Assert.False(String.IsNullOrWhiteSpace(LspServer.ServerVersion))
            Assert.Matches(New Regex("^\d+\.\d+\.\d+(-[A-Za-z0-9\.\-]+)?$"), LspServer.ServerVersion)
        End Sub

        <Fact>
        Public Sub WorkspaceManager_IsAccessible()
            Assert.NotNull(_server.WorkspaceManager)
        End Sub

        <Fact>
        Public Sub DocumentManager_IsAccessible()
            Assert.NotNull(_server.DocumentManager)
        End Sub

        <Fact>
        Public Sub DiagnosticsService_IsAccessible()
            Assert.NotNull(_server.DiagnosticsService)
        End Sub

        <Fact>
        Public Async Function SendNotification_PublishesDiagnostics() As Task
            Await _transport.StartAsync()

            Dim diagnosticsParams = New LspProtocol.PublishDiagnosticsParams With {
                .Uri = "file:///test.vb",
                .Diagnostics = New LspProtocol.Diagnostic() {
                    New LspProtocol.Diagnostic With {
                        .Range = New LspProtocol.Range With {
                            .Start = New LspProtocol.Position With {.Line = 0, .Character = 0},
                            .End = New LspProtocol.Position With {.Line = 0, .Character = 10}
                        },
                        .Severity = LspProtocol.DiagnosticSeverity.Error,
                        .Code = "BC30451",
                        .Source = "vbnet",
                        .Message = "Test error"
                    }
                }
            }

            Await _server.SendNotificationAsync("textDocument/publishDiagnostics", diagnosticsParams)

            Dim sentMessages = _transport.GetSentMessages()
            Assert.Single(sentMessages)
            Assert.Contains("textDocument/publishDiagnostics", sentMessages(0))
        End Function

        <Fact>
        Public Async Function Initialized_DoesNotBlockShutdownWhileWorkspaceLoads() As Task
            Dim transport As New QueuedTransport()
            Dim server As New LspServer(transport, NullLoggerFactory.Instance)
            Dim loadStarted As New TaskCompletionSource(Of Object)(TaskCreationOptions.RunContinuationsAsynchronously)

            server.TestBeforeWorkspaceLoadAsync =
                Async Function(ct)
                    loadStarted.TrySetResult(Nothing)
                    Await Task.Delay(TimeSpan.FromSeconds(30), ct).ConfigureAwait(False)
                End Function

            Using runCts As New CancellationTokenSource()
                Dim runTask = server.RunAsync(runCts.Token)

                Try
                    Dim rootUri = New Uri(TestProjectsRoot & Path.DirectorySeparatorChar).AbsoluteUri
                    Dim initializeMessage =
                        "{""jsonrpc"":""2.0"",""id"":1,""method"":""initialize"",""params"":{""rootUri"":" &
                        JsonSerializer.Serialize(rootUri) &
                        ",""clientInfo"":{""name"":""test"",""version"":""1.0""},""initializationOptions"":{""loadProjectsOnStart"":true}}}"

                    transport.EnqueueMessage(initializeMessage)
                    Await WaitForResponseIdAsync(transport, 1).ConfigureAwait(False)

                    transport.EnqueueMessage("{""jsonrpc"":""2.0"",""method"":""initialized"",""params"":{}}")
                    Await WaitWithTimeoutAsync(loadStarted.Task, TimeSpan.FromSeconds(3)).ConfigureAwait(False)

                    transport.EnqueueMessage("{""jsonrpc"":""2.0"",""id"":2,""method"":""shutdown""}")
                    Dim shutdownResponse = Await WaitForResponseIdAsync(transport, 2).ConfigureAwait(False)
                    Assert.Contains("""id"":2", shutdownResponse)

                    transport.EnqueueMessage("{""jsonrpc"":""2.0"",""method"":""exit""}")
                    transport.Complete()
                    Await WaitWithTimeoutAsync(runTask, TimeSpan.FromSeconds(3)).ConfigureAwait(False)
                Finally
                    transport.Complete()
                    runCts.Cancel()
                    server.DisposeAsync().AsTask().GetAwaiter().GetResult()
                End Try
            End Using
        End Function

        Private Shared Async Function WaitForResponseIdAsync(transport As QueuedTransport, id As Integer) As Task(Of String)
            Using timeoutCts As New CancellationTokenSource(TimeSpan.FromSeconds(3))
                While True
                    Dim message = Await transport.ReadSentMessageAsync(timeoutCts.Token).ConfigureAwait(False)
                    Using document = JsonDocument.Parse(message)
                        Dim idElement As JsonElement
                        If document.RootElement.TryGetProperty("id", idElement) AndAlso
                           idElement.ValueKind = JsonValueKind.Number AndAlso
                           idElement.GetInt32() = id Then
                            Return message
                        End If
                    End Using
                End While
            End Using

            Throw New TimeoutException($"Timed out waiting for response id {id}.")
        End Function

        Private Shared Async Function WaitWithTimeoutAsync(task As Task, timeout As TimeSpan) As Task
            Dim completed = Await Task.WhenAny(task, Task.Delay(timeout)).ConfigureAwait(False)
            Assert.Same(task, completed)
            Await task.ConfigureAwait(False)
        End Function
    End Class

    ''' <summary>
    ''' A mock transport for testing the language server without actual I/O.
    ''' </summary>
    Public Class MockTransport
        Implements LspProtocol.ITransport

        Private ReadOnly _sentMessages As New List(Of String)()
        Private _isStarted As Boolean = False

        Public Function GetSentMessages() As List(Of String)
            Return _sentMessages.ToList()
        End Function

        Public Function StartAsync(Optional cancellationToken As CancellationToken = Nothing) As Task Implements LspProtocol.ITransport.StartAsync
            _isStarted = True
            Return Task.CompletedTask
        End Function

        Public Function ReadMessageAsync(Optional cancellationToken As CancellationToken = Nothing) As Task(Of String) Implements LspProtocol.ITransport.ReadMessageAsync
            Return Task.FromResult(Of String)(Nothing)
        End Function

        Public Function WriteMessageAsync(message As String, Optional cancellationToken As CancellationToken = Nothing) As Task Implements LspProtocol.ITransport.WriteMessageAsync
            If Not _isStarted Then
                Throw New InvalidOperationException("Transport not started")
            End If
            _sentMessages.Add(message)
            Return Task.CompletedTask
        End Function

        Public Function DisposeAsync() As ValueTask Implements IAsyncDisposable.DisposeAsync
            Return ValueTask.CompletedTask
        End Function
    End Class

    Friend NotInheritable Class QueuedTransport
        Implements LspProtocol.ITransport

        Private ReadOnly _inbound As Channel(Of String) = Channel.CreateUnbounded(Of String)()
        Private ReadOnly _outbound As Channel(Of String) = Channel.CreateUnbounded(Of String)()

        Public Function StartAsync(Optional cancellationToken As CancellationToken = Nothing) As Task Implements LspProtocol.ITransport.StartAsync
            Return Task.CompletedTask
        End Function

        Public Async Function ReadMessageAsync(Optional cancellationToken As CancellationToken = Nothing) As Task(Of String) Implements LspProtocol.ITransport.ReadMessageAsync
            Try
                Return Await _inbound.Reader.ReadAsync(cancellationToken).ConfigureAwait(False)
            Catch ex As ChannelClosedException
                Return Nothing
            End Try
        End Function

        Public Function WriteMessageAsync(message As String, Optional cancellationToken As CancellationToken = Nothing) As Task Implements LspProtocol.ITransport.WriteMessageAsync
            _outbound.Writer.TryWrite(message)
            Return Task.CompletedTask
        End Function

        Public Sub EnqueueMessage(message As String)
            _inbound.Writer.TryWrite(message)
        End Sub

        Public Async Function ReadSentMessageAsync(cancellationToken As CancellationToken) As Task(Of String)
            Return Await _outbound.Reader.ReadAsync(cancellationToken).ConfigureAwait(False)
        End Function

        Public Sub Complete()
            _inbound.Writer.TryWrite(Nothing)
            _inbound.Writer.TryComplete()
        End Sub

        Public Function DisposeAsync() As ValueTask Implements IAsyncDisposable.DisposeAsync
            Return ValueTask.CompletedTask
        End Function
    End Class

End Namespace
