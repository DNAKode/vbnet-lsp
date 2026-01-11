using System.Text.Json;
using VbNet.LanguageServer.Protocol;
using Xunit;
using Range = VbNet.LanguageServer.Protocol.Range;

namespace VbNet.LanguageServer.Tests.Protocol;

public class LspTypesTests
{
    [Fact]
    public void InitializeParams_DeserializesFromVSCode()
    {
        // Sample initialize params similar to what VS Code sends
        var json = """
        {
            "processId": 12345,
            "clientInfo": {
                "name": "Visual Studio Code",
                "version": "1.85.0"
            },
            "rootUri": "file:///c:/projects/myproject",
            "capabilities": {
                "textDocument": {
                    "synchronization": {
                        "dynamicRegistration": true,
                        "didSave": true
                    },
                    "completion": {
                        "dynamicRegistration": true,
                        "completionItem": {
                            "snippetSupport": true
                        }
                    }
                }
            }
        }
        """;

        var options = JsonSerializerOptionsProvider.Options;
        var @params = JsonSerializer.Deserialize<InitializeParams>(json, options);

        Assert.NotNull(@params);
        Assert.Equal(12345, @params.ProcessId);
        Assert.Equal("Visual Studio Code", @params.ClientInfo?.Name);
        Assert.Equal("1.85.0", @params.ClientInfo?.Version);
        Assert.Equal("file:///c:/projects/myproject", @params.RootUri);
        Assert.True(@params.Capabilities.TextDocument?.Synchronization?.DynamicRegistration);
        Assert.True(@params.Capabilities.TextDocument?.Completion?.CompletionItem?.SnippetSupport);
    }

    [Fact]
    public void InitializeResult_SerializesCorrectly()
    {
        var result = new InitializeResult
        {
            Capabilities = new ServerCapabilities
            {
                PositionEncoding = "utf-16",
                TextDocumentSync = new TextDocumentSyncOptions
                {
                    OpenClose = true,
                    Change = TextDocumentSyncKind.Incremental
                },
                CompletionProvider = new CompletionOptions
                {
                    TriggerCharacters = new[] { "." },
                    ResolveProvider = true
                },
                HoverProvider = true,
                DefinitionProvider = true
            },
            ServerInfo = new ServerInfo
            {
                Name = "VbNet.LanguageServer",
                Version = "0.1.0"
            }
        };

        var json = JsonSerializer.Serialize(result, JsonSerializerOptionsProvider.Options);
        var doc = JsonDocument.Parse(json);

        var capabilities = doc.RootElement.GetProperty("capabilities");
        Assert.Equal("utf-16", capabilities.GetProperty("positionEncoding").GetString());
        Assert.True(capabilities.GetProperty("textDocumentSync").GetProperty("openClose").GetBoolean());
        Assert.Equal(2, capabilities.GetProperty("textDocumentSync").GetProperty("change").GetInt32()); // Incremental = 2
        Assert.True(capabilities.GetProperty("hoverProvider").GetBoolean());

        var serverInfo = doc.RootElement.GetProperty("serverInfo");
        Assert.Equal("VbNet.LanguageServer", serverInfo.GetProperty("name").GetString());
    }

    [Fact]
    public void Position_SerializesCorrectly()
    {
        var position = new Position(10, 5);
        var json = JsonSerializer.Serialize(position, JsonSerializerOptionsProvider.Options);
        var doc = JsonDocument.Parse(json);

        Assert.Equal(10, doc.RootElement.GetProperty("line").GetInt32());
        Assert.Equal(5, doc.RootElement.GetProperty("character").GetInt32());
    }

    [Fact]
    public void Range_SerializesCorrectly()
    {
        var range = new Range(new Position(1, 0), new Position(1, 10));
        var json = JsonSerializer.Serialize(range, JsonSerializerOptionsProvider.Options);
        var doc = JsonDocument.Parse(json);

        var start = doc.RootElement.GetProperty("start");
        var end = doc.RootElement.GetProperty("end");

        Assert.Equal(1, start.GetProperty("line").GetInt32());
        Assert.Equal(0, start.GetProperty("character").GetInt32());
        Assert.Equal(1, end.GetProperty("line").GetInt32());
        Assert.Equal(10, end.GetProperty("character").GetInt32());
    }

    [Fact]
    public void Diagnostic_SerializesCorrectly()
    {
        var diagnostic = new Diagnostic
        {
            Range = new Range(new Position(5, 0), new Position(5, 20)),
            Severity = DiagnosticSeverity.Error,
            Code = "BC30451",
            Source = "vbnet",
            Message = "'foo' is not declared"
        };

        var json = JsonSerializer.Serialize(diagnostic, JsonSerializerOptionsProvider.Options);
        var doc = JsonDocument.Parse(json);

        Assert.Equal(1, doc.RootElement.GetProperty("severity").GetInt32()); // Error = 1
        Assert.Equal("BC30451", doc.RootElement.GetProperty("code").GetString());
        Assert.Equal("vbnet", doc.RootElement.GetProperty("source").GetString());
        Assert.Equal("'foo' is not declared", doc.RootElement.GetProperty("message").GetString());
    }

    [Fact]
    public void DidChangeTextDocumentParams_DeserializesCorrectly()
    {
        var json = """
        {
            "textDocument": {
                "uri": "file:///c:/test.vb",
                "version": 3
            },
            "contentChanges": [
                {
                    "range": {
                        "start": { "line": 5, "character": 0 },
                        "end": { "line": 5, "character": 10 }
                    },
                    "text": "Dim x As Integer"
                }
            ]
        }
        """;

        var options = JsonSerializerOptionsProvider.Options;
        var @params = JsonSerializer.Deserialize<DidChangeTextDocumentParams>(json, options);

        Assert.NotNull(@params);
        Assert.Equal("file:///c:/test.vb", @params.TextDocument.Uri);
        Assert.Equal(3, @params.TextDocument.Version);
        Assert.Single(@params.ContentChanges);
        Assert.Equal("Dim x As Integer", @params.ContentChanges[0].Text);
        Assert.Equal(5, @params.ContentChanges[0].Range?.Start.Line);
    }

    [Fact]
    public void CompletionItem_SerializesCorrectly()
    {
        var item = new CompletionItem
        {
            Label = "Console",
            Kind = CompletionItemKind.Class,
            Detail = "System.Console",
            InsertText = "Console"
        };

        var json = JsonSerializer.Serialize(item, JsonSerializerOptionsProvider.Options);
        var doc = JsonDocument.Parse(json);

        Assert.Equal("Console", doc.RootElement.GetProperty("label").GetString());
        Assert.Equal(7, doc.RootElement.GetProperty("kind").GetInt32()); // Class = 7
        Assert.Equal("System.Console", doc.RootElement.GetProperty("detail").GetString());
    }

    [Fact]
    public void DidChangeWatchedFilesParams_DeserializesCorrectly()
    {
        var json = """
        {
            "changes": [
                {
                    "uri": "file:///c:/test/Module1.vb",
                    "type": 2
                }
            ]
        }
        """;

        var options = JsonSerializerOptionsProvider.Options;
        var @params = JsonSerializer.Deserialize<DidChangeWatchedFilesParams>(json, options);

        Assert.NotNull(@params);
        Assert.Single(@params.Changes);
        Assert.Equal("file:///c:/test/Module1.vb", @params.Changes[0].Uri);
        Assert.Equal(FileChangeType.Changed, @params.Changes[0].Type);
    }
}
