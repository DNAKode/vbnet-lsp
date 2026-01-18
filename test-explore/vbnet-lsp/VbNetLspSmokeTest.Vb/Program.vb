Imports System
Imports System.Collections.Generic
Imports System.Diagnostics
Imports System.IO
Imports System.IO.Pipes
Imports System.Linq
Imports System.Text
Imports System.Text.Json
Imports System.Threading
Imports System.Threading.Tasks
Imports StreamJsonRpc
Imports StreamJsonRpc.Protocol

Friend NotInheritable Class Options
    Public Property ServerPath As String = String.Empty
    Public Property DotnetPath As String = "dotnet"
    Public Property LogLevel As String = "Information"
    Public Property Transport As String = "pipe"
    Public Property TimeoutSeconds As Integer = 30
    Public Property DiagnosticsTimeoutSeconds As Integer = 30
    Public Property WorkspaceLoadDelaySeconds As Integer = 3
    Public Property RootPath As String = String.Empty
    Public Property TestFilePath As String = String.Empty
    Public Property ExpectDiagnostics As Boolean
    Public Property DiagnosticsMode As String = String.Empty
    Public Property DebounceMs As Integer?
    Public Property ExpectedDiagnosticCode As String = String.Empty
    Public Property SendDidSave As Boolean
    Public Property ServiceTestsPath As String = String.Empty
    Public Property ServiceTimeoutSeconds As Integer = 60
    Public Property ServiceLogPath As String = String.Empty
    Public Property ServiceTestId As String = String.Empty
    Public Property ProtocolLogPath As String = String.Empty
    Public Property TimingLogPath As String = String.Empty
    Public Property TimingLabel As String = String.Empty
End Class

