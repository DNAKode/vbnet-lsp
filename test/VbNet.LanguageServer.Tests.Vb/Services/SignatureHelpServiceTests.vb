Imports System.Threading
Imports System.Threading.Tasks
Imports Microsoft.Extensions.Logging.Abstractions
Imports VbNet.LanguageServer.Protocol
Imports VbNet.LanguageServer.Services
Imports VbNet.LanguageServer.Workspace
Imports Xunit

Namespace VbNet.LanguageServer.Tests.Services

    ''' <summary>
    ''' Unit tests for SignatureHelpService.
    ''' </summary>
    Public Class SignatureHelpServiceTests
        Private ReadOnly _workspaceManager As WorkspaceManager
        Private ReadOnly _documentManager As DocumentManager
        Private ReadOnly _signatureHelpService As SignatureHelpService

        Public Sub New()
            _workspaceManager = New WorkspaceManager(NullLogger(Of WorkspaceManager).Instance)
            _documentManager = New DocumentManager(_workspaceManager, NullLogger(Of DocumentManager).Instance)
            _signatureHelpService = New SignatureHelpService(
                _workspaceManager,
                _documentManager,
                NullLogger(Of SignatureHelpService).Instance)

            _workspaceManager.Initialize()
        End Sub

        <Fact>
        Public Async Function GetSignatureHelpAsync_NoDocument_ReturnsNull() As Task
            Dim request = New SignatureHelpParams With {
                .TextDocument = New TextDocumentIdentifier With {.Uri = "file:///nonexistent.vb"},
                .Position = New Position With {.Line = 0, .Character = 0}
            }

            Dim result = Await _signatureHelpService.GetSignatureHelpAsync(request, CancellationToken.None)

            Assert.Null(result)
        End Function

        <Fact>
        Public Async Function GetSignatureHelpAsync_NullParams_ReturnsNull() As Task
            Dim result = Await _signatureHelpService.GetSignatureHelpAsync(Nothing, CancellationToken.None)

            Assert.Null(result)
        End Function
    End Class

End Namespace
