using Microsoft.Extensions.Logging.Abstractions;
using VbNet.LanguageServer.Protocol;
using VbNet.LanguageServer.Services;
using VbNet.LanguageServer.Workspace;
using Xunit;

namespace VbNet.LanguageServer.Tests.Services;

/// <summary>
/// Unit tests for CompletionService.
/// </summary>
public class CompletionServiceTests
{
    private readonly WorkspaceManager _workspaceManager;
    private readonly DocumentManager _documentManager;
    private readonly CompletionService _completionService;

    public CompletionServiceTests()
    {
        _workspaceManager = new WorkspaceManager(NullLogger<WorkspaceManager>.Instance);
        _documentManager = new DocumentManager(_workspaceManager, NullLogger<DocumentManager>.Instance);
        _completionService = new CompletionService(
            _workspaceManager,
            _documentManager,
            NullLogger<CompletionService>.Instance);

        _workspaceManager.Initialize();
    }

    [Fact]
    public async Task GetCompletionAsync_NoDocument_ReturnsEmptyList()
    {
        var @params = new CompletionParams
        {
            TextDocument = new TextDocumentIdentifier { Uri = "file:///nonexistent.vb" },
            Position = new Position { Line = 0, Character = 0 }
        };

        var result = await _completionService.GetCompletionAsync(@params, CancellationToken.None);

        Assert.NotNull(result);
        Assert.False(result.IsIncomplete);
        Assert.Empty(result.Items);
    }

    [Fact]
    public async Task GetCompletionAsync_StandaloneDocument_ReturnsEmptyList()
    {
        var uri = "file:///c:/test/module.vb";
        var text = "Module Module1\n    Sub Main()\n        \n    End Sub\nEnd Module";

        _documentManager.HandleDidOpen(new DidOpenTextDocumentParams
        {
            TextDocument = new TextDocumentItem
            {
                Uri = uri,
                LanguageId = "vb",
                Version = 1,
                Text = text
            }
        });

        var @params = new CompletionParams
        {
            TextDocument = new TextDocumentIdentifier { Uri = uri },
            Position = new Position { Line = 2, Character = 8 }
        };

        // Without a workspace, we can't get completions
        var result = await _completionService.GetCompletionAsync(@params, CancellationToken.None);

        Assert.NotNull(result);
        Assert.False(result.IsIncomplete);
        // No workspace means no Roslyn document, so empty completions
        Assert.Empty(result.Items);
    }

    [Fact]
    public async Task GetCompletionAsync_NullParams_ReturnsEmptyList()
    {
        var result = await _completionService.GetCompletionAsync(null!, CancellationToken.None);

        Assert.NotNull(result);
        Assert.Empty(result.Items);
    }

    [Fact]
    public async Task ResolveCompletionItemAsync_NoData_ReturnsOriginalItem()
    {
        var item = new CompletionItem
        {
            Label = "Test",
            Kind = CompletionItemKind.Method
        };

        var result = await _completionService.ResolveCompletionItemAsync(item, CancellationToken.None);

        Assert.NotNull(result);
        Assert.Equal("Test", result.Label);
        Assert.Equal(CompletionItemKind.Method, result.Kind);
    }

    [Fact]
    public async Task ResolveCompletionItemAsync_NullItem_ReturnsEmptyItem()
    {
        var result = await _completionService.ResolveCompletionItemAsync(null!, CancellationToken.None);

        Assert.NotNull(result);
        Assert.Equal("", result.Label);
    }
}
