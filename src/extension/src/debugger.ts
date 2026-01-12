/*---------------------------------------------------------------------------------------------
 *  VB.NET Language Support
 *  Licensed under the MIT License. See LICENSE in the project root for license information.
 *--------------------------------------------------------------------------------------------*/

import * as fs from 'fs';
import * as path from 'path';
import * as vscode from 'vscode';
import { PlatformInformation } from './platform';

export function activateDebugging(
    context: vscode.ExtensionContext,
    outputChannel: vscode.OutputChannel,
    platformInfo: PlatformInformation
): void {
    const factory = new NetCoreDbgAdapterFactory(context.extensionPath, platformInfo, outputChannel);
    context.subscriptions.push(vscode.debug.registerDebugAdapterDescriptorFactory('vbnet', factory));
    context.subscriptions.push(
        vscode.debug.registerDebugConfigurationProvider(
            'vbnet',
            new VbNetDebugConfigurationProvider(outputChannel)
        )
    );
}

class VbNetDebugConfigurationProvider implements vscode.DebugConfigurationProvider {
    constructor(private readonly outputChannel: vscode.OutputChannel) {}

    resolveDebugConfiguration(
        folder: vscode.WorkspaceFolder | undefined,
        config: vscode.DebugConfiguration
    ): vscode.DebugConfiguration | undefined {
        const resolved: vscode.DebugConfiguration = { ...config };

        if (!resolved.type && !resolved.request && !resolved.name) {
            resolved.type = 'vbnet';
            resolved.name = 'VB.NET Launch';
            resolved.request = 'launch';
        }

        if (resolved.request === 'launch') {
            if (!resolved.program) {
                vscode.window.showErrorMessage(
                    'VB.NET debugging requires a "program" path to the compiled .dll. Update launch.json and try again.'
                );
                return undefined;
            }

            if (!resolved.cwd) {
                resolved.cwd = folder?.uri.fsPath ?? path.dirname(resolved.program);
            }
        } else if (resolved.request === 'attach') {
            if (!resolved.processId) {
                vscode.window.showErrorMessage(
                    'VB.NET attach requires a "processId". Update launch.json and try again.'
                );
                return undefined;
            }
        }

        if (resolved.console === undefined) {
            resolved.console = 'internalConsole';
        }

        if (resolved.stopAtEntry === undefined) {
            resolved.stopAtEntry = false;
        }

        this.outputChannel.appendLine(
            `Resolved debug configuration (${resolved.request}): ${resolved.name ?? 'Unnamed'}`
        );

        return resolved;
    }
}

class NetCoreDbgAdapterFactory implements vscode.DebugAdapterDescriptorFactory {
    constructor(
        private readonly extensionPath: string,
        private readonly platformInfo: PlatformInformation,
        private readonly outputChannel: vscode.OutputChannel
    ) {}

    async createDebugAdapterDescriptor(
        _session: vscode.DebugSession,
        executable: vscode.DebugAdapterExecutable | undefined
    ): Promise<vscode.DebugAdapterDescriptor> {
        if (executable) {
            return executable;
        }

        const debuggerPath = this.resolveDebuggerPath();
        if (!debuggerPath) {
            throw new Error(
                'netcoredbg executable not found. Set vbnet.debugger.path or install netcoredbg on PATH.'
            );
        }

        const config = vscode.workspace.getConfiguration('vbnet');
        const extraArgs = config.get<string[]>('debugger.args', []);
        const args = ['--interpreter=vscode', ...extraArgs];

        this.outputChannel.appendLine(`Starting netcoredbg: ${debuggerPath} ${args.join(' ')}`);

        return new vscode.DebugAdapterExecutable(debuggerPath, args);
    }

    private resolveDebuggerPath(): string | undefined {
        const config = vscode.workspace.getConfiguration('vbnet');
        const configuredPath = (config.get<string>('debugger.path', '') || '').trim();
        if (configuredPath) {
            const normalized = this.normalizeConfiguredPath(configuredPath);
            if (normalized && fs.existsSync(normalized)) {
                return normalized;
            }
            this.outputChannel.appendLine(`Configured netcoredbg path not found: ${normalized ?? configuredPath}`);
        }

        const exeName = `netcoredbg${this.platformInfo.getExecutableExtension()}`;
        const candidates = [
            path.join(this.extensionPath, '.debugger', exeName),
            path.join(this.extensionPath, '..', '..', '_external', 'netcoredbg', 'bin', exeName)
        ];

        for (const candidate of candidates) {
            if (fs.existsSync(candidate)) {
                return candidate;
            }
        }

        return findOnPath(exeName, this.platformInfo);
    }

    private normalizeConfiguredPath(configuredPath: string): string | undefined {
        const trimmed = configuredPath.trim();
        if (!trimmed) {
            return undefined;
        }

        const ext = this.platformInfo.getExecutableExtension();
        let resolvedPath = trimmed;
        if (!path.isAbsolute(trimmed)) {
            const workspaceFolder = vscode.workspace.workspaceFolders?.[0]?.uri.fsPath;
            resolvedPath = workspaceFolder ? path.resolve(workspaceFolder, trimmed) : path.resolve(this.extensionPath, trimmed);
        }

        if (ext && path.extname(resolvedPath).length === 0) {
            resolvedPath += ext;
        }

        return resolvedPath;
    }
}

function findOnPath(executableName: string, platformInfo: PlatformInformation): string | undefined {
    const pathValue = process.env.PATH ?? '';
    const pathEntries = pathValue.split(path.delimiter).filter((entry) => entry.length > 0);
    const extensions = platformInfo.isWindows()
        ? (process.env.PATHEXT ?? '.EXE;.CMD;.BAT').split(';')
        : [''];

    for (const entry of pathEntries) {
        if (platformInfo.isWindows() && path.extname(executableName).length === 0) {
            for (const ext of extensions) {
                const candidate = path.join(entry, `${executableName}${ext.toLowerCase()}`);
                if (fs.existsSync(candidate)) {
                    return candidate;
                }
            }
        } else {
            const candidate = path.join(entry, executableName);
            if (fs.existsSync(candidate)) {
                return candidate;
            }
        }
    }

    return undefined;
}
