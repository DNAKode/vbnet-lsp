/*---------------------------------------------------------------------------------------------
 *  VB.NET Language Support
 *  Licensed under the MIT License. See LICENSE in the project root for license information.
 *--------------------------------------------------------------------------------------------*/

import * as vscode from 'vscode';

export class UriConverter {
    public static serialize(uri: vscode.Uri): string {
        return uri.toString(true);
    }

    public static deserialize(value: string): vscode.Uri {
        return vscode.Uri.parse(value, true);
    }
}
