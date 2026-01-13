using Microsoft.Extensions.Logging.Abstractions;
using VbNet.LanguageServer.Protocol;
using VbNet.LanguageServer.Services;
using VbNet.LanguageServer.Workspace;
using Xunit;

namespace VbNet.LanguageServer.Tests.Services;

/// <summary>
/// Unit tests for SemanticTokensService.
/// </summary>
public class SemanticTokensServiceTests
{
    private readonly WorkspaceManager _workspaceManager;
    private readonly DocumentManager _documentManager;
    private readonly SemanticTokensService _semanticTokensService;

    public SemanticTokensServiceTests()
    {
        _workspaceManager = new WorkspaceManager(NullLogger<WorkspaceManager>.Instance);
        _documentManager = new DocumentManager(_workspaceManager, NullLogger<DocumentManager>.Instance);
        _semanticTokensService = new SemanticTokensService(
            _workspaceManager,
            _documentManager,
            NullLogger<SemanticTokensService>.Instance);

        _workspaceManager.Initialize();
    }

    [Fact]
    public async Task GetSemanticTokensAsync_NoDocument_ReturnsEmpty()
    {
        var result = await _semanticTokensService.GetSemanticTokensAsync(new SemanticTokensParams
        {
            TextDocument = new TextDocumentIdentifier { Uri = "file:///nonexistent.vb" }
        }, CancellationToken.None);

        Assert.NotNull(result);
        Assert.Empty(result.Data);
    }

    [Fact]
    public async Task GetSemanticTokensAsync_NullParams_ReturnsEmpty()
    {
        var result = await _semanticTokensService.GetSemanticTokensAsync(null!, CancellationToken.None);

        Assert.NotNull(result);
        Assert.Empty(result.Data);
    }
}
