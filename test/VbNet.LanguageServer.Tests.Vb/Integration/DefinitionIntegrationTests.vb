Imports System
Imports System.Diagnostics
Imports System.IO
Imports System.Threading
Imports System.Threading.Tasks
Imports Microsoft.Extensions.Logging.Abstractions
Imports VbNet.LanguageServer.Protocol
Imports VbNet.LanguageServer.Services
Imports VbNet.LanguageServer.Workspace
Imports Xunit

Namespace VbNet.LanguageServer.Tests.Integration

    ''' <summary>
    ''' Integration tests for DefinitionService with real VB.NET projects.
    ''' </summary>
    <Collection("MSBuild")>
    Public Class DefinitionIntegrationTests
        Implements IAsyncLifetime

        Private ReadOnly _workspaceManager As WorkspaceManager
        Private ReadOnly _documentManager As DocumentManager
        Private ReadOnly _definitionService As DefinitionService

        Private Shared ReadOnly TestProjectsRoot As String = GetTestProjectsRoot()

        Public Sub New()
            _workspaceManager = New WorkspaceManager(NullLogger(Of WorkspaceManager).Instance)
            _documentManager = New DocumentManager(_workspaceManager, NullLogger(Of DocumentManager).Instance)
            _definitionService = New DefinitionService(
                _workspaceManager,
                _documentManager,
                NullLogger(Of DefinitionService).Instance)
        End Sub

        Private Shared Function GetTestProjectsRoot() As String
            Dim assemblyLocation = GetType(DefinitionIntegrationTests).Assembly.Location
            Dim assemblyDir = Path.GetDirectoryName(assemblyLocation)
            Dim testProjectsPath = Path.GetFullPath(Path.Combine(assemblyDir, "..", "..", "..", "..", "TestProjects"))
            Return testProjectsPath
        End Function

        Public Function InitializeAsync() As Task Implements IAsyncLifetime.InitializeAsync
            _workspaceManager.Initialize()
            Return Task.CompletedTask
        End Function

        Public Async Function DisposeAsync() As Task Implements IAsyncLifetime.DisposeAsync
            Await _workspaceManager.DisposeAsync()
        End Function

        <Fact>
        Public Async Function GetDefinitionAsync_OnMethodDefinition_ReturnsLocation() As Task
            Dim projectPath = Path.Combine(TestProjectsRoot, "SmallProject", "SmallProject.vbproj")
            Dim helperPath = Path.Combine(TestProjectsRoot, "SmallProject", "Helper.vb")

            If Not File.Exists(projectPath) Then
                Return
            End If

            Await _workspaceManager.LoadProjectAsync(projectPath)

            Dim helperUri = New Uri(helperPath).ToString()
            Dim text = Await File.ReadAllTextAsync(helperPath)

            _documentManager.HandleDidOpen(New DidOpenTextDocumentParams With {
                .TextDocument = New TextDocumentItem With {
                    .Uri = helperUri,
                    .LanguageId = "vb",
                    .Version = 1,
                    .Text = text
                }
            })

            Dim lines = text.Split(ControlChars.Lf)
            Dim lineIndex = Array.FindIndex(lines, Function(line) line.Contains("Public Sub DoWork"))

            If lineIndex < 0 Then
                Return
            End If

            Dim doWorkIndex = lines(lineIndex).IndexOf("DoWork", StringComparison.Ordinal)

            Dim request = New DefinitionParams With {
                .TextDocument = New TextDocumentIdentifier With {.Uri = helperUri},
                .Position = New Position With {.Line = lineIndex, .Character = doWorkIndex + 2}
            }

            Dim result = Await _definitionService.GetDefinitionAsync(request, CancellationToken.None)

            Assert.NotEmpty(result)
            Assert.Contains(result, Function(location) location.Uri.Contains("Helper.vb"))
        End Function

        <Fact>
        Public Async Function GetDefinitionAsync_OnClassDefinition_ReturnsLocation() As Task
            Dim projectPath = Path.Combine(TestProjectsRoot, "SmallProject", "SmallProject.vbproj")
            Dim helperPath = Path.Combine(TestProjectsRoot, "SmallProject", "Helper.vb")

            If Not File.Exists(projectPath) Then
                Return
            End If

            Await _workspaceManager.LoadProjectAsync(projectPath)

            Dim helperUri = New Uri(helperPath).ToString()
            Dim text = Await File.ReadAllTextAsync(helperPath)

            _documentManager.HandleDidOpen(New DidOpenTextDocumentParams With {
                .TextDocument = New TextDocumentItem With {
                    .Uri = helperUri,
                    .LanguageId = "vb",
                    .Version = 1,
                    .Text = text
                }
            })

            Dim lines = text.Split(ControlChars.Lf)
            Dim lineIndex = Array.FindIndex(lines, Function(line) line.Contains("Public Class Helper"))

            If lineIndex < 0 Then
                Return
            End If

            Dim helperIndex = lines(lineIndex).IndexOf("Helper", StringComparison.Ordinal)

            Dim request = New DefinitionParams With {
                .TextDocument = New TextDocumentIdentifier With {.Uri = helperUri},
                .Position = New Position With {.Line = lineIndex, .Character = helperIndex + 2}
            }

            Dim result = Await _definitionService.GetDefinitionAsync(request, CancellationToken.None)

            Assert.NotEmpty(result)
            Assert.Contains(result, Function(location) location.Uri.Contains("Helper.vb"))
        End Function

        <Fact>
        Public Async Function GetDefinitionAsync_OnMethodCall_NavigatesToDefinition() As Task
            Dim projectPath = Path.Combine(TestProjectsRoot, "SmallProject", "SmallProject.vbproj")
            Dim module1Path = Path.Combine(TestProjectsRoot, "SmallProject", "Module1.vb")

            If Not File.Exists(projectPath) OrElse Not File.Exists(module1Path) Then
                Return
            End If

            Await _workspaceManager.LoadProjectAsync(projectPath)

            Dim module1Uri = New Uri(module1Path).ToString()
            Dim text = Await File.ReadAllTextAsync(module1Path)

            _documentManager.HandleDidOpen(New DidOpenTextDocumentParams With {
                .TextDocument = New TextDocumentItem With {
                    .Uri = module1Uri,
                    .LanguageId = "vb",
                    .Version = 1,
                    .Text = text
                }
            })

            Dim lines = text.Split(ControlChars.Lf)
            Dim lineIndex = Array.FindIndex(lines, Function(line) line.Contains("Module"))

            If lineIndex < 0 Then
                Return
            End If

            Dim moduleIndex = lines(lineIndex).IndexOf("Module", StringComparison.Ordinal)

            Dim request = New DefinitionParams With {
                .TextDocument = New TextDocumentIdentifier With {.Uri = module1Uri},
                .Position = New Position With {.Line = lineIndex, .Character = moduleIndex + 2}
            }

            Dim result = Await _definitionService.GetDefinitionAsync(request, CancellationToken.None)

            If result.Length > 0 Then
                Assert.Contains(result, Function(location) location.Uri.Contains("Module1.vb"))
            End If
        End Function

        <Fact>
        Public Async Function GetDefinitionAsync_SdkVbConsoleWithCSharpNetStandardReference_NavigatesToCSharpSource() As Task
            Dim fixture = CreateSdkVbConsoleCSharpReferenceFixture()

            Try
                Await RestoreProjectAsync(fixture.SolutionPath).ConfigureAwait(False)

                Dim loaded = Await _workspaceManager.LoadSolutionAsync(fixture.SolutionPath).ConfigureAwait(False)
                Assert.True(loaded)

                Dim programUri = New Uri(fixture.ProgramPath).ToString()
                Dim text = Await File.ReadAllTextAsync(fixture.ProgramPath).ConfigureAwait(False)

                _documentManager.HandleDidOpen(New DidOpenTextDocumentParams With {
                    .TextDocument = New TextDocumentItem With {
                        .Uri = programUri,
                        .LanguageId = "vb",
                        .Version = 1,
                        .Text = text
                    }
                })

                Dim lines = text.Split(ControlChars.Lf)
                Dim lineIndex = Array.FindIndex(lines, Function(line) line.Contains("Utils.GetMessage", StringComparison.Ordinal))
                Assert.True(lineIndex >= 0)

                Dim getMessageIndex = lines(lineIndex).IndexOf("GetMessage", StringComparison.Ordinal)
                Dim request = New DefinitionParams With {
                    .TextDocument = New TextDocumentIdentifier With {.Uri = programUri},
                    .Position = New Position With {.Line = lineIndex, .Character = getMessageIndex + 2}
                }

                Dim result = Await _definitionService.GetDefinitionAsync(request, CancellationToken.None).ConfigureAwait(False)

                Assert.NotEmpty(result)
                Dim sourceLocation = result.FirstOrDefault(Function(location) Uri.UnescapeDataString(New Uri(location.Uri).LocalPath).EndsWith("Utils.cs", StringComparison.OrdinalIgnoreCase))
                Assert.NotNull(sourceLocation)

                Dim utilsLines = (Await File.ReadAllTextAsync(fixture.UtilsPath).ConfigureAwait(False)).Split(ControlChars.Lf)
                Dim expectedLine = Array.FindIndex(utilsLines, Function(line) line.Contains("GetMessage()", StringComparison.Ordinal))
                Dim expectedCharacter = utilsLines(expectedLine).IndexOf("GetMessage", StringComparison.Ordinal)

                Assert.Equal(expectedLine, sourceLocation.Range.Start.Line)
                Assert.Equal(expectedCharacter, sourceLocation.Range.Start.Character)
            Finally
                If Directory.Exists(fixture.RootPath) Then
                    Directory.Delete(fixture.RootPath, recursive:=True)
                End If
            End Try
        End Function

        <Fact>
        Public Async Function GetDefinitionAsync_ReturnsValidRange() As Task
            Dim projectPath = Path.Combine(TestProjectsRoot, "SmallProject", "SmallProject.vbproj")
            Dim helperPath = Path.Combine(TestProjectsRoot, "SmallProject", "Helper.vb")

            If Not File.Exists(projectPath) Then
                Return
            End If

            Await _workspaceManager.LoadProjectAsync(projectPath)

            Dim helperUri = New Uri(helperPath).ToString()
            Dim text = Await File.ReadAllTextAsync(helperPath)

            _documentManager.HandleDidOpen(New DidOpenTextDocumentParams With {
                .TextDocument = New TextDocumentItem With {
                    .Uri = helperUri,
                    .LanguageId = "vb",
                    .Version = 1,
                    .Text = text
                }
            })

            Dim lines = text.Split(ControlChars.Lf)
            Dim lineIndex = Array.FindIndex(lines, Function(line) line.Contains("Public Function Add"))

            If lineIndex < 0 Then
                Return
            End If

            Dim addIndex = lines(lineIndex).IndexOf("Add", StringComparison.Ordinal)

            Dim request = New DefinitionParams With {
                .TextDocument = New TextDocumentIdentifier With {.Uri = helperUri},
                .Position = New Position With {.Line = lineIndex, .Character = addIndex + 1}
            }

            Dim result = Await _definitionService.GetDefinitionAsync(request, CancellationToken.None)

            If result.Length > 0 Then
                Dim location = result(0)
                Assert.True(location.Range.Start.Line >= 0)
                Assert.True(location.Range.Start.Character >= 0)
                Assert.True(location.Range.End.Line >= location.Range.Start.Line)
            End If
        End Function

        Private Shared Function CreateSdkVbConsoleCSharpReferenceFixture() As SdkVbConsoleCSharpReferenceFixture
            Dim root = Path.Combine(Path.GetTempPath(), "vbnet-lsp-tests", Guid.NewGuid().ToString("N"))

            Try
                Dim consoleDir = Path.Combine(root, "Console", "CoreConsole2")
                Dim libDir = Path.Combine(root, "lib", "MyLib")
                Directory.CreateDirectory(consoleDir)
                Directory.CreateDirectory(libDir)

                Dim programPath = Path.Combine(consoleDir, "Program.vb")
                Dim utilsPath = Path.Combine(libDir, "Utils.cs")
                Dim solutionPath = Path.Combine(consoleDir, "CoreConsole2.slnx")

                File.WriteAllText(
                    programPath,
                    "Imports System" & Environment.NewLine &
                    "Imports MyLib" & Environment.NewLine &
                    Environment.NewLine &
                    "Module Program" & Environment.NewLine &
                    "    Sub Main(args As String())" & Environment.NewLine &
                    "        Console.WriteLine(Utils.GetMessage())" & Environment.NewLine &
                    "    End Sub" & Environment.NewLine &
                    "End Module")
                File.WriteAllText(
                    Path.Combine(consoleDir, "CoreConsole2.vbproj"),
                    "<Project Sdk=""Microsoft.NET.Sdk"">" & Environment.NewLine &
                    "  <PropertyGroup>" & Environment.NewLine &
                    "    <OutputType>Exe</OutputType>" & Environment.NewLine &
                    "    <RootNamespace>CoreConsole2</RootNamespace>" & Environment.NewLine &
                    "    <TargetFramework>net10.0</TargetFramework>" & Environment.NewLine &
                    "  </PropertyGroup>" & Environment.NewLine &
                    "  <ItemGroup>" & Environment.NewLine &
                    "    <ProjectReference Include=""..\..\lib\MyLib\MyLib.csproj"" />" & Environment.NewLine &
                    "  </ItemGroup>" & Environment.NewLine &
                    "</Project>")
                File.WriteAllText(
                    utilsPath,
                    "using System;" & Environment.NewLine &
                    Environment.NewLine &
                    "namespace MyLib" & Environment.NewLine &
                    "{" & Environment.NewLine &
                    "    public static class Utils" & Environment.NewLine &
                    "    {" & Environment.NewLine &
                    "        public static string GetMessage()" & Environment.NewLine &
                    "        {" & Environment.NewLine &
                    "            return ""Hello from MyLib (netstandard)"";" & Environment.NewLine &
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
                    "  <Project Path=""..\..\lib\MyLib\MyLib.csproj"" />" & Environment.NewLine &
                    "  <Project Path=""CoreConsole2.vbproj"" />" & Environment.NewLine &
                    "</Solution>")

                Return New SdkVbConsoleCSharpReferenceFixture With {
                    .RootPath = root,
                    .ProgramPath = programPath,
                    .UtilsPath = utilsPath,
                    .SolutionPath = solutionPath
                }
            Catch
                If Directory.Exists(root) Then
                    Directory.Delete(root, recursive:=True)
                End If

                Throw
            End Try
        End Function

        Private NotInheritable Class SdkVbConsoleCSharpReferenceFixture
            Public Property RootPath As String
            Public Property ProgramPath As String
            Public Property UtilsPath As String
            Public Property SolutionPath As String
        End Class

        Private Shared Async Function RestoreProjectAsync(projectOrSolutionPath As String) As Task
            Dim startInfo As New ProcessStartInfo With {
                .FileName = "dotnet",
                .RedirectStandardError = True,
                .RedirectStandardOutput = True,
                .UseShellExecute = False
            }
            startInfo.ArgumentList.Add("restore")
            startInfo.ArgumentList.Add(projectOrSolutionPath)

            Using restoreProcess = Process.Start(startInfo)
                Assert.NotNull(restoreProcess)

                Dim standardOutput = restoreProcess.StandardOutput.ReadToEndAsync()
                Dim standardError = restoreProcess.StandardError.ReadToEndAsync()

                Using timeout As New CancellationTokenSource(TimeSpan.FromMinutes(2))
                    Try
                        Await restoreProcess.WaitForExitAsync(timeout.Token).ConfigureAwait(False)
                    Catch ex As OperationCanceledException
                        Try
                            If Not restoreProcess.HasExited Then
                                restoreProcess.Kill(entireProcessTree:=True)
                            End If
                        Catch
                        End Try

                        Assert.True(False, "dotnet restore timed out for test fixture: " & projectOrSolutionPath)
                    End Try
                End Using

                Dim output = Await standardOutput.ConfigureAwait(False)
                Dim [error] = Await standardError.ConfigureAwait(False)

                Assert.True(
                    restoreProcess.ExitCode = 0,
                    "dotnet restore failed for test fixture:" & Environment.NewLine & output & Environment.NewLine & [error])
            End Using
        End Function
    End Class

End Namespace
