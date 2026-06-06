using System.Diagnostics;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;

var repoRoot = args.Length > 0
    ? Path.GetFullPath(args[0])
    : Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", "..", ".."));

var serverPath = args.Length > 1
    ? Path.GetFullPath(args[1])
    : Path.Combine(repoRoot, "src", "VbNet.LanguageServer.Vb", "bin", "Debug", "net10.0", "VbNet.LanguageServer.dll");
var workspaceRoot = Path.Combine(repoRoot, "test", "TestProjects", "SmallProject");
var documentPath = Path.Combine(workspaceRoot, "Module1.vb");

Expect(File.Exists(serverPath), $"Language server not found: {serverPath}");
Expect(Directory.Exists(workspaceRoot), $"Workspace not found: {workspaceRoot}");
Expect(File.Exists(documentPath), $"Document not found: {documentPath}");

using var process = StartServer(serverPath, workspaceRoot);
var stderrTask = process.StandardError.ReadToEndAsync();

try
{
    var workspaceUri = new Uri(workspaceRoot + Path.DirectorySeparatorChar).AbsoluteUri;
    var documentUri = new Uri(documentPath).AbsoluteUri;

    Send(process, new JsonObject
    {
        ["jsonrpc"] = "2.0",
        ["id"] = 1,
        ["method"] = "initialize",
        ["params"] = new JsonObject
        {
            ["processId"] = Environment.ProcessId,
            ["rootUri"] = workspaceUri,
            ["capabilities"] = new JsonObject(),
            ["initializationOptions"] = new JsonObject
            {
                ["semanticTokens"] = true,
                ["loadProjectsOnStart"] = false,
                ["workspace"] = new JsonObject
                {
                    ["solutionPath"] = "SmallProject.sln"
                }
            },
            ["workspaceFolders"] = new JsonArray
            {
                new JsonObject
                {
                    ["uri"] = workspaceUri,
                    ["name"] = "SmallProject"
                }
            }
        }
    });

    var initialize = ReadResponse(process, 1);
    Expect(initialize["result"]?["capabilities"]?["hoverProvider"]?.GetValue<bool>() == true,
        "Real server did not advertise hoverProvider.");
    Expect(initialize["result"]?["serverInfo"]?["name"]?.GetValue<string>() == "VbNet.LanguageServer",
        "Real server did not return expected serverInfo.name.");

    Send(process, new JsonObject
    {
        ["jsonrpc"] = "2.0",
        ["method"] = "initialized",
        ["params"] = new JsonObject()
    });

    Send(process, new JsonObject
    {
        ["jsonrpc"] = "2.0",
        ["method"] = "textDocument/didOpen",
        ["params"] = new JsonObject
        {
            ["textDocument"] = new JsonObject
            {
                ["uri"] = documentUri,
                ["languageId"] = "vb",
                ["version"] = 1,
                ["text"] = File.ReadAllText(documentPath)
            }
        }
    });

    Send(process, new JsonObject
    {
        ["jsonrpc"] = "2.0",
        ["id"] = 2,
        ["method"] = "shutdown",
        ["params"] = new JsonObject()
    });

    _ = ReadResponse(process, 2);

    process.StandardInput.Close();
    if (!process.WaitForExit(10_000))
    {
        process.Kill(entireProcessTree: true);
        throw new InvalidOperationException("Real server did not exit after shutdown.");
    }

    var stderr = await stderrTask.ConfigureAwait(false);
    Expect(process.ExitCode == 0, $"Real server exited with code {process.ExitCode}: {stderr}");
    Expect(stderr.Contains("VB.NET Language Server", StringComparison.OrdinalIgnoreCase),
        "Real server stderr did not include startup log output.");
    Expect(!stderr.Contains("Language server crashed", StringComparison.OrdinalIgnoreCase),
        $"Real server stderr contained crash text: {stderr}");
    Expect(!stderr.Contains("Unhandled exception", StringComparison.OrdinalIgnoreCase),
        $"Real server stderr contained unhandled exception text: {stderr}");

    Console.WriteLine("Zed real-server protocol harness passed.");
}
finally
{
    if (!process.HasExited)
    {
        process.Kill(entireProcessTree: true);
    }
}

static Process StartServer(string serverPath, string workspaceRoot)
{
    var isDll = string.Equals(Path.GetExtension(serverPath), ".dll", StringComparison.OrdinalIgnoreCase);
    var startInfo = new ProcessStartInfo(isDll ? "dotnet" : serverPath)
    {
        RedirectStandardInput = true,
        RedirectStandardOutput = true,
        RedirectStandardError = true,
        UseShellExecute = false,
        CreateNoWindow = true,
        WorkingDirectory = workspaceRoot
    };

    if (isDll)
    {
        startInfo.ArgumentList.Add(serverPath);
    }

    startInfo.ArgumentList.Add("--stdio");
    startInfo.ArgumentList.Add("--logLevel");
    startInfo.ArgumentList.Add("Debug");
    return Process.Start(startInfo) ?? throw new InvalidOperationException($"Failed to start server: {serverPath}");
}

static void Send(Process process, JsonObject message)
{
    var payload = JsonSerializer.Serialize(message);
    var bytes = Encoding.UTF8.GetBytes(payload);
    var header = Encoding.ASCII.GetBytes($"Content-Length: {bytes.Length}\r\n\r\n");
    process.StandardInput.BaseStream.Write(header);
    process.StandardInput.BaseStream.Write(bytes);
    process.StandardInput.BaseStream.Flush();
}

static JsonNode ReadMessage(Process process)
{
    var headers = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
    while (true)
    {
        var line = ReadAsciiLine(process.StandardOutput.BaseStream)
            ?? throw new InvalidOperationException("Server stdout closed while reading headers.");
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
        throw new InvalidOperationException("Server response did not include a valid Content-Length header.");
    }

    var buffer = new byte[length];
    var offset = 0;
    while (offset < length)
    {
        var read = process.StandardOutput.BaseStream.Read(buffer, offset, length - offset);
        if (read == 0)
        {
            throw new InvalidOperationException("Server stdout closed while reading response body.");
        }

        offset += read;
    }

    return JsonNode.Parse(Encoding.UTF8.GetString(buffer))
        ?? throw new InvalidOperationException("Server response was not valid JSON.");
}

static JsonNode ReadResponse(Process process, int id)
{
    var deadline = DateTime.UtcNow.AddSeconds(20);
    while (DateTime.UtcNow < deadline)
    {
        var message = ReadMessage(process);
        if (message["id"] is JsonValue idValue &&
            idValue.TryGetValue<int>(out var responseId) &&
            responseId == id)
        {
            return message;
        }
    }

    throw new InvalidOperationException($"Server did not send response id {id} before timeout.");
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

static void Expect(bool condition, string message)
{
    if (!condition)
    {
        throw new InvalidOperationException(message);
    }
}
