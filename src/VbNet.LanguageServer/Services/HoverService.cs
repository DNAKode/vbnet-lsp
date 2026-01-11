// HoverService - Provides symbol hover information via LSP
// Services Layer as defined in docs/architecture.md Section 5.4

using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.Text;
using Microsoft.Extensions.Logging;
using VbNet.LanguageServer.Protocol;
using VbNet.LanguageServer.Workspace;

namespace VbNet.LanguageServer.Services;

/// <summary>
/// Provides hover information (quick info) for VB.NET documents.
/// Uses Roslyn semantic model to get symbol information.
/// </summary>
public sealed class HoverService
{
    private readonly WorkspaceManager _workspaceManager;
    private readonly DocumentManager _documentManager;
    private readonly ILogger<HoverService> _logger;

    public HoverService(
        WorkspaceManager workspaceManager,
        DocumentManager documentManager,
        ILogger<HoverService> logger)
    {
        _workspaceManager = workspaceManager ?? throw new ArgumentNullException(nameof(workspaceManager));
        _documentManager = documentManager ?? throw new ArgumentNullException(nameof(documentManager));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    /// <summary>
    /// Gets hover information for a document at the specified position.
    /// </summary>
    public async Task<Hover?> GetHoverAsync(
        HoverParams @params,
        CancellationToken cancellationToken)
    {
        if (@params?.TextDocument == null)
        {
            return null;
        }

        var uri = @params.TextDocument.Uri;
        var position = @params.Position;

        _logger.LogDebug("Hover requested at {Uri} ({Line}:{Character})",
            uri, position.Line, position.Character);

        var document = _documentManager.GetRoslynDocument(uri);
        if (document == null)
        {
            _logger.LogTrace("No Roslyn document found for: {Uri}", uri);
            return null;
        }

        try
        {
            // Get the source text and calculate offset
            var sourceText = await document.GetTextAsync(cancellationToken);
            var offset = GetOffset(position, sourceText);

            cancellationToken.ThrowIfCancellationRequested();

            // Get semantic model
            var semanticModel = await document.GetSemanticModelAsync(cancellationToken);
            if (semanticModel == null)
            {
                _logger.LogWarning("Could not get semantic model for: {Uri}", uri);
                return null;
            }

            // Get syntax root and find token at position
            var syntaxRoot = await document.GetSyntaxRootAsync(cancellationToken);
            if (syntaxRoot == null)
            {
                return null;
            }

            var token = syntaxRoot.FindToken(offset);
            if (token.Span.Length == 0)
            {
                return null;
            }

            // Ensure we have a parent node to analyze
            var parentNode = token.Parent;
            if (parentNode == null)
            {
                return null;
            }

            cancellationToken.ThrowIfCancellationRequested();

            // Try to get symbol info
            var symbolInfo = semanticModel.GetSymbolInfo(parentNode, cancellationToken);
            var symbol = symbolInfo.Symbol ?? symbolInfo.CandidateSymbols.FirstOrDefault();

            // If no symbol, try getting declared symbol
            if (symbol == null)
            {
                symbol = semanticModel.GetDeclaredSymbol(parentNode, cancellationToken);
            }

            // If still no symbol, try type info
            if (symbol == null)
            {
                var typeInfo = semanticModel.GetTypeInfo(parentNode, cancellationToken);
                symbol = typeInfo.Type;
            }

            if (symbol == null)
            {
                _logger.LogTrace("No symbol found at position for: {Uri}", uri);
                return null;
            }

            // Build hover content
            var hoverContent = BuildHoverContent(symbol, semanticModel, offset);
            if (string.IsNullOrEmpty(hoverContent))
            {
                return null;
            }

            // Get the range of the token
            var range = GetRange(token.Span, sourceText);

            _logger.LogDebug("Returning hover for symbol: {Symbol}", symbol.Name);

            return new Hover
            {
                Contents = new MarkupContent
                {
                    Kind = MarkupKind.Markdown,
                    Value = hoverContent
                },
                Range = range
            };
        }
        catch (OperationCanceledException)
        {
            _logger.LogTrace("Hover request cancelled for: {Uri}", uri);
            throw;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error getting hover for: {Uri}", uri);
            return null;
        }
    }

    /// <summary>
    /// Builds markdown content for hover display.
    /// </summary>
    private string BuildHoverContent(ISymbol symbol, SemanticModel semanticModel, int position)
    {
        var sb = new System.Text.StringBuilder();

        // Add symbol signature
        var signature = GetSymbolSignature(symbol);
        if (!string.IsNullOrEmpty(signature))
        {
            sb.AppendLine("```vb");
            sb.AppendLine(signature);
            sb.AppendLine("```");
        }

        // Add documentation if available
        var documentation = GetDocumentation(symbol);
        if (!string.IsNullOrEmpty(documentation))
        {
            sb.AppendLine();
            sb.AppendLine(documentation);
        }

        // Add containing type/namespace info
        var containerInfo = GetContainerInfo(symbol);
        if (!string.IsNullOrEmpty(containerInfo))
        {
            sb.AppendLine();
            sb.AppendLine($"*{containerInfo}*");
        }

        return sb.ToString().Trim();
    }

    /// <summary>
    /// Gets a human-readable signature for a symbol.
    /// </summary>
    private static string GetSymbolSignature(ISymbol symbol)
    {
        return symbol switch
        {
            IMethodSymbol method => GetMethodSignature(method),
            IPropertySymbol property => GetPropertySignature(property),
            IFieldSymbol field => GetFieldSignature(field),
            ILocalSymbol local => GetLocalSignature(local),
            IParameterSymbol param => GetParameterSignature(param),
            INamedTypeSymbol type => GetTypeSignature(type),
            INamespaceSymbol ns => $"Namespace {ns.ToDisplayString()}",
            IEventSymbol evt => GetEventSignature(evt),
            _ => symbol.ToDisplayString(SymbolDisplayFormat.MinimallyQualifiedFormat)
        };
    }

    private static string GetMethodSignature(IMethodSymbol method)
    {
        var returnType = method.ReturnsVoid ? "" : $" As {method.ReturnType.ToDisplayString(SymbolDisplayFormat.MinimallyQualifiedFormat)}";
        var parameters = string.Join(", ", method.Parameters.Select(p =>
            $"{p.Name} As {p.Type.ToDisplayString(SymbolDisplayFormat.MinimallyQualifiedFormat)}"));

        var accessibility = GetAccessibilityString(method.DeclaredAccessibility);
        var modifiers = GetMethodModifiers(method);

        if (method.MethodKind == MethodKind.Constructor)
        {
            return $"{accessibility}{modifiers}Sub New({parameters})";
        }

        var keyword = method.ReturnsVoid ? "Sub" : "Function";
        return $"{accessibility}{modifiers}{keyword} {method.Name}({parameters}){returnType}";
    }

    private static string GetPropertySignature(IPropertySymbol property)
    {
        var accessibility = GetAccessibilityString(property.DeclaredAccessibility);
        var modifiers = property.IsReadOnly ? "ReadOnly " : property.IsWriteOnly ? "WriteOnly " : "";
        var type = property.Type.ToDisplayString(SymbolDisplayFormat.MinimallyQualifiedFormat);

        return $"{accessibility}{modifiers}Property {property.Name} As {type}";
    }

    private static string GetFieldSignature(IFieldSymbol field)
    {
        var accessibility = GetAccessibilityString(field.DeclaredAccessibility);
        var modifiers = "";
        if (field.IsConst) modifiers = "Const ";
        else if (field.IsReadOnly) modifiers = "ReadOnly ";
        else if (field.IsStatic) modifiers = "Shared ";

        var type = field.Type.ToDisplayString(SymbolDisplayFormat.MinimallyQualifiedFormat);

        return $"{accessibility}{modifiers}{field.Name} As {type}";
    }

    private static string GetLocalSignature(ILocalSymbol local)
    {
        var modifiers = local.IsConst ? "Const " : "";
        var type = local.Type.ToDisplayString(SymbolDisplayFormat.MinimallyQualifiedFormat);
        return $"{modifiers}Dim {local.Name} As {type}";
    }

    private static string GetParameterSignature(IParameterSymbol param)
    {
        var type = param.Type.ToDisplayString(SymbolDisplayFormat.MinimallyQualifiedFormat);
        var modifier = param.RefKind switch
        {
            RefKind.Ref => "ByRef ",
            RefKind.Out => "ByRef ",
            _ => ""
        };
        return $"{modifier}{param.Name} As {type}";
    }

    private static string GetTypeSignature(INamedTypeSymbol type)
    {
        var accessibility = GetAccessibilityString(type.DeclaredAccessibility);
        var keyword = type.TypeKind switch
        {
            TypeKind.Class => "Class",
            TypeKind.Interface => "Interface",
            TypeKind.Struct => "Structure",
            TypeKind.Enum => "Enum",
            TypeKind.Module => "Module",
            TypeKind.Delegate => "Delegate",
            _ => "Type"
        };

        var typeParams = "";
        if (type.TypeParameters.Length > 0)
        {
            typeParams = $"(Of {string.Join(", ", type.TypeParameters.Select(tp => tp.Name))})";
        }

        return $"{accessibility}{keyword} {type.Name}{typeParams}";
    }

    private static string GetEventSignature(IEventSymbol evt)
    {
        var accessibility = GetAccessibilityString(evt.DeclaredAccessibility);
        var type = evt.Type.ToDisplayString(SymbolDisplayFormat.MinimallyQualifiedFormat);
        return $"{accessibility}Event {evt.Name} As {type}";
    }

    private static string GetAccessibilityString(Accessibility accessibility)
    {
        return accessibility switch
        {
            Accessibility.Public => "Public ",
            Accessibility.Private => "Private ",
            Accessibility.Protected => "Protected ",
            Accessibility.Internal => "Friend ",
            Accessibility.ProtectedOrInternal => "Protected Friend ",
            Accessibility.ProtectedAndInternal => "Private Protected ",
            _ => ""
        };
    }

    private static string GetMethodModifiers(IMethodSymbol method)
    {
        var modifiers = new List<string>();

        if (method.IsStatic) modifiers.Add("Shared");
        if (method.IsOverride) modifiers.Add("Overrides");
        if (method.IsVirtual && !method.IsOverride) modifiers.Add("Overridable");
        if (method.IsAbstract) modifiers.Add("MustOverride");
        if (method.IsSealed && method.IsOverride) modifiers.Add("NotOverridable");
        if (method.IsAsync) modifiers.Add("Async");

        return modifiers.Count > 0 ? string.Join(" ", modifiers) + " " : "";
    }

    /// <summary>
    /// Gets XML documentation for a symbol.
    /// </summary>
    private static string GetDocumentation(ISymbol symbol)
    {
        var xml = symbol.GetDocumentationCommentXml();
        if (string.IsNullOrEmpty(xml))
        {
            return string.Empty;
        }

        // Simple extraction of summary from XML
        var summaryStart = xml.IndexOf("<summary>", StringComparison.OrdinalIgnoreCase);
        var summaryEnd = xml.IndexOf("</summary>", StringComparison.OrdinalIgnoreCase);

        if (summaryStart >= 0 && summaryEnd > summaryStart)
        {
            var summary = xml.Substring(summaryStart + 9, summaryEnd - summaryStart - 9);
            return CleanXmlContent(summary);
        }

        return string.Empty;
    }

    private static string CleanXmlContent(string content)
    {
        // Remove XML tags and clean up whitespace
        content = System.Text.RegularExpressions.Regex.Replace(content, @"<[^>]+>", "");
        content = System.Text.RegularExpressions.Regex.Replace(content, @"\s+", " ");
        return content.Trim();
    }

    /// <summary>
    /// Gets container (namespace/type) information for a symbol.
    /// </summary>
    private static string GetContainerInfo(ISymbol symbol)
    {
        var container = symbol.ContainingType ?? (ISymbol?)symbol.ContainingNamespace;
        if (container == null || container is INamespaceSymbol ns && ns.IsGlobalNamespace)
        {
            return string.Empty;
        }

        return $"In {container.ToDisplayString(SymbolDisplayFormat.MinimallyQualifiedFormat)}";
    }

    /// <summary>
    /// Converts an LSP Position to a Roslyn offset.
    /// </summary>
    private static int GetOffset(Position position, SourceText text)
    {
        var line = Math.Min(position.Line, text.Lines.Count - 1);
        line = Math.Max(0, line);

        var textLine = text.Lines[line];
        var character = Math.Min(position.Character, textLine.End - textLine.Start);
        character = Math.Max(0, character);

        return textLine.Start + character;
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
