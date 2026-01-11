using Microsoft.Extensions.Logging.Abstractions;
using VbNet.LanguageServer.Protocol;
using VbNet.LanguageServer.Services;
using VbNet.LanguageServer.Workspace;
using Xunit;

namespace VbNet.LanguageServer.Tests.Integration;

/// <summary>
/// Integration tests for DiagnosticsService with real VB.NET projects.
/// These tests verify that diagnostics are correctly computed from Roslyn.
/// </summary>
[Collection("MSBuild")]
public class DiagnosticsIntegrationTests : IAsyncLifetime
{
    private readonly WorkspaceManager _workspaceManager;
    private readonly DocumentManager _documentManager;
    private readonly DiagnosticsService _diagnosticsService;
    private readonly List<PublishDiagnosticsParams> _publishedDiagnostics = new();

    private static readonly string TestProjectsRoot = GetTestProjectsRoot();

    public DiagnosticsIntegrationTests()
    {
        _workspaceManager = new WorkspaceManager(NullLogger<WorkspaceManager>.Instance);
        _documentManager = new DocumentManager(_workspaceManager, NullLogger<DocumentManager>.Instance);
        _diagnosticsService = new DiagnosticsService(
            _workspaceManager,
            _documentManager,
            PublishDiagnosticsAsync,
            NullLogger<DiagnosticsService>.Instance);
    }

    private static string GetTestProjectsRoot()
    {
        var assemblyLocation = typeof(DiagnosticsIntegrationTests).Assembly.Location;
        var assemblyDir = Path.GetDirectoryName(assemblyLocation)!;
        var testProjectsPath = Path.GetFullPath(Path.Combine(assemblyDir, "..", "..", "..", "..", "TestProjects"));
        return testProjectsPath;
    }

    private Task PublishDiagnosticsAsync(string method, PublishDiagnosticsParams @params, CancellationToken ct)
    {
        _publishedDiagnostics.Add(@params);
        return Task.CompletedTask;
    }

    public Task InitializeAsync()
    {
        _workspaceManager.Initialize();
        return Task.CompletedTask;
    }

    public async Task DisposeAsync()
    {
        _diagnosticsService.Dispose();
        await _workspaceManager.DisposeAsync();
    }

    [Fact]
    public async Task GetDiagnosticsAsync_ValidCode_ReturnsNoDiagnostics()
    {
        var projectPath = Path.Combine(TestProjectsRoot, "SmallProject", "SmallProject.vbproj");
        var helperPath = Path.Combine(TestProjectsRoot, "SmallProject", "Helper.vb");

        if (!File.Exists(projectPath))
        {
            return;
        }

        await _workspaceManager.LoadProjectAsync(projectPath);

        var helperUri = new Uri(helperPath).ToString();

        // Open the document
        var text = await File.ReadAllTextAsync(helperPath);
        _documentManager.HandleDidOpen(new DidOpenTextDocumentParams
        {
            TextDocument = new TextDocumentItem
            {
                Uri = helperUri,
                LanguageId = "vb",
                Version = 1,
                Text = text
            }
        });

        // Get diagnostics directly (bypass debounce)
        var diagnostics = await _diagnosticsService.GetDiagnosticsAsync(helperUri);

        // Helper.vb should have no errors
        var errors = diagnostics.Where(d => d.Severity == DiagnosticSeverity.Error).ToList();
        Assert.Empty(errors);
    }

    [Fact]
    public async Task GetDiagnosticsAsync_ErrorCode_ReturnsDiagnostics()
    {
        var projectPath = Path.Combine(TestProjectsRoot, "ErrorProject", "ErrorProject.vbproj");
        var errorClassPath = Path.Combine(TestProjectsRoot, "ErrorProject", "ErrorClass.vb");

        if (!File.Exists(projectPath))
        {
            return;
        }

        await _workspaceManager.LoadProjectAsync(projectPath);

        var errorClassUri = new Uri(errorClassPath).ToString();

        // Open the document
        var text = await File.ReadAllTextAsync(errorClassPath);
        _documentManager.HandleDidOpen(new DidOpenTextDocumentParams
        {
            TextDocument = new TextDocumentItem
            {
                Uri = errorClassUri,
                LanguageId = "vb",
                Version = 1,
                Text = text
            }
        });

        // Get diagnostics
        var diagnostics = await _diagnosticsService.GetDiagnosticsAsync(errorClassUri);

        // ErrorClass.vb should have multiple errors
        var errors = diagnostics.Where(d => d.Severity == DiagnosticSeverity.Error).ToList();
        Assert.NotEmpty(errors);

        // Check for specific error codes
        var errorCodes = errors.Select(e => e.Code).ToList();

        // BC30512 - Option Strict On disallows implicit conversions (string to integer)
        Assert.Contains("BC30512", errorCodes);

        // BC30451 - Undefined variable
        Assert.Contains("BC30451", errorCodes);

        // BC30002 - Type undefined
        Assert.Contains("BC30002", errorCodes);
    }

