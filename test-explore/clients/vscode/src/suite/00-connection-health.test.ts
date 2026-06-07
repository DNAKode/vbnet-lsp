import * as assert from "assert";
import * as path from "path";
import * as vscode from "vscode";

type VbNetExtensionApi = {
    getClientState?: () => string;
    getWorkspaceContext?: () => WorkspaceContext | undefined;
    waitForClientReady?: (timeoutMs?: number) => Promise<void>;
};

type WorkspaceContext = {
    kind?: string;
    solutionPath?: string;
    projectPaths?: string[];
    solutionCandidates?: string[];
};

const extensionId = process.env.EXTENSION_ID ?? "dnakode.vbnet-language-support";

async function findFixtureProject(): Promise<{ absolutePath: string; relativePath: string }> {
    const workspaceFolder = vscode.workspace.workspaceFolders?.[0];
    assert.ok(workspaceFolder, "No workspace folder is open.");

    const projects = await vscode.workspace.findFiles("**/*.vbproj", "**/{bin,obj,node_modules,.git}/**", 10);
    assert.ok(projects.length > 0, "Expected at least one .vbproj in the fixture workspace.");

    const project = projects.sort((a, b) => a.fsPath.localeCompare(b.fsPath))[0];
    return {
        absolutePath: project.fsPath,
        relativePath: path.relative(workspaceFolder!.uri.fsPath, project.fsPath)
    };
}

function withTimeout<T>(promise: Thenable<T>, timeoutMs: number, message: string): Promise<T> {
    return Promise.race([
        Promise.resolve(promise),
        new Promise<T>((_, reject) => setTimeout(() => reject(new Error(message)), timeoutMs))
    ]);
}

async function retryUntil<T>(
    action: () => Thenable<T> | T,
    isReady: (value: T) => boolean,
    timeoutMs = 45000,
    intervalMs = 500
): Promise<T> {
    const deadline = Date.now() + timeoutMs;
    let last: T;
    while (Date.now() < deadline) {
        last = await Promise.resolve(action());
        if (isReady(last)) {
            return last;
        }
        await new Promise((resolve) => setTimeout(resolve, intervalMs));
    }

    return last!;
}

async function ensureLanguageClientReady(exports: VbNetExtensionApi): Promise<void> {
    if (!exports.waitForClientReady) {
        assert.fail("Extension does not expose waitForClientReady; update extension API for tests.");
    }

    try {
        await withTimeout(
            exports.waitForClientReady(30000),
            35000,
            "Language client did not reach Running state within 35s."
        );
        return;
    } catch (error) {
        const state = exports.getClientState ? exports.getClientState() : "unknown";
        if (state !== "Stopped") {
            console.error(`Language client state after timeout: ${state}`);
            throw error;
        }
    }

    await vscode.commands.executeCommand("vbnet.restartServer");
    await withTimeout(
        exports.waitForClientReady(30000),
        35000,
        "Language client did not reach Running state within 35s after restart."
    );
}

suite("VB.NET extension connection health (VS Code harness)", function () {
    this.timeout(60000);

    test("extension activates within 30s", async () => {
        const extension = vscode.extensions.getExtension(extensionId);
        assert.ok(extension, `Extension ${extensionId} is not installed.`);
        await withTimeout(
            extension!.activate(),
            30000,
            "Extension activation timed out after 30s."
        );
    });

    test("language client reaches running state", async () => {
        const extension = vscode.extensions.getExtension(extensionId);
        assert.ok(extension, `Extension ${extensionId} is not installed.`);
        const exports = (extension!.exports ?? {}) as VbNetExtensionApi;

        await ensureLanguageClientReady(exports);

        const state = exports.getClientState ? exports.getClientState() : "unknown";
        assert.strictEqual(state, "Running", `Expected client state Running, got ${state}.`);
    });

    test("workspace context API reports discovered project context", async () => {
        const extension = vscode.extensions.getExtension(extensionId);
        assert.ok(extension, `Extension ${extensionId} is not installed.`);
        const exports = (extension!.exports ?? {}) as VbNetExtensionApi;

        if (!exports.getWorkspaceContext || !exports.waitForClientReady) {
            assert.fail("Extension does not expose workspace context test API.");
        }

        await ensureLanguageClientReady(exports);
        const context = await retryUntil(
            () => exports.getWorkspaceContext!(),
            (value) => !!value && ["solution", "singleProject", "allProjects", "selectContext", "empty"].includes(value.kind ?? "")
        );

        assert.ok(context, "Expected workspace context.");
        assert.notStrictEqual(context!.kind, "unknown", `Expected discovered workspace context, got ${JSON.stringify(context)}.`);
    });

    test("workspace context follows explicit projectPath setting", async () => {
        const extension = vscode.extensions.getExtension(extensionId);
        assert.ok(extension, `Extension ${extensionId} is not installed.`);
        const exports = (extension!.exports ?? {}) as VbNetExtensionApi;

        if (!exports.getWorkspaceContext || !exports.waitForClientReady) {
            assert.fail("Extension does not expose workspace context test API.");
        }

        const config = vscode.workspace.getConfiguration("vbnet");
        const originalSolutionPath = config.inspect<string>("workspace.solutionPath")?.workspaceValue;
        const originalProjectPaths = config.inspect<string[]>("workspace.projectPaths")?.workspaceValue;
        const originalIgnoreSolutionFiles = config.inspect<boolean>("workspace.ignoreSolutionFiles")?.workspaceValue;
        const fixtureProject = await findFixtureProject();

        try {
            await config.update("workspace.solutionPath", "", vscode.ConfigurationTarget.Workspace);
            await config.update(
                "workspace.projectPaths",
                [fixtureProject.relativePath],
                vscode.ConfigurationTarget.Workspace
            );
            await config.update("workspace.ignoreSolutionFiles", true, vscode.ConfigurationTarget.Workspace);

            const context = await retryUntil(
                () => exports.getWorkspaceContext!(),
                (value) =>
                    value?.kind === "singleProject" &&
                    !!value.projectPaths?.some((projectPath) => path.resolve(projectPath).toLowerCase() === fixtureProject.absolutePath.toLowerCase()),
                60000
            );

            assert.strictEqual(context!.kind, "singleProject");
        } finally {
            await config.update("workspace.solutionPath", originalSolutionPath, vscode.ConfigurationTarget.Workspace);
            await config.update("workspace.projectPaths", originalProjectPaths, vscode.ConfigurationTarget.Workspace);
            await config.update("workspace.ignoreSolutionFiles", originalIgnoreSolutionFiles, vscode.ConfigurationTarget.Workspace);
        }
    });
});
