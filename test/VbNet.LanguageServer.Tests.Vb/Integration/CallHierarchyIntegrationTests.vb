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
    ''' Integration tests for CallHierarchyService with real VB.NET projects.
    ''' </summary>
    <Collection("MSBuild")>
    Public Class CallHierarchyIntegrationTests
        Implements IAsyncLifetime

        Private ReadOnly _workspaceManager As WorkspaceManager
        Private ReadOnly _documentManager As DocumentManager
        Private ReadOnly _callHierarchyService As CallHierarchyService

        Private Shared ReadOnly TestProjectsRoot As String = GetTestProjectsRoot()

        Public Sub New()
            _workspaceManager = New WorkspaceManager(NullLogger(Of WorkspaceManager).Instance)
            _documentManager = New DocumentManager(_workspaceManager, NullLogger(Of DocumentManager).Instance)
            _callHierarchyService = New CallHierarchyService(
                _workspaceManager,
                _documentManager,
                NullLogger(Of CallHierarchyService).Instance)
        End Sub

        Private Shared Function GetTestProjectsRoot() As String
            Dim assemblyLocation = GetType(CallHierarchyIntegrationTests).Assembly.Location
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
        Public Async Function PrepareCallHierarchyAsync_OnMethod_ReturnsItem() As Task
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
            Dim lineIndex = Array.FindIndex(lines, Function(line) line.Contains("SignatureHelpTest"))

            If lineIndex < 0 Then
                Return
            End If

            Dim nameIndex = lines(lineIndex).IndexOf("SignatureHelpTest", StringComparison.Ordinal)

            Dim request = New CallHierarchyPrepareParams With {
                .TextDocument = New TextDocumentIdentifier With {.Uri = helperUri},
                .Position = New Position With {.Line = lineIndex, .Character = nameIndex + 2}
            }

            Dim result = Await _callHierarchyService.PrepareCallHierarchyAsync(request, CancellationToken.None)

            Assert.NotEmpty(result)
            Assert.Equal("SignatureHelpTest", result(0).Name)
        End Function

        <Fact>
        Public Async Function GetOutgoingCallsAsync_OnMethod_ReturnsAddCall() As Task
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
            Dim lineIndex = Array.FindIndex(lines, Function(line) line.Contains("SignatureHelpTest"))

            If lineIndex < 0 Then
                Return
            End If

            Dim nameIndex = lines(lineIndex).IndexOf("SignatureHelpTest", StringComparison.Ordinal)

            Dim prepareRequest = New CallHierarchyPrepareParams With {
                .TextDocument = New TextDocumentIdentifier With {.Uri = helperUri},
                .Position = New Position With {.Line = lineIndex, .Character = nameIndex + 2}
            }

            Dim prepareResult = Await _callHierarchyService.PrepareCallHierarchyAsync(prepareRequest, CancellationToken.None)

            If prepareResult.Length = 0 Then
                Return
            End If

            Dim outgoing = Await _callHierarchyService.GetOutgoingCallsAsync(New CallHierarchyOutgoingCallsParams With {
                .Item = prepareResult(0)
            }, CancellationToken.None)

            Assert.NotEmpty(outgoing)
            Assert.Contains(outgoing, Function(callInfo) callInfo.To.Name = "Add")
        End Function

        <Fact>
        Public Async Function GetIncomingCallsAsync_OnMethod_ReturnsSignatureHelpTest() As Task
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
            Dim lineIndex = Array.FindIndex(lines, Function(line) line.Contains("Function Add"))

            If lineIndex < 0 Then
                Return
            End If

            Dim nameIndex = lines(lineIndex).IndexOf("Add", StringComparison.Ordinal)

            Dim prepareRequest = New CallHierarchyPrepareParams With {
                .TextDocument = New TextDocumentIdentifier With {.Uri = helperUri},
                .Position = New Position With {.Line = lineIndex, .Character = nameIndex + 1}
            }

            Dim prepareResult = Await _callHierarchyService.PrepareCallHierarchyAsync(prepareRequest, CancellationToken.None)

            If prepareResult.Length = 0 Then
                Return
            End If

            Dim incoming = Await _callHierarchyService.GetIncomingCallsAsync(New CallHierarchyIncomingCallsParams With {
                .Item = prepareResult(0)
            }, CancellationToken.None)

            Assert.NotEmpty(incoming)
            Assert.Contains(incoming, Function(callInfo) callInfo.From.Name = "SignatureHelpTest")
        End Function
    End Class

End Namespace
