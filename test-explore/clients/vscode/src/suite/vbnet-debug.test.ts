import * as path from "path";
import * as fs from "fs";
import * as assert from "assert";
import * as vscode from "vscode";

const skipVbnetDebug = process.env.SKIP_VBNET_DEBUG === "1";

if (skipVbnetDebug) {
    suite.skip("VB.NET debugging (skipped)", () => {
        // Skipped via SKIP_VBNET_DEBUG.
    });
} else {
    suite("VB.NET debugging (VS Code harness)", () => {
    test("launch debug session with netcoredbg", async function () {
        this.timeout(120000);

        const netcoredbgPath = resolveNetcoreDbgPath();
        if (!netcoredbgPath) {
            this.skip();
            return;
        }

        const repoRoot = path.resolve(__dirname, "..", "..", "..", "..", "..");
        const debugContext = getDebugContext(repoRoot);
        const workspaceRoot = debugContext.debugRoot;

        const extensionId = process.env.EXTENSION_ID ?? "dnakode.vbnet-language-support";
        const extension = vscode.extensions.getExtension(extensionId);
        assert.ok(extension, `Extension ${extensionId} is not installed.`);
        await extension!.activate();

        const entryFile = path.resolve(workspaceRoot, "Program.vb");
        if (fs.existsSync(entryFile)) {
            const doc = await vscode.workspace.openTextDocument(entryFile);
            await vscode.window.showTextDocument(doc);
        }

        const programPath = path.resolve(
            repoRoot,
            "test",
            "TestProjects",
            "DebugConsole",
            "bin",
            "Debug",
            "net10.0",
            "DebugConsole.dll"
        );
        assert.ok(fs.existsSync(programPath), `Debug program not found at ${programPath}`);

        const config = vscode.workspace.getConfiguration("vbnet");
        const restoreDebuggerPath = await applyDebuggerPathOverride(config, netcoredbgPath);

        const traceCapture = setupDapTrace(repoRoot);
        const debugConfig: vscode.DebugConfiguration = {
            type: "vbnet",
            name: "VB.NET Debug Console",
            request: "launch",
            program: programPath,
            cwd: workspaceRoot,
            stopAtEntry: false
        };

        const startPromise = waitForDebugStart(30000);
        const terminatePromise = waitForDebugTerminate(60000);

        let started = false;
        try {
            started = await vscode.debug.startDebugging(debugContext.workspaceFolder, debugConfig);
            assert.ok(started, "Debug session did not start.");
            await startPromise;
            try {
                await terminatePromise;
            } catch (error) {
                console.warn(`Debug session did not terminate cleanly: ${error}`);
                if (started) {
                    await vscode.debug.stopDebugging();
                    try {
                        await waitForDebugTerminate(10000);
                    } catch (stopError) {
                        console.warn(`Debug session still did not terminate after stop: ${stopError}`);
                    }
                }
            }
        } finally {
            traceCapture?.dispose();
            if (traceCapture?.tracePath) {
                console.log(`DAP trace written to ${traceCapture.tracePath}`);
            }
            if (started && vscode.debug.activeDebugSession) {
                await vscode.debug.stopDebugging();
            }
            await restoreDebuggerPath();
        }
    });

    test("variables and watch evaluate values at breakpoint", async function () {
        this.timeout(120000);

        const netcoredbgPath = resolveNetcoreDbgPath();
        if (!netcoredbgPath) {
            this.skip();
            return;
        }

        const repoRoot = path.resolve(__dirname, "..", "..", "..", "..", "..");
        const debugContext = getDebugContext(repoRoot);
        const workspaceRoot = debugContext.debugRoot;

        const extensionId = process.env.EXTENSION_ID ?? "dnakode.vbnet-language-support";
        const extension = vscode.extensions.getExtension(extensionId);
        assert.ok(extension, `Extension ${extensionId} is not installed.`);
        await extension!.activate();

        const entryFile = path.resolve(workspaceRoot, "Program.vb");
        assert.ok(fs.existsSync(entryFile), `Debug entry file missing at ${entryFile}`);
        const doc = await vscode.workspace.openTextDocument(entryFile);
        await vscode.window.showTextDocument(doc);

        const breakpointLine = findLineContaining(doc, "Value = {value}");
        const sourceBreakpoint = new vscode.SourceBreakpoint(
            new vscode.Location(doc.uri, new vscode.Position(breakpointLine, 0))
        );
        if (vscode.debug.breakpoints.length > 0) {
            vscode.debug.removeBreakpoints(vscode.debug.breakpoints);
        }
        vscode.debug.addBreakpoints([sourceBreakpoint]);

        const programPath = path.resolve(
            repoRoot,
            "test",
            "TestProjects",
            "DebugConsole",
            "bin",
            "Debug",
            "net10.0",
            "DebugConsole.dll"
        );
        assert.ok(fs.existsSync(programPath), `Debug program not found at ${programPath}`);

        const config = vscode.workspace.getConfiguration("vbnet");
        const restoreDebuggerPath = await applyDebuggerPathOverride(config, netcoredbgPath);

        const debugConfig: vscode.DebugConfiguration = {
            type: "vbnet",
            name: "VB.NET Debug Console (locals/watch)",
            request: "launch",
            program: programPath,
            cwd: workspaceRoot,
            stopAtEntry: false
        };

        const startPromise = waitForDebugStart(30000);
        const stopCapture = waitForStoppedEvent(30000);
        const terminatePromise = waitForDebugTerminate(60000);

        let started = false;
        try {
            started = await vscode.debug.startDebugging(debugContext.workspaceFolder, debugConfig);
            assert.ok(started, "Debug session did not start.");
            await startPromise;

            const stopped = await stopCapture.promise;
            const session = vscode.debug.activeDebugSession;
            assert.ok(session, "No active debug session after stopped event.");

            let threadId = stopped.threadId;
            if (typeof threadId !== "number") {
                threadId = await getAnyThreadId(session!);
            }
            assert.ok(typeof threadId === "number", "No thread ID available for stack trace.");

            const stackTrace = await session!.customRequest("stackTrace", {
                threadId,
                startFrame: 0,
                levels: 20,
            }) as { stackFrames?: Array<{ id: number; name: string }> };
            assert.ok(Array.isArray(stackTrace.stackFrames) && stackTrace.stackFrames.length > 0, "Stack trace was empty.");

            const frameId = stackTrace.stackFrames![0].id;
            const scopeResponse = await session!.customRequest("scopes", { frameId }) as {
                scopes?: Array<{ name: string; variablesReference: number }>;
            };
            assert.ok(Array.isArray(scopeResponse.scopes) && scopeResponse.scopes.length > 0, "Scopes response was empty.");

            const localsScope = scopeResponse.scopes!.find((scope) => /local/i.test(scope.name)) ?? scopeResponse.scopes![0];
            const variablesResponse = await session!.customRequest("variables", {
                variablesReference: localsScope.variablesReference,
            }) as { variables?: Array<{ name: string; value: string }> };

            const variables = variablesResponse.variables ?? [];
            const valueVariable = variables.find((variable) => variable.name === "value");
            const renderedVariables = variables.map((variable) => `${variable.name}=${variable.value}`).join(", ");
            assert.ok(valueVariable, `Local variable 'value' was not found. Variables: ${renderedVariables}`);
            assert.ok(
                (valueVariable!.value ?? "").includes("42"),
                `Unexpected local value for 'value': ${valueVariable!.value}`
            );

            const watchResult = await session!.customRequest("evaluate", {
                expression: "value",
                frameId,
                context: "watch",
            }) as { result?: string };
            assert.ok(
                typeof watchResult.result === "string" && watchResult.result.includes("42"),
                `Unexpected watch evaluate result: ${JSON.stringify(watchResult)}`
            );

            await session!.customRequest("continue", { threadId });
            await terminatePromise;
        } finally {
            stopCapture.dispose();
            vscode.debug.removeBreakpoints([sourceBreakpoint]);
            if (started && vscode.debug.activeDebugSession) {
                await vscode.debug.stopDebugging();
            }
            await restoreDebuggerPath();
        }
    });

    test("launch debug session with inferred program path", async function () {
        this.timeout(120000);

        const netcoredbgPath = resolveNetcoreDbgPath();
        if (!netcoredbgPath) {
            this.skip();
            return;
        }

        const repoRoot = path.resolve(__dirname, "..", "..", "..", "..", "..");
        const debugContext = getDebugContext(repoRoot);
        const workspaceRoot = debugContext.debugRoot;

        const extensionId = process.env.EXTENSION_ID ?? "dnakode.vbnet-language-support";
        const extension = vscode.extensions.getExtension(extensionId);
        assert.ok(extension, `Extension ${extensionId} is not installed.`);
        await extension!.activate();

        const programPath = path.resolve(
            repoRoot,
            "test",
            "TestProjects",
            "DebugConsole",
            "bin",
            "Debug",
            "net10.0",
            "DebugConsole.dll"
        );
        assert.ok(fs.existsSync(programPath), `Debug program not found at ${programPath}`);

        if (!debugContext.workspaceFolder) {
            this.skip();
            return;
        }
        const config = vscode.workspace.getConfiguration("vbnet");
        const restoreDebuggerPath = await applyDebuggerPathOverride(config, netcoredbgPath);

        const debugConfig: vscode.DebugConfiguration = {
            type: "vbnet",
            name: "VB.NET Debug Console (inferred)",
            request: "launch",
            cwd: workspaceRoot,
            stopAtEntry: false
        };

        const startPromise = waitForDebugStart(30000);
        const terminatePromise = waitForDebugTerminate(60000);

        let started = false;
        try {
            started = await vscode.debug.startDebugging(debugContext.workspaceFolder, debugConfig);
            assert.ok(started, "Debug session did not start with inferred program path.");
            await startPromise;
            try {
                await terminatePromise;
            } catch (error) {
                console.warn(`Debug session did not terminate cleanly: ${error}`);
                if (started) {
                    await vscode.debug.stopDebugging();
                    try {
                        await waitForDebugTerminate(10000);
                    } catch (stopError) {
                        console.warn(`Debug session still did not terminate after stop: ${stopError}`);
                    }
                }
            }
        } finally {
            if (started && vscode.debug.activeDebugSession) {
                await vscode.debug.stopDebugging();
            }
            await restoreDebuggerPath();
        }
    });

    test("launch debug session with template program path", async function () {
        this.timeout(120000);

        const netcoredbgPath = resolveNetcoreDbgPath();
        if (!netcoredbgPath) {
            this.skip();
            return;
        }

        const repoRoot = path.resolve(__dirname, "..", "..", "..", "..", "..");
        const debugContext = getDebugContext(repoRoot);
        const workspaceRoot = debugContext.debugRoot;

        const extensionId = process.env.EXTENSION_ID ?? "dnakode.vbnet-language-support";
        const extension = vscode.extensions.getExtension(extensionId);
        assert.ok(extension, `Extension ${extensionId} is not installed.`);
        await extension!.activate();

        const programPath = path.resolve(
            repoRoot,
            "test",
            "TestProjects",
            "DebugConsole",
            "bin",
            "Debug",
            "net10.0",
            "DebugConsole.dll"
        );
        assert.ok(fs.existsSync(programPath), `Debug program not found at ${programPath}`);

        const config = vscode.workspace.getConfiguration("vbnet");
        const restoreDebuggerPath = await applyDebuggerPathOverride(config, netcoredbgPath);

        const templateProgram = path.join(
            workspaceRoot,
            "bin",
            "Debug",
            "<target-framework>",
            "<project-name>.dll"
        );

        const debugConfig: vscode.DebugConfiguration = {
            type: "vbnet",
            name: "VB.NET Debug Console (template)",
            request: "launch",
            program: templateProgram,
            projectPath: path.join(workspaceRoot, "DebugConsole.vbproj"),
            cwd: workspaceRoot,
            stopAtEntry: false
        };

        const startPromise = waitForDebugStart(30000);
        const terminatePromise = waitForDebugTerminate(60000);

        let started = false;
        try {
            started = await vscode.debug.startDebugging(debugContext.workspaceFolder, debugConfig);
            assert.ok(started, "Debug session did not start with program template.");
            await startPromise;
            try {
                await terminatePromise;
            } catch (error) {
                console.warn(`Debug session did not terminate cleanly: ${error}`);
                if (started) {
                    await vscode.debug.stopDebugging();
                    try {
                        await waitForDebugTerminate(10000);
                    } catch (stopError) {
                        console.warn(`Debug session still did not terminate after stop: ${stopError}`);
                    }
                }
            }
        } finally {
            if (started && vscode.debug.activeDebugSession) {
                await vscode.debug.stopDebugging();
            }
            await restoreDebuggerPath();
        }
    });

    test("launch debug session with projectPath inference", async function () {
        this.timeout(120000);

        const netcoredbgPath = resolveNetcoreDbgPath();
        if (!netcoredbgPath) {
            this.skip();
            return;
        }

        const repoRoot = path.resolve(__dirname, "..", "..", "..", "..", "..");
        const debugContext = getDebugContext(repoRoot);
        const workspaceRoot = debugContext.debugRoot;

        const extensionId = process.env.EXTENSION_ID ?? "dnakode.vbnet-language-support";
        const extension = vscode.extensions.getExtension(extensionId);
        assert.ok(extension, `Extension ${extensionId} is not installed.`);
        await extension!.activate();

        const programPath = path.resolve(
            repoRoot,
            "test",
            "TestProjects",
            "DebugConsole",
            "bin",
            "Debug",
            "net10.0",
            "DebugConsole.dll"
        );
        assert.ok(fs.existsSync(programPath), `Debug program not found at ${programPath}`);

        const config = vscode.workspace.getConfiguration("vbnet");
        const restoreDebuggerPath = await applyDebuggerPathOverride(config, netcoredbgPath);

        const debugConfig: vscode.DebugConfiguration = {
            type: "vbnet",
            name: "VB.NET Debug Console (projectPath)",
            request: "launch",
            projectPath: path.join(workspaceRoot, "DebugConsole.vbproj"),
            cwd: workspaceRoot,
            stopAtEntry: false
        };

        const startPromise = waitForDebugStart(30000);
        const terminatePromise = waitForDebugTerminate(60000);

        let started = false;
        try {
            started = await vscode.debug.startDebugging(debugContext.workspaceFolder, debugConfig);
            assert.ok(started, "Debug session did not start with projectPath inference.");
            await startPromise;
            try {
                await terminatePromise;
            } catch (error) {
                console.warn(`Debug session did not terminate cleanly: ${error}`);
                if (started) {
                    await vscode.debug.stopDebugging();
                    try {
                        await waitForDebugTerminate(10000);
                    } catch (stopError) {
                        console.warn(`Debug session still did not terminate after stop: ${stopError}`);
                    }
                }
            }
        } finally {
            if (started && vscode.debug.activeDebugSession) {
                await vscode.debug.stopDebugging();
            }
            await restoreDebuggerPath();
        }
    });
    });
}

