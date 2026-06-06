using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;

var logPath = Environment.GetEnvironmentVariable("VBNET_ZED_TEST_LOG");
StreamWriter? log = null;

var input = Console.OpenStandardInput();
var output = Console.OpenStandardOutput();

while (TryReadMessage(input, out var message))
{
    var node = JsonNode.Parse(message);
    if (node is null)
    {
        continue;
    }

    log ??= OpenLog(logPath, node);
    Log(log ?? TextWriter.Null, node);

    var method = node["method"]?.GetValue<string>();
    var id = node["id"];
    if (id is null)
    {
        continue;
    }

    JsonNode? result = method switch
    {
        "initialize" => JsonNode.Parse("""
        {
          "capabilities": {
            "textDocumentSync": 1,
            "hoverProvider": true,
            "completionProvider": { "triggerCharacters": ["."] },
            "definitionProvider": true,
            "referencesProvider": true,
            "renameProvider": true,
            "documentFormattingProvider": true,
            "documentSymbolProvider": true,
            "foldingRangeProvider": true,
            "semanticTokensProvider": {
              "legend": { "tokenTypes": ["keyword", "class", "method"], "tokenModifiers": [] },
              "full": true
            }
          },
          "serverInfo": { "name": "vbnet-zed-lsp-probe", "version": "0.1.0" }
        }
        """),
        "shutdown" => new JsonObject(),
        "textDocument/hover" => JsonNode.Parse("""{ "contents": { "kind": "markdown", "value": "VB.NET probe hover" } }"""),
        "textDocument/completion" => JsonNode.Parse("""{ "isIncomplete": false, "items": [{ "label": "ProbeCompletion", "kind": 3 }] }"""),
        "textDocument/definition" => JsonNode.Parse("null"),
        "textDocument/references" => new JsonArray(),
        "textDocument/rename" => JsonNode.Parse("""{ "changes": {} }"""),
        "textDocument/formatting" => new JsonArray(),
        "textDocument/documentSymbol" => new JsonArray(),
        "textDocument/foldingRange" => new JsonArray(),
        "textDocument/semanticTokens/full" => JsonNode.Parse("""{ "data": [] }"""),
        _ => new JsonObject()
    };

    Send(output, new JsonObject
    {
        ["jsonrpc"] = "2.0",
        ["id"] = id.DeepClone(),
        ["result"] = result
    });

    if (method == "shutdown")
    {
        break;
    }
}

log?.Dispose();

static bool TryReadMessage(Stream input, out string message)
{
    message = "";
    var headers = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
    while (true)
    {
        var line = ReadAsciiLine(input);
        if (line is null)
        {
            return false;
        }

        if (line.Length == 0)
        {
            break;
        }

        var separator = line.IndexOf(':');
        if (separator > 0)
        {
            headers[line[..separator].Trim()] = line[(separator + 1)..].Trim();
        }
    }

    if (!headers.TryGetValue("Content-Length", out var lengthText) ||
        !int.TryParse(lengthText, out var length))
    {
        return false;
    }

    var buffer = new byte[length];
    var offset = 0;
    while (offset < length)
    {
        var read = input.Read(buffer, offset, length - offset);
        if (read == 0)
        {
            return false;
        }

        offset += read;
    }

    message = Encoding.UTF8.GetString(buffer);
    return true;
}

static string? ReadAsciiLine(Stream input)
{
    var bytes = new List<byte>();
    while (true)
    {
        var value = input.ReadByte();
        if (value < 0)
        {
            return bytes.Count == 0 ? null : Encoding.ASCII.GetString(bytes.ToArray());
        }

        if (value == '\n')
        {
            if (bytes.Count > 0 && bytes[^1] == '\r')
            {
                bytes.RemoveAt(bytes.Count - 1);
            }

            return Encoding.ASCII.GetString(bytes.ToArray());
        }

        bytes.Add((byte)value);
    }
}

static void Send(Stream output, JsonObject response)
{
    var payload = JsonSerializer.Serialize(response);
    var bytes = Encoding.UTF8.GetBytes(payload);
    var header = Encoding.ASCII.GetBytes($"Content-Length: {bytes.Length}\r\n\r\n");
    output.Write(header);
    output.Write(bytes);
    output.Flush();
}

static void Log(TextWriter log, JsonNode message)
{
    var entry = new JsonObject
    {
        ["timestampUtc"] = DateTimeOffset.UtcNow.ToString("O"),
        ["direction"] = "received",
        ["method"] = message["method"]?.GetValue<string>(),
        ["id"] = message["id"]?.DeepClone(),
        ["message"] = message.DeepClone()
    };
    log.WriteLine(entry.ToJsonString());
}

static StreamWriter? OpenLog(string? logPath, JsonNode firstMessage)
{
    if (string.IsNullOrWhiteSpace(logPath))
    {
        return null;
    }

    var resolvedPath = logPath;
    if (!Path.IsPathRooted(resolvedPath))
    {
        var rootUri = firstMessage["params"]?["rootUri"]?.GetValue<string>();
        if (rootUri is { Length: > 0 } && Uri.TryCreate(rootUri, UriKind.Absolute, out var uri) && uri.IsFile)
        {
            resolvedPath = Path.Combine(uri.LocalPath, resolvedPath);
        }
        else
        {
            resolvedPath = Path.GetFullPath(resolvedPath);
        }
    }

    var directory = Path.GetDirectoryName(resolvedPath);
    if (!string.IsNullOrEmpty(directory))
    {
        Directory.CreateDirectory(directory);
    }

    return new StreamWriter(File.Open(resolvedPath, FileMode.Append, FileAccess.Write, FileShare.ReadWrite), Encoding.UTF8)
    {
        AutoFlush = true
    };
}
