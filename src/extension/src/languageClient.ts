/*---------------------------------------------------------------------------------------------
 *  VB.NET Language Support
 *  Licensed under the MIT License. See LICENSE in the project root for license information.
 *--------------------------------------------------------------------------------------------*/

import * as vscode from 'vscode';
import * as net from 'net';
import * as path from 'path';
import {
    LanguageClient,
    LanguageClientOptions,
    ServerOptions,
    StreamInfo,
    State,
    Trace
} from 'vscode-languageclient/node';
import { PassThrough } from 'stream';
import { PlatformInformation } from './platform';
import { ServerLauncher, TransportType, ServerStartResult } from './serverLauncher';
import { UriConverter } from './uriConverter';
import { UnknownWorkspaceContext, WorkspaceContext } from './workspaceContext';

/**
 * Language client state change event.
 */
export interface LanguageClientStateChangeEvent {
    oldState: State;
    newState: State;
}

/**
 * Manages the VB.NET language client connection.
 */
export class VbNetLanguageClient implements vscode.Disposable {
    private client: LanguageClient | undefined;
    private serverLauncher: ServerLauncher;
    private readonly onStateChangeEmitter = new vscode.EventEmitter<LanguageClientStateChangeEvent>();
    private traceConfigDisposable: vscode.Disposable | undefined;
    private serverConfigDisposable: vscode.Disposable | undefined;
    private workspaceConfigDisposable: vscode.Disposable | undefined;
    private initializationOptions: object | undefined;
    private currentState: State = State.Stopped;
    private currentWorkspaceContext: WorkspaceContext = UnknownWorkspaceContext;
    private readonly onWorkspaceContextChangeEmitter = new vscode.EventEmitter<WorkspaceContext>();
    private ambiguousContextNotificationShown = false;
    private workspaceRestartTimer: NodeJS.Timeout | undefined;
    private restartPromise: Promise<void> | undefined;

    public readonly onStateChange = this.onStateChangeEmitter.event;
    public readonly onWorkspaceContextChange = this.onWorkspaceContextChangeEmitter.event;

    constructor(
        private readonly channel: vscode.OutputChannel,
        private readonly traceChannel: vscode.OutputChannel,
        private readonly platformInfo: PlatformInformation,
        private readonly extensionPath: string,
        private readonly logRoot: string
    ) {
        this.serverLauncher = new ServerLauncher(channel, platformInfo, extensionPath, logRoot);
    }

    /**
     * Starts the language client and connects to the server.
     */
    public async start(): Promise<void> {
        if (this.client) {
            this.channel.appendLine('Language client already started');
            return;
        }

        try {
            // Get transport type from configuration
            const config = vscode.workspace.getConfiguration('vbnet');
            const transportType = config.get<TransportType>('server.transportType', 'auto');
            const backend = config.get<'vbnet' | 'roslyn'>('server.backend', 'vbnet');

            // Start the server
            const serverResult = await this.serverLauncher.startServer(transportType, backend);

            // Create the language client
            this.initializationOptions = await this.buildInitializationOptions();
            this.client = await this.createLanguageClient(serverResult, this.initializationOptions);

            // Register state change handler
            this.client.onDidChangeState((event) => {
                this.channel.appendLine(`Language client state: ${State[event.oldState]} -> ${State[event.newState]}`);
                this.currentState = event.newState;
                this.onStateChangeEmitter.fire(event);
            });

            // Start the client
            await this.client.start();
            this.currentState = this.client.state;
            if (typeof (this.client as any).onReady === 'function') {
                await (this.client as any).onReady();
            }
            await this.updateTraceLevel();

            this.traceConfigDisposable = vscode.workspace.onDidChangeConfiguration((event) => {
                if (event.affectsConfiguration('vbnet.trace.server')) {
                    this.updateTraceLevel().catch((error) => {
                        this.channel.appendLine(`Failed to update trace level: ${error}`);
                    });
                }
            });

            this.serverConfigDisposable = vscode.workspace.onDidChangeConfiguration((event) => {
                if (event.affectsConfiguration('vbnet.server.backend') ||
                    event.affectsConfiguration('vbnet.server.path') ||
                    event.affectsConfiguration('vbnet.server.transportType') ||
                    event.affectsConfiguration('vbnet.output.language') ||
                    event.affectsConfiguration('vbnet.roslyn.server.path') ||
                    event.affectsConfiguration('vbnet.roslyn.server.extensionPath')) {
                    this.channel.appendLine('Server configuration changed. Restarting language server...');
                    this.restart().catch((error) => {
                        this.channel.appendLine(`Failed to restart language server after config change: ${error}`);
                    });
                }
            });

            this.workspaceConfigDisposable = vscode.workspace.onDidChangeConfiguration((event) => {
                if (event.affectsConfiguration('vbnet.workspace.solutionPath') ||
                    event.affectsConfiguration('vbnet.solutionPath') ||
                    event.affectsConfiguration('vbnet.workspace.projectPaths') ||
                    event.affectsConfiguration('vbnet.workspace.ignoreSolutionFiles') ||
                    event.affectsConfiguration('vbnet.workspace.projectSearchPaths') ||
                    event.affectsConfiguration('vbnet.workspace.excludePaths') ||
                    event.affectsConfiguration('vbnet.workspace.maxProjectResults')) {
                    this.scheduleWorkspaceContextRestart();
                }
            });
            this.channel.appendLine('VB.NET Language Client started successfully');

        } catch (error) {
            const message = error instanceof Error ? error.message : String(error);
            this.channel.appendLine(`Failed to start language client: ${message}`);
            throw error;
        }
    }

