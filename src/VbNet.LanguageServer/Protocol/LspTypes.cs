// LSP (Language Server Protocol) type definitions
// See: https://microsoft.github.io/language-server-protocol/specifications/lsp/3.17/specification/

using System.Text.Json.Serialization;

namespace VbNet.LanguageServer.Protocol;

#region Initialization

/// <summary>
/// Parameters for the initialize request.
/// </summary>
public class InitializeParams
{
    [JsonPropertyName("processId")]
    public int? ProcessId { get; set; }

    [JsonPropertyName("clientInfo")]
    public ClientInfo? ClientInfo { get; set; }

    [JsonPropertyName("rootPath")]
    public string? RootPath { get; set; }

    [JsonPropertyName("rootUri")]
    public string? RootUri { get; set; }

    [JsonPropertyName("capabilities")]
    public ClientCapabilities Capabilities { get; set; } = new();

    [JsonPropertyName("trace")]
    public string? Trace { get; set; }

    [JsonPropertyName("workspaceFolders")]
    public WorkspaceFolder[]? WorkspaceFolders { get; set; }
}

public class ClientInfo
{
    [JsonPropertyName("name")]
    public string Name { get; set; } = string.Empty;

    [JsonPropertyName("version")]
    public string? Version { get; set; }
}

public class ClientCapabilities
{
    [JsonPropertyName("workspace")]
    public WorkspaceClientCapabilities? Workspace { get; set; }

    [JsonPropertyName("textDocument")]
    public TextDocumentClientCapabilities? TextDocument { get; set; }

    [JsonPropertyName("general")]
    public GeneralClientCapabilities? General { get; set; }
}

public class WorkspaceClientCapabilities
{
    [JsonPropertyName("workspaceFolders")]
    public bool? WorkspaceFolders { get; set; }

    [JsonPropertyName("configuration")]
    public bool? Configuration { get; set; }

    [JsonPropertyName("didChangeConfiguration")]
    public DidChangeConfigurationCapability? DidChangeConfiguration { get; set; }
}

public class DidChangeConfigurationCapability
{
    [JsonPropertyName("dynamicRegistration")]
    public bool? DynamicRegistration { get; set; }
}

public class TextDocumentClientCapabilities
{
    [JsonPropertyName("synchronization")]
    public TextDocumentSyncClientCapabilities? Synchronization { get; set; }

    [JsonPropertyName("completion")]
    public CompletionClientCapabilities? Completion { get; set; }

    [JsonPropertyName("hover")]
    public HoverClientCapabilities? Hover { get; set; }

    [JsonPropertyName("definition")]
    public DefinitionClientCapabilities? Definition { get; set; }

    [JsonPropertyName("references")]
    public ReferenceClientCapabilities? References { get; set; }

    [JsonPropertyName("rename")]
    public RenameClientCapabilities? Rename { get; set; }

    [JsonPropertyName("publishDiagnostics")]
    public PublishDiagnosticsClientCapabilities? PublishDiagnostics { get; set; }
}

public class TextDocumentSyncClientCapabilities
{
    [JsonPropertyName("dynamicRegistration")]
    public bool? DynamicRegistration { get; set; }

    [JsonPropertyName("willSave")]
    public bool? WillSave { get; set; }

    [JsonPropertyName("willSaveWaitUntil")]
    public bool? WillSaveWaitUntil { get; set; }

    [JsonPropertyName("didSave")]
    public bool? DidSave { get; set; }
}

public class CompletionClientCapabilities
{
    [JsonPropertyName("dynamicRegistration")]
    public bool? DynamicRegistration { get; set; }

    [JsonPropertyName("completionItem")]
    public CompletionItemCapabilities? CompletionItem { get; set; }
}

public class CompletionItemCapabilities
{
    [JsonPropertyName("snippetSupport")]
    public bool? SnippetSupport { get; set; }

    [JsonPropertyName("commitCharactersSupport")]
    public bool? CommitCharactersSupport { get; set; }

