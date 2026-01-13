const fs = require('fs');
const path = require('path');

const extensionRoot = path.resolve(__dirname, '..');
const repoRoot = path.resolve(extensionRoot, '..', '..');
const sourceDir = path.join(repoRoot, '_external', 'netcoredbg', 'bin');
const sourceExe = path.join(sourceDir, 'netcoredbg.exe');
const sourceLicense = path.join(repoRoot, '_external', 'netcoredbg', 'LICENSE');
const targetDir = path.join(extensionRoot, '.debugger');
const targetExe = path.join(targetDir, 'netcoredbg.exe');
const targetLicense = path.join(targetDir, 'LICENSE.netcoredbg');

if (!fs.existsSync(sourceExe)) {
    throw new Error(`netcoredbg not found at ${sourceExe}. Build it or set up _external/netcoredbg first.`);
}

fs.rmSync(targetDir, { recursive: true, force: true });
fs.mkdirSync(targetDir, { recursive: true });
fs.copyFileSync(sourceExe, targetExe);

if (fs.existsSync(sourceLicense)) {
    fs.copyFileSync(sourceLicense, targetLicense);
} else {
    console.warn(`netcoredbg LICENSE not found at ${sourceLicense}`);
}

console.log(`Copied netcoredbg from ${sourceExe} to ${targetExe}`);
