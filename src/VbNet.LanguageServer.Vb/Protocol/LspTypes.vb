' LSP (Language Server Protocol) type definitions
' See: https://microsoft.github.io/language-server-protocol/specifications/lsp/3.17/specification/

Imports System.Text.Json
Imports System.Text.Json.Serialization

Namespace Protocol

#Region "Initialization"

    ''' <summary>
    ''' Parameters for the initialize request.
    ''' </summary>
    Public Class InitializeParams
        <JsonPropertyName("processId")>
        Public Property ProcessId As Integer?

        <JsonPropertyName("clientInfo")>
        Public Property ClientInfo As ClientInfo

        <JsonPropertyName("rootPath")>
        Public Property RootPath As String

        <JsonPropertyName("rootUri")>
        Public Property RootUri As String

        <JsonPropertyName("capabilities")>
        Public Property Capabilities As ClientCapabilities = New ClientCapabilities()

        <JsonPropertyName("trace")>
        Public Property Trace As String

        <JsonPropertyName("workspaceFolders")>
        Public Property WorkspaceFolders As WorkspaceFolder()

        <JsonPropertyName("initializationOptions")>
        Public Property InitializationOptions As JsonElement?
    End Class

    Public Class ClientInfo
        <JsonPropertyName("name")>
        Public Property Name As String = String.Empty

        <JsonPropertyName("version")>
        Public Property Version As String
    End Class

    Public Class ClientCapabilities
        <JsonPropertyName("workspace")>
        Public Property Workspace As WorkspaceClientCapabilities

        <JsonPropertyName("textDocument")>
        Public Property TextDocument As TextDocumentClientCapabilities

        <JsonPropertyName("general")>
        Public Property General As GeneralClientCapabilities
    End Class

    Public Class WorkspaceClientCapabilities
        <JsonPropertyName("workspaceFolders")>
        Public Property WorkspaceFolders As Boolean?

        <JsonPropertyName("configuration")>
        Public Property Configuration As Boolean?

        <JsonPropertyName("didChangeConfiguration")>
        Public Property DidChangeConfiguration As DidChangeConfigurationCapability
    End Class

    Public Class DidChangeConfigurationCapability
        <JsonPropertyName("dynamicRegistration")>
        Public Property DynamicRegistration As Boolean?
    End Class

    Public Class TextDocumentClientCapabilities
        <JsonPropertyName("synchronization")>
        Public Property Synchronization As TextDocumentSyncClientCapabilities

        <JsonPropertyName("completion")>
        Public Property Completion As CompletionClientCapabilities

        <JsonPropertyName("hover")>
        Public Property Hover As HoverClientCapabilities

        <JsonPropertyName("definition")>
        Public Property Definition As DefinitionClientCapabilities

        <JsonPropertyName("references")>
        Public Property References As ReferenceClientCapabilities

        <JsonPropertyName("rename")>
        Public Property Rename As RenameClientCapabilities

        <JsonPropertyName("publishDiagnostics")>
        Public Property PublishDiagnostics As PublishDiagnosticsClientCapabilities
    End Class

    Public Class TextDocumentSyncClientCapabilities
        <JsonPropertyName("dynamicRegistration")>
        Public Property DynamicRegistration As Boolean?

        <JsonPropertyName("willSave")>
        Public Property WillSave As Boolean?

        <JsonPropertyName("willSaveWaitUntil")>
        Public Property WillSaveWaitUntil As Boolean?

        <JsonPropertyName("didSave")>
        Public Property DidSave As Boolean?
    End Class

    Public Class CompletionClientCapabilities
        <JsonPropertyName("dynamicRegistration")>
        Public Property DynamicRegistration As Boolean?

        <JsonPropertyName("completionItem")>
        Public Property CompletionItem As CompletionItemCapabilities
    End Class

    Public Class CompletionItemCapabilities
        <JsonPropertyName("snippetSupport")>
        Public Property SnippetSupport As Boolean?

        <JsonPropertyName("commitCharactersSupport")>
        Public Property CommitCharactersSupport As Boolean?

        <JsonPropertyName("documentationFormat")>
        Public Property DocumentationFormat As String()
    End Class

    Public Class HoverClientCapabilities
        <JsonPropertyName("dynamicRegistration")>
        Public Property DynamicRegistration As Boolean?

        <JsonPropertyName("contentFormat")>
        Public Property ContentFormat As String()
    End Class

    Public Class DefinitionClientCapabilities
        <JsonPropertyName("dynamicRegistration")>
        Public Property DynamicRegistration As Boolean?

        <JsonPropertyName("linkSupport")>
        Public Property LinkSupport As Boolean?
    End Class

    Public Class ReferenceClientCapabilities
        <JsonPropertyName("dynamicRegistration")>
        Public Property DynamicRegistration As Boolean?
    End Class

    Public Class RenameClientCapabilities
        <JsonPropertyName("dynamicRegistration")>
        Public Property DynamicRegistration As Boolean?

        <JsonPropertyName("prepareSupport")>
        Public Property PrepareSupport As Boolean?
    End Class

    Public Class PublishDiagnosticsClientCapabilities
        <JsonPropertyName("relatedInformation")>
        Public Property RelatedInformation As Boolean?

        <JsonPropertyName("versionSupport")>
        Public Property VersionSupport As Boolean?

        <JsonPropertyName("codeDescriptionSupport")>
        Public Property CodeDescriptionSupport As Boolean?
    End Class

    Public Class GeneralClientCapabilities
        <JsonPropertyName("positionEncodings")>
        Public Property PositionEncodings As String()
    End Class

    ''' <summary>
    ''' Result of the initialize request.
    ''' </summary>
    Public Class InitializeResult
        <JsonPropertyName("capabilities")>
        Public Property Capabilities As ServerCapabilities = New ServerCapabilities()

        <JsonPropertyName("serverInfo")>
        Public Property ServerInfo As ServerInfo
    End Class

    Public Class ServerInfo
        <JsonPropertyName("name")>
        Public Property Name As String = String.Empty

        <JsonPropertyName("version")>
        Public Property Version As String
    End Class

    Public Class ServerCapabilities
        <JsonPropertyName("positionEncoding")>
        Public Property PositionEncoding As String

        <JsonPropertyName("textDocumentSync")>
        Public Property TextDocumentSync As TextDocumentSyncOptions

        <JsonPropertyName("completionProvider")>
        Public Property CompletionProvider As CompletionOptions

        <JsonPropertyName("hoverProvider")>
        Public Property HoverProvider As Boolean?

        <JsonPropertyName("definitionProvider")>
        Public Property DefinitionProvider As Boolean?

        <JsonPropertyName("referencesProvider")>
        Public Property ReferencesProvider As Boolean?

        <JsonPropertyName("renameProvider")>
        Public Property RenameProvider As RenameOptions

        <JsonPropertyName("documentSymbolProvider")>
        Public Property DocumentSymbolProvider As Boolean?

        <JsonPropertyName("workspaceSymbolProvider")>
        Public Property WorkspaceSymbolProvider As Boolean?

        <JsonPropertyName("signatureHelpProvider")>
        Public Property SignatureHelpProvider As SignatureHelpOptions

        <JsonPropertyName("semanticTokensProvider")>
        Public Property SemanticTokensProvider As SemanticTokensOptions

        <JsonPropertyName("codeActionProvider")>
        Public Property CodeActionProvider As CodeActionOptions

        <JsonPropertyName("documentHighlightProvider")>
        Public Property DocumentHighlightProvider As Boolean?

        <JsonPropertyName("selectionRangeProvider")>
        Public Property SelectionRangeProvider As Boolean?

        <JsonPropertyName("foldingRangeProvider")>
        Public Property FoldingRangeProvider As Boolean?

        <JsonPropertyName("documentFormattingProvider")>
        Public Property DocumentFormattingProvider As Boolean?

        <JsonPropertyName("documentRangeFormattingProvider")>
        Public Property DocumentRangeFormattingProvider As Boolean?

        <JsonPropertyName("typeDefinitionProvider")>
        Public Property TypeDefinitionProvider As Boolean?

        <JsonPropertyName("implementationProvider")>
        Public Property ImplementationProvider As Boolean?

        <JsonPropertyName("documentLinkProvider")>
        Public Property DocumentLinkProvider As DocumentLinkOptions

        <JsonPropertyName("callHierarchyProvider")>
        Public Property CallHierarchyProvider As Boolean?

        <JsonPropertyName("typeHierarchyProvider")>
        Public Property TypeHierarchyProvider As Boolean?

        <JsonPropertyName("diagnosticProvider")>
        Public Property DiagnosticProvider As DiagnosticOptions
    End Class

    Public Class TextDocumentSyncOptions
        <JsonPropertyName("openClose")>
        Public Property OpenClose As Boolean?

        <JsonPropertyName("change")>
        Public Property [Change] As TextDocumentSyncKind?

        <JsonPropertyName("save")>
        Public Property Save As SaveOptions
    End Class

    Public Enum TextDocumentSyncKind
        None = 0
        Full = 1
        Incremental = 2
    End Enum

