using System.Diagnostics;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;

var repoRoot = args.Length > 0
    ? Path.GetFullPath(args[0])
    : Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", "..", ".."));

var probeRoot = Path.Combine(repoRoot, "test-explore", "clients", "zed", "probes");
var tempRoot = Path.Combine(Path.GetTempPath(), "vbnet-zed-probe-harness-" + Guid.NewGuid().ToString("N"));
Directory.CreateDirectory(tempRoot);

try
{
    RunLspProbe(probeRoot, tempRoot);
    RunDapProbe(probeRoot, tempRoot);
    Console.WriteLine("Zed probe protocol harness passed.");
}
finally
{
    try
    {
        Directory.Delete(tempRoot, recursive: true);
    }
    catch
    {
        // Best effort cleanup for failed runs.
    }
}

static void RunLspProbe(string probeRoot, string tempRoot)
{
    var workspaceRoot = Path.Combine(tempRoot, "workspace");
    Directory.CreateDirectory(workspaceRoot);
    var logPath = "zed-lsp-probe.jsonl";
    var resolvedLogPath = Path.Combine(workspaceRoot, logPath);
    using var process = StartProbe(
        Path.Combine(probeRoot, "lsp-probe", "bin", "Release", "net10.0", "VbNet.Zed.LspProbe.dll"),
        ("VBNET_ZED_TEST_LOG", logPath));

    Send(process, new JsonObject
    {
        ["jsonrpc"] = "2.0",
        ["id"] = 1,
        ["method"] = "initialize",
        ["params"] = new JsonObject
        {
            ["rootUri"] = new Uri(workspaceRoot).AbsoluteUri,
            ["capabilities"] = new JsonObject()
        }
    });
    var initialize = ReadMessage(process);
    Expect(initialize["result"]?["capabilities"]?["hoverProvider"]?.GetValue<bool>() == true,
        "LSP probe did not advertise hoverProvider.");

    Send(process, new JsonObject
    {
        ["jsonrpc"] = "2.0",
        ["method"] = "textDocument/didOpen",
        ["params"] = new JsonObject
        {
            ["textDocument"] = new JsonObject
            {
                ["uri"] = "file:///tmp/vbnet-zed/Module1.vb",
                ["languageId"] = "vb",
                ["version"] = 1,
                ["text"] = "Module Module1\nEnd Module\n"
            }
        }
    });

    Send(process, new JsonObject
    {
        ["jsonrpc"] = "2.0",
        ["id"] = 2,
        ["method"] = "textDocument/hover",
        ["params"] = new JsonObject()
    });
    var hover = ReadMessage(process);
    Expect(hover["result"]?["contents"]?["value"]?.GetValue<string>() == "VB.NET probe hover",
        "LSP probe hover response was not deterministic.");

    Send(process, new JsonObject
    {
        ["jsonrpc"] = "2.0",
        ["id"] = 3,
        ["method"] = "shutdown",
        ["params"] = new JsonObject()
    });
    _ = ReadMessage(process);
    process.StandardInput.Close();
    ExpectExit(process, "LSP probe");

    var log = File.ReadAllText(resolvedLogPath);
    Expect(log.Contains("\"method\":\"initialize\""), "LSP probe log did not record initialize.");
    Expect(log.Contains("\"method\":\"textDocument/didOpen\""), "LSP probe log did not record didOpen.");
    Expect(log.Contains("\"method\":\"textDocument/hover\""), "LSP probe log did not record hover.");
}

static void RunDapProbe(string probeRoot, string tempRoot)
{
    RunDapSession(probeRoot, Path.Combine(tempRoot, "dap-launch.jsonl"), "launch");
    RunDapSession(probeRoot, Path.Combine(tempRoot, "dap-attach.jsonl"), "attach");
}

static void RunDapSession(string probeRoot, string logPath, string requestCommand)
{
    using var process = StartProbe(
        Path.Combine(probeRoot, "dap-probe", "bin", "Release", "net10.0", "VbNet.Zed.DapProbe.dll"),
        ("VBNET_ZED_DAP_TEST_LOG", logPath));

    Send(process, new JsonObject
    {
        ["seq"] = 1,
        ["type"] = "request",
        ["command"] = "initialize",
        ["arguments"] = new JsonObject()
    });
    var initialize = ReadMessage(process);
    Expect(initialize["type"]?.GetValue<string>() == "response" &&
           initialize["command"]?.GetValue<string>() == "initialize",
        "DAP probe did not respond to initialize.");
    var initializedEvent = ReadMessage(process);
    Expect(initializedEvent["event"]?.GetValue<string>() == "initialized",
        "DAP probe did not send initialized event.");

    foreach (var command in new[] { "setBreakpoints", requestCommand, "configurationDone", "disconnect" })
    {
        Send(process, new JsonObject
        {
            ["seq"] = command switch
            {
                "setBreakpoints" => 2,
                "launch" or "attach" => 3,
                "configurationDone" => 4,
                _ => 5
            },
            ["type"] = "request",
            ["command"] = command,
            ["arguments"] = new JsonObject()
        });
        var response = ReadMessage(process);
        Expect(response["type"]?.GetValue<string>() == "response" &&
               response["command"]?.GetValue<string>() == command,
            $"DAP probe did not respond to {command}.");
    }

    var terminatedEvent = ReadMessage(process);
    Expect(terminatedEvent["event"]?.GetValue<string>() == "terminated",
        "DAP probe did not send terminated event.");
    process.StandardInput.Close();
    ExpectExit(process, "DAP probe");

    var log = File.ReadAllText(logPath);
    foreach (var command in new[] { "initialize", "setBreakpoints", requestCommand, "configurationDone", "disconnect" })
    {
        Expect(log.Contains($"\"command\":\"{command}\""), $"DAP probe log did not record {command}.");
    }
}

static Process StartProbe(string dllPath, params (string Key, string Value)[] environment)
{
    if (!File.Exists(dllPath))
    {
        throw new InvalidOperationException($"Probe DLL not found: {dllPath}");
    }

    var startInfo = new ProcessStartInfo("dotnet", dllPath)
    {
        RedirectStandardInput = true,
        RedirectStandardOutput = true,
        RedirectStandardError = true,
        UseShellExecute = false,
        CreateNoWindow = true
    };

    foreach (var (key, value) in environment)
    {
        startInfo.Environment[key] = value;
    }

    var process = Process.Start(startInfo)
        ?? throw new InvalidOperationException($"Failed to start probe: {dllPath}");
    return process;
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
            ?? throw new InvalidOperationException("Probe stdout closed while reading headers.");
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
        throw new InvalidOperationException("Probe response did not include a valid Content-Length header.");
    }

    var buffer = new byte[length];
    var offset = 0;
    while (offset < length)
    {
        var read = process.StandardOutput.BaseStream.Read(buffer, offset, length - offset);
        if (read == 0)
        {
            throw new InvalidOperationException("Probe stdout closed while reading response body.");
        }

        offset += read;
    }

    return JsonNode.Parse(Encoding.UTF8.GetString(buffer))
        ?? throw new InvalidOperationException("Probe response was not valid JSON.");
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

static void ExpectExit(Process process, string name)
{
    if (!process.WaitForExit(5000))
    {
        process.Kill(entireProcessTree: true);
        throw new InvalidOperationException($"{name} did not exit after shutdown.");
    }

    if (process.ExitCode != 0)
    {
        var stderr = process.StandardError.ReadToEnd();
        throw new InvalidOperationException($"{name} exited with code {process.ExitCode}: {stderr}");
    }
}
