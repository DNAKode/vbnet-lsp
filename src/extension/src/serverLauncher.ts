/*---------------------------------------------------------------------------------------------
 *  VB.NET Language Support
 *  Licensed under the MIT License. See LICENSE in the project root for license information.
 *--------------------------------------------------------------------------------------------*/

import * as vscode from 'vscode';
import * as path from 'path';
import * as fs from 'fs';
import * as cp from 'child_process';
import * as net from 'net';
import { PlatformInformation } from './platform';
import { DotnetRuntimeResolver, HostExecutableInfo } from './dotnetRuntime';

/**
 * Transport type for language server communication.
 */
export type TransportType = 'auto' | 'namedPipe' | 'stdio';

/**
 * Result of starting the language server.
 */
export interface ServerStartResult {
    transport: 'namedPipe' | 'stdio';
    process: cp.ChildProcess;
    pipeName?: string;
}

/**
 * Named pipe information received from the server.
 */
interface NamedPipeInfo {
    pipeName: string;
}

/**
 * Launches and manages the VB.NET language server process.
 */
export class ServerLauncher {
    private serverProcess: cp.ChildProcess | undefined;
    private readonly runtimeResolver: DotnetRuntimeResolver;

    constructor(
        private readonly channel: vscode.OutputChannel,
        private readonly platformInfo: PlatformInformation,
        private readonly extensionPath: string
    ) {
        this.runtimeResolver = new DotnetRuntimeResolver(channel, platformInfo);
    }

    /**
     * Starts the language server with the specified transport type.
     */
    public async startServer(transportType: TransportType): Promise<ServerStartResult> {
        const serverPath = this.getServerPath();
        this.channel.appendLine(`Starting VB.NET Language Server at: ${serverPath}`);

        const hostInfo = await this.runtimeResolver.getHostExecutableInfo();

        // Determine actual transport to use
        const actualTransport = this.resolveTransport(transportType);
        this.channel.appendLine(`Using transport: ${actualTransport}`);

        if (actualTransport === 'namedPipe') {
            return await this.startWithNamedPipe(serverPath, hostInfo);
        } else {
            return await this.startWithStdio(serverPath, hostInfo);
        }
    }

    /**
     * Resolves 'auto' transport to the actual transport type.
     */
    private resolveTransport(transportType: TransportType): 'namedPipe' | 'stdio' {
        if (transportType === 'auto') {
            // Named pipes are preferred, but fall back to stdio if there are issues
            // For now, default to named pipes on all platforms
            return 'namedPipe';
        }
        return transportType;
    }

    /**
     * Gets the path to the language server executable.
     */
    private getServerPath(): string {
        // Check for environment variable override
        const envPath = process.env.VBNET_SERVER_PATH;
        if (envPath) {
            this.channel.appendLine(`Using server path from VBNET_SERVER_PATH: ${envPath}`);
            return envPath;
        }

        // Check for configuration setting
        const config = vscode.workspace.getConfiguration('vbnet');
        const configPath = config.get<string>('server.path');
        if (configPath && configPath.trim() !== '') {
            this.channel.appendLine(`Using server path from settings: ${configPath}`);
            return configPath;
        }

        // Use bundled server path (relative to extension)
        const serverDir = path.join(this.extensionPath, '.server');
        let serverPath = path.join(serverDir, 'VbNet.LanguageServer');

        // Add platform-specific extension
        if (this.platformInfo.isWindows()) {
            serverPath += '.exe';
        } else if (this.platformInfo.isMacOS()) {
            // On macOS, we use the .dll and run via dotnet
            serverPath += '.dll';
        }

        // If the executable doesn't exist, try the .dll
        if (!fs.existsSync(serverPath)) {
            serverPath = path.join(serverDir, 'VbNet.LanguageServer.dll');
        }

        if (!fs.existsSync(serverPath)) {
            throw new Error(
                `Cannot find VB.NET language server. Expected at: ${serverPath}\n` +
                `Please set 'vbnet.server.path' in settings or the VBNET_SERVER_PATH environment variable.`
            );
        }

        return serverPath;
    }

    /**
     * Starts the server with named pipe transport.
     */
    private async startWithNamedPipe(
        serverPath: string,
        hostInfo: HostExecutableInfo
    ): Promise<ServerStartResult> {
        const args = ['--pipe'];
        const childProcess = this.spawnServer(serverPath, args, hostInfo);

        // Wait for the server to output the pipe name
        const pipeName = await this.waitForPipeName(childProcess);

        this.channel.appendLine(`Server started with named pipe: ${pipeName}`);
        this.serverProcess = childProcess;

        return {
            transport: 'namedPipe',
            process: childProcess,
            pipeName
        };
    }

