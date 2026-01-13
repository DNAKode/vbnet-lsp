using Microsoft.Extensions.Logging.Abstractions;
using VbNet.LanguageServer.Protocol;
using VbNet.LanguageServer.Services;
using VbNet.LanguageServer.Workspace;
using Xunit;

namespace VbNet.LanguageServer.Tests.Integration;

/// <summary>
/// Integration tests for CodeActionsService with real VB.NET projects.
/// </summary>
[Collection("MSBuild")]
public class CodeActionsIntegrationTests : IAsyncLifetime
{
    private readonly WorkspaceManager _workspaceManager;
    private readonly DocumentManager _documentManager;
    private readonly CodeActionsService _codeActionsService;

    private static readonly string TestProjectsRoot = GetTestProjectsRoot();

    public CodeActionsIntegrationTests()
    {
        _workspaceManager = new WorkspaceManager(NullLogger<WorkspaceManager>.Instance);
        _documentManager = new DocumentManager(_workspaceManager, NullLogger<DocumentManager>.Instance);
        _codeActionsService = new CodeActionsService(
            _workspaceManager,
            _documentManager,
            NullLogger<CodeActionsService>.Instance);
    }

    private static string GetTestProjectsRoot()
    {
        var assemblyLocation = typeof(CodeActionsIntegrationTests).Assembly.Location;
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
    public async Task GetCodeActionsAsync_ReturnsOptionActions()
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

        var result = await _codeActionsService.GetCodeActionsAsync(new CodeActionParams
        {
            TextDocument = new TextDocumentIdentifier { Uri = helperUri },
            Range = new VbNet.LanguageServer.Protocol.Range
            {
                Start = new Position(0, 0),
                End = new Position(0, 0)
            },
            Context = new CodeActionContext()
        }, CancellationToken.None);

        Assert.NotNull(result);
        Assert.Contains(result, action => action.Title.Contains("Option Strict", StringComparison.OrdinalIgnoreCase));
        Assert.Contains(result, action => action.Title.Contains("Option Explicit", StringComparison.OrdinalIgnoreCase));
    }
}
