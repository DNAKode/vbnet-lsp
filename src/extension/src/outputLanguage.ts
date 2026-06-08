/*---------------------------------------------------------------------------------------------
 *  VB.NET Language Support
 *  Licensed under the MIT License. See LICENSE in the project root for license information.
 *--------------------------------------------------------------------------------------------*/

import * as vscode from 'vscode';

export type OutputLanguage = 'auto' | 'en-US';

/**
 * Applies the configured diagnostic/output language to child process environments.
 */
export function getProcessEnvironmentWithOutputLanguage(baseEnv: NodeJS.ProcessEnv): NodeJS.ProcessEnv {
    const env: NodeJS.ProcessEnv = { ...baseEnv };
    const language = getConfiguredOutputLanguage();

    if (language === 'en-US') {
        env.DOTNET_CLI_UI_LANGUAGE = 'en-US';
        env.VSLANG = '1033';
        env.PreferredUILang = 'en-US';
        env.VBNET_UI_CULTURE = 'en-US';
    }

    return env;
}

export function getConfiguredOutputLanguage(): OutputLanguage {
    const config = vscode.workspace.getConfiguration('vbnet');
    const value = (config.get<string>('output.language', 'auto') ?? 'auto').trim();

    if (value === 'en-US') {
        return 'en-US';
    }

    return 'auto';
}