Friend Module Program
    Public Sub Main(args As String())
        Dim exitCode = MainAsync(args).GetAwaiter().GetResult()
        Environment.ExitCode = exitCode
    End Sub

    Private Async Function MainAsync(args As String()) As Task(Of Integer)
        Dim options = ParseArgs(args)
        If String.IsNullOrWhiteSpace(options.ServerPath) Then
            Console.Error.WriteLine("Missing required --serverPath argument.")
            Return 2
        End If

        Dim protocolLog As ProtocolLog = ProtocolLog.Create(options.ProtocolLogPath, "vbnet-smoke")
        Dim timingLog As TimingLog = TimingLog.Create(options.TimingLogPath, options.TimingLabel)
        Dim stopwatch As Stopwatch = Stopwatch.StartNew()
        Dim process = StartServer(options, timingLog, stopwatch)
        If process Is Nothing Then
            protocolLog.Write("error", "Failed to start VB.NET language server.")
            Return 3
        End If

        Dim totalTimeoutSeconds = options.TimeoutSeconds
        If Not String.IsNullOrWhiteSpace(options.ServiceTestsPath) Then
            totalTimeoutSeconds += options.ServiceTimeoutSeconds
        End If

        Using cts As New CancellationTokenSource(TimeSpan.FromSeconds(totalTimeoutSeconds))
            Dim exitCode = Await RunLspHandshakeAsync(process, options, protocolLog, timingLog, stopwatch, cts.Token)

            Try
                If Not process.HasExited Then
                    process.Kill(entireProcessTree:=True)
                End If
            Catch
                ' Best-effort cleanup only.
            End Try

            Return exitCode
        End Using
    End Function

    Private Function StartServer(options As Options, timingLog As TimingLog, stopwatch As Stopwatch) As Process
        Dim args As New List(Of String) From {
            Quote(options.ServerPath)
        }

        If String.Equals(options.Transport, "stdio", StringComparison.OrdinalIgnoreCase) Then
            args.Add("--stdio")
        Else
            args.Add("--pipe")
        End If

        args.Add("--logLevel")
        args.Add(options.LogLevel)

        Dim startInfo As New ProcessStartInfo With {
            .FileName = options.DotnetPath,
            .Arguments = String.Join(" ", args),
            .RedirectStandardInput = True,
            .RedirectStandardOutput = True,
            .RedirectStandardError = True,
            .UseShellExecute = False,
            .CreateNoWindow = True
        }

        Dim process As New Process With {.StartInfo = startInfo}
        AddHandler process.ErrorDataReceived, Sub(unused, e)
                                                  If Not String.IsNullOrWhiteSpace(e.Data) Then
                                                      Console.Error.WriteLine($"[server stderr] {e.Data}")
                                                      timingLog.TryMarkFromServerLine(e.Data, stopwatch)
                                                  End If
                                              End Sub

        If Not process.Start() Then
            Console.Error.WriteLine("Failed to start VB.NET language server.")
            Return Nothing
        End If

        process.BeginErrorReadLine()
        Return process
    End Function

    Private Async Function RunLspHandshakeAsync(
        process As Process,
        options As Options,
        protocolLog As ProtocolLog,
        timingLog As TimingLog,
        stopwatch As Stopwatch,
        token As CancellationToken) As Task(Of Integer)

        Dim formatter As New SystemTextJsonFormatter()
        formatter.JsonSerializerOptions.PropertyNamingPolicy = JsonNamingPolicy.CamelCase
        formatter.JsonSerializerOptions.DefaultIgnoreCondition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull

        Dim inputStream As Stream
        Dim outputStream As Stream

        If String.Equals(options.Transport, "pipe", StringComparison.OrdinalIgnoreCase) Then
            Dim pipeName = Await ReadPipeNameAsync(process, token)
            Using pipeStream = ConnectToPipe(pipeName)
                inputStream = pipeStream
                outputStream = pipeStream
                Return Await RunRpcOverStreamAsync(inputStream, outputStream, formatter, options, protocolLog, timingLog, stopwatch, token)
            End Using
        End If

        inputStream = process.StandardOutput.BaseStream
        outputStream = process.StandardInput.BaseStream
        Return Await RunRpcOverStreamAsync(inputStream, outputStream, formatter, options, protocolLog, timingLog, stopwatch, token)
    End Function

    Private Async Function RunRpcOverStreamAsync(
        inputStream As Stream,
        outputStream As Stream,
        formatter As SystemTextJsonFormatter,
        options As Options,
        protocolLog As ProtocolLog,
        timingLog As TimingLog,
        stopwatch As Stopwatch,
        token As CancellationToken) As Task(Of Integer)

        Dim handler = New HeaderDelimitedMessageHandler(
            System.IO.Pipelines.PipeWriter.Create(outputStream),
            System.IO.Pipelines.PipeReader.Create(inputStream),
            formatter)

        Dim diagnosticsWaiter As DiagnosticsWaiter = Nothing

        Using rpc As New JsonRpc(handler)
            If options.ExpectDiagnostics AndAlso Not String.IsNullOrWhiteSpace(options.TestFilePath) Then
                diagnosticsWaiter = New DiagnosticsWaiter(New Uri(Path.GetFullPath(options.TestFilePath)).AbsoluteUri)
            End If

            Dim settingsPayload = BuildSettingsPayload(options)
            rpc.AddLocalRpcTarget(New ClientHandlers(settingsPayload, diagnosticsWaiter, options, protocolLog))
            rpc.StartListening()

            Dim workspaceRoot = If(String.IsNullOrWhiteSpace(options.RootPath), Nothing, Path.GetFullPath(options.RootPath))
            Dim rootUri = If(workspaceRoot Is Nothing, Nothing, New Uri(workspaceRoot))
            Dim workspaceInit = If(workspaceRoot Is Nothing, Nothing, New With {
                .projectSearchPaths = New String() {workspaceRoot},
                .excludePaths = New String() {".git", "bin", "obj", "_external", "test-explore", "test"},
                .ignoreSolutionFiles = True,
                .maxProjectResults = 25
            })

            Dim initParams = New With {
                .processId = Environment.ProcessId,
                .rootUri = If(rootUri Is Nothing, Nothing, rootUri.AbsoluteUri),
                .capabilities = New Dictionary(Of String, Object)(),
                .clientInfo = New With {.name = "CodexVbNetLspSmokeTest", .version = "0.1"},
                .initializationOptions = If(workspaceInit Is Nothing, Nothing, New With {.workspace = workspaceInit})
            }

            Try
                Dim initializeResult = Await rpc.
                    InvokeWithParameterObjectAsync(Of JsonElement)("initialize", initParams).
                    WaitAsync(token)
                Console.WriteLine($"initialize: {initializeResult.ValueKind}")
                If initializeResult.ValueKind = JsonValueKind.Null OrElse initializeResult.ValueKind = JsonValueKind.Undefined Then
                    protocolLog.Write("error", "initialize returned null/undefined.")
                End If
                timingLog.Mark("initialize_response", stopwatch)

                Await rpc.NotifyWithParameterObjectAsync("initialized", New Dictionary(Of String, Object)()).WaitAsync(token)
                If settingsPayload IsNot Nothing Then
                    Await rpc.NotifyWithParameterObjectAsync("workspace/didChangeConfiguration", New With {
                        .settings = settingsPayload
                    }).WaitAsync(token)
                End If

                If Not String.IsNullOrWhiteSpace(options.TestFilePath) Then
                    Dim diagnosticsReceived = Await RunDocumentWorkflowAsync(rpc, options, diagnosticsWaiter, timingLog, stopwatch, token)
                    If options.ExpectDiagnostics AndAlso Not diagnosticsReceived Then
                        Console.Error.WriteLine("Expected diagnostics but none were received.")
                        protocolLog.Write("error", "Expected diagnostics but none were received.")
                        Return 6
                    End If
                End If

                If Not String.IsNullOrWhiteSpace(options.ServiceTestsPath) Then
                    Console.WriteLine("Running service tests...")
                    Using serviceCts As New CancellationTokenSource(TimeSpan.FromSeconds(options.ServiceTimeoutSeconds))
                        Dim servicesOk = Await RunServiceTestsAsync(rpc, options, protocolLog, serviceCts.Token)
                        If Not servicesOk Then
                            protocolLog.Write("error", "One or more service tests failed.")
                            Return 7
                        End If
                    End Using
                End If

                Try
                    Await rpc.InvokeWithParameterObjectAsync(Of JsonElement)("shutdown", New Dictionary(Of String, Object)()).WaitAsync(token)
                    Await rpc.NotifyWithParameterObjectAsync("exit", New Dictionary(Of String, Object)()).WaitAsync(token)
                Catch ex As Exception When IsConnectionLost(ex)
                    Console.Error.WriteLine("Connection lost during shutdown; treating as graceful exit for scaffold server.")
                End Try

                Return 0
            Catch ex As OperationCanceledException
                Console.Error.WriteLine("Handshake timed out.")
                protocolLog.Write("error", "Handshake timed out.")
                Return 4
            Catch ex As Exception
                Console.Error.WriteLine($"Handshake failed: {ex.Message}")
                protocolLog.Write("error", $"Handshake failed: {ex.Message}")
                Return 5
            End Try
        End Using
    End Function

    Private Function IsConnectionLost(ex As Exception) As Boolean
        If ex.Message.Contains("connection with the remote party was lost", StringComparison.OrdinalIgnoreCase) Then
            Return True
        End If

        If ex.InnerException Is Nothing Then
            Return False
        End If

        Return IsConnectionLost(ex.InnerException)
    End Function

    Private Async Function RunDocumentWorkflowAsync(
        rpc As JsonRpc,
        options As Options,
        diagnosticsWaiter As DiagnosticsWaiter,
        timingLog As TimingLog,
        stopwatch As Stopwatch,
        token As CancellationToken) As Task(Of Boolean)

        Dim fullPath = Path.GetFullPath(options.TestFilePath)
        If Not File.Exists(fullPath) Then
            Throw New FileNotFoundException("Test file not found.", fullPath)
        End If

        Dim uri = New Uri(fullPath).AbsoluteUri
        Dim text = Await File.ReadAllTextAsync(fullPath, token)

        If options.ExpectDiagnostics AndAlso options.WorkspaceLoadDelaySeconds > 0 Then
            Console.WriteLine($"Waiting {options.WorkspaceLoadDelaySeconds}s for workspace load...")
            Await Task.Delay(TimeSpan.FromSeconds(options.WorkspaceLoadDelaySeconds), token)
        End If

        Console.WriteLine("Sending didOpen...")
        Await rpc.NotifyWithParameterObjectAsync("textDocument/didOpen", New With {
            .textDocument = New With {
                .uri = uri,
                .languageId = "vb",
                .version = 1,
                .text = text
            }
        }).WaitAsync(token)
        timingLog.Mark("didOpen_sent", stopwatch)

        Dim updatedText = text & Environment.NewLine
        Console.WriteLine("Sending didChange...")
        Await rpc.NotifyWithParameterObjectAsync("textDocument/didChange", New With {
            .textDocument = New With {.uri = uri, .version = 2},
            .contentChanges = New Object() {
                New With {.text = updatedText}
            }
        }).WaitAsync(token)

        If options.SendDidSave Then
            Console.WriteLine("Sending didSave...")
            Await rpc.NotifyWithParameterObjectAsync("textDocument/didSave", New With {
                .textDocument = New With {.uri = uri},
                .text = updatedText
            }).WaitAsync(token)
        End If

        Dim diagnosticsReceived = True
        If diagnosticsWaiter IsNot Nothing Then
            diagnosticsReceived = Await WaitForDiagnosticsAsync(diagnosticsWaiter.Tcs.Task, options.DiagnosticsTimeoutSeconds, token)
            If Not diagnosticsReceived Then
                Console.WriteLine("Diagnostics not received; retrying after workspace delay...")
                Await rpc.NotifyWithParameterObjectAsync("textDocument/didClose", New With {
                    .textDocument = New With {.uri = uri}
                }).WaitAsync(token)

                diagnosticsWaiter.Reset()
                If options.WorkspaceLoadDelaySeconds > 0 Then
                    Await Task.Delay(TimeSpan.FromSeconds(options.WorkspaceLoadDelaySeconds), token)
                End If

                Console.WriteLine("Re-sending didOpen...")
                Await rpc.NotifyWithParameterObjectAsync("textDocument/didOpen", New With {
                    .textDocument = New With {
                        .uri = uri,
                        .languageId = "vb",
                        .version = 3,
                        .text = updatedText
                    }
                }).WaitAsync(token)

                Console.WriteLine("Re-sending didChange...")
                Await rpc.NotifyWithParameterObjectAsync("textDocument/didChange", New With {
                    .textDocument = New With {.uri = uri, .version = 4},
                    .contentChanges = New Object() {
                        New With {.text = updatedText}
                    }
                }).WaitAsync(token)

                If options.SendDidSave Then
                    Console.WriteLine("Re-sending didSave...")
                    Await rpc.NotifyWithParameterObjectAsync("textDocument/didSave", New With {
                        .textDocument = New With {.uri = uri},
                        .text = updatedText
                    }).WaitAsync(token)
                End If

                diagnosticsReceived = Await WaitForDiagnosticsAsync(diagnosticsWaiter.Tcs.Task, options.DiagnosticsTimeoutSeconds, token)
            End If
        End If

        Await rpc.NotifyWithParameterObjectAsync("textDocument/didClose", New With {
            .textDocument = New With {.uri = uri}
        }).WaitAsync(token)

        Return diagnosticsReceived
    End Function

    Private Async Function RunServiceTestsAsync(
        rpc As JsonRpc,
        options As Options,
        protocolLog As ProtocolLog,
        token As CancellationToken) As Task(Of Boolean)

        Dim manifest = ServiceTestManifest.Load(options.ServiceTestsPath)
        If manifest.Tests.Count = 0 Then
            Console.WriteLine("Service test manifest has no tests.")
            Return True
        End If

        Dim serviceLog As ServiceLog = ServiceLog.Create(options.ServiceLogPath)
        Dim testFilePath = Path.GetFullPath(manifest.File)
        If Not File.Exists(testFilePath) Then
            Throw New FileNotFoundException("Service test file not found.", testFilePath)
        End If

        Dim text = Await File.ReadAllTextAsync(testFilePath, token)
        Dim markerLocator = New MarkerLocator(text)
        Dim uri = New Uri(testFilePath).AbsoluteUri

        Await rpc.NotifyWithParameterObjectAsync("textDocument/didOpen", New With {
            .textDocument = New With {
                .uri = uri,
                .languageId = "vb",
                .version = 1,
                .text = text
            }
        }).WaitAsync(token)

        Await Task.Delay(500, token)
        Await WaitForWorkspaceReadyAsync(rpc, protocolLog, token)

        Dim textDocument = New With {.uri = uri}
        Dim allOk = True

        Dim tests = manifest.Tests
        If Not String.IsNullOrWhiteSpace(options.ServiceTestId) Then
            tests = tests.Where(Function(test) String.Equals(test.Id, options.ServiceTestId, StringComparison.OrdinalIgnoreCase)).ToList()
        End If

        If tests.Count = 0 Then
            Console.WriteLine("No matching service tests to run.")
            Return True
        End If

        For Each test In tests
            Dim position As TextPosition
            Dim reason As String = String.Empty
            If Not markerLocator.TryGetPosition(test, position, reason) Then
                protocolLog.Write("error", $"Service test marker not found: {test.Marker} ({reason})")
                serviceLog.Write(New ServiceLogEntry With {
                    .Id = test.Id,
                    .Method = test.Method,
                    .Expectation = test.Expectation,
                    .Outcome = "marker_not_found"
                })
                allOk = False
                Continue For
            End If

            Dim ok = False
            For attempt = 1 To 2
                ok = Await ExecuteServiceTestAsync(rpc, textDocument, position, test, protocolLog, serviceLog, token)
                If ok OrElse attempt = 2 Then
                    Exit For
                End If

                protocolLog.Write("warn", $"Service test {test.Id} failed on attempt {attempt}; retrying.")
                Await Task.Delay(500, token)
            Next

            allOk = allOk AndAlso ok
        Next

        Await rpc.NotifyWithParameterObjectAsync("textDocument/didClose", New With {
            .textDocument = New With {.uri = uri}
        }).WaitAsync(token)

        Return allOk
    End Function

    Private Async Function WaitForWorkspaceReadyAsync(rpc As JsonRpc, protocolLog As ProtocolLog, token As CancellationToken) As Task
        Dim deadline = DateTimeOffset.UtcNow + TimeSpan.FromSeconds(15)
        While DateTimeOffset.UtcNow < deadline AndAlso Not token.IsCancellationRequested
            Dim symbols = Await rpc.InvokeWithParameterObjectAsync(Of JsonElement)("workspace/symbol", New With {.query = "Greeter"}).WaitAsync(token)

            If symbols.ValueKind = JsonValueKind.Array AndAlso symbols.GetArrayLength() > 0 Then
                Return
            End If

            Await Task.Delay(500, token)
        End While

        protocolLog.Write("warn", "Service readiness check timed out; proceeding with service tests.")
    End Function

    Private Async Function ExecuteServiceTestAsync(
        rpc As JsonRpc,
        textDocument As Object,
        position As TextPosition,
        test As ServiceTestCase,
        protocolLog As ProtocolLog,
        serviceLog As ServiceLog,
        token As CancellationToken) As Task(Of Boolean)

        Dim method = test.Method
        Dim expectation = If(test.Expectation, String.Empty)
        Console.WriteLine($"service: {test.Id} -> {method} ({expectation})")

        Select Case method
            Case "textDocument/completion"
                Dim completion = Await rpc.InvokeWithParameterObjectAsync(Of JsonElement)(method, New With {
                    textDocument,
                    .position = New With {.line = position.Line, .character = position.Character}
                }).WaitAsync(token)

                Dim itemCount As Integer
                Dim expectedFound As Boolean
                Dim ok = EvaluateCompletion(completion, test, protocolLog, itemCount, expectedFound)
                serviceLog.Write(New ServiceLogEntry With {
                    .Id = test.Id,
                    .Method = method,
                    .Expectation = test.Expectation,
                    .Outcome = If(ok, "pass", "fail"),
                    .Count = itemCount,
                    .ExpectedFound = expectedFound
                })
                Return ok

            Case "textDocument/hover"
                Dim hover = Await rpc.InvokeWithParameterObjectAsync(Of JsonElement)(method, New With {
                    textDocument,
                    .position = New With {.line = position.Line, .character = position.Character}
                }).WaitAsync(token)

                Dim ok = hover.ValueKind <> JsonValueKind.Null AndAlso hover.ValueKind <> JsonValueKind.Undefined
                If Not ok Then
                    protocolLog.Write("error", $"Service test {test.Id} returned null hover.")
                End If

                serviceLog.Write(New ServiceLogEntry With {
                    .Id = test.Id,
                    .Method = method,
                    .Expectation = test.Expectation,
                    .Outcome = If(ok, "pass", "fail")
                })
                Return ok

            Case "textDocument/definition"
                Dim definition = Await rpc.InvokeWithParameterObjectAsync(Of JsonElement)(method, New With {
                    textDocument,
                    .position = New With {.line = position.Line, .character = position.Character}
                }).WaitAsync(token)

                Dim ok = HasAnyResult(definition)
                If Not ok Then
                    protocolLog.Write("error", $"Service test {test.Id} returned empty definition.")
                End If

                serviceLog.Write(New ServiceLogEntry With {
                    .Id = test.Id,
                    .Method = method,
                    .Expectation = test.Expectation,
                    .Outcome = If(ok, "pass", "fail")
                })
                Return ok

            Case "textDocument/references"
                Dim references = Await rpc.InvokeWithParameterObjectAsync(Of JsonElement)(method, New With {
                    textDocument,
                    .position = New With {.line = position.Line, .character = position.Character},
                    .context = New With {.includeDeclaration = True}
                }).WaitAsync(token)

                Dim ok = references.ValueKind = JsonValueKind.Array AndAlso references.GetArrayLength() > 0
                If Not ok Then
                    protocolLog.Write("error", $"Service test {test.Id} returned empty references.")
                End If

                serviceLog.Write(New ServiceLogEntry With {
                    .Id = test.Id,
                    .Method = method,
                    .Expectation = test.Expectation,
                    .Outcome = If(ok, "pass", "fail"),
                    .Count = If(references.ValueKind = JsonValueKind.Array, references.GetArrayLength(), 0)
                })
                Return ok

            Case "textDocument/rename"
                Dim rename = Await rpc.InvokeWithParameterObjectAsync(Of JsonElement)(method, New With {
                    textDocument,
                    .position = New With {.line = position.Line, .character = position.Character},
                    .newName = "RenamedValue"
                }).WaitAsync(token)

                Dim fileCount = CountWorkspaceEditFiles(rename)
                Dim ok = IsWorkspaceEdit(rename)
                Dim expectedFileCount = ParseWorkspaceEditFileCount(test.Expectation)
                If expectedFileCount.HasValue AndAlso fileCount < expectedFileCount.Value Then
                    ok = False
                    protocolLog.Write("error", $"Service test {test.Id} returned workspace edit with {fileCount} file(s), expected at least {expectedFileCount.Value}.")
                End If
                If Not ok Then
                    protocolLog.Write("error", $"Service test {test.Id} returned invalid workspace edit.")
                End If

                serviceLog.Write(New ServiceLogEntry With {
                    .Id = test.Id,
                    .Method = method,
                    .Expectation = test.Expectation,
                    .Outcome = If(ok, "pass", "fail"),
                    .Count = fileCount
                })
                Return ok

            Case "textDocument/documentSymbol"
                Dim symbols = Await rpc.InvokeWithParameterObjectAsync(Of JsonElement)(method, New With {textDocument}).WaitAsync(token)
                Dim ok = HasSymbolResults(symbols)
                If Not ok Then
                    protocolLog.Write("error", $"Service test {test.Id} returned empty document symbols.")
                End If

                serviceLog.Write(New ServiceLogEntry With {
                    .Id = test.Id,
                    .Method = method,
                    .Expectation = test.Expectation,
                    .Outcome = If(ok, "pass", "fail"),
                    .Count = If(symbols.ValueKind = JsonValueKind.Array, symbols.GetArrayLength(), 0)
                })
                Return ok

            Case "workspace/symbol"
                Dim symbols = Await rpc.InvokeWithParameterObjectAsync(Of JsonElement)(method, New With {.query = "Greeter"}).WaitAsync(token)
                Dim ok = HasSymbolResults(symbols)
                If Not ok Then
                    protocolLog.Write("error", $"Service test {test.Id} returned empty workspace symbols.")
                End If

                serviceLog.Write(New ServiceLogEntry With {
                    .Id = test.Id,
                    .Method = method,
                    .Expectation = test.Expectation,
                    .Outcome = If(ok, "pass", "fail"),
                    .Count = If(symbols.ValueKind = JsonValueKind.Array, symbols.GetArrayLength(), 0)
                })
                Return ok

            Case Else
                protocolLog.Write("error", $"Unsupported service method: {method}")
                serviceLog.Write(New ServiceLogEntry With {
                    .Id = test.Id,
                    .Method = method,
                    .Expectation = test.Expectation,
                    .Outcome = "unsupported_method"
                })
                Return False
        End Select
    End Function

    Private Function EvaluateCompletion(
        completion As JsonElement,
        test As ServiceTestCase,
        protocolLog As ProtocolLog,
        ByRef itemCount As Integer,
        ByRef expectedFound As Boolean) As Boolean

        expectedFound = True
        Dim labels = CollectCompletionLabels(completion)
        itemCount = labels.Count
        If itemCount = 0 Then
            protocolLog.Write("error", $"Service test {test.Id} returned empty completion.")
            Return False
        End If

        If test.Expectation IsNot Nothing AndAlso test.Expectation.StartsWith("contains:", StringComparison.OrdinalIgnoreCase) Then
            Dim expected = test.Expectation.Substring("contains:".Length)
            expectedFound = labels.Any(Function(label) String.Equals(label, expected, StringComparison.Ordinal))
            If Not expectedFound Then
                protocolLog.Write("error", $"Service test {test.Id} completion missing expected item: {expected}.")
            End If

            Return expectedFound
        End If

        Return True
    End Function

    Private Function CollectCompletionLabels(completion As JsonElement) As HashSet(Of String)
        Dim labels As New HashSet(Of String)(StringComparer.Ordinal)

        If completion.ValueKind = JsonValueKind.Array Then
            For Each item In completion.EnumerateArray()
                Dim labelElement As JsonElement
                If item.TryGetProperty("label", labelElement) AndAlso labelElement.ValueKind = JsonValueKind.String Then
                    labels.Add(If(labelElement.GetString(), String.Empty))
                End If
            Next

            Return labels
        End If

        Dim itemsElement As JsonElement
        If completion.ValueKind = JsonValueKind.Object AndAlso
            completion.TryGetProperty("items", itemsElement) AndAlso
            itemsElement.ValueKind = JsonValueKind.Array Then

            For Each item In itemsElement.EnumerateArray()
                Dim labelElement As JsonElement
                If item.TryGetProperty("label", labelElement) AndAlso labelElement.ValueKind = JsonValueKind.String Then
                    labels.Add(If(labelElement.GetString(), String.Empty))
                End If
            Next
        End If

        Return labels
    End Function

    Private Function HasAnyResult(result As JsonElement) As Boolean
        If result.ValueKind = JsonValueKind.Array Then
            Return result.GetArrayLength() > 0
        End If

        Return result.ValueKind = JsonValueKind.Object
    End Function

    Private Function HasSymbolResults(symbols As JsonElement) As Boolean
        If symbols.ValueKind = JsonValueKind.Array Then
            Return symbols.GetArrayLength() > 0
        End If

        Dim itemsElement As JsonElement
        If symbols.ValueKind = JsonValueKind.Object AndAlso
            symbols.TryGetProperty("items", itemsElement) AndAlso
            itemsElement.ValueKind = JsonValueKind.Array Then
            Return itemsElement.GetArrayLength() > 0
        End If

        Return False
    End Function

    Private Function IsWorkspaceEdit(edit As JsonElement) As Boolean
        If edit.ValueKind <> JsonValueKind.Object Then
            Return False
        End If

        Dim changesElement As JsonElement
        If edit.TryGetProperty("changes", changesElement) AndAlso
            changesElement.ValueKind = JsonValueKind.Object AndAlso
            changesElement.EnumerateObject().Any() Then
            Return True
        End If

        Dim docChangesElement As JsonElement
        If edit.TryGetProperty("documentChanges", docChangesElement) AndAlso
            docChangesElement.ValueKind = JsonValueKind.Array AndAlso
            docChangesElement.GetArrayLength() > 0 Then
            Return True
        End If

        Return False
    End Function

    Private Function CountWorkspaceEditFiles(edit As JsonElement) As Integer
        If edit.ValueKind <> JsonValueKind.Object Then
            Return 0
        End If

        Dim files As New HashSet(Of String)(StringComparer.OrdinalIgnoreCase)

        Dim changesElement As JsonElement
        If edit.TryGetProperty("changes", changesElement) AndAlso changesElement.ValueKind = JsonValueKind.Object Then
            For Each prop In changesElement.EnumerateObject()
                files.Add(prop.Name)
            Next
        End If

        Dim docChangesElement As JsonElement
        If edit.TryGetProperty("documentChanges", docChangesElement) AndAlso docChangesElement.ValueKind = JsonValueKind.Array Then
            For Each change In docChangesElement.EnumerateArray()
                If change.ValueKind <> JsonValueKind.Object Then
                    Continue For
                End If

                Dim docElement As JsonElement
                Dim uriElement As JsonElement
                If change.TryGetProperty("textDocument", docElement) AndAlso
                    docElement.ValueKind = JsonValueKind.Object AndAlso
                    docElement.TryGetProperty("uri", uriElement) AndAlso
                    uriElement.ValueKind = JsonValueKind.String Then

                    Dim uri = uriElement.GetString()
                    If Not String.IsNullOrWhiteSpace(uri) Then
                        files.Add(uri)
                    End If
                End If
            Next
        End If

        Return files.Count
    End Function

    Private Function ParseWorkspaceEditFileCount(expectation As String) As Integer?
        If String.IsNullOrWhiteSpace(expectation) Then
            Return Nothing
        End If

        Const prefix As String = "workspace_edit_files:"
        If Not expectation.StartsWith(prefix, StringComparison.OrdinalIgnoreCase) Then
            Return Nothing
        End If

        Dim value = expectation.Substring(prefix.Length)
        Dim count As Integer
        If Integer.TryParse(value, count) Then
            Return count
        End If

        Return Nothing
    End Function

    Private Async Function ReadPipeNameAsync(process As Process, token As CancellationToken) As Task(Of String)
        Dim regex As New System.Text.RegularExpressions.Regex("\{""pipeName"":""[^""]+""\}")
        Dim stdoutStream = process.StandardOutput.BaseStream
        Dim buffer(4095) As Byte
        Dim builder As New StringBuilder()

        While Not token.IsCancellationRequested
            Dim bytesRead = Await stdoutStream.ReadAsync(buffer, 0, buffer.Length, CancellationToken.None)
            If bytesRead = 0 Then
                Throw New InvalidOperationException("Server stdout closed before pipe name was received.")
            End If

            Dim text = Encoding.UTF8.GetString(buffer, 0, bytesRead)
            builder.Append(text)

            Dim match = regex.Match(builder.ToString())
            If match.Success Then
                Using doc = JsonDocument.Parse(match.Value)
                    Dim pipeElement As JsonElement
                    If doc.RootElement.TryGetProperty("pipeName", pipeElement) Then
                        Dim pipeName = pipeElement.GetString()
                        If String.IsNullOrWhiteSpace(pipeName) Then
                            Throw New InvalidOperationException("Pipe name was empty.")
                        End If
                        Return pipeName
                    End If
                End Using
            End If
        End While

        Throw New OperationCanceledException("Timed out waiting for pipe name.")
    End Function

    Private Function ConnectToPipe(pipeName As String) As NamedPipeClientStream
        Const windowsPrefix As String = "\\.\pipe\"
        If pipeName.StartsWith(windowsPrefix, StringComparison.OrdinalIgnoreCase) Then
            pipeName = pipeName.Substring(windowsPrefix.Length)
        End If

        Dim pipeStream As New NamedPipeClientStream(
            ".",
            pipeName,
            PipeDirection.InOut,
            PipeOptions.Asynchronous)

        pipeStream.Connect(TimeSpan.FromSeconds(10))
        If Not pipeStream.IsConnected Then
            Throw New InvalidOperationException("Failed to connect to named pipe.")
        End If

        Return pipeStream
    End Function

    Private Function ParseArgs(args As String()) As Options
        Dim options As New Options()

        Dim i = 0
        While i < args.Length
            Dim arg = args(i)
            If arg = "--serverPath" AndAlso i + 1 < args.Length Then
                i += 1
                options.ServerPath = args(i)
            ElseIf arg = "--dotnetPath" AndAlso i + 1 < args.Length Then
                i += 1
                options.DotnetPath = args(i)
            ElseIf arg = "--logLevel" AndAlso i + 1 < args.Length Then
                i += 1
                options.LogLevel = args(i)
            ElseIf arg = "--transport" AndAlso i + 1 < args.Length Then
                i += 1
                options.Transport = args(i)
            ElseIf arg = "--rootPath" AndAlso i + 1 < args.Length Then
                i += 1
                options.RootPath = args(i)
            ElseIf arg = "--testFile" AndAlso i + 1 < args.Length Then
                i += 1
                options.TestFilePath = args(i)
            ElseIf arg = "--timeoutSeconds" AndAlso i + 1 < args.Length Then
                Dim timeout As Integer
                If Integer.TryParse(args(i + 1), timeout) Then
                    i += 1
                    options.TimeoutSeconds = timeout
                End If
            ElseIf arg = "--diagnosticsTimeoutSeconds" AndAlso i + 1 < args.Length Then
                Dim diagTimeout As Integer
                If Integer.TryParse(args(i + 1), diagTimeout) Then
                    i += 1
                    options.DiagnosticsTimeoutSeconds = diagTimeout
                End If
            ElseIf arg = "--workspaceLoadDelaySeconds" AndAlso i + 1 < args.Length Then
                Dim delay As Integer
                If Integer.TryParse(args(i + 1), delay) Then
                    i += 1
                    options.WorkspaceLoadDelaySeconds = delay
                End If
            ElseIf arg = "--expectDiagnostics" Then
                options.ExpectDiagnostics = True
            ElseIf arg = "--diagnosticsMode" AndAlso i + 1 < args.Length Then
                i += 1
                options.DiagnosticsMode = args(i)
            ElseIf arg = "--debounceMs" AndAlso i + 1 < args.Length Then
                Dim debounceMs As Integer
                If Integer.TryParse(args(i + 1), debounceMs) Then
                    i += 1
                    options.DebounceMs = debounceMs
                End If
            ElseIf arg = "--expectDiagnosticCode" AndAlso i + 1 < args.Length Then
                i += 1
                options.ExpectedDiagnosticCode = args(i)
            ElseIf arg = "--sendDidSave" Then
                options.SendDidSave = True
            ElseIf arg = "--serviceTests" Then
                options.ServiceTestsPath = "test-explore\vbnet-lsp\fixtures\services\service-tests.json"
            ElseIf arg = "--serviceManifest" AndAlso i + 1 < args.Length Then
                i += 1
                options.ServiceTestsPath = args(i)
            ElseIf arg = "--serviceTimeoutSeconds" AndAlso i + 1 < args.Length Then
                Dim serviceTimeout As Integer
                If Integer.TryParse(args(i + 1), serviceTimeout) Then
                    i += 1
                    options.ServiceTimeoutSeconds = serviceTimeout
                End If
            ElseIf arg = "--serviceLog" AndAlso i + 1 < args.Length Then
                i += 1
                options.ServiceLogPath = args(i)
            ElseIf arg = "--serviceTestId" AndAlso i + 1 < args.Length Then
                i += 1
                options.ServiceTestId = args(i)
            ElseIf arg = "--protocolLog" AndAlso i + 1 < args.Length Then
                i += 1
                options.ProtocolLogPath = args(i)
            ElseIf arg = "--timingLog" AndAlso i + 1 < args.Length Then
                i += 1
                options.TimingLogPath = args(i)
            ElseIf arg = "--timingLabel" AndAlso i + 1 < args.Length Then
                i += 1
                options.TimingLabel = args(i)
            End If

            i += 1
        End While

        Return options
    End Function

    Private Function Quote(value As String) As String
        Dim quoteChar = """"c
        If value.Contains(quoteChar) Then
            value = value.Replace("""", "\\" & """")
        End If

        Return $"""{value}"""
    End Function

    Private Async Function WaitForDiagnosticsAsync(diagnosticsTask As Task(Of Integer), timeoutSeconds As Integer, token As CancellationToken) As Task(Of Boolean)
        Dim timeout = Task.Delay(TimeSpan.FromSeconds(timeoutSeconds), token)
        Dim completed = Await Task.WhenAny(diagnosticsTask, timeout)
        Return completed Is diagnosticsTask
    End Function

    Private Function BuildSettingsPayload(options As Options) As Object
        If String.IsNullOrWhiteSpace(options.DiagnosticsMode) AndAlso Not options.DebounceMs.HasValue Then
            Return Nothing
        End If

        Dim diagnosticsSettings As New Dictionary(Of String, Object)()
        If Not String.IsNullOrWhiteSpace(options.DiagnosticsMode) Then
            diagnosticsSettings("diagnosticsMode") = options.DiagnosticsMode
        End If

        If options.DebounceMs.HasValue Then
            diagnosticsSettings("debounceMs") = options.DebounceMs.Value
        End If

        Dim payload As New Dictionary(Of String, Object) From {
            {"vbnetLs", diagnosticsSettings}
        }

        Return payload
    End Function

    Private Function ContainsDiagnosticCode(diagnosticsElement As JsonElement, expectedCode As String) As Boolean
        For Each diagnostic In diagnosticsElement.EnumerateArray()
            Dim codeElement As JsonElement
            If Not diagnostic.TryGetProperty("code", codeElement) Then
                Continue For
            End If

            Dim codeValue As String = Nothing
            Select Case codeElement.ValueKind
                Case JsonValueKind.String
                    codeValue = codeElement.GetString()
                Case JsonValueKind.Number
                    codeValue = codeElement.GetRawText()
            End Select

            If String.Equals(codeValue, expectedCode, StringComparison.OrdinalIgnoreCase) Then
                Return True
            End If
        Next

        Return False
    End Function

    Private NotInheritable Class ClientHandlers
        Private ReadOnly _settingsPayload As Object
        Private ReadOnly _diagnosticsWaiter As DiagnosticsWaiter
        Private ReadOnly _options As Options
        Private ReadOnly _protocolLog As ProtocolLog

        Public Sub New(settingsPayload As Object, diagnosticsWaiter As DiagnosticsWaiter, options As Options, protocolLog As ProtocolLog)
            _settingsPayload = settingsPayload
            _diagnosticsWaiter = diagnosticsWaiter
            _options = options
            _protocolLog = protocolLog
        End Sub

        <JsonRpcMethod("workspace/configuration", UseSingleObjectParameterDeserialization:=True)>
        Public Function WorkspaceConfiguration(paramsElement As JsonElement) As Object
            Dim payload = _settingsPayload
            Dim itemsElement As JsonElement
            If paramsElement.TryGetProperty("items", itemsElement) AndAlso itemsElement.ValueKind = JsonValueKind.Array Then
                Dim results As New List(Of Object)()
                For Each item In itemsElement.EnumerateArray()
                    results.Add(payload)
                Next

                Return results
            End If

            Return payload
        End Function

        <JsonRpcMethod("textDocument/publishDiagnostics", UseSingleObjectParameterDeserialization:=True)>
        Public Sub PublishDiagnostics(paramsElement As JsonElement)
            If _diagnosticsWaiter Is Nothing Then
                Return
            End If

            Dim uriElement As JsonElement
            If Not paramsElement.TryGetProperty("uri", uriElement) Then
                _protocolLog.Write("error", "publishDiagnostics missing uri.")
                Return
            End If

            Dim uri = uriElement.GetString()
            If Not String.Equals(uri, _diagnosticsWaiter.TargetUri, StringComparison.OrdinalIgnoreCase) Then
                Return
            End If

            Dim diagnosticsElement As JsonElement
            If Not paramsElement.TryGetProperty("diagnostics", diagnosticsElement) OrElse diagnosticsElement.ValueKind <> JsonValueKind.Array Then
                _protocolLog.Write("error", "publishDiagnostics missing diagnostics array.")
                Return
            End If

            Dim count = diagnosticsElement.GetArrayLength()
            Dim expectedFound = True
            If Not String.IsNullOrWhiteSpace(_options.ExpectedDiagnosticCode) Then
                expectedFound = ContainsDiagnosticCode(diagnosticsElement, _options.ExpectedDiagnosticCode)
                If Not expectedFound Then
                    Dim codes = ExtractDiagnosticCodes(diagnosticsElement)
                    Dim codesText = If(codes.Count = 0, "none", String.Join(", ", codes))
                    _protocolLog.Write("warn", $"Expected diagnostic code {_options.ExpectedDiagnosticCode} not found in publishDiagnostics payload. codes={codesText}")
                    Console.WriteLine($"diagnostics: codes={codesText}")
                End If
            End If

            Console.WriteLine($"diagnostics: {count} for {uri} (expectedCode={_options.ExpectedDiagnosticCode}, found={expectedFound})")
            _diagnosticsWaiter.Notify(count, expectedFound)
        End Sub

        Private Shared Function ExtractDiagnosticCodes(diagnosticsElement As JsonElement) As List(Of String)
            Dim codes As New List(Of String)()
            For Each diagnostic In diagnosticsElement.EnumerateArray()
                Dim codeElement As JsonElement
                If Not diagnostic.TryGetProperty("code", codeElement) Then
                    Continue For
                End If

                Dim codeValue As String = Nothing
                Select Case codeElement.ValueKind
                    Case JsonValueKind.String
                        codeValue = codeElement.GetString()
                    Case JsonValueKind.Number
                        codeValue = codeElement.GetRawText()
                End Select

                If Not String.IsNullOrWhiteSpace(codeValue) Then
                    codes.Add(codeValue)
                End If
            Next

            Return codes
        End Function
    End Class

    Private NotInheritable Class DiagnosticsWaiter
        Public Sub New(targetUri As String)
            Me.TargetUri = targetUri
            Me.Tcs = NewTcs()
        End Sub

        Public ReadOnly Property TargetUri As String

        Public Property Tcs As TaskCompletionSource(Of Integer)

        Public Sub Reset()
            Tcs = NewTcs()
        End Sub

        Public Sub Notify(count As Integer, expectedFound As Boolean)
            If count > 0 AndAlso expectedFound Then
                Tcs.TrySetResult(count)
            End If
        End Sub

        Private Shared Function NewTcs() As TaskCompletionSource(Of Integer)
            Return New TaskCompletionSource(Of Integer)(TaskCreationOptions.RunContinuationsAsynchronously)
        End Function
    End Class

    Private NotInheritable Class ProtocolLog
        Private ReadOnly _path As String
        Private ReadOnly _harness As String

        Private Sub New(logPath As String, harness As String)
            _path = logPath
            _harness = harness
        End Sub

        Public Shared Function Create(logPath As String, harness As String) As ProtocolLog
            If String.IsNullOrWhiteSpace(logPath) Then
                Return New ProtocolLog(String.Empty, harness)
            End If

            Dim fullPath = Path.GetFullPath(logPath)
            Dim directoryPath = Path.GetDirectoryName(fullPath)
            If Not String.IsNullOrWhiteSpace(directoryPath) Then
                System.IO.Directory.CreateDirectory(directoryPath)
            End If
            Return New ProtocolLog(fullPath, harness)
        End Function

        Public Sub Write(severity As String, message As String)
            If String.IsNullOrWhiteSpace(_path) Then
                Return
            End If

            Dim payload = New With {
                .timestamp = DateTimeOffset.Now.ToString("o"),
                .harness = _harness,
                severity,
                message
            }

            File.AppendAllText(_path, JsonSerializer.Serialize(payload) & Environment.NewLine)
        End Sub
    End Class

    Private NotInheritable Class TimingLog
        Private ReadOnly _path As String
        Private ReadOnly _label As String
        Private ReadOnly _marks As New HashSet(Of String)(StringComparer.OrdinalIgnoreCase)

        Private Sub New(logPath As String, label As String)
            _path = logPath
            _label = label
        End Sub

        Public Shared Function Create(logPath As String, label As String) As TimingLog
            If String.IsNullOrWhiteSpace(logPath) Then
                Return New TimingLog(String.Empty, label)
            End If

            Dim fullPath = Path.GetFullPath(logPath)
            Dim directoryPath = Path.GetDirectoryName(fullPath)
            If Not String.IsNullOrWhiteSpace(directoryPath) Then
                System.IO.Directory.CreateDirectory(directoryPath)
            End If
            Return New TimingLog(fullPath, label)
        End Function

        Public Sub Mark(name As String, stopwatch As Stopwatch)
            If String.IsNullOrWhiteSpace(_path) Then
                Return
            End If

            If Not _marks.Add(name) Then
                Return
            End If

            Dim payload = New With {
                .timestamp = DateTimeOffset.Now.ToString("o"),
                .label = _label,
                name,
                .elapsedMs = stopwatch.Elapsed.TotalMilliseconds
            }

            File.AppendAllText(_path, JsonSerializer.Serialize(payload) & Environment.NewLine)
        End Sub

        Public Sub TryMarkFromServerLine(line As String, stopwatch As Stopwatch)
            If String.IsNullOrWhiteSpace(line) Then
                Return
            End If

            If line.Contains("VB.NET Language Server starting", StringComparison.OrdinalIgnoreCase) Then
                Mark("server_starting", stopwatch)
            ElseIf line.Contains("Loading solution", StringComparison.OrdinalIgnoreCase) Then
                Mark("solution_loading", stopwatch)
            ElseIf line.Contains("Solution loaded", StringComparison.OrdinalIgnoreCase) Then
                Mark("solution_loaded", stopwatch)
            End If
        End Sub
    End Class

    Private NotInheritable Class ServiceTestManifest
        Public Property Workspace As String = String.Empty
        Public Property File As String = String.Empty
        Public Property Tests As List(Of ServiceTestCase) = New List(Of ServiceTestCase)()

        Public Shared Function Load(manifestPath As String) As ServiceTestManifest
            If String.IsNullOrWhiteSpace(manifestPath) Then
                Throw New InvalidOperationException("Service test manifest path is required.")
            End If

            Dim fullPath = Path.GetFullPath(manifestPath)
            If Not System.IO.File.Exists(fullPath) Then
                Throw New FileNotFoundException("Service test manifest not found.", fullPath)
            End If

            Dim json = System.IO.File.ReadAllText(fullPath)
            Dim manifest = JsonSerializer.Deserialize(Of ServiceTestManifest)(json, New JsonSerializerOptions With {
                .PropertyNameCaseInsensitive = True
            })

            If manifest Is Nothing Then
                Throw New InvalidOperationException("Failed to parse service test manifest.")
            End If

            If String.IsNullOrWhiteSpace(manifest.Workspace) OrElse String.IsNullOrWhiteSpace(manifest.File) Then
                Throw New InvalidOperationException("Service test manifest missing workspace or file.")
            End If

            Return manifest
        End Function
    End Class

    Private NotInheritable Class ServiceTestCase
        Public Property Id As String = String.Empty
        Public Property Method As String = String.Empty
        Public Property Marker As String = String.Empty
        Public Property Expectation As String = String.Empty
        Public Property Token As String = String.Empty
        Public Property TokenOffset As Integer
    End Class

    Private NotInheritable Class MarkerLocator
        Private ReadOnly _text As String
        Private ReadOnly _cache As New Dictionary(Of String, TextPosition)(StringComparer.OrdinalIgnoreCase)

        Public Sub New(text As String)
            _text = text
        End Sub

        Public Function TryGetPosition(test As ServiceTestCase, ByRef position As TextPosition, ByRef reason As String) As Boolean
            position = Nothing
            reason = String.Empty

            If String.IsNullOrWhiteSpace(test.Marker) Then
                reason = "marker_missing"
                Return False
            End If

            Dim cacheKey = $"{test.Marker}|{test.Token}|{test.TokenOffset}"
            If _cache.TryGetValue(cacheKey, position) Then
                Return True
            End If

            Dim markerToken = $"' MARKER: {test.Marker}"
            Dim markerIndex = _text.IndexOf(markerToken, StringComparison.Ordinal)
            If markerIndex < 0 Then
                reason = "marker_not_found"
                Return False
            End If

            Dim lineStart = _text.LastIndexOf(ControlChars.Lf, markerIndex)
            Dim lineEnd = _text.IndexOf(ControlChars.Lf, markerIndex)
            If lineStart < 0 Then
                lineStart = -1
            End If

            If lineEnd < 0 Then
                lineEnd = _text.Length
            End If

            If Not String.IsNullOrWhiteSpace(test.Token) Then
                Dim lineText = _text.Substring(lineStart + 1, markerIndex - (lineStart + 1))
                Dim tokenIndex = lineText.IndexOf(test.Token, StringComparison.Ordinal)
                If tokenIndex < 0 Then
                    reason = "token_not_found"
                    Return False
                End If

                Dim absoluteIndex = lineStart + 1 + tokenIndex + test.TokenOffset
                position = GetPosition(_text, absoluteIndex)
                _cache(cacheKey) = position
                Return True
            End If

            position = GetPosition(_text, markerIndex)
            _cache(cacheKey) = position
            Return True
        End Function
    End Class

    Private Structure TextPosition
        Public Sub New(line As Integer, character As Integer)
            Me.Line = line
            Me.Character = character
        End Sub

        Public ReadOnly Property Line As Integer
        Public ReadOnly Property Character As Integer
    End Structure

    Private Function GetPosition(text As String, index As Integer) As TextPosition
        Dim line = 0
        Dim lastNewline = -1
        Dim i = 0
        While i < index
            If text(i) = ControlChars.Lf Then
                line += 1
                lastNewline = i
            End If
            i += 1
        End While

        Dim character = index - lastNewline - 1
        Return New TextPosition(line, character)
    End Function

    Private NotInheritable Class ServiceLog
        Private ReadOnly _path As String

        Private Sub New(logPath As String)
            _path = logPath
        End Sub

        Public Shared Function Create(logPath As String) As ServiceLog
            If String.IsNullOrWhiteSpace(logPath) Then
                Return New ServiceLog(String.Empty)
            End If

            Dim fullPath = Path.GetFullPath(logPath)
            Dim directoryPath = Path.GetDirectoryName(fullPath)
            If Not String.IsNullOrWhiteSpace(directoryPath) Then
                System.IO.Directory.CreateDirectory(directoryPath)
            End If
            Return New ServiceLog(fullPath)
        End Function

        Public Sub Write(entry As ServiceLogEntry)
            If String.IsNullOrWhiteSpace(_path) Then
                Return
            End If

            entry.Timestamp = DateTimeOffset.Now.ToString("o")
            Dim payload = JsonSerializer.Serialize(entry)
            File.AppendAllText(_path, payload & Environment.NewLine)
        End Sub
    End Class

    Private NotInheritable Class ServiceLogEntry
        Public Property Timestamp As String = String.Empty
        Public Property Id As String = String.Empty
        Public Property Method As String = String.Empty
        Public Property Expectation As String = String.Empty
        Public Property Outcome As String = String.Empty
        Public Property Count As Integer?
        Public Property ExpectedFound As Boolean?
    End Class
End Module
