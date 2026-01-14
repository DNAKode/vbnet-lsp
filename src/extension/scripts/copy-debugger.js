const fs = require('fs');
const path = require('path');

const extensionRoot = path.resolve(__dirname, '..');
const repoRoot = path.resolve(extensionRoot, '..', '..');
const sourceDir = path.join(repoRoot, '_external', 'netcoredbg', 'bin');
const resolveConfiguredPath = (value) => {
    const trimmed = (value || '').trim();
    if (!trimmed) {
        return '';
    }
    return path.isAbsolute(trimmed) ? trimmed : path.resolve(repoRoot, trimmed);
};

const configuredDebuggerPath = resolveConfiguredPath(process.env.NETCOREDBG_PATH);
const configuredLicensePath = resolveConfiguredPath(process.env.NETCOREDBG_LICENSE);
const defaultExeName = process.platform === 'win32' ? 'netcoredbg.exe' : 'netcoredbg';
const sourceExe = configuredDebuggerPath || path.join(sourceDir, defaultExeName);
const sourceLicense = configuredLicensePath || path.join(repoRoot, '_external', 'netcoredbg', 'LICENSE');
const targetDir = path.join(extensionRoot, '.debugger');
const targetExe = path.join(targetDir, path.basename(sourceExe));
const targetLicense = path.join(targetDir, 'LICENSE.netcoredbg');

if (!fs.existsSync(sourceExe)) {
    throw new Error(
        `netcoredbg not found at ${sourceExe}. Build it, set NETCOREDBG_PATH, or set up _external/netcoredbg first.`
    );
}

fs.rmSync(targetDir, { recursive: true, force: true });
fs.mkdirSync(targetDir, { recursive: true });
fs.copyFileSync(sourceExe, targetExe);
if (process.platform !== 'win32') {
    fs.chmodSync(targetExe, 0o755);
}

if (fs.existsSync(sourceLicense)) {
    fs.copyFileSync(sourceLicense, targetLicense);
} else {
    console.warn(`netcoredbg LICENSE not found at ${sourceLicense}`);
}

console.log(`Copied netcoredbg from ${sourceExe} to ${targetExe}`);
