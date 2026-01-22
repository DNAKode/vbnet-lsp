Imports System.IO
Imports VbNet.LanguageServer.Workspace
Imports Xunit

Namespace VbNet.LanguageServer.Tests.Workspace

    Public Class NetFxProjectInspectorTests

        <Fact>
        Public Sub GetSdkStyleNetFxTargets_ReturnsNet48()
            Dim content = "<Project Sdk=""Microsoft.NET.Sdk""><PropertyGroup><TargetFramework>net48</TargetFramework></PropertyGroup></Project>"
            Dim path = WriteTempProject(content)

            Try
                Dim targets = NetFxProjectInspector.GetSdkStyleNetFxTargets(path)
                Assert.Contains("net48", targets)
            Finally
                CleanupTempProject(path)
            End Try
        End Sub

        <Fact>
        Public Sub GetSdkStyleNetFxTargets_FiltersNonNetFxTargets()
            Dim content = "<Project Sdk=""Microsoft.NET.Sdk""><PropertyGroup><TargetFrameworks>net472;net6.0</TargetFrameworks></PropertyGroup></Project>"
            Dim path = WriteTempProject(content)

            Try
                Dim targets = NetFxProjectInspector.GetSdkStyleNetFxTargets(path)
                Assert.Single(targets)
                Assert.Equal("net472", targets(0), ignoreCase:=True)
            Finally
                CleanupTempProject(path)
            End Try
        End Sub

        <Fact>
        Public Sub GetSdkStyleNetFxTargets_NonSdkStyleReturnsEmpty()
            Dim content = "<Project><PropertyGroup><TargetFrameworkVersion>v4.8</TargetFrameworkVersion></PropertyGroup></Project>"
            Dim path = WriteTempProject(content)

            Try
                Dim targets = NetFxProjectInspector.GetSdkStyleNetFxTargets(path)
                Assert.Empty(targets)
            Finally
                CleanupTempProject(path)
            End Try
        End Sub

        <Theory>
        <InlineData("net48", "v4.8")>
        <InlineData("net481", "v4.8.1")>
        <InlineData("net462", "v4.6.2")>
        Public Sub GetNetFxReferenceFolderName_MapsTargets(targetFramework As String, expected As String)
            Dim folder = NetFxProjectInspector.GetNetFxReferenceFolderName(targetFramework)
            Assert.Equal(expected, folder, ignoreCase:=True)
        End Sub

        Private Shared Function WriteTempProject(content As String) As String
            Dim tempRoot = Path.Combine(Path.GetTempPath(), "vbnet-lsp-tests", Guid.NewGuid().ToString("N"))
            Directory.CreateDirectory(tempRoot)

            Dim projectPath = Path.Combine(tempRoot, "Test.vbproj")
            File.WriteAllText(projectPath, content)
            Return projectPath
        End Function

        Private Shared Sub CleanupTempProject(projectPath As String)
            Try
                Dim dir = Path.GetDirectoryName(projectPath)
                If Not String.IsNullOrWhiteSpace(dir) AndAlso Directory.Exists(dir) Then
                    Directory.Delete(dir, recursive:=True)
                End If
            Catch
                ' Ignore cleanup failures.
            End Try
        End Sub
    End Class

End Namespace
