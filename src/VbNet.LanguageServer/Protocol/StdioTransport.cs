// Stdio transport implementation for LSP communication
// Secondary transport option - simpler but supported for compatibility

using System.Text;
using Microsoft.Extensions.Logging;

namespace VbNet.LanguageServer.Protocol;

/// <summary>
/// LSP transport using standard input/output streams.
/// Implements Content-Length header-based message framing per LSP specification.
/// </summary>
public sealed class StdioTransport : ITransport
{
    private readonly Stream _input;
    private readonly Stream _output;
    private readonly ILogger<StdioTransport> _logger;
    private readonly SemaphoreSlim _writeLock = new(1, 1);

    private const string ContentLengthHeader = "Content-Length: ";
    private const string HeaderTerminator = "\r\n\r\n";

    public StdioTransport(Stream input, Stream output, ILogger<StdioTransport> logger)
    {
        _input = input ?? throw new ArgumentNullException(nameof(input));
        _output = output ?? throw new ArgumentNullException(nameof(output));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    /// <summary>
    /// Creates a StdioTransport using Console.OpenStandardInput/Output.
    /// </summary>
    public static StdioTransport CreateFromConsole(ILogger<StdioTransport> logger)
    {
        return new StdioTransport(
            Console.OpenStandardInput(),
            Console.OpenStandardOutput(),
            logger);
    }

    public Task StartAsync(CancellationToken cancellationToken = default)
    {
        _logger.LogDebug("Stdio transport started");
        return Task.CompletedTask;
    }

    public async Task<string?> ReadMessageAsync(CancellationToken cancellationToken = default)
    {
        try
        {
            // Read headers until we find Content-Length
            var contentLength = await ReadContentLengthAsync(cancellationToken);
            if (contentLength == null)
            {
                _logger.LogDebug("End of input stream reached");
                return null;
            }

            // Read the message body
            var buffer = new byte[contentLength.Value];
            var totalRead = 0;
            while (totalRead < contentLength.Value)
            {
                var bytesRead = await _input.ReadAsync(
                    buffer.AsMemory(totalRead, contentLength.Value - totalRead),
                    cancellationToken);

                if (bytesRead == 0)
                {
                    _logger.LogWarning("Unexpected end of stream while reading message body");
                    return null;
                }

                totalRead += bytesRead;
            }

            var message = Encoding.UTF8.GetString(buffer);
            _logger.LogTrace("Received message: {Message}", message);
            return message;
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error reading message from stdio");
            throw;
        }
    }

    public async Task WriteMessageAsync(string message, CancellationToken cancellationToken = default)
    {
        var messageBytes = Encoding.UTF8.GetBytes(message);
        var header = $"{ContentLengthHeader}{messageBytes.Length}\r\n\r\n";
        var headerBytes = Encoding.UTF8.GetBytes(header);

        await _writeLock.WaitAsync(cancellationToken);
        try
        {
            await _output.WriteAsync(headerBytes, cancellationToken);
            await _output.WriteAsync(messageBytes, cancellationToken);
            await _output.FlushAsync(cancellationToken);
            _logger.LogTrace("Sent message: {Message}", message);
        }
        finally
        {
            _writeLock.Release();
        }
    }

    private async Task<int?> ReadContentLengthAsync(CancellationToken cancellationToken)
    {
        var headerBuilder = new StringBuilder();
        var buffer = new byte[1];

        // Read character by character until we find the header terminator
        while (true)
        {
            var bytesRead = await _input.ReadAsync(buffer, cancellationToken);
            if (bytesRead == 0)
            {
                return null; // End of stream
            }

            headerBuilder.Append((char)buffer[0]);
            var headerText = headerBuilder.ToString();

            // Check if we've reached the end of headers
            if (headerText.EndsWith(HeaderTerminator, StringComparison.Ordinal))
            {
                // Parse Content-Length from headers
                var lines = headerText.Split("\r\n", StringSplitOptions.RemoveEmptyEntries);
                foreach (var line in lines)
                {
                    if (line.StartsWith(ContentLengthHeader, StringComparison.OrdinalIgnoreCase))
                    {
                        var lengthStr = line.Substring(ContentLengthHeader.Length);
                        if (int.TryParse(lengthStr, out var length))
                        {
                            return length;
                        }
                        _logger.LogError("Invalid Content-Length value: {Value}", lengthStr);
                        throw new InvalidOperationException($"Invalid Content-Length: {lengthStr}");
                    }
                }

                _logger.LogError("No Content-Length header found in: {Headers}", headerText);
                throw new InvalidOperationException("Missing Content-Length header");
            }

            // Safety check for header size
            if (headerBuilder.Length > 8192)
            {
                throw new InvalidOperationException("Header too large");
            }
        }
    }

    public async ValueTask DisposeAsync()
    {
        _writeLock.Dispose();
        await _input.DisposeAsync();
        await _output.DisposeAsync();
    }
}
