using Microsoft.Extensions.Logging.Abstractions;
using VbNet.LanguageServer.Protocol;
using VbNet.LanguageServer.Services;
using VbNet.LanguageServer.Workspace;
using Xunit;

namespace VbNet.LanguageServer.Tests.Services;

public class DiagnosticsServiceTests : IDisposable
{
    private readonly WorkspaceManager _workspaceManager;
    private readonly DocumentManager _documentManager;
    private readonly DiagnosticsService _diagnosticsService;
    private readonly List<PublishDiagnosticsParams> _publishedDiagnostics = new();
    private readonly TaskCompletionSource<PublishDiagnosticsParams> _publishTcs =
        new(TaskCreationOptions.RunContinuationsAsynchronously);

    public DiagnosticsServiceTests()
    {
        _workspaceManager = new WorkspaceManager(NullLogger<WorkspaceManager>.Instance);
        _documentManager = new DocumentManager(_workspaceManager, NullLogger<DocumentManager>.Instance);
        _diagnosticsService = new DiagnosticsService(
            _workspaceManager,
            _documentManager,
            PublishDiagnosticsAsync,
            NullLogger<DiagnosticsService>.Instance);
    }

    private Task PublishDiagnosticsAsync(string method, PublishDiagnosticsParams @params, CancellationToken ct)
    {
        _publishedDiagnostics.Add(@params);
        _publishTcs.TrySetResult(@params);
        return Task.CompletedTask;
    }

    [Fact]
    public void DefaultDebounceDelay_Is300Ms()
    {
        Assert.Equal(300, _diagnosticsService.DebounceDelayMs);
    }

    [Fact]
    public void DefaultMinimumSeverity_IsWarning()
    {
        Assert.Equal(DiagnosticSeverity.Warning, _diagnosticsService.MinimumSeverity);
    }

    [Fact]
    public async Task GetDiagnosticsAsync_ReturnsEmptyForUnknownDocument()
    {
        var diagnostics = await _diagnosticsService.GetDiagnosticsAsync("file:///unknown.vb");
        Assert.Empty(diagnostics);
    }

    [Fact]
    public async Task ClearDiagnosticsAsync_PublishesEmptyDiagnostics()
    {
        var uri = "file:///c:/test/module1.vb";

        await _diagnosticsService.ClearDiagnosticsAsync(uri);

        Assert.Single(_publishedDiagnostics);
        Assert.Equal(uri, _publishedDiagnostics[0].Uri);
        Assert.Empty(_publishedDiagnostics[0].Diagnostics);
    }

    [Fact]
    public void TriggerDiagnostics_SchedulesComputation()
    {
        var uri = "file:///c:/test/module1.vb";

        // This should not throw and should schedule (but not immediately execute)
        _diagnosticsService.TriggerDiagnostics(uri);

        // Since debounce is 300ms, nothing should be published yet
        Assert.Empty(_publishedDiagnostics);
    }

    [Fact]
    public void TriggerDiagnostics_DoesNothingWhenDisabled()
    {
        var uri = "file:///c:/test/module1.vb";

        _diagnosticsService.Enabled = false;

        _diagnosticsService.TriggerDiagnostics(uri);

        Assert.Empty(_publishedDiagnostics);
    }

    [Fact]
    public async Task ComputeAndPublishDiagnosticsAsync_ForStandaloneDocument_PublishesDiagnostics()
    {
        var uri = "file:///c:/test/module1.vb";

        // Open a document without loading a workspace
        _documentManager.HandleDidOpen(new DidOpenTextDocumentParams
        {
            TextDocument = new TextDocumentItem
            {
                Uri = uri,
                LanguageId = "vb",
                Version = 1,
                Text = "Module Module1\nEnd Module"
            }
        });

        await _diagnosticsService.ComputeAndPublishDiagnosticsAsync(uri);
        var published = await WaitForPublishAsync();

        // Should publish (possibly empty since no Roslyn document)
        Assert.NotEmpty(_publishedDiagnostics);
        Assert.Equal(uri, published.Uri);
    }

    [Fact]
    public async Task ComputeAndPublishDiagnosticsAsync_DoesNothingWhenDisabled()
    {
        var uri = "file:///c:/test/module1.vb";

        _diagnosticsService.Enabled = false;

        _documentManager.HandleDidOpen(new DidOpenTextDocumentParams
        {
            TextDocument = new TextDocumentItem
            {
                Uri = uri,
                LanguageId = "vb",
                Version = 1,
                Text = "Module Module1\nEnd Module"
            }
        });

        await _diagnosticsService.ComputeAndPublishDiagnosticsAsync(uri);

        Assert.Empty(_publishedDiagnostics);
    }

    [Fact]
    public void Dispose_CancelsAllPendingOperations()
    {
        var uri = "file:///c:/test/module1.vb";

        // Trigger some diagnostics
        _diagnosticsService.TriggerDiagnostics(uri);

        // Dispose should not throw
        _diagnosticsService.Dispose();

        // Should be safe to call again
        _diagnosticsService.Dispose();
    }

    [Fact]
    public void DebounceDelayMs_CanBeConfigured()
    {
        _diagnosticsService.DebounceDelayMs = 500;
        Assert.Equal(500, _diagnosticsService.DebounceDelayMs);
    }

    [Fact]
    public void MinimumSeverity_CanBeConfigured()
    {
        _diagnosticsService.MinimumSeverity = DiagnosticSeverity.Error;
        Assert.Equal(DiagnosticSeverity.Error, _diagnosticsService.MinimumSeverity);
    }

    public void Dispose()
    {
        _diagnosticsService.Dispose();
    }

    private async Task<PublishDiagnosticsParams> WaitForPublishAsync()
    {
        var completed = await Task.WhenAny(_publishTcs.Task, Task.Delay(TimeSpan.FromSeconds(2)));
        if (completed != _publishTcs.Task)
        {
            throw new TimeoutException("Timed out waiting for diagnostics to publish.");
        }

        return await _publishTcs.Task;
    }
}
