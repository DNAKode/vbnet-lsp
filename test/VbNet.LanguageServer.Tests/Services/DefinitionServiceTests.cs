using Microsoft.Extensions.Logging.Abstractions;
using VbNet.LanguageServer.Protocol;
using VbNet.LanguageServer.Services;
using VbNet.LanguageServer.Workspace;
using Xunit;

namespace VbNet.LanguageServer.Tests.Services;

/// <summary>
/// Unit tests for DefinitionService.
/// </summary>
public class DefinitionServiceTests
{
    private readonly WorkspaceManager _workspaceManager;
    private readonly DocumentManager _documentManager;
    private readonly DefinitionService _definitionService;

    public DefinitionServiceTests()
    {
        _workspaceManager = new WorkspaceManager(NullLogger<WorkspaceManager>.Instance);
        _documentManager = new DocumentManager(_workspaceManager, NullLogger<DocumentManager>.Instance);
        _definitionService = new DefinitionService(
            _workspaceManager,
            _documentManager,
            NullLogger<DefinitionService>.Instance);

        _workspaceManager.Initialize();
    }

    [Fact]
    public async Task GetDefinitionAsync_NoDocument_ReturnsEmpty()
    {
        var @params = new DefinitionParams
        {
            TextDocument = new TextDocumentIdentifier { Uri = "file:///nonexistent.vb" },
            Position = new Position { Line = 0, Character = 0 }
        };

        var result = await _definitionService.GetDefinitionAsync(@params, CancellationToken.None);

        Assert.Empty(result);
    }

    [Fact]
    public async Task GetDefinitionAsync_StandaloneDocument_ReturnsEmpty()
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

        var @params = new DefinitionParams
        {
            TextDocument = new TextDocumentIdentifier { Uri = uri },
            Position = new Position { Line = 1, Character = 8 }
        };

        // Without a workspace, we can't get definition info
        var result = await _definitionService.GetDefinitionAsync(@params, CancellationToken.None);

        Assert.Empty(result);
    }

    [Fact]
    public async Task GetDefinitionAsync_NullParams_ReturnsEmpty()
    {
        var result = await _definitionService.GetDefinitionAsync(null!, CancellationToken.None);

        Assert.Empty(result);
    }

    [Fact]
    public async Task GetDefinitionAsync_NullTextDocument_ReturnsEmpty()
    {
        var @params = new DefinitionParams
        {
            TextDocument = null!,
            Position = new Position { Line = 0, Character = 0 }
        };

        var result = await _definitionService.GetDefinitionAsync(@params, CancellationToken.None);

        Assert.Empty(result);
    }
}
