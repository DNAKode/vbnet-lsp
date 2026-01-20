Imports System
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
    ''' Integration tests for TypeHierarchyService with real VB.NET projects.
    ''' </summary>
    <Collection("MSBuild")>
    Public Class TypeHierarchyIntegrationTests
        Implements IAsyncLifetime

        Private ReadOnly _workspaceManager As WorkspaceManager
        Private ReadOnly _documentManager As DocumentManager
        Private ReadOnly _typeHierarchyService As TypeHierarchyService

        Private Shared ReadOnly TestProjectsRoot As String = GetTestProjectsRoot()

        Public Sub New()
            _workspaceManager = New WorkspaceManager(NullLogger(Of WorkspaceManager).Instance)
            _documentManager = New DocumentManager(_workspaceManager, NullLogger(Of DocumentManager).Instance)
            _typeHierarchyService = New TypeHierarchyService(
                _workspaceManager,
                _documentManager,
                NullLogger(Of TypeHierarchyService).Instance)
        End Sub

        Private Shared Function GetTestProjectsRoot() As String
            Dim assemblyLocation = GetType(TypeHierarchyIntegrationTests).Assembly.Location
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
        Public Async Function PrepareTypeHierarchyAsync_OnDerivedClass_ReturnsItem() As Task
            Dim helperPath = Path.Combine(TestProjectsRoot, "SmallProject", "Helper.vb")
            Dim projectPath = Path.Combine(TestProjectsRoot, "SmallProject", "SmallProject.vbproj")

            If Not File.Exists(projectPath) OrElse Not File.Exists(helperPath) Then
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
            Dim lineIndex = Array.FindIndex(lines, Function(line) line.Contains("Class DerivedHelper"))

            If lineIndex < 0 Then
                Return
            End If

            Dim nameIndex = lines(lineIndex).IndexOf("DerivedHelper", StringComparison.Ordinal)

            Dim request = New TypeHierarchyPrepareParams With {
                .TextDocument = New TextDocumentIdentifier With {.Uri = helperUri},
                .Position = New Position With {.Line = lineIndex, .Character = nameIndex + 2}
            }

            Dim result = Await _typeHierarchyService.PrepareTypeHierarchyAsync(request, CancellationToken.None)

            Assert.NotEmpty(result)
            Assert.Equal("DerivedHelper", result(0).Name)
        End Function

        <Fact>
        Public Async Function GetSupertypesAsync_OnDerivedClass_ReturnsBase() As Task
            Dim helperPath = Path.Combine(TestProjectsRoot, "SmallProject", "Helper.vb")
            Dim projectPath = Path.Combine(TestProjectsRoot, "SmallProject", "SmallProject.vbproj")

            If Not File.Exists(projectPath) OrElse Not File.Exists(helperPath) Then
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
            Dim lineIndex = Array.FindIndex(lines, Function(line) line.Contains("Class DerivedHelper"))

            If lineIndex < 0 Then
                Return
            End If

            Dim nameIndex = lines(lineIndex).IndexOf("DerivedHelper", StringComparison.Ordinal)

            Dim prepareRequest = New TypeHierarchyPrepareParams With {
                .TextDocument = New TextDocumentIdentifier With {.Uri = helperUri},
                .Position = New Position With {.Line = lineIndex, .Character = nameIndex + 2}
            }

            Dim prepareResult = Await _typeHierarchyService.PrepareTypeHierarchyAsync(prepareRequest, CancellationToken.None)

            If prepareResult.Length = 0 Then
                Return
            End If

            Dim supertypes = Await _typeHierarchyService.GetSupertypesAsync(New TypeHierarchySupertypesParams With {
                .Item = prepareResult(0)
            }, CancellationToken.None)

            Assert.NotEmpty(supertypes)
            Assert.Contains(supertypes, Function(typeInfo) typeInfo.Name = "BaseHelper")
        End Function

        <Fact>
        Public Async Function GetSubtypesAsync_OnBaseClass_ReturnsDerived() As Task
            Dim helperPath = Path.Combine(TestProjectsRoot, "SmallProject", "Helper.vb")
            Dim projectPath = Path.Combine(TestProjectsRoot, "SmallProject", "SmallProject.vbproj")

            If Not File.Exists(projectPath) OrElse Not File.Exists(helperPath) Then
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
            Dim lineIndex = Array.FindIndex(lines, Function(line) line.Contains("Class BaseHelper"))

            If lineIndex < 0 Then
                Return
            End If

            Dim nameIndex = lines(lineIndex).IndexOf("BaseHelper", StringComparison.Ordinal)

            Dim prepareRequest = New TypeHierarchyPrepareParams With {
                .TextDocument = New TextDocumentIdentifier With {.Uri = helperUri},
                .Position = New Position With {.Line = lineIndex, .Character = nameIndex + 2}
            }

            Dim prepareResult = Await _typeHierarchyService.PrepareTypeHierarchyAsync(prepareRequest, CancellationToken.None)

            If prepareResult.Length = 0 Then
                Return
            End If

            Dim subtypes = Await _typeHierarchyService.GetSubtypesAsync(New TypeHierarchySubtypesParams With {
                .Item = prepareResult(0)
            }, CancellationToken.None)

            Assert.NotEmpty(subtypes)
            Assert.Contains(subtypes, Function(typeInfo) typeInfo.Name = "DerivedHelper")
        End Function
    End Class

End Namespace
