Imports System.Text.Json
Imports System.Threading
Imports System.Threading.Channels
Imports Microsoft.Extensions.Logging.Abstractions
Imports VbNet.LanguageServer.Protocol
Imports Xunit

Namespace VbNet.LanguageServer.Tests.Protocol

    Public Class MessageDispatcherTests
        <Fact>
        Public Async Function CancelRequest_CancelsInFlightRequest() As Task
            Dim transport As New TestTransport()
            Dim dispatcher As New MessageDispatcher(transport, NullLogger(Of MessageDispatcher).Instance)

            dispatcher.RegisterRequest(Of Object, Object)("test/cancellable", Async Function(unused, ct)
                                                                           Await Task.Delay(TimeSpan.FromSeconds(30), ct)
                                                                           Return New With {.ok = True}
                                                                       End Function)

            Using runCts As New CancellationTokenSource()
                Dim runTask = dispatcher.RunAsync(runCts.Token)

                transport.EnqueueMessage("{""jsonrpc"":""2.0"",""id"":1,""method"":""test/cancellable""}")
                transport.EnqueueMessage("{""jsonrpc"":""2.0"",""method"":""$/cancelRequest"",""params"":{ ""id"":1 }}")

                Dim response = Await transport.WaitForSentMessageAsync().ConfigureAwait(False)
                Using doc = JsonDocument.Parse(response)
                    Assert.Equal(1, doc.RootElement.GetProperty("id").GetInt32())
                    Dim err = doc.RootElement.GetProperty("error")
                    Assert.Equal(JsonRpcErrorCodes.RequestCancelled, err.GetProperty("code").GetInt32())
                End Using

                transport.Complete()
                runCts.Cancel()

                Await runTask.ConfigureAwait(False)
            End Using
        End Function

        Private NotInheritable Class TestTransport
            Implements ITransport

            Private ReadOnly _inbound As Channel(Of String) = Channel.CreateUnbounded(Of String)()
            Private ReadOnly _sentMessage As TaskCompletionSource(Of String) = New TaskCompletionSource(Of String)(TaskCreationOptions.RunContinuationsAsynchronously)

            Public Function StartAsync(Optional cancellationToken As CancellationToken = Nothing) As Task Implements ITransport.StartAsync
                Return Task.CompletedTask
            End Function

            Public Async Function ReadMessageAsync(Optional cancellationToken As CancellationToken = Nothing) As Task(Of String) Implements ITransport.ReadMessageAsync
                Dim message = Await _inbound.Reader.ReadAsync(cancellationToken).ConfigureAwait(False)
                Return message
            End Function

            Public Function WriteMessageAsync(message As String, Optional cancellationToken As CancellationToken = Nothing) As Task Implements ITransport.WriteMessageAsync
                _sentMessage.TrySetResult(message)
                Return Task.CompletedTask
            End Function

            Public Sub EnqueueMessage(message As String)
                _inbound.Writer.TryWrite(message)
            End Sub

            Public Function WaitForSentMessageAsync() As Task(Of String)
                Return _sentMessage.Task
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
