using Microsoft.Extensions.Logging.Abstractions;
using VbNet.LanguageServer.Protocol;
using VbNet.LanguageServer.Services;
using VbNet.LanguageServer.Workspace;
using Xunit;

namespace VbNet.LanguageServer.Tests.Integration;

/// <summary>
/// Integration tests for SemanticTokensService with real VB.NET projects.
/// </summary>
[Collection("MSBuild")]
public class SemanticTokensIntegrationTests : IAsyncLifetime
{
    private readonly WorkspaceManager _workspaceManager;
    private readonly DocumentManager _documentManager;
    private readonly SemanticTokensService _semanticTokensService;

    private static readonly string TestProjectsRoot = GetTestProjectsRoot();

    public SemanticTokensIntegrationTests()
    {
        _workspaceManager = new WorkspaceManager(NullLogger<WorkspaceManager>.Instance);
        _documentManager = new DocumentManager(_workspaceManager, NullLogger<DocumentManager>.Instance);
        _semanticTokensService = new SemanticTokensService(
            _workspaceManager,
            _documentManager,
            NullLogger<SemanticTokensService>.Instance);
    }

    private static string GetTestProjectsRoot()
    {
        var assemblyLocation = typeof(SemanticTokensIntegrationTests).Assembly.Location;
        var assemblyDir = Path.GetDirectoryName(assemblyLocation)!;
        return Path.GetFullPath(Path.Combine(assemblyDir, "..", "..", "..", "..", "TestProjects"));
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
    public async Task GetSemanticTokensAsync_ReturnsTokenData()
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

        var result = await _semanticTokensService.GetSemanticTokensAsync(new SemanticTokensParams
        {
            TextDocument = new TextDocumentIdentifier { Uri = helperUri }
        }, CancellationToken.None);

        Assert.NotNull(result);
        Assert.NotEmpty(result.Data);
        Assert.True(result.Data.Length % 5 == 0);

        var legend = SemanticTokensService.GetLegend();
        for (var i = 0; i < result.Data.Length; i += 5)
        {
            var tokenType = result.Data[i + 3];
            Assert.True(tokenType < legend.TokenTypes.Length);
        }
    }
}
