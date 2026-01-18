' FoldingRangeService - Provides folding ranges for VB.NET documents
' Services Layer as defined in docs/architecture.md Section 5.4

Imports System.Collections.Generic
Imports System.Linq
Imports Microsoft.CodeAnalysis
Imports Microsoft.CodeAnalysis.Text
Imports Microsoft.CodeAnalysis.VisualBasic
Imports Microsoft.CodeAnalysis.VisualBasic.Syntax
Imports Microsoft.Extensions.Logging
Imports VbNet.LanguageServer.Protocol
Imports VbNet.LanguageServer.Workspace

Namespace Services

    ''' <summary>
    ''' Provides folding ranges for VB.NET documents.
    ''' </summary>
    Public NotInheritable Class FoldingRangeService
        Private ReadOnly _workspaceManager As WorkspaceManager
        Private ReadOnly _documentManager As DocumentManager
        Private ReadOnly _logger As ILogger(Of FoldingRangeService)

        Public Sub New(workspaceManager As WorkspaceManager, documentManager As DocumentManager, logger As ILogger(Of FoldingRangeService))
            If workspaceManager Is Nothing Then
                Throw New ArgumentNullException(NameOf(workspaceManager))
            End If
            If documentManager Is Nothing Then
                Throw New ArgumentNullException(NameOf(documentManager))
            End If
            If logger Is Nothing Then
                Throw New ArgumentNullException(NameOf(logger))
            End If

            _workspaceManager = workspaceManager
            _documentManager = documentManager
            _logger = logger
        End Sub

        ''' <summary>
        ''' Gets folding ranges for a document.
        ''' </summary>
        Public Async Function GetFoldingRangesAsync(parameters As FoldingRangeParams, cancellationToken As CancellationToken) As Task(Of FoldingRange())
            If parameters Is Nothing OrElse parameters.TextDocument Is Nothing Then
                Return Array.Empty(Of FoldingRange)()
            End If

            Dim uri = parameters.TextDocument.Uri
            Dim document = _documentManager.GetRoslynDocument(uri)

            Dim sourceText As SourceText = Nothing
            Dim root As SyntaxNode = Nothing

            If document IsNot Nothing Then
                sourceText = Await document.GetTextAsync(cancellationToken).ConfigureAwait(False)
                root = Await document.GetSyntaxRootAsync(cancellationToken).ConfigureAwait(False)
            Else
                sourceText = Await _documentManager.GetSourceTextAsync(uri, cancellationToken).ConfigureAwait(False)
                If sourceText Is Nothing Then
                    Return Array.Empty(Of FoldingRange)()
                End If

                Dim syntaxTree = VisualBasicSyntaxTree.ParseText(sourceText, cancellationToken:=cancellationToken)
                root = Await syntaxTree.GetRootAsync(cancellationToken).ConfigureAwait(False)
            End If

            If root Is Nothing OrElse sourceText Is Nothing Then
                Return Array.Empty(Of FoldingRange)()
            End If

            Dim ranges As New List(Of FoldingRange)()

            AddRegionRanges(root, sourceText, ranges)
            AddBlockRanges(root, sourceText, ranges)

            Return ranges.ToArray()
        End Function

        Private Shared Sub AddRegionRanges(root As SyntaxNode, sourceText As SourceText, ranges As List(Of FoldingRange))
            Dim regionStack As New Stack(Of RegionDirectiveTriviaSyntax)()

            For Each trivia In root.DescendantTrivia(descendIntoTrivia:=True)
                Dim nodeStructure = trivia.GetStructure()
                Dim region = TryCast(nodeStructure, RegionDirectiveTriviaSyntax)
                If region IsNot Nothing Then
                    regionStack.Push(region)
                    Continue For
                End If

                Dim endRegion = TryCast(nodeStructure, EndRegionDirectiveTriviaSyntax)
                If endRegion IsNot Nothing Then
                    If regionStack.Count = 0 Then
                        Continue For
                    End If

                    Dim startRegion = regionStack.Pop()
                    Dim span = TextSpan.FromBounds(startRegion.SpanStart, endRegion.Span.End)
                    AddRange(span, sourceText, FoldingRangeKind.Region, ranges)
                End If
            Next
        End Sub

        Private Shared Sub AddBlockRanges(root As SyntaxNode, sourceText As SourceText, ranges As List(Of FoldingRange))
            For Each node In root.DescendantNodes()
                If TypeOf node Is NamespaceBlockSyntax Then
                    AddRange(node.Span, sourceText, Nothing, ranges)
                ElseIf TypeOf node Is ModuleBlockSyntax Then
                    AddRange(node.Span, sourceText, Nothing, ranges)
                ElseIf TypeOf node Is ClassBlockSyntax Then
                    AddRange(node.Span, sourceText, Nothing, ranges)
                ElseIf TypeOf node Is StructureBlockSyntax Then
                    AddRange(node.Span, sourceText, Nothing, ranges)
                ElseIf TypeOf node Is InterfaceBlockSyntax Then
                    AddRange(node.Span, sourceText, Nothing, ranges)
                ElseIf TypeOf node Is EnumBlockSyntax Then
                    AddRange(node.Span, sourceText, Nothing, ranges)
                ElseIf TypeOf node Is MethodBlockSyntax Then
                    AddRange(node.Span, sourceText, Nothing, ranges)
                ElseIf TypeOf node Is PropertyBlockSyntax Then
                    AddRange(node.Span, sourceText, Nothing, ranges)
                ElseIf TypeOf node Is EventBlockSyntax Then
                    AddRange(node.Span, sourceText, Nothing, ranges)
                ElseIf TypeOf node Is AccessorBlockSyntax Then
                    AddRange(node.Span, sourceText, Nothing, ranges)
                End If
            Next
        End Sub

        Private Shared Sub AddRange(span As TextSpan, sourceText As SourceText, kind As String, ranges As List(Of FoldingRange))
            If span.Length = 0 Then
                Return
            End If

            Dim startLine = sourceText.Lines.GetLineFromPosition(span.Start)
            Dim endPosition = Math.Max(span.[End] - 1, span.Start)
            Dim endLine = sourceText.Lines.GetLineFromPosition(endPosition)

            If endLine.LineNumber <= startLine.LineNumber Then
                Return
            End If

            ranges.Add(New FoldingRange With {
                .StartLine = startLine.LineNumber,
                .EndLine = endLine.LineNumber,
                .Kind = kind
            })
        End Sub
    End Class

End Namespace