    /**
     * Starts the server with stdio transport.
     */
    private async startWithStdio(
        serverPath: string,
        hostInfo: HostExecutableInfo
    ): Promise<ServerStartResult> {
        const args = ['--stdio'];
        const childProcess = this.spawnServer(serverPath, args, hostInfo);

        this.channel.appendLine('Server started with stdio transport');
        this.serverProcess = childProcess;

        return {
            transport: 'stdio',
            process: childProcess
        };
    }

    /**
     * Spawns the language server process.
     */
    private spawnServer(
        serverPath: string,
        args: string[],
        hostInfo: HostExecutableInfo
    ): cp.ChildProcess {
        const cpOptions: cp.SpawnOptions = {
            env: hostInfo.env,
            cwd: vscode.workspace.workspaceFolders?.[0]?.uri.fsPath,
            stdio: ['pipe', 'pipe', 'pipe']
        };

        let childProcess: cp.ChildProcess;

        // If the server is a .dll, run via dotnet
        if (serverPath.endsWith('.dll')) {
            const argsWithPath = [serverPath, ...args];
            this.channel.appendLine(`Spawning: ${hostInfo.path} ${argsWithPath.join(' ')}`);
            childProcess = cp.spawn(hostInfo.path, argsWithPath, cpOptions);
        } else {
            this.channel.appendLine(`Spawning: ${serverPath} ${args.join(' ')}`);
            childProcess = cp.spawn(serverPath, args, cpOptions);
        }

        // Handle process errors
        childProcess.on('error', (error) => {
            this.channel.appendLine(`Server process error: ${error.message}`);
        });

        childProcess.on('exit', (code, signal) => {
            if (code !== null) {
                this.channel.appendLine(`Server process exited with code: ${code}`);
            } else if (signal !== null) {
                this.channel.appendLine(`Server process killed by signal: ${signal}`);
            }
        });

        // Forward stderr to output channel
        childProcess.stderr?.on('data', (data: Buffer) => {
            this.channel.appendLine(`[Server] ${data.toString().trim()}`);
        });

        return childProcess;
    }

    /**
     * Waits for the server to output the named pipe name.
     */
    private waitForPipeName(childProcess: cp.ChildProcess): Promise<string> {
        return new Promise((resolve, reject) => {
            const timeout = setTimeout(() => {
                reject(new Error('Timeout waiting for server to output pipe name'));
            }, 30000);

            // Pattern to match the pipe name JSON: {"pipeName":"..."}
            const pipeNameRegex = /(\{"pipeName":"[^"]+"\})/;

            let buffer = '';

            const onData = (data: Buffer) => {
                buffer += data.toString();
                const match = pipeNameRegex.exec(buffer);
                if (match) {
                    clearTimeout(timeout);
                    childProcess.stdout?.off('data', onData);
                    try {
                        // Parse the JSON to properly unescape the pipe name
                        const pipeInfo = JSON.parse(match[1]) as NamedPipeInfo;
                        resolve(pipeInfo.pipeName);
                    } catch (e) {
                        reject(new Error(`Failed to parse pipe name JSON: ${match[1]}`));
                    }
                }
            };

            childProcess.stdout?.on('data', onData);

            childProcess.on('error', (error) => {
                clearTimeout(timeout);
                reject(error);
            });

            childProcess.on('exit', (code) => {
                clearTimeout(timeout);
                if (code !== 0) {
                    reject(new Error(`Server exited with code ${code} before outputting pipe name`));
                }
            });
        });
    }

    /**
     * Creates a connection to the named pipe with retry logic.
     */
    public async connectToPipe(pipeName: string): Promise<net.Socket> {
        const maxRetries = 10;
        const retryDelayMs = 100;

        for (let attempt = 1; attempt <= maxRetries; attempt++) {
            try {
                const socket = await this.tryConnectToPipe(pipeName);
                this.channel.appendLine(`Connected to named pipe: ${pipeName}`);
                return socket;
            } catch (error) {
                if (attempt === maxRetries) {
                    throw new Error(`Failed to connect to pipe after ${maxRetries} attempts: ${error}`);
                }
                this.channel.appendLine(`Pipe connection attempt ${attempt} failed, retrying in ${retryDelayMs}ms...`);
                await new Promise(resolve => setTimeout(resolve, retryDelayMs));
            }
        }

        throw new Error('Failed to connect to pipe');
    }

    /**
     * Single attempt to connect to the named pipe.
     */
    private tryConnectToPipe(pipeName: string): Promise<net.Socket> {
        return new Promise((resolve, reject) => {
            const socket = net.createConnection(pipeName, () => {
                resolve(socket);
            });

            socket.on('error', (error) => {
                socket.destroy();
                reject(error);
            });
        });
    }

    /**
     * Stops the language server if running.
     */
    public stopServer(): void {
        if (this.serverProcess) {
            this.channel.appendLine('Stopping language server...');
            this.serverProcess.kill();
            this.serverProcess = undefined;
        }
    }

    /**
     * Gets whether the server is currently running.
     */
    public get isRunning(): boolean {
        return this.serverProcess !== undefined && !this.serverProcess.killed;
    }
}
