// Transport abstraction for LSP communication
// Supports named pipes (primary) and stdio (secondary) as per architecture decision 14.5

namespace VbNet.LanguageServer.Protocol;

/// <summary>
/// Abstraction for LSP transport layer.
/// Implementations handle the underlying communication mechanism (named pipes or stdio).
/// </summary>
public interface ITransport : IAsyncDisposable
{
    /// <summary>
    /// Reads a complete LSP message from the transport.
    /// Returns null when the transport is closed.
    /// </summary>
    /// <param name="cancellationToken">Cancellation token for the read operation.</param>
    /// <returns>The message content, or null if the transport is closed.</returns>
    Task<string?> ReadMessageAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// Writes a complete LSP message to the transport.
    /// </summary>
    /// <param name="message">The JSON-RPC message content to write.</param>
    /// <param name="cancellationToken">Cancellation token for the write operation.</param>
    Task WriteMessageAsync(string message, CancellationToken cancellationToken = default);

    /// <summary>
    /// Starts the transport, making it ready for communication.
    /// For named pipes, this creates the pipe and outputs the pipe name to stdout.
    /// For stdio, this is a no-op.
    /// </summary>
    /// <param name="cancellationToken">Cancellation token.</param>
    Task StartAsync(CancellationToken cancellationToken = default);
}