    [JsonPropertyName("documentationFormat")]
    public string[]? DocumentationFormat { get; set; }
}

public class HoverClientCapabilities
{
    [JsonPropertyName("dynamicRegistration")]
    public bool? DynamicRegistration { get; set; }

    [JsonPropertyName("contentFormat")]
    public string[]? ContentFormat { get; set; }
}

public class DefinitionClientCapabilities
{
    [JsonPropertyName("dynamicRegistration")]
    public bool? DynamicRegistration { get; set; }

    [JsonPropertyName("linkSupport")]
    public bool? LinkSupport { get; set; }
}

public class ReferenceClientCapabilities
{
    [JsonPropertyName("dynamicRegistration")]
    public bool? DynamicRegistration { get; set; }
}

public class RenameClientCapabilities
{
    [JsonPropertyName("dynamicRegistration")]
    public bool? DynamicRegistration { get; set; }

    [JsonPropertyName("prepareSupport")]
    public bool? PrepareSupport { get; set; }
}

public class PublishDiagnosticsClientCapabilities
{
    [JsonPropertyName("relatedInformation")]
    public bool? RelatedInformation { get; set; }

    [JsonPropertyName("versionSupport")]
    public bool? VersionSupport { get; set; }

    [JsonPropertyName("codeDescriptionSupport")]
    public bool? CodeDescriptionSupport { get; set; }
}

public class GeneralClientCapabilities
{
    [JsonPropertyName("positionEncodings")]
    public string[]? PositionEncodings { get; set; }
}

/// <summary>
/// Result of the initialize request.
/// </summary>
public class InitializeResult
{
    [JsonPropertyName("capabilities")]
    public ServerCapabilities Capabilities { get; set; } = new();

    [JsonPropertyName("serverInfo")]
    public ServerInfo? ServerInfo { get; set; }
}

public class ServerInfo
{
    [JsonPropertyName("name")]
    public string Name { get; set; } = string.Empty;

    [JsonPropertyName("version")]
    public string? Version { get; set; }
}

public class ServerCapabilities
{
    [JsonPropertyName("positionEncoding")]
    public string? PositionEncoding { get; set; }

    [JsonPropertyName("textDocumentSync")]
    public TextDocumentSyncOptions? TextDocumentSync { get; set; }

    [JsonPropertyName("completionProvider")]
    public CompletionOptions? CompletionProvider { get; set; }

    [JsonPropertyName("hoverProvider")]
    public bool? HoverProvider { get; set; }

    [JsonPropertyName("definitionProvider")]
    public bool? DefinitionProvider { get; set; }

    [JsonPropertyName("referencesProvider")]
    public bool? ReferencesProvider { get; set; }

    [JsonPropertyName("renameProvider")]
    public RenameOptions? RenameProvider { get; set; }

    [JsonPropertyName("documentSymbolProvider")]
    public bool? DocumentSymbolProvider { get; set; }

    [JsonPropertyName("workspaceSymbolProvider")]
    public bool? WorkspaceSymbolProvider { get; set; }
}

public class TextDocumentSyncOptions
{
    [JsonPropertyName("openClose")]
    public bool? OpenClose { get; set; }

    [JsonPropertyName("change")]
    public TextDocumentSyncKind? Change { get; set; }

    [JsonPropertyName("save")]
    public SaveOptions? Save { get; set; }
}

public enum TextDocumentSyncKind
{
    None = 0,
    Full = 1,
    Incremental = 2
}

public class SaveOptions
{
    [JsonPropertyName("includeText")]
    public bool? IncludeText { get; set; }
}

public class CompletionOptions
{
    [JsonPropertyName("triggerCharacters")]
    public string[]? TriggerCharacters { get; set; }

    [JsonPropertyName("resolveProvider")]
    public bool? ResolveProvider { get; set; }
}

public class RenameOptions
{
    [JsonPropertyName("prepareProvider")]
    public bool? PrepareProvider { get; set; }
}

#endregion

#region Workspace

public class WorkspaceFolder
{
    [JsonPropertyName("uri")]
    public string Uri { get; set; } = string.Empty;

