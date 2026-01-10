import * as path from "path";
import * as assert from "assert";
import * as vscode from "vscode";

suite("Workspace open (VS Code harness)", () => {
    test("workspace folder is available", async () => {
        const folders = vscode.workspace.workspaceFolders;
        assert.ok(folders && folders.length > 0, "No workspace folders were opened.");

        const expected = process.env.FIXTURE_WORKSPACE
            ? path.resolve(process.env.FIXTURE_WORKSPACE)
            : null;
        if (expected) {
            const opened = folders![0].uri.fsPath;
            assert.strictEqual(
                path.resolve(opened).toLowerCase(),
                expected.toLowerCase(),
                "Opened workspace did not match expected path."
            );
        }
    });
});
