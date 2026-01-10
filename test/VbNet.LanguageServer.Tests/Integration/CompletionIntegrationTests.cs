using Microsoft.Build.Locator;
using Microsoft.Extensions.Logging.Abstractions;
using VbNet.LanguageServer.Protocol;
using VbNet.LanguageServer.Services;
using VbNet.LanguageServer.Workspace;
using Xunit;

namespace VbNet.LanguageServer.Tests.Integration;

/// <summary>
/// Integration tests for CompletionService with real VB.NET projects.
/// </summary>
public class CompletionIntegrationTests : IAsyncLifetime
{
    private readonly WorkspaceManager _workspaceManager;
    private readonly DocumentManager _documentManager;
    private readonly CompletionService _completionService;

    private static bool _msBuildRegistered = false;
    private static readonly object _lockObject = new();
    private static readonly string TestProjectsRoot = GetTestProjectsRoot();

    public CompletionIntegrationTests()
    {
        lock (_lockObject)
        {
            if (!_msBuildRegistered)
            {
                MSBuildLocator.RegisterDefaults();
                _msBuildRegistered = true;
            }
        }

        _workspaceManager = new WorkspaceManager(NullLogger<WorkspaceManager>.Instance);
        _documentManager = new DocumentManager(_workspaceManager, NullLogger<DocumentManager>.Instance);
        _completionService = new CompletionService(
            _workspaceManager,
            _documentManager,
            NullLogger<CompletionService>.Instance);
    }

    private static string GetTestProjectsRoot()
    {
        var assemblyLocation = typeof(CompletionIntegrationTests).Assembly.Location;
        var assemblyDir = Path.GetDirectoryName(assemblyLocation)!;
        var testProjectsPath = Path.GetFullPath(Path.Combine(assemblyDir, "..", "..", "..", "..", "TestProjects"));
        return testProjectsPath;
    }

    public Task InitializeAsync()
    {
        _workspaceManager.Initialize();
        return Task.CompletedTask;
    }

    public async Task DisposeAsync()
    {
        await _workspaceManager.DisposeAsync();
    }

    [Fact]
    public async Task GetCompletionAsync_InMethod_ReturnsResult()
    {
        var projectPath = Path.Combine(TestProjectsRoot, "SmallProject", "SmallProject.vbproj");
        var helperPath = Path.Combine(TestProjectsRoot, "SmallProject", "Helper.vb");

        if (!File.Exists(projectPath))
        {
            return;
        }

        await _workspaceManager.LoadProjectAsync(projectPath);

        var helperUri = new Uri(helperPath).ToString();
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

        // Request completion inside the DoWork method
        var lines = text.Split('\n');
        var lineIndex = Array.FindIndex(lines, l => l.Contains("_counter += 1"));

        if (lineIndex < 0)
        {
            return;
        }

        var @params = new CompletionParams
        {
            TextDocument = new TextDocumentIdentifier { Uri = helperUri },
            Position = new Position { Line = lineIndex, Character = 8 }
        };

        var result = await _completionService.GetCompletionAsync(@params, CancellationToken.None);

        // Verify we get a valid result
        Assert.NotNull(result);
        Assert.False(result.IsIncomplete);
    }

    [Fact]
    public async Task GetCompletionAsync_AtModuleLevel_ReturnsResult()
    {
        var projectPath = Path.Combine(TestProjectsRoot, "SmallProject", "SmallProject.vbproj");
        var helperPath = Path.Combine(TestProjectsRoot, "SmallProject", "Helper.vb");

        if (!File.Exists(projectPath))
        {
            return;
        }

        await _workspaceManager.LoadProjectAsync(projectPath);

        var helperUri = new Uri(helperPath).ToString();
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

        // Request completion inside class body (after a method)
        var lines = text.Split('\n');
        var lineIndex = Array.FindIndex(lines, l => l.Contains("End Function"));

        if (lineIndex < 0)
        {
            return;
        }

        var @params = new CompletionParams
        {
            TextDocument = new TextDocumentIdentifier { Uri = helperUri },
            Position = new Position { Line = lineIndex + 1, Character = 4 }
        };

        var result = await _completionService.GetCompletionAsync(@params, CancellationToken.None);

        // Just verify we get a result (may or may not have items depending on position)
        Assert.NotNull(result);
        Assert.False(result.IsIncomplete);
    }

    [Fact]
    public async Task GetCompletionAsync_CompletionItemsHaveKinds()
    {
        var projectPath = Path.Combine(TestProjectsRoot, "SmallProject", "SmallProject.vbproj");
        var helperPath = Path.Combine(TestProjectsRoot, "SmallProject", "Helper.vb");

        if (!File.Exists(projectPath))
        {
            return;
        }

        await _workspaceManager.LoadProjectAsync(projectPath);

        var helperUri = new Uri(helperPath).ToString();
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

        var @params = new CompletionParams
        {
            TextDocument = new TextDocumentIdentifier { Uri = helperUri },
            Position = new Position { Line = 5, Character = 0 }
        };

        var result = await _completionService.GetCompletionAsync(@params, CancellationToken.None);

        Assert.NotNull(result);

        if (result.Items.Length > 0)
        {
            // All items should have a kind
            Assert.All(result.Items, item => Assert.NotNull(item.Kind));
        }
    }

    [Fact]
    public async Task GetCompletionAsync_CompletionItemsHaveSortText()
    {
        var projectPath = Path.Combine(TestProjectsRoot, "SmallProject", "SmallProject.vbproj");
        var helperPath = Path.Combine(TestProjectsRoot, "SmallProject", "Helper.vb");

        if (!File.Exists(projectPath))
        {
            return;
        }

        await _workspaceManager.LoadProjectAsync(projectPath);

        var helperUri = new Uri(helperPath).ToString();
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

        var @params = new CompletionParams
        {
            TextDocument = new TextDocumentIdentifier { Uri = helperUri },
            Position = new Position { Line = 5, Character = 0 }
        };

        var result = await _completionService.GetCompletionAsync(@params, CancellationToken.None);

        Assert.NotNull(result);

        if (result.Items.Length > 0)
        {
            // All items should have sort text for proper ordering
            Assert.All(result.Items, item => Assert.NotNull(item.SortText));
        }
    }
}
