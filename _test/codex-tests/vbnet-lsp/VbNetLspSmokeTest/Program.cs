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
    public string RootPath { get; set; } = string.Empty;
    public string TestFilePath { get; set; } = string.Empty;
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
            using var pipeStream = ConnectToPipe(pipeName);
            inputStream = pipeStream;
            outputStream = pipeStream;
            return await RunRpcOverStreamAsync(inputStream, outputStream, formatter, options, token);
        }

        inputStream = process.StandardOutput.BaseStream;
        outputStream = process.StandardInput.BaseStream;
        return await RunRpcOverStreamAsync(inputStream, outputStream, formatter, options, token);
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
            clientInfo = new { name = "CodexVbNetLspSmokeTest", version = "0.1" },
        };

        try
        {
            var initializeResult = await rpc
                .InvokeWithParameterObjectAsync<JsonElement>("initialize", initParams)
                .WaitAsync(token);
            Console.WriteLine($"initialize: {initializeResult.ValueKind}");

            await rpc.NotifyWithParameterObjectAsync("initialized", new { }).WaitAsync(token);

            if (!string.IsNullOrWhiteSpace(options.TestFilePath))
            {
                await SendTextDocumentNotificationsAsync(rpc, options, token);
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
            return 4;
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine($"Handshake failed: {ex.Message}");
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

    private static async Task SendTextDocumentNotificationsAsync(JsonRpc rpc, Options options, CancellationToken token)
    {
        var fullPath = Path.GetFullPath(options.TestFilePath);
        if (!File.Exists(fullPath))
        {
            throw new FileNotFoundException("Test file not found.", fullPath);
        }

        var uri = new Uri(fullPath).AbsoluteUri;
        var text = await File.ReadAllTextAsync(fullPath, token);

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
        await rpc.NotifyWithParameterObjectAsync("textDocument/didChange", new
        {
            textDocument = new { uri, version = 2 },
            contentChanges = new[]
            {
                new { text = updatedText }
            }
        }).WaitAsync(token);

        await rpc.NotifyWithParameterObjectAsync("textDocument/didSave", new
        {
            textDocument = new { uri }
        }).WaitAsync(token);

        await rpc.NotifyWithParameterObjectAsync("textDocument/didClose", new
        {
            textDocument = new { uri }
        }).WaitAsync(token);
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
}
