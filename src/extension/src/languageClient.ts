/*---------------------------------------------------------------------------------------------
 *  VB.NET Language Support
 *  Licensed under the MIT License. See LICENSE in the project root for license information.
 *--------------------------------------------------------------------------------------------*/

import * as vscode from 'vscode';
import * as net from 'net';
import {
    LanguageClient,
    LanguageClientOptions,
    ServerOptions,
    StreamInfo,
    State,
    Trace
} from 'vscode-languageclient/node';
import { PlatformInformation } from './platform';
import { ServerLauncher, TransportType, ServerStartResult } from './serverLauncher';
import { UriConverter } from './uriConverter';

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
    private initializationOptions: object | undefined;

    public readonly onStateChange = this.onStateChangeEmitter.event;

    constructor(
        private readonly channel: vscode.OutputChannel,
        private readonly traceChannel: vscode.OutputChannel,
        private readonly platformInfo: PlatformInformation,
        private readonly extensionPath: string
    ) {
        this.serverLauncher = new ServerLauncher(channel, platformInfo, extensionPath);
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

            // Start the server
            const serverResult = await this.serverLauncher.startServer(transportType);

            // Create the language client
            this.initializationOptions = await this.buildInitializationOptions();
            this.client = await this.createLanguageClient(serverResult, this.initializationOptions);

            // Register state change handler
            this.client.onDidChangeState((event) => {
                this.channel.appendLine(`Language client state: ${State[event.oldState]} -> ${State[event.newState]}`);
                this.onStateChangeEmitter.fire(event);
            });

            // Start the client
            await this.client.start();
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
                return {
                    reader: serverResult.process.stdout!,
                    writer: serverResult.process.stdin!
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
        if (this.client) {
            try {
                await this.client.stop();
                this.channel.appendLine('Language client stopped');
            } catch (error) {
                this.channel.appendLine(`Error stopping client: ${error}`);
            }
            this.client = undefined;
        }

        this.traceConfigDisposable?.dispose();
        this.traceConfigDisposable = undefined;

        await this.serverLauncher.stopServer();
    }

    /**
     * Restarts the language client and server.
     */
    public async restart(): Promise<void> {
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

    private async buildInitializationOptions(): Promise<object | undefined> {
        const workspaceFolders = vscode.workspace.workspaceFolders;
        if (!workspaceFolders || workspaceFolders.length === 0) {
            return undefined;
        }

        const config = vscode.workspace.getConfiguration('vbnet');
        const defaultExclude = '**/node_modules/**,**/.git/**,**/bower_components/**';
        const excludePattern = config.get<string>('workspace.projectFilesExcludePattern', defaultExclude);
        const maxProjectResults = config.get<number>('workspace.maxProjectResults', 250);
        const configuredSolution = (config.get<string>('workspace.solutionPath', '') || '').trim();
        const ignoreSolutionFiles = config.get<boolean>('workspace.ignoreSolutionFiles', false);

        if (configuredSolution) {
            return {
                workspace: {
                    solutionPath: configuredSolution,
                    ignoreSolutionFiles
                }
            };
        }

        const resources = await vscode.workspace.findFiles(
            '{**/*.sln,**/*.slnf,**/*.vbproj}',
            `{${excludePattern}}`
        );

        const workspaceRoot = workspaceFolders[0].uri.fsPath;
        const solutionCandidates = resources.filter((resource) => /\.slnf?$/i.test(resource.fsPath));
        const vbProjectFiles = resources.filter((resource) => /\.vbproj$/i.test(resource.fsPath));

        const solutionPath = await this.pickSolutionWithVbProjects(solutionCandidates);
        if (solutionPath && !ignoreSolutionFiles) {
            return {
                workspace: {
                    solutionPath,
                    ignoreSolutionFiles
                }
            };
        }

        const projectPaths = vbProjectFiles
            .map((resource) => resource.fsPath)
            .slice(0, Math.max(0, maxProjectResults));

        if (projectPaths.length === 0) {
            return {
                workspace: {
                    ignoreSolutionFiles
                }
            };
        }

        return {
            workspace: {
                projectPaths,
                ignoreSolutionFiles
            }
        };
    }

    private async pickSolutionWithVbProjects(candidates: vscode.Uri[]): Promise<string | undefined> {
        if (candidates.length === 0) {
            return undefined;
        }

        const filtered = [];
        for (const candidate of candidates) {
            try {
                const content = await vscode.workspace.fs.readFile(candidate);
                const text = Buffer.from(content).toString('utf8');
                if (text.toLowerCase().includes('.vbproj')) {
                    filtered.push(candidate);
                }
            } catch {
                filtered.push(candidate);
            }
        }

        if (filtered.length === 0) {
            return undefined;
        }

        filtered.sort((a, b) => a.fsPath.split(/\\|\//).length - b.fsPath.split(/\\|\//).length);
        return filtered[0].fsPath;
    }
}