#Region "Document Highlight"

    Public Class DocumentHighlightParams
        Inherits TextDocumentPositionParams
    End Class

    Public Class DocumentHighlight
        <JsonPropertyName("range")>
        Public Property Range As Range = New Range()

        <JsonPropertyName("kind")>
        Public Property Kind As DocumentHighlightKind?
    End Class

    Public Enum DocumentHighlightKind
        Text = 1
        Read = 2
        Write = 3
    End Enum

#End Region

#Region "Selection Range"

    Public Class SelectionRangeParams
        <JsonPropertyName("textDocument")>
        Public Property TextDocument As TextDocumentIdentifier = New TextDocumentIdentifier()

        <JsonPropertyName("positions")>
        Public Property Positions As Position() = Array.Empty(Of Position)()
    End Class

    Public Class SelectionRange
        <JsonPropertyName("range")>
        Public Property Range As Range = New Range()

        <JsonPropertyName("parent")>
        Public Property Parent As SelectionRange
    End Class

#End Region

#Region "Type Definition"

    Public Class TypeDefinitionParams
        Inherits TextDocumentPositionParams
    End Class

#End Region

#Region "Implementation"

    Public Class ImplementationParams
        Inherits TextDocumentPositionParams
    End Class

#End Region

#Region "Document Link"

    Public Class DocumentLinkParams
        <JsonPropertyName("textDocument")>
        Public Property TextDocument As TextDocumentIdentifier = New TextDocumentIdentifier()
    End Class

    Public Class DocumentLink
        <JsonPropertyName("range")>
        Public Property Range As Range = New Range()

        <JsonPropertyName("target")>
        Public Property Target As String

        <JsonPropertyName("tooltip")>
        Public Property Tooltip As String

        <JsonPropertyName("data")>
        Public Property Data As Object
    End Class

    Public Class DocumentLinkOptions
        <JsonPropertyName("resolveProvider")>
        Public Property ResolveProvider As Boolean?
    End Class

