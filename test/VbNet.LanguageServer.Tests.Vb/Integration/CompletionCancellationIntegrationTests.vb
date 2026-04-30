Imports System
Imports System.Text.Json
Imports System.Threading
Imports System.Threading.Channels
Imports System.Threading.Tasks
Imports Microsoft.Extensions.Logging.Abstractions
Imports VbNet.LanguageServer.Protocol
Imports VbNet.LanguageServer.Services
Imports Xunit

Namespace VbNet.LanguageServer.Tests.Integration

    Public Class CompletionCancellationIntegrationTests
        Implements IAsyncDisposable

        Private ReadOnly _transport As New TestTransport()
        Private ReadOnly _server As Core.LanguageServer

        Public Sub New()
            _server = New Core.LanguageServer(_transport, NullLoggerFactory.Instance)
        End Sub

        <Fact>
        Public Async Function CompletionRequest_CanBeCancelled() As Task
            Dim handlerStarted As New TaskCompletionSource(Of Boolean)(TaskCreationOptions.RunContinuationsAsynchronously)
            _server.CompletionService.TestDelayAsync = Function(ct)
                                                           handlerStarted.TrySetResult(True)
                                                           Return Task.Delay(TimeSpan.FromSeconds(30), ct)
                                                       End Function

            Using runCts As New CancellationTokenSource()
                Dim runTask = _server.RunAsync(runCts.Token)

                _transport.EnqueueMessage("{""jsonrpc"":""2.0"",""method"":""textDocument/didOpen"",""params"":{""textDocument"":{""uri"":""file:///c:/test/module1.vb"",""languageId"":""vb"",""version"":1,""text"":""Module Module1\nEnd Module""}}}")
                _transport.EnqueueMessage("{""jsonrpc"":""2.0"",""id"":1,""method"":""textDocument/completion"",""params"":{""textDocument"":{""uri"":""file:///c:/test/module1.vb""},""position"":{""line"":0,""character"":1}}}")

                Await handlerStarted.Task
                _transport.EnqueueMessage("{""jsonrpc"":""2.0"",""method"":""$/cancelRequest"",""params"":{""id"":1}}")

                Dim response = Await _transport.WaitForMessageWithIdAsync(1)
                Using doc = JsonDocument.Parse(response)
                    Assert.Equal(1, doc.RootElement.GetProperty("id").GetInt32())
                    Dim [error] = doc.RootElement.GetProperty("error")
                    Assert.Equal(JsonRpcErrorCodes.RequestCancelled, [error].GetProperty("code").GetInt32())
                End Using

                _transport.Complete()
                runCts.Cancel()

                Await runTask
            End Using
        End Function

        Public Function DisposeAsync() As ValueTask Implements IAsyncDisposable.DisposeAsync
            _server.CompletionService.TestDelayAsync = Nothing
            Return _server.DisposeAsync()
        End Function

        Private NotInheritable Class TestTransport
            Implements ITransport

            Private ReadOnly _inbound As Channel(Of String) = Channel.CreateUnbounded(Of String)()
            Private ReadOnly _responseMessage As TaskCompletionSource(Of String) =
                New TaskCompletionSource(Of String)(TaskCreationOptions.RunContinuationsAsynchronously)

            Public Function StartAsync(Optional cancellationToken As CancellationToken = Nothing) As Task Implements ITransport.StartAsync
                Return Task.CompletedTask
            End Function

            Public Async Function ReadMessageAsync(Optional cancellationToken As CancellationToken = Nothing) As Task(Of String) Implements ITransport.ReadMessageAsync
                Dim message = Await _inbound.Reader.ReadAsync(cancellationToken)
                Return message
            End Function

            Public Function WriteMessageAsync(message As String, Optional cancellationToken As CancellationToken = Nothing) As Task Implements ITransport.WriteMessageAsync
                If message.Contains("""id"":1", StringComparison.Ordinal) Then
                    _responseMessage.TrySetResult(message)
                End If
                Return Task.CompletedTask
            End Function

            Public Sub EnqueueMessage(message As String)
                _inbound.Writer.TryWrite(message)
            End Sub

            Public Function WaitForMessageWithIdAsync(id As Integer) As Task(Of String)
                Return _responseMessage.Task
            End Function

            Public Sub Complete()
                _inbound.Writer.TryWrite(Nothing)
                _inbound.Writer.TryComplete()
            End Sub

            Public Function DisposeAsync() As ValueTask Implements IAsyncDisposable.DisposeAsync
                Return ValueTask.CompletedTask
            End Function
        End Class
    End Class

End Namespace