function resolveNetcoreDbgPath(): string | undefined {
    const envPath = process.env.NETCOREDBG_PATH;
    if (envPath && fs.existsSync(envPath)) {
        return envPath;
    }

    const exeName = process.platform === "win32" ? "netcoredbg.exe" : "netcoredbg";
    const repoRoot = path.resolve(__dirname, "..", "..", "..", "..", "..");
    const bundledCandidate = path.resolve(repoRoot, "src", "extension", ".debugger", exeName);
    if (fs.existsSync(bundledCandidate)) {
        return bundledCandidate;
    }
    const candidate = path.resolve(repoRoot, "_external", "netcoredbg", "bin", exeName);
    if (fs.existsSync(candidate)) {
        return candidate;
    }

    const pathEntries = (process.env.PATH ?? "").split(path.delimiter).filter((entry) => entry.length > 0);
    const extensions = process.platform === "win32"
        ? (process.env.PATHEXT ?? ".EXE;.CMD;.BAT").split(";")
        : [""];

    for (const entry of pathEntries) {
        if (process.platform === "win32") {
            for (const ext of extensions) {
                const candidatePath = path.join(entry, `netcoredbg${ext.toLowerCase()}`);
                if (fs.existsSync(candidatePath)) {
                    return candidatePath;
                }
            }
        } else {
            const candidatePath = path.join(entry, exeName);
            if (fs.existsSync(candidatePath)) {
                return candidatePath;
            }
        }
    }

    return undefined;
}