#End Region

    Public Class SaveOptions
        <JsonPropertyName("includeText")>
        Public Property IncludeText As Boolean?
    End Class

    Public Class CompletionOptions
        <JsonPropertyName("triggerCharacters")>
        Public Property TriggerCharacters As String()

        <JsonPropertyName("resolveProvider")>
        Public Property ResolveProvider As Boolean?
    End Class

    Public Class RenameOptions
        <JsonPropertyName("prepareProvider")>
        Public Property PrepareProvider As Boolean?
    End Class

#End Region

#Region "Call Hierarchy"

    Public Class CallHierarchyPrepareParams
        Inherits TextDocumentPositionParams
    End Class

    Public Class CallHierarchyIncomingCallsParams
        <JsonPropertyName("item")>
        Public Property Item As CallHierarchyItem = New CallHierarchyItem()
    End Class

    Public Class CallHierarchyOutgoingCallsParams
        <JsonPropertyName("item")>
        Public Property Item As CallHierarchyItem = New CallHierarchyItem()
    End Class

    Public Class CallHierarchyItem
        <JsonPropertyName("name")>
        Public Property Name As String = String.Empty

        <JsonPropertyName("kind")>
        Public Property Kind As SymbolKind

        <JsonPropertyName("tags")>
        Public Property Tags As SymbolTag()

        <JsonPropertyName("detail")>
        Public Property Detail As String

        <JsonPropertyName("uri")>
        Public Property Uri As String = String.Empty

        <JsonPropertyName("range")>
        Public Property Range As Range = New Range()

        <JsonPropertyName("selectionRange")>
        Public Property SelectionRange As Range = New Range()

        <JsonPropertyName("data")>
        Public Property Data As Object
    End Class

    Public Enum SymbolTag
        Deprecated = 1
    End Enum

    Public Class CallHierarchyIncomingCall
        <JsonPropertyName("from")>
        Public Property [From] As CallHierarchyItem = New CallHierarchyItem()

        <JsonPropertyName("fromRanges")>
        Public Property FromRanges As Range() = Array.Empty(Of Range)()
    End Class

    Public Class CallHierarchyOutgoingCall
        <JsonPropertyName("to")>
        Public Property [To] As CallHierarchyItem = New CallHierarchyItem()

        <JsonPropertyName("fromRanges")>
        Public Property FromRanges As Range() = Array.Empty(Of Range)()
    End Class

#End Region

#Region "Type Hierarchy"

    Public Class TypeHierarchyPrepareParams
        Inherits TextDocumentPositionParams
    End Class

    Public Class TypeHierarchySupertypesParams
        <JsonPropertyName("item")>
        Public Property Item As TypeHierarchyItem = New TypeHierarchyItem()
    End Class

    Public Class TypeHierarchySubtypesParams
        <JsonPropertyName("item")>
        Public Property Item As TypeHierarchyItem = New TypeHierarchyItem()
    End Class

    Public Class TypeHierarchyItem
        <JsonPropertyName("name")>
        Public Property Name As String = String.Empty

        <JsonPropertyName("kind")>
        Public Property Kind As SymbolKind

        <JsonPropertyName("tags")>
        Public Property Tags As SymbolTag()

        <JsonPropertyName("detail")>
        Public Property Detail As String

        <JsonPropertyName("uri")>
        Public Property Uri As String = String.Empty

        <JsonPropertyName("range")>
        Public Property Range As Range = New Range()

        <JsonPropertyName("selectionRange")>
        Public Property SelectionRange As Range = New Range()

        <JsonPropertyName("data")>
        Public Property Data As Object
    End Class

