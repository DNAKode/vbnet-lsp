Imports System
Imports System.IO
Imports System.Linq
Imports System.Threading
Imports System.Threading.Tasks
Imports Microsoft.CodeAnalysis
Imports Microsoft.CodeAnalysis.Text
Imports Microsoft.CodeAnalysis.VisualBasic.Syntax
Imports Microsoft.Extensions.Logging.Abstractions
Imports VbNet.LanguageServer.Protocol
Imports VbNet.LanguageServer.Services
Imports VbNet.LanguageServer.Workspace
Imports Xunit

Namespace VbNet.LanguageServer.Tests.Integration

    ''' <summary>
    ''' Integration tests for SignatureHelpService with real VB.NET projects.
    ''' </summary>
    <Collection("MSBuild")>
    Public Class SignatureHelpIntegrationTests
        Implements IAsyncLifetime

        Private ReadOnly _workspaceManager As WorkspaceManager
        Private ReadOnly _documentManager As DocumentManager
        Private ReadOnly _signatureHelpService As SignatureHelpService

        Private Shared ReadOnly TestProjectsRoot As String = GetTestProjectsRoot()

        Public Sub New()
            _workspaceManager = New WorkspaceManager(NullLogger(Of WorkspaceManager).Instance)
            _documentManager = New DocumentManager(_workspaceManager, NullLogger(Of DocumentManager).Instance)
            _signatureHelpService = New SignatureHelpService(
                _workspaceManager,
                _documentManager,
                NullLogger(Of SignatureHelpService).Instance)
        End Sub

        Private Shared Function GetTestProjectsRoot() As String
            Dim assemblyLocation = GetType(SignatureHelpIntegrationTests).Assembly.Location
            Dim assemblyDir = Path.GetDirectoryName(assemblyLocation)
            Return Path.GetFullPath(Path.Combine(assemblyDir, "..", "..", "..", "..", "TestProjects"))
        End Function

        Public Function InitializeAsync() As Task Implements IAsyncLifetime.InitializeAsync
            _workspaceManager.Initialize()
            Return Task.CompletedTask
        End Function

        Public Async Function DisposeAsync() As Task Implements IAsyncLifetime.DisposeAsync
            Await _workspaceManager.DisposeAsync()
        End Function

        <Fact>
        Public Async Function GetSignatureHelpAsync_OnMethodCall_ReturnsSignatures() As Task
            Dim projectPath = Path.Combine(TestProjectsRoot, "SmallProject", "SmallProject.vbproj")
            Dim helperPath = Path.Combine(TestProjectsRoot, "SmallProject", "Helper.vb")

            If Not File.Exists(projectPath) Then
                Return
            End If

            Await _workspaceManager.LoadProjectAsync(projectPath)

            Dim helperUri = New Uri(helperPath).ToString()
            Dim text = Await File.ReadAllTextAsync(helperPath)
            Dim updatedText = InsertSignatureHelpSnippet(text)

            _documentManager.HandleDidOpen(New DidOpenTextDocumentParams With {
                .TextDocument = New TextDocumentItem With {
                    .Uri = helperUri,
                    .LanguageId = "vb",
                    .Version = 1,
                    .Text = updatedText
                }
            })

            Dim document = _documentManager.GetRoslynDocument(helperUri)
            Assert.NotNull(document)

            Dim root = Await document.GetSyntaxRootAsync(CancellationToken.None)
            Assert.NotNull(root)

            Dim invocation = root.DescendantNodes().
                OfType(Of InvocationExpressionSyntax)().
                FirstOrDefault(Function(node) node.ToString().Contains("Add(1, 2)", StringComparison.Ordinal))
            Assert.NotNull(invocation)

            Dim semanticModel = Await document.GetSemanticModelAsync(CancellationToken.None)
            Assert.NotNull(semanticModel)

            Dim symbolInfo = semanticModel.GetSymbolInfo(invocation, CancellationToken.None)
            Dim methodSymbol = TryCast(symbolInfo.Symbol, IMethodSymbol)
            If methodSymbol Is Nothing Then
                methodSymbol = symbolInfo.CandidateSymbols.OfType(Of IMethodSymbol)().FirstOrDefault()
            End If
            Assert.NotNull(methodSymbol)

            Dim sourceText As SourceText = SourceText.From(updatedText)
            Dim marker = "Add(1, 2)"
            Dim markerIndex = updatedText.IndexOf(marker, StringComparison.Ordinal)
            If markerIndex < 0 Then
                Return
            End If

            Dim positionOffset = markerIndex + "Add(".Length
            Dim line = sourceText.Lines.GetLineFromPosition(positionOffset)
            Dim position = New Position With {
                .Line = line.LineNumber,
                .Character = positionOffset - line.Start
            }

            Dim result = Await _signatureHelpService.GetSignatureHelpAsync(New SignatureHelpParams With {
                .TextDocument = New TextDocumentIdentifier With {.Uri = helperUri},
                .Position = position
            }, CancellationToken.None)

            Assert.NotNull(result)
            Assert.NotEmpty(result.Signatures)
            Assert.Contains(result.Signatures, Function(sig) sig.Label.Contains("Add", StringComparison.OrdinalIgnoreCase))
        End Function

        Private Shared Function InsertSignatureHelpSnippet(text As String) As String
            Dim snippet = String.Join(vbLf,
                "",
                "    Public Sub SignatureHelpTest()",
                "        Dim result = Add(1, 2)",
                "    End Sub",
                "")

            Dim endClassIndex = text.LastIndexOf("End Class", StringComparison.OrdinalIgnoreCase)
            If endClassIndex < 0 Then
                Return text & snippet
            End If

            Return text.Insert(endClassIndex, snippet)
        End Function
    End Class

End Namespace