    [JsonPropertyName("name")]
    public string Name { get; set; } = string.Empty;
}

public class DidChangeConfigurationParams
{
    [JsonPropertyName("settings")]
    public object? Settings { get; set; }
}

public class DidChangeWatchedFilesParams
{
    [JsonPropertyName("changes")]
    public FileEvent[] Changes { get; set; } = [];
}

public class FileEvent
{
    [JsonPropertyName("uri")]
    public string Uri { get; set; } = string.Empty;

    [JsonPropertyName("type")]
    public FileChangeType Type { get; set; }
}

public enum FileChangeType
{
    Created = 1,
    Changed = 2,
    Deleted = 3
}

#endregion

#region Text Document

public class TextDocumentIdentifier
{
    [JsonPropertyName("uri")]
    public string Uri { get; set; } = string.Empty;
}

public class VersionedTextDocumentIdentifier : TextDocumentIdentifier
{
    [JsonPropertyName("version")]
    public int Version { get; set; }
}

public class TextDocumentItem
{
    [JsonPropertyName("uri")]
    public string Uri { get; set; } = string.Empty;

    [JsonPropertyName("languageId")]
    public string LanguageId { get; set; } = string.Empty;

    [JsonPropertyName("version")]
    public int Version { get; set; }

    [JsonPropertyName("text")]
    public string Text { get; set; } = string.Empty;
}

public class DidOpenTextDocumentParams
{
    [JsonPropertyName("textDocument")]
    public TextDocumentItem TextDocument { get; set; } = new();
}

public class DidCloseTextDocumentParams
{
    [JsonPropertyName("textDocument")]
    public TextDocumentIdentifier TextDocument { get; set; } = new();
}

public class DidChangeTextDocumentParams
{
    [JsonPropertyName("textDocument")]
    public VersionedTextDocumentIdentifier TextDocument { get; set; } = new();

    [JsonPropertyName("contentChanges")]
    public TextDocumentContentChangeEvent[] ContentChanges { get; set; } = [];
}

public class TextDocumentContentChangeEvent
{
    [JsonPropertyName("range")]
    public Range? Range { get; set; }

    [JsonPropertyName("rangeLength")]
    public int? RangeLength { get; set; }

    [JsonPropertyName("text")]
    public string Text { get; set; } = string.Empty;
}

public class DidSaveTextDocumentParams
{
    [JsonPropertyName("textDocument")]
    public TextDocumentIdentifier TextDocument { get; set; } = new();

    [JsonPropertyName("text")]
    public string? Text { get; set; }
}

#endregion

#region Position and Range

public class Position
{
    [JsonPropertyName("line")]
    public int Line { get; set; }

    [JsonPropertyName("character")]
    public int Character { get; set; }

    public Position() { }

    public Position(int line, int character)
    {
        Line = line;
        Character = character;
    }
}

public class Range
{
    [JsonPropertyName("start")]
    public Position Start { get; set; } = new();

    [JsonPropertyName("end")]
    public Position End { get; set; } = new();

    public Range() { }

    public Range(Position start, Position end)
    {
        Start = start;
        End = end;
    }
}

public class Location
{
    [JsonPropertyName("uri")]
    public string Uri { get; set; } = string.Empty;

    [JsonPropertyName("range")]
    public Range Range { get; set; } = new();
}

#endregion

#region Diagnostics

public class PublishDiagnosticsParams
{
    [JsonPropertyName("uri")]
    public string Uri { get; set; } = string.Empty;

    [JsonPropertyName("version")]
    public int? Version { get; set; }

    [JsonPropertyName("diagnostics")]
    public Diagnostic[] Diagnostics { get; set; } = [];
}

public class Diagnostic
{
    [JsonPropertyName("range")]
    public Range Range { get; set; } = new();

    [JsonPropertyName("severity")]
    public DiagnosticSeverity? Severity { get; set; }

    [JsonPropertyName("code")]
    public string? Code { get; set; }

    [JsonPropertyName("codeDescription")]
    public CodeDescription? CodeDescription { get; set; }

