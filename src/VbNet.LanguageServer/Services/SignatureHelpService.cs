// SignatureHelpService - Provides parameter hints via LSP
// Services Layer as defined in docs/architecture.md Section 5.4

using System.Collections;
using System.Reflection;
using System.Text;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.Text;
using Microsoft.CodeAnalysis.VisualBasic.Syntax;
using Microsoft.Extensions.Logging;
using VbNet.LanguageServer.Protocol;
using VbNet.LanguageServer.Workspace;

namespace VbNet.LanguageServer.Services;

/// <summary>
/// Provides signature help (parameter hints) for VB.NET documents.
/// Uses Roslyn's SignatureHelpService for accurate overload data.
/// </summary>
public sealed class SignatureHelpService
{
    private readonly WorkspaceManager _workspaceManager;
    private readonly DocumentManager _documentManager;
    private readonly ILogger<SignatureHelpService> _logger;

    private static readonly string[] DefaultTriggerCharacters = new[] { "(", "," };
    private static readonly string[] DefaultRetriggerCharacters = new[] { ")" };

    public SignatureHelpService(
        WorkspaceManager workspaceManager,
        DocumentManager documentManager,
        ILogger<SignatureHelpService> logger)
    {
        _workspaceManager = workspaceManager ?? throw new ArgumentNullException(nameof(workspaceManager));
        _documentManager = documentManager ?? throw new ArgumentNullException(nameof(documentManager));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    public static SignatureHelpOptions GetDefaultOptions()
    {
        return new SignatureHelpOptions
        {
            TriggerCharacters = DefaultTriggerCharacters,
            RetriggerCharacters = DefaultRetriggerCharacters
        };
    }

    /// <summary>
    /// Gets signature help for a document at the specified position.
    /// </summary>
    public async Task<Protocol.SignatureHelp?> GetSignatureHelpAsync(
        SignatureHelpParams @params,
        CancellationToken cancellationToken)
    {
        if (@params?.TextDocument == null)
        {
            return null;
        }

        var uri = @params.TextDocument.Uri;
        var position = @params.Position;

        _logger.LogDebug("Signature help requested at {Uri} ({Line}:{Character})",
            uri, position.Line, position.Character);

        var document = _documentManager.GetRoslynDocument(uri);
        if (document == null)
        {
            _logger.LogTrace("No Roslyn document found for: {Uri}", uri);
            return null;
        }

        SourceText? sourceText = null;
        var offset = 0;

        try
        {
            sourceText = await document.GetTextAsync(cancellationToken);
            offset = GetOffset(position, sourceText);

            cancellationToken.ThrowIfCancellationRequested();

            var signatureHelpService = GetSignatureHelpService(document);
            if (signatureHelpService == null)
            {
                _logger.LogWarning("Signature help service not available for document: {Uri}", uri);
                return await GetFallbackSignatureHelpAsync(document, offset, cancellationToken);
            }

            var triggerInfo = CreateTriggerInfo(signatureHelpService, @params.Context);
            if (triggerInfo == null)
            {
                _logger.LogWarning("Signature help trigger info could not be created for: {Uri}", uri);
                return await GetFallbackSignatureHelpAsync(document, offset, cancellationToken);
            }

            var signatureHelp = await InvokeSignatureHelpAsync(
                signatureHelpService,
                document,
                offset,
                triggerInfo,
                cancellationToken);

            var lspHelp = TryTranslateSignatureHelp(signatureHelp, cancellationToken);
            if (lspHelp != null)
            {
                _logger.LogDebug("Returning {Count} signature help items for: {Uri}", lspHelp.Signatures.Length, uri);
                return lspHelp;
            }

            return await GetFallbackSignatureHelpAsync(document, offset, cancellationToken);
        }
        catch (OperationCanceledException)
        {
            _logger.LogTrace("Signature help request cancelled for: {Uri}", uri);
            throw;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error getting signature help for: {Uri}", uri);
            var fallbackText = sourceText ?? await document.GetTextAsync(cancellationToken);
            var fallbackOffset = sourceText != null ? offset : GetOffset(position, fallbackText);
            return await GetFallbackSignatureHelpAsync(document, fallbackOffset, cancellationToken);
        }
    }

    private static async Task<object?> InvokeSignatureHelpAsync(
        object signatureHelpService,
        Document document,
        int offset,
        object triggerInfo,
        CancellationToken cancellationToken)
    {
        try
        {
            var method = signatureHelpService.GetType()
                .GetMethods(BindingFlags.Instance | BindingFlags.Public)
                .FirstOrDefault(m => m.Name == "GetSignatureHelpAsync" && m.GetParameters().Length == 4);

            if (method == null)
            {
                return null;
            }

            var task = method.Invoke(signatureHelpService, new[] { document, offset, triggerInfo, cancellationToken }) as Task;
            if (task == null)
            {
                return null;
            }

            await task.ConfigureAwait(false);

            var resultProperty = task.GetType().GetProperty("Result", BindingFlags.Public | BindingFlags.Instance);
            return resultProperty?.GetValue(task);
        }
        catch
        {
            return null;
        }
    }

    private static Protocol.SignatureHelp? TryTranslateSignatureHelp(object? signatureHelp, CancellationToken cancellationToken)
    {
        if (signatureHelp == null)
        {
            return null;
        }

        var items = GetEnumerable(GetPropertyValue(signatureHelp, "Items")).ToArray();
        if (items.Length == 0)
        {
            return null;
        }

        var activeSignature = ReadOptionalInt(GetPropertyValue(signatureHelp, "SelectedItemIndex")) ?? 0;
        if (activeSignature < 0 || activeSignature >= items.Length)
        {
            activeSignature = 0;
        }

        var activeParameter = ReadOptionalInt(GetPropertyValue(signatureHelp, "SemanticParameterIndex"));
        if (activeParameter < 0)
        {
            activeParameter = null;
        }

        var signatures = items
            .Select(item => ToSignatureInformation(item, activeParameter, cancellationToken))
            .ToArray();

        return new Protocol.SignatureHelp
        {
            Signatures = signatures,
            ActiveSignature = activeSignature,
            ActiveParameter = activeParameter
        };
    }

    private static async Task<Protocol.SignatureHelp?> GetFallbackSignatureHelpAsync(
        Document document,
        int offset,
        CancellationToken cancellationToken)
    {
        var root = await document.GetSyntaxRootAsync(cancellationToken);
        if (root == null)
        {
            return null;
        }

        var adjustedOffset = Math.Max(0, Math.Min(offset - 1, root.FullSpan.End - 1));
        var invocation = root.DescendantNodes()
            .OfType<InvocationExpressionSyntax>()
            .FirstOrDefault(node => node.Span.Contains(adjustedOffset));

        var objectCreation = root.DescendantNodes()
            .OfType<ObjectCreationExpressionSyntax>()
            .FirstOrDefault(node => node.Span.Contains(adjustedOffset));

        if (invocation == null && objectCreation == null)
        {
            return null;
        }
        var argumentList = invocation?.ArgumentList ?? objectCreation?.ArgumentList;

        if (argumentList == null)
        {
            return null;
        }

        var semanticModel = await document.GetSemanticModelAsync(cancellationToken);
        if (semanticModel == null)
        {
            return null;
        }

        var symbolInfo = invocation != null
            ? semanticModel.GetSymbolInfo(invocation, cancellationToken)
            : semanticModel.GetSymbolInfo(objectCreation!, cancellationToken);

        var methods = new List<IMethodSymbol>();
        if (symbolInfo.Symbol is IMethodSymbol method)
        {
            methods.Add(method);
        }

        methods.AddRange(symbolInfo.CandidateSymbols.OfType<IMethodSymbol>());
        methods = methods.Distinct(new MethodSymbolComparer()).ToList();

        if (methods.Count == 0)
        {
            return null;
        }

        var activeParameter = GetActiveParameterIndex(argumentList, offset);

        var signatures = methods
            .Select(m => ToSignatureInformation(m, activeParameter))
            .ToArray();

        return new Protocol.SignatureHelp
        {
            Signatures = signatures,
            ActiveSignature = 0,
            ActiveParameter = activeParameter
        };
    }

    private static int? GetActiveParameterIndex(ArgumentListSyntax argumentList, int offset)
    {
        if (argumentList.Arguments.Count == 0)
        {
            return 0;
        }

        var index = 0;
        foreach (var argument in argumentList.Arguments)
        {
            if (offset <= argument.Span.End)
            {
                break;
            }

            index++;
        }

        if (index >= argumentList.Arguments.Count)
        {
            index = argumentList.Arguments.Count - 1;
        }

        return index;
    }

    private static Protocol.SignatureInformation ToSignatureInformation(IMethodSymbol method, int? activeParameter)
    {
        var parameters = method.Parameters
            .Select(p => new Protocol.ParameterInformation
            {
                Label = BuildParameterLabel(p)
            })
            .ToArray();

        return new Protocol.SignatureInformation
        {
            Label = BuildMethodSignature(method, parameters),
            Documentation = BuildDocumentation(method),
            Parameters = parameters,
            ActiveParameter = activeParameter
        };
    }

    private static string BuildMethodSignature(IMethodSymbol method, Protocol.ParameterInformation[] parameters)
    {
        var name = method.MethodKind == MethodKind.Constructor ? "New" : method.Name;
        var parameterList = string.Join(", ", parameters.Select(p => p.Label));
        var returnType = method.ReturnsVoid
            ? string.Empty
            : $" As {method.ReturnType.ToDisplayString(SymbolDisplayFormat.MinimallyQualifiedFormat)}";

        return $"{name}({parameterList}){returnType}";
    }

    private static string BuildParameterLabel(IParameterSymbol parameter)
    {
        var modifier = parameter.RefKind switch
        {
            RefKind.Ref => "ByRef ",
            RefKind.Out => "ByRef ",
            _ => string.Empty
        };

        var optional = parameter.IsOptional ? "Optional " : string.Empty;
        var type = parameter.Type.ToDisplayString(SymbolDisplayFormat.MinimallyQualifiedFormat);
        return $"{optional}{modifier}{parameter.Name} As {type}";
    }

    private static MarkupContent? BuildDocumentation(ISymbol symbol)
    {
        var xml = symbol.GetDocumentationCommentXml();
        if (string.IsNullOrWhiteSpace(xml))
        {
            return null;
        }

        var summary = ExtractXmlTagContent(xml, "summary");
        if (string.IsNullOrWhiteSpace(summary))
        {
            return null;
        }

        return new MarkupContent
        {
            Kind = MarkupKind.Markdown,
            Value = CleanXmlContent(summary)
        };
    }

    private static string ExtractXmlTagContent(string xml, string tagName)
    {
        var startTag = $"<{tagName}>";
        var endTag = $"</{tagName}>";
        var startIndex = xml.IndexOf(startTag, StringComparison.OrdinalIgnoreCase);
        var endIndex = xml.IndexOf(endTag, StringComparison.OrdinalIgnoreCase);
        if (startIndex < 0 || endIndex <= startIndex)
        {
            return string.Empty;
        }

        return xml.Substring(startIndex + startTag.Length, endIndex - startIndex - startTag.Length);
    }

    private static string CleanXmlContent(string content)
    {
        var noTags = System.Text.RegularExpressions.Regex.Replace(content, @"<[^>]+>", "");
        return System.Text.RegularExpressions.Regex.Replace(noTags, @"\s+", " ").Trim();
    }

    private sealed class MethodSymbolComparer : IEqualityComparer<IMethodSymbol>
    {
        public bool Equals(IMethodSymbol? x, IMethodSymbol? y)
        {
            return SymbolEqualityComparer.Default.Equals(x, y);
        }

        public int GetHashCode(IMethodSymbol obj)
        {
            return SymbolEqualityComparer.Default.GetHashCode(obj);
        }
    }

    private static object? GetSignatureHelpService(Document document)
    {
        var featureAssembly = typeof(Microsoft.CodeAnalysis.Completion.CompletionService).Assembly;
        var signatureHelpServiceType = featureAssembly.GetType(
            "Microsoft.CodeAnalysis.SignatureHelp.SignatureHelpService",
            throwOnError: false);

        if (signatureHelpServiceType == null)
        {
            return null;
        }

        var services = document.Project.Services;
        var getServiceByType = services.GetType().GetMethod("GetService", new[] { typeof(Type) });
        if (getServiceByType != null)
        {
            return getServiceByType.Invoke(services, new object[] { signatureHelpServiceType });
        }

        var genericGetService = services.GetType()
            .GetMethods(BindingFlags.Public | BindingFlags.Instance)
            .FirstOrDefault(m => m.Name == "GetService" && m.IsGenericMethodDefinition && m.GetParameters().Length == 0);

        if (genericGetService == null)
        {
            return null;
        }

        return genericGetService.MakeGenericMethod(signatureHelpServiceType).Invoke(services, Array.Empty<object>());
    }

    private static Protocol.SignatureInformation ToSignatureInformation(
        object item,
        int? activeParameter,
        CancellationToken cancellationToken)
    {
        var parameters = GetEnumerable(GetPropertyValue(item, "Parameters"))
            .Select(param => new Protocol.ParameterInformation
            {
                Label = BuildParameterLabel(param),
                Documentation = BuildDocumentation(GetPropertyValue(param, "DocumentationFactory"), cancellationToken)
            })
            .ToArray();

        return new Protocol.SignatureInformation
        {
            Label = BuildSignatureLabel(item, parameters),
            Documentation = BuildDocumentation(GetPropertyValue(item, "DocumentationFactory"), cancellationToken),
            Parameters = parameters,
            ActiveParameter = activeParameter
        };
    }

    private static string BuildSignatureLabel(object item, Protocol.ParameterInformation[] parameters)
    {
        var prefix = TaggedPartsToString(GetTaggedParts(item, "PrefixDisplayParts"));
        var suffix = TaggedPartsToString(GetTaggedParts(item, "SuffixDisplayParts"));
        var separator = TaggedPartsToString(GetTaggedParts(item, "SeparatorDisplayParts"));
        var parameterLabels = parameters.Select(p => p.Label);

        return $"{prefix}{string.Join(separator, parameterLabels)}{suffix}";
    }

    private static string BuildParameterLabel(object parameter)
    {
        var prefix = TaggedPartsToString(GetTaggedParts(parameter, "PrefixDisplayParts"));
        var display = TaggedPartsToString(GetTaggedParts(parameter, "DisplayParts"));
        var suffix = TaggedPartsToString(GetTaggedParts(parameter, "SuffixDisplayParts"));

        return $"{prefix}{display}{suffix}";
    }

    private static MarkupContent? BuildDocumentation(object? documentationFactory, CancellationToken cancellationToken)
    {
        if (documentationFactory is not Delegate del)
        {
            return null;
        }

        object? result;
        try
        {
            result = del.DynamicInvoke(cancellationToken);
        }
        catch
        {
            return null;
        }

        var parts = result as IEnumerable<TaggedText> ?? (result as IEnumerable)?.OfType<TaggedText>();
        if (parts == null)
        {
            return null;
        }

        var text = TaggedPartsToString(parts);
        if (string.IsNullOrWhiteSpace(text))
        {
            return null;
        }

        return new MarkupContent
        {
            Kind = MarkupKind.Markdown,
            Value = text.Trim()
        };
    }

    private static string TaggedPartsToString(IEnumerable<TaggedText> parts)
    {
        if (parts == null)
        {
            return string.Empty;
        }

        var sb = new StringBuilder();
        foreach (var part in parts)
        {
            sb.Append(part.Text);
        }

        return sb.ToString();
    }

    private static IEnumerable<TaggedText> GetTaggedParts(object? instance, string propertyName)
    {
        if (instance == null)
        {
            return Array.Empty<TaggedText>();
        }

        var value = GetPropertyValue(instance, propertyName);
        if (value is IEnumerable<TaggedText> taggedParts)
        {
            return taggedParts;
        }

        if (value is IEnumerable enumerable)
        {
            return enumerable.OfType<TaggedText>().ToArray();
        }

        return Array.Empty<TaggedText>();
    }

    private static object? GetPropertyValue(object instance, string propertyName)
    {
        return instance.GetType().GetProperty(propertyName, BindingFlags.Public | BindingFlags.Instance)?.GetValue(instance);
    }

    private static IEnumerable<object> GetEnumerable(object? value)
    {
        if (value is IEnumerable enumerable)
        {
            foreach (var item in enumerable)
            {
                if (item != null)
                {
                    yield return item;
                }
            }
        }
    }

    private static int? ReadOptionalInt(object? value)
    {
        if (value == null)
        {
            return null;
        }

        if (value is int i)
        {
            return i;
        }

        return null;
    }

    private static object? CreateTriggerInfo(object signatureHelpService, SignatureHelpContext? context)
    {
        var assembly = signatureHelpService.GetType().Assembly;
        var triggerInfoType = assembly.GetType("Microsoft.CodeAnalysis.SignatureHelp.SignatureHelpTriggerInfo");
        var triggerReasonType = assembly.GetType("Microsoft.CodeAnalysis.SignatureHelp.SignatureHelpTriggerReason");

        if (triggerInfoType == null || triggerReasonType == null)
        {
            return null;
        }

        var reasonName = context?.TriggerKind switch
        {
            SignatureHelpTriggerKind.TriggerCharacter => "TypeCharCommand",
            SignatureHelpTriggerKind.ContentChange => "RetriggerCommand",
            _ => "InvokeSignatureHelpCommand"
        };

        var reasonValue = Enum.Parse(triggerReasonType, reasonName);
        var triggerChar = GetTriggerCharacter(context?.TriggerCharacter);

        foreach (var ctor in triggerInfoType.GetConstructors(BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance))
        {
            var parameters = ctor.GetParameters();
            if (parameters.Length == 2)
            {
                var charParam = parameters[1].ParameterType;
                object? charValue = triggerChar;
                if (charParam == typeof(char) && triggerChar == null)
                {
                    charValue = default(char);
                }

                return ctor.Invoke(new[] { reasonValue, charValue });
            }

            if (parameters.Length == 1)
            {
                return ctor.Invoke(new[] { reasonValue });
            }
        }

        return null;
    }

    private static char? GetTriggerCharacter(string? triggerCharacter)
    {
        if (string.IsNullOrEmpty(triggerCharacter))
        {
            return null;
        }

        return triggerCharacter[0];
    }

    private static int GetOffset(Position position, SourceText text)
    {
        var line = Math.Min(position.Line, text.Lines.Count - 1);
        line = Math.Max(0, line);

        var textLine = text.Lines[line];
        var character = Math.Min(position.Character, textLine.End - textLine.Start);
        character = Math.Max(0, character);

        return textLine.Start + character;
    }
}
