Imports System.IO
Imports Microsoft.CodeAnalysis
Imports Microsoft.Extensions.Logging.Abstractions
Imports VbNet.LanguageServer.Protocol
Imports VbNet.LanguageServer.Workspace
Imports Xunit

Namespace VbNet.LanguageServer.Tests.Workspace

    <Collection("MSBuild")>
    Public Class DocumentManagerWorkspaceReloadTests
        Implements IAsyncLifetime

        Private ReadOnly _workspaceManager As WorkspaceManager
        Private ReadOnly _documentManager As DocumentManager

        Private Shared ReadOnly TestProjectsRoot As String = GetTestProjectsRoot()

        Public Sub New()
            _workspaceManager = New WorkspaceManager(NullLogger(Of WorkspaceManager).Instance)
            _documentManager = New DocumentManager(_workspaceManager, NullLogger(Of DocumentManager).Instance)
        End Sub

        Private Shared Function GetTestProjectsRoot() As String
            Dim assemblyLocation = GetType(DocumentManagerWorkspaceReloadTests).Assembly.Location
            Dim assemblyDir = Path.GetDirectoryName(assemblyLocation)
            Return Path.GetFullPath(Path.Combine(assemblyDir, "..", "..", "..", "..", "TestProjects"))
        End Function

        Public Function InitializeAsync() As Task Implements IAsyncLifetime.InitializeAsync
            _workspaceManager.Initialize()
            Return Task.CompletedTask
        End Function

        Public Async Function DisposeAsync() As Task Implements IAsyncLifetime.DisposeAsync
            Await _workspaceManager.DisposeAsync().ConfigureAwait(False)
        End Function

        <Fact>
        Public Async Function ReassociateDocumentsWithWorkspace_RebindsStaleDocumentId() As Task
            Dim solutionPath = Path.Combine(TestProjectsRoot, "SmallProject", "SmallProject.sln")
            Dim modulePath = Path.Combine(TestProjectsRoot, "SmallProject", "Module1.vb")

            If Not File.Exists(solutionPath) OrElse Not File.Exists(modulePath) Then
                Return
            End If

            Dim loaded = Await _workspaceManager.LoadSolutionAsync(solutionPath).ConfigureAwait(False)
            Assert.True(loaded)

            Dim uri = New Uri(modulePath).ToString()
            Dim text = Await File.ReadAllTextAsync(modulePath).ConfigureAwait(False)

            _documentManager.HandleDidOpen(New DidOpenTextDocumentParams With {
                .TextDocument = New TextDocumentItem With {
                    .Uri = uri,
                    .LanguageId = "vb",
                    .Version = 1,
                    .Text = text
                }
            })

            Dim openDoc = _documentManager.GetOpenDocument(uri)
            Assert.NotNull(openDoc)
            Assert.NotNull(openDoc.DocumentId)

            Dim staleDocumentId = DocumentId.CreateNewId(ProjectId.CreateNewId(debugName:="OldProject"), debugName:="OldModule1.vb")
            openDoc.DocumentId = staleDocumentId

            _documentManager.ReassociateDocumentsWithWorkspace()

            Assert.NotNull(openDoc.DocumentId)
            Assert.False(staleDocumentId.Equals(openDoc.DocumentId))

            Dim reboundDocument = _documentManager.GetRoslynDocument(uri)
            Assert.NotNull(reboundDocument)
            Assert.Equal(Path.GetFullPath(modulePath), Path.GetFullPath(reboundDocument.FilePath), StringComparer.OrdinalIgnoreCase)
        End Function

        <Fact>
        Public Async Function ReloadWorkspaceAsync_RebindsOpenDocumentAndPreservesOpenBuffer() As Task
            Dim solutionPath = Path.Combine(TestProjectsRoot, "SmallProject", "SmallProject.sln")
            Dim modulePath = Path.Combine(TestProjectsRoot, "SmallProject", "Module1.vb")

            If Not File.Exists(solutionPath) OrElse Not File.Exists(modulePath) Then
                Return
            End If

            Dim loaded = Await _workspaceManager.LoadSolutionAsync(solutionPath).ConfigureAwait(False)
            Assert.True(loaded)

            Dim uri = New Uri(modulePath).ToString()
            Dim diskText = Await File.ReadAllTextAsync(modulePath).ConfigureAwait(False)

            _documentManager.HandleDidOpen(New DidOpenTextDocumentParams With {
                .TextDocument = New TextDocumentItem With {
                    .Uri = uri,
                    .LanguageId = "vb",
                    .Version = 1,
                    .Text = diskText
                }
            })

            Dim openDoc = _documentManager.GetOpenDocument(uri)
            Assert.NotNull(openDoc)
            Assert.NotNull(openDoc.DocumentId)
            Dim originalDocumentId = openDoc.DocumentId

            Dim unsavedText = "' unsaved across reload" & Environment.NewLine & diskText
            _documentManager.HandleDidChange(New DidChangeTextDocumentParams With {
                .TextDocument = New VersionedTextDocumentIdentifier With {
                    .Uri = uri,
                    .Version = 2
                },
                .ContentChanges = New TextDocumentContentChangeEvent() {
                    New TextDocumentContentChangeEvent With {.Text = unsavedText}
                }
            })

            Dim reloaded = Await _workspaceManager.ReloadWorkspaceAsync().ConfigureAwait(False)

            Assert.True(reloaded)
            Assert.NotNull(openDoc.DocumentId)
            Assert.False(originalDocumentId.Equals(openDoc.DocumentId))

            Dim reloadedDocument = _documentManager.GetRoslynDocument(uri)
            Assert.NotNull(reloadedDocument)
            Assert.Equal(Path.GetFullPath(modulePath), Path.GetFullPath(reloadedDocument.FilePath), StringComparer.OrdinalIgnoreCase)

            Dim reloadedText = Await reloadedDocument.GetTextAsync().ConfigureAwait(False)
            Assert.Contains("' unsaved across reload", reloadedText.ToString())
        End Function

        <Fact>
        Public Async Function HandleDidChangeAndSave_RebindStaleDocumentIdBeforeSynchronizingText() As Task
            Dim solutionPath = Path.Combine(TestProjectsRoot, "SmallProject", "SmallProject.sln")
            Dim modulePath = Path.Combine(TestProjectsRoot, "SmallProject", "Module1.vb")

            If Not File.Exists(solutionPath) OrElse Not File.Exists(modulePath) Then
                Return
            End If

            Dim loaded = Await _workspaceManager.LoadSolutionAsync(solutionPath).ConfigureAwait(False)
            Assert.True(loaded)

            Dim uri = New Uri(modulePath).ToString()
            Dim text = Await File.ReadAllTextAsync(modulePath).ConfigureAwait(False)

            _documentManager.HandleDidOpen(New DidOpenTextDocumentParams With {
                .TextDocument = New TextDocumentItem With {
                    .Uri = uri,
                    .LanguageId = "vb",
                    .Version = 1,
                    .Text = text
                }
            })

            Dim openDoc = _documentManager.GetOpenDocument(uri)
            Assert.NotNull(openDoc)

            Dim staleChangeDocumentId = DocumentId.CreateNewId(ProjectId.CreateNewId(debugName:="OldProject"), debugName:="OldModule1.vb")
            openDoc.DocumentId = staleChangeDocumentId

            Dim changedText = "' changed in memory" & Environment.NewLine & text
            _documentManager.HandleDidChange(New DidChangeTextDocumentParams With {
                .TextDocument = New VersionedTextDocumentIdentifier With {
                    .Uri = uri,
                    .Version = 2
                },
                .ContentChanges = New TextDocumentContentChangeEvent() {
                    New TextDocumentContentChangeEvent With {.Text = changedText}
                }
            })

            Assert.NotNull(openDoc.DocumentId)
            Assert.False(staleChangeDocumentId.Equals(openDoc.DocumentId))

            Dim changedDocument = _workspaceManager.CurrentSolution.GetDocument(openDoc.DocumentId)
            Assert.NotNull(changedDocument)
            Dim changedDocumentText = Await changedDocument.GetTextAsync().ConfigureAwait(False)
            Assert.Contains("' changed in memory", changedDocumentText.ToString())

            Dim staleSaveWithoutTextDocumentId = DocumentId.CreateNewId(ProjectId.CreateNewId(debugName:="OlderProject"), debugName:="OlderModule1.vb")
            openDoc.DocumentId = staleSaveWithoutTextDocumentId

            _documentManager.HandleDidSave(New DidSaveTextDocumentParams With {
                .TextDocument = New TextDocumentIdentifier With {.Uri = uri}
            })

            Assert.NotNull(openDoc.DocumentId)
            Assert.False(staleSaveWithoutTextDocumentId.Equals(openDoc.DocumentId))

            Dim saveWithoutTextDocument = _workspaceManager.CurrentSolution.GetDocument(openDoc.DocumentId)
            Assert.NotNull(saveWithoutTextDocument)
            Dim saveWithoutTextDocumentText = Await saveWithoutTextDocument.GetTextAsync().ConfigureAwait(False)
            Assert.Contains("' changed in memory", saveWithoutTextDocumentText.ToString())

            Dim staleSaveWithTextDocumentId = DocumentId.CreateNewId(ProjectId.CreateNewId(debugName:="EvenOlderProject"), debugName:="EvenOlderModule1.vb")
            openDoc.DocumentId = staleSaveWithTextDocumentId

            Dim savedText = "' saved in memory" & Environment.NewLine & text
            _documentManager.HandleDidSave(New DidSaveTextDocumentParams With {
                .TextDocument = New TextDocumentIdentifier With {.Uri = uri},
                .Text = savedText
            })

            Assert.NotNull(openDoc.DocumentId)
            Assert.False(staleSaveWithTextDocumentId.Equals(openDoc.DocumentId))

            Dim savedDocument = _workspaceManager.CurrentSolution.GetDocument(openDoc.DocumentId)
            Assert.NotNull(savedDocument)
            Dim savedDocumentText = Await savedDocument.GetTextAsync().ConfigureAwait(False)
            Assert.Contains("' saved in memory", savedDocumentText.ToString())
        End Function
    End Class

End Namespace