    /**
     * Creates the language client based on the server start result.
     */
    private async createLanguageClient(serverResult: ServerStartResult, initializationOptions?: object): Promise<LanguageClient> {
        const clientOptions = this.getClientOptions(initializationOptions);
        let serverOptions: ServerOptions;

        if (serverResult.transport === 'namedPipe' && serverResult.pipeName) {
            // Named pipe transport
            serverOptions = async (): Promise<StreamInfo> => {
                const socket = await this.serverLauncher.connectToPipe(serverResult.pipeName!);
                return {
                    reader: socket,
                    writer: socket
                };
            };
        } else {
            // Stdio transport - use the process streams directly
            serverOptions = async (): Promise<StreamInfo> => {
                const stdout = serverResult.process.stdout!;
                const stdin = serverResult.process.stdin!;

                const traceStreams = process.env.VBNET_TRACE_CLIENT_STREAMS;
                if (traceStreams) {
                    const verbose =
                        traceStreams === '2' ||
                        traceStreams.toLowerCase() === 'true' ||
                        traceStreams.toLowerCase() === 'verbose';
                    const reader = new PassThrough();
                    const writer = new PassThrough();

                    stdout.pipe(reader);
                    writer.pipe(stdin);

                    reader.on('data', (chunk: Buffer) => {
                        this.traceChannel.appendLine(`[client stream] recv ${chunk.length} bytes`);
                        if (verbose) {
                            const preview = chunk.toString('utf8', 0, Math.min(chunk.length, 200));
                            const hex = chunk.subarray(0, Math.min(chunk.length, 64)).toString('hex');
                            this.traceChannel.appendLine(`[client stream] recv preview: ${preview}`);
                            this.traceChannel.appendLine(`[client stream] recv hex: ${hex}`);
                        }
                    });
                    writer.on('data', (chunk: Buffer) => {
                        this.traceChannel.appendLine(`[client stream] send ${chunk.length} bytes`);
                        if (verbose) {
                            const preview = chunk.toString('utf8', 0, Math.min(chunk.length, 200));
                            const hex = chunk.subarray(0, Math.min(chunk.length, 64)).toString('hex');
                            this.traceChannel.appendLine(`[client stream] send preview: ${preview}`);
                            this.traceChannel.appendLine(`[client stream] send hex: ${hex}`);
                        }
                    });

                    return { reader, writer };
                }

                return {
                    reader: stdout,
                    writer: stdin
                };
            };
        }

        return new LanguageClient(
            'vbnet',
            'VB.NET Language Server',
            serverOptions,
            clientOptions
        );
    }

    /**
     * Gets the language client options.
     */
    private getClientOptions(initializationOptions?: object): LanguageClientOptions {
        const config = vscode.workspace.getConfiguration('vbnet');
        const traceLevel = config.get<string>('trace.server', 'off');

        return {
            documentSelector: [
                { scheme: 'file', language: 'vb' },
                { scheme: 'untitled', language: 'vb' }
            ],
            synchronize: {
                configurationSection: 'vbnet',
                // Notify the server about file changes to VB.NET project files
                fileEvents: [
                    vscode.workspace.createFileSystemWatcher('**/*.vb'),
                    vscode.workspace.createFileSystemWatcher('**/*.vbproj'),
                    vscode.workspace.createFileSystemWatcher('**/*.sln'),
                    vscode.workspace.createFileSystemWatcher('**/*.slnf'),
                    vscode.workspace.createFileSystemWatcher('**/*.slnx'),
                    vscode.workspace.createFileSystemWatcher('**/Directory.Build.props'),
                    vscode.workspace.createFileSystemWatcher('**/Directory.Build.targets')
                ]
            },
            outputChannel: this.channel,
            traceOutputChannel: this.traceChannel,
            uriConverters: {
                code2Protocol: UriConverter.serialize,
                protocol2Code: UriConverter.deserialize
            },
            middleware: {
                handleDiagnostics: (uri, diagnostics, next) => {
                    const config = vscode.workspace.getConfiguration('vbnet');
                    if (!config.get<boolean>('diagnostics.enable', true)) {
                        next(uri, []);
                        return;
                    }

                    next(uri, diagnostics);
                },
                provideCompletionItem: async (document, position, context, token, next) => {
                    const config = vscode.workspace.getConfiguration('vbnet');
                    if (!config.get<boolean>('completion.enable', true)) {
                        return null;
                    }

                    return next(document, position, context, token);
                }
            },
            initializationOptions
        };
    }

