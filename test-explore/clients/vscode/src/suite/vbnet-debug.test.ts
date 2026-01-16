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
        try {
            await config.update("debugger.path", netcoredbgPath, vscode.ConfigurationTarget.Workspace);
        } catch (error) {
            console.warn(`Unable to update vbnet.debugger.path in workspace settings: ${error}`);
        }

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

        const config = vscode.workspace.getConfiguration("vbnet");
        try {
            await config.update("debugger.path", netcoredbgPath, vscode.ConfigurationTarget.Workspace);
        } catch (error) {
            console.warn(`Unable to update vbnet.debugger.path in workspace settings: ${error}`);
        }

        if (!debugContext.workspaceFolder) {
            this.skip();
            return;
        }

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
        try {
            await config.update("debugger.path", netcoredbgPath, vscode.ConfigurationTarget.Workspace);
        } catch (error) {
            console.warn(`Unable to update vbnet.debugger.path in workspace settings: ${error}`);
        }

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
        try {
            await config.update("debugger.path", netcoredbgPath, vscode.ConfigurationTarget.Workspace);
        } catch (error) {
            console.warn(`Unable to update vbnet.debugger.path in workspace settings: ${error}`);
        }

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


