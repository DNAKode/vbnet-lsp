import * as path from "path";
import * as fs from "fs";
import * as assert from "assert";
import * as vscode from "vscode";

suite("VB.NET debugging (VS Code harness)", () => {
    test("launch debug session with netcoredbg", async function () {
        this.timeout(120000);

        const netcoredbgPath = resolveNetcoreDbgPath();
        if (!netcoredbgPath) {
            this.skip();
            return;
        }

        const repoRoot = path.resolve(__dirname, "..", "..", "..", "..", "..", "..");
        const workspaceRoot = vscode.workspace.workspaceFolders?.[0]?.uri.fsPath;
        assert.ok(workspaceRoot, "No workspace folder opened for debugging.");

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
            "net8.0",
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
            name: "VB.NET Debug Console",
            request: "launch",
            program: path.relative(workspaceRoot, programPath),
            cwd: "${workspaceFolder}",
            stopAtEntry: true
        };

        const startPromise = waitForDebugStart(30000);
        const terminatePromise = waitForDebugTerminate(60000);

        let started = false;
        try {
            started = await vscode.debug.startDebugging(vscode.workspace.workspaceFolders?.[0], debugConfig);
            assert.ok(started, "Debug session did not start.");
            await startPromise;
        } finally {
            if (started) {
                await vscode.debug.stopDebugging();
                try {
                    await terminatePromise;
                } catch (error) {
                    console.warn(`Debug session did not terminate cleanly: ${error}`);
                }
            }
        }
    });
});

function resolveNetcoreDbgPath(): string | undefined {
    const envPath = process.env.NETCOREDBG_PATH;
    if (envPath && fs.existsSync(envPath)) {
        return envPath;
    }

    const exeName = process.platform === "win32" ? "netcoredbg.exe" : "netcoredbg";
    const repoRoot = path.resolve(__dirname, "..", "..", "..", "..", "..", "..");
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