async function applyDebuggerPathOverride(
    config: vscode.WorkspaceConfiguration,
    debuggerPath: string
): Promise<() => Promise<void>> {
    const previousWorkspaceValue = config.inspect<string>("debugger.path")?.workspaceValue;
    let didOverride = false;

    try {
        await config.update("debugger.path", debuggerPath, vscode.ConfigurationTarget.Workspace);
        didOverride = true;
    } catch (error) {
        console.warn(`Unable to update vbnet.debugger.path in workspace settings: ${error}`);
    }

    return async () => {
        if (!didOverride) {
            return;
        }

        try {
            await config.update("debugger.path", previousWorkspaceValue, vscode.ConfigurationTarget.Workspace);
        } catch (error) {
            console.warn(`Unable to restore vbnet.debugger.path in workspace settings: ${error}`);
        }
    };
}

function getDebugContext(repoRoot: string): { debugRoot: string; workspaceFolder?: vscode.WorkspaceFolder } {
    const debugRoot = path.resolve(repoRoot, "test", "TestProjects", "DebugConsole");
    assert.ok(fs.existsSync(debugRoot), `Debug project folder missing at ${debugRoot}`);

    const existing = vscode.workspace.workspaceFolders ?? [];
    const existingMatch = existing.find(
        (folder) => folder.uri.fsPath.toLowerCase() === debugRoot.toLowerCase()
    );
    if (existingMatch) {
        return { debugRoot, workspaceFolder: existingMatch };
    }

    return { debugRoot };
}

