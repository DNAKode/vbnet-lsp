/*---------------------------------------------------------------------------------------------
 *  VB.NET Language Support
 *  Licensed under the MIT License. See LICENSE in the project root for license information.
 *--------------------------------------------------------------------------------------------*/

import * as vscode from 'vscode';
import * as path from 'path';
import { PlatformInformation } from './platform';

/**
 * The .NET runtime version required by the language server.
 */
const DotNetMajorVersion = '10';
const DotNetMinorVersion = '0';
export const DotNetRuntimeVersion = `${DotNetMajorVersion}.${DotNetMinorVersion}`;

/**
 * Extension ID for the .NET Runtime extension.
 */
export const DotNetRuntimeExtensionId = 'ms-dotnettools.vscode-dotnet-runtime';

/**
 * Extension ID for this extension (used when requesting runtime).
 */
export const VbNetExtensionId = 'dnakode.vbnet-language-support';

/**
 * Result of acquiring the .NET runtime.
 */
export interface DotnetAcquireResult {
    dotnetPath: string;
}

/**
 * Context for acquiring the .NET runtime.
 */
interface DotnetAcquireContext {
    version: string;
    requestingExtensionId: string;
    mode?: 'runtime' | 'aspnetcore';
}

/**
 * Context for finding an existing .NET installation.
 */
interface DotnetFindPathContext {
    acquireContext: DotnetAcquireContext;
    versionSpecRequirement: 'greater_than_or_equal' | 'equal';
    rejectPreviews?: boolean;
}

/**
 * Host executable information for running the language server.
 */
export interface HostExecutableInfo {
    path: string;
    env: NodeJS.ProcessEnv;
}

/**
 * Resolves the .NET runtime for running the language server.
 * Uses the ms-dotnettools.vscode-dotnet-runtime extension.
 */
export class DotnetRuntimeResolver {
    private cachedHostInfo: HostExecutableInfo | undefined;

    constructor(
        private readonly channel: vscode.OutputChannel,
        private readonly platformInfo: PlatformInformation
    ) {}

    /**
     * Gets the host executable info for running the language server.
     * This resolves the .NET runtime path and sets up environment variables.
     */
    public async getHostExecutableInfo(): Promise<HostExecutableInfo> {
        if (this.cachedHostInfo) {
            return this.cachedHostInfo;
        }

        this.channel.appendLine(`Locating .NET runtime version ${DotNetRuntimeVersion}...`);

        // First try to find an existing .NET installation
        let dotnetPath = await this.findExistingDotnet();

        // If not found, acquire via the runtime extension
        if (!dotnetPath) {
            this.channel.appendLine(
                `Did not find .NET ${DotNetRuntimeVersion} on path, acquiring via ${DotNetRuntimeExtensionId}...`
            );
            dotnetPath = await this.acquireDotnetRuntime();
        }

        if (!dotnetPath) {
            throw new Error(
                `Failed to acquire .NET runtime. Please ensure the ${DotNetRuntimeExtensionId} extension is installed.`
            );
        }

        this.channel.appendLine(`Using .NET runtime at: ${dotnetPath}`);

        const hostInfo: HostExecutableInfo = {
            path: dotnetPath,
            env: this.getEnvironmentVariables(dotnetPath)
        };

        this.cachedHostInfo = hostInfo;
        return hostInfo;
    }

    /**
     * Tries to find an existing .NET installation using the runtime extension.
     */
    private async findExistingDotnet(): Promise<string | undefined> {
        try {
            const findPathRequest: DotnetFindPathContext = {
                acquireContext: {
                    version: DotNetRuntimeVersion,
                    requestingExtensionId: VbNetExtensionId,
                    mode: 'runtime'
                },
                versionSpecRequirement: 'greater_than_or_equal',
                rejectPreviews: true
            };

            const result = await vscode.commands.executeCommand<DotnetAcquireResult | undefined>(
                'dotnet.findPath',
                findPathRequest
            );

            return result?.dotnetPath;
        } catch (error) {
            this.channel.appendLine(`Error finding .NET path: ${error}`);
            return undefined;
        }
    }

    /**
     * Acquires the .NET runtime via the runtime extension.
     */
    private async acquireDotnetRuntime(): Promise<string | undefined> {
        try {
            const acquireContext: DotnetAcquireContext = {
                version: DotNetRuntimeVersion,
                requestingExtensionId: VbNetExtensionId,
                mode: 'runtime'
            };

            // Check if already acquired
            let result = await vscode.commands.executeCommand<DotnetAcquireResult | undefined>(
                'dotnet.acquireStatus',
                acquireContext
            );

            if (!result) {
                // Show the acquisition log to keep user informed
                await vscode.commands.executeCommand('dotnet.showAcquisitionLog');

                // Acquire the runtime
                result = await vscode.commands.executeCommand<DotnetAcquireResult>(
                    'dotnet.acquire',
                    acquireContext
                );
            }

            return result?.dotnetPath;
        } catch (error) {
            this.channel.appendLine(`Error acquiring .NET runtime: ${error}`);
            return undefined;
        }
    }

    /**
     * Gets the environment variables needed to run the language server.
     */
    private getEnvironmentVariables(dotnetPath: string): NodeJS.ProcessEnv {
        const env: NodeJS.ProcessEnv = { ...process.env };

        // Set DOTNET_ROOT to ensure .NET processes use the correct runtime
        env.DOTNET_ROOT = path.dirname(dotnetPath);

        // Prevent looking for other .NET installations
        env.DOTNET_MULTILEVEL_LOOKUP = '0';

        // Save user's original DOTNET_ROOT if set
        env.DOTNET_ROOT_USER = process.env.DOTNET_ROOT ?? '';

        return env;
    }

    /**
     * Clears the cached host info, forcing re-resolution on next call.
     */
    public clearCache(): void {
        this.cachedHostInfo = undefined;
    }
}
