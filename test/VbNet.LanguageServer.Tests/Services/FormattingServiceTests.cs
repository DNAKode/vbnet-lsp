using Microsoft.Extensions.Logging.Abstractions;
using VbNet.LanguageServer.Protocol;
using VbNet.LanguageServer.Services;
using VbNet.LanguageServer.Workspace;
using Xunit;

namespace VbNet.LanguageServer.Tests.Services;

/// <summary>
/// Unit tests for FormattingService.
/// </summary>
public class FormattingServiceTests
{
    private readonly WorkspaceManager _workspaceManager;
    private readonly DocumentManager _documentManager;
    private readonly FormattingService _formattingService;

    public FormattingServiceTests()
    {
        _workspaceManager = new WorkspaceManager(NullLogger<WorkspaceManager>.Instance);
        _documentManager = new DocumentManager(_workspaceManager, NullLogger<DocumentManager>.Instance);
        _formattingService = new FormattingService(
            _workspaceManager,
            _documentManager,
            NullLogger<FormattingService>.Instance);

        _workspaceManager.Initialize();
    }

    [Fact]
    public async Task FormatDocumentAsync_NoDocument_ReturnsEmpty()
    {
        var @params = new DocumentFormattingParams
        {
            TextDocument = new TextDocumentIdentifier { Uri = "file:///nonexistent.vb" }
        };

        var result = await _formattingService.FormatDocumentAsync(@params, CancellationToken.None);

        Assert.NotNull(result);
        Assert.Empty(result);
    }

    [Fact]
    public async Task FormatRangeAsync_NoDocument_ReturnsEmpty()
    {
        var @params = new DocumentRangeFormattingParams
        {
            TextDocument = new TextDocumentIdentifier { Uri = "file:///nonexistent.vb" },
            Range = new VbNet.LanguageServer.Protocol.Range
            {
                Start = new Position(0, 0),
                End = new Position(0, 1)
            }
        };

        var result = await _formattingService.FormatRangeAsync(@params, CancellationToken.None);

        Assert.NotNull(result);
        Assert.Empty(result);
    }
}