    /**
     * Stops the language client and server.
     */
    public async stop(): Promise<void> {
        if (this.workspaceRestartTimer) {
            clearTimeout(this.workspaceRestartTimer);
            this.workspaceRestartTimer = undefined;
        }

        const clientToStop = this.client;
        this.client = undefined;

        if (clientToStop) {
            try {
                await clientToStop.stop();
                this.channel.appendLine('Language client stopped');
            } catch (error) {
                this.channel.appendLine(`Error stopping client: ${error}`);
            }
        }

        this.traceConfigDisposable?.dispose();
        this.traceConfigDisposable = undefined;
        this.serverConfigDisposable?.dispose();
        this.serverConfigDisposable = undefined;
        this.workspaceConfigDisposable?.dispose();
        this.workspaceConfigDisposable = undefined;

        await this.serverLauncher.stopServer();
    }

    /**
     * Restarts the language client and server.
     */
    public async restart(): Promise<void> {
        if (this.restartPromise) {
            this.channel.appendLine('VB.NET Language Server restart already in progress');
            return this.restartPromise;
        }

        this.restartPromise = this.restartCore();
        try {
            await this.restartPromise;
        } finally {
            this.restartPromise = undefined;
        }
    }

    private async restartCore(): Promise<void> {
        this.channel.appendLine('Restarting VB.NET Language Server...');
        await this.stop();
        await this.start();
    }

    /**
     * Gets the current state of the language client.
     */
    public get state(): State {
        return this.client?.state ?? State.Stopped;
    }

    /**
     * Gets whether the client is running.
     */
    public get isRunning(): boolean {
        return this.client?.state === State.Running;
    }

    /**
     * Disposes of the language client resources.
     */
    public dispose(): void {
        this.onStateChangeEmitter.dispose();
        this.onWorkspaceContextChangeEmitter.dispose();
        this.stop().catch((error) => {
            this.channel.appendLine(`Error during disposal: ${error}`);
        });
    }

    private async updateTraceLevel(): Promise<void> {
        if (!this.client) {
            return;
        }

        const config = vscode.workspace.getConfiguration('vbnet');
        const traceLevel = config.get<string>('trace.server', 'off');

        const trace = traceLevel === 'verbose'
            ? Trace.Verbose
            : traceLevel === 'messages'
                ? Trace.Messages
                : Trace.Off;

        await this.client.setTrace(trace);
        this.channel.appendLine(`Language client trace level set to ${traceLevel}`);
    }

    private scheduleWorkspaceContextRestart(): void {
        if (this.workspaceRestartTimer) {
            clearTimeout(this.workspaceRestartTimer);
        }

        this.workspaceRestartTimer = setTimeout(() => {
            this.workspaceRestartTimer = undefined;
            this.channel.appendLine('Workspace context configuration changed. Restarting language server...');
            this.restart().catch((error) => {
                this.channel.appendLine(`Failed to restart language server after workspace context change: ${error}`);
            });
        }, 300);
    }

