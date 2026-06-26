Imports System.IO
Imports Microsoft.CodeAnalysis
Imports Microsoft.Extensions.Logging.Abstractions
Imports VbNet.LanguageServer.Workspace
Imports Xunit

Namespace VbNet.LanguageServer.Tests.Workspace

    ''' <summary>
    ''' Tests for WorkspaceManager with real VB.NET projects.
    ''' </summary>
    <Collection("MSBuild")>
    Public Class WorkspaceManagerTests
        Implements IAsyncLifetime

        Private ReadOnly _workspaceManager As WorkspaceManager

        Private Shared ReadOnly TestProjectsRoot As String = GetTestProjectsRoot()

        Public Sub New()
            _workspaceManager = New WorkspaceManager(NullLogger(Of WorkspaceManager).Instance)
        End Sub

        Private Shared Function GetTestProjectsRoot() As String
            Dim assemblyLocation = GetType(WorkspaceManagerTests).Assembly.Location
            Dim assemblyDir = Path.GetDirectoryName(assemblyLocation)
            Dim testProjectsPath = Path.GetFullPath(Path.Combine(assemblyDir, "..", "..", "..", "..", "TestProjects"))
            Return testProjectsPath
        End Function

        Public Function InitializeAsync() As Task Implements IAsyncLifetime.InitializeAsync
            _workspaceManager.Initialize()
            Return Task.CompletedTask
        End Function

        Public Async Function DisposeAsync() As Task Implements IAsyncLifetime.DisposeAsync
            Await _workspaceManager.DisposeAsync().ConfigureAwait(False)
        End Function

        <Fact>
        Public Sub Initialize_CreatesWorkspace()
            Assert.NotNull(_workspaceManager.CurrentSolution)
        End Sub

        <Fact>
        Public Sub CurrentSolution_BeforeLoad_IsEmpty()
            Assert.False(_workspaceManager.IsLoaded)
        End Sub

        <Fact>
        Public Async Function LoadProjectAsync_SmallProject_LoadsSuccessfully() As Task
            Dim projectPath = Path.Combine(TestProjectsRoot, "SmallProject", "SmallProject.vbproj")

            If Not File.Exists(projectPath) Then
                Return
            End If

            Dim result = Await _workspaceManager.LoadProjectAsync(projectPath).ConfigureAwait(False)

            Assert.True(result)
            Assert.True(_workspaceManager.IsLoaded)

            Dim projects = _workspaceManager.GetVbNetProjects().ToList()
            Assert.Single(projects)
            Assert.Equal("SmallProject", projects(0).Name)
        End Function

        <Fact>
        Public Async Function LoadProjectAsync_SmallProject_HasDocuments() As Task
            Dim projectPath = Path.Combine(TestProjectsRoot, "SmallProject", "SmallProject.vbproj")

            If Not File.Exists(projectPath) Then
                Return
            End If

            Await _workspaceManager.LoadProjectAsync(projectPath).ConfigureAwait(False)

            Dim projects = _workspaceManager.GetVbNetProjects().ToList()
            Assert.Single(projects)

            Dim documents = projects(0).Documents.ToList()
            Assert.True(documents.Count >= 2, "Expected at least 2 documents (Module1.vb, Helper.vb)")

            Dim documentNames = documents.Select(Function(d) Path.GetFileName(d.FilePath)).ToList()
            Assert.Contains("Module1.vb", documentNames)
            Assert.Contains("Helper.vb", documentNames)
        End Function

        <Fact>
        Public Async Function GetDocumentByPath_ReturnsCorrectDocument() As Task
            Dim projectPath = Path.Combine(TestProjectsRoot, "SmallProject", "SmallProject.vbproj")
            Dim module1Path = Path.Combine(TestProjectsRoot, "SmallProject", "Module1.vb")

            If Not File.Exists(projectPath) Then
                Return
            End If

            Await _workspaceManager.LoadProjectAsync(projectPath).ConfigureAwait(False)

            Dim document = _workspaceManager.GetDocumentByPath(module1Path)

            Assert.NotNull(document)
            Assert.Equal("Module1.vb", Path.GetFileName(document.FilePath))
        End Function

        <Fact>
        Public Async Function GetDocumentByUri_ReturnsCorrectDocument() As Task
            Dim projectPath = Path.Combine(TestProjectsRoot, "SmallProject", "SmallProject.vbproj")
            Dim module1Path = Path.Combine(TestProjectsRoot, "SmallProject", "Module1.vb")
            Dim module1Uri = New Uri(module1Path).ToString()

            If Not File.Exists(projectPath) Then
                Return
            End If

            Await _workspaceManager.LoadProjectAsync(projectPath).ConfigureAwait(False)

            Dim document = _workspaceManager.GetDocumentByUri(module1Uri)

            Assert.NotNull(document)
            Assert.Equal("Module1.vb", Path.GetFileName(document.FilePath))
        End Function

        <Fact>
        Public Async Function GetDocumentByPath_NonExistentPath_ReturnsNull() As Task
            Dim projectPath = Path.Combine(TestProjectsRoot, "SmallProject", "SmallProject.vbproj")

            If Not File.Exists(projectPath) Then
                Return
            End If

            Await _workspaceManager.LoadProjectAsync(projectPath).ConfigureAwait(False)

            Dim document = _workspaceManager.GetDocumentByPath("C:\NonExistent\File.vb")

            Assert.Null(document)
        End Function

        <Fact>
        Public Async Function LoadProjectAsync_NonExistentPath_ReturnsFalse() As Task
            Dim result = Await _workspaceManager.LoadProjectAsync("C:\NonExistent\Project.vbproj").ConfigureAwait(False)

            Assert.False(result)
            Assert.False(_workspaceManager.IsLoaded)
        End Function

        <Fact>
        Public Async Function LoadSolutionAsync_MixedSlnx_LoadsSdkStyleAndProjectedLegacyProjects() As Task
            Dim root = Path.Combine(Path.GetTempPath(), "vbnet-lsp-tests", Guid.NewGuid().ToString("N"))

            Try
                Dim legacyDir = Path.Combine(root, "Legacy")
                Dim sdkDir = Path.Combine(root, "Sdk")
                Directory.CreateDirectory(legacyDir)
                Directory.CreateDirectory(sdkDir)

                File.WriteAllText(
                    Path.Combine(legacyDir, "Program.vb"),
                    "Module Program" & Environment.NewLine &
                    "    Sub Main()" & Environment.NewLine &
                    "        Dim pathValue As String = My.Application.Info.DirectoryPath" & Environment.NewLine &
                    "    End Sub" & Environment.NewLine &
                    "End Module")
                File.WriteAllText(
                    Path.Combine(legacyDir, "LegacyProjected.vbproj"),
                    "<?xml version=""1.0"" encoding=""utf-8""?>" & Environment.NewLine &
                    "<Project ToolsVersion=""15.0"" xmlns=""http://schemas.microsoft.com/developer/msbuild/2003"">" & Environment.NewLine &
                    "  <PropertyGroup>" & Environment.NewLine &
                    "    <OutputType>Exe</OutputType>" & Environment.NewLine &
                    "    <AssemblyName>LegacyProjected</AssemblyName>" & Environment.NewLine &
                    "    <TargetFrameworkVersion>v4.8</TargetFrameworkVersion>" & Environment.NewLine &
                    "    <MyType>Console</MyType>" & Environment.NewLine &
                    "    <OptionStrict>On</OptionStrict>" & Environment.NewLine &
                    "    <OptionInfer>On</OptionInfer>" & Environment.NewLine &
                    "    <OptionExplicit>On</OptionExplicit>" & Environment.NewLine &
                    "  </PropertyGroup>" & Environment.NewLine &
                    "  <ItemGroup>" & Environment.NewLine &
                    "    <Reference Include=""System"" />" & Environment.NewLine &
                    "  </ItemGroup>" & Environment.NewLine &
                    "  <ItemGroup>" & Environment.NewLine &
                    "    <Import Include=""Microsoft.VisualBasic"" />" & Environment.NewLine &
                    "    <Import Include=""System"" />" & Environment.NewLine &
                    "  </ItemGroup>" & Environment.NewLine &
                    "  <ItemGroup>" & Environment.NewLine &
                    "    <Compile Include=""Program.vb"" />" & Environment.NewLine &
                    "  </ItemGroup>" & Environment.NewLine &
                    "</Project>")

                File.WriteAllText(
                    Path.Combine(sdkDir, "Program.vb"),
                    "Module Program" & Environment.NewLine &
                    "    Sub Main()" & Environment.NewLine &
                    "        System.Console.WriteLine(""sdk"")" & Environment.NewLine &
                    "    End Sub" & Environment.NewLine &
                    "End Module")
                File.WriteAllText(
                    Path.Combine(sdkDir, "SdkProject.vbproj"),
                    "<Project Sdk=""Microsoft.NET.Sdk"">" & Environment.NewLine &
                    "  <PropertyGroup>" & Environment.NewLine &
                    "    <OutputType>Exe</OutputType>" & Environment.NewLine &
                    "    <TargetFramework>net10.0</TargetFramework>" & Environment.NewLine &
                    "    <RootNamespace></RootNamespace>" & Environment.NewLine &
                    "    <AssemblyName>SdkProject</AssemblyName>" & Environment.NewLine &
                    "  </PropertyGroup>" & Environment.NewLine &
                    "</Project>")

                Dim slnxPath = Path.Combine(root, "Mixed.slnx")
                File.WriteAllText(
                    slnxPath,
                    "<Solution>" & Environment.NewLine &
                    "  <Project Path=""Legacy/LegacyProjected.vbproj"" Id=""11111111-1111-1111-1111-111111111111"" />" & Environment.NewLine &
                    "  <Project Path=""Sdk/SdkProject.vbproj"" Id=""22222222-2222-2222-2222-222222222222"" />" & Environment.NewLine &
                    "</Solution>")

                Dim result = Await _workspaceManager.LoadSolutionAsync(slnxPath).ConfigureAwait(False)

                Assert.True(result)
                Dim projectNames = _workspaceManager.GetVbNetProjects().Select(Function(project) project.Name).ToList()
                Assert.Contains("LegacyProjected", projectNames)
                Assert.Contains("SdkProject", projectNames)
            Finally
                If Directory.Exists(root) Then
                    Directory.Delete(root, recursive:=True)
                End If
            End Try
        End Function

        <Fact>
        Public Async Function LoadSolutionAsync_LegacyWebSlnFailure_UsesProjectFallback() As Task
            Dim referenceAssemblyDir = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86),
                "Reference Assemblies",
                "Microsoft",
                "Framework",
                ".NETFramework",
                "v4.7.2")
            If Not Directory.Exists(referenceAssemblyDir) Then
                Return
            End If

            Dim root = Path.Combine(Path.GetTempPath(), "vbnet-lsp-tests", Guid.NewGuid().ToString("N"))

            Try
                Directory.CreateDirectory(root)

                File.WriteAllText(
                    Path.Combine(root, "Default.aspx.vb"),
                    "Public Partial Class _Default" & Environment.NewLine &
                    "    Inherits Page" & Environment.NewLine &
                    "    Protected Sub Page_Load(sender As Object, e As EventArgs)" & Environment.NewLine &
                    "        Dim pageTitle As String = Me.Title" & Environment.NewLine &
                    "    End Sub" & Environment.NewLine &
                    "End Class")
                File.WriteAllText(
                    Path.Combine(root, "Default.aspx.designer.vb"),
                    "Option Strict Off" & Environment.NewLine &
                    "Option Explicit On" & Environment.NewLine &
                    Environment.NewLine &
                    "Partial Public Class _Default" & Environment.NewLine &
                    "    Protected WithEvents SampleLabel As Global.System.Web.UI.WebControls.Label" & Environment.NewLine &
                    "End Class")
                File.WriteAllText(
                    Path.Combine(root, "WebApplication.vbproj"),
                    "<?xml version=""1.0"" encoding=""utf-8""?>" & Environment.NewLine &
                    "<Project ToolsVersion=""12.0"" DefaultTargets=""Build"" xmlns=""http://schemas.microsoft.com/developer/msbuild/2003"">" & Environment.NewLine &
                    "  <PropertyGroup>" & Environment.NewLine &
                    "    <ProjectGuid>{CD8E53D2-B177-494B-AE08-1CEEF98E43D7}</ProjectGuid>" & Environment.NewLine &
                    "    <ProjectTypeGuids>{349c5851-65df-11da-9384-00065b846f21};{F184B08F-C81C-45F6-A57F-5ABD9991F28F}</ProjectTypeGuids>" & Environment.NewLine &
                    "    <OutputType>Library</OutputType>" & Environment.NewLine &
                    "    <RootNamespace>WebApplication</RootNamespace>" & Environment.NewLine &
                    "    <AssemblyName>WebApplication</AssemblyName>" & Environment.NewLine &
                    "    <TargetFrameworkVersion>v4.7.2</TargetFrameworkVersion>" & Environment.NewLine &
                    "    <MyType>Custom</MyType>" & Environment.NewLine &
                    "    <OptionExplicit>On</OptionExplicit>" & Environment.NewLine &
                    "    <OptionStrict>Off</OptionStrict>" & Environment.NewLine &
                    "    <OptionInfer>On</OptionInfer>" & Environment.NewLine &
                    "  </PropertyGroup>" & Environment.NewLine &
                    "  <ItemGroup>" & Environment.NewLine &
                    "    <Reference Include=""System"" />" & Environment.NewLine &
                    "    <Reference Include=""System.Core"" />" & Environment.NewLine &
                    "    <Reference Include=""System.Web"" />" & Environment.NewLine &
                    "  </ItemGroup>" & Environment.NewLine &
                    "  <ItemGroup>" & Environment.NewLine &
                    "    <Import Include=""Microsoft.VisualBasic"" />" & Environment.NewLine &
                    "    <Import Include=""System"" />" & Environment.NewLine &
                    "    <Import Include=""System.Web"" />" & Environment.NewLine &
                    "    <Import Include=""System.Web.UI"" />" & Environment.NewLine &
                    "    <Import Include=""System.Web.UI.WebControls"" />" & Environment.NewLine &
                    "  </ItemGroup>" & Environment.NewLine &
                    "  <ItemGroup>" & Environment.NewLine &
                    "    <Compile Include=""Default.aspx.designer.vb"">" & Environment.NewLine &
                    "      <DependentUpon>Default.aspx</DependentUpon>" & Environment.NewLine &
                    "    </Compile>" & Environment.NewLine &
                    "    <Compile Include=""Default.aspx.vb"">" & Environment.NewLine &
                    "      <DependentUpon>Default.aspx</DependentUpon>" & Environment.NewLine &
                    "      <SubType>ASPXCodebehind</SubType>" & Environment.NewLine &
                    "    </Compile>" & Environment.NewLine &
                    "  </ItemGroup>" & Environment.NewLine &
                    "</Project>")

                Dim slnPath = Path.Combine(root, "WebApplication.sln")
                File.WriteAllText(
                    slnPath,
                    "This intentionally is not a valid Visual Studio solution header." & Environment.NewLine &
                    "Project(""{F184B08F-C81C-45F6-A57F-5ABD9991F28F}"") = ""WebApplication"", ""WebApplication.vbproj"", ""{CD8E53D2-B177-494B-AE08-1CEEF98E43D7}""" & Environment.NewLine &
                    "EndProject")

                Dim result = Await _workspaceManager.LoadSolutionAsync(slnPath).ConfigureAwait(False)

                Assert.True(result)
                Assert.True(_workspaceManager.IsLoaded)
                Assert.Equal(Path.GetFullPath(slnPath), Path.GetFullPath(_workspaceManager.LoadedSolutionPath))

                Dim project = Assert.Single(_workspaceManager.GetVbNetProjects())
                Assert.Equal("WebApplication", project.Name)

                Dim documentNames = project.Documents.Select(Function(document) Path.GetFileName(document.FilePath)).ToList()
                Assert.Contains("Default.aspx.vb", documentNames)
                Assert.Contains("Default.aspx.designer.vb", documentNames)
            Finally
                If Directory.Exists(root) Then
                    Directory.Delete(root, recursive:=True)
                End If
            End Try
        End Function

        <Fact>
        Public Async Function LoadSolutionAsync_LegacyWebSlnxWithCSharpNetStandardReference_ResolvesReferencedSymbols() As Task
            If Not HasReferenceAssemblyDirectory("v4.7.2") Then
                Return
            End If

            Dim fixture = CreateLegacyWebCSharpReferenceFixture()
            Dim workspaceDiagnostics As New List(Of String)()
            CaptureWorkspaceDiagnostics(workspaceDiagnostics)

            Try
                Dim result = Await _workspaceManager.LoadSolutionAsync(fixture.SolutionPath).ConfigureAwait(False)

                Assert.True(result)
                Await AssertLegacyWebCSharpReferenceResolvedAsync(fixture.PagePath, workspaceDiagnostics).ConfigureAwait(False)
            Finally
                If Directory.Exists(fixture.RootPath) Then
                    Directory.Delete(fixture.RootPath, recursive:=True)
                End If
            End Try
        End Function

        <Fact>
        Public Async Function LoadProjectAsync_LegacyWebProjectWithCSharpNetStandardReference_ResolvesReferencedSymbols() As Task
            If Not HasReferenceAssemblyDirectory("v4.7.2") Then
                Return
            End If

            Dim fixture = CreateLegacyWebCSharpReferenceFixture()
            Dim workspaceDiagnostics As New List(Of String)()
            CaptureWorkspaceDiagnostics(workspaceDiagnostics)

            Try
                Dim result = Await _workspaceManager.LoadProjectAsync(fixture.WebProjectPath).ConfigureAwait(False)

                Assert.True(result)
                Await AssertLegacyWebCSharpReferenceResolvedAsync(fixture.PagePath, workspaceDiagnostics).ConfigureAwait(False)
            Finally
                If Directory.Exists(fixture.RootPath) Then
                    Directory.Delete(fixture.RootPath, recursive:=True)
                End If
            End Try
        End Function

        <Fact>
        Public Async Function GetVbNetProjects_ReturnsOnlyVbProjects() As Task
            Dim projectPath = Path.Combine(TestProjectsRoot, "SmallProject", "SmallProject.vbproj")

            If Not File.Exists(projectPath) Then
                Return
            End If

            Await _workspaceManager.LoadProjectAsync(projectPath).ConfigureAwait(False)

            Dim vbProjects = _workspaceManager.GetVbNetProjects().ToList()
            Assert.All(vbProjects, Sub(p) Assert.Equal("Visual Basic", p.Language))
        End Function

        <Fact>
        Public Async Function ApplyTextChange_UpdatesDocument() As Task
            Dim projectPath = Path.Combine(TestProjectsRoot, "SmallProject", "SmallProject.vbproj")
            Dim module1Path = Path.Combine(TestProjectsRoot, "SmallProject", "Module1.vb")

            If Not File.Exists(projectPath) Then
                Return
            End If

            Dim originalDiskText = Await File.ReadAllTextAsync(module1Path).ConfigureAwait(False)

            Await _workspaceManager.LoadProjectAsync(projectPath).ConfigureAwait(False)

            Dim document = _workspaceManager.GetDocumentByPath(module1Path)
            Assert.NotNull(document)

            Dim newText = Microsoft.CodeAnalysis.Text.SourceText.From("' Modified" & vbLf & "Module Module1" & vbLf & "End Module")

            Dim updatedDoc = _workspaceManager.ApplyTextChange(document.Id, newText)

            Assert.NotNull(updatedDoc)

            Dim updatedText = Await updatedDoc.GetTextAsync().ConfigureAwait(False)
            Assert.Contains("Modified", updatedText.ToString())

            Dim diskText = Await File.ReadAllTextAsync(module1Path).ConfigureAwait(False)
            Assert.Equal(originalDiskText, diskText)
        End Function

        <Fact>
        Public Async Function SolutionChanged_EventFired_OnProjectLoad() As Task
            Dim projectPath = Path.Combine(TestProjectsRoot, "SmallProject", "SmallProject.vbproj")

            If Not File.Exists(projectPath) Then
                Return
            End If

            Dim receivedArgs As SolutionChangedEventArgs = Nothing
            AddHandler _workspaceManager.SolutionChanged, Sub(sender, args) receivedArgs = args

            Await _workspaceManager.LoadProjectAsync(projectPath).ConfigureAwait(False)

            Assert.NotNull(receivedArgs)
            Assert.Equal(SolutionChangeKind.ProjectAdded, receivedArgs.Kind)
        End Function

        <Fact>
        Public Async Function ReloadWorkspaceAsync_FiresReloadedChangeKind() As Task
            Dim projectPath = Path.Combine(TestProjectsRoot, "SmallProject", "SmallProject.vbproj")

            If Not File.Exists(projectPath) Then
                Return
            End If

            Await _workspaceManager.LoadProjectAsync(projectPath).ConfigureAwait(False)

            Dim receivedArgs As SolutionChangedEventArgs = Nothing
            AddHandler _workspaceManager.SolutionChanged, Sub(sender, args) receivedArgs = args

            Await _workspaceManager.ReloadWorkspaceAsync().ConfigureAwait(False)

            Assert.NotNull(receivedArgs)
            Assert.Equal(SolutionChangeKind.Reloaded, receivedArgs.Kind)
        End Function

        <Fact>
        Public Async Function ReloadWorkspaceAsync_ProjectMode_ReopensProjectInFreshWorkspace() As Task
            Dim projectPath = Path.Combine(TestProjectsRoot, "SmallProject", "SmallProject.vbproj")
            Dim module1Path = Path.Combine(TestProjectsRoot, "SmallProject", "Module1.vb")

            If Not File.Exists(projectPath) Then
                Return
            End If

            Await _workspaceManager.LoadProjectAsync(projectPath).ConfigureAwait(False)

            Dim originalDocument = _workspaceManager.GetDocumentByPath(module1Path)
            Assert.NotNull(originalDocument)
            Dim originalDocumentId = originalDocument.Id

            Dim result = Await _workspaceManager.ReloadWorkspaceAsync().ConfigureAwait(False)

            Assert.True(result)

            Dim reloadedDocument = _workspaceManager.GetDocumentByPath(module1Path)
            Assert.NotNull(reloadedDocument)
            Assert.False(originalDocumentId.Equals(reloadedDocument.Id))
        End Function

        <Fact>
        Public Async Function WorkspaceDiagnostic_EventFired_OnLoadFailure() As Task
            Dim diagnosticReceived = False
            AddHandler _workspaceManager.WorkspaceDiagnostic, Sub(sender, args) diagnosticReceived = True

            Dim projectPath = Path.Combine(TestProjectsRoot, "SmallProject", "SmallProject.vbproj")

            If Not File.Exists(projectPath) Then
                Return
            End If

            Await _workspaceManager.LoadProjectAsync(projectPath).ConfigureAwait(False)

            Assert.False(diagnosticReceived)
        End Function

        Private Sub CaptureWorkspaceDiagnostics(workspaceDiagnostics As IList(Of String))
            AddHandler _workspaceManager.WorkspaceDiagnostic,
                Sub(sender, args)
                    If args?.Diagnostic IsNot Nothing Then
                        workspaceDiagnostics.Add(args.Diagnostic.Message)
                    End If
                End Sub
        End Sub

        Private Async Function AssertLegacyWebCSharpReferenceResolvedAsync(pagePath As String, workspaceDiagnostics As IEnumerable(Of String)) As Task
            Assert.True(
                _workspaceManager.CurrentSolution.Projects.Any(Function(project) project.Language = LanguageNames.CSharp AndAlso project.Name = "MyLib"),
                "Expected MyLib C# project to load. Diagnostics: " & String.Join(" | ", workspaceDiagnostics))

            Dim document = _workspaceManager.GetDocumentByPath(pagePath)
            Assert.NotNull(document)
            Assert.True(
                document.Project.ProjectReferences.Any(Function(reference) _workspaceManager.CurrentSolution.GetProject(reference.ProjectId)?.Name = "MyLib"),
                "Expected WebApplication to reference MyLib. Project references: " &
                String.Join(", ", document.Project.ProjectReferences.Select(Function(reference) _workspaceManager.CurrentSolution.GetProject(reference.ProjectId)?.Name)))
            Dim referencedProject = document.Project.ProjectReferences.
                Select(Function(reference) _workspaceManager.CurrentSolution.GetProject(reference.ProjectId)).
                First(Function(project) project?.Name = "MyLib")
            Dim referencedCompilation = Await referencedProject.GetCompilationAsync().ConfigureAwait(False)
            Assert.NotNull(referencedCompilation.GetTypeByMetadataName("MyLib.Utils"))
            Dim vbCompilation = Await document.Project.GetCompilationAsync().ConfigureAwait(False)
            Assert.True(
                vbCompilation.GetTypeByMetadataName("MyLib.Utils") IsNot Nothing,
                "Expected VB compilation to see MyLib.Utils. References: " &
                String.Join(", ", vbCompilation.References.Select(Function(reference) reference.Display)))

            Dim semanticModel = Await document.GetSemanticModelAsync().ConfigureAwait(False)
            Assert.NotNull(semanticModel)

            Dim diagnostics = semanticModel.GetDiagnostics().ToList()
            Assert.DoesNotContain(diagnostics, Function(diagnostic) diagnostic.Id = "BC40056")
            Assert.DoesNotContain(diagnostics, Function(diagnostic) diagnostic.Id = "BC30451")
        End Function

        Private Shared Function CreateLegacyWebCSharpReferenceFixture() As LegacyWebCSharpReferenceFixture
            Dim root = Path.Combine(Path.GetTempPath(), "vbnet-lsp-tests", Guid.NewGuid().ToString("N"))

            Try
                Dim webDir = Path.Combine(root, "Web", "WebApplication")
                Dim libDir = Path.Combine(root, "lib", "MyLib")
                Directory.CreateDirectory(webDir)
                Directory.CreateDirectory(libDir)

                Dim pagePath = Path.Combine(webDir, "Default.aspx.vb")
                Dim webProjectPath = Path.Combine(webDir, "WebApplication.vbproj")
                Dim solutionPath = Path.Combine(webDir, "WebApplication.slnx")

                File.WriteAllText(
                    pagePath,
                    "Imports MyLib" & Environment.NewLine &
                    Environment.NewLine &
                    "Public Partial Class _Default" & Environment.NewLine &
                    "    Inherits Page" & Environment.NewLine &
                    Environment.NewLine &
                    "    Protected Sub Page_Load(sender As Object, e As EventArgs)" & Environment.NewLine &
                    "        Dim message As String = Utils.GetMessage()" & Environment.NewLine &
                    "    End Sub" & Environment.NewLine &
                    "End Class")
                File.WriteAllText(
                    Path.Combine(webDir, "Default.aspx.designer.vb"),
                    "Option Strict Off" & Environment.NewLine &
                    "Option Explicit On" & Environment.NewLine &
                    Environment.NewLine &
                    "Partial Public Class _Default" & Environment.NewLine &
                    "End Class")
                File.WriteAllText(
                    webProjectPath,
                    "<?xml version=""1.0"" encoding=""utf-8""?>" & Environment.NewLine &
                    "<Project ToolsVersion=""12.0"" DefaultTargets=""Build"" xmlns=""http://schemas.microsoft.com/developer/msbuild/2003"">" & Environment.NewLine &
                    "  <PropertyGroup>" & Environment.NewLine &
                    "    <ProjectGuid>{CD8E53D2-B177-494B-AE08-1CEEF98E43D7}</ProjectGuid>" & Environment.NewLine &
                    "    <ProjectTypeGuids>{349c5851-65df-11da-9384-00065b846f21};{F184B08F-C81C-45F6-A57F-5ABD9991F28F}</ProjectTypeGuids>" & Environment.NewLine &
                    "    <OutputType>Library</OutputType>" & Environment.NewLine &
                    "    <RootNamespace>WebApplication</RootNamespace>" & Environment.NewLine &
                    "    <AssemblyName>WebApplication</AssemblyName>" & Environment.NewLine &
                    "    <TargetFrameworkVersion>v4.7.2</TargetFrameworkVersion>" & Environment.NewLine &
                    "    <MyType>Custom</MyType>" & Environment.NewLine &
                    "    <OptionExplicit>On</OptionExplicit>" & Environment.NewLine &
                    "    <OptionStrict>Off</OptionStrict>" & Environment.NewLine &
                    "    <OptionInfer>On</OptionInfer>" & Environment.NewLine &
                    "  </PropertyGroup>" & Environment.NewLine &
                    "  <ItemGroup>" & Environment.NewLine &
                    "    <Reference Include=""System"" />" & Environment.NewLine &
                    "    <Reference Include=""System.Core"" />" & Environment.NewLine &
                    "    <Reference Include=""System.Web"" />" & Environment.NewLine &
                    "  </ItemGroup>" & Environment.NewLine &
                    "  <ItemGroup>" & Environment.NewLine &
                    "    <Import Include=""Microsoft.VisualBasic"" />" & Environment.NewLine &
                    "    <Import Include=""System"" />" & Environment.NewLine &
                    "    <Import Include=""System.Web"" />" & Environment.NewLine &
                    "    <Import Include=""System.Web.UI"" />" & Environment.NewLine &
                    "    <Import Include=""System.Web.UI.WebControls"" />" & Environment.NewLine &
                    "  </ItemGroup>" & Environment.NewLine &
                    "  <ItemGroup>" & Environment.NewLine &
                    "    <Compile Include=""Default.aspx.designer.vb"">" & Environment.NewLine &
                    "      <DependentUpon>Default.aspx</DependentUpon>" & Environment.NewLine &
                    "    </Compile>" & Environment.NewLine &
                    "    <Compile Include=""Default.aspx.vb"">" & Environment.NewLine &
                    "      <DependentUpon>Default.aspx</DependentUpon>" & Environment.NewLine &
                    "      <SubType>ASPXCodebehind</SubType>" & Environment.NewLine &
                    "    </Compile>" & Environment.NewLine &
                    "  </ItemGroup>" & Environment.NewLine &
                    "  <ItemGroup>" & Environment.NewLine &
                    "    <ProjectReference Include=""..\..\lib\MyLib\MyLib.csproj"">" & Environment.NewLine &
                    "      <Name>MyLib</Name>" & Environment.NewLine &
                    "    </ProjectReference>" & Environment.NewLine &
                    "  </ItemGroup>" & Environment.NewLine &
                    "</Project>")

                File.WriteAllText(
                    Path.Combine(libDir, "Utils.cs"),
                    "namespace MyLib" & Environment.NewLine &
                    "{" & Environment.NewLine &
                    "    public static class Utils" & Environment.NewLine &
                    "    {" & Environment.NewLine &
                    "        public static string GetMessage()" & Environment.NewLine &
                    "        {" & Environment.NewLine &
                    "            return ""Hello from MyLib"";" & Environment.NewLine &
                    "        }" & Environment.NewLine &
                    "    }" & Environment.NewLine &
                    "}")
                File.WriteAllText(
                    Path.Combine(libDir, "MyLib.csproj"),
                    "<Project Sdk=""Microsoft.NET.Sdk"">" & Environment.NewLine &
                    "  <PropertyGroup>" & Environment.NewLine &
                    "    <TargetFramework>netstandard2.0</TargetFramework>" & Environment.NewLine &
                    "    <GenerateAssemblyInfo>false</GenerateAssemblyInfo>" & Environment.NewLine &
                    "    <RootNamespace>MyLib</RootNamespace>" & Environment.NewLine &
                    "    <AssemblyName>MyLib</AssemblyName>" & Environment.NewLine &
                    "  </PropertyGroup>" & Environment.NewLine &
                    "</Project>")
                File.WriteAllText(
                    solutionPath,
                    "<Solution>" & Environment.NewLine &
                    "  <Project Path=""WebApplication.vbproj"" Id=""cd8e53d2-b177-494b-ae08-1ceef98e43d7"" />" & Environment.NewLine &
                    "  <Project Path=""..\..\lib\MyLib\MyLib.csproj"" Id=""094121eb-82c8-4c12-95db-e591ee45633c"" />" & Environment.NewLine &
                    "</Solution>")

                Return New LegacyWebCSharpReferenceFixture With {
                    .RootPath = root,
                    .PagePath = pagePath,
                    .WebProjectPath = webProjectPath,
                    .SolutionPath = solutionPath
                }
            Catch
                If Directory.Exists(root) Then
                    Directory.Delete(root, recursive:=True)
                End If

                Throw
            End Try
        End Function

        Private Shared Function HasReferenceAssemblyDirectory(targetFrameworkVersion As String) As Boolean
            Dim referenceAssemblyDir = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86),
                "Reference Assemblies",
                "Microsoft",
                "Framework",
                ".NETFramework",
                targetFrameworkVersion)

            Return Directory.Exists(referenceAssemblyDir)
        End Function

        Private NotInheritable Class LegacyWebCSharpReferenceFixture
            Public Property RootPath As String
            Public Property PagePath As String
            Public Property WebProjectPath As String
            Public Property SolutionPath As String
        End Class
    End Class

End Namespace