    [JsonPropertyName("source")]
    public string? Source { get; set; }

    [JsonPropertyName("message")]
    public string Message { get; set; } = string.Empty;

    [JsonPropertyName("relatedInformation")]
    public DiagnosticRelatedInformation[]? RelatedInformation { get; set; }
}

public enum DiagnosticSeverity
{
    Error = 1,
    Warning = 2,
    Information = 3,
    Hint = 4
}

public class CodeDescription
{
    [JsonPropertyName("href")]
    public string Href { get; set; } = string.Empty;
}

public class DiagnosticRelatedInformation
{
    [JsonPropertyName("location")]
    public Location Location { get; set; } = new();

    [JsonPropertyName("message")]
    public string Message { get; set; } = string.Empty;
}

#endregion

#region Completion

public class CompletionParams : TextDocumentPositionParams
{
    [JsonPropertyName("context")]
    public CompletionContext? Context { get; set; }
}

public class TextDocumentPositionParams
{
    [JsonPropertyName("textDocument")]
    public TextDocumentIdentifier TextDocument { get; set; } = new();

    [JsonPropertyName("position")]
    public Position Position { get; set; } = new();
}

public class CompletionContext
{
    [JsonPropertyName("triggerKind")]
    public CompletionTriggerKind TriggerKind { get; set; }

    [JsonPropertyName("triggerCharacter")]
    public string? TriggerCharacter { get; set; }
}

public enum CompletionTriggerKind
{
    Invoked = 1,
    TriggerCharacter = 2,
    TriggerForIncompleteCompletions = 3
}

public class CompletionList
{
    [JsonPropertyName("isIncomplete")]
    public bool IsIncomplete { get; set; }

    [JsonPropertyName("items")]
    public CompletionItem[] Items { get; set; } = [];
}

public class CompletionItem
{
    [JsonPropertyName("label")]
    public string Label { get; set; } = string.Empty;

    [JsonPropertyName("kind")]
    public CompletionItemKind? Kind { get; set; }

    [JsonPropertyName("detail")]
    public string? Detail { get; set; }

    [JsonPropertyName("documentation")]
    public MarkupContent? Documentation { get; set; }

    [JsonPropertyName("insertText")]
    public string? InsertText { get; set; }

    [JsonPropertyName("textEdit")]
    public TextEdit? TextEdit { get; set; }

    [JsonPropertyName("additionalTextEdits")]
    public TextEdit[]? AdditionalTextEdits { get; set; }

    [JsonPropertyName("insertTextFormat")]
    public InsertTextFormat? InsertTextFormat { get; set; }

    [JsonPropertyName("sortText")]
    public string? SortText { get; set; }

    [JsonPropertyName("filterText")]
    public string? FilterText { get; set; }

    [JsonPropertyName("commitCharacters")]
    public string[]? CommitCharacters { get; set; }

    [JsonPropertyName("data")]
    public object? Data { get; set; }
}

public enum CompletionItemKind
{
    Text = 1,
    Method = 2,
    Function = 3,
    Constructor = 4,
    Field = 5,
    Variable = 6,
    Class = 7,
    Interface = 8,
    Module = 9,
    Property = 10,
    Unit = 11,
    Value = 12,
    Enum = 13,
    Keyword = 14,
    Snippet = 15,
    Color = 16,
    File = 17,
    Reference = 18,
    Folder = 19,
    EnumMember = 20,
    Constant = 21,
    Struct = 22,
    Event = 23,
    Operator = 24,
    TypeParameter = 25
}

public enum InsertTextFormat
{
    PlainText = 1,
    Snippet = 2
}

#endregion

#region Hover

public class HoverParams : TextDocumentPositionParams { }

public class Hover
{
    [JsonPropertyName("contents")]
    public MarkupContent Contents { get; set; } = new();

    [JsonPropertyName("range")]
    public Range? Range { get; set; }
}

public class MarkupContent
{
    [JsonPropertyName("kind")]
    public string Kind { get; set; } = MarkupKind.PlainText;

