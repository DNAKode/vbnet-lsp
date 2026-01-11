import * as path from "path";
import * as assert from "assert";
import * as vscode from "vscode";

const extensionId = process.env.EXTENSION_ID ?? "dnakode.vbnet-language-support";

async function retryUntil<T>(
    action: () => Thenable<T>,
    isReady: (value: T) => boolean,
    timeoutMs = 90000,
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

function getMarkerPosition(
    doc: vscode.TextDocument,
    marker: string,
    token: string,
    tokenOffset = 0
): vscode.Position {
    const text = doc.getText();
    const markerIndex = text.indexOf(marker);
    assert.ok(markerIndex >= 0, `Marker not found: ${marker}`);

    const lineStart = text.lastIndexOf("\n", markerIndex);
    const lineEnd = text.indexOf("\n", markerIndex);
    const lineText = text.slice(lineStart + 1, lineEnd === -1 ? text.length : lineEnd);
    const tokenIndexInLine = lineText.indexOf(token);
    assert.ok(tokenIndexInLine >= 0, `Token not found for marker ${marker}: ${token}`);

    const tokenIndex = lineStart + 1 + tokenIndexInLine + tokenOffset;
    return doc.positionAt(tokenIndex);
}

suite("VB.NET extension LSP smoke (VS Code harness)", () => {
    let doc: vscode.TextDocument;

    test("extension installed and activated", async () => {
        const extension = vscode.extensions.getExtension(extensionId);
        assert.ok(extension, `Extension ${extensionId} is not installed.`);
        await extension!.activate();
    });

    test("open fixture and run core services", async () => {
        const repoRoot = path.resolve(__dirname, "..", "..", "..", "..", "..");
        const filePath = process.env.FIXTURE_FILE
            ? path.resolve(process.env.FIXTURE_FILE)
            : path.resolve(
                  repoRoot,
                  "_test",
                  "codex-tests",
                  "vbnet-lsp",
                  "fixtures",
                  "services",
                  "ServiceSamples.vb"
              );

        doc = await vscode.workspace.openTextDocument(filePath);
        await vscode.window.showTextDocument(doc);

        const completionPosition = getMarkerPosition(doc, "MARKER: completion_text", "text.", "text.".length);
        const extensionCompletionPosition = getMarkerPosition(
            doc,
            "MARKER: completion_extension",
            "sum.",
            "sum.".length
        );
        const hoverPosition = getMarkerPosition(doc, "MARKER: hover_text", "sum");
        const definitionPosition = getMarkerPosition(doc, "MARKER: definition_add", "Add");
        const referencesPosition = getMarkerPosition(doc, "MARKER: references_greet", "Greet");

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

        const extensionCompletions = await retryUntil(
            () =>
                vscode.commands.executeCommand<vscode.CompletionList>(
                    "vscode.executeCompletionItemProvider",
                    doc.uri,
                    extensionCompletionPosition
                ),
            (list) => !!list && list.items.length > 0
        );
        const extensionItem = extensionCompletions.items.find((item) => item.label === "DoubleIt");
        assert.ok(extensionItem, "Extension method completion DoubleIt not found.");

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
                    "Greeter"
                ),
            (items) => !!items && items.length > 0
        );
        assert.ok(workspaceSymbols && workspaceSymbols.length > 0, "Workspace symbols were empty.");
    });

    test("rename provider returns workspace edits", async () => {
        assert.ok(doc, "Fixture document was not opened.");
        const renamePosition = getMarkerPosition(doc, "MARKER: hover_text", "sum");

        const edit = await retryUntil(
            () =>
                vscode.commands.executeCommand<vscode.WorkspaceEdit>(
                    "vscode.executeDocumentRenameProvider",
                    doc.uri,
                    renamePosition,
                    "total"
                ),
            (result) => !!result && result.size > 0
        );
        assert.ok(edit && edit.size > 0, "Rename workspace edit was empty.");
    });

    test("commands are registered and restart applies config changes", async () => {
        const config = vscode.workspace.getConfiguration("vbnet");
        const originalTransport = config.get<string>("server.transportType", "auto");
        const originalTrace = config.get<string>("trace.server", "off");

        try {
            await config.update("trace.server", "verbose", vscode.ConfigurationTarget.Workspace);
            await config.update("server.transportType", "stdio", vscode.ConfigurationTarget.Workspace);

            await vscode.commands.executeCommand("vbnet.restartServer");

            const hoverPosition = getMarkerPosition(doc, "MARKER: hover_text", "sum");
            const hover = await retryUntil(
                () =>
                    vscode.commands.executeCommand<vscode.Hover[]>(
                        "vscode.executeHoverProvider",
                        doc.uri,
                        hoverPosition
                    ),
                (items) => !!items && items.length > 0
            );
            assert.ok(hover && hover.length > 0, "Hover failed after restart.");

            await vscode.commands.executeCommand("vbnet.showOutputChannel");
        } finally {
            await config.update("trace.server", originalTrace, vscode.ConfigurationTarget.Workspace);
            await config.update("server.transportType", originalTransport, vscode.ConfigurationTarget.Workspace);
        }
    });

    test("completion respects configuration toggle", async () => {
        assert.ok(doc, "Fixture document was not opened.");
        const config = vscode.workspace.getConfiguration("vbnet");
        const originalCompletion = config.get<boolean>("completion.enable", true);
        const completionPosition = getMarkerPosition(doc, "MARKER: completion_text", "text.", "text.".length);

        try {
            await config.update("completion.enable", false, vscode.ConfigurationTarget.Workspace);
            await vscode.commands.executeCommand("vbnet.restartServer");

            const completions = await retryUntil(
                () =>
                    vscode.commands.executeCommand<vscode.CompletionList>(
                        "vscode.executeCompletionItemProvider",
                        doc.uri,
                        completionPosition
                    ),
                (list) => !!list
            );

            const count = completions?.items?.length ?? 0;
            assert.ok(count === 0, `Expected no completions when disabled, got ${count}.`);
        } finally {
            await config.update("completion.enable", originalCompletion, vscode.ConfigurationTarget.Workspace);
            await vscode.commands.executeCommand("vbnet.restartServer");
        }
    });
});
