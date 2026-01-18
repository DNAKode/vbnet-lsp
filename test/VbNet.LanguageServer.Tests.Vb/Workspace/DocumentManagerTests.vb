Imports Microsoft.Extensions.Logging.Abstractions
Imports VbNet.LanguageServer.Protocol
Imports VbNet.LanguageServer.Workspace
Imports Xunit

Namespace VbNet.LanguageServer.Tests.Workspace

    Public Class DocumentManagerTests
        Private ReadOnly _workspaceManager As WorkspaceManager
        Private ReadOnly _documentManager As DocumentManager

        Public Sub New()
            _workspaceManager = New WorkspaceManager(NullLogger(Of WorkspaceManager).Instance)
            _documentManager = New DocumentManager(_workspaceManager, NullLogger(Of DocumentManager).Instance)
        End Sub

        <Fact>
        Public Sub HandleDidOpen_TracksDocument()
            Dim parameters = New DidOpenTextDocumentParams With {
                .TextDocument = New TextDocumentItem With {
                    .Uri = "file:///c:/test/module1.vb",
                    .LanguageId = "vb",
                    .Version = 1,
                    .Text = "Module Module1" & vbLf & "End Module"
                }
            }

            _documentManager.HandleDidOpen(parameters)

            Assert.True(_documentManager.IsDocumentOpen(parameters.TextDocument.Uri))
            Dim doc = _documentManager.GetOpenDocument(parameters.TextDocument.Uri)
            Assert.NotNull(doc)
            Assert.Equal(1, doc.Version)
            Assert.Equal("vb", doc.LanguageId)
        End Sub

        <Fact>
        Public Sub HandleDidChange_UpdatesDocumentText()
            Dim uri = "file:///c:/test/module1.vb"

            _documentManager.HandleDidOpen(New DidOpenTextDocumentParams With {
                .TextDocument = New TextDocumentItem With {
                    .Uri = uri,
                    .LanguageId = "vb",
                    .Version = 1,
                    .Text = "Module Module1" & vbLf & "End Module"
                }
            })

            _documentManager.HandleDidChange(New DidChangeTextDocumentParams With {
                .TextDocument = New VersionedTextDocumentIdentifier With {
                    .Uri = uri,
                    .Version = 2
                },
                .ContentChanges = New TextDocumentContentChangeEvent() {
                    New TextDocumentContentChangeEvent With {
                        .Range = New Global.VbNet.LanguageServer.Protocol.Range With {
                            .Start = New Position(0, 7),
                            .[End] = New Position(0, 14)
                        },
                        .Text = "TestModule"
                    }
                }
            })

            Dim doc = _documentManager.GetOpenDocument(uri)
            Assert.NotNull(doc)
            Assert.Equal(2, doc.Version)
            Assert.Contains("TestModule", doc.Text.ToString())
        End Sub

        <Fact>
        Public Sub HandleDidChange_FullDocumentUpdate()
            Dim uri = "file:///c:/test/module1.vb"

            _documentManager.HandleDidOpen(New DidOpenTextDocumentParams With {
                .TextDocument = New TextDocumentItem With {
                    .Uri = uri,
                    .LanguageId = "vb",
                    .Version = 1,
                    .Text = "Module Module1" & vbLf & "End Module"
                }
            })

            _documentManager.HandleDidChange(New DidChangeTextDocumentParams With {
                .TextDocument = New VersionedTextDocumentIdentifier With {
                    .Uri = uri,
                    .Version = 2
                },
                .ContentChanges = New TextDocumentContentChangeEvent() {
                    New TextDocumentContentChangeEvent With {
                        .Text = "Module NewModule" & vbLf & "    Sub Main()" & vbLf & "    End Sub" & vbLf & "End Module"
                    }
                }
            })

            Dim doc = _documentManager.GetOpenDocument(uri)
            Assert.NotNull(doc)
            Assert.Contains("NewModule", doc.Text.ToString())
            Assert.Contains("Sub Main", doc.Text.ToString())
        End Sub

        <Fact>
        Public Sub HandleDidChange_EmptyChanges_DoesNotUpdateVersion()
            Dim uri = "file:///c:/test/module1.vb"

            _documentManager.HandleDidOpen(New DidOpenTextDocumentParams With {
                .TextDocument = New TextDocumentItem With {
                    .Uri = uri,
                    .LanguageId = "vb",
                    .Version = 1,
                    .Text = "Module Module1" & vbLf & "End Module"
                }
            })

            _documentManager.HandleDidChange(New DidChangeTextDocumentParams With {
                .TextDocument = New VersionedTextDocumentIdentifier With {
                    .Uri = uri,
                    .Version = 2
                },
                .ContentChanges = Array.Empty(Of TextDocumentContentChangeEvent)()
            })

            Dim doc = _documentManager.GetOpenDocument(uri)
            Assert.NotNull(doc)
            Assert.Equal(1, doc.Version)
        End Sub

        <Fact>
        Public Sub HandleDidClose_RemovesDocument()
            Dim uri = "file:///c:/test/module1.vb"

            _documentManager.HandleDidOpen(New DidOpenTextDocumentParams With {
                .TextDocument = New TextDocumentItem With {
                    .Uri = uri,
                    .LanguageId = "vb",
                    .Version = 1,
                    .Text = "Module Module1" & vbLf & "End Module"
                }
            })

            Assert.True(_documentManager.IsDocumentOpen(uri))

            _documentManager.HandleDidClose(New DidCloseTextDocumentParams With {
                .TextDocument = New TextDocumentIdentifier With {.Uri = uri}
            })

            Assert.False(_documentManager.IsDocumentOpen(uri))
        End Sub

        <Fact>
        Public Sub DocumentChanged_EventRaisedOnChange()
            Dim uri = "file:///c:/test/module1.vb"
            Dim eventArgs As DocumentChangedEventArgs = Nothing

            AddHandler _documentManager.DocumentChanged, Sub(sender, args) eventArgs = args

            _documentManager.HandleDidOpen(New DidOpenTextDocumentParams With {
                .TextDocument = New TextDocumentItem With {
                    .Uri = uri,
                    .LanguageId = "vb",
                    .Version = 1,
                    .Text = "Module Module1" & vbLf & "End Module"
                }
            })

            Assert.NotNull(eventArgs)
            Assert.Equal(uri, eventArgs.Uri)
            Assert.Equal(1, eventArgs.Version)
        End Sub

        <Fact>
        Public Sub OpenDocumentUris_ReturnsAllOpenDocuments()
            Dim uri1 = "file:///c:/test/module1.vb"
            Dim uri2 = "file:///c:/test/module2.vb"

            _documentManager.HandleDidOpen(New DidOpenTextDocumentParams With {
                .TextDocument = New TextDocumentItem With {
                    .Uri = uri1,
                    .LanguageId = "vb",
                    .Version = 1,
                    .Text = "Module Module1" & vbLf & "End Module"
                }
            })

            _documentManager.HandleDidOpen(New DidOpenTextDocumentParams With {
                .TextDocument = New TextDocumentItem With {
                    .Uri = uri2,
                    .LanguageId = "vb",
                    .Version = 1,
                    .Text = "Module Module2" & vbLf & "End Module"
                }
            })

            Dim openDocs = _documentManager.OpenDocumentUris.ToList()
            Assert.Equal(2, openDocs.Count)
            Assert.Contains(uri1, openDocs)
            Assert.Contains(uri2, openDocs)
        End Sub

        <Fact>
        Public Sub ReassociateDocumentsWithWorkspace_TriggersDocumentChanged()
            Dim uri = "file:///c:/test/module1.vb"
            Dim changedCount = 0

            AddHandler _documentManager.DocumentChanged, Sub(sender, args)
                                                             If args.Uri = uri Then
                                                                 changedCount += 1
                                                             End If
                                                         End Sub

            _documentManager.HandleDidOpen(New DidOpenTextDocumentParams With {
                .TextDocument = New TextDocumentItem With {
                    .Uri = uri,
                    .LanguageId = "vb",
                    .Version = 1,
                    .Text = "Module Module1" & vbLf & "End Module"
                }
            })

            Assert.Equal(1, changedCount)

            Dim doc = _documentManager.GetOpenDocument(uri)
            Assert.NotNull(doc)
            Assert.Null(doc.DocumentId)

            _documentManager.ReassociateDocumentsWithWorkspace()

            Assert.Equal(1, changedCount)
        End Sub

        <Fact>
        Public Sub HandleDidOpen_DocumentIdIsNullWithoutWorkspace()
            Dim uri = "file:///c:/test/module1.vb"

            _documentManager.HandleDidOpen(New DidOpenTextDocumentParams With {
                .TextDocument = New TextDocumentItem With {
                    .Uri = uri,
                    .LanguageId = "vb",
                    .Version = 1,
                    .Text = "Module Module1" & vbLf & "End Module"
                }
            })

            Dim doc = _documentManager.GetOpenDocument(uri)
            Assert.NotNull(doc)
            Assert.Null(doc.DocumentId)
        End Sub

        <Fact>
        Public Sub GetRoslynDocument_ReturnsNullForStandaloneDocument()
            Dim uri = "file:///c:/test/module1.vb"

            _documentManager.HandleDidOpen(New DidOpenTextDocumentParams With {
                .TextDocument = New TextDocumentItem With {
                    .Uri = uri,
                    .LanguageId = "vb",
                    .Version = 1,
                    .Text = "Module Module1" & vbLf & "End Module"
                }
            })

            Dim roslynDoc = _documentManager.GetRoslynDocument(uri)
            Assert.Null(roslynDoc)
        End Sub
    End Class

End Namespace
