Imports System.IO
Imports Microsoft.CodeAnalysis
Imports Microsoft.CodeAnalysis.VisualBasic
Imports VbNet.LanguageServer.Workspace
Imports Xunit

Namespace VbNet.LanguageServer.Tests.Workspace

    Public Class LegacyVbProjectReaderTests

        <Fact>
        Public Sub TryRead_OldStyleNet48Project_PreservesImportsReferencesAndOptions()
            Dim root = CreateLegacyProject()

            Try
                Dim projectPath = Path.Combine(root, "Sample.vbproj")
                Dim info = LegacyVbProjectReader.TryRead(projectPath)

                Assert.NotNull(info)
                Assert.Equal("SampleAssembly", info.AssemblyName)
                Assert.Equal(OutputKind.WindowsApplication, info.OutputKind)
                Assert.Equal(OptionStrict.On, info.OptionStrict)
                Assert.True(info.OptionInfer)
                Assert.True(info.OptionExplicit)
                Assert.False(info.OptionCompareText)
                Assert.Contains(info.GlobalImports, Function(import) String.Equals(import.Name, "System", StringComparison.OrdinalIgnoreCase))
                Assert.Contains(info.GlobalImports, Function(import) String.Equals(import.Name, "Microsoft.VisualBasic", StringComparison.OrdinalIgnoreCase))
                Assert.Contains(info.References, Function(reference) reference.Display.EndsWith("System.Windows.Forms.dll", StringComparison.OrdinalIgnoreCase))
                Assert.Contains(info.References, Function(reference) reference.Display.EndsWith("System.Core.dll", StringComparison.OrdinalIgnoreCase))
                Assert.Contains(info.Documents, Function(documentPath) documentPath.EndsWith("Program.vb", StringComparison.OrdinalIgnoreCase))
            Finally
                Directory.Delete(root, recursive:=True)
            End Try
        End Sub

        <Fact>
        Public Sub TryRead_ResolvesProjectReferences()
            Dim root = CreateLegacyProject(projectReference:="..\Library\Library.vbproj")

            Try
                Dim libraryDir = Path.Combine(Path.GetDirectoryName(root), "Library")
                Directory.CreateDirectory(libraryDir)
                File.WriteAllText(Path.Combine(libraryDir, "Library.vbproj"), "<Project ToolsVersion=""15.0"" xmlns=""http://schemas.microsoft.com/developer/msbuild/2003"" />")

                Dim info = LegacyVbProjectReader.TryRead(Path.Combine(root, "Sample.vbproj"))

                Assert.NotNull(info)
                Assert.Single(info.ProjectReferences)
                Assert.EndsWith(Path.Combine("Library", "Library.vbproj"), info.ProjectReferences(0), StringComparison.OrdinalIgnoreCase)
            Finally
                Directory.Delete(Path.GetDirectoryName(root), recursive:=True)
            End Try
        End Sub

        <Fact>
        Public Sub TryRead_ResolvesPackagesConfigAssemblies()
            Dim root = CreateLegacyProject()

            Try
                File.WriteAllText(
                    Path.Combine(root, "packages.config"),
                    "<packages><package id=""Example.Package"" version=""1.2.3"" targetFramework=""net48"" /></packages>")

                Dim packageLib = Path.Combine(Path.GetDirectoryName(root), "packages", "Example.Package.1.2.3", "lib", "net48")
                Directory.CreateDirectory(packageLib)
                File.Copy(GetNet48ReferenceAssembly("System.Xml.dll"), Path.Combine(packageLib, "Example.Package.dll"))

                Dim info = LegacyVbProjectReader.TryRead(Path.Combine(root, "Sample.vbproj"))

                Assert.NotNull(info)
                Assert.Contains(info.References, Function(reference) reference.Display.EndsWith("Example.Package.dll", StringComparison.OrdinalIgnoreCase))
            Finally
                Directory.Delete(Path.GetDirectoryName(root), recursive:=True)
            End Try
        End Sub

        <Fact>
        Public Sub TryRead_ComReferenceWithoutInteropAssembly_AddsWarning()
            Dim root = CreateLegacyProject(comReference:="Shell32")

            Try
                Dim info = LegacyVbProjectReader.TryRead(Path.Combine(root, "Sample.vbproj"))

                Assert.NotNull(info)
                Assert.Contains(info.Warnings, Function(warning) warning.Contains("COM references were found", StringComparison.OrdinalIgnoreCase))
            Finally
                Directory.Delete(root, recursive:=True)
            End Try
        End Sub

        <Fact>
        Public Sub TryRead_ConditionalImportsAndUnresolvedReferences_AddsWarnings()
            Dim root = CreateLegacyProject(
                extraProjectXml:="  <Import Project=""Custom.targets"" Condition=""Exists('Custom.targets')"" />" & Environment.NewLine,
                extraReferenceXml:="    <Reference Include=""Missing.Assembly"" />" & Environment.NewLine)

            Try
                Dim info = LegacyVbProjectReader.TryRead(Path.Combine(root, "Sample.vbproj"))

                Assert.NotNull(info)
                Assert.Contains(info.Warnings, Function(warning) warning.Contains("Condition attributes", StringComparison.OrdinalIgnoreCase))
                Assert.Contains(info.Warnings, Function(warning) warning.Contains("Imported MSBuild props/targets", StringComparison.OrdinalIgnoreCase))
                Assert.Contains(info.Warnings, Function(warning) warning.Contains("Missing.Assembly", StringComparison.OrdinalIgnoreCase))
            Finally
                Directory.Delete(root, recursive:=True)
            End Try
        End Sub


        <Fact>
        Public Sub GetProjectPathsFromSlnx_ReturnsVbProjectPaths()
            Dim root = CreateLegacyProject()

            Try
                Dim slnxPath = Path.Combine(root, "Sample.slnx")
                File.WriteAllText(slnxPath, "<Solution><Project Path=""Sample.vbproj"" Id=""11111111-1111-1111-1111-111111111111"" /></Solution>")

                Dim paths = LegacyVbProjectReader.GetProjectPathsFromSlnx(slnxPath)

                Assert.Single(paths)
                Assert.Equal(Path.Combine(root, "Sample.vbproj"), paths(0), ignoreCase:=True)
            Finally
                Directory.Delete(root, recursive:=True)
            End Try
        End Sub

        Private Shared Function CreateLegacyProject(Optional projectReference As String = Nothing, Optional comReference As String = Nothing, Optional extraProjectXml As String = Nothing, Optional extraReferenceXml As String = Nothing) As String
            Dim root = Path.Combine(Path.GetTempPath(), "vbnet-lsp-tests", Guid.NewGuid().ToString("N"), "App")
            Directory.CreateDirectory(root)
            File.WriteAllText(Path.Combine(root, "Program.vb"), "Module Program" & Environment.NewLine & "End Module")

            Dim extraItems = String.Empty
            If Not String.IsNullOrWhiteSpace(projectReference) Then
                extraItems &= "  <ItemGroup>" & Environment.NewLine &
                    $"    <ProjectReference Include=""{projectReference}"" />" & Environment.NewLine &
                    "  </ItemGroup>" & Environment.NewLine
            End If

            If Not String.IsNullOrWhiteSpace(comReference) Then
                extraItems &= "  <ItemGroup>" & Environment.NewLine &
                    $"    <COMReference Include=""{comReference}"" />" & Environment.NewLine &
                    "  </ItemGroup>" & Environment.NewLine
            End If

            File.WriteAllText(
                Path.Combine(root, "Sample.vbproj"),
                "<?xml version=""1.0"" encoding=""utf-8""?>" & Environment.NewLine &
                "<Project ToolsVersion=""15.0"" xmlns=""http://schemas.microsoft.com/developer/msbuild/2003"">" & Environment.NewLine &
                "  <PropertyGroup>" & Environment.NewLine &
                "    <OutputType>WinExe</OutputType>" & Environment.NewLine &
                "    <RootNamespace></RootNamespace>" & Environment.NewLine &
                "    <AssemblyName>SampleAssembly</AssemblyName>" & Environment.NewLine &
                "    <TargetFrameworkVersion>v4.8</TargetFrameworkVersion>" & Environment.NewLine &
                "    <OptionExplicit>On</OptionExplicit>" & Environment.NewLine &
                "    <OptionCompare>Binary</OptionCompare>" & Environment.NewLine &
                "    <OptionStrict>On</OptionStrict>" & Environment.NewLine &
                "    <OptionInfer>On</OptionInfer>" & Environment.NewLine &
                "  </PropertyGroup>" & Environment.NewLine &
                "  <ItemGroup>" & Environment.NewLine &
                "    <Reference Include=""System"" />" & Environment.NewLine &
                "    <Reference Include=""System.Windows.Forms"" />" & Environment.NewLine &
                If(extraReferenceXml, String.Empty) &
                "  </ItemGroup>" & Environment.NewLine &
                "  <ItemGroup>" & Environment.NewLine &
                "    <Import Include=""Microsoft.VisualBasic"" />" & Environment.NewLine &
                "    <Import Include=""System"" />" & Environment.NewLine &
                "  </ItemGroup>" & Environment.NewLine &
                "  <ItemGroup>" & Environment.NewLine &
                "    <Compile Include=""Program.vb"" />" & Environment.NewLine &
                "  </ItemGroup>" & Environment.NewLine &
                extraItems &
                If(extraProjectXml, String.Empty) &
                "</Project>")

            Return root
        End Function

        Private Shared Function GetNet48ReferenceAssembly(fileName As String) As String
            Return Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86),
                "Reference Assemblies",
                "Microsoft",
                "Framework",
                ".NETFramework",
                "v4.8",
                fileName)
        End Function
    End Class

End Namespace
