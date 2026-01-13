using Microsoft.Extensions.Logging.Abstractions;
using VbNet.LanguageServer.Protocol;
using VbNet.LanguageServer.Services;
using VbNet.LanguageServer.Workspace;
using Xunit;

namespace VbNet.LanguageServer.Tests.Services;

/// <summary>
/// Unit tests for SignatureHelpService.
/// </summary>
public class SignatureHelpServiceTests
{
    private readonly WorkspaceManager _workspaceManager;
    private readonly DocumentManager _documentManager;
    private readonly SignatureHelpService _signatureHelpService;

    public SignatureHelpServiceTests()
    {
        _workspaceManager = new WorkspaceManager(NullLogger<WorkspaceManager>.Instance);
        _documentManager = new DocumentManager(_workspaceManager, NullLogger<DocumentManager>.Instance);
        _signatureHelpService = new SignatureHelpService(
            _workspaceManager,
            _documentManager,
            NullLogger<SignatureHelpService>.Instance);

        _workspaceManager.Initialize();
    }

    [Fact]
    public async Task GetSignatureHelpAsync_NoDocument_ReturnsNull()
    {
        var @params = new SignatureHelpParams
        {
            TextDocument = new TextDocumentIdentifier { Uri = "file:///nonexistent.vb" },
            Position = new Position { Line = 0, Character = 0 }
        };

        var result = await _signatureHelpService.GetSignatureHelpAsync(@params, CancellationToken.None);

        Assert.Null(result);
    }

    [Fact]
    public async Task GetSignatureHelpAsync_NullParams_ReturnsNull()
    {
        var result = await _signatureHelpService.GetSignatureHelpAsync(null!, CancellationToken.None);

        Assert.Null(result);
    }
}
