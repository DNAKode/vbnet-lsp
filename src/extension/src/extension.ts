/*---------------------------------------------------------------------------------------------
 *  VB.NET Language Support
 *  Licensed under the MIT License. See LICENSE in the project root for license information.
 *--------------------------------------------------------------------------------------------*/

import * as vscode from 'vscode';
import * as path from 'path';
import * as cp from 'child_process';
import * as fs from 'fs';
import { PlatformInformation } from './platform';
import { VbNetLanguageClient } from './languageClient';
import { VbNetStatusBar } from './statusBar';
import { activateDebugging } from './debugger';

// Global instances
let languageClient: VbNetLanguageClient | undefined;
let statusBar: VbNetStatusBar | undefined;
let outputChannel: vscode.OutputChannel | undefined;
let traceChannel: vscode.OutputChannel | undefined;
let testController: vscode.TestController | undefined;
let testRefreshTimer: NodeJS.Timeout | undefined;

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

        // Create the language client
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
        languageClient.onWorkspaceContextChange((context) => {
            statusBar?.setWorkspaceContext(context);
        });

        // Register commands
        registerCommands(context);

        // Register debugging integration
        activateDebugging(context, outputChannel, platformInfo);
        activateTestExplorer(context);

        const startLanguageClientIfEnabled = async () => {
            const config = vscode.workspace.getConfiguration('vbnet');
            const enabled = config.get<boolean>('enable', true);
            if (!enabled) {
                outputChannel?.appendLine('VB.NET language server is disabled (vbnet.enable = false).');
                statusBar?.setStatus('stopped');
                return;
            }

            await languageClient!.start();
        };

        context.subscriptions.push(
            vscode.workspace.onDidChangeConfiguration(async (event) => {
                if (!event.affectsConfiguration('vbnet.enable')) {
                    return;
                }

                const config = vscode.workspace.getConfiguration('vbnet');
                const enabled = config.get<boolean>('enable', true);
                try {
                    if (enabled) {
                        outputChannel?.appendLine('VB.NET language server enabled; starting.');
                        statusBar?.setStatus('initializing');
                        await startLanguageClientIfEnabled();
                    } else {
                        outputChannel?.appendLine('VB.NET language server disabled; stopping.');
                        statusBar?.setStatus('stopped');
                        await languageClient?.stop();
                    }
                } catch (error) {
                    const message = error instanceof Error ? error.message : String(error);
                    outputChannel?.appendLine(`Failed to apply vbnet.enable change: ${message}`);
                    vscode.window.showErrorMessage(`Failed to apply vbnet.enable change: ${message}`);
                }
            })
        );

        // Start the language client if enabled
        await startLanguageClientIfEnabled();

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
        getWorkspaceContext: () => languageClient?.getWorkspaceContext(),
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
        vscode.commands.registerCommand('vbnet.selectWorkspaceContext', async () => {
            try {
                await selectWorkspaceContext();
            } catch (error) {
                const message = error instanceof Error ? error.message : String(error);
                outputChannel?.appendLine(`Failed to select workspace context: ${message}`);
                vscode.window.showErrorMessage(`Failed to select workspace context: ${message}`);
            }
        })
    );

    context.subscriptions.push(
        vscode.commands.registerCommand('vbnet.selectWorkspaceSolution', async () => {
            try {
                await selectWorkspaceContext();
            } catch (error) {
                const message = error instanceof Error ? error.message : String(error);
                outputChannel?.appendLine(`Failed to select workspace solution: ${message}`);
                vscode.window.showErrorMessage(`Failed to select workspace solution: ${message}`);
            }
        })
    );

    context.subscriptions.push(
        vscode.commands.registerCommand('vbnet.showLogs', async () => {
            outputChannel?.show();
            traceChannel?.show();
            outputChannel?.appendLine(`Log directory: ${context.logUri.fsPath}`);
        })
    );

    context.subscriptions.push(
        vscode.commands.registerCommand('vbnet.toggleLspTrace', async () => {
            try {
                await toggleLspTrace();
            } catch (error) {
                const message = error instanceof Error ? error.message : String(error);
                outputChannel?.appendLine(`Failed to toggle LSP trace: ${message}`);
                vscode.window.showErrorMessage(`Failed to toggle LSP trace: ${message}`);
            }
        })
    );

    context.subscriptions.push(
        vscode.commands.registerCommand('vbnet.restoreWorkspace', async () => {
            try {
                await restoreWorkspace();
            } catch (error) {
                const message = error instanceof Error ? error.message : String(error);
                outputChannel?.appendLine(`Failed to restore workspace: ${message}`);
                vscode.window.showErrorMessage(`Failed to restore workspace: ${message}`);
            }
        })
    );

    context.subscriptions.push(
        vscode.commands.registerCommand('vbnet.restoreProject', async () => {
            try {
                await restoreProject();
            } catch (error) {
                const message = error instanceof Error ? error.message : String(error);
                outputChannel?.appendLine(`Failed to restore project: ${message}`);
                vscode.window.showErrorMessage(`Failed to restore project: ${message}`);
            }
        })
    );

    context.subscriptions.push(
        vscode.commands.registerCommand('vbnet.reloadWorkspace', async () => {
            if (!languageClient) {
                vscode.window.showWarningMessage('VB.NET Language Server is not running.');
                return;
            }

            try {
                await vscode.window.withProgress(
                    {
                        location: vscode.ProgressLocation.Notification,
                        title: 'Reloading VB.NET workspace',
                        cancellable: false
                    },
                    () => languageClient!.reloadWorkspace()
                );
                vscode.window.showInformationMessage('VB.NET workspace reload requested.');
            } catch (error) {
                const message = error instanceof Error ? error.message : String(error);
                outputChannel?.appendLine(`Failed to reload workspace: ${message}`);
                vscode.window.showErrorMessage(`Failed to reload workspace: ${message}`);
            }
        })
    );

    context.subscriptions.push(
        vscode.commands.registerCommand('vbnet.attachToProcess', async () => {
            try {
                await attachToProcess();
            } catch (error) {
                const message = error instanceof Error ? error.message : String(error);
                outputChannel?.appendLine(`Failed to attach to process: ${message}`);
                vscode.window.showErrorMessage(`Failed to attach to process: ${message}`);
            }
        })
    );

    context.subscriptions.push(
        vscode.commands.registerCommand('vbnet.runTestsInContext', async () => {
            try {
                await runTestsInContext();
            } catch (error) {
                const message = error instanceof Error ? error.message : String(error);
                outputChannel?.appendLine(`Failed to run tests: ${message}`);
                vscode.window.showErrorMessage(`Failed to run tests: ${message}`);
            }
        })
    );

    context.subscriptions.push(
        vscode.commands.registerCommand('vbnet.debugTestsInContext', async () => {
            try {
                vscode.window.showInformationMessage('Debug test support is in preview; running dotnet test for now.');
                await runTestsInContext(true);
            } catch (error) {
                const message = error instanceof Error ? error.message : String(error);
                outputChannel?.appendLine(`Failed to debug tests: ${message}`);
                vscode.window.showErrorMessage(`Failed to debug tests: ${message}`);
            }
        })
    );
}

