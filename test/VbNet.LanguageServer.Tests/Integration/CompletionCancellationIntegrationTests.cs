using System.Text.Json;
using System.Threading.Channels;
using Microsoft.Extensions.Logging.Abstractions;
using VbNet.LanguageServer.Core;
using VbNet.LanguageServer.Protocol;
using VbNet.LanguageServer.Services;
using Xunit;

namespace VbNet.LanguageServer.Tests.Integration;

using LspServer = VbNet.LanguageServer.Core.LanguageServer;

public class CompletionCancellationIntegrationTests : IAsyncDisposable
{
    private readonly TestTransport _transport = new();
    private readonly LspServer _server;

    public CompletionCancellationIntegrationTests()
    {
        _server = new LspServer(_transport, NullLoggerFactory.Instance);
    }

    [Fact]
    public async Task CompletionRequest_CanBeCancelled()
    {
        var handlerStarted = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        CompletionService.TestDelayAsync = ct =>
        {
            handlerStarted.TrySetResult(true);
            return Task.Delay(TimeSpan.FromSeconds(30), ct);
        };

        using var runCts = new CancellationTokenSource();
        var runTask = _server.RunAsync(runCts.Token);

        _transport.EnqueueMessage("""
            {"jsonrpc":"2.0","method":"textDocument/didOpen","params":{"textDocument":{"uri":"file:///c:/test/module1.vb","languageId":"vb","version":1,"text":"Module Module1\nEnd Module"}}}
            """);
        _transport.EnqueueMessage("""
            {"jsonrpc":"2.0","id":1,"method":"textDocument/completion","params":{"textDocument":{"uri":"file:///c:/test/module1.vb"},"position":{"line":0,"character":1}}}
            """);

        await handlerStarted.Task;
        _transport.EnqueueMessage("""{"jsonrpc":"2.0","method":"$/cancelRequest","params":{"id":1}}""");

        var response = await _transport.WaitForMessageWithIdAsync(1);
        using var doc = JsonDocument.Parse(response);

        Assert.Equal(1, doc.RootElement.GetProperty("id").GetInt32());
        var error = doc.RootElement.GetProperty("error");
        Assert.Equal(JsonRpcErrorCodes.RequestCancelled, error.GetProperty("code").GetInt32());

        _transport.Complete();
        runCts.Cancel();

        await runTask;
    }

    public async ValueTask DisposeAsync()
    {
        CompletionService.TestDelayAsync = null;
        await _server.DisposeAsync();
    }

    private sealed class TestTransport : ITransport
    {
        private readonly Channel<string?> _inbound = Channel.CreateUnbounded<string?>();
        private readonly TaskCompletionSource<string> _responseMessage = new(TaskCreationOptions.RunContinuationsAsynchronously);

        public Task StartAsync(CancellationToken cancellationToken = default) => Task.CompletedTask;

        public async Task<string?> ReadMessageAsync(CancellationToken cancellationToken = default)
        {
            var message = await _inbound.Reader.ReadAsync(cancellationToken);
            return message;
        }

        public Task WriteMessageAsync(string message, CancellationToken cancellationToken = default)
        {
            if (message.Contains("\"id\":1", StringComparison.Ordinal))
            {
                _responseMessage.TrySetResult(message);
            }
            return Task.CompletedTask;
        }

        public void EnqueueMessage(string message)
        {
            _inbound.Writer.TryWrite(message);
        }

        public Task<string> WaitForMessageWithIdAsync(int id)
        {
            return _responseMessage.Task;
        }

        public void Complete()
        {
            _inbound.Writer.TryWrite(null);
            _inbound.Writer.TryComplete();
        }

        public ValueTask DisposeAsync() => ValueTask.CompletedTask;
    }
}
