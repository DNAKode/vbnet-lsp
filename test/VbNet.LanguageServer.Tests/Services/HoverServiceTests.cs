using Microsoft.Extensions.Logging.Abstractions;
using VbNet.LanguageServer.Protocol;
using VbNet.LanguageServer.Services;
using VbNet.LanguageServer.Workspace;
using Xunit;

namespace VbNet.LanguageServer.Tests.Services;

/// <summary>
/// Unit tests for HoverService.
/// </summary>
public class HoverServiceTests
{
    private readonly WorkspaceManager _workspaceManager;
    private readonly DocumentManager _documentManager;
    private readonly HoverService _hoverService;

    public HoverServiceTests()
    {
        _workspaceManager = new WorkspaceManager(NullLogger<WorkspaceManager>.Instance);
        _documentManager = new DocumentManager(_workspaceManager, NullLogger<DocumentManager>.Instance);
        _hoverService = new HoverService(
            _workspaceManager,
            _documentManager,
            NullLogger<HoverService>.Instance);

        _workspaceManager.Initialize();
    }

    [Fact]
    public async Task GetHoverAsync_NoDocument_ReturnsNull()
    {
        var @params = new HoverParams
        {
            TextDocument = new TextDocumentIdentifier { Uri = "file:///nonexistent.vb" },
            Position = new Position { Line = 0, Character = 0 }
        };

        var result = await _hoverService.GetHoverAsync(@params, CancellationToken.None);

        Assert.Null(result);
    }

    [Fact]
    public async Task GetHoverAsync_StandaloneDocument_ReturnsNull()
    {
        var uri = "file:///c:/test/module.vb";
        var text = "Module Module1\n    Sub Main()\n    End Sub\nEnd Module";

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

        var @params = new HoverParams
        {
            TextDocument = new TextDocumentIdentifier { Uri = uri },
            Position = new Position { Line = 1, Character = 8 }
        };

        // Without a workspace, we can't get hover info
        var result = await _hoverService.GetHoverAsync(@params, CancellationToken.None);

        Assert.Null(result);
    }

    [Fact]
    public async Task GetHoverAsync_NullParams_ReturnsNull()
    {
        var result = await _hoverService.GetHoverAsync(null!, CancellationToken.None);

        Assert.Null(result);
    }

    [Fact]
    public async Task GetHoverAsync_NullTextDocument_ReturnsNull()
    {
        var @params = new HoverParams
        {
            TextDocument = null!,
            Position = new Position { Line = 0, Character = 0 }
        };

        var result = await _hoverService.GetHoverAsync(@params, CancellationToken.None);

        Assert.Null(result);
    }
}