interface WorkspaceContextPickItem extends vscode.QuickPickItem {
    solutionPath?: string;
    projectPath?: string;
    action?: 'auto' | 'allProjects';
}

interface ProjectPickItem extends vscode.QuickPickItem {
    projectPath: string;
}

interface ProcessInfo {
    pid: number;
    name: string;
    commandLine?: string;
}

interface ProcessPickItem extends vscode.QuickPickItem {
    pid: number;
}

interface TestPickItem extends vscode.QuickPickItem {
    targetPath?: string;
}

function activateTestExplorer(context: vscode.ExtensionContext): void {
    if (testController) {
        return;
    }

    testController = vscode.tests.createTestController('vbnetTests', 'VB.NET Tests');
    context.subscriptions.push(testController);

    const runProfile = testController.createRunProfile(
        'Run Tests',
        vscode.TestRunProfileKind.Run,
        (request, token) => runTestRequest(request, token),
        true
    );
    context.subscriptions.push(runProfile);

    const watcherPatterns = [
        '**/*.sln',
        '**/*.slnf',
        '**/*.slnx',
        '**/*.vbproj'
    ];
    for (const pattern of watcherPatterns) {
        const watcher = vscode.workspace.createFileSystemWatcher(pattern);
        watcher.onDidCreate(() => scheduleTestExplorerRefresh());
        watcher.onDidChange(() => scheduleTestExplorerRefresh());
        watcher.onDidDelete(() => scheduleTestExplorerRefresh());
        context.subscriptions.push(watcher);
    }

    context.subscriptions.push(
        vscode.workspace.onDidChangeWorkspaceFolders(() => scheduleTestExplorerRefresh())
    );

    context.subscriptions.push(
        vscode.workspace.onDidChangeConfiguration((event) => {
            if (event.affectsConfiguration('vbnet.workspace.projectFilesExcludePattern')) {
                scheduleTestExplorerRefresh();
            }
        })
    );

    scheduleTestExplorerRefresh();
}

