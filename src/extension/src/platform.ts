/*---------------------------------------------------------------------------------------------
 *  VB.NET Language Support
 *  Licensed under the MIT License. See LICENSE in the project root for license information.
 *--------------------------------------------------------------------------------------------*/

import * as os from 'os';
import * as process from 'process';

/**
 * Represents the current platform information.
 */
export class PlatformInformation {
    public readonly platform: string;
    public readonly architecture: string;

    private constructor(platform: string, architecture: string) {
        this.platform = platform;
        this.architecture = architecture;
    }

    /**
     * Gets the current platform information.
     */
    public static async getCurrent(): Promise<PlatformInformation> {
        const platform = os.platform();
        const architecture = PlatformInformation.getArchitecture();
        return new PlatformInformation(platform, architecture);
    }

    /**
     * Gets the current architecture, normalized to common names.
     */
    private static getArchitecture(): string {
        const arch = process.arch;
        switch (arch) {
            case 'x64':
                return 'x86_64';
            case 'arm64':
                return 'arm64';
            case 'ia32':
                return 'x86';
            default:
                return arch;
        }
    }

    /**
     * Returns true if running on Windows.
     */
    public isWindows(): boolean {
        return this.platform === 'win32';
    }

    /**
     * Returns true if running on macOS.
     */
    public isMacOS(): boolean {
        return this.platform === 'darwin';
    }

    /**
     * Returns true if running on Linux.
     */
    public isLinux(): boolean {
        return this.platform === 'linux';
    }

    /**
     * Gets the platform-specific executable extension.
     */
    public getExecutableExtension(): string {
        if (this.isWindows()) {
            return '.exe';
        }
        return '';
    }

    /**
     * Gets a string representation for logging.
     */
    public toString(): string {
        return `${this.platform} (${this.architecture})`;
    }
}
