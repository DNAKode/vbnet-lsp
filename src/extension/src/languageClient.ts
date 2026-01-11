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
    State
} from 'vscode-languageclient/node';
import { PlatformInformation } from './platform';
import { ServerLauncher, TransportType, ServerStartResult } from './serverLauncher';

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
            this.client = await this.createLanguageClient(serverResult);

            // Register state change handler
            this.client.onDidChangeState((event) => {
                this.channel.appendLine(`Language client state: ${State[event.oldState]} -> ${State[event.newState]}`);
                this.onStateChangeEmitter.fire(event);
            });

            // Start the client
            await this.client.start();
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
    private async createLanguageClient(serverResult: ServerStartResult): Promise<LanguageClient> {
        const clientOptions = this.getClientOptions();
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
    private getClientOptions(): LanguageClientOptions {
        const config = vscode.workspace.getConfiguration('vbnet');
        const traceLevel = config.get<string>('trace.server', 'off');

        return {
            documentSelector: [
                { scheme: 'file', language: 'vb' },
                { scheme: 'untitled', language: 'vb' }
            ],
            synchronize: {
                // Notify the server about file changes to VB.NET project files
                fileEvents: [
                    vscode.workspace.createFileSystemWatcher('**/*.vb'),
                    vscode.workspace.createFileSystemWatcher('**/*.vbproj'),
                    vscode.workspace.createFileSystemWatcher('**/*.sln')
                ]
            },
            outputChannel: this.channel,
            traceOutputChannel: this.traceChannel,
            middleware: {
                // Add any middleware here for request/response interception
            },
            initializationOptions: {
                // Options to pass to the server during initialization
            }
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

        this.serverLauncher.stopServer();
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
}
