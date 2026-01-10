using Microsoft.Extensions.Logging.Abstractions;
using VbNet.LanguageServer.Protocol;
using VbNet.LanguageServer.Services;
using VbNet.LanguageServer.Workspace;
using Xunit;

namespace VbNet.LanguageServer.Tests.Services;

/// <summary>
/// Unit tests for ReferencesService.
/// </summary>
public class ReferencesServiceTests
{
    private readonly WorkspaceManager _workspaceManager;
    private readonly DocumentManager _documentManager;
    private readonly ReferencesService _referencesService;

    public ReferencesServiceTests()
    {
        _workspaceManager = new WorkspaceManager(NullLogger<WorkspaceManager>.Instance);
        _documentManager = new DocumentManager(_workspaceManager, NullLogger<DocumentManager>.Instance);
        _referencesService = new ReferencesService(
            _workspaceManager,
            _documentManager,
            NullLogger<ReferencesService>.Instance);

        _workspaceManager.Initialize();
    }

    [Fact]
    public async Task GetReferencesAsync_NoDocument_ReturnsEmpty()
    {
        var @params = new ReferenceParams
        {
            TextDocument = new TextDocumentIdentifier { Uri = "file:///nonexistent.vb" },
            Position = new Position { Line = 0, Character = 0 },
            Context = new ReferenceContext { IncludeDeclaration = true }
        };

        var result = await _referencesService.GetReferencesAsync(@params, CancellationToken.None);

        Assert.Empty(result);
    }

    [Fact]
    public async Task GetReferencesAsync_StandaloneDocument_ReturnsEmpty()
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

        var @params = new ReferenceParams
        {
            TextDocument = new TextDocumentIdentifier { Uri = uri },
            Position = new Position { Line = 1, Character = 8 },
            Context = new ReferenceContext { IncludeDeclaration = true }
        };

        // Without a workspace, we can't get reference info
        var result = await _referencesService.GetReferencesAsync(@params, CancellationToken.None);

        Assert.Empty(result);
    }

    [Fact]
    public async Task GetReferencesAsync_NullParams_ReturnsEmpty()
    {
        var result = await _referencesService.GetReferencesAsync(null!, CancellationToken.None);

        Assert.Empty(result);
    }

    [Fact]
    public async Task GetReferencesAsync_NullTextDocument_ReturnsEmpty()
    {
        var @params = new ReferenceParams
        {
            TextDocument = null!,
            Position = new Position { Line = 0, Character = 0 },
            Context = new ReferenceContext { IncludeDeclaration = true }
        };

        var result = await _referencesService.GetReferencesAsync(@params, CancellationToken.None);

        Assert.Empty(result);
    }
}
