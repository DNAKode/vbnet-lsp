const fs = require('fs');
const path = require('path');
const cp = require('child_process');

const extensionRoot = path.resolve(__dirname, '..');
const repoRoot = path.resolve(extensionRoot, '..', '..');
const args = process.argv.slice(2);
const implArgIndex = args.findIndex((arg) => arg === '--impl');
const implArg = implArgIndex >= 0 ? args[implArgIndex + 1] : undefined;
const serverImpl = (process.env.VBNET_SERVER_IMPL || implArg || 'vb').toLowerCase();
const serverProjectDir =
    serverImpl === 'cs'
        ? path.join(repoRoot, 'src', 'VbNet.LanguageServer')
        : path.join(repoRoot, 'src', 'VbNet.LanguageServer.Vb');
const sourceDir = path.join(serverProjectDir, 'bin', 'Release', 'net10.0');
const targetDir = path.join(extensionRoot, '.server');

if (args.includes('--build')) {
    const projectFile = serverImpl === 'cs'
        ? path.join(serverProjectDir, 'VbNet.LanguageServer.csproj')
        : path.join(serverProjectDir, 'VbNet.LanguageServer.Vb.vbproj');
    cp.execFileSync('dotnet', ['build', projectFile, '-c', 'Release'], {
        stdio: 'inherit'
    });
}

if (!fs.existsSync(sourceDir)) {
    throw new Error(`Server build output not found (${serverImpl}): ${sourceDir}`);
}

fs.rmSync(targetDir, { recursive: true, force: true });
fs.mkdirSync(targetDir, { recursive: true });
fs.cpSync(sourceDir, targetDir, { recursive: true });

console.log(`Copied ${serverImpl} language server from ${sourceDir} to ${targetDir}`);