#End Region

#Region "Semantic Tokens"

    Public Class SemanticTokensParams
        <JsonPropertyName("textDocument")>
        Public Property TextDocument As TextDocumentIdentifier = New TextDocumentIdentifier()
    End Class

    Public Class SemanticTokensRangeParams
        Inherits SemanticTokensParams

        <JsonPropertyName("range")>
        Public Property Range As Range = New Range()
    End Class

    Public Class SemanticTokens
        <JsonPropertyName("data")>
        Public Property Data As UInteger() = Array.Empty(Of UInteger)()

        <JsonPropertyName("resultId")>
        Public Property ResultId As String
    End Class

    Public Class SemanticTokensLegend
        <JsonPropertyName("tokenTypes")>
        Public Property TokenTypes As String() = Array.Empty(Of String)()

        <JsonPropertyName("tokenModifiers")>
        Public Property TokenModifiers As String() = Array.Empty(Of String)()
    End Class

    Public Class SemanticTokensOptions
        <JsonPropertyName("legend")>
        Public Property Legend As SemanticTokensLegend = New SemanticTokensLegend()

        <JsonPropertyName("range")>
        Public Property Range As Boolean?

        <JsonPropertyName("full")>
        Public Property Full As Boolean?
    End Class

#End Region

#Region "Diagnostics Pull"

    Public Class DiagnosticOptions
        <JsonPropertyName("identifier")>
        Public Property Identifier As String

        <JsonPropertyName("interFileDependencies")>
        Public Property InterFileDependencies As Boolean?

        <JsonPropertyName("workspaceDiagnostics")>
        Public Property WorkspaceDiagnostics As Boolean?
    End Class

    Public Class TextDocumentDiagnosticParams
        <JsonPropertyName("textDocument")>
        Public Property TextDocument As TextDocumentIdentifier = New TextDocumentIdentifier()

        <JsonPropertyName("identifier")>
        Public Property Identifier As String

        <JsonPropertyName("previousResultId")>
        Public Property PreviousResultId As String
    End Class

    Public Class WorkspaceDiagnosticParams
        <JsonPropertyName("identifier")>
        Public Property Identifier As String

        <JsonPropertyName("previousResultIds")>
        Public Property PreviousResultIds As PreviousResultId() = Array.Empty(Of PreviousResultId)()
    End Class

    Public Class PreviousResultId
        <JsonPropertyName("uri")>
        Public Property Uri As String = String.Empty

        <JsonPropertyName("value")>
        Public Property Value As String
    End Class

    Public Class DocumentDiagnosticReport
        <JsonPropertyName("kind")>
        Public Property Kind As String = "full"

        <JsonPropertyName("resultId")>
        Public Property ResultId As String

        <JsonPropertyName("items")>
        Public Property Items As Diagnostic() = Array.Empty(Of Diagnostic)()
    End Class

    Public Class WorkspaceDiagnosticReport
        <JsonPropertyName("items")>
        Public Property Items As WorkspaceDocumentDiagnosticReport() = Array.Empty(Of WorkspaceDocumentDiagnosticReport)()
    End Class

    Public Class WorkspaceDocumentDiagnosticReport
        <JsonPropertyName("uri")>
        Public Property Uri As String = String.Empty

        <JsonPropertyName("version")>
        Public Property Version As Integer?

        <JsonPropertyName("kind")>
        Public Property Kind As String = "full"

        <JsonPropertyName("resultId")>
        Public Property ResultId As String

        <JsonPropertyName("items")>
        Public Property Items As Diagnostic() = Array.Empty(Of Diagnostic)()
    End Class

#End Region

#Region "Workspace"

    Public Class WorkspaceFolder
        <JsonPropertyName("uri")>
        Public Property Uri As String = String.Empty

        <JsonPropertyName("name")>
        Public Property Name As String = String.Empty
    End Class

    Public Class DidChangeConfigurationParams
        <JsonPropertyName("settings")>
        Public Property Settings As Object
    End Class

    Public Class DidChangeWatchedFilesParams
        <JsonPropertyName("changes")>
        Public Property Changes As FileEvent() = Array.Empty(Of FileEvent)()
    End Class

    Public Class FileEvent
        <JsonPropertyName("uri")>
        Public Property Uri As String = String.Empty

        <JsonPropertyName("type")>
        Public Property Type As FileChangeType
    End Class

    Public Enum FileChangeType
        Created = 1
        Changed = 2
        Deleted = 3
    End Enum

