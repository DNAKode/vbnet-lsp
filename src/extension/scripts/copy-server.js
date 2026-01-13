const fs = require('fs');
const path = require('path');

const extensionRoot = path.resolve(__dirname, '..');
const repoRoot = path.resolve(extensionRoot, '..', '..');
const sourceDir = path.join(repoRoot, 'src', 'VbNet.LanguageServer', 'bin', 'Release', 'net10.0');
const targetDir = path.join(extensionRoot, '.server');

if (!fs.existsSync(sourceDir)) {
    throw new Error(`Server build output not found: ${sourceDir}`);
}

fs.rmSync(targetDir, { recursive: true, force: true });
fs.mkdirSync(targetDir, { recursive: true });
fs.cpSync(sourceDir, targetDir, { recursive: true });

console.log(`Copied language server from ${sourceDir} to ${targetDir}`);
