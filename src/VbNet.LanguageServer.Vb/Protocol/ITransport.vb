' Transport abstraction for LSP communication
' Supports named pipes (primary) and stdio (secondary) as per architecture decision 14.5

Namespace Protocol

    ''' <summary>
    ''' Abstraction for LSP transport layer.
    ''' Implementations handle the underlying communication mechanism (named pipes or stdio).
    ''' </summary>
    Public Interface ITransport
        Inherits IAsyncDisposable

        ''' <summary>
        ''' Reads a complete LSP message from the transport.
        ''' Returns Nothing when the transport is closed.
        ''' </summary>
        ''' <param name="cancellationToken">Cancellation token for the read operation.</param>
        ''' <returns>The message content, or Nothing if the transport is closed.</returns>
        Function ReadMessageAsync(Optional cancellationToken As CancellationToken = Nothing) As Task(Of String)

        ''' <summary>
        ''' Writes a complete LSP message to the transport.
        ''' </summary>
        ''' <param name="message">The JSON-RPC message content to write.</param>
        ''' <param name="cancellationToken">Cancellation token for the write operation.</param>
        Function WriteMessageAsync(message As String, Optional cancellationToken As CancellationToken = Nothing) As Task

        ''' <summary>
        ''' Starts the transport, making it ready for communication.
        ''' For named pipes, this creates the pipe and outputs the pipe name to stdout.
        ''' For stdio, this is a no-op.
        ''' </summary>
        ''' <param name="cancellationToken">Cancellation token.</param>
        Function StartAsync(Optional cancellationToken As CancellationToken = Nothing) As Task
    End Interface

End Namespace
