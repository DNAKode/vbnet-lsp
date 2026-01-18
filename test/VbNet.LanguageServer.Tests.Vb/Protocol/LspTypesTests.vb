Imports System.Text.Json
Imports VbNet.LanguageServer.Protocol
Imports Xunit

Namespace VbNet.LanguageServer.Tests.Protocol

    Public Class LspTypesTests
        <Fact>
        Public Sub InitializeParams_DeserializesFromVSCode()
            Dim json = "{" & vbCrLf &
                "    ""processId"": 12345," & vbCrLf &
                "    ""clientInfo"": {" & vbCrLf &
                "        ""name"": ""Visual Studio Code""," & vbCrLf &
                "        ""version"": ""1.85.0""" & vbCrLf &
                "    }," & vbCrLf &
                "    ""rootUri"": ""file:///c:/projects/myproject""," & vbCrLf &
                "    ""capabilities"": {" & vbCrLf &
                "        ""textDocument"": {" & vbCrLf &
                "            ""synchronization"": {" & vbCrLf &
                "                ""dynamicRegistration"": true," & vbCrLf &
                "                ""didSave"": true" & vbCrLf &
                "            }," & vbCrLf &
                "            ""completion"": {" & vbCrLf &
                "                ""dynamicRegistration"": true," & vbCrLf &
                "                ""completionItem"": {" & vbCrLf &
                "                    ""snippetSupport"": true" & vbCrLf &
                "                }" & vbCrLf &
                "            }" & vbCrLf &
                "        }" & vbCrLf &
                "    }" & vbCrLf &
                "}"

            Dim options = JsonSerializerOptionsProvider.Options
            Dim parameters = JsonSerializer.Deserialize(Of InitializeParams)(json, options)

            Assert.NotNull(parameters)
            Assert.Equal(12345, parameters.ProcessId)
            Assert.Equal("Visual Studio Code", parameters.ClientInfo?.Name)
            Assert.Equal("1.85.0", parameters.ClientInfo?.Version)
            Assert.Equal("file:///c:/projects/myproject", parameters.RootUri)
            Assert.True(parameters.Capabilities.TextDocument?.Synchronization?.DynamicRegistration)
            Assert.True(parameters.Capabilities.TextDocument?.Completion?.CompletionItem?.SnippetSupport)
        End Sub

        <Fact>
        Public Sub InitializeResult_SerializesCorrectly()
            Dim result = New InitializeResult With {
                .Capabilities = New ServerCapabilities With {
                    .PositionEncoding = "utf-16",
                    .TextDocumentSync = New TextDocumentSyncOptions With {
                        .OpenClose = True,
                        .Change = TextDocumentSyncKind.Incremental
                    },
                    .CompletionProvider = New CompletionOptions With {
                        .TriggerCharacters = New String() {"."},
                        .ResolveProvider = True
                    },
                    .HoverProvider = True,
                    .DefinitionProvider = True
                },
                .ServerInfo = New ServerInfo With {
                    .Name = "VbNet.LanguageServer",
                    .Version = "0.1.0"
                }
            }

            Dim json = JsonSerializer.Serialize(result, JsonSerializerOptionsProvider.Options)
            Dim doc = JsonDocument.Parse(json)

            Dim capabilities = doc.RootElement.GetProperty("capabilities")
            Assert.Equal("utf-16", capabilities.GetProperty("positionEncoding").GetString())
            Assert.True(capabilities.GetProperty("textDocumentSync").GetProperty("openClose").GetBoolean())
            Assert.Equal(2, capabilities.GetProperty("textDocumentSync").GetProperty("change").GetInt32())
            Assert.True(capabilities.GetProperty("hoverProvider").GetBoolean())

            Dim serverInfo = doc.RootElement.GetProperty("serverInfo")
            Assert.Equal("VbNet.LanguageServer", serverInfo.GetProperty("name").GetString())
        End Sub

        <Fact>
        Public Sub Position_SerializesCorrectly()
            Dim position = New Position(10, 5)
            Dim json = JsonSerializer.Serialize(position, JsonSerializerOptionsProvider.Options)
            Dim doc = JsonDocument.Parse(json)

            Assert.Equal(10, doc.RootElement.GetProperty("line").GetInt32())
            Assert.Equal(5, doc.RootElement.GetProperty("character").GetInt32())
        End Sub

        <Fact>
        Public Sub Range_SerializesCorrectly()
            Dim rangeValue = New Global.VbNet.LanguageServer.Protocol.Range(New Position(1, 0), New Position(1, 10))
            Dim json = JsonSerializer.Serialize(rangeValue, JsonSerializerOptionsProvider.Options)
            Dim doc = JsonDocument.Parse(json)

            Dim start = doc.RootElement.GetProperty("start")
            Dim [end] = doc.RootElement.GetProperty("end")

            Assert.Equal(1, start.GetProperty("line").GetInt32())
            Assert.Equal(0, start.GetProperty("character").GetInt32())
            Assert.Equal(1, [end].GetProperty("line").GetInt32())
            Assert.Equal(10, [end].GetProperty("character").GetInt32())
        End Sub

        <Fact>
        Public Sub Diagnostic_SerializesCorrectly()
            Dim diagnostic = New Diagnostic With {
                .Range = New Global.VbNet.LanguageServer.Protocol.Range(New Position(5, 0), New Position(5, 20)),
                .Severity = DiagnosticSeverity.[Error],
                .Code = "BC30451",
                .Source = "vbnet",
                .Message = "'foo' is not declared"
            }

            Dim json = JsonSerializer.Serialize(diagnostic, JsonSerializerOptionsProvider.Options)
            Dim doc = JsonDocument.Parse(json)

            Assert.Equal(1, doc.RootElement.GetProperty("severity").GetInt32())
            Assert.Equal("BC30451", doc.RootElement.GetProperty("code").GetString())
            Assert.Equal("vbnet", doc.RootElement.GetProperty("source").GetString())
            Assert.Equal("'foo' is not declared", doc.RootElement.GetProperty("message").GetString())
        End Sub

        <Fact>
        Public Sub DidChangeTextDocumentParams_DeserializesCorrectly()
            Dim json = "{" & vbCrLf &
                "    ""textDocument"": {" & vbCrLf &
                "        ""uri"": ""file:///c:/test.vb""," & vbCrLf &
                "        ""version"": 3" & vbCrLf &
                "    }," & vbCrLf &
                "    ""contentChanges"": [" & vbCrLf &
                "        {" & vbCrLf &
                "            ""range"": {" & vbCrLf &
                "                ""start"": { ""line"": 5, ""character"": 0 }," & vbCrLf &
                "                ""end"": { ""line"": 5, ""character"": 10 }" & vbCrLf &
                "            }," & vbCrLf &
                "            ""text"": ""Dim x As Integer""" & vbCrLf &
                "        }" & vbCrLf &
                "    ]" & vbCrLf &
                "}"

            Dim options = JsonSerializerOptionsProvider.Options
            Dim parameters = JsonSerializer.Deserialize(Of DidChangeTextDocumentParams)(json, options)

            Assert.NotNull(parameters)
            Assert.Equal("file:///c:/test.vb", parameters.TextDocument.Uri)
            Assert.Equal(3, parameters.TextDocument.Version)
            Assert.Single(parameters.ContentChanges)
            Assert.Equal("Dim x As Integer", parameters.ContentChanges(0).Text)
            Assert.Equal(5, parameters.ContentChanges(0).Range?.Start.Line)
        End Sub

        <Fact>
        Public Sub CompletionItem_SerializesCorrectly()
            Dim item = New CompletionItem With {
                .Label = "Console",
                .Kind = CompletionItemKind.Class,
                .Detail = "System.Console",
                .InsertText = "Console"
            }

            Dim json = JsonSerializer.Serialize(item, JsonSerializerOptionsProvider.Options)
            Dim doc = JsonDocument.Parse(json)

            Assert.Equal("Console", doc.RootElement.GetProperty("label").GetString())
            Assert.Equal(7, doc.RootElement.GetProperty("kind").GetInt32())
            Assert.Equal("System.Console", doc.RootElement.GetProperty("detail").GetString())
        End Sub

        <Fact>
        Public Sub DidChangeWatchedFilesParams_DeserializesCorrectly()
            Dim json = "{" & vbCrLf &
                "    ""changes"": [" & vbCrLf &
                "        {" & vbCrLf &
                "            ""uri"": ""file:///c:/test/Module1.vb""," & vbCrLf &
                "            ""type"": 2" & vbCrLf &
                "        }" & vbCrLf &
                "    ]" & vbCrLf &
                "}"

            Dim options = JsonSerializerOptionsProvider.Options
            Dim parameters = JsonSerializer.Deserialize(Of DidChangeWatchedFilesParams)(json, options)

            Assert.NotNull(parameters)
            Assert.Single(parameters.Changes)
            Assert.Equal("file:///c:/test/Module1.vb", parameters.Changes(0).Uri)
            Assert.Equal(FileChangeType.Changed, parameters.Changes(0).Type)
        End Sub
    End Class

End Namespace
