import * as assert from "assert";
import * as vscode from "vscode";

type VbNetExtensionApi = {
    getClientState?: () => string;
    waitForClientReady?: (timeoutMs?: number) => Promise<void>;
};

const extensionId = process.env.EXTENSION_ID ?? "dnakode.vbnet-language-support";

function withTimeout<T>(promise: Thenable<T>, timeoutMs: number, message: string): Promise<T> {
    return Promise.race([
        Promise.resolve(promise),
        new Promise<T>((_, reject) => setTimeout(() => reject(new Error(message)), timeoutMs))
    ]);
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

        if (!exports.waitForClientReady) {
            assert.fail("Extension does not expose waitForClientReady; update extension API for tests.");
        }

        try {
            await withTimeout(
                exports.waitForClientReady(30000),
                35000,
                "Language client did not reach Running state within 35s."
            );
        } catch (error) {
            const state = exports.getClientState ? exports.getClientState() : "unknown";
            console.error(`Language client state after timeout: ${state}`);
            throw error;
        }

        const state = exports.getClientState ? exports.getClientState() : "unknown";
        assert.strictEqual(state, "Running", `Expected client state Running, got ${state}.`);
    });
});
