import * as assert from "assert";
import * as vscode from "vscode";

function parseFileList(value: string | undefined): string[] {
    if (!value) {
        return [];
    }

    return value
        .split(",")
        .map((entry) => entry.trim())
        .filter((entry) => entry.length > 0);
}

suite("VB.NET extension fuzz (VS Code harness)", () => {
    test("open files and probe symbols", async function () {
        const files = parseFileList(process.env.FUZZ_FILES);
        if (files.length === 0) {
            this.skip();
            return;
        }

        const requireSymbols = process.env.FUZZ_REQUIRE_SYMBOLS === "1";
        for (const filePath of files) {
            const doc = await vscode.workspace.openTextDocument(filePath);
            await vscode.window.showTextDocument(doc, { preview: false });

            const symbols = await retryForSymbols(() =>
                vscode.commands.executeCommand<vscode.DocumentSymbol[]>(
                    "vscode.executeDocumentSymbolProvider",
                    doc.uri
                )
            );

            const count = symbols?.length ?? 0;
            if (requireSymbols) {
                assert.ok(count > 0, `Document symbols empty for ${filePath}`);
            } else if (count === 0) {
                console.warn(`Document symbols empty for ${filePath}`);
            }
        }
    });

    test("workspace symbol query", async function () {
        const query = process.env.FUZZ_QUERY;
        if (!query) {
            this.skip();
            return;
        }

        const requireSymbols = process.env.FUZZ_REQUIRE_SYMBOLS === "1";
        const symbols = await retryForSymbols(() =>
            vscode.commands.executeCommand<vscode.SymbolInformation[]>(
                "vscode.executeWorkspaceSymbolProvider",
                query
            )
        );
        const count = symbols?.length ?? 0;
        if (requireSymbols) {
            assert.ok(count > 0, `Workspace symbols empty for query '${query}'`);
        } else if (count === 0) {
            console.warn(`Workspace symbols empty for query '${query}'`);
        }
    });
});

async function retryForSymbols<T extends { length?: number }>(
    action: () => Thenable<T>,
    timeoutMs = 20000,
    intervalMs = 1000
): Promise<T> {
    const deadline = Date.now() + timeoutMs;
    let last: T;
    while (Date.now() < deadline) {
        last = await Promise.resolve(action());
        const count = (last as unknown as { length?: number })?.length ?? 0;
        if (count > 0) {
            return last;
        }

        await new Promise((resolve) => setTimeout(resolve, intervalMs));
    }

    return last!;
}
