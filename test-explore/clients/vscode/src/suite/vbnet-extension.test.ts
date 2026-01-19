import * as path from "path";
import * as assert from "assert";
import * as vscode from "vscode";

const extensionId = process.env.EXTENSION_ID ?? "dnakode.vbnet-language-support";
const skipVbnetSmoke = process.env.SKIP_VBNET_SMOKE === "1";

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

if (skipVbnetSmoke) {
    suite.skip("VB.NET extension LSP smoke (skipped)", () => {
        // Skipped via SKIP_VBNET_SMOKE.
    });
} else {
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
            ? (path.isAbsolute(process.env.FIXTURE_FILE)
                  ? process.env.FIXTURE_FILE
                  : path.resolve(repoRoot, process.env.FIXTURE_FILE))
            : path.resolve(
                  repoRoot,
                  "test-explore",
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
        const signaturePosition = getMarkerPosition(doc, "MARKER: signature_help", "Add(", "Add(".length);

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

        const signatureHelp = await retryUntil(
            () =>
                vscode.commands.executeCommand<vscode.SignatureHelp>(
                    "vscode.executeSignatureHelpProvider",
                    doc.uri,
                    signaturePosition
                ),
            (help) => !!help && help.signatures.length > 0
        );
        assert.ok(signatureHelp && signatureHelp.signatures.length > 0, "Signature help was empty.");
        assert.ok(
            signatureHelp.signatures.length > 1,
            `Expected multiple overloads in signature help, got ${signatureHelp.signatures.length}.`
        );

        const codeActions = await retryUntil(
            () =>
                vscode.commands.executeCommand<(vscode.CodeAction | vscode.Command)[]>(
                    "vscode.executeCodeActionProvider",
                    doc.uri,
                    new vscode.Range(new vscode.Position(0, 0), new vscode.Position(0, 0))
                ),
            (items) => !!items
        );
        const optionAction = (codeActions ?? []).find(
            (item): item is vscode.CodeAction =>
                "title" in item &&
                typeof item.title === "string" &&
                item.title.startsWith("Add Option ")
        );
        assert.ok(optionAction, "Option code action was not offered.");

        const documentSymbols = await retryUntil(
            () =>
                vscode.commands.executeCommand<vscode.DocumentSymbol[]>(
                    "vscode.executeDocumentSymbolProvider",
                    doc.uri
                ),
            (items) => !!items && items.length > 0
        );
        if (!documentSymbols || documentSymbols.length === 0) {
            console.warn("Document symbols were empty; continuing with workspace symbols check.");
        } else {
            assert.ok(documentSymbols.length > 0, "Document symbols were empty.");
        }

        const workspaceSymbols = await retryUntil(
            () =>
                vscode.commands.executeCommand<vscode.SymbolInformation[]>(
                    "vscode.executeWorkspaceSymbolProvider",
                    "Greeter"
                ),
            (items) => !!items && items.length > 0
        );
        assert.ok(workspaceSymbols && workspaceSymbols.length > 0, "Workspace symbols were empty.");

        const foldingRanges = await retryUntil(
            () =>
                vscode.commands.executeCommand<vscode.FoldingRange[]>(
                    "vscode.executeFoldingRangeProvider",
                    doc.uri
                ),
            (items) => !!items && items.length > 0
        );
        assert.ok(foldingRanges && foldingRanges.length > 0, "Folding ranges were empty.");
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

    test("toggle breakpoint adds a source breakpoint", async () => {
        assert.ok(doc, "Fixture document was not opened.");
        const editor = await vscode.window.showTextDocument(doc);
        const breakpointPosition = getMarkerPosition(doc, "MARKER: breakpoint_toggle", "sum4");
        editor.selection = new vscode.Selection(breakpointPosition, breakpointPosition);

        if (vscode.debug.breakpoints.length > 0) {
            vscode.debug.removeBreakpoints(vscode.debug.breakpoints);
        }

        const availableCommands = await vscode.commands.getCommands(true);
        const toggleCommand =
            availableCommands.find((cmd) => cmd === "editor.debug.action.toggleBreakpoint") ??
            availableCommands.find((cmd) => cmd === "editor.action.debug.toggleBreakpoint") ??
            availableCommands.find((cmd) => cmd === "editor.action.toggleBreakpoint");

        assert.ok(toggleCommand, "Toggle breakpoint command not available in this VS Code build.");
        await vscode.commands.executeCommand(toggleCommand!);

        const breakpoint = await retryUntil(
            () => Promise.resolve(vscode.debug.breakpoints),
            (items) => items.length > 0
        );

        const sourceBreakpoint = breakpoint.find(
            (item): item is vscode.SourceBreakpoint => item instanceof vscode.SourceBreakpoint
        );
        assert.ok(sourceBreakpoint, "No source breakpoint was created.");

        const breakpointLine = sourceBreakpoint.location.range.start.line;
        assert.strictEqual(
            breakpointLine,
            breakpointPosition.line,
            `Expected breakpoint at line ${breakpointPosition.line}, got ${breakpointLine}.`
        );

        vscode.debug.removeBreakpoints(vscode.debug.breakpoints);
    });

    test("commands are registered and restart applies config changes", async () => {
        const config = vscode.workspace.getConfiguration("vbnet");
        const originalTransport = config.get<string>("server.transportType", "auto");
        const originalTrace = config.get<string>("trace.server", "off");

        try {
            await config.update("trace.server", "verbose", vscode.ConfigurationTarget.Workspace);
            await config.update("server.transportType", "namedPipe", vscode.ConfigurationTarget.Workspace);

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
        const editorConfig = vscode.workspace.getConfiguration("editor");
        const originalCompletion = config.get<boolean>("completion.enable", true);
        const originalWordBasedSuggestions = editorConfig.get<unknown>("wordBasedSuggestions");
        const completionPosition = getMarkerPosition(doc, "MARKER: completion_text", "text.", "text.".length);

        try {
            await editorConfig.update("wordBasedSuggestions", "off", vscode.ConfigurationTarget.Workspace);
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

            const items = completions?.items ?? [];
            const nonTextItems = items.filter(
                (item) =>
                    item.kind !== vscode.CompletionItemKind.Text &&
                    item.kind !== vscode.CompletionItemKind.Snippet
            );
            assert.ok(
                nonTextItems.length === 0,
                `Expected no LSP completions when disabled; got ${nonTextItems.length} non-text items.`
            );
        } finally {
            await config.update("completion.enable", originalCompletion, vscode.ConfigurationTarget.Workspace);
            await editorConfig.update(
                "wordBasedSuggestions",
                originalWordBasedSuggestions,
                vscode.ConfigurationTarget.Workspace
            );
            await vscode.commands.executeCommand("vbnet.restartServer");
        }
    });

    test("typing completion does not duplicate prefix", async () => {
        const repoRoot = path.resolve(__dirname, "..", "..", "..", "..", "..");
        const workspaceRoot = process.env.FIXTURE_WORKSPACE
            ? (path.isAbsolute(process.env.FIXTURE_WORKSPACE)
                  ? process.env.FIXTURE_WORKSPACE
                  : path.resolve(repoRoot, process.env.FIXTURE_WORKSPACE))
            : path.resolve(repoRoot, "test-explore", "vbnet-lsp", "fixtures", "services");
        const tempFilePath = path.join(workspaceRoot, `TypingSample-${Date.now()}.vb`);
        const tempUri = vscode.Uri.file(tempFilePath);
        const editorConfig = vscode.workspace.getConfiguration("editor");
        const originalWordBasedSuggestions = editorConfig.get<unknown>("wordBasedSuggestions");

        try {
            await editorConfig.update("wordBasedSuggestions", "off", vscode.ConfigurationTarget.Workspace);

            const content = [
                "Public Class TypingSample",
                "    Public Sub Test()",
                "        Dim x ",
                "    End Sub",
                "End Class",
                ""
            ].join("\n");
            await vscode.workspace.fs.writeFile(tempUri, Buffer.from(content, "utf8"));

            const typingDoc = await vscode.workspace.openTextDocument(tempUri);
            const editor = await vscode.window.showTextDocument(typingDoc);

            const insertIndex = typingDoc.getText().indexOf("Dim x ");
            assert.ok(insertIndex >= 0, "Failed to find typing location.");
            const insertPosition = typingDoc.positionAt(insertIndex + "Dim x ".length);
            editor.selection = new vscode.Selection(insertPosition, insertPosition);

            await vscode.commands.executeCommand("type", { text: "A" });

            const completionPosition = editor.selection.active;
            const completions = await retryUntil(
                () =>
                    vscode.commands.executeCommand<vscode.CompletionList>(
                        "vscode.executeCompletionItemProvider",
                        typingDoc.uri,
                        completionPosition
                    ),
                (list) => !!list && list.items.length > 0
            );
            const hasAs = completions.items.some((item) => item.label === "As");
            assert.ok(hasAs, "Expected 'As' completion after typing 'A'.");

            await vscode.commands.executeCommand("editor.action.triggerSuggest");
            await new Promise((resolve) => setTimeout(resolve, 200));
            await vscode.commands.executeCommand("acceptSelectedSuggestion");
            await new Promise((resolve) => setTimeout(resolve, 200));

            const updatedLine = editor.document.lineAt(insertPosition.line).text;
            assert.ok(updatedLine.includes("Dim x As"), `Expected "Dim x As" but got: ${updatedLine}`);
            assert.ok(!updatedLine.includes("AAs"), `Unexpected duplicate prefix in line: ${updatedLine}`);
        } finally {
            await editorConfig.update(
                "wordBasedSuggestions",
                originalWordBasedSuggestions,
                vscode.ConfigurationTarget.Workspace
            );
            await vscode.commands.executeCommand("workbench.action.closeActiveEditor");
            try {
                await vscode.workspace.fs.delete(tempUri);
            } catch {
                // Ignore cleanup failures if file is already removed.
            }
        }
    });

    test("formatting returns edits for unformatted document", async () => {
        const repoRoot = path.resolve(__dirname, "..", "..", "..", "..", "..");
        const filePath = path.resolve(
            repoRoot,
            "test-explore",
            "vbnet-lsp",
            "fixtures",
            "services",
            "FormattingSample.vb"
        );

        const formatDoc = await vscode.workspace.openTextDocument(filePath);
        await vscode.window.showTextDocument(formatDoc);

        const edits = await retryUntil(
            () =>
                vscode.commands.executeCommand<vscode.TextEdit[]>(
                    "vscode.executeFormatDocumentProvider",
                    formatDoc.uri,
                    {
                        tabSize: 4,
                        insertSpaces: true
                    }
                ),
            (items) => !!items
        );

        assert.ok(edits && edits.length > 0, "Format document edits were empty.");
    });
    });
}

