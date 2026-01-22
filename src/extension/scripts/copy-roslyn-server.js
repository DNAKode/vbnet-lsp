const fs = require('fs');
const path = require('path');
const cp = require('child_process');
const https = require('https');
const http = require('http');

const extensionRoot = path.resolve(__dirname, '..');
const args = process.argv.slice(2);

function getArg(name) {
    const index = args.indexOf(name);
    if (index === -1 || index + 1 >= args.length) {
        return undefined;
    }
    return args[index + 1];
}

function detectRid() {
    const platform = process.platform;
    const arch = process.arch;
    if (platform === 'win32') {
        return arch === 'arm64' ? 'win-arm64' : 'win-x64';
    }
    if (platform === 'darwin') {
        return arch === 'arm64' ? 'osx-arm64' : 'osx-x64';
    }
    if (platform === 'linux') {
        return arch === 'arm64' ? 'linux-arm64' : 'linux-x64';
    }
    throw new Error(`Unsupported platform/arch for RID auto-detect: ${platform}/${arch}`);
}

function ensureDir(dir) {
    if (!fs.existsSync(dir)) {
        fs.mkdirSync(dir, { recursive: true });
    }
}

function downloadFile(url, dest, redirectCount = 0) {
    return new Promise((resolve, reject) => {
        const client = url.startsWith('https:') ? https : http;
        const request = client.get(url, { headers: { 'User-Agent': 'vbnet-language-support' } }, (response) => {
            if (response.statusCode >= 300 && response.statusCode < 400 && response.headers.location) {
                if (redirectCount > 5) {
                    reject(new Error(`Too many redirects for ${url}`));
                    return;
                }
                const redirectUrl = response.headers.location;
                response.resume();
                downloadFile(redirectUrl, dest, redirectCount + 1)
                    .then(resolve)
                    .catch(reject);
                return;
            }
            if (response.statusCode !== 200) {
                reject(new Error(`Download failed ${response.statusCode} for ${url}`));
                return;
            }
            fs.rmSync(dest, { force: true });
            const file = fs.createWriteStream(dest);
            response.pipe(file);
            file.on('finish', () => {
                file.close(resolve);
            });
            file.on('error', (err) => {
                file.close(() => reject(err));
            });
        });
        request.on('error', (err) => {
            reject(err);
        });
    });
}

function extractZip(zipPath, outDir) {
    ensureDir(outDir);
    try {
        cp.execFileSync('tar', ['-xf', zipPath, '-C', outDir], { stdio: 'inherit' });
        return;
    } catch (error) {
        // fall back to PowerShell on Windows
    }

    if (process.platform === 'win32') {
        const command = `Expand-Archive -Path "${zipPath}" -DestinationPath "${outDir}" -Force`;
        cp.execFileSync('powershell', ['-NoProfile', '-Command', command], { stdio: 'inherit' });
        return;
    }

    throw new Error(`Failed to extract ${zipPath}. Ensure 'tar' is available on PATH.`);
}

function copyDir(source, destination) {
    fs.rmSync(destination, { recursive: true, force: true });
    fs.mkdirSync(destination, { recursive: true });
    fs.cpSync(source, destination, { recursive: true });
}

function findFirstDir(root, predicate) {
    const entries = fs.readdirSync(root, { withFileTypes: true });
    for (const entry of entries) {
        const fullPath = path.join(root, entry.name);
        if (entry.isDirectory()) {
            if (predicate(fullPath)) {
                return fullPath;
            }
            const nested = findFirstDir(fullPath, predicate);
            if (nested) {
                return nested;
            }
        }
    }
    return undefined;
}

function collectMatchingFiles(root, matcher, results) {
    const entries = fs.readdirSync(root, { withFileTypes: true });
    for (const entry of entries) {
        const fullPath = path.join(root, entry.name);
        if (entry.isDirectory()) {
            collectMatchingFiles(fullPath, matcher, results);
        } else if (matcher(entry.name)) {
            results.push(fullPath);
        }
    }
}