function findLineContaining(document: vscode.TextDocument, pattern: string): number {
    for (let line = 0; line < document.lineCount; line++) {
        if (document.lineAt(line).text.includes(pattern)) {
            return line;
        }
    }

    throw new Error(`Pattern '${pattern}' was not found in ${document.uri.fsPath}.`);
}

function waitForStoppedEvent(timeoutMs: number): {
    promise: Promise<{ threadId?: number; reason?: string }>;
    dispose: () => void;
} {
    let resolved = false;
    let resolvePromise: (value: { threadId?: number; reason?: string }) => void = () => undefined;
    let rejectPromise: (reason?: unknown) => void = () => undefined;

    const promise = new Promise<{ threadId?: number; reason?: string }>((resolve, reject) => {
        resolvePromise = resolve;
        rejectPromise = reject;
    });

    const timeout = setTimeout(() => {
        if (resolved) {
            return;
        }
        resolved = true;
        rejectPromise(new Error("Timed out waiting for debug adapter stopped event."));
    }, timeoutMs);

    const trackerFactory = vscode.debug.registerDebugAdapterTrackerFactory("vbnet", {
        createDebugAdapterTracker() {
            return {
                onDidSendMessage: (message: any) => {
                    if (resolved) {
                        return;
                    }

                    if (message?.type === "event" && message?.event === "stopped") {
                        resolved = true;
                        clearTimeout(timeout);
                        resolvePromise({
                            threadId: typeof message?.body?.threadId === "number" ? message.body.threadId : undefined,
                            reason: typeof message?.body?.reason === "string" ? message.body.reason : undefined,
                        });
                    }
                },
            };
        },
    });

    return {
        promise,
        dispose: () => {
            clearTimeout(timeout);
            trackerFactory.dispose();
        },
    };
}

