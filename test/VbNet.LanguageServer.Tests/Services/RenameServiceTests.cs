using Microsoft.Extensions.Logging.Abstractions;
using VbNet.LanguageServer.Protocol;
using VbNet.LanguageServer.Services;
using VbNet.LanguageServer.Workspace;
using Xunit;

namespace VbNet.LanguageServer.Tests.Services;

/// <summary>
/// Unit tests for RenameService.
/// </summary>
public class RenameServiceTests
{
    private readonly WorkspaceManager _workspaceManager;
    private readonly DocumentManager _documentManager;
    private readonly RenameService _renameService;

    public RenameServiceTests()
    {
        _workspaceManager = new WorkspaceManager(NullLogger<WorkspaceManager>.Instance);
        _documentManager = new DocumentManager(_workspaceManager, NullLogger<DocumentManager>.Instance);
        _renameService = new RenameService(
            _workspaceManager,
            _documentManager,
            NullLogger<RenameService>.Instance);

        _workspaceManager.Initialize();
    }

    [Fact]
    public async Task PrepareRenameAsync_NoDocument_ReturnsNull()
    {
        var @params = new PrepareRenameParams
        {
            TextDocument = new TextDocumentIdentifier { Uri = "file:///nonexistent.vb" },
            Position = new Position { Line = 0, Character = 0 }
        };

        var result = await _renameService.PrepareRenameAsync(@params, CancellationToken.None);

        Assert.Null(result);
    }

    [Fact]
    public async Task PrepareRenameAsync_NullParams_ReturnsNull()
    {
        var result = await _renameService.PrepareRenameAsync(null!, CancellationToken.None);

        Assert.Null(result);
    }

    [Fact]
    public async Task RenameAsync_NoDocument_ReturnsNull()
    {
        var @params = new RenameParams
        {
            TextDocument = new TextDocumentIdentifier { Uri = "file:///nonexistent.vb" },
            Position = new Position { Line = 0, Character = 0 },
            NewName = "NewName"
        };

        var result = await _renameService.RenameAsync(@params, CancellationToken.None);

        Assert.Null(result);
    }

    [Fact]
    public async Task RenameAsync_NullParams_ReturnsNull()
    {
        var result = await _renameService.RenameAsync(null!, CancellationToken.None);

        Assert.Null(result);
    }

    [Fact]
    public async Task RenameAsync_EmptyNewName_ReturnsNull()
    {
        var @params = new RenameParams
        {
            TextDocument = new TextDocumentIdentifier { Uri = "file:///test.vb" },
            Position = new Position { Line = 0, Character = 0 },
            NewName = ""
        };

        var result = await _renameService.RenameAsync(@params, CancellationToken.None);

        Assert.Null(result);
    }
}