    private async buildInitializationOptions(): Promise<object | undefined> {
        const workspaceFolders = vscode.workspace.workspaceFolders;
        if (!workspaceFolders || workspaceFolders.length === 0) {
            this.setWorkspaceContext({ kind: 'empty' });
            return undefined;
        }

        const config = vscode.workspace.getConfiguration('vbnet');
        const defaultExclude = '**/node_modules/**,**/.git/**,**/bower_components/**';
        const excludePattern = config.get<string>('workspace.projectFilesExcludePattern', defaultExclude);
        const maxProjectResults = config.get<number>('workspace.maxProjectResults', 250);
        const configuredSolution = (config.get<string>('workspace.solutionPath', '') || '').trim();
        const legacySolution = (config.get<string>('solutionPath', '') || '').trim();
        const configuredProjectPaths = (config.get<string[]>('workspace.projectPaths', []) || [])
            .map((projectPath) => projectPath.trim())
            .filter((projectPath) => projectPath.length > 0);
        const ignoreSolutionFiles = config.get<boolean>('workspace.ignoreSolutionFiles', false);
        const projectSearchPaths = config.get<string[]>('workspace.projectSearchPaths', []);
        const excludePaths = config.get<string[]>('workspace.excludePaths', []);
        const loadProjectsOnStart = config.get<boolean>('loadProjectsOnStart', true);
        const maxProjectCount = config.get<number>('maxProjectCount', 100);
        const maxMemoryMB = config.get<number>('maxMemoryMB', 2048);
        const enableFormatting = config.get<boolean>('enableFormatting', true);
        const enableCodeActions = config.get<boolean>('enableCodeActions', true);
        const semanticTokens = config.get<boolean>('semanticTokens', true);
        const diagnosticsEnabled = config.get<boolean>('diagnostics.enable', true);
        const completionEnabled = config.get<boolean>('completion.enable', true);

        const workspaceOptions: Record<string, unknown> = {
            ignoreSolutionFiles,
            projectSearchPaths,
            excludePaths,
            maxProjectResults
        };

        const effectiveSolution = configuredSolution || legacySolution;
        if (effectiveSolution) {
            if (!configuredSolution && legacySolution) {
                this.channel.appendLine('Using legacy vbnet.solutionPath; prefer vbnet.workspace.solutionPath.');
            }
            workspaceOptions.solutionPath = effectiveSolution;
            this.setWorkspaceContext({
                kind: 'solution',
                workspaceRoot: workspaceFolders[0].uri.fsPath,
                solutionPath: path.resolve(workspaceFolders[0].uri.fsPath, effectiveSolution)
            });
        } else if (configuredProjectPaths.length > 0) {
            const resolvedProjectPaths = configuredProjectPaths.map((projectPath) =>
                path.resolve(workspaceFolders[0].uri.fsPath, projectPath)
            );
            workspaceOptions.projectPaths = configuredProjectPaths;
            workspaceOptions.ignoreSolutionFiles = true;
            this.setWorkspaceContext({
                kind: resolvedProjectPaths.length === 1 ? 'singleProject' : 'allProjects',
                workspaceRoot: workspaceFolders[0].uri.fsPath,
                projectPaths: resolvedProjectPaths
            });
        } else if (ignoreSolutionFiles) {
            const projectPaths = await this.findWorkspaceProjectPaths(excludePattern, maxProjectResults);
            if (projectPaths.length > 0) {
                workspaceOptions.projectPaths = projectPaths;
                this.setWorkspaceContext({
                    kind: projectPaths.length === 1 ? 'singleProject' : 'allProjects',
                    workspaceRoot: workspaceFolders[0].uri.fsPath,
                    projectPaths
                });
            } else {
                this.setWorkspaceContext({
                    kind: 'empty',
                    workspaceRoot: workspaceFolders[0].uri.fsPath
                });
            }
        } else {
            const resources = await vscode.workspace.findFiles(
                '{**/*.sln,**/*.slnf,**/*.slnx,**/*.vbproj}',
                `{${excludePattern}}`
            );

            const solutionCandidates = resources.filter((resource) => /\.(sln|slnf|slnx)$/i.test(resource.fsPath));
            const vbProjectFiles = this.sortUris(resources.filter((resource) => /\.vbproj$/i.test(resource.fsPath)));

            const vbSolutionCandidates = await this.filterSolutionsWithVbProjects(solutionCandidates);
            if (vbSolutionCandidates.length === 1) {
                const solutionPath = vbSolutionCandidates[0].fsPath;
                workspaceOptions.solutionPath = solutionPath;
                this.setWorkspaceContext({
                    kind: 'solution',
                    workspaceRoot: workspaceFolders[0].uri.fsPath,
                    solutionPath
                });
            } else if (vbSolutionCandidates.length > 1) {
                const projectPaths = vbProjectFiles
                    .map((resource) => resource.fsPath)
                    .slice(0, Math.max(0, maxProjectResults));
                workspaceOptions.ignoreSolutionFiles = true;
                if (projectPaths.length > 0) {
                    workspaceOptions.projectPaths = projectPaths;
                }
                const candidatePaths = vbSolutionCandidates.map((candidate) => candidate.fsPath);
                this.channel.appendLine('Multiple VB.NET solution candidates found; not selecting one automatically.');
                for (const candidatePath of candidatePaths) {
                    this.channel.appendLine(`  Candidate solution: ${candidatePath}`);
                }
                this.setWorkspaceContext({
                    kind: 'selectContext',
                    workspaceRoot: workspaceFolders[0].uri.fsPath,
                    projectPaths,
                    solutionCandidates: candidatePaths
                });
                this.showAmbiguousContextNotification().catch((error) => {
                    this.channel.appendLine(`Failed to show workspace context notification: ${error}`);
                });
            } else {
                const projectPaths = vbProjectFiles
                    .map((resource) => resource.fsPath)
                    .slice(0, Math.max(0, maxProjectResults));
                if (projectPaths.length > 0) {
                    workspaceOptions.projectPaths = projectPaths;
                    this.setWorkspaceContext({
                        kind: projectPaths.length === 1 ? 'singleProject' : 'allProjects',
                        workspaceRoot: workspaceFolders[0].uri.fsPath,
                        projectPaths
                    });
                } else {
                    this.setWorkspaceContext({
                        kind: 'empty',
                        workspaceRoot: workspaceFolders[0].uri.fsPath
                    });
                }
            }
        }

        return {
            diagnostics: { enable: diagnosticsEnabled },
            completion: { enable: completionEnabled },
            enableFormatting,
            enableCodeActions,
            semanticTokens,
            loadProjectsOnStart,
            maxProjectCount,
            maxMemoryMB,
            workspace: workspaceOptions
        };
    }