#End Region

#Region "Text Document"

    Public Class TextDocumentIdentifier
        <JsonPropertyName("uri")>
        Public Property Uri As String = String.Empty
    End Class

    Public Class VersionedTextDocumentIdentifier
        Inherits TextDocumentIdentifier

        <JsonPropertyName("version")>
        Public Property Version As Integer
    End Class

    Public Class TextDocumentItem
        <JsonPropertyName("uri")>
        Public Property Uri As String = String.Empty

        <JsonPropertyName("languageId")>
        Public Property LanguageId As String = String.Empty

        <JsonPropertyName("version")>
        Public Property Version As Integer

        <JsonPropertyName("text")>
        Public Property Text As String = String.Empty
    End Class

    Public Class DidOpenTextDocumentParams
        <JsonPropertyName("textDocument")>
        Public Property TextDocument As TextDocumentItem = New TextDocumentItem()
    End Class

    Public Class DidCloseTextDocumentParams
        <JsonPropertyName("textDocument")>
        Public Property TextDocument As TextDocumentIdentifier = New TextDocumentIdentifier()
    End Class

    Public Class DidChangeTextDocumentParams
        <JsonPropertyName("textDocument")>
        Public Property TextDocument As VersionedTextDocumentIdentifier = New VersionedTextDocumentIdentifier()

        <JsonPropertyName("contentChanges")>
        Public Property ContentChanges As TextDocumentContentChangeEvent() = Array.Empty(Of TextDocumentContentChangeEvent)()
    End Class

    Public Class TextDocumentContentChangeEvent
        <JsonPropertyName("range")>
        Public Property Range As Range

        <JsonPropertyName("rangeLength")>
        Public Property RangeLength As Integer?

        <JsonPropertyName("text")>
        Public Property Text As String = String.Empty
    End Class

    Public Class DidSaveTextDocumentParams
        <JsonPropertyName("textDocument")>
        Public Property TextDocument As TextDocumentIdentifier = New TextDocumentIdentifier()

        <JsonPropertyName("text")>
        Public Property Text As String
    End Class

#End Region

#Region "Position and Range"

    Public Class Position
        <JsonPropertyName("line")>
        Public Property Line As Integer

        <JsonPropertyName("character")>
        Public Property Character As Integer

        Public Sub New()
        End Sub

        Public Sub New(line As Integer, character As Integer)
            Me.Line = line
            Me.Character = character
        End Sub
    End Class

    Public Class Range
        <JsonPropertyName("start")>
        Public Property Start As Position = New Position()

        <JsonPropertyName("end")>
        Public Property [End] As Position = New Position()

        Public Sub New()
        End Sub

        Public Sub New(startPos As Position, endPos As Position)
            Start = startPos
            [End] = endPos
        End Sub
    End Class

    Public Class Location
        <JsonPropertyName("uri")>
        Public Property Uri As String = String.Empty

        <JsonPropertyName("range")>
        Public Property Range As Range = New Range()
    End Class

#End Region

#Region "Diagnostics"

    Public Class PublishDiagnosticsParams
        <JsonPropertyName("uri")>
        Public Property Uri As String = String.Empty

        <JsonPropertyName("version")>
        Public Property Version As Integer?

        <JsonPropertyName("diagnostics")>
        Public Property Diagnostics As Diagnostic() = Array.Empty(Of Diagnostic)()
    End Class

    Public Class Diagnostic
        <JsonPropertyName("range")>
        Public Property Range As Range = New Range()

        <JsonPropertyName("severity")>
        Public Property Severity As DiagnosticSeverity?

        <JsonPropertyName("code")>
        Public Property Code As String

        <JsonPropertyName("codeDescription")>
        Public Property CodeDescription As CodeDescription

        <JsonPropertyName("source")>
        Public Property Source As String

        <JsonPropertyName("message")>
        Public Property Message As String = String.Empty

        <JsonPropertyName("relatedInformation")>
        Public Property RelatedInformation As DiagnosticRelatedInformation()
    End Class

    Public Enum DiagnosticSeverity
        [Error] = 1
        Warning = 2
        Information = 3
        Hint = 4
    End Enum

    Public Class CodeDescription
        <JsonPropertyName("href")>
        Public Property Href As String = String.Empty
    End Class

    Public Class DiagnosticRelatedInformation
        <JsonPropertyName("location")>
        Public Property Location As Location = New Location()

        <JsonPropertyName("message")>
        Public Property Message As String = String.Empty
    End Class

#End Region