    [JsonPropertyName("value")]
    public string Value { get; set; } = string.Empty;
}

public static class MarkupKind
{
    public const string PlainText = "plaintext";
    public const string Markdown = "markdown";
}

#endregion

#region Definition and References

public class DefinitionParams : TextDocumentPositionParams { }

public class ReferenceParams : TextDocumentPositionParams
{
    [JsonPropertyName("context")]
    public ReferenceContext Context { get; set; } = new();
}

public class ReferenceContext
{
    [JsonPropertyName("includeDeclaration")]
    public bool IncludeDeclaration { get; set; }
}

#endregion

#region Rename

public class RenameParams : TextDocumentPositionParams
{
    [JsonPropertyName("newName")]
    public string NewName { get; set; } = string.Empty;
}

public class PrepareRenameParams : TextDocumentPositionParams { }

public class WorkspaceEdit
{
    [JsonPropertyName("changes")]
    public Dictionary<string, TextEdit[]>? Changes { get; set; }

    [JsonPropertyName("documentChanges")]
    public TextDocumentEdit[]? DocumentChanges { get; set; }
}

public class TextEdit
{
    [JsonPropertyName("range")]
    public Range Range { get; set; } = new();

    [JsonPropertyName("newText")]
    public string NewText { get; set; } = string.Empty;
}

public class TextDocumentEdit
{
    [JsonPropertyName("textDocument")]
    public OptionalVersionedTextDocumentIdentifier TextDocument { get; set; } = new();

    [JsonPropertyName("edits")]
    public TextEdit[] Edits { get; set; } = [];
}

public class OptionalVersionedTextDocumentIdentifier : TextDocumentIdentifier
{
    [JsonPropertyName("version")]
    public int? Version { get; set; }
}

#endregion

#region Document Symbols

public class DocumentSymbolParams
{
    [JsonPropertyName("textDocument")]
    public TextDocumentIdentifier TextDocument { get; set; } = new();
}

public class DocumentSymbol
{
    [JsonPropertyName("name")]
    public string Name { get; set; } = string.Empty;

    [JsonPropertyName("detail")]
    public string? Detail { get; set; }

    [JsonPropertyName("kind")]
    public SymbolKind Kind { get; set; }

    [JsonPropertyName("range")]
    public Range Range { get; set; } = new();

    [JsonPropertyName("selectionRange")]
    public Range SelectionRange { get; set; } = new();

    [JsonPropertyName("children")]
    public DocumentSymbol[]? Children { get; set; }
}

public class SymbolInformation
{
    [JsonPropertyName("name")]
    public string Name { get; set; } = string.Empty;

    [JsonPropertyName("kind")]
    public SymbolKind Kind { get; set; }

    [JsonPropertyName("location")]
    public Location Location { get; set; } = new();

    [JsonPropertyName("containerName")]
    public string? ContainerName { get; set; }
}

public enum SymbolKind
{
    File = 1,
    Module = 2,
    Namespace = 3,
    Package = 4,
    Class = 5,
    Method = 6,
    Property = 7,
    Field = 8,
    Constructor = 9,
    Enum = 10,
    Interface = 11,
    Function = 12,
    Variable = 13,
    Constant = 14,
    String = 15,
    Number = 16,
    Boolean = 17,
    Array = 18,
    Object = 19,
    Key = 20,
    Null = 21,
    EnumMember = 22,
    Struct = 23,
    Event = 24,
    Operator = 25,
    TypeParameter = 26
}

#endregion

#region Workspace Symbols

public class WorkspaceSymbolParams
{
    [JsonPropertyName("query")]
    public string Query { get; set; } = string.Empty;
}

#endregion

#region Shutdown and Exit

// Shutdown request has no parameters and returns null
// Exit notification has no parameters

#endregion

#region Cancel Request

public class CancelParams
{
    [JsonPropertyName("id")]
    public JsonRpcId Id { get; set; }
}

#endregion

#region Folding Ranges

