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
                Assert.Equal("net48", info.TargetFramework)
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
        Public Sub TryRead_ConsoleMyType_GeneratesApplicationInfoProjection()
            Dim source = String.Join(
                Environment.NewLine,
                "Imports System.IO",
                "",
                "Module Program",
                "    Sub Main()",
                "        Dim pathValue As String = My.Application.Info.DirectoryPath",
                "        Dim versionValue As Version = My.Application.Info.Version",
                "    End Sub",
                "End Module")
            Dim root = CreateLegacyProject(myType:="Console", programSource:=source)

            Try
                Dim info = LegacyVbProjectReader.TryRead(Path.Combine(root, "Sample.vbproj"))

                Assert.NotNull(info)
                Assert.Single(info.GeneratedSources)
                Assert.Equal("SdkEquivalentMyApplication.g.vb", info.GeneratedSources(0).FileName)

                Dim diagnostics = CreateCompilation(info).GetDiagnostics()

                Assert.DoesNotContain(diagnostics, Function(diagnostic) IsErrorContaining(diagnostic, "DirectoryPath"))
                Assert.DoesNotContain(diagnostics, Function(diagnostic) IsErrorContaining(diagnostic, "My.Application"))
            Finally
                Directory.Delete(root, recursive:=True)
            End Try
        End Sub

        <Fact>
        Public Sub TryRead_Bt4gLikeConsoleProject_ResolvesApplicationInfoDirectoryPath()
            Dim source = String.Join(
                Environment.NewLine,
                "Imports System.IO",
                "",
                "Public Module AppGlobals",
                "    Friend ReadOnly WindowTitle As String =",
                "        $""BT4G Torrent Magnet Scraper v{My.Application.Info.Version.ToString(fieldCount:=3)}""",
                "",
                "    Friend ReadOnly HistoryFilePath As String =",
                "        Path.Combine(My.Application.Info.DirectoryPath, ""cache\BT4G History.txt"")",
                "",
                "    Friend ReadOnly OutputDirectoryPath As String =",
                "        Path.Combine(My.Application.Info.DirectoryPath, ""output"")",
                "End Module")
            Dim root = CreateLegacyProject(myType:="Console", programSource:=source)

            Try
                Dim info = LegacyVbProjectReader.TryRead(Path.Combine(root, "Sample.vbproj"))

                Assert.NotNull(info)
                Dim diagnostics = CreateCompilation(info).GetDiagnostics()

                Assert.DoesNotContain(diagnostics, Function(diagnostic) IsErrorContaining(diagnostic, "DirectoryPath"))
                Assert.DoesNotContain(diagnostics, Function(diagnostic) IsErrorContaining(diagnostic, "fieldCount"))
            Finally
                Directory.Delete(root, recursive:=True)
            End Try
        End Sub

        <Fact>
        Public Sub TryRead_WindowsFormsMyType_GeneratesApplicationBaseProjection()
            Dim root = CreateLegacyProject(
                myType:="WindowsForms",
                programSource:="Public Class Form1" & Environment.NewLine &
                    "    Inherits System.Windows.Forms.Form" & Environment.NewLine &
                    "End Class",
                extraCompileXml:="    <Compile Include=""My Project\Application.Designer.vb"" />" & Environment.NewLine)

            Try
                Dim myProjectDir = Path.Combine(root, "My Project")
                Directory.CreateDirectory(myProjectDir)
                File.WriteAllText(
                    Path.Combine(myProjectDir, "Application.Designer.vb"),
                    String.Join(
                        Environment.NewLine,
                        "Option Strict On",
                        "Option Explicit On",
                        "",
                        "Namespace My",
                        "    Partial Friend Class MyApplication",
                        "        Public Sub New()",
                        "            MyBase.New(Global.Microsoft.VisualBasic.ApplicationServices.AuthenticationMode.Windows)",
                        "            Me.IsSingleInstance = False",
                        "            Me.EnableVisualStyles = True",
                        "            Me.SaveMySettingsOnExit = False",
                        "            Me.ShutDownStyle = Global.Microsoft.VisualBasic.ApplicationServices.ShutdownMode.AfterMainFormCloses",
                        "        End Sub",
                        "",
                        "        Protected Overrides Sub OnCreateMainForm()",
                        "            Me.MainForm = Global.Form1",
                        "        End Sub",
                        "    End Class",
                        "End Namespace"))

                Dim info = LegacyVbProjectReader.TryRead(Path.Combine(root, "Sample.vbproj"))

                Assert.NotNull(info)
                Assert.Contains("WindowsFormsApplicationBase", info.GeneratedSources(0).Source)
                Assert.Equal("My.MyApplication", info.MainTypeName)
                Assert.DoesNotContain(info.Documents, Function(documentPath) documentPath.EndsWith(Path.Combine("My Project", "Application.Designer.vb"), StringComparison.OrdinalIgnoreCase))

                Dim diagnostics = CreateCompilation(info).GetDiagnostics()

                Assert.DoesNotContain(diagnostics, Function(diagnostic) IsErrorContaining(diagnostic, "Sub Main"))
                Assert.DoesNotContain(diagnostics, Function(diagnostic) IsErrorContaining(diagnostic, "Form1"))
                Assert.DoesNotContain(diagnostics, Function(diagnostic) IsErrorContaining(diagnostic, "Too many arguments"))
                Assert.DoesNotContain(diagnostics, Function(diagnostic) IsErrorContaining(diagnostic, "IsSingleInstance"))
                Assert.DoesNotContain(diagnostics, Function(diagnostic) IsErrorContaining(diagnostic, "EnableVisualStyles"))
                Assert.DoesNotContain(diagnostics, Function(diagnostic) IsErrorContaining(diagnostic, "SaveMySettingsOnExit"))
                Assert.DoesNotContain(diagnostics, Function(diagnostic) IsErrorContaining(diagnostic, "ShutDownStyle"))
            Finally
                Directory.Delete(root, recursive:=True)
            End Try
        End Sub

        <Fact>
        Public Sub TryRead_CsVbSummaryNet48Project_ResolvesRoslynPackageAliases()
            Const roslynVersion = "4.12.0"
            Dim source = String.Join(
                Environment.NewLine,
                "Imports Microsoft.CodeAnalysis",
                "Imports Cs = Microsoft.CodeAnalysis.CSharp",
                "Imports CsSyntax = Microsoft.CodeAnalysis.CSharp.Syntax",
                "Imports Vb = Microsoft.CodeAnalysis.VisualBasic",
                "Imports VbSyntax = Microsoft.CodeAnalysis.VisualBasic.Syntax",
                "",
                "Module Program",
                "    <DebuggerStepThrough>",
                "    Sub Main()",
                "        Dim consoleType As Type = GetType(Console)",
                "        Dim tree As SyntaxTree = Nothing",
                "        Dim csKind As Cs.SyntaxKind = Cs.SyntaxKind.None",
                "        Dim vbKind As Vb.SyntaxKind = Vb.SyntaxKind.None",
                "        Dim csIdentifier As CsSyntax.IdentifierNameSyntax = Nothing",
                "        Dim vbIdentifier As VbSyntax.IdentifierNameSyntax = Nothing",
                "    End Sub",
                "End Module")
            Dim root = CreateLegacyProject(
                myType:="Console",
                programSource:=source,
                extraProjectXml:="  <ItemGroup>" & Environment.NewLine &
                    "    <Import Include=""System.Diagnostics"" />" & Environment.NewLine &
                    "  </ItemGroup>" & Environment.NewLine)

            Try
                File.WriteAllText(
                    Path.Combine(root, "packages.config"),
                    "<packages>" &
                    $"<package id=""Microsoft.CodeAnalysis.Common"" version=""{roslynVersion}"" targetFramework=""net48"" />" &
                    $"<package id=""Microsoft.CodeAnalysis.CSharp"" version=""{roslynVersion}"" targetFramework=""net48"" />" &
                    $"<package id=""Microsoft.CodeAnalysis.VisualBasic"" version=""{roslynVersion}"" targetFramework=""net48"" />" &
                    "</packages>")
                CopyGlobalPackageAssembly(root, "Microsoft.CodeAnalysis.Common", roslynVersion, "Microsoft.CodeAnalysis.dll")
                CopyGlobalPackageAssembly(root, "Microsoft.CodeAnalysis.CSharp", roslynVersion, "Microsoft.CodeAnalysis.CSharp.dll")
                CopyGlobalPackageAssembly(root, "Microsoft.CodeAnalysis.VisualBasic", roslynVersion, "Microsoft.CodeAnalysis.VisualBasic.dll")

                Dim info = LegacyVbProjectReader.TryRead(Path.Combine(root, "Sample.vbproj"))

                Assert.NotNull(info)
                Assert.Contains(info.PackageReferences, Function(reference) String.Equals(reference.Id, "Microsoft.CodeAnalysis.Common", StringComparison.OrdinalIgnoreCase) AndAlso String.Equals(reference.Source, "packages.config", StringComparison.OrdinalIgnoreCase))
                Assert.Contains(info.References, Function(reference) reference.Display.EndsWith("Microsoft.CodeAnalysis.dll", StringComparison.OrdinalIgnoreCase))
                Assert.Contains(info.References, Function(reference) reference.Display.EndsWith("Microsoft.CodeAnalysis.CSharp.dll", StringComparison.OrdinalIgnoreCase))
                Assert.Contains(info.References, Function(reference) reference.Display.EndsWith("Microsoft.CodeAnalysis.VisualBasic.dll", StringComparison.OrdinalIgnoreCase))

                Dim diagnostics = CreateCompilation(info).GetDiagnostics()

                Assert.DoesNotContain(diagnostics, Function(diagnostic) IsErrorContaining(diagnostic, "Microsoft.CodeAnalysis"))
                Assert.DoesNotContain(diagnostics, Function(diagnostic) IsErrorContaining(diagnostic, "Console"))
                Assert.DoesNotContain(diagnostics, Function(diagnostic) IsErrorContaining(diagnostic, "DebuggerStepThrough"))
                Assert.DoesNotContain(diagnostics, Function(diagnostic) IsErrorContaining(diagnostic, "CsSyntax"))
                Assert.DoesNotContain(diagnostics, Function(diagnostic) IsErrorContaining(diagnostic, "VbSyntax"))
            Finally
                Directory.Delete(Path.GetDirectoryName(root), recursive:=True)
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
        Public Sub TryRead_ResolvesPackagesConfigAssembliesFromGlobalNuGetCache()
            Const roslynVersion = "4.12.0"
            Dim root = CreateLegacyProject()

            Try
                File.WriteAllText(
                    Path.Combine(root, "packages.config"),
                    "<packages><package id=""Microsoft.CodeAnalysis.Common"" version=""" & roslynVersion & """ targetFramework=""net48"" /></packages>")

                Dim info = LegacyVbProjectReader.TryRead(Path.Combine(root, "Sample.vbproj"))

                Assert.NotNull(info)
                Assert.Contains(info.References, Function(reference) reference.Display.EndsWith("Microsoft.CodeAnalysis.dll", StringComparison.OrdinalIgnoreCase))
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

        Private Shared Function CreateLegacyProject(Optional projectReference As String = Nothing, Optional comReference As String = Nothing, Optional extraProjectXml As String = Nothing, Optional extraReferenceXml As String = Nothing, Optional myType As String = Nothing, Optional programSource As String = Nothing, Optional extraCompileXml As String = Nothing) As String
            Dim root = Path.Combine(Path.GetTempPath(), "vbnet-lsp-tests", Guid.NewGuid().ToString("N"), "App")
            Directory.CreateDirectory(root)
            File.WriteAllText(Path.Combine(root, "Program.vb"), If(programSource, "Module Program" & Environment.NewLine & "End Module"))

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
                If(String.IsNullOrWhiteSpace(myType), String.Empty, $"    <MyType>{myType}</MyType>" & Environment.NewLine) &
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
                If(extraCompileXml, String.Empty) &
                "  </ItemGroup>" & Environment.NewLine &
                extraItems &
                If(extraProjectXml, String.Empty) &
                "</Project>")

            Return root
        End Function

        Private Shared Function CreateCompilation(info As LegacyVbProjectProjection) As VisualBasicCompilation
            Dim syntaxTrees = info.Documents.
                Select(Function(documentPath) VisualBasicSyntaxTree.ParseText(File.ReadAllText(documentPath), path:=documentPath)).
                Concat(info.GeneratedSources.Select(Function(source) VisualBasicSyntaxTree.ParseText(source.Source, path:=source.FileName)))

            Dim options = New VisualBasicCompilationOptions(info.OutputKind).
                WithRootNamespace(If(info.RootNamespace, String.Empty)).
                WithOptionStrict(info.OptionStrict).
                WithOptionInfer(info.OptionInfer).
                WithOptionExplicit(info.OptionExplicit).
                WithOptionCompareText(info.OptionCompareText).
                WithGlobalImports(info.GlobalImports)
            If Not String.IsNullOrWhiteSpace(info.MainTypeName) Then
                options = options.WithMainTypeName(info.MainTypeName)
            End If

            Return VisualBasicCompilation.Create(info.AssemblyName, syntaxTrees, info.References, options)
        End Function

        Private Shared Function IsErrorContaining(diagnostic As Diagnostic, text As String) As Boolean
            Return diagnostic.Severity = DiagnosticSeverity.Error AndAlso
                diagnostic.GetMessage().Contains(text, StringComparison.OrdinalIgnoreCase)
        End Function

        Private Shared Sub CopyGlobalPackageAssembly(projectRoot As String, packageId As String, version As String, assemblyName As String)
            Dim globalPackageAssembly = Path.Combine(
                GetNuGetPackageRoot(),
                packageId.ToLowerInvariant(),
                version.ToLowerInvariant(),
                "lib",
                "netstandard2.0",
                assemblyName)
            Assert.True(File.Exists(globalPackageAssembly), $"Expected restored package assembly at {globalPackageAssembly}.")

            Dim localPackageLib = Path.Combine(
                Path.GetDirectoryName(projectRoot),
                "packages",
                packageId & "." & version,
                "lib",
                "netstandard2.0")
            Directory.CreateDirectory(localPackageLib)
            File.Copy(globalPackageAssembly, Path.Combine(localPackageLib, assemblyName), overwrite:=True)
        End Sub

        Private Shared Function GetNuGetPackageRoot() As String
            Dim configuredRoot = Environment.GetEnvironmentVariable("NUGET_PACKAGES")
            If Not String.IsNullOrWhiteSpace(configuredRoot) Then
                Return configuredRoot
            End If

            Return Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),
                ".nuget",
                "packages")
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
