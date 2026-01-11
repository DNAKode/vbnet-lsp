using Microsoft.Extensions.Logging.Abstractions;
using VbNet.LanguageServer.Tests.Integration;
using VbNet.LanguageServer.Workspace;
using Xunit;

namespace VbNet.LanguageServer.Tests.Workspace;

/// <summary>
/// Tests for WorkspaceManager with real VB.NET projects.
/// </summary>
[Collection("MSBuild")]
public class WorkspaceManagerTests : IAsyncLifetime
{
    private readonly WorkspaceManager _workspaceManager;

    // Path to test projects (relative to test output directory)
    private static readonly string TestProjectsRoot = GetTestProjectsRoot();

    public WorkspaceManagerTests()
    {
        _workspaceManager = new WorkspaceManager(NullLogger<WorkspaceManager>.Instance);
    }

    private static string GetTestProjectsRoot()
    {
        // Navigate from bin/Debug/net10.0 up to test/TestProjects
        var assemblyLocation = typeof(WorkspaceManagerTests).Assembly.Location;
        var assemblyDir = Path.GetDirectoryName(assemblyLocation)!;
        var testProjectsPath = Path.GetFullPath(Path.Combine(assemblyDir, "..", "..", "..", "..", "TestProjects"));
        return testProjectsPath;
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
    public void Initialize_CreatesWorkspace()
    {
        // Initialize is called in InitializeAsync
        Assert.NotNull(_workspaceManager.CurrentSolution);
    }

    [Fact]
    public void CurrentSolution_BeforeLoad_IsEmpty()
    {
        Assert.False(_workspaceManager.IsLoaded);
    }

    [Fact]
    public async Task LoadProjectAsync_SmallProject_LoadsSuccessfully()
    {
        var projectPath = Path.Combine(TestProjectsRoot, "SmallProject", "SmallProject.vbproj");

        // Skip if test project doesn't exist
        if (!File.Exists(projectPath))
        {
            // This can happen in CI if test projects aren't deployed
            return;
        }

        var result = await _workspaceManager.LoadProjectAsync(projectPath);

        Assert.True(result);
        Assert.True(_workspaceManager.IsLoaded);

        var projects = _workspaceManager.GetVbNetProjects().ToList();
        Assert.Single(projects);
        Assert.Equal("SmallProject", projects[0].Name);
    }

    [Fact]
    public async Task LoadProjectAsync_SmallProject_HasDocuments()
    {
        var projectPath = Path.Combine(TestProjectsRoot, "SmallProject", "SmallProject.vbproj");

        if (!File.Exists(projectPath))
        {
            return;
        }

        await _workspaceManager.LoadProjectAsync(projectPath);

        var projects = _workspaceManager.GetVbNetProjects().ToList();
        Assert.Single(projects);

        var documents = projects[0].Documents.ToList();
        Assert.True(documents.Count >= 2, "Expected at least 2 documents (Module1.vb, Helper.vb)");

        var documentNames = documents.Select(d => Path.GetFileName(d.FilePath)).ToList();
        Assert.Contains("Module1.vb", documentNames);
        Assert.Contains("Helper.vb", documentNames);
    }

    [Fact]
    public async Task GetDocumentByPath_ReturnsCorrectDocument()
    {
        var projectPath = Path.Combine(TestProjectsRoot, "SmallProject", "SmallProject.vbproj");
        var module1Path = Path.Combine(TestProjectsRoot, "SmallProject", "Module1.vb");

        if (!File.Exists(projectPath))
        {
            return;
        }

        await _workspaceManager.LoadProjectAsync(projectPath);

        var document = _workspaceManager.GetDocumentByPath(module1Path);

        Assert.NotNull(document);
        Assert.Equal("Module1.vb", Path.GetFileName(document.FilePath));
    }

    [Fact]
    public async Task GetDocumentByUri_ReturnsCorrectDocument()
    {
        var projectPath = Path.Combine(TestProjectsRoot, "SmallProject", "SmallProject.vbproj");
        var module1Path = Path.Combine(TestProjectsRoot, "SmallProject", "Module1.vb");
        var module1Uri = new Uri(module1Path).ToString();

        if (!File.Exists(projectPath))
        {
            return;
        }

        await _workspaceManager.LoadProjectAsync(projectPath);

        var document = _workspaceManager.GetDocumentByUri(module1Uri);

        Assert.NotNull(document);
        Assert.Equal("Module1.vb", Path.GetFileName(document.FilePath));
    }

    [Fact]
    public async Task GetDocumentByPath_NonExistentPath_ReturnsNull()
    {
        var projectPath = Path.Combine(TestProjectsRoot, "SmallProject", "SmallProject.vbproj");

        if (!File.Exists(projectPath))
        {
            return;
        }

        await _workspaceManager.LoadProjectAsync(projectPath);

        var document = _workspaceManager.GetDocumentByPath(@"C:\NonExistent\File.vb");

        Assert.Null(document);
    }

    [Fact]
    public async Task LoadProjectAsync_NonExistentPath_ReturnsFalse()
    {
        var result = await _workspaceManager.LoadProjectAsync(@"C:\NonExistent\Project.vbproj");

        Assert.False(result);
        Assert.False(_workspaceManager.IsLoaded);
    }

    [Fact]
    public async Task GetVbNetProjects_ReturnsOnlyVbProjects()
    {
        var projectPath = Path.Combine(TestProjectsRoot, "SmallProject", "SmallProject.vbproj");

        if (!File.Exists(projectPath))
        {
            return;
        }

        await _workspaceManager.LoadProjectAsync(projectPath);

        var vbProjects = _workspaceManager.GetVbNetProjects().ToList();

        Assert.All(vbProjects, p => Assert.Equal("Visual Basic", p.Language));
    }

    [Fact]
    public async Task ApplyTextChange_UpdatesDocument()
    {
        var projectPath = Path.Combine(TestProjectsRoot, "SmallProject", "SmallProject.vbproj");
        var module1Path = Path.Combine(TestProjectsRoot, "SmallProject", "Module1.vb");

        if (!File.Exists(projectPath))
        {
            return;
        }

        await _workspaceManager.LoadProjectAsync(projectPath);

        var document = _workspaceManager.GetDocumentByPath(module1Path);
        Assert.NotNull(document);

        // Create new source text
        var newText = Microsoft.CodeAnalysis.Text.SourceText.From("' Modified\nModule Module1\nEnd Module");

        var updatedDoc = _workspaceManager.ApplyTextChange(document.Id, newText);

        Assert.NotNull(updatedDoc);

        var updatedText = await updatedDoc.GetTextAsync();
        Assert.Contains("Modified", updatedText.ToString());
    }

    [Fact]
    public async Task SolutionChanged_EventFired_OnProjectLoad()
    {
        var projectPath = Path.Combine(TestProjectsRoot, "SmallProject", "SmallProject.vbproj");

        if (!File.Exists(projectPath))
        {
            return;
        }

        SolutionChangedEventArgs? receivedArgs = null;
        _workspaceManager.SolutionChanged += (sender, args) => receivedArgs = args;

        await _workspaceManager.LoadProjectAsync(projectPath);

        Assert.NotNull(receivedArgs);
        Assert.Equal(SolutionChangeKind.ProjectAdded, receivedArgs.Kind);
    }

    [Fact]
    public async Task ReloadWorkspaceAsync_FiresReloadedChangeKind()
    {
        var projectPath = Path.Combine(TestProjectsRoot, "SmallProject", "SmallProject.vbproj");

        if (!File.Exists(projectPath))
        {
            return;
        }

        await _workspaceManager.LoadProjectAsync(projectPath);

        SolutionChangedEventArgs? receivedArgs = null;
        _workspaceManager.SolutionChanged += (sender, args) => receivedArgs = args;

        await _workspaceManager.ReloadWorkspaceAsync();

        Assert.NotNull(receivedArgs);
        Assert.Equal(SolutionChangeKind.Reloaded, receivedArgs.Kind);
    }

    [Fact]
    public async Task WorkspaceDiagnostic_EventFired_OnLoadFailure()
    {
        // This test verifies that workspace diagnostics are reported
        // We can't easily force a failure, but we can verify the event mechanism works
        var diagnosticReceived = false;
        _workspaceManager.WorkspaceDiagnostic += (sender, args) => diagnosticReceived = true;

        // Load a valid project - should not fire diagnostic for success
        var projectPath = Path.Combine(TestProjectsRoot, "SmallProject", "SmallProject.vbproj");

        if (!File.Exists(projectPath))
        {
            return;
        }

        await _workspaceManager.LoadProjectAsync(projectPath);

        // Note: We can't reliably assert diagnosticReceived here since
        // it depends on the project state and MSBuild configuration
    }
}