public class FoldingRangeParams
{
    [JsonPropertyName("textDocument")]
    public TextDocumentIdentifier TextDocument { get; set; } = new();
}

public class FoldingRange
{
    [JsonPropertyName("startLine")]
    public int StartLine { get; set; }

    [JsonPropertyName("startCharacter")]
    public int? StartCharacter { get; set; }

    [JsonPropertyName("endLine")]
    public int EndLine { get; set; }

    [JsonPropertyName("endCharacter")]
    public int? EndCharacter { get; set; }

    [JsonPropertyName("kind")]
    public string? Kind { get; set; }

    [JsonPropertyName("collapsedText")]
    public string? CollapsedText { get; set; }
}

public static class FoldingRangeKind
{
    public const string Comment = "comment";
    public const string Imports = "imports";
    public const string Region = "region";
}

#endregion

#region Formatting

public class DocumentFormattingParams
{
    [JsonPropertyName("textDocument")]
    public TextDocumentIdentifier TextDocument { get; set; } = new();

    [JsonPropertyName("options")]
    public FormattingOptions Options { get; set; } = new();
}

public class DocumentRangeFormattingParams
{
    [JsonPropertyName("textDocument")]
    public TextDocumentIdentifier TextDocument { get; set; } = new();

    [JsonPropertyName("range")]
    public Range Range { get; set; } = new();

    [JsonPropertyName("options")]
    public FormattingOptions Options { get; set; } = new();
}

public class FormattingOptions
{
    [JsonPropertyName("tabSize")]
    public int TabSize { get; set; } = 4;

    [JsonPropertyName("insertSpaces")]
    public bool InsertSpaces { get; set; } = true;

    [JsonPropertyName("trimTrailingWhitespace")]
    public bool? TrimTrailingWhitespace { get; set; }

    [JsonPropertyName("insertFinalNewline")]
    public bool? InsertFinalNewline { get; set; }

    [JsonPropertyName("trimFinalNewlines")]
    public bool? TrimFinalNewlines { get; set; }
}

#endregion

#region Signature Help

public class SignatureHelpParams : TextDocumentPositionParams
{
    [JsonPropertyName("context")]
    public SignatureHelpContext? Context { get; set; }
}

public class SignatureHelpContext
{
    [JsonPropertyName("triggerKind")]
    public SignatureHelpTriggerKind TriggerKind { get; set; }

    [JsonPropertyName("triggerCharacter")]
    public string? TriggerCharacter { get; set; }

    [JsonPropertyName("isRetrigger")]
    public bool IsRetrigger { get; set; }

    [JsonPropertyName("activeSignatureHelp")]
    public SignatureHelp? ActiveSignatureHelp { get; set; }
}

public enum SignatureHelpTriggerKind
{
    Invoked = 1,
    TriggerCharacter = 2,
    ContentChange = 3
}

public class SignatureHelp
{
    [JsonPropertyName("signatures")]
    public SignatureInformation[] Signatures { get; set; } = [];

    [JsonPropertyName("activeSignature")]
    public int? ActiveSignature { get; set; }

    [JsonPropertyName("activeParameter")]
    public int? ActiveParameter { get; set; }
}

public class SignatureInformation
{
    [JsonPropertyName("label")]
    public string Label { get; set; } = string.Empty;

    [JsonPropertyName("documentation")]
    public MarkupContent? Documentation { get; set; }

    [JsonPropertyName("parameters")]
    public ParameterInformation[]? Parameters { get; set; }

    [JsonPropertyName("activeParameter")]
    public int? ActiveParameter { get; set; }
}

public class ParameterInformation
{
    [JsonPropertyName("label")]
    public string Label { get; set; } = string.Empty;

    [JsonPropertyName("documentation")]
    public MarkupContent? Documentation { get; set; }
}

public class SignatureHelpOptions
{
    [JsonPropertyName("triggerCharacters")]
    public string[]? TriggerCharacters { get; set; }

    [JsonPropertyName("retriggerCharacters")]
    public string[]? RetriggerCharacters { get; set; }
}

#endregion
