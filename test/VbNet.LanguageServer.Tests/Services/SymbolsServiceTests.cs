using Microsoft.Extensions.Logging.Abstractions;
using VbNet.LanguageServer.Protocol;
using VbNet.LanguageServer.Services;
using VbNet.LanguageServer.Workspace;
using Xunit;

namespace VbNet.LanguageServer.Tests.Services;

/// <summary>
/// Unit tests for SymbolsService.
/// </summary>
public class SymbolsServiceTests
{
    private readonly WorkspaceManager _workspaceManager;
    private readonly DocumentManager _documentManager;
    private readonly SymbolsService _symbolsService;

    public SymbolsServiceTests()
    {
        _workspaceManager = new WorkspaceManager(NullLogger<WorkspaceManager>.Instance);
        _documentManager = new DocumentManager(_workspaceManager, NullLogger<DocumentManager>.Instance);
        _symbolsService = new SymbolsService(
            _workspaceManager,
            _documentManager,
            NullLogger<SymbolsService>.Instance);

        _workspaceManager.Initialize();
    }

    [Fact]
    public async Task GetDocumentSymbolsAsync_NoDocument_ReturnsEmpty()
    {
        var @params = new DocumentSymbolParams
        {
            TextDocument = new TextDocumentIdentifier { Uri = "file:///nonexistent.vb" }
        };

        var result = await _symbolsService.GetDocumentSymbolsAsync(@params, CancellationToken.None);

        Assert.Empty(result);
    }

    [Fact]
    public async Task GetDocumentSymbolsAsync_NullParams_ReturnsEmpty()
    {
        var result = await _symbolsService.GetDocumentSymbolsAsync(null!, CancellationToken.None);

        Assert.Empty(result);
    }

    [Fact]
    public async Task GetDocumentSymbolsAsync_NullTextDocument_ReturnsEmpty()
    {
        var @params = new DocumentSymbolParams
        {
            TextDocument = null!
        };

        var result = await _symbolsService.GetDocumentSymbolsAsync(@params, CancellationToken.None);

        Assert.Empty(result);
    }

    [Fact]
    public async Task GetWorkspaceSymbolsAsync_NoSolution_ReturnsEmpty()
    {
        var @params = new WorkspaceSymbolParams
        {
            Query = "Test"
        };

        var result = await _symbolsService.GetWorkspaceSymbolsAsync(@params, CancellationToken.None);

        Assert.Empty(result);
    }

    [Fact]
    public async Task GetWorkspaceSymbolsAsync_NullParams_ReturnsEmpty()
    {
        var result = await _symbolsService.GetWorkspaceSymbolsAsync(null!, CancellationToken.None);

        Assert.Empty(result);
    }

    [Fact]
    public async Task GetWorkspaceSymbolsAsync_EmptyQuery_ReturnsEmpty()
    {
        var @params = new WorkspaceSymbolParams
        {
            Query = ""
        };

        var result = await _symbolsService.GetWorkspaceSymbolsAsync(@params, CancellationToken.None);

        Assert.Empty(result);
    }
}
