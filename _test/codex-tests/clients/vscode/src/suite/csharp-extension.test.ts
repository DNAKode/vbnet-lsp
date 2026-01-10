import * as path from "path";
import * as assert from "assert";
import * as vscode from "vscode";

const extensionId = process.env.EXTENSION_ID ?? "ms-dotnettools.csharp";

suite("C# extension LSP smoke (VS Code harness)", () => {
    async function retryUntil<T>(
        action: () => Thenable<T>,
        isReady: (value: T) => boolean,
        timeoutMs = 60000,
        intervalMs = 1000
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

    test("activate extension", async () => {
        const extension = vscode.extensions.getExtension(extensionId);
        assert.ok(extension, `Extension ${extensionId} is not installed.`);
        await extension!.activate();
    });

    test("hover/definition/completion/symbols", async () => {
        const repoRoot = path.resolve(__dirname, "..", "..", "..", "..", "..");
        const filePath = process.env.FIXTURE_FILE
            ? path.resolve(process.env.FIXTURE_FILE)
            : path.resolve(
                  repoRoot,
                  "_test",
                  "codex-tests",
                  "csharp-lsp",
                  "fixtures",
                  "basic",
                  "Basic",
                  "Class1.cs"
              );

        const doc = await vscode.workspace.openTextDocument(filePath);
        await vscode.window.showTextDocument(doc);

        const text = doc.getText();
        const marker = "/*caret*/";
        const markerIndex = text.indexOf(marker);
        assert.ok(markerIndex >= 0, "Caret marker not found.");

        const completionPosition = doc.positionAt(markerIndex);
        const addIndex = text.indexOf("Add(1, 2)");
        assert.ok(addIndex >= 0, "Add call site not found.");
        const symbolPosition = doc.positionAt(addIndex);

        const hover = await retryUntil(
            () =>
                vscode.commands.executeCommand<vscode.Hover[]>(
                    "vscode.executeHoverProvider",
                    doc.uri,
                    symbolPosition
                ),
            (items) => !!items && items.length > 0
        );
        assert.ok(hover && hover.length > 0, "Hover result was empty.");

        const definitions = await retryUntil(
            () =>
                vscode.commands.executeCommand<vscode.Location[]>(
                    "vscode.executeDefinitionProvider",
                    doc.uri,
                    symbolPosition
                ),
            (items) => !!items && items.length > 0
        );
        assert.ok(definitions && definitions.length > 0, "Definition result was empty.");

        const completions = await retryUntil(
            () =>
                vscode.commands.executeCommand<vscode.CompletionList>(
                    "vscode.executeCompletionItemProvider",
                    doc.uri,
                    completionPosition
                ),
            (list) => !!list && list.items.length > 0
        );
        assert.ok(completions && completions.items.length > 0, "Completion list was empty.");

        const symbols = await retryUntil(
            () =>
                vscode.commands.executeCommand<vscode.DocumentSymbol[]>(
                    "vscode.executeDocumentSymbolProvider",
                    doc.uri
                ),
            (items) => !!items && items.length > 0
        );
        assert.ok(symbols && symbols.length > 0, "Document symbols were empty.");
    });
});
