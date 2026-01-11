// SymbolsService - Provides Document and Workspace symbols via LSP
// Services Layer as defined in docs/architecture.md Section 5.4

using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.FindSymbols;
using Microsoft.CodeAnalysis.Text;
using Microsoft.CodeAnalysis.VisualBasic.Syntax;
using Microsoft.Extensions.Logging;
using VbNet.LanguageServer.Protocol;
using VbNet.LanguageServer.Workspace;

namespace VbNet.LanguageServer.Services;

/// <summary>
/// Provides document and workspace symbol navigation for VB.NET.
/// Uses Roslyn to extract symbol hierarchies and search across solutions.
/// </summary>
public sealed class SymbolsService
{
    private readonly WorkspaceManager _workspaceManager;
    private readonly DocumentManager _documentManager;
    private readonly ILogger<SymbolsService> _logger;

    public SymbolsService(
        WorkspaceManager workspaceManager,
        DocumentManager documentManager,
        ILogger<SymbolsService> logger)
    {
        _workspaceManager = workspaceManager ?? throw new ArgumentNullException(nameof(workspaceManager));
        _documentManager = documentManager ?? throw new ArgumentNullException(nameof(documentManager));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    /// <summary>
    /// Gets the document symbols (outline) for a document.
    /// Returns a hierarchical structure of symbols in the document.
    /// </summary>
    public async Task<DocumentSymbol[]> GetDocumentSymbolsAsync(
        DocumentSymbolParams @params,
        CancellationToken cancellationToken)
    {
        if (@params?.TextDocument == null)
        {
            return Array.Empty<DocumentSymbol>();
        }

        var uri = @params.TextDocument.Uri;

        _logger.LogDebug("Document symbols requested for: {Uri}", uri);

        var document = _documentManager.GetRoslynDocument(uri);
        if (document == null)
        {
            _logger.LogTrace("No Roslyn document found for: {Uri}", uri);
            return Array.Empty<DocumentSymbol>();
        }

        try
        {
            var sourceText = await document.GetTextAsync(cancellationToken);
            var syntaxRoot = await document.GetSyntaxRootAsync(cancellationToken);
            var semanticModel = await document.GetSemanticModelAsync(cancellationToken);

            if (syntaxRoot == null || semanticModel == null)
            {
                return Array.Empty<DocumentSymbol>();
            }

            cancellationToken.ThrowIfCancellationRequested();

            var symbols = new List<DocumentSymbol>();

            // Find all type declarations using VB.NET-specific syntax nodes
            var typeDeclarations = syntaxRoot.DescendantNodes()
                .Where(n => n is TypeBlockSyntax || n is ModuleBlockSyntax || n is EnumBlockSyntax)
                .ToList();

            foreach (var node in typeDeclarations)
            {
                cancellationToken.ThrowIfCancellationRequested();

                var declaredSymbol = semanticModel.GetDeclaredSymbol(node, cancellationToken);
                if (declaredSymbol is not INamedTypeSymbol typeSymbol)
                {
                    continue;
                }

                // Only process top-level type symbols here
                if (declaredSymbol.ContainingType != null)
                {
                    continue;
                }

                var docSymbol = await CreateDocumentSymbolAsync(
                    typeSymbol, node, sourceText, semanticModel, cancellationToken);

                if (docSymbol != null)
                {
                    symbols.Add(docSymbol);
                }
            }

            _logger.LogDebug("Found {Count} top-level symbols in: {Uri}", symbols.Count, uri);

            return symbols.ToArray();
        }
        catch (OperationCanceledException)
        {
            _logger.LogTrace("Document symbols request cancelled for: {Uri}", uri);
            throw;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error getting document symbols for: {Uri}", uri);
            return Array.Empty<DocumentSymbol>();
        }
    }

    /// <summary>
    /// Searches for symbols across the workspace matching the query.
    /// </summary>
    public async Task<SymbolInformation[]> GetWorkspaceSymbolsAsync(
        WorkspaceSymbolParams @params,
        CancellationToken cancellationToken)
    {
        if (@params == null)
        {
            return Array.Empty<SymbolInformation>();
        }

        var query = @params.Query ?? string.Empty;

        _logger.LogDebug("Workspace symbols requested with query: '{Query}'", query);

        var solution = _workspaceManager.CurrentSolution;
        if (solution == null)
        {
            _logger.LogTrace("No solution available");
            return Array.Empty<SymbolInformation>();
        }

        try
        {
            var results = new List<SymbolInformation>();

            // If query is empty, return nothing (avoid returning everything)
            if (string.IsNullOrWhiteSpace(query))
            {
                return Array.Empty<SymbolInformation>();
            }

            cancellationToken.ThrowIfCancellationRequested();

            // Search for symbols matching the query
            var symbols = await SymbolFinder.FindSourceDeclarationsWithPatternAsync(
                solution,
                query,
                SymbolFilter.TypeAndMember,
                cancellationToken);

            foreach (var symbol in symbols)
            {
                cancellationToken.ThrowIfCancellationRequested();

                // Skip implicit symbols
                if (symbol.IsImplicitlyDeclared)
                {
                    continue;
                }

                // Get first source location
                var location = symbol.Locations.FirstOrDefault(l => l.IsInSource);
                if (location == null)
                {
                    continue;
                }

                var syntaxTree = location.SourceTree;
                if (syntaxTree == null || string.IsNullOrEmpty(syntaxTree.FilePath))
                {
                    continue;
                }

                var sourceText = await syntaxTree.GetTextAsync(cancellationToken);
                var range = GetRange(location.SourceSpan, sourceText);
                var uri = new Uri(syntaxTree.FilePath).ToString();

                results.Add(new SymbolInformation
                {
                    Name = symbol.Name,
                    Kind = GetSymbolKind(symbol),
                    Location = new Protocol.Location
                    {
                        Uri = uri,
                        Range = range
                    },
                    ContainerName = symbol.ContainingType?.Name ?? symbol.ContainingNamespace?.ToDisplayString()
                });

                // Limit results to prevent overwhelming the client
                if (results.Count >= 100)
                {
                    break;
                }
            }

            _logger.LogDebug("Found {Count} workspace symbols for query: '{Query}'", results.Count, query);

            return results.ToArray();
        }
        catch (OperationCanceledException)
        {
            _logger.LogTrace("Workspace symbols request cancelled for query: '{Query}'", query);
            throw;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error getting workspace symbols for query: '{Query}'", query);
            return Array.Empty<SymbolInformation>();
        }
    }

    /// <summary>
    /// Creates a DocumentSymbol for a type symbol including its members.
    /// </summary>
    private async Task<DocumentSymbol?> CreateDocumentSymbolAsync(
        INamedTypeSymbol typeSymbol,
        SyntaxNode node,
        SourceText sourceText,
        SemanticModel semanticModel,
        CancellationToken cancellationToken)
    {
        var range = GetRange(node.Span, sourceText);
        var selectionRange = GetSelectionRange(typeSymbol, node, sourceText);

        var children = new List<DocumentSymbol>();

        // Add members as children
        foreach (var member in typeSymbol.GetMembers())
        {
            cancellationToken.ThrowIfCancellationRequested();

            // Skip implicit members
            if (member.IsImplicitlyDeclared)
            {
                continue;
            }

            // Skip nested types (they'll be processed separately)
            if (member is INamedTypeSymbol)
            {
                continue;
            }

            // Get the member's syntax location
            var memberLocation = member.Locations.FirstOrDefault(l => l.IsInSource);
            if (memberLocation == null)
            {
                continue;
            }

            var memberNode = node.FindNode(memberLocation.SourceSpan);
            if (memberNode == null)
            {
                continue;
            }

            var memberSymbol = CreateMemberSymbol(member, memberNode, sourceText);
            if (memberSymbol != null)
            {
                children.Add(memberSymbol);
            }
        }

        // Add nested types as children
        foreach (var nestedType in typeSymbol.GetTypeMembers())
        {
            cancellationToken.ThrowIfCancellationRequested();

            if (nestedType.IsImplicitlyDeclared)
            {
                continue;
            }

            var nestedLocation = nestedType.Locations.FirstOrDefault(l => l.IsInSource);
            if (nestedLocation == null)
            {
                continue;
            }

            var nestedNode = node.FindNode(nestedLocation.SourceSpan);
            if (nestedNode == null)
            {
                continue;
            }

            var nestedSymbol = await CreateDocumentSymbolAsync(
                nestedType, nestedNode, sourceText, semanticModel, cancellationToken);

            if (nestedSymbol != null)
            {
                children.Add(nestedSymbol);
            }
        }

        return new DocumentSymbol
        {
            Name = typeSymbol.Name,
            Detail = GetTypeDetail(typeSymbol),
            Kind = GetSymbolKind(typeSymbol),
            Range = range,
            SelectionRange = selectionRange,
            Children = children.Count > 0 ? children.ToArray() : null
        };
    }

    /// <summary>
    /// Creates a DocumentSymbol for a member (method, property, field, etc.).
    /// </summary>
    private DocumentSymbol? CreateMemberSymbol(
        ISymbol member,
        SyntaxNode node,
        SourceText sourceText)
    {
        var range = GetRange(node.Span, sourceText);
        var selectionRange = GetSelectionRange(member, node, sourceText);

        return new DocumentSymbol
        {
            Name = member.Name,
            Detail = GetMemberDetail(member),
            Kind = GetSymbolKind(member),
            Range = range,
            SelectionRange = selectionRange,
            Children = null
        };
    }

    /// <summary>
    /// Gets the selection range (identifier span) for a symbol.
    /// </summary>
    private Protocol.Range GetSelectionRange(ISymbol symbol, SyntaxNode node, SourceText sourceText)
    {
        // Try to find the identifier token
        foreach (var token in node.ChildTokens())
        {
            if (token.IsKind(Microsoft.CodeAnalysis.VisualBasic.SyntaxKind.IdentifierToken) &&
                token.Text == symbol.Name)
            {
                return GetRange(token.Span, sourceText);
            }
        }

        // Fallback to node span
        return GetRange(node.Span, sourceText);
    }

    /// <summary>
    /// Gets detail text for a type symbol.
    /// </summary>
    private static string? GetTypeDetail(INamedTypeSymbol type)
    {
        if (type.TypeParameters.Length > 0)
        {
            return $"(Of {string.Join(", ", type.TypeParameters.Select(tp => tp.Name))})";
        }
        return null;
    }

    /// <summary>
    /// Gets detail text for a member symbol.
    /// </summary>
    private static string? GetMemberDetail(ISymbol member)
    {
        return member switch
        {
            IMethodSymbol method => method.ReturnsVoid
                ? null
                : $"As {method.ReturnType.ToDisplayString(SymbolDisplayFormat.MinimallyQualifiedFormat)}",
            IPropertySymbol property => $"As {property.Type.ToDisplayString(SymbolDisplayFormat.MinimallyQualifiedFormat)}",
            IFieldSymbol field => $"As {field.Type.ToDisplayString(SymbolDisplayFormat.MinimallyQualifiedFormat)}",
            IEventSymbol evt => $"As {evt.Type.ToDisplayString(SymbolDisplayFormat.MinimallyQualifiedFormat)}",
            _ => null
        };
    }

    /// <summary>
    /// Maps a Roslyn symbol to an LSP SymbolKind.
    /// </summary>
    private static Protocol.SymbolKind GetSymbolKind(ISymbol symbol)
    {
        return symbol switch
        {
            INamedTypeSymbol type => type.TypeKind switch
            {
                TypeKind.Class => Protocol.SymbolKind.Class,
                TypeKind.Interface => Protocol.SymbolKind.Interface,
                TypeKind.Struct => Protocol.SymbolKind.Struct,
                TypeKind.Enum => Protocol.SymbolKind.Enum,
                TypeKind.Module => Protocol.SymbolKind.Module,
                TypeKind.Delegate => Protocol.SymbolKind.Function,
                _ => Protocol.SymbolKind.Class
            },
            IMethodSymbol method => method.MethodKind switch
            {
                MethodKind.Constructor => Protocol.SymbolKind.Constructor,
                MethodKind.Destructor => Protocol.SymbolKind.Method,
                MethodKind.PropertyGet => Protocol.SymbolKind.Property,
                MethodKind.PropertySet => Protocol.SymbolKind.Property,
                MethodKind.EventAdd => Protocol.SymbolKind.Event,
                MethodKind.EventRemove => Protocol.SymbolKind.Event,
                _ => Protocol.SymbolKind.Method
            },
            IPropertySymbol => Protocol.SymbolKind.Property,
            IFieldSymbol field => field.IsConst ? Protocol.SymbolKind.Constant : Protocol.SymbolKind.Field,
            IEventSymbol => Protocol.SymbolKind.Event,
            INamespaceSymbol => Protocol.SymbolKind.Namespace,
            IParameterSymbol => Protocol.SymbolKind.Variable,
            ILocalSymbol => Protocol.SymbolKind.Variable,
            ITypeParameterSymbol => Protocol.SymbolKind.TypeParameter,
            _ => Protocol.SymbolKind.Variable
        };
    }

    /// <summary>
    /// Converts a TextSpan to an LSP Range.
    /// </summary>
    private static Protocol.Range GetRange(TextSpan span, SourceText sourceText)
    {
        var startLine = sourceText.Lines.GetLineFromPosition(span.Start);
        var endLine = sourceText.Lines.GetLineFromPosition(span.End);

        return new Protocol.Range
        {
            Start = new Position
            {
                Line = startLine.LineNumber,
                Character = span.Start - startLine.Start
            },
            End = new Position
            {
                Line = endLine.LineNumber,
                Character = span.End - endLine.Start
            }
        };
    }
}
