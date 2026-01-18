Imports System
Imports System.Collections.Generic
Imports System.IO
Imports System.Linq
Imports System.Threading
Imports System.Threading.Tasks
Imports Microsoft.Extensions.Logging.Abstractions
Imports Xunit
Imports VbNet.LanguageServer.Core
Imports LspServer = VbNet.LanguageServer.Core.LanguageServer
Imports LspProtocol = VbNet.LanguageServer.Protocol

Namespace VbNet.LanguageServer.Tests.Integration

    ''' <summary>
    ''' Integration tests for the full LanguageServer lifecycle.
    ''' Tests server initialization, document handling, and diagnostics publishing.
    ''' </summary>
    <Collection("MSBuild")>
    Public Class LanguageServerIntegrationTests
        Implements IAsyncDisposable

        Private ReadOnly _transport As MockTransport
        Private ReadOnly _server As LspServer

        Private Shared ReadOnly TestProjectsRoot As String = GetTestProjectsRoot()

        Public Sub New()
            _transport = New MockTransport()
            Dim loggerFactory = NullLoggerFactory.Instance
            _server = New LspServer(_transport, loggerFactory)
        End Sub

        Private Shared Function GetTestProjectsRoot() As String
            Dim assemblyLocation = GetType(LanguageServerIntegrationTests).Assembly.Location
            Dim assemblyDir = Path.GetDirectoryName(assemblyLocation)
            Dim testProjectsPath = Path.GetFullPath(Path.Combine(assemblyDir, "..", "..", "..", "..", "TestProjects"))
            Return testProjectsPath
        End Function

        Public Function DisposeAsync() As ValueTask Implements IAsyncDisposable.DisposeAsync
            Return _server.DisposeAsync()
        End Function

        <Fact>
        Public Sub Server_InitialState_IsNotStarted()
            Assert.Equal(ServerState.NotStarted, _server.State)
        End Sub

        <Fact>
        Public Sub Server_HasCorrectServerInfo()
            Assert.Equal("VbNet.LanguageServer", LspServer.ServerName)
            Assert.Equal("0.1.1", LspServer.ServerVersion)
        End Sub

        <Fact>
        Public Sub WorkspaceManager_IsAccessible()
            Assert.NotNull(_server.WorkspaceManager)
        End Sub

        <Fact>
        Public Sub DocumentManager_IsAccessible()
            Assert.NotNull(_server.DocumentManager)
        End Sub

        <Fact>
        Public Sub DiagnosticsService_IsAccessible()
            Assert.NotNull(_server.DiagnosticsService)
        End Sub

        <Fact>
        Public Async Function SendNotification_PublishesDiagnostics() As Task
            Await _transport.StartAsync()

            Dim diagnosticsParams = New LspProtocol.PublishDiagnosticsParams With {
                .Uri = "file:///test.vb",
                .Diagnostics = New LspProtocol.Diagnostic() {
                    New LspProtocol.Diagnostic With {
                        .Range = New LspProtocol.Range With {
                            .Start = New LspProtocol.Position With {.Line = 0, .Character = 0},
                            .End = New LspProtocol.Position With {.Line = 0, .Character = 10}
                        },
                        .Severity = LspProtocol.DiagnosticSeverity.Error,
                        .Code = "BC30451",
                        .Source = "vbnet",
                        .Message = "Test error"
                    }
                }
            }

            Await _server.SendNotificationAsync("textDocument/publishDiagnostics", diagnosticsParams)

            Dim sentMessages = _transport.GetSentMessages()
            Assert.Single(sentMessages)
            Assert.Contains("textDocument/publishDiagnostics", sentMessages(0))
        End Function
    End Class

    ''' <summary>
    ''' A mock transport for testing the language server without actual I/O.
    ''' </summary>
    Public Class MockTransport
        Implements LspProtocol.ITransport

        Private ReadOnly _sentMessages As New List(Of String)()
        Private _isStarted As Boolean = False

        Public Function GetSentMessages() As List(Of String)
            Return _sentMessages.ToList()
        End Function

        Public Function StartAsync(Optional cancellationToken As CancellationToken = Nothing) As Task Implements LspProtocol.ITransport.StartAsync
            _isStarted = True
            Return Task.CompletedTask
        End Function

        Public Function ReadMessageAsync(Optional cancellationToken As CancellationToken = Nothing) As Task(Of String) Implements LspProtocol.ITransport.ReadMessageAsync
            Return Task.FromResult(Of String)(Nothing)
        End Function

        Public Function WriteMessageAsync(message As String, Optional cancellationToken As CancellationToken = Nothing) As Task Implements LspProtocol.ITransport.WriteMessageAsync
            If Not _isStarted Then
                Throw New InvalidOperationException("Transport not started")
            End If
            _sentMessages.Add(message)
            Return Task.CompletedTask
        End Function

        Public Function DisposeAsync() As ValueTask Implements IAsyncDisposable.DisposeAsync
            Return ValueTask.CompletedTask
        End Function
    End Class

End Namespace
