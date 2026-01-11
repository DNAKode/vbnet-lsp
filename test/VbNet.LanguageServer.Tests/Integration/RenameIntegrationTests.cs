using Microsoft.Extensions.Logging.Abstractions;
using VbNet.LanguageServer.Protocol;
using VbNet.LanguageServer.Services;
using VbNet.LanguageServer.Workspace;
using Xunit;

namespace VbNet.LanguageServer.Tests.Integration;

/// <summary>
/// Integration tests for RenameService with real VB.NET projects.
/// </summary>
[Collection("MSBuild")]
public class RenameIntegrationTests : IAsyncLifetime
{
    private readonly WorkspaceManager _workspaceManager;
    private readonly DocumentManager _documentManager;
    private readonly RenameService _renameService;

    private static readonly string TestProjectsRoot = GetTestProjectsRoot();

    public RenameIntegrationTests()
    {
        _workspaceManager = new WorkspaceManager(NullLogger<WorkspaceManager>.Instance);
        _documentManager = new DocumentManager(_workspaceManager, NullLogger<DocumentManager>.Instance);
        _renameService = new RenameService(
            _workspaceManager,
            _documentManager,
            NullLogger<RenameService>.Instance);
    }

    private static string GetTestProjectsRoot()
    {
        var assemblyLocation = typeof(RenameIntegrationTests).Assembly.Location;
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
    public async Task PrepareRenameAsync_OnMethodName_ReturnsRangeAndPlaceholder()
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

        var doWorkIndex = lines[lineIndex].IndexOf("DoWork");

        var @params = new PrepareRenameParams
        {
            TextDocument = new TextDocumentIdentifier { Uri = helperUri },
            Position = new Position { Line = lineIndex, Character = doWorkIndex + 2 }
        };

        var result = await _renameService.PrepareRenameAsync(@params, CancellationToken.None);

        Assert.NotNull(result);
        Assert.Equal("DoWork", result.Placeholder);
        Assert.Equal(lineIndex, result.Range.Start.Line);
    }

    [Fact]
    public async Task PrepareRenameAsync_OnClassName_ReturnsRangeAndPlaceholder()
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

        var @params = new PrepareRenameParams
        {
            TextDocument = new TextDocumentIdentifier { Uri = helperUri },
            Position = new Position { Line = lineIndex, Character = helperIndex + 2 }
        };

        var result = await _renameService.PrepareRenameAsync(@params, CancellationToken.None);

        Assert.NotNull(result);
        Assert.Equal("Helper", result.Placeholder);
    }

    [Fact]
    public async Task RenameAsync_OnMethodName_ReturnsWorkspaceEdit()
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

        var doWorkIndex = lines[lineIndex].IndexOf("DoWork");

        var @params = new RenameParams
        {
            TextDocument = new TextDocumentIdentifier { Uri = helperUri },
            Position = new Position { Line = lineIndex, Character = doWorkIndex + 2 },
            NewName = "DoWorkRenamed"
        };

        var result = await _renameService.RenameAsync(@params, CancellationToken.None);

        Assert.NotNull(result);
        Assert.NotNull(result.Changes);
        Assert.NotEmpty(result.Changes);
        // Should contain edits for the Helper.vb file
        Assert.Contains(result.Changes.Keys, k => k.Contains("Helper.vb"));
    }

    [Fact]
    public async Task RenameAsync_ReturnsValidTextEdits()
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

        // Find the Add method
        var lines = text.Split('\n');
        var lineIndex = Array.FindIndex(lines, l => l.Contains("Public Function Add"));

        if (lineIndex < 0)
        {
            return;
        }

        var addIndex = lines[lineIndex].IndexOf("Add");

        var @params = new RenameParams
        {
            TextDocument = new TextDocumentIdentifier { Uri = helperUri },
            Position = new Position { Line = lineIndex, Character = addIndex + 1 },
            NewName = "AddNumbers"
        };

        var result = await _renameService.RenameAsync(@params, CancellationToken.None);

        if (result?.Changes != null)
        {
            foreach (var (uri, edits) in result.Changes)
            {
                foreach (var edit in edits)
                {
                    // Verify valid range
                    Assert.True(edit.Range.Start.Line >= 0);
                    Assert.True(edit.Range.Start.Character >= 0);
                    Assert.True(edit.Range.End.Line >= edit.Range.Start.Line);
                    // Verify new text contains the new name
                    Assert.Contains("AddNumbers", edit.NewText);
                }
            }
        }
    }
}