function scheduleTestExplorerRefresh(): void {
    if (!testController) {
        return;
    }

    if (testRefreshTimer) {
        clearTimeout(testRefreshTimer);
    }

    testRefreshTimer = setTimeout(() => {
        refreshTestExplorer().catch((error) => {
            const message = error instanceof Error ? error.message : String(error);
            outputChannel?.appendLine(`Failed to refresh test explorer: ${message}`);
        });
    }, 300);
}

async function refreshTestExplorer(): Promise<void> {
    if (!testController) {
        return;
    }

    testController.items.forEach((item) => {
        testController?.items.delete(item.id);
    });

    const workspaceFolders = vscode.workspace.workspaceFolders ?? [];
    if (workspaceFolders.length === 0) {
        return;
    }

    const excludePattern = getProjectExcludePattern();
    const config = vscode.workspace.getConfiguration('vbnet');
    const maxProjectResults = config.get<number>('workspace.maxProjectResults', 250);

    for (const folder of workspaceFolders) {
        const workspaceItem = testController.createTestItem(
            `workspace:${folder.uri.toString()}`,
            folder.name,
            folder.uri
        );
        testController.items.add(workspaceItem);

        const solutions = await findWorkspaceSolutionsForFolder(folder, excludePattern);
        for (const solutionPath of solutions) {
            const relative = path.relative(folder.uri.fsPath, solutionPath);
            const label = relative && !relative.startsWith('..') && !path.isAbsolute(relative)
                ? relative
                : path.basename(solutionPath);
            const item = testController.createTestItem(
                `solution:${solutionPath}`,
                label,
                vscode.Uri.file(solutionPath)
            );
            workspaceItem.children.add(item);
        }

        const projects = await findWorkspaceProjectsForFolder(folder, excludePattern, maxProjectResults);
        for (const projectPath of projects) {
            const relative = path.relative(folder.uri.fsPath, projectPath);
            const label = relative && !relative.startsWith('..') && !path.isAbsolute(relative)
                ? relative
                : path.basename(projectPath);
            const item = testController.createTestItem(
                `project:${projectPath}`,
                label,
                vscode.Uri.file(projectPath)
            );
            workspaceItem.children.add(item);
        }
    }
}

async function runTestRequest(request: vscode.TestRunRequest, token: vscode.CancellationToken): Promise<void> {
    if (!testController) {
        return;
    }

    const run = testController.createTestRun(request);
    try {
        const queue = collectRunnableTestItems(request);
        for (const item of queue) {
            if (token.isCancellationRequested) {
                run.skipped(item);
                continue;
            }

            run.started(item);
            try {
                await runDotnetTestForItem(item, run, token);
                run.passed(item);
            } catch (error) {
                const message = error instanceof Error ? error.message : String(error);
                run.failed(item, new vscode.TestMessage(message));
            }
        }
    } finally {
        run.end();
    }
}

function collectRunnableTestItems(request: vscode.TestRunRequest): vscode.TestItem[] {
    if (!testController) {
        return [];
    }

    const roots: vscode.TestItem[] = [];
    const excludeIds = new Set((request.exclude ?? []).map((item) => item.id));

    if (request.include && request.include.length > 0) {
        roots.push(...request.include);
    } else {
        testController.items.forEach((item) => roots.push(item));
    }

    const result: vscode.TestItem[] = [];
    const addRunnable = (item: vscode.TestItem, isExcluded: boolean) => {
        const excluded = isExcluded || excludeIds.has(item.id);
        if (excluded) {
            return;
        }

        if (item.children.size === 0) {
            result.push(item);
            return;
        }

        item.children.forEach((child) => addRunnable(child, excluded));
    };

    for (const item of roots) {
        addRunnable(item, false);
    }

    return result;
}

