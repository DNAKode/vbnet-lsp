Imports System
Imports System.IO
Imports System.Linq
Imports System.Threading
Imports System.Threading.Tasks
Imports Microsoft.Extensions.Logging.Abstractions
Imports VbNet.LanguageServer.Protocol
Imports VbNet.LanguageServer.Services
Imports VbNet.LanguageServer.Workspace
Imports Xunit

Namespace VbNet.LanguageServer.Tests.Integration

    ''' <summary>
    ''' Integration tests for SymbolsService with real VB.NET projects.
    ''' </summary>
    <Collection("MSBuild")>
    Public Class SymbolsIntegrationTests
        Implements IAsyncLifetime

        Private ReadOnly _workspaceManager As WorkspaceManager
        Private ReadOnly _documentManager As DocumentManager
        Private ReadOnly _symbolsService As SymbolsService

        Private Shared ReadOnly TestProjectsRoot As String = GetTestProjectsRoot()

        Public Sub New()
            _workspaceManager = New WorkspaceManager(NullLogger(Of WorkspaceManager).Instance)
            _documentManager = New DocumentManager(_workspaceManager, NullLogger(Of DocumentManager).Instance)
            _symbolsService = New SymbolsService(
                _workspaceManager,
                _documentManager,
                NullLogger(Of SymbolsService).Instance)
        End Sub

        Private Shared Function GetTestProjectsRoot() As String
            Dim assemblyLocation = GetType(SymbolsIntegrationTests).Assembly.Location
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
        Public Async Function GetDocumentSymbolsAsync_ReturnsClassSymbol() As Task
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

            Dim request = New DocumentSymbolParams With {
                .TextDocument = New TextDocumentIdentifier With {.Uri = helperUri}
            }

            Dim result = Await _symbolsService.GetDocumentSymbolsAsync(request, CancellationToken.None)

            Assert.NotEmpty(result)
            Assert.Contains(result, Function(symbol) symbol.Name = "Helper" AndAlso symbol.Kind = SymbolKind.Class)
        End Function

        <Fact>
        Public Async Function GetDocumentSymbolsAsync_ReturnsMethodsAsChildren() As Task
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

            Dim request = New DocumentSymbolParams With {
                .TextDocument = New TextDocumentIdentifier With {.Uri = helperUri}
            }

            Dim result = Await _symbolsService.GetDocumentSymbolsAsync(request, CancellationToken.None)

            Dim helperClass = result.FirstOrDefault(Function(symbol) symbol.Name = "Helper")
            Assert.NotNull(helperClass)
            Assert.NotNull(helperClass.Children)
            Assert.Contains(helperClass.Children, Function(child) child.Name = "DoWork" AndAlso child.Kind = SymbolKind.Method)
            Assert.Contains(helperClass.Children, Function(child) child.Name = "Add" AndAlso child.Kind = SymbolKind.Method)
        End Function

        <Fact>
        Public Async Function GetDocumentSymbolsAsync_ReturnsModuleSymbol() As Task
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

            Dim request = New DocumentSymbolParams With {
                .TextDocument = New TextDocumentIdentifier With {.Uri = module1Uri}
            }

            Dim result = Await _symbolsService.GetDocumentSymbolsAsync(request, CancellationToken.None)

            Assert.NotEmpty(result)
            Assert.Contains(result, Function(symbol) symbol.Name = "Module1" AndAlso symbol.Kind = SymbolKind.Module)
        End Function

        <Fact>
        Public Async Function GetWorkspaceSymbolsAsync_FindsClassByName() As Task
            Dim projectPath = Path.Combine(TestProjectsRoot, "SmallProject", "SmallProject.vbproj")

            If Not File.Exists(projectPath) Then
                Return
            End If

            Await _workspaceManager.LoadProjectAsync(projectPath)

            Dim request = New WorkspaceSymbolParams With {
                .Query = "Helper"
            }

            Dim result = Await _symbolsService.GetWorkspaceSymbolsAsync(request, CancellationToken.None)

            Assert.NotEmpty(result)
            Assert.Contains(result, Function(symbol) symbol.Name = "Helper")
        End Function

        <Fact>
        Public Async Function GetWorkspaceSymbolsAsync_FindsMethodByName() As Task
            Dim projectPath = Path.Combine(TestProjectsRoot, "SmallProject", "SmallProject.vbproj")

            If Not File.Exists(projectPath) Then
                Return
            End If

            Await _workspaceManager.LoadProjectAsync(projectPath)

            Dim request = New WorkspaceSymbolParams With {
                .Query = "DoWork"
            }

            Dim result = Await _symbolsService.GetWorkspaceSymbolsAsync(request, CancellationToken.None)

            Assert.NotEmpty(result)
            Assert.Contains(result, Function(symbol) symbol.Name = "DoWork")
        End Function

        <Fact>
        Public Async Function GetWorkspaceSymbolsAsync_ReturnsValidLocations() As Task
            Dim projectPath = Path.Combine(TestProjectsRoot, "SmallProject", "SmallProject.vbproj")

            If Not File.Exists(projectPath) Then
                Return
            End If

            Await _workspaceManager.LoadProjectAsync(projectPath)

            Dim request = New WorkspaceSymbolParams With {
                .Query = "Add"
            }

            Dim result = Await _symbolsService.GetWorkspaceSymbolsAsync(request, CancellationToken.None)

            For Each symbol In result
                Assert.False(String.IsNullOrEmpty(symbol.Name))
                Assert.False(String.IsNullOrEmpty(symbol.Location.Uri))
                Assert.True(symbol.Location.Range.Start.Line >= 0)
                Assert.True(symbol.Location.Range.Start.Character >= 0)
            Next
        End Function
    End Class

End Namespace
