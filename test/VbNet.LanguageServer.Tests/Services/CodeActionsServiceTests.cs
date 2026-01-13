using Microsoft.Extensions.Logging.Abstractions;
using VbNet.LanguageServer.Protocol;
using VbNet.LanguageServer.Services;
using VbNet.LanguageServer.Workspace;
using Xunit;

namespace VbNet.LanguageServer.Tests.Services;

/// <summary>
/// Unit tests for CodeActionsService.
/// </summary>
public class CodeActionsServiceTests
{
    private readonly WorkspaceManager _workspaceManager;
    private readonly DocumentManager _documentManager;
    private readonly CodeActionsService _codeActionsService;

    public CodeActionsServiceTests()
    {
        _workspaceManager = new WorkspaceManager(NullLogger<WorkspaceManager>.Instance);
        _documentManager = new DocumentManager(_workspaceManager, NullLogger<DocumentManager>.Instance);
        _codeActionsService = new CodeActionsService(
            _workspaceManager,
            _documentManager,
            NullLogger<CodeActionsService>.Instance);

        _workspaceManager.Initialize();
    }

    [Fact]
    public async Task GetCodeActionsAsync_NoDocument_ReturnsEmpty()
    {
        var result = await _codeActionsService.GetCodeActionsAsync(new CodeActionParams
        {
            TextDocument = new TextDocumentIdentifier { Uri = "file:///nonexistent.vb" },
            Range = new VbNet.LanguageServer.Protocol.Range(),
            Context = new CodeActionContext()
        }, CancellationToken.None);

        Assert.NotNull(result);
        Assert.Empty(result);
    }

    [Fact]
    public async Task GetCodeActionsAsync_NullParams_ReturnsEmpty()
    {
        var result = await _codeActionsService.GetCodeActionsAsync(null!, CancellationToken.None);

        Assert.NotNull(result);
        Assert.Empty(result);
    }
}
