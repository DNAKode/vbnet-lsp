using Microsoft.Extensions.Logging.Abstractions;
using VbNet.LanguageServer.Protocol;
using VbNet.LanguageServer.Services;
using VbNet.LanguageServer.Workspace;
using Xunit;

namespace VbNet.LanguageServer.Tests.Integration;

/// <summary>
/// Integration tests for SymbolsService with real VB.NET projects.
/// </summary>
[Collection("MSBuild")]
public class SymbolsIntegrationTests : IAsyncLifetime
{
    private readonly WorkspaceManager _workspaceManager;
    private readonly DocumentManager _documentManager;
    private readonly SymbolsService _symbolsService;

    private static readonly string TestProjectsRoot = GetTestProjectsRoot();

    public SymbolsIntegrationTests()
    {
        _workspaceManager = new WorkspaceManager(NullLogger<WorkspaceManager>.Instance);
        _documentManager = new DocumentManager(_workspaceManager, NullLogger<DocumentManager>.Instance);
        _symbolsService = new SymbolsService(
            _workspaceManager,
            _documentManager,
            NullLogger<SymbolsService>.Instance);
    }

    private static string GetTestProjectsRoot()
    {
        var assemblyLocation = typeof(SymbolsIntegrationTests).Assembly.Location;
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
    public async Task GetDocumentSymbolsAsync_ReturnsClassSymbol()
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

        var @params = new DocumentSymbolParams
        {
            TextDocument = new TextDocumentIdentifier { Uri = helperUri }
        };

        var result = await _symbolsService.GetDocumentSymbolsAsync(@params, CancellationToken.None);

        Assert.NotEmpty(result);
        // Should find the Helper class
        Assert.Contains(result, s => s.Name == "Helper" && s.Kind == SymbolKind.Class);
    }

    [Fact]
    public async Task GetDocumentSymbolsAsync_ReturnsMethodsAsChildren()
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

        var @params = new DocumentSymbolParams
        {
            TextDocument = new TextDocumentIdentifier { Uri = helperUri }
        };

        var result = await _symbolsService.GetDocumentSymbolsAsync(@params, CancellationToken.None);

        var helperClass = result.FirstOrDefault(s => s.Name == "Helper");
        Assert.NotNull(helperClass);
        Assert.NotNull(helperClass.Children);
        // Should contain DoWork and Add methods
        Assert.Contains(helperClass.Children, c => c.Name == "DoWork" && c.Kind == SymbolKind.Method);
        Assert.Contains(helperClass.Children, c => c.Name == "Add" && c.Kind == SymbolKind.Method);
    }

    [Fact]
    public async Task GetDocumentSymbolsAsync_ReturnsModuleSymbol()
    {
        var projectPath = Path.Combine(TestProjectsRoot, "SmallProject", "SmallProject.vbproj");
        var module1Path = Path.Combine(TestProjectsRoot, "SmallProject", "Module1.vb");

        if (!File.Exists(projectPath) || !File.Exists(module1Path))
        {
            return;
        }

        await _workspaceManager.LoadProjectAsync(projectPath);

        var module1Uri = new Uri(module1Path).ToString();
        var text = await File.ReadAllTextAsync(module1Path);

        _documentManager.HandleDidOpen(new DidOpenTextDocumentParams
        {
            TextDocument = new TextDocumentItem
            {
                Uri = module1Uri,
                LanguageId = "vb",
                Version = 1,
                Text = text
            }
        });

        var @params = new DocumentSymbolParams
        {
            TextDocument = new TextDocumentIdentifier { Uri = module1Uri }
        };

        var result = await _symbolsService.GetDocumentSymbolsAsync(@params, CancellationToken.None);

        Assert.NotEmpty(result);
        // Should find the Module1 module
        Assert.Contains(result, s => s.Name == "Module1" && s.Kind == SymbolKind.Module);
    }

    [Fact]
    public async Task GetWorkspaceSymbolsAsync_FindsClassByName()
    {
        var projectPath = Path.Combine(TestProjectsRoot, "SmallProject", "SmallProject.vbproj");

        if (!File.Exists(projectPath))
        {
            return;
        }

        await _workspaceManager.LoadProjectAsync(projectPath);

        var @params = new WorkspaceSymbolParams
        {
            Query = "Helper"
        };

        var result = await _symbolsService.GetWorkspaceSymbolsAsync(@params, CancellationToken.None);

        Assert.NotEmpty(result);
        Assert.Contains(result, s => s.Name == "Helper");
    }

    [Fact]
    public async Task GetWorkspaceSymbolsAsync_FindsMethodByName()
    {
        var projectPath = Path.Combine(TestProjectsRoot, "SmallProject", "SmallProject.vbproj");

        if (!File.Exists(projectPath))
        {
            return;
        }

        await _workspaceManager.LoadProjectAsync(projectPath);

        var @params = new WorkspaceSymbolParams
        {
            Query = "DoWork"
        };

        var result = await _symbolsService.GetWorkspaceSymbolsAsync(@params, CancellationToken.None);

        Assert.NotEmpty(result);
        Assert.Contains(result, s => s.Name == "DoWork");
    }

    [Fact]
    public async Task GetWorkspaceSymbolsAsync_ReturnsValidLocations()
    {
        var projectPath = Path.Combine(TestProjectsRoot, "SmallProject", "SmallProject.vbproj");

        if (!File.Exists(projectPath))
        {
            return;
        }

        await _workspaceManager.LoadProjectAsync(projectPath);

        var @params = new WorkspaceSymbolParams
        {
            Query = "Add"
        };

        var result = await _symbolsService.GetWorkspaceSymbolsAsync(@params, CancellationToken.None);

        foreach (var symbol in result)
        {
            Assert.False(string.IsNullOrEmpty(symbol.Name));
            Assert.False(string.IsNullOrEmpty(symbol.Location.Uri));
            Assert.True(symbol.Location.Range.Start.Line >= 0);
            Assert.True(symbol.Location.Range.Start.Character >= 0);
        }
    }
}