#Region "Completion"

    Public Class CompletionParams
        Inherits TextDocumentPositionParams

        <JsonPropertyName("context")>
        Public Property Context As CompletionContext
    End Class

    Public Class TextDocumentPositionParams
        <JsonPropertyName("textDocument")>
        Public Property TextDocument As TextDocumentIdentifier = New TextDocumentIdentifier()

        <JsonPropertyName("position")>
        Public Property Position As Position = New Position()
    End Class

    Public Class CompletionContext
        <JsonPropertyName("triggerKind")>
        Public Property TriggerKind As CompletionTriggerKind

        <JsonPropertyName("triggerCharacter")>
        Public Property TriggerCharacter As String
    End Class

    Public Enum CompletionTriggerKind
        Invoked = 1
        TriggerCharacter = 2
        TriggerForIncompleteCompletions = 3
    End Enum

    Public Class CompletionList
        <JsonPropertyName("isIncomplete")>
        Public Property IsIncomplete As Boolean

        <JsonPropertyName("items")>
        Public Property Items As CompletionItem() = Array.Empty(Of CompletionItem)()
    End Class

    Public Class CompletionItem
        <JsonPropertyName("label")>
        Public Property Label As String = String.Empty

        <JsonPropertyName("kind")>
        Public Property Kind As CompletionItemKind?

        <JsonPropertyName("detail")>
        Public Property Detail As String

        <JsonPropertyName("documentation")>
        Public Property Documentation As MarkupContent

        <JsonPropertyName("insertText")>
        Public Property InsertText As String

        <JsonPropertyName("textEdit")>
        Public Property TextEdit As TextEdit

        <JsonPropertyName("additionalTextEdits")>
        Public Property AdditionalTextEdits As TextEdit()

        <JsonPropertyName("insertTextFormat")>
        Public Property InsertTextFormat As InsertTextFormat?

        <JsonPropertyName("sortText")>
        Public Property SortText As String

        <JsonPropertyName("filterText")>
        Public Property FilterText As String

        <JsonPropertyName("commitCharacters")>
        Public Property CommitCharacters As String()

        <JsonPropertyName("data")>
        Public Property Data As Object
    End Class

    Public Enum CompletionItemKind
        Text = 1
        Method = 2
        [Function] = 3
        Constructor = 4
        Field = 5
        Variable = 6
        [Class] = 7
        [Interface] = 8
        [Module] = 9
        [Property] = 10
        Unit = 11
        Value = 12
        [Enum] = 13
        Keyword = 14
        Snippet = 15
        Color = 16
        File = 17
        Reference = 18
        Folder = 19
        EnumMember = 20
        [Constant] = 21
        [Struct] = 22
        [Event] = 23
        [Operator] = 24
        TypeParameter = 25
    End Enum

    Public Enum InsertTextFormat
        PlainText = 1
        Snippet = 2
    End Enum

#End Region

#Region "Hover"

    Public Class HoverParams
        Inherits TextDocumentPositionParams
    End Class

    Public Class Hover
        <JsonPropertyName("contents")>
        Public Property Contents As MarkupContent = New MarkupContent()

        <JsonPropertyName("range")>
        Public Property Range As Range
    End Class

    Public Class MarkupContent
        <JsonPropertyName("kind")>
        Public Property Kind As String = MarkupKind.PlainText

        <JsonPropertyName("value")>
        Public Property Value As String = String.Empty
    End Class

    Public NotInheritable Class MarkupKind
        Private Sub New()
        End Sub

        Public Const PlainText As String = "plaintext"
        Public Const Markdown As String = "markdown"
    End Class

#End Region

#Region "Definition and References"

    Public Class DefinitionParams
        Inherits TextDocumentPositionParams
    End Class

    Public Class ReferenceParams
        Inherits TextDocumentPositionParams

        <JsonPropertyName("context")>
        Public Property Context As ReferenceContext = New ReferenceContext()
    End Class

    Public Class ReferenceContext
        <JsonPropertyName("includeDeclaration")>
        Public Property IncludeDeclaration As Boolean
    End Class

#End Region

#Region "Rename"

    Public Class RenameParams
        Inherits TextDocumentPositionParams

        <JsonPropertyName("newName")>
        Public Property NewName As String = String.Empty
    End Class

    Public Class PrepareRenameParams
        Inherits TextDocumentPositionParams
    End Class

    Public Class WorkspaceEdit
        <JsonPropertyName("changes")>
        Public Property Changes As Dictionary(Of String, TextEdit())

        <JsonPropertyName("documentChanges")>
        Public Property DocumentChanges As TextDocumentEdit()
    End Class

    Public Class TextEdit
        <JsonPropertyName("range")>
        Public Property Range As Range = New Range()

        <JsonPropertyName("newText")>
        Public Property NewText As String = String.Empty
    End Class

    Public Class TextDocumentEdit
        <JsonPropertyName("textDocument")>
        Public Property TextDocument As OptionalVersionedTextDocumentIdentifier = New OptionalVersionedTextDocumentIdentifier()

        <JsonPropertyName("edits")>
        Public Property Edits As TextEdit() = Array.Empty(Of TextEdit)()
    End Class

    Public Class OptionalVersionedTextDocumentIdentifier
        Inherits TextDocumentIdentifier

        <JsonPropertyName("version")>
        Public Property Version As Integer?
    End Class

