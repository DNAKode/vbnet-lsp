/*---------------------------------------------------------------------------------------------
 *  VB.NET Language Support
 *  Licensed under the MIT License. See LICENSE in the project root for license information.
 *--------------------------------------------------------------------------------------------*/

import * as vscode from 'vscode';
import { PlatformInformation } from './platform';
import { VbNetLanguageClient } from './languageClient';
import { VbNetStatusBar } from './statusBar';

// Global instances
let languageClient: VbNetLanguageClient | undefined;
let statusBar: VbNetStatusBar | undefined;
let outputChannel: vscode.OutputChannel | undefined;
let traceChannel: vscode.OutputChannel | undefined;

/**
 * Extension activation entry point.
 * Called when VS Code activates the extension.
 */
export async function activate(context: vscode.ExtensionContext): Promise<void> {
    const startTime = process.hrtime();

    // Create output channels
    outputChannel = vscode.window.createOutputChannel('VB.NET', { log: true });
    traceChannel = vscode.window.createOutputChannel('VB.NET LSP Trace', { log: true });

    outputChannel.appendLine('VB.NET Language Support activating...');

    // Create status bar
    statusBar = new VbNetStatusBar();
    statusBar.show();
    context.subscriptions.push(statusBar);

    try {
        // Check workspace trust
        if (vscode.workspace.isTrusted === false) {
            outputChannel.appendLine('Workspace is not trusted. Running in limited mode.');
            statusBar.setStatus('stopped');

            // Register handler for when trust is granted
            context.subscriptions.push(
                vscode.workspace.onDidGrantWorkspaceTrust(() => {
                    outputChannel?.appendLine('Workspace trust granted. Restarting extension...');
                    vscode.commands.executeCommand('workbench.action.restartExtensionHost');
                })
            );
            return;
        }

        // Get platform information
        const platformInfo = await PlatformInformation.getCurrent();
        outputChannel.appendLine(`Platform: ${platformInfo.toString()}`);

        // Create and start the language client
        languageClient = new VbNetLanguageClient(
            outputChannel,
            traceChannel,
            platformInfo,
            context.extensionPath
        );

        // Update status bar on state changes
        languageClient.onStateChange((event) => {
            statusBar?.updateFromClientState(event.newState);
        });

        // Register commands
        registerCommands(context);

        // Start the language client
        await languageClient.start();

        // Calculate activation time
        const elapsed = process.hrtime(startTime);
        const elapsedMs = (elapsed[0] * 1000 + elapsed[1] / 1000000).toFixed(0);
        outputChannel.appendLine(`VB.NET Language Support activated in ${elapsedMs}ms`);

    } catch (error) {
        const message = error instanceof Error ? error.message : String(error);
        outputChannel.appendLine(`Activation failed: ${message}`);
        statusBar.setStatus('error');

        // Show error message to user
        const action = await vscode.window.showErrorMessage(
            `VB.NET Language Support failed to start: ${message}`,
            'Show Output',
            'Retry'
        );

        if (action === 'Show Output') {
            outputChannel.show();
        } else if (action === 'Retry') {
            // Restart extension host
            vscode.commands.executeCommand('workbench.action.restartExtensionHost');
        }
    }

    // Add language client to subscriptions for cleanup
    if (languageClient) {
        context.subscriptions.push(languageClient);
    }
}

/**
 * Registers extension commands.
 */
function registerCommands(context: vscode.ExtensionContext): void {
    // Restart server command
    context.subscriptions.push(
        vscode.commands.registerCommand('vbnet.restartServer', async () => {
            if (languageClient) {
                statusBar?.setStatus('initializing');
                try {
                    await languageClient.restart();
                    vscode.window.showInformationMessage('VB.NET Language Server restarted successfully');
                } catch (error) {
                    const message = error instanceof Error ? error.message : String(error);
                    vscode.window.showErrorMessage(`Failed to restart server: ${message}`);
                    statusBar?.setStatus('error');
                }
            } else {
                vscode.window.showWarningMessage('VB.NET Language Server is not running');
            }
        })
    );

    // Show output channel command
    context.subscriptions.push(
        vscode.commands.registerCommand('vbnet.showOutputChannel', () => {
            outputChannel?.show();
        })
    );
}

/**
 * Extension deactivation.
 * Called when the extension is deactivated.
 */
export async function deactivate(): Promise<void> {
    outputChannel?.appendLine('VB.NET Language Support deactivating...');

    if (languageClient) {
        await languageClient.stop();
        languageClient = undefined;
    }

    outputChannel?.appendLine('VB.NET Language Support deactivated');
}