async function runDotnetTestForItem(
    item: vscode.TestItem,
    run: vscode.TestRun,
    token: vscode.CancellationToken
): Promise<void> {
    const workspaceFolder = item.uri ? vscode.workspace.getWorkspaceFolder(item.uri) : undefined;
    const workspaceRoot = workspaceFolder?.uri.fsPath
        ?? vscode.workspace.workspaceFolders?.[0]?.uri.fsPath
        ?? process.cwd();

    const args = ['test'];
    const targetPath = item.uri?.fsPath;
    if (targetPath && fsPathExists(targetPath)) {
        args.push(targetPath);
    }

    run.appendOutput(`[vbnet] dotnet ${args.join(' ')}\r\n`, undefined, item);
    outputChannel?.show();
    outputChannel?.appendLine(`Running: dotnet ${args.join(' ')}`);

    await new Promise<void>((resolve, reject) => {
        const child = cp.spawn('dotnet', args, { cwd: workspaceRoot, env: process.env });

        const onExit = (code: number | null) => {
            if (code === 0) {
                resolve();
                return;
            }
            reject(new Error(`dotnet test exited with code ${code}`));
        };

        child.stdout?.on('data', (data: Buffer) => {
            const text = data.toString();
            run.appendOutput(text, undefined, item);
            outputChannel?.appendLine(text.trimEnd());
        });

        child.stderr?.on('data', (data: Buffer) => {
            const text = data.toString();
            run.appendOutput(text, undefined, item);
            outputChannel?.appendLine(text.trimEnd());
        });

        child.on('error', (error) => {
            reject(error);
        });

        child.on('exit', onExit);

        token.onCancellationRequested(() => {
            try {
                child.kill();
            } catch {
                // Best-effort cancellation.
            }
        });
    });
}

function getProjectExcludePattern(): string {
    const config = vscode.workspace.getConfiguration('vbnet');
    const defaultExclude = '**/node_modules/**,**/.git/**,**/bower_components/**';
    const excludePattern = config.get<string>('workspace.projectFilesExcludePattern', defaultExclude);
    return `{${excludePattern}}`;
}

async function findWorkspaceSolutionsForFolder(
    folder: vscode.WorkspaceFolder,
    excludePattern: string
): Promise<string[]> {
    const patterns = ['**/*.sln', '**/*.slnf', '**/*.slnx'];
    const results: vscode.Uri[] = [];

    for (const pattern of patterns) {
        const uris = await vscode.workspace.findFiles(
            new vscode.RelativePattern(folder, pattern),
            excludePattern
        );
        results.push(...uris);
    }

    const unique = new Map<string, string>();
    for (const uri of results) {
        unique.set(uri.fsPath, uri.fsPath);
    }

    return Array.from(unique.values());
}

async function findWorkspaceProjectsForFolder(
    folder: vscode.WorkspaceFolder,
    excludePattern: string,
    maxProjectResults: number
): Promise<string[]> {
    const resources = await vscode.workspace.findFiles(
        new vscode.RelativePattern(folder, '**/*.vbproj'),
        excludePattern
    );
    const paths = resources.map((resource) => resource.fsPath);
    if (maxProjectResults > 0 && paths.length > maxProjectResults) {
        return paths.slice(0, maxProjectResults);
    }

    return paths;
}