#End Region

#Region "Code Actions"

    Public Class CodeActionParams
        <JsonPropertyName("textDocument")>
        Public Property TextDocument As TextDocumentIdentifier = New TextDocumentIdentifier()

        <JsonPropertyName("range")>
        Public Property Range As Range = New Range()

        <JsonPropertyName("context")>
        Public Property Context As CodeActionContext = New CodeActionContext()
    End Class

    Public Class CodeActionContext
        <JsonPropertyName("diagnostics")>
        Public Property Diagnostics As Diagnostic() = Array.Empty(Of Diagnostic)()

        <JsonPropertyName("only")>
        Public Property Only As String()

        <JsonPropertyName("triggerKind")>
        Public Property TriggerKind As CodeActionTriggerKind?
    End Class

    Public Enum CodeActionTriggerKind
        Invoked = 1
        Automatic = 2
    End Enum

    Public Class CodeAction
        <JsonPropertyName("title")>
        Public Property Title As String = String.Empty

        <JsonPropertyName("kind")>
        Public Property Kind As String

        <JsonPropertyName("diagnostics")>
        Public Property Diagnostics As Diagnostic()

        <JsonPropertyName("edit")>
        Public Property Edit As WorkspaceEdit

        <JsonPropertyName("command")>
        Public Property Command As Command

        <JsonPropertyName("isPreferred")>
        Public Property IsPreferred As Boolean?

        <JsonPropertyName("data")>
        Public Property Data As Object
    End Class

    Public Class CodeActionOptions
        <JsonPropertyName("codeActionKinds")>
        Public Property CodeActionKinds As String()

        <JsonPropertyName("resolveProvider")>
        Public Property ResolveProvider As Boolean?
    End Class

    Public NotInheritable Class CodeActionKind
        Private Sub New()
        End Sub

        Public Const Empty As String = ""
        Public Const QuickFix As String = "quickfix"
        Public Const Refactor As String = "refactor"
        Public Const Source As String = "source"
    End Class

    Public Class Command
        <JsonPropertyName("title")>
        Public Property Title As String = String.Empty

        <JsonPropertyName("command")>
        Public Property CommandIdentifier As String = String.Empty

        <JsonPropertyName("arguments")>
        Public Property Arguments As Object()
    End Class

#End Region

#Region "Document Symbols"

    Public Class DocumentSymbolParams
        <JsonPropertyName("textDocument")>
        Public Property TextDocument As TextDocumentIdentifier = New TextDocumentIdentifier()
    End Class

    Public Class DocumentSymbol
        <JsonPropertyName("name")>
        Public Property Name As String = String.Empty

        <JsonPropertyName("detail")>
        Public Property Detail As String

        <JsonPropertyName("kind")>
        Public Property Kind As SymbolKind

        <JsonPropertyName("range")>
        Public Property Range As Range = New Range()

        <JsonPropertyName("selectionRange")>
        Public Property SelectionRange As Range = New Range()

        <JsonPropertyName("children")>
        Public Property Children As DocumentSymbol()
    End Class

    Public Class SymbolInformation
        <JsonPropertyName("name")>
        Public Property Name As String = String.Empty

        <JsonPropertyName("kind")>
        Public Property Kind As SymbolKind

        <JsonPropertyName("location")>
        Public Property Location As Location = New Location()

        <JsonPropertyName("containerName")>
        Public Property ContainerName As String
    End Class

    Public Enum SymbolKind
        File = 1
        [Module] = 2
        [Namespace] = 3
        Package = 4
        [Class] = 5
        Method = 6
        [Property] = 7
        Field = 8
        Constructor = 9
        [Enum] = 10
        [Interface] = 11
        [Function] = 12
        Variable = 13
        [Constant] = 14
        [String] = 15
        Number = 16
        [Boolean] = 17
        [Array] = 18
        [Object] = 19
        Key = 20
        Null = 21
        EnumMember = 22
        [Struct] = 23
        [Event] = 24
        [Operator] = 25
        TypeParameter = 26
    End Enum

#End Region

#Region "Workspace Symbols"

    Public Class WorkspaceSymbolParams
        <JsonPropertyName("query")>
        Public Property Query As String = String.Empty
    End Class

