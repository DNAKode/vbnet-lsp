using Microsoft.CodeAnalysis.Text;
using Microsoft.Extensions.Logging.Abstractions;
using VbNet.LanguageServer.Protocol;
using VbNet.LanguageServer.Workspace;
using Xunit;
using Range = VbNet.LanguageServer.Protocol.Range;

namespace VbNet.LanguageServer.Tests.Workspace;

public class DocumentManagerTests
{
    private readonly WorkspaceManager _workspaceManager;
    private readonly DocumentManager _documentManager;

    public DocumentManagerTests()
    {
        _workspaceManager = new WorkspaceManager(NullLogger<WorkspaceManager>.Instance);
        _documentManager = new DocumentManager(_workspaceManager, NullLogger<DocumentManager>.Instance);
    }

    [Fact]
    public void HandleDidOpen_TracksDocument()
    {
        var @params = new DidOpenTextDocumentParams
        {
            TextDocument = new TextDocumentItem
            {
                Uri = "file:///c:/test/module1.vb",
                LanguageId = "vb",
                Version = 1,
                Text = "Module Module1\nEnd Module"
            }
        };

        _documentManager.HandleDidOpen(@params);

        Assert.True(_documentManager.IsDocumentOpen(@params.TextDocument.Uri));
        var doc = _documentManager.GetOpenDocument(@params.TextDocument.Uri);
        Assert.NotNull(doc);
        Assert.Equal(1, doc.Version);
        Assert.Equal("vb", doc.LanguageId);
    }

    [Fact]
    public void HandleDidChange_UpdatesDocumentText()
    {
        var uri = "file:///c:/test/module1.vb";

        // First open the document
        _documentManager.HandleDidOpen(new DidOpenTextDocumentParams
        {
            TextDocument = new TextDocumentItem
            {
                Uri = uri,
                LanguageId = "vb",
                Version = 1,
                Text = "Module Module1\nEnd Module"
            }
        });

        // Apply an incremental change
        _documentManager.HandleDidChange(new DidChangeTextDocumentParams
        {
            TextDocument = new VersionedTextDocumentIdentifier
            {
                Uri = uri,
                Version = 2
            },
            ContentChanges = new[]
            {
                new TextDocumentContentChangeEvent
                {
                    Range = new Range
                    {
                        Start = new Position(0, 7),
                        End = new Position(0, 14)
                    },
                    Text = "TestModule"
                }
            }
        });

        var doc = _documentManager.GetOpenDocument(uri);
        Assert.NotNull(doc);
        Assert.Equal(2, doc.Version);
        Assert.Contains("TestModule", doc.Text.ToString());
    }

    [Fact]
    public void HandleDidChange_FullDocumentUpdate()
    {
        var uri = "file:///c:/test/module1.vb";

        _documentManager.HandleDidOpen(new DidOpenTextDocumentParams
        {
            TextDocument = new TextDocumentItem
            {
                Uri = uri,
                LanguageId = "vb",
                Version = 1,
                Text = "Module Module1\nEnd Module"
            }
        });

        // Full document replacement (no range specified)
        _documentManager.HandleDidChange(new DidChangeTextDocumentParams
        {
            TextDocument = new VersionedTextDocumentIdentifier
            {
                Uri = uri,
                Version = 2
            },
            ContentChanges = new[]
            {
                new TextDocumentContentChangeEvent
                {
                    Text = "Module NewModule\n    Sub Main()\n    End Sub\nEnd Module"
                }
            }
        });

        var doc = _documentManager.GetOpenDocument(uri);
        Assert.NotNull(doc);
        Assert.Contains("NewModule", doc.Text.ToString());
        Assert.Contains("Sub Main", doc.Text.ToString());
    }

    [Fact]
    public void HandleDidClose_RemovesDocument()
    {
        var uri = "file:///c:/test/module1.vb";

        _documentManager.HandleDidOpen(new DidOpenTextDocumentParams
        {
            TextDocument = new TextDocumentItem
            {
                Uri = uri,
                LanguageId = "vb",
                Version = 1,
                Text = "Module Module1\nEnd Module"
            }
        });

        Assert.True(_documentManager.IsDocumentOpen(uri));

        _documentManager.HandleDidClose(new DidCloseTextDocumentParams
        {
            TextDocument = new TextDocumentIdentifier { Uri = uri }
        });

        Assert.False(_documentManager.IsDocumentOpen(uri));
    }

