using System.Diagnostics;
using System.Text;
using System.Text.Json;
using StreamJsonRpc;
using StreamJsonRpc.Protocol;

internal sealed class Options
{
    public string ServerPath { get; set; } = string.Empty;
    public string DotnetPath { get; set; } = "dotnet";
    public string LogLevel { get; set; } = "Information";
    public string Transport { get; set; } = "pipe";
    public int TimeoutSeconds { get; set; } = 30;
    public int DiagnosticsTimeoutSeconds { get; set; } = 30;
    public int WorkspaceLoadDelaySeconds { get; set; } = 3;
    public string RootPath { get; set; } = string.Empty;
    public string TestFilePath { get; set; } = string.Empty;
    public bool ExpectDiagnostics { get; set; }
    public string DiagnosticsMode { get; set; } = string.Empty;
    public int? DebounceMs { get; set; }
    public string ExpectedDiagnosticCode { get; set; } = string.Empty;
    public bool SendDidSave { get; set; }
    public string ProtocolLogPath { get; set; } = string.Empty;
}

internal static class Program
{
    private static async Task<int> Main(string[] args)
    {
        var options = ParseArgs(args);
        if (string.IsNullOrWhiteSpace(options.ServerPath))
        {
            Console.Error.WriteLine("Missing required --serverPath argument.");
            return 2;
        }

        var protocolLog = ProtocolLog.Create(options.ProtocolLogPath, "vbnet-smoke");
        var process = StartServer(options);
        if (process is null)
        {
            protocolLog.Write("error", "Failed to start VB.NET language server.");
            return 3;
        }

        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(options.TimeoutSeconds));
        var exitCode = await RunLspHandshakeAsync(process, options, protocolLog, cts.Token);

        try
        {
            if (!process.HasExited)
            {
                process.Kill(entireProcessTree: true);
            }
        }
        catch
        {
            // Best-effort cleanup only.
        }

