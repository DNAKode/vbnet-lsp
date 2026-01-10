using Microsoft.Build.Locator;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using VbNet.LanguageServer.Core;
using Xunit;

// Aliases to avoid conflict with namespace
using LspServer = VbNet.LanguageServer.Core.LanguageServer;
using LspProtocol = VbNet.LanguageServer.Protocol;

namespace VbNet.LanguageServer.Tests.Integration;

/// <summary>
/// Integration tests for the full LanguageServer lifecycle.
/// Tests server initialization, document handling, and diagnostics publishing.
/// </summary>
public class LanguageServerIntegrationTests : IAsyncDisposable
{
    private readonly MockTransport _transport;
    private readonly LspServer _server;

    private static bool _msBuildRegistered = false;
    private static readonly object _lockObject = new();
    private static readonly string TestProjectsRoot = GetTestProjectsRoot();

    public LanguageServerIntegrationTests()
    {
        lock (_lockObject)
        {
            if (!_msBuildRegistered)
            {
                MSBuildLocator.RegisterDefaults();
                _msBuildRegistered = true;
            }
        }

        _transport = new MockTransport();
        var loggerFactory = NullLoggerFactory.Instance;
        _server = new LspServer(_transport, loggerFactory);
    }

    private static string GetTestProjectsRoot()
    {
        var assemblyLocation = typeof(LanguageServerIntegrationTests).Assembly.Location;
        var assemblyDir = Path.GetDirectoryName(assemblyLocation)!;
        var testProjectsPath = Path.GetFullPath(Path.Combine(assemblyDir, "..", "..", "..", "..", "TestProjects"));
        return testProjectsPath;
    }

    public async ValueTask DisposeAsync()
    {
        await _server.DisposeAsync();
    }

    [Fact]
    public void Server_InitialState_IsNotStarted()
    {
        Assert.Equal(ServerState.NotStarted, _server.State);
    }

    [Fact]
    public void Server_HasCorrectServerInfo()
    {
        Assert.Equal("VbNet.LanguageServer", LspServer.ServerName);
        Assert.Equal("0.1.0", LspServer.ServerVersion);
    }

    [Fact]
    public void WorkspaceManager_IsAccessible()
    {
        Assert.NotNull(_server.WorkspaceManager);
    }

    [Fact]
    public void DocumentManager_IsAccessible()
    {
        Assert.NotNull(_server.DocumentManager);
    }

    [Fact]
    public void DiagnosticsService_IsAccessible()
    {
        Assert.NotNull(_server.DiagnosticsService);
    }

    [Fact]
    public async Task SendNotification_PublishesDiagnostics()
    {
        // Start the transport to enable sending
        await _transport.StartAsync();

        var diagnosticsParams = new LspProtocol.PublishDiagnosticsParams
        {
            Uri = "file:///test.vb",
            Diagnostics = new[]
            {
                new LspProtocol.Diagnostic
                {
                    Range = new LspProtocol.Range
                    {
                        Start = new LspProtocol.Position { Line = 0, Character = 0 },
                        End = new LspProtocol.Position { Line = 0, Character = 10 }
                    },
                    Severity = LspProtocol.DiagnosticSeverity.Error,
                    Code = "BC30451",
                    Source = "vbnet",
                    Message = "Test error"
                }
            }
        };

        await _server.SendNotificationAsync("textDocument/publishDiagnostics", diagnosticsParams);

        // Verify the notification was sent
        var sentMessages = _transport.GetSentMessages();
        Assert.Single(sentMessages);
        Assert.Contains("textDocument/publishDiagnostics", sentMessages[0]);
    }
}

/// <summary>
/// A mock transport for testing the language server without actual I/O.
/// </summary>
public class MockTransport : LspProtocol.ITransport
{
    private readonly List<string> _sentMessages = new();
    private bool _isStarted = false;

    public List<string> GetSentMessages() => _sentMessages.ToList();

    public Task StartAsync(CancellationToken cancellationToken = default)
    {
        _isStarted = true;
        return Task.CompletedTask;
    }

    public Task<string?> ReadMessageAsync(CancellationToken cancellationToken = default)
    {
        // Return null to indicate no more messages (will cause the message loop to wait)
        return Task.FromResult<string?>(null);
    }

    public Task WriteMessageAsync(string message, CancellationToken cancellationToken = default)
    {
        if (!_isStarted)
        {
            throw new InvalidOperationException("Transport not started");
        }
        _sentMessages.Add(message);
        return Task.CompletedTask;
    }

    public ValueTask DisposeAsync()
    {
        return ValueTask.CompletedTask;
    }
}