    [Fact]
    public void DocumentChanged_EventRaisedOnChange()
    {
        var uri = "file:///c:/test/module1.vb";
        DocumentChangedEventArgs? eventArgs = null;

        _documentManager.DocumentChanged += (sender, args) => eventArgs = args;

        _documentManager.HandleDidOpen(new DidOpenTextDocumentParams
        {
            TextDocument = new TextDocumentItem
            {
                Uri = uri,
                LanguageId = "vb",
                Version = 1,
                Text = "Module Module1\nEnd Module"
            }
        });

        Assert.NotNull(eventArgs);
        Assert.Equal(uri, eventArgs.Uri);
        Assert.Equal(1, eventArgs.Version);
    }

    [Fact]
    public void OpenDocumentUris_ReturnsAllOpenDocuments()
    {
        var uri1 = "file:///c:/test/module1.vb";
        var uri2 = "file:///c:/test/module2.vb";

        _documentManager.HandleDidOpen(new DidOpenTextDocumentParams
        {
            TextDocument = new TextDocumentItem
            {
                Uri = uri1,
                LanguageId = "vb",
                Version = 1,
                Text = "Module Module1\nEnd Module"
            }
        });

        _documentManager.HandleDidOpen(new DidOpenTextDocumentParams
        {
            TextDocument = new TextDocumentItem
            {
                Uri = uri2,
                LanguageId = "vb",
                Version = 1,
                Text = "Module Module2\nEnd Module"
            }
        });

        var openDocs = _documentManager.OpenDocumentUris.ToList();
        Assert.Equal(2, openDocs.Count);
        Assert.Contains(uri1, openDocs);
        Assert.Contains(uri2, openDocs);
    }

    [Fact]
    public void ReassociateDocumentsWithWorkspace_TriggersDocumentChanged()
    {
        var uri = "file:///c:/test/module1.vb";
        var changedCount = 0;

        _documentManager.DocumentChanged += (sender, args) =>
        {
            if (args.Uri == uri) changedCount++;
        };

        // Open a document (will have DocumentId = null since no workspace loaded)
        _documentManager.HandleDidOpen(new DidOpenTextDocumentParams
        {
            TextDocument = new TextDocumentItem
            {
                Uri = uri,
                LanguageId = "vb",
                Version = 1,
                Text = "Module Module1\nEnd Module"
            }
        });

        // First DocumentChanged event fired by HandleDidOpen
        Assert.Equal(1, changedCount);

        // Verify document is not associated (no workspace)
        var doc = _documentManager.GetOpenDocument(uri);
        Assert.NotNull(doc);
        Assert.Null(doc.DocumentId);

        // Call reassociate - since there's no workspace loaded, nothing should happen
        _documentManager.ReassociateDocumentsWithWorkspace();

        // No additional events since document couldn't be found in workspace
        Assert.Equal(1, changedCount);
    }

    [Fact]
    public void HandleDidOpen_DocumentIdIsNullWithoutWorkspace()
    {
        var uri = "file:///c:/test/module1.vb";

        _documentManager.HandleDidOpen(new DidOpenTextDocumentParams
        {
            TextDocument = new TextDocumentItem
            {
                Uri = uri,
                LanguageId = "vb",
                Version = 1,
                Text = "Module Module1\nEnd Module"
            }
        });

        var doc = _documentManager.GetOpenDocument(uri);
        Assert.NotNull(doc);
        Assert.Null(doc.DocumentId); // No workspace, so no DocumentId
    }

    [Fact]
    public void GetRoslynDocument_ReturnsNullForStandaloneDocument()
    {
        var uri = "file:///c:/test/module1.vb";

        _documentManager.HandleDidOpen(new DidOpenTextDocumentParams
        {
            TextDocument = new TextDocumentItem
            {
                Uri = uri,
                LanguageId = "vb",
                Version = 1,
                Text = "Module Module1\nEnd Module"
            }
        });

        // Without a workspace, GetRoslynDocument should return null
        var roslynDoc = _documentManager.GetRoslynDocument(uri);
        Assert.Null(roslynDoc);
    }
}
