// Named pipe transport implementation for LSP communication
// Primary transport - follows C# extension pattern (outputs pipe name as JSON to stdout)

using System.IO.Pipes;
using System.Runtime.InteropServices;
using System.Text;
using System.Text.Json;
using Microsoft.Extensions.Logging;

namespace VbNet.LanguageServer.Protocol;

/// <summary>
/// LSP transport using named pipes (Windows) or Unix domain sockets (macOS/Linux).
/// This is the primary transport, matching the C# extension behavior.
///
/// Protocol:
/// 1. Server creates a named pipe with a unique name
/// 2. Server outputs {"pipeName":"..."} to stdout
/// 3. Client reads this and connects to the pipe
/// 4. Bidirectional LSP communication over the pipe
/// </summary>
public sealed class NamedPipeTransport : ITransport
{
    private readonly ILogger<NamedPipeTransport> _logger;
    private readonly string _pipeName;
    private readonly SemaphoreSlim _writeLock = new(1, 1);

    private NamedPipeServerStream? _pipeServer;
    private Stream? _readStream;
    private Stream? _writeStream;
    private bool _isConnected;

    private const string ContentLengthHeader = "Content-Length: ";

    public NamedPipeTransport(ILogger<NamedPipeTransport> logger, string? pipeName = null)
    {
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        _pipeName = pipeName ?? GeneratePipeName();
    }

    /// <summary>
    /// Gets the pipe name for this transport.
    /// </summary>
    public string PipeName => _pipeName;

    /// <summary>
    /// Generates a unique pipe name appropriate for the current platform.
    /// </summary>
    private static string GeneratePipeName()
    {
        var uniqueId = Guid.NewGuid().ToString("N")[..8];

        if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
        {
            // Windows named pipes don't need a path prefix in the API
            return $"vbnet-lsp-{uniqueId}";
        }
        else
        {
            // Unix domain sockets need a file path
            var tempDir = Path.GetTempPath();
            return Path.Combine(tempDir, $"vbnet-lsp-{uniqueId}.sock");
        }
    }

    public async Task StartAsync(CancellationToken cancellationToken = default)
    {
        _logger.LogDebug("Creating named pipe: {PipeName}", _pipeName);

        // Create the named pipe server
        _pipeServer = new NamedPipeServerStream(
            _pipeName,
            PipeDirection.InOut,
            maxNumberOfServerInstances: 1,
            PipeTransmissionMode.Byte,
            PipeOptions.Asynchronous);

        // IMPORTANT: Start the async wait for connection BEFORE outputting the pipe name.
        // This ensures the pipe is fully ready to accept connections when the client
        // reads the pipe name from stdout. The client will connect, which completes
        // the WaitForConnectionAsync task.
        //
        // Protocol synchronization:
        // 1. Server creates pipe and starts listening (WaitForConnectionAsync begins)
        // 2. Server outputs pipe name to stdout (signals readiness)
        // 3. Client reads pipe name and connects
        // 4. WaitForConnectionAsync completes
        _logger.LogDebug("Starting to listen on pipe: {PipeName}", _pipeName);
        var connectionTask = _pipeServer.WaitForConnectionAsync(cancellationToken);

        // Small delay to ensure the pipe is fully registered in the OS
        // before we signal readiness to the client
        await Task.Delay(50, cancellationToken);

        // NOW output the pipe name - we're ready to accept connections
        var pipeInfo = JsonSerializer.Serialize(new { pipeName = GetFullPipePath() });
        await Console.Out.WriteLineAsync(pipeInfo);
        await Console.Out.FlushAsync();
        _logger.LogInformation("Pipe ready, name output to stdout: {PipeInfo}", pipeInfo);

        // Wait for client to connect
        _logger.LogDebug("Waiting for client connection on pipe: {PipeName}", _pipeName);
        await connectionTask;

        _readStream = _pipeServer;
        _writeStream = _pipeServer;
        _isConnected = true;

        _logger.LogInformation("Client connected to named pipe");
    }

    /// <summary>
    /// Gets the full pipe path that clients should use to connect.
    /// </summary>
    private string GetFullPipePath()
    {
        if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
        {
            return $@"\\.\pipe\{_pipeName}";
        }
        else
        {
            // On Unix, the pipe name is already the full socket path
            return _pipeName;
        }
    }

    public async Task<string?> ReadMessageAsync(CancellationToken cancellationToken = default)
    {
        if (!_isConnected || _readStream == null)
        {
            throw new InvalidOperationException("Transport is not connected");
        }

        try
        {
            var contentLength = await ReadContentLengthAsync(cancellationToken);
            if (contentLength == null)
            {
                _logger.LogDebug("End of pipe stream reached");
                return null;
            }

            var buffer = new byte[contentLength.Value];
            var totalRead = 0;
            while (totalRead < contentLength.Value)
            {
                var bytesRead = await _readStream.ReadAsync(
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
            _logger.LogTrace("Received message via pipe: {Message}", message);
            return message;
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error reading message from named pipe");
            throw;
        }
    }

    public async Task WriteMessageAsync(string message, CancellationToken cancellationToken = default)
    {
        if (!_isConnected || _writeStream == null)
        {
            throw new InvalidOperationException("Transport is not connected");
        }

        var messageBytes = Encoding.UTF8.GetBytes(message);
        var header = $"{ContentLengthHeader}{messageBytes.Length}\r\n\r\n";
        var headerBytes = Encoding.UTF8.GetBytes(header);

        await _writeLock.WaitAsync(cancellationToken);
        try
        {
            await _writeStream.WriteAsync(headerBytes, cancellationToken);
            await _writeStream.WriteAsync(messageBytes, cancellationToken);
            await _writeStream.FlushAsync(cancellationToken);
            _logger.LogTrace("Sent message via pipe: {Message}", message);
        }
        finally
        {
            _writeLock.Release();
        }
    }

    private async Task<int?> ReadContentLengthAsync(CancellationToken cancellationToken)
    {
        if (_readStream == null) return null;

        var headerBuilder = new StringBuilder();
        var buffer = new byte[1];

        while (true)
        {
            var bytesRead = await _readStream.ReadAsync(buffer, cancellationToken);
            if (bytesRead == 0)
            {
                return null;
            }

            headerBuilder.Append((char)buffer[0]);
            var headerText = headerBuilder.ToString();

            if (headerText.EndsWith("\r\n\r\n", StringComparison.Ordinal))
            {
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

            if (headerBuilder.Length > 8192)
            {
                throw new InvalidOperationException("Header too large");
            }
        }
    }

    public async ValueTask DisposeAsync()
    {
        _isConnected = false;
        _writeLock.Dispose();

        if (_pipeServer != null)
        {
            await _pipeServer.DisposeAsync();
        }

        // Clean up Unix socket file if it exists
        if (!RuntimeInformation.IsOSPlatform(OSPlatform.Windows) && File.Exists(_pipeName))
        {
            try
            {
                File.Delete(_pipeName);
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Failed to delete socket file: {Path}", _pipeName);
            }
        }
    }
}