async function getAnyThreadId(session: vscode.DebugSession): Promise<number | undefined> {
    const threadResponse = await session.customRequest("threads") as { threads?: Array<{ id: number }> };
    if (!Array.isArray(threadResponse.threads) || threadResponse.threads.length === 0) {
        return undefined;
    }

    const firstThread = threadResponse.threads.find((thread) => typeof thread.id === "number");
    return firstThread?.id;
}

function waitForDebugStart(timeoutMs: number): Promise<void> {
    return new Promise((resolve, reject) => {
        const timer = setTimeout(() => {
            disposable.dispose();
            reject(new Error("Timed out waiting for debug session to start."));
        }, timeoutMs);
        const disposable = vscode.debug.onDidStartDebugSession(() => {
            clearTimeout(timer);
            disposable.dispose();
            resolve();
        });
    });
}

function waitForDebugTerminate(timeoutMs: number): Promise<void> {
    return new Promise((resolve, reject) => {
        const timer = setTimeout(() => {
            disposable.dispose();
            reject(new Error("Timed out waiting for debug session to terminate."));
        }, timeoutMs);
        const disposable = vscode.debug.onDidTerminateDebugSession(() => {
            clearTimeout(timer);
            disposable.dispose();
            resolve();
        });
    });
}

function setupDapTrace(repoRoot: string): { tracePath: string; dispose: () => void } | undefined {
    if (process.env.CAPTURE_DAP_TRACE === "0") {
        return undefined;
    }

    const traceRoot = path.resolve(repoRoot, "test-explore", "clients", "vscode", "logs");
    fs.mkdirSync(traceRoot, { recursive: true });
    const timestamp = new Date().toISOString().replace(/[:.]/g, "");
    const tracePath = path.join(traceRoot, `dap-trace-${timestamp}.log`);

    const writeLine = (direction: string, message: unknown) => {
        try {
            const payload = JSON.stringify(message);
            fs.appendFileSync(tracePath, `[${new Date().toISOString()}] ${direction} ${payload}\n`, "utf8");
        } catch (error) {
            console.warn(`Failed to write DAP trace: ${error}`);
        }
    };

    const trackerFactory = vscode.debug.registerDebugAdapterTrackerFactory("vbnet", {
        createDebugAdapterTracker(session) {
            writeLine("session-start", { id: session.id, name: session.name, type: session.type });
            return {
                onWillReceiveMessage: (message) => writeLine("client->adapter", message),
                onDidSendMessage: (message) => writeLine("adapter->client", message),
                onError: (error) => writeLine("adapter-error", { message: error.message, name: error.name }),
                onExit: (code, signal) => writeLine("adapter-exit", { code, signal }),
            };
        },
    });

    return {
        tracePath,
        dispose: () => {
            writeLine("session-end", { reason: "disposed" });
            trackerFactory.dispose();
        },
    };
}


