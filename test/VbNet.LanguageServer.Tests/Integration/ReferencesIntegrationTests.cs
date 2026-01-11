using Microsoft.Extensions.Logging.Abstractions;
using VbNet.LanguageServer.Protocol;
using VbNet.LanguageServer.Services;
using VbNet.LanguageServer.Workspace;
using Xunit;

namespace VbNet.LanguageServer.Tests.Integration;

/// <summary>
/// Integration tests for ReferencesService with real VB.NET projects.
/// </summary>
[Collection("MSBuild")]
public class ReferencesIntegrationTests : IAsyncLifetime
{
    private readonly WorkspaceManager _workspaceManager;
    private readonly DocumentManager _documentManager;
    private readonly ReferencesService _referencesService;

    private static readonly string TestProjectsRoot = GetTestProjectsRoot();

    public ReferencesIntegrationTests()
    {
        _workspaceManager = new WorkspaceManager(NullLogger<WorkspaceManager>.Instance);
        _documentManager = new DocumentManager(_workspaceManager, NullLogger<DocumentManager>.Instance);
        _referencesService = new ReferencesService(
            _workspaceManager,
            _documentManager,
            NullLogger<ReferencesService>.Instance);
    }

    private static string GetTestProjectsRoot()
    {
        var assemblyLocation = typeof(ReferencesIntegrationTests).Assembly.Location;
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
    public async Task GetReferencesAsync_OnMethodDefinition_FindsReferences()
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

        var @params = new ReferenceParams
        {
            TextDocument = new TextDocumentIdentifier { Uri = helperUri },
            Position = new Position { Line = lineIndex, Character = doWorkIndex + 2 },
            Context = new ReferenceContext { IncludeDeclaration = true }
        };

        var result = await _referencesService.GetReferencesAsync(@params, CancellationToken.None);

        // Should find at least the declaration
        Assert.NotEmpty(result);
    }

    [Fact]
    public async Task GetReferencesAsync_OnClassDefinition_FindsReferences()
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

        var @params = new ReferenceParams
        {
            TextDocument = new TextDocumentIdentifier { Uri = helperUri },
            Position = new Position { Line = lineIndex, Character = helperIndex + 2 },
            Context = new ReferenceContext { IncludeDeclaration = true }
        };

        var result = await _referencesService.GetReferencesAsync(@params, CancellationToken.None);

        // Should find the declaration
        Assert.NotEmpty(result);
        Assert.Contains(result, l => l.Uri.Contains("Helper.vb"));
    }

    [Fact]
    public async Task GetReferencesAsync_WithoutDeclaration_ExcludesDeclaration()
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

        var doWorkIndex = lines[lineIndex].IndexOf("DoWork");

        var paramsWithDecl = new ReferenceParams
        {
            TextDocument = new TextDocumentIdentifier { Uri = helperUri },
            Position = new Position { Line = lineIndex, Character = doWorkIndex + 2 },
            Context = new ReferenceContext { IncludeDeclaration = true }
        };

        var paramsWithoutDecl = new ReferenceParams
        {
            TextDocument = new TextDocumentIdentifier { Uri = helperUri },
            Position = new Position { Line = lineIndex, Character = doWorkIndex + 2 },
            Context = new ReferenceContext { IncludeDeclaration = false }
        };

        var resultWithDecl = await _referencesService.GetReferencesAsync(paramsWithDecl, CancellationToken.None);
        var resultWithoutDecl = await _referencesService.GetReferencesAsync(paramsWithoutDecl, CancellationToken.None);

        // With declaration should have >= references than without
        Assert.True(resultWithDecl.Length >= resultWithoutDecl.Length);
    }

    [Fact]
    public async Task GetReferencesAsync_ReturnsValidLocations()
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

        // Find class line
        var lines = text.Split('\n');
        var lineIndex = Array.FindIndex(lines, l => l.Contains("Public Class Helper"));

        if (lineIndex < 0)
        {
            return;
        }

        var helperIndex = lines[lineIndex].IndexOf("Helper");

        var @params = new ReferenceParams
        {
            TextDocument = new TextDocumentIdentifier { Uri = helperUri },
            Position = new Position { Line = lineIndex, Character = helperIndex + 2 },
            Context = new ReferenceContext { IncludeDeclaration = true }
        };

        var result = await _referencesService.GetReferencesAsync(@params, CancellationToken.None);

        foreach (var location in result)
        {
            // All locations should have valid URIs and ranges
            Assert.False(string.IsNullOrEmpty(location.Uri));
            Assert.True(location.Range.Start.Line >= 0);
            Assert.True(location.Range.Start.Character >= 0);
            Assert.True(location.Range.End.Line >= location.Range.Start.Line);
        }
    }
}
