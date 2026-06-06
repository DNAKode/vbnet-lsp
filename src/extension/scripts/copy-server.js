const fs = require('fs');
const path = require('path');
const cp = require('child_process');

const extensionRoot = path.resolve(__dirname, '..');
const repoRoot = path.resolve(extensionRoot, '..', '..');
const args = process.argv.slice(2);
const serverProjectDir = path.join(repoRoot, 'src', 'VbNet.LanguageServer.Vb');
const sourceDir = path.join(serverProjectDir, 'bin', 'Release', 'net10.0');
const targetDir = path.join(extensionRoot, '.server');

if (args.includes('--build')) {
    const projectFile = path.join(serverProjectDir, 'VbNet.LanguageServer.Vb.vbproj');
    cp.execFileSync('dotnet', ['build', projectFile, '-c', 'Release'], {
        stdio: 'inherit'
    });
}

if (!fs.existsSync(sourceDir)) {
    throw new Error(`Server build output not found: ${sourceDir}`);
}

fs.rmSync(targetDir, { recursive: true, force: true });
fs.mkdirSync(targetDir, { recursive: true });
fs.cpSync(sourceDir, targetDir, {
    recursive: true,
    filter: (source) => path.basename(source) !== 'publish'
});

console.log(`Copied VB.NET language server from ${sourceDir} to ${targetDir}`);
