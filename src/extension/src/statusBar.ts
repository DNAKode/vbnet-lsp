/*---------------------------------------------------------------------------------------------
 *  VB.NET Language Support
 *  Licensed under the MIT License. See LICENSE in the project root for license information.
 *--------------------------------------------------------------------------------------------*/

import * as vscode from 'vscode';
import { State } from 'vscode-languageclient/node';

/**
 * Status bar item for showing VB.NET language server status.
 */
export class VbNetStatusBar implements vscode.Disposable {
    private readonly statusBarItem: vscode.StatusBarItem;

    constructor() {
        this.statusBarItem = vscode.window.createStatusBarItem(
            vscode.StatusBarAlignment.Right,
            100
        );
        this.statusBarItem.name = 'VB.NET Language Server';
        this.statusBarItem.command = 'vbnet.showOutputChannel';
        this.setStatus('initializing');
    }

    /**
     * Sets the status bar to show the current server state.
     */
    public setStatus(status: 'initializing' | 'running' | 'stopped' | 'error'): void {
        switch (status) {
            case 'initializing':
                this.statusBarItem.text = '$(sync~spin) VB.NET';
                this.statusBarItem.tooltip = 'VB.NET Language Server: Starting...';
                this.statusBarItem.backgroundColor = undefined;
                break;

            case 'running':
                this.statusBarItem.text = '$(check) VB.NET';
                this.statusBarItem.tooltip = 'VB.NET Language Server: Running';
                this.statusBarItem.backgroundColor = undefined;
                break;

            case 'stopped':
                this.statusBarItem.text = '$(circle-slash) VB.NET';
                this.statusBarItem.tooltip = 'VB.NET Language Server: Stopped';
                this.statusBarItem.backgroundColor = undefined;
                break;

            case 'error':
                this.statusBarItem.text = '$(error) VB.NET';
                this.statusBarItem.tooltip = 'VB.NET Language Server: Error (click for details)';
                this.statusBarItem.backgroundColor = new vscode.ThemeColor('statusBarItem.errorBackground');
                break;
        }
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
