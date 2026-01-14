using System.Text.Json;
using System.Linq;
using Xunit;

namespace VbNet.Extension.Tests;

public class ExtensionManifestTests
{
    private static string FindRepoRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory != null && !File.Exists(Path.Combine(directory.FullName, "VbNet.LanguageServer.sln")))
        {
            directory = directory.Parent;
        }

        if (directory == null)
        {
            throw new InvalidOperationException("Unable to locate repository root from test output directory.");
        }

        return directory.FullName;
    }

    private static JsonElement LoadPackageJson()
    {
        var repoRoot = FindRepoRoot();
        var packageJsonPath = Path.Combine(repoRoot, "src", "extension", "package.json");
        Assert.True(File.Exists(packageJsonPath), $"Expected extension manifest at {packageJsonPath}.");

        var json = File.ReadAllText(packageJsonPath);
        using var document = JsonDocument.Parse(json);
        return document.RootElement.Clone();
    }

    [Fact]
    public void ExcludePathsIncludeExternalAndExploratory()
    {
        var root = LoadPackageJson();
        var defaults = root
            .GetProperty("contributes")
            .GetProperty("configuration")[0]
            .GetProperty("properties")
            .GetProperty("vbnet.workspace.excludePaths")
            .GetProperty("default");

        var values = defaults.EnumerateArray().Select(item => item.GetString()).ToArray();
        Assert.Contains("_external", values);
        Assert.Contains("tests-exploratory", values);
    }

    [Fact]
    public void ProjectFilesExcludePatternCoversExternalAndExploratory()
    {
        var root = LoadPackageJson();
        var pattern = root
            .GetProperty("contributes")
            .GetProperty("configuration")[0]
            .GetProperty("properties")
            .GetProperty("vbnet.workspace.projectFilesExcludePattern")
            .GetProperty("default")
            .GetString();

        Assert.NotNull(pattern);
        Assert.Contains("**/_external/**", pattern, StringComparison.Ordinal);
        Assert.Contains("**/tests-exploratory/**", pattern, StringComparison.Ordinal);
    }

    [Fact]
    public void DebuggerLaunchSchemaExposesProjectPath()
    {
        var root = LoadPackageJson();
        var launchProps = root
            .GetProperty("contributes")
            .GetProperty("debuggers")[0]
            .GetProperty("configurationAttributes")
            .GetProperty("launch")
            .GetProperty("properties");

        Assert.True(launchProps.TryGetProperty("projectPath", out _), "Expected launch configuration to include projectPath.");
    }
}
