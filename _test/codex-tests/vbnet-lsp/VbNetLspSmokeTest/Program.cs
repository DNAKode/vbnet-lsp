using System.Diagnostics;
using System.Text;
using System.Text.Json;
using System.Linq;
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
    public string ServiceTestsPath { get; set; } = string.Empty;
    public int ServiceTimeoutSeconds { get; set; } = 60;
    public string ServiceLogPath { get; set; } = string.Empty;
    public string ServiceTestId { get; set; } = string.Empty;
    public string ProtocolLogPath { get; set; } = string.Empty;
    public string TimingLogPath { get; set; } = string.Empty;
    public string TimingLabel { get; set; } = string.Empty;
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
        var timingLog = TimingLog.Create(options.TimingLogPath, options.TimingLabel);
        var stopwatch = Stopwatch.StartNew();
        var process = StartServer(options, timingLog, stopwatch);
        if (process is null)
        {
            protocolLog.Write("error", "Failed to start VB.NET language server.");
            return 3;
        }

        var totalTimeoutSeconds = options.TimeoutSeconds;
        if (!string.IsNullOrWhiteSpace(options.ServiceTestsPath))
        {
            totalTimeoutSeconds += options.ServiceTimeoutSeconds;
        }

        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(totalTimeoutSeconds));
        var exitCode = await RunLspHandshakeAsync(process, options, protocolLog, timingLog, stopwatch, cts.Token);

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

    private static Process? StartServer(Options options, TimingLog timingLog, Stopwatch stopwatch)
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
                timingLog.TryMarkFromServerLine(e.Data, stopwatch);
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

    private static async Task<int> RunLspHandshakeAsync(
        Process process,
        Options options,
        ProtocolLog protocolLog,
        TimingLog timingLog,
        Stopwatch stopwatch,
        CancellationToken token)
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
            return await RunRpcOverStreamAsync(inputStream, outputStream, formatter, options, protocolLog, timingLog, stopwatch, token);
        }

        inputStream = process.StandardOutput.BaseStream;
        outputStream = process.StandardInput.BaseStream;
        return await RunRpcOverStreamAsync(inputStream, outputStream, formatter, options, protocolLog, timingLog, stopwatch, token);
    }

    private static async Task<int> RunRpcOverStreamAsync(
        Stream inputStream,
        Stream outputStream,
        SystemTextJsonFormatter formatter,
        Options options,
        ProtocolLog protocolLog,
        TimingLog timingLog,
        Stopwatch stopwatch,
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
            timingLog.Mark("initialize_response", stopwatch);

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
                var diagnosticsReceived = await RunDocumentWorkflowAsync(rpc, options, diagnosticsWaiter, timingLog, stopwatch, token);
                if (options.ExpectDiagnostics && !diagnosticsReceived)
                {
                    Console.Error.WriteLine("Expected diagnostics but none were received.");
                    protocolLog.Write("error", "Expected diagnostics but none were received.");
                    return 6;
                }
            }

            if (!string.IsNullOrWhiteSpace(options.ServiceTestsPath))
            {
                Console.WriteLine("Running service tests...");
                using var serviceCts = new CancellationTokenSource(TimeSpan.FromSeconds(options.ServiceTimeoutSeconds));
                var servicesOk = await RunServiceTestsAsync(rpc, options, protocolLog, serviceCts.Token);
                if (!servicesOk)
                {
                    protocolLog.Write("error", "One or more service tests failed.");
                    return 7;
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
        TimingLog timingLog,
        Stopwatch stopwatch,
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
        timingLog.Mark("didOpen_sent", stopwatch);

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

    private static async Task<bool> RunServiceTestsAsync(
        JsonRpc rpc,
        Options options,
        ProtocolLog protocolLog,
        CancellationToken token)
    {
        var manifest = ServiceTestManifest.Load(options.ServiceTestsPath);
        if (manifest.Tests.Count == 0)
        {
            Console.WriteLine("Service test manifest has no tests.");
            return true;
        }

        var serviceLog = ServiceLog.Create(options.ServiceLogPath);
        var testFilePath = Path.GetFullPath(manifest.File);
        if (!File.Exists(testFilePath))
        {
            throw new FileNotFoundException("Service test file not found.", testFilePath);
        }

        var text = await File.ReadAllTextAsync(testFilePath, token);
        var markerLocator = new MarkerLocator(text);
        var uri = new Uri(testFilePath).AbsoluteUri;

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

        await Task.Delay(500, token);

        var textDocument = new { uri };
        var allOk = true;

        var tests = manifest.Tests;
        if (!string.IsNullOrWhiteSpace(options.ServiceTestId))
        {
            tests = tests
                .Where(test => string.Equals(test.Id, options.ServiceTestId, StringComparison.OrdinalIgnoreCase))
                .ToList();
        }

        if (tests.Count == 0)
        {
            Console.WriteLine("No matching service tests to run.");
            return true;
        }

        foreach (var test in tests)
        {
            if (!markerLocator.TryGetPosition(test, out var position, out var reason))
            {
                protocolLog.Write("error", $"Service test marker not found: {test.Marker} ({reason})");
                serviceLog.Write(new ServiceLogEntry
                {
                    Id = test.Id,
                    Method = test.Method,
                    Expectation = test.Expectation,
                    Outcome = "marker_not_found"
                });
                allOk = false;
                continue;
            }

            var ok = await ExecuteServiceTestAsync(rpc, textDocument, position, test, protocolLog, serviceLog, token);
            allOk &= ok;
        }

        await rpc.NotifyWithParameterObjectAsync("textDocument/didClose", new
        {
            textDocument = new { uri }
        }).WaitAsync(token);

        return allOk;
    }

    private static async Task<bool> ExecuteServiceTestAsync(
        JsonRpc rpc,
        object textDocument,
        TextPosition position,
        ServiceTestCase test,
        ProtocolLog protocolLog,
        ServiceLog serviceLog,
        CancellationToken token)
    {
        var method = test.Method;
        var expectation = test.Expectation ?? string.Empty;
        Console.WriteLine($"service: {test.Id} -> {method} ({expectation})");

        switch (method)
        {
            case "textDocument/completion":
            {
                var completion = await rpc
                    .InvokeWithParameterObjectAsync<JsonElement>(method, new
                    {
                        textDocument,
                        position = new { line = position.Line, character = position.Character }
                    })
                    .WaitAsync(token);
                var ok = EvaluateCompletion(completion, test, protocolLog, out var itemCount, out var expectedFound);
                serviceLog.Write(new ServiceLogEntry
                {
                    Id = test.Id,
                    Method = method,
                    Expectation = test.Expectation,
                    Outcome = ok ? "pass" : "fail",
                    Count = itemCount,
                    ExpectedFound = expectedFound
                });
                return ok;
            }
            case "textDocument/hover":
            {
                var hover = await rpc
                    .InvokeWithParameterObjectAsync<JsonElement>(method, new
                    {
                        textDocument,
                        position = new { line = position.Line, character = position.Character }
                    })
                    .WaitAsync(token);
                var ok = hover.ValueKind != JsonValueKind.Null && hover.ValueKind != JsonValueKind.Undefined;
                if (!ok)
                {
                    protocolLog.Write("error", $"Service test {test.Id} returned null hover.");
                }

                serviceLog.Write(new ServiceLogEntry
                {
                    Id = test.Id,
                    Method = method,
                    Expectation = test.Expectation,
                    Outcome = ok ? "pass" : "fail"
                });
                return ok;
            }
            case "textDocument/definition":
            {
                var definition = await rpc
                    .InvokeWithParameterObjectAsync<JsonElement>(method, new
                    {
                        textDocument,
                        position = new { line = position.Line, character = position.Character }
                    })
                    .WaitAsync(token);
                var ok = HasAnyResult(definition);
                if (!ok)
                {
                    protocolLog.Write("error", $"Service test {test.Id} returned empty definition.");
                }

                serviceLog.Write(new ServiceLogEntry
                {
                    Id = test.Id,
                    Method = method,
                    Expectation = test.Expectation,
                    Outcome = ok ? "pass" : "fail"
                });
                return ok;
            }
            case "textDocument/references":
            {
                var references = await rpc
                    .InvokeWithParameterObjectAsync<JsonElement>(method, new
                    {
                        textDocument,
                        position = new { line = position.Line, character = position.Character },
                        context = new { includeDeclaration = true }
                    })
                    .WaitAsync(token);
                var ok = references.ValueKind == JsonValueKind.Array && references.GetArrayLength() > 0;
                if (!ok)
                {
                    protocolLog.Write("error", $"Service test {test.Id} returned empty references.");
                }

                serviceLog.Write(new ServiceLogEntry
                {
                    Id = test.Id,
                    Method = method,
                    Expectation = test.Expectation,
                    Outcome = ok ? "pass" : "fail",
                    Count = references.ValueKind == JsonValueKind.Array ? references.GetArrayLength() : 0
                });
                return ok;
            }
            case "textDocument/rename":
            {
                var rename = await rpc
                    .InvokeWithParameterObjectAsync<JsonElement>(method, new
                    {
                        textDocument,
                        position = new { line = position.Line, character = position.Character },
                        newName = "RenamedValue"
                    })
                    .WaitAsync(token);
                var fileCount = CountWorkspaceEditFiles(rename);
                var ok = IsWorkspaceEdit(rename);
                var expectedFileCount = ParseWorkspaceEditFileCount(test.Expectation);
                if (expectedFileCount.HasValue && fileCount < expectedFileCount.Value)
                {
                    ok = false;
                    protocolLog.Write(
                        "error",
                        $"Service test {test.Id} returned workspace edit with {fileCount} file(s), expected at least {expectedFileCount.Value}.");
                }
                if (!ok)
                {
                    protocolLog.Write("error", $"Service test {test.Id} returned invalid workspace edit.");
                }

                serviceLog.Write(new ServiceLogEntry
                {
                    Id = test.Id,
                    Method = method,
                    Expectation = test.Expectation,
                    Outcome = ok ? "pass" : "fail",
                    Count = fileCount
                });
                return ok;
            }
            case "textDocument/documentSymbol":
            {
                var symbols = await rpc
                    .InvokeWithParameterObjectAsync<JsonElement>(method, new { textDocument })
                    .WaitAsync(token);
                var ok = HasSymbolResults(symbols);
                if (!ok)
                {
                    protocolLog.Write("error", $"Service test {test.Id} returned empty document symbols.");
                }

                serviceLog.Write(new ServiceLogEntry
                {
                    Id = test.Id,
                    Method = method,
                    Expectation = test.Expectation,
                    Outcome = ok ? "pass" : "fail",
                    Count = symbols.ValueKind == JsonValueKind.Array ? symbols.GetArrayLength() : 0
                });
                return ok;
            }
            case "workspace/symbol":
            {
                var symbols = await rpc
                    .InvokeWithParameterObjectAsync<JsonElement>(method, new { query = "Greeter" })
                    .WaitAsync(token);
                var ok = HasSymbolResults(symbols);
                if (!ok)
                {
                    protocolLog.Write("error", $"Service test {test.Id} returned empty workspace symbols.");
                }

                serviceLog.Write(new ServiceLogEntry
                {
                    Id = test.Id,
                    Method = method,
                    Expectation = test.Expectation,
                    Outcome = ok ? "pass" : "fail",
                    Count = symbols.ValueKind == JsonValueKind.Array ? symbols.GetArrayLength() : 0
                });
                return ok;
            }
            default:
                protocolLog.Write("error", $"Unsupported service method: {method}");
                serviceLog.Write(new ServiceLogEntry
                {
                    Id = test.Id,
                    Method = method,
                    Expectation = test.Expectation,
                    Outcome = "unsupported_method"
                });
                return false;
        }
    }

    private static bool EvaluateCompletion(
        JsonElement completion,
        ServiceTestCase test,
        ProtocolLog protocolLog,
        out int itemCount,
        out bool expectedFound)
    {
        expectedFound = true;
        var labels = CollectCompletionLabels(completion);
        itemCount = labels.Count;
        if (itemCount == 0)
        {
            protocolLog.Write("error", $"Service test {test.Id} returned empty completion.");
            return false;
        }

        if (test.Expectation != null && test.Expectation.StartsWith("contains:", StringComparison.OrdinalIgnoreCase))
        {
            var expected = test.Expectation.Substring("contains:".Length);
            expectedFound = labels.Any(label => string.Equals(label, expected, StringComparison.Ordinal));
            if (!expectedFound)
            {
                protocolLog.Write("error", $"Service test {test.Id} completion missing expected item: {expected}.");
            }

            return expectedFound;
        }

        return true;
    }

    private static HashSet<string> CollectCompletionLabels(JsonElement completion)
    {
        var labels = new HashSet<string>(StringComparer.Ordinal);

        if (completion.ValueKind == JsonValueKind.Array)
        {
            foreach (var item in completion.EnumerateArray())
            {
                if (item.TryGetProperty("label", out var labelElement) && labelElement.ValueKind == JsonValueKind.String)
                {
                    labels.Add(labelElement.GetString() ?? string.Empty);
                }
            }

            return labels;
        }

        if (completion.ValueKind == JsonValueKind.Object &&
            completion.TryGetProperty("items", out var itemsElement) &&
            itemsElement.ValueKind == JsonValueKind.Array)
        {
            foreach (var item in itemsElement.EnumerateArray())
            {
                if (item.TryGetProperty("label", out var labelElement) && labelElement.ValueKind == JsonValueKind.String)
                {
                    labels.Add(labelElement.GetString() ?? string.Empty);
                }
            }
        }

        return labels;
    }

    private static bool HasAnyResult(JsonElement result)
    {
        if (result.ValueKind == JsonValueKind.Array)
        {
            return result.GetArrayLength() > 0;
        }

        return result.ValueKind == JsonValueKind.Object;
    }

    private static bool HasSymbolResults(JsonElement symbols)
    {
        if (symbols.ValueKind == JsonValueKind.Array)
        {
            return symbols.GetArrayLength() > 0;
        }

        if (symbols.ValueKind == JsonValueKind.Object &&
            symbols.TryGetProperty("items", out var itemsElement) &&
            itemsElement.ValueKind == JsonValueKind.Array)
        {
            return itemsElement.GetArrayLength() > 0;
        }

        return false;
    }

    private static bool IsWorkspaceEdit(JsonElement edit)
    {
        if (edit.ValueKind != JsonValueKind.Object)
        {
            return false;
        }

        if (edit.TryGetProperty("changes", out var changesElement) &&
            changesElement.ValueKind == JsonValueKind.Object &&
            changesElement.EnumerateObject().Any())
        {
            return true;
        }

        if (edit.TryGetProperty("documentChanges", out var docChangesElement) &&
            docChangesElement.ValueKind == JsonValueKind.Array &&
            docChangesElement.GetArrayLength() > 0)
        {
            return true;
        }

        return false;
    }

    private static int CountWorkspaceEditFiles(JsonElement edit)
    {
        if (edit.ValueKind != JsonValueKind.Object)
        {
            return 0;
        }

        var files = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        if (edit.TryGetProperty("changes", out var changesElement) &&
            changesElement.ValueKind == JsonValueKind.Object)
        {
            foreach (var property in changesElement.EnumerateObject())
            {
                files.Add(property.Name);
            }
        }

        if (edit.TryGetProperty("documentChanges", out var docChangesElement) &&
            docChangesElement.ValueKind == JsonValueKind.Array)
        {
            foreach (var change in docChangesElement.EnumerateArray())
            {
                if (change.ValueKind != JsonValueKind.Object)
                {
                    continue;
                }

                if (change.TryGetProperty("textDocument", out var docElement) &&
                    docElement.ValueKind == JsonValueKind.Object &&
                    docElement.TryGetProperty("uri", out var uriElement) &&
                    uriElement.ValueKind == JsonValueKind.String)
                {
                    var uri = uriElement.GetString();
                    if (!string.IsNullOrWhiteSpace(uri))
                    {
                        files.Add(uri);
                    }
                }
            }
        }

        return files.Count;
    }

    private static int? ParseWorkspaceEditFileCount(string? expectation)
    {
        if (string.IsNullOrWhiteSpace(expectation))
        {
            return null;
        }

        const string prefix = "workspace_edit_files:";
        if (!expectation.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
        {
            return null;
        }

        var value = expectation.Substring(prefix.Length);
        if (int.TryParse(value, out var count))
        {
            return count;
        }

        return null;
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
        else if (arg == "--serviceTests")
        {
            options.ServiceTestsPath = "_test\\codex-tests\\vbnet-lsp\\fixtures\\services\\service-tests.json";
        }
        else if (arg == "--serviceManifest" && i + 1 < args.Length)
        {
            options.ServiceTestsPath = args[++i];
        }
        else if (arg == "--serviceTimeoutSeconds" && i + 1 < args.Length && int.TryParse(args[++i], out var serviceTimeout))
        {
            options.ServiceTimeoutSeconds = serviceTimeout;
        }
        else if (arg == "--serviceLog" && i + 1 < args.Length)
        {
            options.ServiceLogPath = args[++i];
        }
        else if (arg == "--serviceTestId" && i + 1 < args.Length)
        {
            options.ServiceTestId = args[++i];
        }
        else if (arg == "--protocolLog" && i + 1 < args.Length)
        {
            options.ProtocolLogPath = args[++i];
        }
        else if (arg == "--timingLog" && i + 1 < args.Length)
        {
            options.TimingLogPath = args[++i];
        }
        else if (arg == "--timingLabel" && i + 1 < args.Length)
        {
            options.TimingLabel = args[++i];
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

    private sealed class TimingLog
    {
        private readonly string _path;
        private readonly string _label;
        private readonly HashSet<string> _marks = new(StringComparer.OrdinalIgnoreCase);

        private TimingLog(string path, string label)
        {
            _path = path;
            _label = label;
        }

        public static TimingLog Create(string path, string label)
        {
            if (string.IsNullOrWhiteSpace(path))
            {
                return new TimingLog(string.Empty, label);
            }

            var fullPath = Path.GetFullPath(path);
            Directory.CreateDirectory(Path.GetDirectoryName(fullPath)!);
            return new TimingLog(fullPath, label);
        }

        public void Mark(string name, Stopwatch stopwatch)
        {
            if (string.IsNullOrWhiteSpace(_path))
            {
                return;
            }

            if (!_marks.Add(name))
            {
                return;
            }

            var payload = new
            {
                timestamp = DateTimeOffset.Now.ToString("o"),
                label = _label,
                name,
                elapsedMs = stopwatch.Elapsed.TotalMilliseconds
            };

            File.AppendAllText(_path, JsonSerializer.Serialize(payload) + Environment.NewLine);
        }

        public void TryMarkFromServerLine(string line, Stopwatch stopwatch)
        {
            if (string.IsNullOrWhiteSpace(line))
            {
                return;
            }

            if (line.Contains("VB.NET Language Server starting", StringComparison.OrdinalIgnoreCase))
            {
                Mark("server_starting", stopwatch);
            }
            else if (line.Contains("Loading solution", StringComparison.OrdinalIgnoreCase))
            {
                Mark("solution_loading", stopwatch);
            }
            else if (line.Contains("Solution loaded", StringComparison.OrdinalIgnoreCase))
            {
                Mark("solution_loaded", stopwatch);
            }
        }
    }

    private sealed class ServiceTestManifest
    {
        public string Workspace { get; set; } = string.Empty;
        public string File { get; set; } = string.Empty;
        public List<ServiceTestCase> Tests { get; set; } = new();

        public static ServiceTestManifest Load(string path)
        {
            if (string.IsNullOrWhiteSpace(path))
            {
                throw new InvalidOperationException("Service test manifest path is required.");
            }

            var fullPath = Path.GetFullPath(path);
            if (!System.IO.File.Exists(fullPath))
            {
                throw new FileNotFoundException("Service test manifest not found.", fullPath);
            }

            var json = System.IO.File.ReadAllText(fullPath);
            var manifest = JsonSerializer.Deserialize<ServiceTestManifest>(json, new JsonSerializerOptions
            {
                PropertyNameCaseInsensitive = true
            });

            if (manifest == null)
            {
                throw new InvalidOperationException("Failed to parse service test manifest.");
            }

            if (string.IsNullOrWhiteSpace(manifest.Workspace) || string.IsNullOrWhiteSpace(manifest.File))
            {
                throw new InvalidOperationException("Service test manifest missing workspace or file.");
            }

            return manifest;
        }
    }

    private sealed class ServiceTestCase
    {
        public string Id { get; set; } = string.Empty;
        public string Method { get; set; } = string.Empty;
        public string Marker { get; set; } = string.Empty;
        public string Expectation { get; set; } = string.Empty;
        public string Token { get; set; } = string.Empty;
        public int TokenOffset { get; set; }
    }

    private sealed class MarkerLocator
    {
        private readonly string _text;
        private readonly Dictionary<string, TextPosition> _cache = new(StringComparer.OrdinalIgnoreCase);

        public MarkerLocator(string text)
        {
            _text = text;
        }

        public bool TryGetPosition(ServiceTestCase test, out TextPosition position, out string reason)
        {
            position = default;
            reason = string.Empty;

            if (string.IsNullOrWhiteSpace(test.Marker))
            {
                reason = "marker_missing";
                return false;
            }

            var cacheKey = $"{test.Marker}|{test.Token}|{test.TokenOffset}";
            if (_cache.TryGetValue(cacheKey, out position))
            {
                return true;
            }

            var markerToken = $"' MARKER: {test.Marker}";
            var markerIndex = _text.IndexOf(markerToken, StringComparison.Ordinal);
            if (markerIndex < 0)
            {
                reason = "marker_not_found";
                return false;
            }

            var lineStart = _text.LastIndexOf('\n', markerIndex);
            var lineEnd = _text.IndexOf('\n', markerIndex);
            if (lineStart < 0)
            {
                lineStart = -1;
            }

            if (lineEnd < 0)
            {
                lineEnd = _text.Length;
            }

            if (!string.IsNullOrWhiteSpace(test.Token))
            {
                var lineText = _text.Substring(lineStart + 1, markerIndex - (lineStart + 1));
                var tokenIndex = lineText.IndexOf(test.Token, StringComparison.Ordinal);
                if (tokenIndex < 0)
                {
                    reason = "token_not_found";
                    return false;
                }

                var absoluteIndex = lineStart + 1 + tokenIndex + test.TokenOffset;
                position = GetPosition(_text, absoluteIndex);
                _cache[cacheKey] = position;
                return true;
            }

            position = GetPosition(_text, markerIndex);
            _cache[cacheKey] = position;
            return true;
        }
    }

    private readonly record struct TextPosition(int Line, int Character);

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

    private sealed class ServiceLog
    {
        private readonly string _path;

        private ServiceLog(string path)
        {
            _path = path;
        }

        public static ServiceLog Create(string path)
        {
            if (string.IsNullOrWhiteSpace(path))
            {
                return new ServiceLog(string.Empty);
            }

            var fullPath = Path.GetFullPath(path);
            Directory.CreateDirectory(Path.GetDirectoryName(fullPath)!);
            return new ServiceLog(fullPath);
        }

        public void Write(ServiceLogEntry entry)
        {
            if (string.IsNullOrWhiteSpace(_path))
            {
                return;
            }

            entry.Timestamp = DateTimeOffset.Now.ToString("o");
            var payload = JsonSerializer.Serialize(entry);
            File.AppendAllText(_path, payload + Environment.NewLine);
        }
    }

    private sealed class ServiceLogEntry
    {
        public string Timestamp { get; set; } = string.Empty;
        public string Id { get; set; } = string.Empty;
        public string Method { get; set; } = string.Empty;
        public string Expectation { get; set; } = string.Empty;
        public string Outcome { get; set; } = string.Empty;
        public int? Count { get; set; }
        public bool? ExpectedFound { get; set; }
    }
}