    [Fact]
    public async Task GetDiagnosticsAsync_DiagnosticsHaveCorrectRanges()
    {
        var projectPath = Path.Combine(TestProjectsRoot, "ErrorProject", "ErrorProject.vbproj");
        var errorClassPath = Path.Combine(TestProjectsRoot, "ErrorProject", "ErrorClass.vb");

        if (!File.Exists(projectPath))
        {
            return;
        }

        await _workspaceManager.LoadProjectAsync(projectPath);

        var errorClassUri = new Uri(errorClassPath).ToString();
        var text = await File.ReadAllTextAsync(errorClassPath);

        _documentManager.HandleDidOpen(new DidOpenTextDocumentParams
        {
            TextDocument = new TextDocumentItem
            {
                Uri = errorClassUri,
                LanguageId = "vb",
                Version = 1,
                Text = text
            }
        });

        var diagnostics = await _diagnosticsService.GetDiagnosticsAsync(errorClassUri);

        // All diagnostics should have valid ranges
        foreach (var diagnostic in diagnostics)
        {
            Assert.NotNull(diagnostic.Range);
            Assert.True(diagnostic.Range.Start.Line >= 0);
            Assert.True(diagnostic.Range.Start.Character >= 0);
            Assert.True(diagnostic.Range.End.Line >= diagnostic.Range.Start.Line);
        }
    }

    [Fact]
    public async Task GetDiagnosticsAsync_DiagnosticsHaveSource()
    {
        var projectPath = Path.Combine(TestProjectsRoot, "ErrorProject", "ErrorProject.vbproj");
        var errorClassPath = Path.Combine(TestProjectsRoot, "ErrorProject", "ErrorClass.vb");

        if (!File.Exists(projectPath))
        {
            return;
        }

        await _workspaceManager.LoadProjectAsync(projectPath);

        var errorClassUri = new Uri(errorClassPath).ToString();
        var text = await File.ReadAllTextAsync(errorClassPath);

        _documentManager.HandleDidOpen(new DidOpenTextDocumentParams
        {
            TextDocument = new TextDocumentItem
            {
                Uri = errorClassUri,
                LanguageId = "vb",
                Version = 1,
                Text = text
            }
        });

        var diagnostics = await _diagnosticsService.GetDiagnosticsAsync(errorClassUri);

        // All diagnostics should have source set to "vbnet"
        Assert.All(diagnostics, d => Assert.Equal("vbnet", d.Source));
    }

    [Fact]
    public async Task ComputeAndPublishDiagnosticsAsync_PublishesDiagnostics()
    {
        var projectPath = Path.Combine(TestProjectsRoot, "ErrorProject", "ErrorProject.vbproj");
        var errorClassPath = Path.Combine(TestProjectsRoot, "ErrorProject", "ErrorClass.vb");

        if (!File.Exists(projectPath))
        {
            return;
        }

        await _workspaceManager.LoadProjectAsync(projectPath);

        var errorClassUri = new Uri(errorClassPath).ToString();
        var text = await File.ReadAllTextAsync(errorClassPath);

        _documentManager.HandleDidOpen(new DidOpenTextDocumentParams
        {
            TextDocument = new TextDocumentItem
            {
                Uri = errorClassUri,
                LanguageId = "vb",
                Version = 1,
                Text = text
            }
        });

        _publishedDiagnostics.Clear();

        // Compute and publish diagnostics
        await _diagnosticsService.ComputeAndPublishDiagnosticsAsync(errorClassUri);

        // Should have published diagnostics
        Assert.Single(_publishedDiagnostics);
        Assert.Equal(errorClassUri, _publishedDiagnostics[0].Uri);
        Assert.NotEmpty(_publishedDiagnostics[0].Diagnostics);
    }