        return exitCode;
    }

    private static Process? StartServer(Options options)
    {
        var args = new List<string>
        {
            Quote(options.ServerPath),
        };

        if (string.Equals(options.Transport, "stdio", StringComparison.OrdinalIgnoreCase))
        {
            args.Add("--stdio");
        }
        else
        {
            args.Add("--pipe");
        }

        args.Add("--logLevel");
        args.Add(options.LogLevel);

        var startInfo = new ProcessStartInfo
        {
            FileName = options.DotnetPath,
            Arguments = string.Join(" ", args),
            RedirectStandardInput = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true,
        };

        var process = new Process { StartInfo = startInfo };
        process.ErrorDataReceived += (_, e) =>
        {
            if (!string.IsNullOrWhiteSpace(e.Data))
            {
                Console.Error.WriteLine($"[server stderr] {e.Data}");
            }
        };

        if (!process.Start())
        {
            Console.Error.WriteLine("Failed to start VB.NET language server.");
            return null;
        }

        process.BeginErrorReadLine();
        return process;
    }

    private static async Task<int> RunLspHandshakeAsync(Process process, Options options, ProtocolLog protocolLog, CancellationToken token)
    {
        var formatter = new SystemTextJsonFormatter();
        formatter.JsonSerializerOptions.PropertyNamingPolicy = JsonNamingPolicy.CamelCase;
        formatter.JsonSerializerOptions.DefaultIgnoreCondition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull;

        Stream inputStream;
        Stream outputStream;

        if (string.Equals(options.Transport, "pipe", StringComparison.OrdinalIgnoreCase))
        {
            var pipeName = await ReadPipeNameAsync(process, token);
            using var pipeStream = ConnectToPipe(pipeName);
            inputStream = pipeStream;
            outputStream = pipeStream;
            return await RunRpcOverStreamAsync(inputStream, outputStream, formatter, options, protocolLog, token);
        }

        inputStream = process.StandardOutput.BaseStream;
        outputStream = process.StandardInput.BaseStream;
        return await RunRpcOverStreamAsync(inputStream, outputStream, formatter, options, protocolLog, token);
    }

    private static async Task<int> RunRpcOverStreamAsync(
        Stream inputStream,
        Stream outputStream,
        SystemTextJsonFormatter formatter,
        Options options,
        ProtocolLog protocolLog,
        CancellationToken token)
    {
        var handler = new HeaderDelimitedMessageHandler(
            System.IO.Pipelines.PipeWriter.Create(outputStream),
            System.IO.Pipelines.PipeReader.Create(inputStream),
            formatter);

        DiagnosticsWaiter? diagnosticsWaiter = null;

        using var rpc = new JsonRpc(handler);
        var settingsPayload = BuildSettingsPayload(options);
        if (settingsPayload != null)
        {
            rpc.AddLocalRpcMethod("workspace/configuration", new Func<JsonElement, object?>(paramsElement =>
            {
                if (paramsElement.TryGetProperty("items", out var itemsElement) &&
                    itemsElement.ValueKind == JsonValueKind.Array)
                {
                    var results = new List<object?>();
                    foreach (var _ in itemsElement.EnumerateArray())
                    {
                        results.Add(settingsPayload);
                    }

                    return results;
                }

                return settingsPayload;
            }));
        }
        if (options.ExpectDiagnostics && !string.IsNullOrWhiteSpace(options.TestFilePath))
        {
            diagnosticsWaiter = new DiagnosticsWaiter(new Uri(Path.GetFullPath(options.TestFilePath)).AbsoluteUri);
            rpc.AddLocalRpcMethod("textDocument/publishDiagnostics", new Action<JsonElement>(paramsElement =>
            {
                if (!paramsElement.TryGetProperty("uri", out var uriElement))
                {
                    protocolLog.Write("error", "publishDiagnostics missing uri.");
                    return;
                }

                var uri = uriElement.GetString();
                if (!string.Equals(uri, diagnosticsWaiter.TargetUri, StringComparison.OrdinalIgnoreCase))
                {
                    return;
                }

                if (!paramsElement.TryGetProperty("diagnostics", out var diagnosticsElement) ||
                    diagnosticsElement.ValueKind != JsonValueKind.Array)
                {
                    protocolLog.Write("error", "publishDiagnostics missing diagnostics array.");
                    return;
                }

                var count = diagnosticsElement.GetArrayLength();
                var expectedFound = true;
                if (!string.IsNullOrWhiteSpace(options.ExpectedDiagnosticCode))
                {
                    expectedFound = ContainsDiagnosticCode(diagnosticsElement, options.ExpectedDiagnosticCode);
                    if (!expectedFound)
                    {
                        protocolLog.Write("warn", $"Expected diagnostic code {options.ExpectedDiagnosticCode} not found in publishDiagnostics payload.");
                    }
                }

                Console.WriteLine($"diagnostics: {count} for {uri} (expectedCode={options.ExpectedDiagnosticCode}, found={expectedFound})");
                diagnosticsWaiter.Notify(count, expectedFound);
            }));
        }
        rpc.StartListening();

        var rootUri = string.IsNullOrWhiteSpace(options.RootPath)
            ? null
            : new Uri(Path.GetFullPath(options.RootPath));

        var initParams = new
        {
            processId = Environment.ProcessId,
            rootUri = rootUri?.AbsoluteUri,
            capabilities = new { },
            clientInfo = new { name = "CodexVbNetLspSmokeTest", version = "0.1" },
        };

        try
        {
            var initializeResult = await rpc
                .InvokeWithParameterObjectAsync<JsonElement>("initialize", initParams)
                .WaitAsync(token);
            Console.WriteLine($"initialize: {initializeResult.ValueKind}");
            if (initializeResult.ValueKind is JsonValueKind.Null or JsonValueKind.Undefined)
            {
                protocolLog.Write("error", "initialize returned null/undefined.");
            }

            await rpc.NotifyWithParameterObjectAsync("initialized", new { }).WaitAsync(token);
            if (settingsPayload != null)
            {
                await rpc.NotifyWithParameterObjectAsync("workspace/didChangeConfiguration", new
                {
                    settings = settingsPayload
                }).WaitAsync(token);
            }

            if (!string.IsNullOrWhiteSpace(options.TestFilePath))
            {
                var diagnosticsReceived = await RunDocumentWorkflowAsync(rpc, options, diagnosticsWaiter, token);
                if (options.ExpectDiagnostics && !diagnosticsReceived)
                {
                    Console.Error.WriteLine("Expected diagnostics but none were received.");
                    protocolLog.Write("error", "Expected diagnostics but none were received.");
                    return 6;
                }
            }

            try
            {
                await rpc
                    .InvokeWithParameterObjectAsync<JsonElement>("shutdown", new { })
                    .WaitAsync(token);
                await rpc.NotifyWithParameterObjectAsync("exit", new { }).WaitAsync(token);
            }
            catch (Exception ex) when (IsConnectionLost(ex))
            {
                Console.Error.WriteLine("Connection lost during shutdown; treating as graceful exit for scaffold server.");
            }

            return 0;
        }
        catch (OperationCanceledException)
        {
            Console.Error.WriteLine("Handshake timed out.");
            protocolLog.Write("error", "Handshake timed out.");
            return 4;
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine($"Handshake failed: {ex.Message}");
            protocolLog.Write("error", $"Handshake failed: {ex.Message}");
            return 5;
        }
    }

    private static bool IsConnectionLost(Exception ex)
    {
        if (ex.Message.Contains("connection with the remote party was lost", StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }

        return ex.InnerException != null && IsConnectionLost(ex.InnerException);
    }

    private static async Task<bool> RunDocumentWorkflowAsync(
        JsonRpc rpc,
        Options options,
        DiagnosticsWaiter? diagnosticsWaiter,
        CancellationToken token)
    {
        var fullPath = Path.GetFullPath(options.TestFilePath);
        if (!File.Exists(fullPath))
        {
            throw new FileNotFoundException("Test file not found.", fullPath);
        }

        var uri = new Uri(fullPath).AbsoluteUri;
        var text = await File.ReadAllTextAsync(fullPath, token);

        if (options.ExpectDiagnostics && options.WorkspaceLoadDelaySeconds > 0)
        {
            Console.WriteLine($"Waiting {options.WorkspaceLoadDelaySeconds}s for workspace load...");
            await Task.Delay(TimeSpan.FromSeconds(options.WorkspaceLoadDelaySeconds), token);
        }

        Console.WriteLine("Sending didOpen...");
        await rpc.NotifyWithParameterObjectAsync("textDocument/didOpen", new
        {
            textDocument = new
            {
                uri,
                languageId = "vb",
                version = 1,
                text
            }
        }).WaitAsync(token);

        var updatedText = text + Environment.NewLine;
        Console.WriteLine("Sending didChange...");
        await rpc.NotifyWithParameterObjectAsync("textDocument/didChange", new
        {
            textDocument = new { uri, version = 2 },
            contentChanges = new[]
            {
                new { text = updatedText }
            }
        }).WaitAsync(token);

        if (options.SendDidSave)
        {
            Console.WriteLine("Sending didSave...");
            await rpc.NotifyWithParameterObjectAsync("textDocument/didSave", new
            {
                textDocument = new { uri },
                text = updatedText
            }).WaitAsync(token);
        }

        var diagnosticsReceived = true;
        if (diagnosticsWaiter != null)
        {
            diagnosticsReceived = await WaitForDiagnosticsAsync(diagnosticsWaiter.Tcs.Task, options.DiagnosticsTimeoutSeconds, token);
            if (!diagnosticsReceived)
            {
                Console.WriteLine("Diagnostics not received; retrying after workspace delay...");
                await rpc.NotifyWithParameterObjectAsync("textDocument/didClose", new
                {
                    textDocument = new { uri }
                }).WaitAsync(token);

                diagnosticsWaiter.Reset();
                if (options.WorkspaceLoadDelaySeconds > 0)
                {
                    await Task.Delay(TimeSpan.FromSeconds(options.WorkspaceLoadDelaySeconds), token);
                }

                Console.WriteLine("Re-sending didOpen...");
                await rpc.NotifyWithParameterObjectAsync("textDocument/didOpen", new
                {
                    textDocument = new
                    {
                        uri,
                        languageId = "vb",
                        version = 3,
                        text = updatedText
                    }
                }).WaitAsync(token);

                Console.WriteLine("Re-sending didChange...");
                await rpc.NotifyWithParameterObjectAsync("textDocument/didChange", new
                {
                    textDocument = new { uri, version = 4 },
                    contentChanges = new[]
                    {
                        new { text = updatedText }
                    }
                }).WaitAsync(token);

                if (options.SendDidSave)
                {
                    Console.WriteLine("Re-sending didSave...");
                    await rpc.NotifyWithParameterObjectAsync("textDocument/didSave", new
                    {
                        textDocument = new { uri },
                        text = updatedText
                    }).WaitAsync(token);
                }

                diagnosticsReceived = await WaitForDiagnosticsAsync(diagnosticsWaiter.Tcs.Task, options.DiagnosticsTimeoutSeconds, token);
            }
        }

        await rpc.NotifyWithParameterObjectAsync("textDocument/didClose", new
        {
            textDocument = new { uri }
        }).WaitAsync(token);

        return diagnosticsReceived;
    }

    private static async Task<string> ReadPipeNameAsync(Process process, CancellationToken token)
    {
        var regex = new System.Text.RegularExpressions.Regex("\\{\"pipeName\":\"[^\"]+\"\\}");
        var stdoutStream = process.StandardOutput.BaseStream;
        var buffer = new byte[4096];
        var builder = new StringBuilder();

        while (!token.IsCancellationRequested)
        {
            var bytesRead = await stdoutStream.ReadAsync(buffer, token);
            if (bytesRead == 0)
            {
                throw new InvalidOperationException("Server stdout closed before pipe name was received.");
            }

            var text = Encoding.UTF8.GetString(buffer, 0, bytesRead);
            builder.Append(text);

            var match = regex.Match(builder.ToString());
            if (match.Success)
            {
                using var doc = JsonDocument.Parse(match.Value);
                if (doc.RootElement.TryGetProperty("pipeName", out var pipeElement))
                {
                    return pipeElement.GetString() ?? throw new InvalidOperationException("Pipe name was empty.");
                }
            }
        }

        throw new OperationCanceledException("Timed out waiting for pipe name.");
    }

    private static System.IO.Pipes.NamedPipeClientStream ConnectToPipe(string pipeName)
    {
        const string windowsPrefix = @"\\.\pipe\";
        if (pipeName.StartsWith(windowsPrefix, StringComparison.OrdinalIgnoreCase))
        {
            pipeName = pipeName.Substring(windowsPrefix.Length);
        }

        var pipeStream = new System.IO.Pipes.NamedPipeClientStream(
            ".",
            pipeName,
            System.IO.Pipes.PipeDirection.InOut,
            System.IO.Pipes.PipeOptions.Asynchronous);

        pipeStream.Connect(TimeSpan.FromSeconds(10));
        if (!pipeStream.IsConnected)
        {
            throw new InvalidOperationException("Failed to connect to named pipe.");
        }

        return pipeStream;
    }

    private static Options ParseArgs(string[] args)
    {
        var options = new Options();

        for (var i = 0; i < args.Length; i++)
        {
            var arg = args[i];
            if (arg == "--serverPath" && i + 1 < args.Length)
            {
                options.ServerPath = args[++i];
            }
            else if (arg == "--dotnetPath" && i + 1 < args.Length)
            {
                options.DotnetPath = args[++i];
            }
            else if (arg == "--logLevel" && i + 1 < args.Length)
            {
                options.LogLevel = args[++i];
            }
            else if (arg == "--transport" && i + 1 < args.Length)
            {
                options.Transport = args[++i];
            }
            else if (arg == "--rootPath" && i + 1 < args.Length)
            {
                options.RootPath = args[++i];
            }
            else if (arg == "--testFile" && i + 1 < args.Length)
            {
                options.TestFilePath = args[++i];
            }
            else if (arg == "--timeoutSeconds" && i + 1 < args.Length && int.TryParse(args[++i], out var timeout))
            {
                options.TimeoutSeconds = timeout;
            }
            else if (arg == "--diagnosticsTimeoutSeconds" && i + 1 < args.Length && int.TryParse(args[++i], out var diagTimeout))
            {
                options.DiagnosticsTimeoutSeconds = diagTimeout;
            }
            else if (arg == "--workspaceLoadDelaySeconds" && i + 1 < args.Length && int.TryParse(args[++i], out var delay))
            {
                options.WorkspaceLoadDelaySeconds = delay;
            }
            else if (arg == "--expectDiagnostics")
            {
                options.ExpectDiagnostics = true;
            }
            else if (arg == "--diagnosticsMode" && i + 1 < args.Length)
            {
                options.DiagnosticsMode = args[++i];
            }
            else if (arg == "--debounceMs" && i + 1 < args.Length && int.TryParse(args[++i], out var debounceMs))
            {
                options.DebounceMs = debounceMs;
            }
            else if (arg == "--expectDiagnosticCode" && i + 1 < args.Length)
            {
                options.ExpectedDiagnosticCode = args[++i];
            }
            else if (arg == "--sendDidSave")
            {
                options.SendDidSave = true;
            }
            else if (arg == "--protocolLog" && i + 1 < args.Length)
            {
                options.ProtocolLogPath = args[++i];
            }
        }

        return options;
    }

    private static string Quote(string value)
    {
        if (value.Contains('"'))
        {
            value = value.Replace("\"", "\\\"");
        }

        return $"\"{value}\"";
    }

    private static async Task<bool> WaitForDiagnosticsAsync(Task<int> diagnosticsTask, int timeoutSeconds, CancellationToken token)
    {
        var timeout = Task.Delay(TimeSpan.FromSeconds(timeoutSeconds), token);
        var completed = await Task.WhenAny(diagnosticsTask, timeout);
        return completed == diagnosticsTask;
    }

    private static object? BuildSettingsPayload(Options options)
    {
        if (string.IsNullOrWhiteSpace(options.DiagnosticsMode) && options.DebounceMs is null)
        {
            return null;
        }

        var diagnosticsSettings = new Dictionary<string, object?>();
        if (!string.IsNullOrWhiteSpace(options.DiagnosticsMode))
        {
            diagnosticsSettings["diagnosticsMode"] = options.DiagnosticsMode;
        }

        if (options.DebounceMs.HasValue)
        {
            diagnosticsSettings["debounceMs"] = options.DebounceMs.Value;
        }

        return new Dictionary<string, object?>
        {
            ["vbnetLs"] = diagnosticsSettings
        };
    }

    private static bool ContainsDiagnosticCode(JsonElement diagnosticsElement, string expectedCode)
    {
        foreach (var diagnostic in diagnosticsElement.EnumerateArray())
        {
            if (!diagnostic.TryGetProperty("code", out var codeElement))
            {
                continue;
            }

            string? codeValue = codeElement.ValueKind switch
            {
                JsonValueKind.String => codeElement.GetString(),
                JsonValueKind.Number => codeElement.GetRawText(),
                _ => null
            };

            if (string.Equals(codeValue, expectedCode, StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }
        }

        return false;
    }

    private sealed class DiagnosticsWaiter
    {
        public DiagnosticsWaiter(string targetUri)
        {
            TargetUri = targetUri;
            Tcs = NewTcs();
        }

        public string TargetUri { get; }

        public TaskCompletionSource<int> Tcs { get; private set; }

        public void Reset()
        {
            Tcs = NewTcs();
        }

        public void Notify(int count, bool expectedFound)
        {
            if (count > 0 && expectedFound)
            {
                Tcs.TrySetResult(count);
            }
        }

        private static TaskCompletionSource<int> NewTcs()
        {
            return new TaskCompletionSource<int>(TaskCreationOptions.RunContinuationsAsynchronously);
        }
    }

    private sealed class ProtocolLog
    {
        private readonly string _path;
        private readonly string _harness;

        private ProtocolLog(string path, string harness)
        {
            _path = path;
            _harness = harness;
        }

        public static ProtocolLog Create(string path, string harness)
        {
            if (string.IsNullOrWhiteSpace(path))
            {
                return new ProtocolLog(string.Empty, harness);
            }

            var fullPath = Path.GetFullPath(path);
            Directory.CreateDirectory(Path.GetDirectoryName(fullPath)!);
            return new ProtocolLog(fullPath, harness);
        }

        public void Write(string severity, string message)
        {
            if (string.IsNullOrWhiteSpace(_path))
            {
                return;
            }

            var payload = new
            {
                timestamp = DateTimeOffset.Now.ToString("o"),
                harness = _harness,
                severity,
                message
            };

            File.AppendAllText(_path, JsonSerializer.Serialize(payload) + Environment.NewLine);
        }
    }
}
