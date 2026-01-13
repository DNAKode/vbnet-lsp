using Microsoft.Extensions.Logging.Abstractions;
using VbNet.LanguageServer.Protocol;
using VbNet.LanguageServer.Services;
using VbNet.LanguageServer.Workspace;
using Xunit;

namespace VbNet.LanguageServer.Tests.Services;

/// <summary>
/// Unit tests for FoldingRangeService.
/// </summary>
public class FoldingRangeServiceTests
{
    private readonly WorkspaceManager _workspaceManager;
    private readonly DocumentManager _documentManager;
    private readonly FoldingRangeService _foldingRangeService;

    public FoldingRangeServiceTests()
    {
        _workspaceManager = new WorkspaceManager(NullLogger<WorkspaceManager>.Instance);
        _documentManager = new DocumentManager(_workspaceManager, NullLogger<DocumentManager>.Instance);
        _foldingRangeService = new FoldingRangeService(
            _workspaceManager,
            _documentManager,
            NullLogger<FoldingRangeService>.Instance);

        _workspaceManager.Initialize();
    }

    [Fact]
    public async Task GetFoldingRangesAsync_NoDocument_ReturnsEmpty()
    {
        var @params = new FoldingRangeParams
        {
            TextDocument = new TextDocumentIdentifier { Uri = "file:///nonexistent.vb" }
        };

        var result = await _foldingRangeService.GetFoldingRangesAsync(@params, CancellationToken.None);

        Assert.NotNull(result);
        Assert.Empty(result);
    }

    [Fact]
    public async Task GetFoldingRangesAsync_StandaloneDocument_ReturnsRegion()
    {
        var uri = "file:///c:/test/module.vb";
        var text = """
                   #Region "Test"
                   Module Module1
                       Sub Main()
                           Dim x = 1
                       End Sub
                   End Module
                   #End Region
                   """;

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

        var @params = new FoldingRangeParams
        {
            TextDocument = new TextDocumentIdentifier { Uri = uri }
        };

        var result = await _foldingRangeService.GetFoldingRangesAsync(@params, CancellationToken.None);

        Assert.NotNull(result);
        Assert.Contains(result, range => range.Kind == FoldingRangeKind.Region);
    }
}
