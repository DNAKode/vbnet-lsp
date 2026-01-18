const fs = require('fs');
const path = require('path');
const https = require('https');
const { execFileSync } = require('child_process');

const extensionRoot = path.resolve(__dirname, '..');
const repoRoot = path.resolve(extensionRoot, '..', '..');
const assetsPath = path.join(__dirname, 'netcoredbg-assets.json');
const sourceDir = path.join(repoRoot, '_external', 'netcoredbg', 'bin');

const resolveConfiguredPath = (value) => {
    const trimmed = (value || '').trim();
    if (!trimmed) {
        return '';
    }
    return path.isAbsolute(trimmed) ? trimmed : path.resolve(repoRoot, trimmed);
};

const getArgValue = (name) => {
    const prefix = `${name}=`;
    const args = process.argv.slice(2);
    for (let i = 0; i < args.length; i += 1) {
        const arg = args[i];
        if (arg === name) {
            return args[i + 1] ?? '';
        }
        if (arg.startsWith(prefix)) {
            return arg.slice(prefix.length);
        }
    }
    return '';
};

const inferTargetPlatform = () => {
    if (process.platform === 'win32') {
        return 'win32-x64';
    }
    if (process.platform === 'darwin') {
        return process.arch === 'arm64' ? 'darwin-arm64' : 'darwin-x64';
    }
    if (process.platform === 'linux') {
        return process.arch === 'arm64' ? 'linux-arm64' : 'linux-x64';
    }
    return 'win32-x64';
};

const assets = JSON.parse(fs.readFileSync(assetsPath, 'utf8'));
const targetOverride =
    getArgValue('--target') ||
    getArgValue('--platform') ||
    process.env.VBNET_DEBUGGER_TARGET ||
    '';
const targetPlatform = targetOverride || inferTargetPlatform();
const targetAsset = assets.targets?.[targetPlatform];
const configuredDebuggerPath = resolveConfiguredPath(process.env.NETCOREDBG_PATH);
const configuredLicensePath = resolveConfiguredPath(process.env.NETCOREDBG_LICENSE);
const defaultExeName = targetPlatform.startsWith('win32') ? 'netcoredbg.exe' : 'netcoredbg';
const targetDir = path.join(extensionRoot, '.debugger');
const targetExe = path.join(targetDir, defaultExeName);
const targetLicense = path.join(targetDir, 'LICENSE.netcoredbg');
const fallbackLicense = assets.licensePath
    ? path.resolve(repoRoot, assets.licensePath)
    : path.join(repoRoot, 'third_party', 'netcoredbg', 'LICENSE');

const findNetcoredbg = (root, exeName) => {
    if (!fs.existsSync(root)) {
        return '';
    }
    const entries = fs.readdirSync(root, { withFileTypes: true });
    for (const entry of entries) {
        const entryPath = path.join(root, entry.name);
        if (entry.isDirectory()) {
            const nested = findNetcoredbg(entryPath, exeName);
            if (nested) {
                return nested;
            }
        } else if (entry.isFile() && entry.name === exeName) {
            return entryPath;
        }
    }
    return '';
};

const downloadFile = (url, destination) =>
    new Promise((resolve, reject) => {
        const request = (currentUrl) => {
            const handler = (response) => {
                if (response.statusCode >= 300 && response.statusCode < 400 && response.headers.location) {
                    request(response.headers.location);
                    return;
                }
                if (response.statusCode !== 200) {
                    reject(new Error(`Download failed (${response.statusCode}) for ${currentUrl}`));
                    return;
                }
                fs.mkdirSync(path.dirname(destination), { recursive: true });
                const file = fs.createWriteStream(destination);
                response.pipe(file);
                file.on('finish', () => file.close(resolve));
                file.on('error', reject);
            };
            https.get(currentUrl, handler).on('error', reject);
        };
        request(url);
    });

const extractAsset = (assetPath, extractDir) => {
    fs.rmSync(extractDir, { recursive: true, force: true });
    fs.mkdirSync(extractDir, { recursive: true });

    if (assetPath.endsWith('.zip')) {
        if (process.platform === 'win32') {
            execFileSync('powershell', [
                '-NoProfile',
                '-Command',
                `Expand-Archive -LiteralPath "${assetPath}" -DestinationPath "${extractDir}" -Force`
            ], { stdio: 'inherit' });
        } else {
            execFileSync('unzip', ['-o', assetPath, '-d', extractDir], { stdio: 'inherit' });
        }
        return;
    }
    if (assetPath.endsWith('.tar.gz') || assetPath.endsWith('.tgz')) {
        execFileSync('tar', ['-xf', assetPath, '-C', extractDir], { stdio: 'inherit' });
        return;
    }
};

const resolveSourceExe = async () => {
    if (configuredDebuggerPath && fs.existsSync(configuredDebuggerPath)) {
        return configuredDebuggerPath;
    }

    const localCandidate = path.join(sourceDir, defaultExeName);
    if (fs.existsSync(localCandidate)) {
        return localCandidate;
    }

    if (!targetAsset?.url) {
        return '';
    }

    const cacheRoot = path.join(extensionRoot, '.debugger-cache', targetPlatform);
    const assetUrl = targetAsset.url;
    const assetName = path.basename(new URL(assetUrl).pathname) || 'netcoredbg-asset';
    const downloadDir = path.join(cacheRoot, 'download');
    const extractDir = path.join(cacheRoot, 'extract');
    const assetPath = path.join(downloadDir, assetName);

    if (!fs.existsSync(assetPath)) {
        console.log(`Downloading netcoredbg asset ${assetUrl}`);
        await downloadFile(assetUrl, assetPath);
    }

    if (assetPath.endsWith('.zip') || assetPath.endsWith('.tar.gz') || assetPath.endsWith('.tgz')) {
        extractAsset(assetPath, extractDir);
        const extracted = findNetcoredbg(extractDir, defaultExeName);
        if (extracted) {
            return extracted;
        }
    }

    const fallback = findNetcoredbg(downloadDir, defaultExeName);
    return fallback;
};

const main = async () => {
    const sourceExe = await resolveSourceExe();
    if (!sourceExe || !fs.existsSync(sourceExe)) {
        throw new Error(
            `netcoredbg not found for ${targetPlatform}. Set NETCOREDBG_PATH or ensure the curated asset is reachable.`
        );
    }

    const sourceLicense = configuredLicensePath || fallbackLicense;
    fs.rmSync(targetDir, { recursive: true, force: true });
    fs.mkdirSync(targetDir, { recursive: true });
    fs.copyFileSync(sourceExe, targetExe);
    if (!targetPlatform.startsWith('win32')) {
        fs.chmodSync(targetExe, 0o755);
    }

    if (sourceLicense && fs.existsSync(sourceLicense)) {
        fs.copyFileSync(sourceLicense, targetLicense);
    } else {
        console.warn(`netcoredbg LICENSE not found at ${sourceLicense}`);
    }

    console.log(`Copied netcoredbg from ${sourceExe} to ${targetExe}`);
};

main().catch((error) => {
    console.error(error);
    process.exit(1);
});