async function main() {
    const rid = getArg('--rid') || process.env.VBNET_ROSLYN_RID || detectRid();
    const lspVersion = getArg('--lsp-version') || process.env.VBNET_ROSLYN_LSP_VERSION || '5.0.0-1.25277.114';
    const vbVersion = getArg('--vb-version') || process.env.VBNET_ROSLYN_VB_VERSION || '5.0.0';

    const downloadsDir = path.join(extensionRoot, '.roslyn-downloads');
    const roslynDir = path.join(extensionRoot, '.roslyn');
    const roslynVbDir = path.join(extensionRoot, '.roslyn-vb');

    ensureDir(downloadsDir);

    const lspPackageId = `Microsoft.CodeAnalysis.LanguageServer.${rid}`;
    const lspPackageName = `${lspPackageId}.${lspVersion}.nupkg`;
    const lspPackagePath = path.join(downloadsDir, lspPackageName);

    const vbPackages = [
        'Microsoft.CodeAnalysis.VisualBasic',
        'Microsoft.CodeAnalysis.VisualBasic.Workspaces',
        'Microsoft.CodeAnalysis.VisualBasic.Features'
    ];

    console.log(`Using RID=${rid}`);
    console.log(`Roslyn LSP version=${lspVersion}`);
    console.log(`Roslyn VB packages version=${vbVersion}`);

    if (!fs.existsSync(lspPackagePath)) {
        const lspUrl = `https://www.nuget.org/api/v2/package/${lspPackageId}/${lspVersion}`;
        console.log(`Downloading ${lspPackageId} from ${lspUrl}`);
        await downloadFile(lspUrl, lspPackagePath);
    }

    const lspExtractDir = path.join(downloadsDir, `${lspPackageId}.${lspVersion}`);
    if (fs.existsSync(lspExtractDir)) {
        fs.rmSync(lspExtractDir, { recursive: true, force: true });
    }
    extractZip(lspPackagePath, lspExtractDir);

    const lspRidDir = findFirstDir(lspExtractDir, (dir) => {
        const normalized = dir.split(path.sep).join('/');
        return normalized.endsWith(`/content/LanguageServer/${rid}`);
    });

    let lspContentDir = lspRidDir;
    if (!lspContentDir) {
        const baseContentDir = findFirstDir(lspExtractDir, (dir) => {
            const normalized = dir.split(path.sep).join('/');
            return normalized.endsWith(`/content/LanguageServer`);
        });

        if (baseContentDir) {
            const ridCandidate = path.join(baseContentDir, rid);
            if (fs.existsSync(ridCandidate) && fs.statSync(ridCandidate).isDirectory()) {
                lspContentDir = ridCandidate;
            } else {
                lspContentDir = baseContentDir;
            }
        }
    }

    if (!lspContentDir) {
        throw new Error(`Failed to locate Roslyn LSP content directory in ${lspExtractDir}`);
    }

    copyDir(lspContentDir, roslynDir);
    console.log(`Copied Roslyn LSP to ${roslynDir}`);

    fs.rmSync(roslynVbDir, { recursive: true, force: true });
    fs.mkdirSync(roslynVbDir, { recursive: true });

    for (const vbPackageId of vbPackages) {
        const vbPackageName = `${vbPackageId}.${vbVersion}.nupkg`;
        const vbPackagePath = path.join(downloadsDir, vbPackageName);
        if (!fs.existsSync(vbPackagePath)) {
            const vbUrl = `https://www.nuget.org/api/v2/package/${vbPackageId}/${vbVersion}`;
            console.log(`Downloading ${vbPackageId} from ${vbUrl}`);
            await downloadFile(vbUrl, vbPackagePath);
        }

        const vbExtractDir = path.join(downloadsDir, `${vbPackageId}.${vbVersion}`);
        if (fs.existsSync(vbExtractDir)) {
            fs.rmSync(vbExtractDir, { recursive: true, force: true });
        }
        extractZip(vbPackagePath, vbExtractDir);

        const files = [];
        collectMatchingFiles(vbExtractDir, (name) => name.startsWith('Microsoft.CodeAnalysis.VisualBasic') && (name.endsWith('.dll') || name.endsWith('.xml')), files);

        for (const filePath of files) {
            const fileName = path.basename(filePath);
            const dest = path.join(roslynVbDir, fileName);
            fs.copyFileSync(filePath, dest);
        }
    }

    console.log(`Copied Roslyn VB extension assemblies to ${roslynVbDir}`);
}

main().catch((error) => {
    console.error(error.message || error);
    process.exit(1);
});
