/*---------------------------------------------------------------------------------------------
 *  VB.NET Language Support
 *  Licensed under the MIT License. See LICENSE in the project root for license information.
 *--------------------------------------------------------------------------------------------*/

import * as vscode from 'vscode';
import { State } from 'vscode-languageclient/node';
import {
    formatWorkspaceContextDetail,
    formatWorkspaceContextLabel,
    UnknownWorkspaceContext,
    WorkspaceContext
} from './workspaceContext';

/**
 * Status bar item for showing VB.NET language server status.
 */
export class VbNetStatusBar implements vscode.Disposable {
    private readonly statusBarItem: vscode.StatusBarItem;
    private serverStatus: 'initializing' | 'running' | 'stopped' | 'error' = 'initializing';
    private workspaceContext: WorkspaceContext = UnknownWorkspaceContext;

    constructor() {
        this.statusBarItem = vscode.window.createStatusBarItem(
            vscode.StatusBarAlignment.Right,
            100
        );
        this.statusBarItem.name = 'VB.NET Language Server';
        this.statusBarItem.command = 'vbnet.selectWorkspaceContext';
        this.setStatus('initializing');
    }

    /**
     * Sets the status bar to show the current server state.
     */
    public setStatus(status: 'initializing' | 'running' | 'stopped' | 'error'): void {
        this.serverStatus = status;
        this.render();
    }

    /**
     * Sets the workspace context shown alongside server state.
     */
    public setWorkspaceContext(context: WorkspaceContext): void {
        this.workspaceContext = context;
        this.render();
    }

    /**
     * Updates status based on language client state.
     */
    public updateFromClientState(state: State): void {
        switch (state) {
            case State.Starting:
                this.setStatus('initializing');
                break;
            case State.Running:
                this.setStatus('running');
                break;
            case State.Stopped:
                this.setStatus('stopped');
                break;
            default:
                this.setStatus('stopped');
        }
    }

    private render(): void {
        const contextLabel = formatWorkspaceContextLabel(this.workspaceContext);
        const contextDetail = formatWorkspaceContextDetail(this.workspaceContext);
        let serverDetail = '';

        switch (this.serverStatus) {
            case 'initializing':
                this.statusBarItem.text = '$(sync~spin) VB.NET';
                serverDetail = 'Starting';
                this.statusBarItem.backgroundColor = undefined;
                break;

            case 'running':
                this.statusBarItem.text = `$(check) VB.NET: ${contextLabel}`;
                serverDetail = 'Running';
                this.statusBarItem.backgroundColor = undefined;
                break;

            case 'stopped':
                this.statusBarItem.text = '$(circle-slash) VB.NET: Stopped';
                serverDetail = 'Stopped';
                this.statusBarItem.backgroundColor = undefined;
                break;

            case 'error':
                this.statusBarItem.text = '$(error) VB.NET: Error';
                serverDetail = 'Error';
                this.statusBarItem.backgroundColor = new vscode.ThemeColor('statusBarItem.errorBackground');
                break;
        }

        if (this.workspaceContext.kind === 'selectContext' && this.serverStatus === 'running') {
            this.statusBarItem.text = '$(warning) VB.NET: Select Context';
        }

        this.statusBarItem.tooltip = [
            `VB.NET Language Server: ${serverDetail}`,
            contextDetail,
            'Click to select VB.NET workspace context.'
        ].join('\n');
    }

    /**
     * Shows the status bar item.
     */
    public show(): void {
        this.statusBarItem.show();
    }

    /**
     * Hides the status bar item.
     */
    public hide(): void {
        this.statusBarItem.hide();
    }

    /**
     * Disposes of the status bar item.
     */
    public dispose(): void {
        this.statusBarItem.dispose();
    }
}
