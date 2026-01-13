using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.Text;
using Microsoft.CodeAnalysis.VisualBasic.Syntax;
using Microsoft.Extensions.Logging.Abstractions;
using VbNet.LanguageServer.Protocol;
using VbNet.LanguageServer.Services;
using VbNet.LanguageServer.Workspace;
using Xunit;

namespace VbNet.LanguageServer.Tests.Integration;

/// <summary>
/// Integration tests for SignatureHelpService with real VB.NET projects.
/// </summary>
[Collection("MSBuild")]
public class SignatureHelpIntegrationTests : IAsyncLifetime
{
    private readonly WorkspaceManager _workspaceManager;
    private readonly DocumentManager _documentManager;
    private readonly SignatureHelpService _signatureHelpService;

    private static readonly string TestProjectsRoot = GetTestProjectsRoot();

    public SignatureHelpIntegrationTests()
    {
        _workspaceManager = new WorkspaceManager(NullLogger<WorkspaceManager>.Instance);
        _documentManager = new DocumentManager(_workspaceManager, NullLogger<DocumentManager>.Instance);
        _signatureHelpService = new SignatureHelpService(
            _workspaceManager,
            _documentManager,
            NullLogger<SignatureHelpService>.Instance);
    }

    private static string GetTestProjectsRoot()
    {
        var assemblyLocation = typeof(SignatureHelpIntegrationTests).Assembly.Location;
        var assemblyDir = Path.GetDirectoryName(assemblyLocation)!;
        return Path.GetFullPath(Path.Combine(assemblyDir, "..", "..", "..", "..", "TestProjects"));
    }

    public Task InitializeAsync()
    {
        _workspaceManager.Initialize();
        return Task.CompletedTask;
    }

    public async Task DisposeAsync()
    {
        await _workspaceManager.DisposeAsync();
    }

    [Fact]
    public async Task GetSignatureHelpAsync_OnMethodCall_ReturnsSignatures()
    {
        var projectPath = Path.Combine(TestProjectsRoot, "SmallProject", "SmallProject.vbproj");
        var helperPath = Path.Combine(TestProjectsRoot, "SmallProject", "Helper.vb");

        if (!File.Exists(projectPath))
        {
            return;
        }

        await _workspaceManager.LoadProjectAsync(projectPath);

        var helperUri = new Uri(helperPath).ToString();
        var text = await File.ReadAllTextAsync(helperPath);
        var updatedText = InsertSignatureHelpSnippet(text);

        _documentManager.HandleDidOpen(new DidOpenTextDocumentParams
        {
            TextDocument = new TextDocumentItem
            {
                Uri = helperUri,
                LanguageId = "vb",
                Version = 1,
                Text = updatedText
            }
        });

        var document = _documentManager.GetRoslynDocument(helperUri);
        Assert.NotNull(document);

        var root = await document!.GetSyntaxRootAsync(CancellationToken.None);
        Assert.NotNull(root);

        var invocation = root!.DescendantNodes()
            .OfType<InvocationExpressionSyntax>()
            .FirstOrDefault(node => node.ToString().Contains("Add(1, 2)", StringComparison.Ordinal));
        Assert.NotNull(invocation);

        var semanticModel = await document.GetSemanticModelAsync(CancellationToken.None);
        Assert.NotNull(semanticModel);

        var symbolInfo = semanticModel!.GetSymbolInfo(invocation!, CancellationToken.None);
        var methodSymbol = symbolInfo.Symbol as IMethodSymbol
            ?? symbolInfo.CandidateSymbols.OfType<IMethodSymbol>().FirstOrDefault();
        Assert.NotNull(methodSymbol);

        var sourceText = SourceText.From(updatedText);
        var marker = "Add(1, 2)";
        var markerIndex = updatedText.IndexOf(marker, StringComparison.Ordinal);
        if (markerIndex < 0)
        {
            return;
        }

        var positionOffset = markerIndex + "Add(".Length;
        var line = sourceText.Lines.GetLineFromPosition(positionOffset);
        var position = new Position
        {
            Line = line.LineNumber,
            Character = positionOffset - line.Start
        };

        var result = await _signatureHelpService.GetSignatureHelpAsync(new SignatureHelpParams
        {
            TextDocument = new TextDocumentIdentifier { Uri = helperUri },
            Position = position
        }, CancellationToken.None);

        Assert.NotNull(result);
        Assert.NotEmpty(result!.Signatures);
        Assert.Contains(result.Signatures, sig => sig.Label.Contains("Add", StringComparison.OrdinalIgnoreCase));
    }

    private static string InsertSignatureHelpSnippet(string text)
    {
        const string snippet = """

    Public Sub SignatureHelpTest()
        Dim result = Add(1, 2)
    End Sub
""";

        var endClassIndex = text.LastIndexOf("End Class", StringComparison.OrdinalIgnoreCase);
        if (endClassIndex < 0)
        {
            return text + snippet;
        }

        return text.Insert(endClassIndex, snippet);
    }
}
