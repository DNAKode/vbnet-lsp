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

    async resolveDebugConfiguration(
        folder: vscode.WorkspaceFolder | undefined,
        config: vscode.DebugConfiguration
    ): Promise<vscode.DebugConfiguration | undefined> {
        const resolved: vscode.DebugConfiguration = { ...config };

        if (!resolved.type && !resolved.request && !resolved.name) {
            resolved.type = 'vbnet';
            resolved.name = 'VB.NET Launch';
            resolved.request = 'launch';
        }

        if (resolved.request === 'launch') {
            if (!resolved.program) {
                const inferredProgram = await this.tryInferProgramPath(folder, resolved);
                if (inferredProgram) {
                    resolved.program = inferredProgram;
                    this.outputChannel.appendLine(`Inferred debug program: ${resolved.program}`);
                } else {
                    const message =
                        'VB.NET debugging requires a "program" path to the compiled .dll. Update launch.json and try again.';
                    const action = await vscode.window.showErrorMessage(message, 'Open launch.json');
                    if (action === 'Open launch.json') {
                        await vscode.commands.executeCommand('workbench.action.debug.configure');
                    }
                    return undefined;
                }
            }

            if (typeof resolved.program === 'string' && !path.isAbsolute(resolved.program)) {
                const workspaceRoot = folder?.uri.fsPath ?? vscode.workspace.workspaceFolders?.[0]?.uri.fsPath;
                if (workspaceRoot) {
                    resolved.program = path.resolve(workspaceRoot, resolved.program);
                }
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

            if (typeof resolved.processId === 'string') {
                const parsed = Number.parseInt(resolved.processId, 10);
                if (!Number.isNaN(parsed)) {
                    resolved.processId = parsed;
                }
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

    private async tryInferProgramPath(
        folder: vscode.WorkspaceFolder | undefined,
        config: vscode.DebugConfiguration
    ): Promise<string | undefined> {
        const projectPath = this.getProjectPathFromConfig(config, folder) ?? (await this.findSingleProjectPath(folder));
        if (!projectPath) {
            return undefined;
        }

        const projectDir = path.dirname(projectPath);
        const projectInfo = await this.readProjectInfo(projectPath);
        const assemblyName = projectInfo.assemblyName ?? path.basename(projectPath, path.extname(projectPath));
        const candidate = await this.findBuiltAssembly(projectDir, assemblyName);
        if (candidate) {
            return candidate;
        }

        const defaultParts = [projectDir, 'bin', 'Debug'];
        if (projectInfo.targetFramework) {
            defaultParts.push(projectInfo.targetFramework);
        }
        defaultParts.push(`${assemblyName}.dll`);
        const fallback = path.join(...defaultParts);
        if (fs.existsSync(fallback)) {
            return fallback;
        }

        this.outputChannel.appendLine(
            `Unable to infer debug program: no built ${assemblyName}.dll found under ${projectDir}\\bin\\Debug.`
        );
        return undefined;
    }

    private getProjectPathFromConfig(
        config: vscode.DebugConfiguration,
        folder: vscode.WorkspaceFolder | undefined
    ): string | undefined {
        const rawCandidate = (config.projectPath ?? config.project ?? '').toString().trim();
        if (!rawCandidate) {
            return undefined;
        }

        let candidate = rawCandidate;
        const workspaceRoot = folder?.uri.fsPath ?? vscode.workspace.workspaceFolders?.[0]?.uri.fsPath;
        if (workspaceRoot) {
            candidate = candidate.replace('${workspaceFolder}', workspaceRoot);
        }

        return path.isAbsolute(candidate) ? candidate : path.resolve(candidate);
    }

    private async findSingleProjectPath(folder: vscode.WorkspaceFolder | undefined): Promise<string | undefined> {
        const root = folder ?? vscode.workspace.workspaceFolders?.[0];
        if (!root) {
            return undefined;
        }

        const pattern = new vscode.RelativePattern(root, '**/*.vbproj');
        const candidates = await vscode.workspace.findFiles(pattern, '**/{bin,obj,.git}/**', 2);
        if (candidates.length !== 1) {
            if (candidates.length > 1) {
                this.outputChannel.appendLine(
                    `Multiple VB projects found; unable to infer debug program automatically (${candidates.length} projects).`
                );
            }
            return undefined;
        }

        return candidates[0].fsPath;
    }

    private async readProjectInfo(projectPath: string): Promise<{ assemblyName?: string; targetFramework?: string }> {
        try {
            const contents = await fs.promises.readFile(projectPath, 'utf8');
            const assemblyMatch = contents.match(/<AssemblyName>([^<]+)<\/AssemblyName>/i);
            const tfmMatch = contents.match(/<TargetFramework>([^<]+)<\/TargetFramework>/i);
            const tfmsMatch = contents.match(/<TargetFrameworks>([^<]+)<\/TargetFrameworks>/i);
            const targetFramework = tfmMatch?.[1]?.trim() ?? tfmsMatch?.[1]?.split(';')[0]?.trim();
            return {
                assemblyName: assemblyMatch?.[1]?.trim(),
                targetFramework
            };
        } catch (error) {
            this.outputChannel.appendLine(`Failed to read project file ${projectPath}: ${error}`);
            return {};
        }
    }

    private async findBuiltAssembly(projectDir: string, assemblyName: string): Promise<string | undefined> {
        const pattern = new vscode.RelativePattern(projectDir, `bin/Debug/**/${assemblyName}.dll`);
        const candidates = await vscode.workspace.findFiles(pattern, '**/obj/**', 3);
        if (candidates.length > 0) {
            return candidates[0].fsPath;
        }
        return undefined;
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

        if (fs.existsSync(resolvedPath) && fs.statSync(resolvedPath).isDirectory()) {
            resolvedPath = path.join(resolvedPath, `netcoredbg${ext}`);
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