#End Region

#Region "Shutdown and Exit"

    ' Shutdown request has no parameters and returns null
    ' Exit notification has no parameters

#End Region

#Region "Cancel Request"

    Public Class CancelParams
        <JsonPropertyName("id")>
        Public Property Id As JsonRpcId
    End Class

#End Region

#Region "Folding Ranges"

    Public Class FoldingRangeParams
        <JsonPropertyName("textDocument")>
        Public Property TextDocument As TextDocumentIdentifier = New TextDocumentIdentifier()
    End Class

    Public Class FoldingRange
        <JsonPropertyName("startLine")>
        Public Property StartLine As Integer

        <JsonPropertyName("startCharacter")>
        Public Property StartCharacter As Integer?

        <JsonPropertyName("endLine")>
        Public Property EndLine As Integer

        <JsonPropertyName("endCharacter")>
        Public Property EndCharacter As Integer?

        <JsonPropertyName("kind")>
        Public Property Kind As String

        <JsonPropertyName("collapsedText")>
        Public Property CollapsedText As String
    End Class

    Public NotInheritable Class FoldingRangeKind
        Private Sub New()
        End Sub

        Public Const Comment As String = "comment"
        Public Const [Imports] As String = "imports"
        Public Const Region As String = "region"
    End Class

#End Region

#Region "Formatting"

    Public Class DocumentFormattingParams
        <JsonPropertyName("textDocument")>
        Public Property TextDocument As TextDocumentIdentifier = New TextDocumentIdentifier()

        <JsonPropertyName("options")>
        Public Property Options As FormattingOptions = New FormattingOptions()
    End Class

    Public Class DocumentRangeFormattingParams
        <JsonPropertyName("textDocument")>
        Public Property TextDocument As TextDocumentIdentifier = New TextDocumentIdentifier()

        <JsonPropertyName("range")>
        Public Property Range As Range = New Range()

        <JsonPropertyName("options")>
        Public Property Options As FormattingOptions = New FormattingOptions()
    End Class

    Public Class FormattingOptions
        <JsonPropertyName("tabSize")>
        Public Property TabSize As Integer = 4

        <JsonPropertyName("insertSpaces")>
        Public Property InsertSpaces As Boolean = True

        <JsonPropertyName("trimTrailingWhitespace")>
        Public Property TrimTrailingWhitespace As Boolean?

        <JsonPropertyName("insertFinalNewline")>
        Public Property InsertFinalNewline As Boolean?

        <JsonPropertyName("trimFinalNewlines")>
        Public Property TrimFinalNewlines As Boolean?
    End Class

#End Region

#Region "Signature Help"

    Public Class SignatureHelpParams
        Inherits TextDocumentPositionParams

        <JsonPropertyName("context")>
        Public Property Context As SignatureHelpContext
    End Class

    Public Class SignatureHelpContext
        <JsonPropertyName("triggerKind")>
        Public Property TriggerKind As SignatureHelpTriggerKind

        <JsonPropertyName("triggerCharacter")>
        Public Property TriggerCharacter As String

        <JsonPropertyName("isRetrigger")>
        Public Property IsRetrigger As Boolean

        <JsonPropertyName("activeSignatureHelp")>
        Public Property ActiveSignatureHelp As SignatureHelp
    End Class

    Public Enum SignatureHelpTriggerKind
        Invoked = 1
        TriggerCharacter = 2
        ContentChange = 3
    End Enum

    Public Class SignatureHelp
        <JsonPropertyName("signatures")>
        Public Property Signatures As SignatureInformation() = Array.Empty(Of SignatureInformation)()

        <JsonPropertyName("activeSignature")>
        Public Property ActiveSignature As Integer?

        <JsonPropertyName("activeParameter")>
        Public Property ActiveParameter As Integer?
    End Class

    Public Class SignatureInformation
        <JsonPropertyName("label")>
        Public Property Label As String = String.Empty

        <JsonPropertyName("documentation")>
        Public Property Documentation As MarkupContent

        <JsonPropertyName("parameters")>
        Public Property Parameters As ParameterInformation()

        <JsonPropertyName("activeParameter")>
        Public Property ActiveParameter As Integer?
    End Class

    Public Class ParameterInformation
        <JsonPropertyName("label")>
        Public Property Label As String = String.Empty

        <JsonPropertyName("documentation")>
        Public Property Documentation As MarkupContent
    End Class

    Public Class SignatureHelpOptions
        <JsonPropertyName("triggerCharacters")>
        Public Property TriggerCharacters As String()

        <JsonPropertyName("retriggerCharacters")>
        Public Property RetriggerCharacters As String()
    End Class

#End Region

End Namespace
