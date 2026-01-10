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

        var process = StartServer(options);
        if (process is null)
        {
            return 3;
        }

        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(options.TimeoutSeconds));
        var exitCode = await RunLspHandshakeAsync(process, options, cts.Token);

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

    private static async Task<int> RunLspHandshakeAsync(Process process, Options options, CancellationToken token)
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
            return await RunRpcOverStreamAsync(inputStream, outputStream, formatter, options, token);
        }
        else
        {
            inputStream = process.StandardOutput.BaseStream;
            outputStream = process.StandardInput.BaseStream;
            return await RunRpcOverStreamAsync(inputStream, outputStream, formatter, options, token);
        }
    }

    private static async Task<int> RunRpcOverStreamAsync(
        Stream inputStream,
        Stream outputStream,
        SystemTextJsonFormatter formatter,
        Options options,
        CancellationToken token)
    {
        var handler = new HeaderDelimitedMessageHandler(
            System.IO.Pipelines.PipeWriter.Create(outputStream),
            System.IO.Pipelines.PipeReader.Create(inputStream),
            formatter);

        using var rpc = new JsonRpc(handler);
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

            await rpc.NotifyWithParameterObjectAsync("initialized", new { }).WaitAsync(token);

            if (!string.IsNullOrWhiteSpace(options.SolutionPath))
            {
                var solutionUri = new Uri(Path.GetFullPath(options.SolutionPath)).AbsoluteUri;
                var method = ResolveRoslynMethod(
                    options.RoslynProtocolPath,
                    "OpenSolutionNotification",
                    "solution/open");
                await rpc.NotifyWithParameterObjectAsync(method, new { solution = solutionUri }).WaitAsync(token);
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
            return 4;
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine($"Handshake failed: {ex.Message}");
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
            else if (arg == "--timeoutSeconds" && i + 1 < args.Length && int.TryParse(args[++i], out var timeout))
            {
                options.TimeoutSeconds = timeout;
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
}
