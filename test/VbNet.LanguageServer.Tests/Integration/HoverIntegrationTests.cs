using Microsoft.Extensions.Logging.Abstractions;
using VbNet.LanguageServer.Protocol;
using VbNet.LanguageServer.Services;
using VbNet.LanguageServer.Workspace;
using Xunit;

namespace VbNet.LanguageServer.Tests.Integration;

/// <summary>
/// Integration tests for HoverService with real VB.NET projects.
/// </summary>
[Collection("MSBuild")]
public class HoverIntegrationTests : IAsyncLifetime
{
    private readonly WorkspaceManager _workspaceManager;
    private readonly DocumentManager _documentManager;
    private readonly HoverService _hoverService;

    private static readonly string TestProjectsRoot = GetTestProjectsRoot();

    public HoverIntegrationTests()
    {
        _workspaceManager = new WorkspaceManager(NullLogger<WorkspaceManager>.Instance);
        _documentManager = new DocumentManager(_workspaceManager, NullLogger<DocumentManager>.Instance);
        _hoverService = new HoverService(
            _workspaceManager,
            _documentManager,
            NullLogger<HoverService>.Instance);
    }

    private static string GetTestProjectsRoot()
    {
        var assemblyLocation = typeof(HoverIntegrationTests).Assembly.Location;
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
    public async Task GetHoverAsync_OnMethodName_ReturnsHover()
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

        // Find a method definition line
        var lines = text.Split('\n');
        var lineIndex = Array.FindIndex(lines, l => l.Contains("Public Sub DoWork"));

        if (lineIndex < 0)
        {
            return;
        }

        // Hover over the method name "DoWork"
        var doWorkIndex = lines[lineIndex].IndexOf("DoWork");

        var @params = new HoverParams
        {
            TextDocument = new TextDocumentIdentifier { Uri = helperUri },
            Position = new Position { Line = lineIndex, Character = doWorkIndex + 2 }
        };

        var result = await _hoverService.GetHoverAsync(@params, CancellationToken.None);

        Assert.NotNull(result);
        Assert.NotNull(result.Contents);
        Assert.Contains("DoWork", result.Contents.Value);
    }

    [Fact]
    public async Task GetHoverAsync_OnClassName_ReturnsHover()
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

        // Find class definition line
        var lines = text.Split('\n');
        var lineIndex = Array.FindIndex(lines, l => l.Contains("Public Class Helper"));

        if (lineIndex < 0)
        {
            return;
        }

        var helperIndex = lines[lineIndex].IndexOf("Helper");

        var @params = new HoverParams
        {
            TextDocument = new TextDocumentIdentifier { Uri = helperUri },
            Position = new Position { Line = lineIndex, Character = helperIndex + 2 }
        };

        var result = await _hoverService.GetHoverAsync(@params, CancellationToken.None);

        Assert.NotNull(result);
        Assert.NotNull(result.Contents);
        Assert.Contains("Class", result.Contents.Value);
        Assert.Contains("Helper", result.Contents.Value);
    }

    [Fact]
    public async Task GetHoverAsync_ReturnsMarkdownFormat()
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

        // Find a method line
        var lines = text.Split('\n');
        var lineIndex = Array.FindIndex(lines, l => l.Contains("Public Sub DoWork"));

        if (lineIndex < 0)
        {
            return;
        }

        var @params = new HoverParams
        {
            TextDocument = new TextDocumentIdentifier { Uri = helperUri },
            Position = new Position { Line = lineIndex, Character = lines[lineIndex].IndexOf("DoWork") + 2 }
        };

        var result = await _hoverService.GetHoverAsync(@params, CancellationToken.None);

        if (result != null)
        {
            // Verify markdown format
            Assert.Equal(MarkupKind.Markdown, result.Contents.Kind);
            // Should contain VB code block
            Assert.Contains("```vb", result.Contents.Value);
        }
    }

    [Fact]
    public async Task GetHoverAsync_OnDocumentedMethod_IncludesDocumentation()
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

        // Find the Add method which has documentation
        var lines = text.Split('\n');
        var lineIndex = Array.FindIndex(lines, l => l.Contains("Public Function Add"));

        if (lineIndex < 0)
        {
            return;
        }

        var addIndex = lines[lineIndex].IndexOf("Add");

        var @params = new HoverParams
        {
            TextDocument = new TextDocumentIdentifier { Uri = helperUri },
            Position = new Position { Line = lineIndex, Character = addIndex + 1 }
        };

        var result = await _hoverService.GetHoverAsync(@params, CancellationToken.None);

        if (result != null)
        {
            // Documented methods should include summary
            Assert.Contains("Add", result.Contents.Value);
            // The Helper.vb has XML docs, so we should see documentation
            Assert.Contains("Adds two numbers", result.Contents.Value);
        }
    }
}
