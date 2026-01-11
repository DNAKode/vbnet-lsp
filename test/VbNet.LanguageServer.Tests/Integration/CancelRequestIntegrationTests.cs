using System.Text.Json;
using System.Threading.Channels;
using Microsoft.Extensions.Logging.Abstractions;
using VbNet.LanguageServer.Protocol;
using Xunit;

namespace VbNet.LanguageServer.Tests.Integration;

using LspServer = VbNet.LanguageServer.Core.LanguageServer;

public class CancelRequestIntegrationTests : IAsyncDisposable
{
    private readonly TestTransport _transport = new();
    private readonly LspServer _server;

    public CancelRequestIntegrationTests()
    {
        _server = new LspServer(_transport, NullLoggerFactory.Instance);
        _server.Dispatcher.RegisterRequest<object?, object?>("test/slowRequest", async (_, ct) =>
        {
            await Task.Delay(TimeSpan.FromSeconds(30), ct);
            return new { ok = true };
        });
    }

    [Fact]
    public async Task CancelRequest_CancelsLongRunningRequest()
    {
        using var runCts = new CancellationTokenSource();
        var runTask = _server.RunAsync(runCts.Token);

        _transport.EnqueueMessage("""{"jsonrpc":"2.0","id":1,"method":"test/slowRequest"}""");
        _transport.EnqueueMessage("""{"jsonrpc":"2.0","method":"$/cancelRequest","params":{"id":1}}""");

        var response = await _transport.WaitForSentMessageAsync();
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
        await _server.DisposeAsync();
    }

    private sealed class TestTransport : ITransport
    {
        private readonly Channel<string?> _inbound = Channel.CreateUnbounded<string?>();
        private readonly TaskCompletionSource<string> _sentMessage = new(TaskCreationOptions.RunContinuationsAsynchronously);

        public Task StartAsync(CancellationToken cancellationToken = default) => Task.CompletedTask;

        public async Task<string?> ReadMessageAsync(CancellationToken cancellationToken = default)
        {
            var message = await _inbound.Reader.ReadAsync(cancellationToken);
            return message;
        }

        public Task WriteMessageAsync(string message, CancellationToken cancellationToken = default)
        {
            _sentMessage.TrySetResult(message);
            return Task.CompletedTask;
        }

        public void EnqueueMessage(string message)
        {
            _inbound.Writer.TryWrite(message);
        }

        public Task<string> WaitForSentMessageAsync()
        {
            return _sentMessage.Task;
        }

        public void Complete()
        {
            _inbound.Writer.TryWrite(null);
            _inbound.Writer.TryComplete();
        }

        public ValueTask DisposeAsync() => ValueTask.CompletedTask;
    }
}