    private async filterSolutionsWithVbProjects(candidates: vscode.Uri[]): Promise<vscode.Uri[]> {
        if (candidates.length === 0) {
            return [];
        }

        const filtered: vscode.Uri[] = [];
        for (const candidate of this.sortUris(candidates)) {
            try {
                const extension = path.extname(candidate.fsPath).toLowerCase();
                if (extension === '.slnx') {
                    filtered.push(candidate);
                    continue;
                }
                const content = await vscode.workspace.fs.readFile(candidate);
                const text = Buffer.from(content).toString('utf8');
                if (text.toLowerCase().includes('.vbproj')) {
                    filtered.push(candidate);
                }
            } catch {
                filtered.push(candidate);
            }
        }

        return filtered;
    }

    private async findWorkspaceProjectPaths(excludePattern: string, maxProjectResults: number): Promise<string[]> {
        const resources = await vscode.workspace.findFiles(
            '**/*.vbproj',
            `{${excludePattern}}`
        );
        return this.sortUris(resources)
            .map((resource) => resource.fsPath)
            .slice(0, Math.max(0, maxProjectResults));
    }

    private sortUris(resources: vscode.Uri[]): vscode.Uri[] {
        return resources.slice().sort((a, b) => {
            const depth = a.fsPath.split(/\\|\//).length - b.fsPath.split(/\\|\//).length;
            return depth !== 0 ? depth : a.fsPath.localeCompare(b.fsPath);
        });
    }

    private setWorkspaceContext(context: WorkspaceContext): void {
        this.currentWorkspaceContext = context;
        this.onWorkspaceContextChangeEmitter.fire(context);
    }

    private async showAmbiguousContextNotification(): Promise<void> {
        if (this.ambiguousContextNotificationShown) {
            return;
        }

        this.ambiguousContextNotificationShown = true;
        const action = await vscode.window.showInformationMessage(
            'Multiple VB.NET solutions were found. Select the workspace context to avoid ambiguous project loading.',
            'Select Context',
            'Show Output'
        );

        if (action === 'Select Context') {
            await vscode.commands.executeCommand('vbnet.selectWorkspaceContext');
        } else if (action === 'Show Output') {
            this.channel.show();
        }
    }

    public getStateName(): string {
        return State[this.currentState];
    }

    public getWorkspaceContext(): WorkspaceContext {
        return this.currentWorkspaceContext;
    }

    public async waitForReady(timeoutMs = 30000): Promise<void> {
        if (!this.client) {
            throw new Error('Language client not started.');
        }

        if (this.currentState === State.Running) {
            return;
        }

        await new Promise<void>((resolve, reject) => {
            const timeout = setTimeout(() => {
                reject(new Error(`Language client did not reach Running within ${timeoutMs} ms (state=${this.getStateName()}).`));
            }, timeoutMs);

            const disposable = this.onStateChange((event) => {
                if (event.newState === State.Running) {
                    clearTimeout(timeout);
                    disposable.dispose();
                    resolve();
                }
            });
        });
    }

    public async reloadWorkspace(): Promise<void> {
        if (!this.client) {
            throw new Error('Language client not started.');
        }

        await this.client.sendNotification('vbnet/reloadWorkspace');
    }
}