async function selectWorkspaceContext(): Promise<void> {
    const workspaceFolders = vscode.workspace.workspaceFolders;
    if (!workspaceFolders || workspaceFolders.length === 0) {
        vscode.window.showWarningMessage('No workspace folder is open.');
        return;
    }

    const config = vscode.workspace.getConfiguration('vbnet');
    const defaultExclude = '**/node_modules/**,**/.git/**,**/bower_components/**';
    const excludePattern = config.get<string>('workspace.projectFilesExcludePattern', defaultExclude);

    const resources = await vscode.workspace.findFiles(
        '{**/*.sln,**/*.slnf,**/*.slnx,**/*.vbproj}',
        `{${excludePattern}}`
    );

    if (resources.length === 0) {
        vscode.window.showInformationMessage('No VB.NET solution or project files were found in this workspace.');
        return;
    }

    const workspaceRoot = workspaceFolders[0].uri.fsPath;
    const configuredSolution = (config.get<string>('workspace.solutionPath', '') || '').trim();
    const configuredResolved = configuredSolution
        ? path.normalize(path.resolve(workspaceRoot, configuredSolution))
        : '';

    const configuredProjects = config.get<string[]>('workspace.projectPaths', []) || [];
    const configuredProjectResolved = new Set(
        configuredProjects.map((projectPath) => path.normalize(path.resolve(workspaceRoot, projectPath)))
    );

    const items: WorkspaceContextPickItem[] = [];
    items.push({
        label: 'Auto-detect',
        description: 'Clear explicit solution/project context',
        action: 'auto'
    });

    const solutionCandidates = resources
        .filter((resource) => /\.(sln|slnf|slnx)$/i.test(resource.fsPath))
        .map((resource) => resource.fsPath)
        .sort((a, b) => a.localeCompare(b));

    const projectCandidates = resources
        .filter((resource) => /\.vbproj$/i.test(resource.fsPath))
        .map((resource) => resource.fsPath)
        .sort((a, b) => a.localeCompare(b));

    if (projectCandidates.length > 0) {
        items.push({
            label: `Workspace Dev Mode (${projectCandidates.length} project${projectCandidates.length === 1 ? '' : 's'})`,
            description: 'Load discovered VB.NET projects without a solution',
            action: 'allProjects'
        });
    }

    for (const candidate of solutionCandidates) {
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
            label: `Solution: ${label}`,
            description,
            detail: candidate,
            solutionPath: candidate
        });
    }

    for (const candidate of projectCandidates) {
        const resolved = path.normalize(candidate);
        const relative = path.relative(workspaceRoot, candidate);
        const isRelative = relative && !relative.startsWith('..') && !path.isAbsolute(relative);
        const label = isRelative ? relative : path.basename(candidate);
        const description = configuredProjectResolved.has(resolved)
            ? 'Current project context'
            : undefined;

        items.push({
            label: `Project: ${label}`,
            description,
            detail: candidate,
            projectPath: candidate
        });
    }

    const pick = await vscode.window.showQuickPick(items, {
        placeHolder: 'Select the VB.NET workspace context',
        canPickMany: false
    });

    if (!pick) {
        return;
    }

    if (pick.action === 'auto') {
        await updateWorkspaceContextSettings('', [], false);
        outputChannel?.appendLine('VB.NET workspace context override cleared (auto-detect enabled).');
        vscode.window.showInformationMessage('VB.NET workspace context override cleared (auto-detect enabled).');
        await restartLanguageClientForContextChange();
        return;
    }

    if (pick.action === 'allProjects') {
        await updateWorkspaceContextSettings('', [], true);
        outputChannel?.appendLine('VB.NET workspace context set to Workspace Dev Mode (all discovered projects).');
        vscode.window.showInformationMessage('VB.NET workspace context set to Workspace Dev Mode.');
        await restartLanguageClientForContextChange();
        return;
    }

    if (pick.solutionPath) {
        const relative = path.relative(workspaceRoot, pick.solutionPath);
        const configValue = relative && !relative.startsWith('..') && !path.isAbsolute(relative)
            ? relative
            : pick.solutionPath;

        await updateWorkspaceContextSettings(configValue, [], false);
        outputChannel?.appendLine(`VB.NET workspace context set to solution: ${configValue}`);
        vscode.window.showInformationMessage(`VB.NET workspace context set to ${configValue}`);
        await restartLanguageClientForContextChange();
        return;
    }

    if (pick.projectPath) {
        const relative = path.relative(workspaceRoot, pick.projectPath);
        const configValue = relative && !relative.startsWith('..') && !path.isAbsolute(relative)
            ? relative
            : pick.projectPath;

        await updateWorkspaceContextSettings('', [configValue], true);
        outputChannel?.appendLine(`VB.NET workspace context set to project: ${configValue}`);
        vscode.window.showInformationMessage(`VB.NET workspace context set to ${configValue}`);
        await restartLanguageClientForContextChange();
    }
}

async function updateWorkspaceContextSettings(
    solutionPath: string,
    projectPaths: string[],
    ignoreSolutionFiles: boolean
): Promise<void> {
    const config = vscode.workspace.getConfiguration('vbnet');
    await config.update('workspace.solutionPath', solutionPath, vscode.ConfigurationTarget.Workspace);
    await config.update('workspace.projectPaths', projectPaths, vscode.ConfigurationTarget.Workspace);
    await config.update('workspace.ignoreSolutionFiles', ignoreSolutionFiles, vscode.ConfigurationTarget.Workspace);
}

