Imports System
Imports System.IO
Imports System.Linq
Imports System.Text.Json
Imports Xunit

Namespace VbNet.Extension.Tests

    Public Class ExtensionManifestTests
        Private Shared Function FindRepoRoot() As String
            Dim directory As DirectoryInfo = New DirectoryInfo(AppContext.BaseDirectory)
            While directory IsNot Nothing AndAlso Not File.Exists(Path.Combine(directory.FullName, "VbNet.LanguageServer.sln"))
                directory = directory.Parent
            End While

            If directory Is Nothing Then
                Throw New InvalidOperationException("Unable to locate repository root from test output directory.")
            End If

            Return directory.FullName
        End Function

        Private Shared Function LoadPackageJson() As JsonElement
            Dim repoRoot = FindRepoRoot()
            Dim packageJsonPath = Path.Combine(repoRoot, "src", "extension", "package.json")
            Assert.True(File.Exists(packageJsonPath), $"Expected extension manifest at {packageJsonPath}.")

            Dim json = File.ReadAllText(packageJsonPath)
            Using document = JsonDocument.Parse(json)
                Return document.RootElement.Clone()
            End Using
        End Function

        <Fact>
        Public Sub ExcludePathsIncludeExternalAndExploratory()
            Dim root = LoadPackageJson()
            Dim defaults = root.GetProperty("contributes").GetProperty("configuration")(0).GetProperty("properties").GetProperty("vbnet.workspace.excludePaths").GetProperty("default")

            Dim values = defaults.EnumerateArray().Select(Function(item) item.GetString()).ToArray()
            Assert.Contains("_external", values)
            Assert.Contains("test-explore", values)
        End Sub

        <Fact>
        Public Sub ProjectFilesExcludePatternCoversExternalAndExploratory()
            Dim root = LoadPackageJson()
            Dim pattern = root.GetProperty("contributes").GetProperty("configuration")(0).GetProperty("properties").GetProperty("vbnet.workspace.projectFilesExcludePattern").GetProperty("default").GetString()

            Assert.NotNull(pattern)
            Assert.Contains("**/_external/**", pattern, StringComparison.Ordinal)
            Assert.Contains("**/test-explore/**", pattern, StringComparison.Ordinal)
        End Sub

        <Fact>
        Public Sub DebuggerLaunchSchemaExposesProjectPath()
            Dim root = LoadPackageJson()
            Dim launchProps = root.GetProperty("contributes").GetProperty("debuggers")(0).GetProperty("configurationAttributes").GetProperty("launch").GetProperty("properties")

            Assert.True(launchProps.TryGetProperty("projectPath", Nothing), "Expected launch configuration to include projectPath.")
        End Sub

        <Fact>
        Public Sub CommandsIncludeWorkspaceSolutionPicker()
            Dim root = LoadPackageJson()
            Dim commands = root.GetProperty("contributes").GetProperty("commands")
            Dim commandList = commands.EnumerateArray().Select(Function(item) item.GetProperty("command").GetString()).ToArray()

            Dim required = New String() {
                "vbnet.selectWorkspaceSolution",
                "vbnet.selectWorkspaceContext",
                "vbnet.showLogs",
                "vbnet.toggleLspTrace",
                "vbnet.restoreWorkspace",
                "vbnet.restoreProject",
                "vbnet.runTestsInContext",
                "vbnet.debugTestsInContext",
                "vbnet.reloadWorkspace",
                "vbnet.attachToProcess"
            }

            For Each commandName In required
                Assert.Contains(commandName, commandList)
            Next
        End Sub

        <Fact>
        Public Sub ActivationEventsIncludeSolutionFilters()
            Dim root = LoadPackageJson()
            Dim eventsArray = root.GetProperty("activationEvents").EnumerateArray().Select(Function(item) item.GetString()).ToArray()

            Assert.Contains("workspaceContains:**/*.slnf", eventsArray)
            Assert.Contains("workspaceContains:**/*.slnx", eventsArray)
        End Sub

        <Fact>
        Public Sub ConfigurationDefaultsIncludeFileNesting()
            Dim root = LoadPackageJson()
            Dim defaults = root.GetProperty("contributes").GetProperty("configurationDefaults")
            Dim patterns = defaults.GetProperty("explorer.fileNesting.patterns")

            Dim vbPattern = patterns.GetProperty("*.vb").GetString()
            Assert.NotNull(vbPattern)
            Assert.Contains(".Designer.vb", vbPattern, StringComparison.OrdinalIgnoreCase)
        End Sub

        <Fact>
        Public Sub ConfigurationIncludesMsbuildAndLoggingSettings()
            Dim root = LoadPackageJson()
            Dim properties = root.GetProperty("contributes").GetProperty("configuration")(0).GetProperty("properties")

            Assert.True(properties.TryGetProperty("vbnet.logLevel", Nothing), "Expected vbnet.logLevel setting.")
            Assert.True(properties.TryGetProperty("vbnet.output.language", Nothing), "Expected vbnet.output.language setting.")
            Assert.True(properties.TryGetProperty("vbnet.msbuildPath", Nothing), "Expected vbnet.msbuildPath setting.")
            Assert.True(properties.TryGetProperty("vbnet.maxMemoryMB", Nothing), "Expected vbnet.maxMemoryMB setting.")
        End Sub

        <Fact>
        Public Sub OutputLanguageSettingSupportsAutoAndEnglish()
            Dim root = LoadPackageJson()
            Dim properties = root.GetProperty("contributes").GetProperty("configuration")(0).GetProperty("properties")
            Dim outputLanguage = properties.GetProperty("vbnet.output.language")

            Assert.Equal("auto", outputLanguage.GetProperty("default").GetString())

            Dim values = outputLanguage.GetProperty("enum").EnumerateArray().Select(Function(item) item.GetString()).ToArray()
            Assert.Contains("auto", values)
            Assert.Contains("en-US", values)
        End Sub

        <Fact>
        Public Sub ConfigurationIncludesExplicitProjectContextSetting()
            Dim root = LoadPackageJson()
            Dim properties = root.GetProperty("contributes").GetProperty("configuration")(0).GetProperty("properties")

            Dim projectPaths As JsonElement
            Assert.True(properties.TryGetProperty("vbnet.workspace.projectPaths", projectPaths), "Expected vbnet.workspace.projectPaths setting.")
            Assert.Equal("array", projectPaths.GetProperty("type").GetString())
        End Sub
    End Class

End Namespace
