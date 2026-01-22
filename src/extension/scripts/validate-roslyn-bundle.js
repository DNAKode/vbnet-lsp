const fs = require('fs');
const path = require('path');

const extensionRoot = path.resolve(__dirname, '..');
const roslynDir = path.join(extensionRoot, '.roslyn');
const roslynVbDir = path.join(extensionRoot, '.roslyn-vb');

function fail(message) {
    console.error(message);
    process.exit(1);
}

function requireDir(dir, label) {
    if (!fs.existsSync(dir)) {
        fail(`${label} directory not found: ${dir}`);
    }
}

function listFiles(dir) {
    return fs.readdirSync(dir).filter((entry) => {
        const fullPath = path.join(dir, entry);
        return fs.statSync(fullPath).isFile();
    });
}

requireDir(roslynDir, 'Roslyn LSP');
requireDir(roslynVbDir, 'Roslyn VB extension');

const roslynFiles = listFiles(roslynDir);
const vbFiles = listFiles(roslynVbDir);

const hasServerDll = roslynFiles.includes('Microsoft.CodeAnalysis.LanguageServer.dll');
const hasServerExe = roslynFiles.includes('Microsoft.CodeAnalysis.LanguageServer.exe');
if (!hasServerDll && !hasServerExe) {
    fail('Roslyn LSP bundle missing Microsoft.CodeAnalysis.LanguageServer.dll/.exe');
}

const vbDlls = vbFiles.filter((name) => name.startsWith('Microsoft.CodeAnalysis.VisualBasic') && name.endsWith('.dll'));
if (vbDlls.length === 0) {
    fail('Roslyn VB extension bundle missing Microsoft.CodeAnalysis.VisualBasic*.dll');
}

const baseHasVb = roslynFiles.some((name) => name.startsWith('Microsoft.CodeAnalysis.VisualBasic') && name.endsWith('.dll'));
if (baseHasVb) {
    fail('Roslyn base bundle contains VisualBasic assemblies; move them to .roslyn-vb to avoid duplicate analyzers.');
}

console.log('Roslyn bundle validation passed.');