async function restartLanguageClientForContextChange(): Promise<void> {
    if (!languageClient) {
        return;
    }

    statusBar?.setStatus('initializing');
    await languageClient.restart();
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

async function toggleLspTrace(): Promise<void> {
    const config = vscode.workspace.getConfiguration('vbnet');
    const current = config.get<string>('trace.server', 'off');
    const enabled = current === 'off';
    const next = enabled ? 'verbose' : 'off';

    await config.update('trace.server', next, vscode.ConfigurationTarget.Workspace);

    if (enabled) {
        traceChannel?.show();
    }

    const message = enabled
        ? 'VB.NET LSP trace enabled (verbose).'
        : 'VB.NET LSP trace disabled.';
    outputChannel?.appendLine(message);
    vscode.window.showInformationMessage(message);
}

async function restoreWorkspace(): Promise<void> {
    const workspaceRoot = getWorkspaceRoot();
    if (!workspaceRoot) {
        return;
    }

    const configuredSolution = getConfiguredSolutionPath(workspaceRoot);
    const configuredProjects = getConfiguredProjectPaths(workspaceRoot);
    const ignoreSolutionFiles = getIgnoreSolutionFiles();
    const candidateSolution = configuredSolution
        ?? (ignoreSolutionFiles || configuredProjects.length > 0
            ? undefined
            : await pickWorkspaceSolutionCandidate(workspaceRoot, false));
    const args = ['restore'];
    if (candidateSolution) {
        args.push(candidateSolution);
    } else if (configuredProjects.length === 1) {
        args.push(configuredProjects[0]);
    }

    const label = candidateSolution
        ? `Restoring ${path.basename(candidateSolution)}`
        : configuredProjects.length === 1
            ? `Restoring ${path.basename(configuredProjects[0])}`
            : 'Restoring workspace';
    await runDotnetCommand(args, workspaceRoot, label, 'Restore completed.');
}

async function restoreProject(): Promise<void> {
    const workspaceRoot = getWorkspaceRoot();
    if (!workspaceRoot) {
        return;
    }

    const projects = await findWorkspaceProjects();
    if (projects.length === 0) {
        vscode.window.showInformationMessage('No VB.NET project files were found in this workspace.');
        return;
    }

    const items: ProjectPickItem[] = projects.map((projectPath) => {
        const relative = path.relative(workspaceRoot, projectPath);
        const label = relative && !relative.startsWith('..') && !path.isAbsolute(relative)
            ? relative
            : path.basename(projectPath);
        return {
            label,
            detail: projectPath,
            projectPath
        };
    });

    const pick = await vscode.window.showQuickPick(items, {
        placeHolder: 'Select a VB.NET project to restore',
        canPickMany: false
    });

    if (!pick) {
        return;
    }

    await runDotnetCommand(['restore', pick.projectPath], workspaceRoot, `Restoring ${pick.label}`, 'Restore completed.');
}

async function runTestsInContext(debug: boolean = false): Promise<void> {
    const workspaceRoot = getWorkspaceRoot();
    if (!workspaceRoot) {
        return;
    }

    const target = await resolveTestTarget(workspaceRoot);
    const args = ['test'];
    if (target) {
        args.push(target);
    }

    const title = target
        ? `${debug ? 'Debugging' : 'Running'} tests: ${path.basename(target)}`
        : `${debug ? 'Debugging' : 'Running'} tests`;

    const successMessage = debug
        ? 'Test run completed (debug attach not yet implemented).'
        : 'Test run completed.';

    await runDotnetCommand(args, workspaceRoot, title, successMessage);
}

function getWorkspaceRoot(): string | undefined {
    const workspaceFolders = vscode.workspace.workspaceFolders;
    if (!workspaceFolders || workspaceFolders.length === 0) {
        vscode.window.showWarningMessage('No workspace folder is open.');
        return undefined;
    }

    return workspaceFolders[0].uri.fsPath;
}

function getConfiguredSolutionPath(workspaceRoot: string): string | undefined {
    const config = vscode.workspace.getConfiguration('vbnet');
    const configuredSolution = (config.get<string>('workspace.solutionPath', '') || '').trim();
    const legacySolution = (config.get<string>('solutionPath', '') || '').trim();
    const effectiveSolution = configuredSolution || legacySolution;
    if (!effectiveSolution) {
        return undefined;
    }

    const resolved = path.resolve(workspaceRoot, effectiveSolution);
    if (resolved && fsPathExists(resolved)) {
        return resolved;
    }

    outputChannel?.appendLine(`Configured solution path not found: ${effectiveSolution}`);
    return undefined;
}

function getConfiguredProjectPaths(workspaceRoot: string): string[] {
    const config = vscode.workspace.getConfiguration('vbnet');
    const configuredProjects = config.get<string[]>('workspace.projectPaths', []) || [];
    const resolved: string[] = [];

    for (const projectPath of configuredProjects) {
        const trimmed = (projectPath || '').trim();
        if (!trimmed) {
            continue;
        }

        const fullPath = path.resolve(workspaceRoot, trimmed);
        if (fsPathExists(fullPath) && fullPath.toLowerCase().endsWith('.vbproj')) {
            resolved.push(fullPath);
        } else {
            outputChannel?.appendLine(`Configured project path not found: ${trimmed}`);
        }
    }

    return resolved;
}

function getIgnoreSolutionFiles(): boolean {
    const config = vscode.workspace.getConfiguration('vbnet');
    return config.get<boolean>('workspace.ignoreSolutionFiles', false);
}

async function pickWorkspaceSolutionCandidate(workspaceRoot: string, allowMultipleFallback: boolean = true): Promise<string | undefined> {
    const solutions = await findWorkspaceSolutions();
    if (solutions.length === 0) {
        return undefined;
    }

    const withVb = [];
    for (const solutionPath of solutions) {
        if (await solutionLikelyHasVbProjects(solutionPath)) {
            withVb.push(solutionPath);
        }
    }

    const candidates = (withVb.length > 0 ? withVb : solutions)
        .sort((a, b) => {
            const depthA = a.split(path.sep).length;
            const depthB = b.split(path.sep).length;
            const depthCompare = depthA - depthB;
            return depthCompare !== 0 ? depthCompare : a.localeCompare(b);
        });

    if (!allowMultipleFallback && candidates.length > 1) {
        return undefined;
    }

    return candidates[0];
}

async function resolveTestTarget(workspaceRoot: string): Promise<string | undefined> {
    const activeFile = vscode.window.activeTextEditor?.document?.uri?.fsPath;
    if (activeFile && activeFile.startsWith(workspaceRoot) && activeFile.toLowerCase().endsWith('.vb')) {
        const nearestProject = findNearestProjectForFile(activeFile, workspaceRoot);
        if (nearestProject) {
            return nearestProject;
        }
    }

    const configuredSolution = getConfiguredSolutionPath(workspaceRoot);
    if (configuredSolution) {
        return configuredSolution;
    }

    const configuredProjects = getConfiguredProjectPaths(workspaceRoot);
    if (configuredProjects.length === 1) {
        return configuredProjects[0];
    }

    const ignoreSolutionFiles = getIgnoreSolutionFiles();
    const candidateSolution = ignoreSolutionFiles || configuredProjects.length > 0
        ? undefined
        : await pickWorkspaceSolutionCandidate(workspaceRoot, false);
    if (candidateSolution) {
        return candidateSolution;
    }

    const projects = await findWorkspaceProjects();
    if (projects.length === 1) {
        return projects[0];
    }

    if (projects.length > 1) {
        const items: TestPickItem[] = projects.map((projectPath) => {
            const relative = path.relative(workspaceRoot, projectPath);
            const label = relative && !relative.startsWith('..') && !path.isAbsolute(relative)
                ? relative
                : path.basename(projectPath);
            return {
                label,
                detail: projectPath,
                targetPath: projectPath
            };
        });

        items.unshift({
            label: 'Workspace (dotnet test)',
            description: 'Run tests without an explicit project/solution'
        });

        const pick = await vscode.window.showQuickPick(items, {
            placeHolder: 'Select a project/solution to test',
            matchOnDescription: true
        });

        return pick?.targetPath;
    }

    return undefined;
}

async function findWorkspaceSolutions(): Promise<string[]> {
    const config = vscode.workspace.getConfiguration('vbnet');
    const defaultExclude = '**/node_modules/**,**/.git/**,**/bower_components/**';
    const excludePattern = config.get<string>('workspace.projectFilesExcludePattern', defaultExclude);

    const resources = await vscode.workspace.findFiles(
        '{**/*.sln,**/*.slnf,**/*.slnx}',
        `{${excludePattern}}`
    );

    return resources.map((resource) => resource.fsPath);
}

async function findWorkspaceProjects(): Promise<string[]> {
    const config = vscode.workspace.getConfiguration('vbnet');
    const defaultExclude = '**/node_modules/**,**/.git/**,**/bower_components/**';
    const excludePattern = config.get<string>('workspace.projectFilesExcludePattern', defaultExclude);

    const resources = await vscode.workspace.findFiles(
        '**/*.vbproj',
        `{${excludePattern}}`
    );

    return resources.map((resource) => resource.fsPath);
}

async function runDotnetCommand(args: string[], cwd: string, title: string, successMessage?: string): Promise<void> {
    outputChannel?.show();
    outputChannel?.appendLine(`Running: dotnet ${args.join(' ')}`);

    await vscode.window.withProgress(
        {
            location: vscode.ProgressLocation.Notification,
            title,
            cancellable: false
        },
        () => new Promise<void>((resolve, reject) => {
            const child = cp.spawn('dotnet', args, { cwd, env: process.env });

            child.stdout?.on('data', (data: Buffer) => {
                outputChannel?.appendLine(data.toString().trimEnd());
            });

            child.stderr?.on('data', (data: Buffer) => {
                outputChannel?.appendLine(data.toString().trimEnd());
            });

            child.on('error', (error) => {
                reject(error);
            });

            child.on('exit', (code) => {
                if (code === 0) {
                    resolve();
                    return;
                }
                reject(new Error(`dotnet ${args[0]} exited with code ${code}`));
            });
        })
    );

    if (successMessage) {
        vscode.window.showInformationMessage(successMessage);
    }
}

function fsPathExists(filePath: string): boolean {
    try {
        return fs.statSync(filePath).isFile();
    } catch {
        return false;
    }
}

function findNearestProjectForFile(filePath: string, workspaceRoot: string): string | undefined {
    let current = path.dirname(filePath);
    const root = path.resolve(workspaceRoot);

    while (current.startsWith(root)) {
        try {
            const entries = fs.readdirSync(current);
            const candidates = entries.filter((entry) => entry.toLowerCase().endsWith('.vbproj'));
            if (candidates.length > 0) {
                return path.join(current, candidates[0]);
            }
        } catch {
            return undefined;
        }

        if (current === root) {
            break;
        }
        const parent = path.dirname(current);
        if (parent === current) {
            break;
        }
        current = parent;
    }

    return undefined;
}

async function attachToProcess(): Promise<void> {
    const workspaceFolder = vscode.workspace.workspaceFolders?.[0];
    const processes = await listProcesses();

    if (processes.length === 0) {
        vscode.window.showInformationMessage('No running processes found.');
        return;
    }

    processes.sort((a, b) => {
        const nameCompare = a.name.localeCompare(b.name);
        if (nameCompare !== 0) {
            return nameCompare;
        }
        return a.pid - b.pid;
    });

    const items: ProcessPickItem[] = processes.map((proc) => ({
        label: `${proc.name} (${proc.pid})`,
        description: proc.commandLine,
        pid: proc.pid
    }));

    const pick = await vscode.window.showQuickPick(items, {
        placeHolder: 'Select a process to attach netcoredbg',
        matchOnDescription: true
    });

    if (!pick) {
        return;
    }

    const attachConfig: vscode.DebugConfiguration = {
        name: `VB.NET Attach (${pick.pid})`,
        type: 'vbnet',
        request: 'attach',
        processId: pick.pid
    };

    const started = await vscode.debug.startDebugging(workspaceFolder, attachConfig);
    if (!started) {
        vscode.window.showErrorMessage('Failed to start VB.NET attach session.');
    }
}

async function listProcesses(): Promise<ProcessInfo[]> {
    if (process.platform === 'win32') {
        return await listWindowsProcesses();
    }

    return await listUnixProcesses();
}

async function listWindowsProcesses(): Promise<ProcessInfo[]> {
    const output = await execCommand('tasklist', ['/FO', 'CSV', '/NH']);
    const lines = output.split(/\r?\n/).map((line) => line.trim()).filter((line) => line.length > 0);
    const results: ProcessInfo[] = [];

    for (const line of lines) {
        const values = line.split('","').map((value) => value.replace(/^"|"$/g, ''));
        if (values.length < 2) {
            continue;
        }
        const pid = Number.parseInt(values[1], 10);
        if (Number.isNaN(pid)) {
            continue;
        }
        results.push({
            pid,
            name: values[0],
            commandLine: values[0]
        });
    }

    return results;
}

async function listUnixProcesses(): Promise<ProcessInfo[]> {
    const output = await execCommand('ps', ['-ax', '-o', 'pid=,comm=,args=']);
    const lines = output.split(/\r?\n/).map((line) => line.trim()).filter((line) => line.length > 0);
    const results: ProcessInfo[] = [];

    for (const line of lines) {
        const match = line.match(/^(\d+)\s+(\S+)\s*(.*)$/);
        if (!match) {
            continue;
        }
        const pid = Number.parseInt(match[1], 10);
        if (Number.isNaN(pid)) {
            continue;
        }
        results.push({
            pid,
            name: match[2],
            commandLine: match[3]
        });
    }

    return results;
}

async function execCommand(command: string, args: string[]): Promise<string> {
    return await new Promise((resolve, reject) => {
        cp.execFile(command, args, { encoding: 'utf8' }, (error, stdout, stderr) => {
            if (error) {
                reject(error);
                return;
            }
            if (stderr) {
                outputChannel?.appendLine(stderr.trim());
            }
            resolve(stdout ?? '');
        });
    });
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
    getWorkspaceContext(): object | undefined;
    waitForClientReady(timeoutMs?: number): Promise<void>;
}