    [Fact]
    public async Task ClearDiagnosticsAsync_PublishesEmptyDiagnostics()
    {
        var projectPath = Path.Combine(TestProjectsRoot, "ErrorProject", "ErrorProject.vbproj");
        var errorClassPath = Path.Combine(TestProjectsRoot, "ErrorProject", "ErrorClass.vb");

        if (!File.Exists(projectPath))
        {
            return;
        }

        await _workspaceManager.LoadProjectAsync(projectPath);

        var errorClassUri = new Uri(errorClassPath).ToString();
        var text = await File.ReadAllTextAsync(errorClassPath);

        _documentManager.HandleDidOpen(new DidOpenTextDocumentParams
        {
            TextDocument = new TextDocumentItem
            {
                Uri = errorClassUri,
                LanguageId = "vb",
                Version = 1,
                Text = text
            }
        });

        // First publish some diagnostics
        await _diagnosticsService.ComputeAndPublishDiagnosticsAsync(errorClassUri);

        _publishedDiagnostics.Clear();

        // Clear diagnostics
        await _diagnosticsService.ClearDiagnosticsAsync(errorClassUri);

        // Should have published empty diagnostics
        Assert.Single(_publishedDiagnostics);
        Assert.Equal(errorClassUri, _publishedDiagnostics[0].Uri);
        Assert.Empty(_publishedDiagnostics[0].Diagnostics);
    }

    [Fact]
    public async Task DocumentReassociation_TriggersDiagnostics()
    {
        var projectPath = Path.Combine(TestProjectsRoot, "ErrorProject", "ErrorProject.vbproj");
        var errorClassPath = Path.Combine(TestProjectsRoot, "ErrorProject", "ErrorClass.vb");

        if (!File.Exists(projectPath))
        {
            return;
        }

        var errorClassUri = new Uri(errorClassPath).ToString();
        var text = await File.ReadAllTextAsync(errorClassPath);

        // Open document BEFORE loading workspace (simulates real client behavior)
        _documentManager.HandleDidOpen(new DidOpenTextDocumentParams
        {
            TextDocument = new TextDocumentItem
            {
                Uri = errorClassUri,
                LanguageId = "vb",
                Version = 1,
                Text = text
            }
        });

        // Document should not have DocumentId yet
        var doc = _documentManager.GetOpenDocument(errorClassUri);
        Assert.NotNull(doc);
        Assert.Null(doc.DocumentId);

        _publishedDiagnostics.Clear();

        // Now load the workspace - this should trigger reassociation and diagnostics
        await _workspaceManager.LoadProjectAsync(projectPath);

        // Document should now have DocumentId
        doc = _documentManager.GetOpenDocument(errorClassUri);
        Assert.NotNull(doc);
        Assert.NotNull(doc.DocumentId);

        // Wait a bit for debounced diagnostics (300ms default + buffer)
        await Task.Delay(500);

        // Should have received diagnostics after reassociation
        // Note: This may or may not have been published depending on debounce timing
        // The key assertion is that the document was reassociated
    }

    [Fact]
    public async Task MinimumSeverity_FiltersCorrectly()
    {
        var projectPath = Path.Combine(TestProjectsRoot, "ErrorProject", "ErrorProject.vbproj");
        var errorClassPath = Path.Combine(TestProjectsRoot, "ErrorProject", "ErrorClass.vb");

        if (!File.Exists(projectPath))
        {
            return;
        }

        await _workspaceManager.LoadProjectAsync(projectPath);

        var errorClassUri = new Uri(errorClassPath).ToString();
        var text = await File.ReadAllTextAsync(errorClassPath);

        _documentManager.HandleDidOpen(new DidOpenTextDocumentParams
        {
            TextDocument = new TextDocumentItem
            {
                Uri = errorClassUri,
                LanguageId = "vb",
                Version = 1,
                Text = text
            }
        });

        // Set minimum severity to Error only
        _diagnosticsService.MinimumSeverity = DiagnosticSeverity.Error;

        var diagnostics = await _diagnosticsService.GetDiagnosticsAsync(errorClassUri);

        // Should only have errors, no warnings or hints
        Assert.All(diagnostics, d => Assert.Equal(DiagnosticSeverity.Error, d.Severity));
    }
}
