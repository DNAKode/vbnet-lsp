import * as path from "path";
import * as fs from "fs";
import * as assert from "assert";
import * as vscode from "vscode";

const extensionId = process.env.EXTENSION_ID ?? "dnakode.vbnet-language-support";
const enableDwsim = process.env.VBNET_DWSIM === "1";

async function retryUntil<T>(
    action: () => Thenable<T>,
    isReady: (value: T) => boolean,
    timeoutMs = 180000,
    intervalMs = 2000
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

function getTokenPosition(doc: vscode.TextDocument, token: string, occurrence = 1): vscode.Position {
    const text = doc.getText();
    let index = -1;
    let start = 0;
    let remaining = Math.max(1, occurrence);

    while (remaining > 0) {
        index = text.indexOf(token, start);
        assert.ok(index >= 0, `Token not found: ${token}`);
        remaining -= 1;
        start = index + token.length;
    }

    return doc.positionAt(index);
}

if (!enableDwsim) {
    suite.skip("VB.NET extension DWSIM smoke (skipped)", () => {
        // Enabled via VBNET_DWSIM=1.
    });
} else {
    suite("VB.NET extension DWSIM smoke (VS Code harness)", () => {
        let doc: vscode.TextDocument;

        suiteSetup(async function () {
            this.timeout(240000);

            const repoRoot = path.resolve(__dirname, "..", "..", "..", "..", "..");
            const workspaceRoot = process.env.FIXTURE_WORKSPACE
                ? (path.isAbsolute(process.env.FIXTURE_WORKSPACE)
                      ? process.env.FIXTURE_WORKSPACE
                      : path.resolve(repoRoot, process.env.FIXTURE_WORKSPACE))
                : path.resolve(repoRoot, "_external", "dwsim");
            const dwsimSolution = path.join(workspaceRoot, "DWSIM.sln");
            assert.ok(fs.existsSync(dwsimSolution), `DWSIM.sln not found at ${dwsimSolution}`);
        });

        test("extension installed and activated", async () => {
            const extension = vscode.extensions.getExtension(extensionId);
            assert.ok(extension, `Extension ${extensionId} is not installed.`);
            await extension!.activate();
        });

        test("open DWSIM file and run navigation services", async function () {
            this.timeout(240000);

            const repoRoot = path.resolve(__dirname, "..", "..", "..", "..", "..");
            const workspaceRoot = process.env.FIXTURE_WORKSPACE
                ? (path.isAbsolute(process.env.FIXTURE_WORKSPACE)
                      ? process.env.FIXTURE_WORKSPACE
                      : path.resolve(repoRoot, process.env.FIXTURE_WORKSPACE))
                : path.resolve(repoRoot, "_external", "dwsim");
            const filePath = path.join(workspaceRoot, "DWSIM", "ApplicationEvents.vb");
            assert.ok(fs.existsSync(filePath), `DWSIM file not found: ${filePath}`);

            doc = await vscode.workspace.openTextDocument(filePath);
            await vscode.window.showTextDocument(doc);

            const hoverPosition = getTokenPosition(doc, "MyApplication_Startup", 1);
            const definitionPosition = getTokenPosition(doc, "GetSplashScreen", 1);
            const referencesPosition = getTokenPosition(doc, "MyApplication_Startup", 1);

            const hover = await retryUntil(
                () =>
                    vscode.commands.executeCommand<vscode.Hover[]>(
                        "vscode.executeHoverProvider",
                        doc.uri,
                        hoverPosition
                    ),
                (items) => !!items && items.length > 0
            );
            assert.ok(hover && hover.length > 0, "Hover result was empty.");

            const definitions = await retryUntil(
                () =>
                    vscode.commands.executeCommand<vscode.Location[]>(
                        "vscode.executeDefinitionProvider",
                        doc.uri,
                        definitionPosition
                    ),
                (items) => !!items && items.length > 0
            );
            assert.ok(definitions && definitions.length > 0, "Definition result was empty.");

            const references = await retryUntil(
                () =>
                    vscode.commands.executeCommand<vscode.Location[]>(
                        "vscode.executeReferenceProvider",
                        doc.uri,
                        referencesPosition
                    ),
                (items) => !!items && items.length > 0
            );
            assert.ok(references && references.length > 0, "References result was empty.");

            const documentSymbols = await retryUntil(
                () =>
                    vscode.commands.executeCommand<vscode.DocumentSymbol[]>(
                        "vscode.executeDocumentSymbolProvider",
                        doc.uri
                    ),
                (items) => !!items && items.length > 0
            );
            assert.ok(documentSymbols && documentSymbols.length > 0, "Document symbols were empty.");

            const workspaceSymbols = await retryUntil(
                () =>
                    vscode.commands.executeCommand<vscode.SymbolInformation[]>(
                        "vscode.executeWorkspaceSymbolProvider",
                        "MyApplication"
                    ),
                (items) => !!items && items.length > 0
            );
            assert.ok(workspaceSymbols && workspaceSymbols.length > 0, "Workspace symbols were empty.");
        });
    });
}
