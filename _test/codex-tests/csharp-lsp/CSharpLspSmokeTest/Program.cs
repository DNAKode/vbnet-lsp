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
    public string LogDirectory { get; set; } = string.Empty;
    public string RootPath { get; set; } = string.Empty;
    public string Transport { get; set; } = "stdio";
    public int TimeoutSeconds { get; set; } = 30;
    public string SolutionPath { get; set; } = string.Empty;
    public string RoslynProtocolPath { get; set; } = string.Empty;
    public string TestFilePath { get; set; } = string.Empty;
    public string TestMarker { get; set; } = "/*caret*/";
    public int? TestLine { get; set; }
    public int? TestCharacter { get; set; }
    public bool FeatureTests { get; set; }
    public int FeatureTimeoutSeconds { get; set; } = 60;
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

        if (string.IsNullOrWhiteSpace(options.LogDirectory))
        {
            options.LogDirectory = Path.Combine(Path.GetTempPath(), "csharp-lsp-logs");
        }

        Directory.CreateDirectory(options.LogDirectory);

        var protocolLog = ProtocolLog.Create(options.ProtocolLogPath, "csharp-dotnet");
        var process = StartServer(options);
        if (process is null)
        {
            protocolLog.Write("error", "Failed to start language server process.");
            return 3;
        }

        var totalTimeoutSeconds = options.TimeoutSeconds;
        if (options.FeatureTests && !string.IsNullOrWhiteSpace(options.TestFilePath))
        {
            totalTimeoutSeconds += options.FeatureTimeoutSeconds;
        }

        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(totalTimeoutSeconds));
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
            "--logLevel",
            options.LogLevel,
            "--extensionLogDirectory",
            Quote(options.LogDirectory),
        };

        if (string.Equals(options.Transport, "stdio", StringComparison.OrdinalIgnoreCase))
        {
            args.Insert(1, "--stdio");
        }

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
            Console.Error.WriteLine("Failed to start language server process.");
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
            using var pipeStream = ConnectToPipe(pipeName, token);
            inputStream = pipeStream;
            outputStream = pipeStream;
            return await RunRpcOverStreamAsync(inputStream, outputStream, formatter, options, protocolLog, token);
        }
        else
        {
            inputStream = process.StandardOutput.BaseStream;
            outputStream = process.StandardInput.BaseStream;
            return await RunRpcOverStreamAsync(inputStream, outputStream, formatter, options, protocolLog, token);
        }
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

        using var rpc = new JsonRpc(handler);
        var projectInitMethod = ResolveRoslynMethod(
            options.RoslynProtocolPath,
            "ProjectInitializationCompleteNotification",
            "workspace/projectInitializationComplete");
        var projectInitTcs = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        rpc.AddLocalRpcMethod(projectInitMethod, new Action<JsonElement>(_ => projectInitTcs.TrySetResult(true)));
        rpc.StartListening();

        var rootUri = string.IsNullOrWhiteSpace(options.RootPath)
            ? null
            : new Uri(Path.GetFullPath(options.RootPath));

        var initParams = new
        {
            processId = Environment.ProcessId,
            rootUri = rootUri?.AbsoluteUri,
            capabilities = new { },
            clientInfo = new { name = "CodexCSharpLspSmokeTest", version = "0.1" },
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

            Console.WriteLine("sending initialized...");
            await rpc.NotifyWithParameterObjectAsync("initialized", new { }).WaitAsync(token);
            Console.WriteLine("initialized sent.");

            if (!string.IsNullOrWhiteSpace(options.SolutionPath))
            {
                Console.WriteLine("sending solution/open...");
                var solutionUri = new Uri(Path.GetFullPath(options.SolutionPath)).AbsoluteUri;
                var method = ResolveRoslynMethod(
                    options.RoslynProtocolPath,
                    "OpenSolutionNotification",
                    "solution/open");
                await rpc.NotifyWithParameterObjectAsync(method, new { solution = solutionUri }).WaitAsync(token);
                Console.WriteLine("solution/open sent.");
            }

            if (options.FeatureTests && !string.IsNullOrWhiteSpace(options.TestFilePath))
            {
                Console.WriteLine("waiting for project initialization...");
                var projectReady = await WaitForProjectInitializationAsync(projectInitTcs.Task, options.FeatureTimeoutSeconds);
                Console.WriteLine(projectReady
                    ? "Project initialization completed."
                    : "Project initialization timed out; proceeding with feature tests.");
                if (!projectReady)
                {
                    protocolLog.Write("warn", "workspace/projectInitializationComplete did not arrive within timeout.");
                }

                Console.WriteLine("running feature tests...");
                using var featureCts = new CancellationTokenSource(TimeSpan.FromSeconds(options.FeatureTimeoutSeconds));
                var featuresOk = await RunFeatureTestsAsync(rpc, options, featureCts.Token);
                if (!featuresOk)
                {
                    protocolLog.Write("error", "One or more feature tests failed.");
                    return 6;
                }
            }

            await rpc
                .InvokeWithParameterObjectAsync<JsonElement>("shutdown", new { })
                .WaitAsync(token);
            await rpc.NotifyWithParameterObjectAsync("exit", new { }).WaitAsync(token);

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

    private static System.IO.Pipes.NamedPipeClientStream ConnectToPipe(string pipeName, CancellationToken token)
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
            else if (arg == "--logDirectory" && i + 1 < args.Length)
            {
                options.LogDirectory = args[++i];
            }
            else if (arg == "--rootPath" && i + 1 < args.Length)
            {
                options.RootPath = args[++i];
            }
            else if (arg == "--transport" && i + 1 < args.Length)
            {
                options.Transport = args[++i];
            }
            else if (arg == "--solutionPath" && i + 1 < args.Length)
            {
                options.SolutionPath = args[++i];
            }
            else if (arg == "--protocolPath" && i + 1 < args.Length)
            {
                options.RoslynProtocolPath = args[++i];
            }
            else if (arg == "--testFile" && i + 1 < args.Length)
            {
                options.TestFilePath = args[++i];
            }
            else if (arg == "--testMarker" && i + 1 < args.Length)
            {
                options.TestMarker = args[++i];
            }
            else if (arg == "--testLine" && i + 1 < args.Length && int.TryParse(args[++i], out var line))
            {
                options.TestLine = line;
            }
            else if (arg == "--testChar" && i + 1 < args.Length && int.TryParse(args[++i], out var character))
            {
                options.TestCharacter = character;
            }
            else if (arg == "--featureTests")
            {
                options.FeatureTests = true;
            }
            else if (arg == "--timeoutSeconds" && i + 1 < args.Length && int.TryParse(args[++i], out var timeout))
            {
                options.TimeoutSeconds = timeout;
            }
            else if (arg == "--featureTimeoutSeconds" && i + 1 < args.Length && int.TryParse(args[++i], out var featureTimeout))
            {
                options.FeatureTimeoutSeconds = featureTimeout;
            }
            else if (arg == "--protocolLog" && i + 1 < args.Length)
            {
                options.ProtocolLogPath = args[++i];
            }
        }

        if (!string.IsNullOrWhiteSpace(options.TestFilePath))
        {
            options.FeatureTests = true;
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

    private static string ResolveRoslynMethod(string protocolPath, string namespaceName, string fallback)
    {
        if (string.IsNullOrWhiteSpace(protocolPath) || !File.Exists(protocolPath))
        {
            return fallback;
        }

        var content = File.ReadAllText(protocolPath);
        var pattern = $@"export\s+namespace\s+{namespaceName}\s*\{{[^}}]*?export\s+const\s+method\s*=\s*['""]([^'""]+)['""]";
        var match = System.Text.RegularExpressions.Regex.Match(content, pattern, System.Text.RegularExpressions.RegexOptions.Singleline);
        if (match.Success)
        {
            return match.Groups[1].Value;
        }

        return fallback;
    }

    private static async Task<bool> WaitForProjectInitializationAsync(Task<bool> projectInitTask, int timeoutSeconds)
    {
        var timeout = Task.Delay(TimeSpan.FromSeconds(timeoutSeconds));
        var completed = await Task.WhenAny(projectInitTask, timeout);
        return completed == projectInitTask;
    }

    private static async Task<bool> RunFeatureTestsAsync(JsonRpc rpc, Options options, CancellationToken token)
    {
        var doc = LoadTestDocument(options);
        var position = new { line = doc.Position.Line, character = doc.Position.Character };
        var textDocument = new { uri = doc.Uri };

        await rpc.NotifyWithParameterObjectAsync("textDocument/didOpen", new
        {
            textDocument = new
            {
                uri = doc.Uri,
                languageId = "csharp",
                version = 1,
                text = doc.Text
            }
        }).WaitAsync(token);

        await Task.Delay(250, token);

        var completion = await rpc
            .InvokeWithParameterObjectAsync<JsonElement>("textDocument/completion", new { textDocument, position })
            .WaitAsync(token);
        var completionOk = TryFindCompletionItem(completion, "Add", out var completionCount);
        Console.WriteLine($"completion: items={completionCount}, contains Add={completionOk}");

        var hover = await rpc
            .InvokeWithParameterObjectAsync<JsonElement>("textDocument/hover", new { textDocument, position })
            .WaitAsync(token);
        var hoverOk = hover.ValueKind != JsonValueKind.Null && hover.ValueKind != JsonValueKind.Undefined;
        Console.WriteLine($"hover: {hover.ValueKind}");

        var definition = await rpc
            .InvokeWithParameterObjectAsync<JsonElement>("textDocument/definition", new { textDocument, position })
            .WaitAsync(token);
        var definitionOk = HasAnyResult(definition);
        Console.WriteLine($"definition: {definition.ValueKind}");

        var references = await rpc
            .InvokeWithParameterObjectAsync<JsonElement>("textDocument/references", new
            {
                textDocument,
                position,
                context = new { includeDeclaration = true }
            })
            .WaitAsync(token);
        var referencesOk = references.ValueKind == JsonValueKind.Array && references.GetArrayLength() > 0;
        Console.WriteLine($"references: {references.ValueKind}");

        var symbols = await rpc
            .InvokeWithParameterObjectAsync<JsonElement>("textDocument/documentSymbol", new { textDocument })
            .WaitAsync(token);
        var symbolsOk = symbols.ValueKind == JsonValueKind.Array && symbols.GetArrayLength() > 0;
        Console.WriteLine($"documentSymbol: {symbols.ValueKind}");

        return completionOk && hoverOk && definitionOk && referencesOk && symbolsOk;
    }

    private static bool TryFindCompletionItem(JsonElement completion, string label, out int count)
    {
        count = 0;
        if (completion.ValueKind == JsonValueKind.Array)
        {
            count = completion.GetArrayLength();
            foreach (var item in completion.EnumerateArray())
            {
                if (item.TryGetProperty("label", out var labelElement) &&
                    string.Equals(labelElement.GetString(), label, StringComparison.Ordinal))
                {
                    return true;
                }
            }

            return false;
        }

        if (completion.ValueKind == JsonValueKind.Object &&
            completion.TryGetProperty("items", out var itemsElement) &&
            itemsElement.ValueKind == JsonValueKind.Array)
        {
            count = itemsElement.GetArrayLength();
            foreach (var item in itemsElement.EnumerateArray())
            {
                if (item.TryGetProperty("label", out var labelElement) &&
                    string.Equals(labelElement.GetString(), label, StringComparison.Ordinal))
                {
                    return true;
                }
            }

            return false;
        }

        return false;
    }

    private static bool HasAnyResult(JsonElement result)
    {
        if (result.ValueKind == JsonValueKind.Array)
        {
            return result.GetArrayLength() > 0;
        }

        return result.ValueKind == JsonValueKind.Object;
    }

    private static TestDocument LoadTestDocument(Options options)
    {
        if (!File.Exists(options.TestFilePath))
        {
            throw new FileNotFoundException("Test file not found.", options.TestFilePath);
        }

        var text = File.ReadAllText(options.TestFilePath);
        var markerIndex = text.IndexOf(options.TestMarker, StringComparison.Ordinal);
        if (markerIndex >= 0)
        {
            var position = GetPosition(text, markerIndex);
            text = text.Remove(markerIndex, options.TestMarker.Length);
            return new TestDocument(Path.GetFullPath(options.TestFilePath), text, position);
        }

        if (options.TestLine.HasValue && options.TestCharacter.HasValue)
        {
            return new TestDocument(
                Path.GetFullPath(options.TestFilePath),
                text,
                new TextPosition(options.TestLine.Value, options.TestCharacter.Value));
        }

        throw new InvalidOperationException("No marker found and no --testLine/--testChar provided.");
    }

    private static TextPosition GetPosition(string text, int index)
    {
        var line = 0;
        var lastNewline = -1;
        for (var i = 0; i < index; i++)
        {
            if (text[i] == '\n')
            {
                line++;
                lastNewline = i;
            }
        }

        var character = index - lastNewline - 1;
        return new TextPosition(line, character);
    }

    private readonly record struct TestDocument(string Path, string Text, TextPosition Position)
    {
        public string Uri => new Uri(Path).AbsoluteUri;
    }

    private readonly record struct TextPosition(int Line, int Character);

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
