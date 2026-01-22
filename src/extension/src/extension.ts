/*---------------------------------------------------------------------------------------------
 *  VB.NET Language Support
 *  Licensed under the MIT License. See LICENSE in the project root for license information.
 *--------------------------------------------------------------------------------------------*/

import * as vscode from 'vscode';
import * as path from 'path';
import { PlatformInformation } from './platform';
import { VbNetLanguageClient } from './languageClient';
import { VbNetStatusBar } from './statusBar';
import { activateDebugging } from './debugger';

// Global instances
let languageClient: VbNetLanguageClient | undefined;
let statusBar: VbNetStatusBar | undefined;
let outputChannel: vscode.OutputChannel | undefined;
let traceChannel: vscode.OutputChannel | undefined;

/**
 * Extension activation entry point.
 * Called when VS Code activates the extension.
 */
export async function activate(context: vscode.ExtensionContext): Promise<VbNetExtensionApi | void> {
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

        const workspaceIsVirtual = vscode.workspace.workspaceFolders?.some(
            (folder) => folder.uri.scheme !== 'file'
        );
        if (workspaceIsVirtual) {
            outputChannel.appendLine('VB.NET Language Support is not available in virtual workspaces.');
            statusBar.setStatus('stopped');
            const action = await vscode.window.showInformationMessage(
                'VB.NET Language Support requires a local or remote workspace with files on disk (not a virtual workspace).',
                'Show Output'
            );
            if (action === 'Show Output') {
                outputChannel.show();
            }
            return;
        }

        if (vscode.env.uiKind === vscode.UIKind.Web) {
            outputChannel.appendLine('VB.NET Language Support is not available in VS Code Web or virtual workspaces.');
            statusBar.setStatus('stopped');
            const action = await vscode.window.showInformationMessage(
                'VB.NET Language Support requires a local or remote VS Code workspace (not vscode.dev/github.dev).',
                'Show Output'
            );
            if (action === 'Show Output') {
                outputChannel.show();
            }
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
            context.extensionPath,
            context.logUri.fsPath
        );

        // Update status bar on state changes
        languageClient.onStateChange((event) => {
            statusBar?.updateFromClientState(event.newState);
        });

        // Register commands
        registerCommands(context);

        // Register debugging integration
        activateDebugging(context, outputChannel, platformInfo);

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

    return {
        getClientState: () => languageClient?.getStateName() ?? 'stopped',
        waitForClientReady: (timeoutMs?: number) => {
            if (!languageClient) {
                return Promise.reject(new Error('Language client is not initialized.'));
            }
            return languageClient.waitForReady(timeoutMs);
        }
    };
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

    context.subscriptions.push(
        vscode.commands.registerCommand('vbnet.selectWorkspaceSolution', async () => {
            try {
                await selectWorkspaceSolution();
            } catch (error) {
                const message = error instanceof Error ? error.message : String(error);
                outputChannel?.appendLine(`Failed to select workspace solution: ${message}`);
                vscode.window.showErrorMessage(`Failed to select workspace solution: ${message}`);
            }
        })
    );
}

interface SolutionPickItem extends vscode.QuickPickItem {
    solutionPath?: string;
    action?: 'clear';
}

async function selectWorkspaceSolution(): Promise<void> {
    const workspaceFolders = vscode.workspace.workspaceFolders;
    if (!workspaceFolders || workspaceFolders.length === 0) {
        vscode.window.showWarningMessage('No workspace folder is open.');
        return;
    }

    const config = vscode.workspace.getConfiguration('vbnet');
    const defaultExclude = '**/node_modules/**,**/.git/**,**/bower_components/**';
    const excludePattern = config.get<string>('workspace.projectFilesExcludePattern', defaultExclude);

    const resources = await vscode.workspace.findFiles(
        '{**/*.sln,**/*.slnf,**/*.slnx}',
        `{${excludePattern}}`
    );

    if (resources.length === 0) {
        vscode.window.showInformationMessage('No solution files were found in this workspace.');
        return;
    }

    const workspaceRoot = workspaceFolders[0].uri.fsPath;
    const configuredSolution = (config.get<string>('workspace.solutionPath', '') || '').trim();
    const configuredResolved = configuredSolution
        ? path.normalize(path.resolve(workspaceRoot, configuredSolution))
        : '';

    const items: SolutionPickItem[] = [];
    items.push({
        label: 'Auto-detect',
        description: 'Clear workspace solution override',
        action: 'clear'
    });

    const candidates = resources
        .map((resource) => resource.fsPath)
        .sort((a, b) => a.localeCompare(b));

    for (const candidate of candidates) {
        const resolved = path.normalize(candidate);
        const relative = path.relative(workspaceRoot, candidate);
        const isRelative = relative && !relative.startsWith('..') && !path.isAbsolute(relative);
        const label = isRelative ? relative : path.basename(candidate);

        const hasVb = await solutionLikelyHasVbProjects(candidate);
        let description: string | undefined = undefined;
        if (configuredResolved && resolved === configuredResolved) {
            description = 'Current selection';
        } else if (!hasVb) {
            description = 'No .vbproj references detected';
        }

        items.push({
            label,
            description,
            detail: candidate,
            solutionPath: candidate
        });
    }

    const pick = await vscode.window.showQuickPick(items, {
        placeHolder: 'Select a workspace solution for VB.NET (clears auto-detection)',
        canPickMany: false
    });

    if (!pick) {
        return;
    }

    if (pick.action === 'clear') {
        await config.update('workspace.solutionPath', '', vscode.ConfigurationTarget.Workspace);
        outputChannel?.appendLine('Workspace solution override cleared (auto-detect enabled).');
        vscode.window.showInformationMessage('Workspace solution override cleared (auto-detect enabled).');
        return;
    }

    if (!pick.solutionPath) {
        return;
    }

    const relative = path.relative(workspaceRoot, pick.solutionPath);
    const configValue = relative && !relative.startsWith('..') && !path.isAbsolute(relative)
        ? relative
        : pick.solutionPath;

    await config.update('workspace.solutionPath', configValue, vscode.ConfigurationTarget.Workspace);
    outputChannel?.appendLine(`Workspace solution override set to: ${configValue}`);
    vscode.window.showInformationMessage(`Workspace solution set to ${configValue}`);
}

async function solutionLikelyHasVbProjects(solutionPath: string): Promise<boolean> {
    if (solutionPath.toLowerCase().endsWith('.slnx')) {
        return true;
    }

    try {
        const content = await vscode.workspace.fs.readFile(vscode.Uri.file(solutionPath));
        const text = Buffer.from(content).toString('utf8');
        return text.toLowerCase().includes('.vbproj');
    } catch {
        return true;
    }
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

export interface VbNetExtensionApi {
    getClientState(): string;
    waitForClientReady(timeoutMs?: number): Promise<void>;
}
