import * as path from "path";
import * as fs from "fs";
import * as cp from "child_process";
import {
    downloadAndUnzipVSCode,
    resolveCliArgsFromVSCodeExecutablePath,
    runTests,
} from "@vscode/test-electron";

async function main() {
    const repoRoot = path.resolve(__dirname, "..", "..", "..", "..", "..");
    const resolveRepoPath = (value: string) =>
        path.isAbsolute(value) ? value : path.resolve(repoRoot, value);
    const defaultExtensionId = "dnakode.vbnet-language-support";
    const extensionIdEnv = process.env.EXTENSION_ID ?? defaultExtensionId;
    const extensionDevelopmentPath = process.env.EXTENSION_DEV_PATH
        ? path.resolve(process.env.EXTENSION_DEV_PATH)
        : path.resolve(__dirname, "..", "extension");
    const extensionTestsPath = path.resolve(__dirname, "suite", "index");

    const vscodeExecutablePath = process.env.VSCODE_EXECUTABLE
        ? path.resolve(process.env.VSCODE_EXECUTABLE)
        : await downloadAndUnzipVSCode("stable");

    const testRoot = path.resolve(__dirname, "..", ".vscode-test");
    const userDataDir = path.join(testRoot, "user-data");
    const extensionsDir = path.join(testRoot, "extensions");
    fs.mkdirSync(userDataDir, { recursive: true });
    fs.mkdirSync(extensionsDir, { recursive: true });

    const userProfile = process.env.USERPROFILE ?? process.env.HOME;
    if (userProfile) {
        const userExtensionsRoot = path.join(userProfile, ".vscode", "extensions");
        if (fs.existsSync(userExtensionsRoot)) {
            const dotnetRuntimeExtensions = fs
                .readdirSync(userExtensionsRoot, { withFileTypes: true })
                .filter((entry) => entry.isDirectory() && entry.name.startsWith("ms-dotnettools.vscode-dotnet-runtime-"))
                .map((entry) => entry.name)
                .sort();
            const latestRuntimeExtension = dotnetRuntimeExtensions[dotnetRuntimeExtensions.length - 1];
            if (latestRuntimeExtension) {
                const sourcePath = path.join(userExtensionsRoot, latestRuntimeExtension);
                const destPath = path.join(extensionsDir, latestRuntimeExtension);
                if (!fs.existsSync(destPath)) {
                    console.log(`Copying .NET runtime extension to isolated dir: ${latestRuntimeExtension}`);
                    fs.cpSync(sourcePath, destPath, { recursive: true });
                }
            }
        }
    }

    const fixtureWorkspace = process.env.FIXTURE_WORKSPACE
        ? resolveRepoPath(process.env.FIXTURE_WORKSPACE)
        : extensionIdEnv === defaultExtensionId
          ? path.resolve(repoRoot, "_test", "codex-tests", "vbnet-lsp", "fixtures", "services")
          : path.resolve(repoRoot, "_test", "codex-tests", "csharp-lsp", "fixtures", "basic");

    const launchArgs = [
        fixtureWorkspace,
        "--user-data-dir",
        userDataDir,
        "--extensions-dir",
        extensionsDir,
        "--disable-workspace-trust",
    ];

    const extensionId = process.env.EXTENSION_ID;
    const extensionVsix = process.env.EXTENSION_VSIX
        ? resolveRepoPath(process.env.EXTENSION_VSIX)
        : undefined;
    if (extensionId || extensionVsix) {
        const extensionsJsonPath = path.join(extensionsDir, "extensions.json");
        if (fs.existsSync(extensionsJsonPath)) {
            try {
                const raw = fs.readFileSync(extensionsJsonPath, "utf8");
                const installs = JSON.parse(raw);
                if (Array.isArray(installs)) {
                    const filtered = installs.filter((install) => {
                        const id = install?.identifier?.id;
                        if (id !== extensionIdEnv) {
                            return true;
                        }

                        const relativeLocation = install?.relativeLocation;
                        if (!relativeLocation) {
                            return false;
                        }

                        const fullPath = path.join(extensionsDir, relativeLocation);
                        return fs.existsSync(fullPath);
                    });

                    if (filtered.length !== installs.length) {
                        fs.writeFileSync(extensionsJsonPath, JSON.stringify(filtered));
                    }
                }
            } catch (error) {
                console.warn(`Failed to prune extensions.json: ${error}`);
            }
        }

        const [cliPath, ...cliArgs] = resolveCliArgsFromVSCodeExecutablePath(vscodeExecutablePath);
        const filteredCliArgs = cliArgs.filter(
            (arg) => !arg.startsWith("--extensions-dir") && !arg.startsWith("--user-data-dir")
        );
        const installTarget = extensionVsix ?? extensionId!;
        const installArgs = [
            ...filteredCliArgs,
            "--install-extension",
            installTarget,
            "--force",
            "--extensions-dir",
            extensionsDir,
            "--user-data-dir",
            userDataDir,
        ];
        console.log(`Installing extension via: ${cliPath} ${installArgs.join(" ")}`);
        cp.spawnSync(cliPath, installArgs, { stdio: "inherit" });

        const listArgs = [
            ...filteredCliArgs,
            "--list-extensions",
            "--extensions-dir",
            extensionsDir,
            "--user-data-dir",
            userDataDir,
        ];
        console.log(`Listing extensions via: ${cliPath} ${listArgs.join(" ")}`);
        cp.spawnSync(cliPath, listArgs, { stdio: "inherit" });
    }

    const defaultServerPath = path.resolve(
        repoRoot,
        "src",
        "VbNet.LanguageServer",
        "bin",
        "Debug",
        "net10.0",
        "VbNet.LanguageServer.dll"
    );
    const extensionTestsEnv = { ...process.env };
    if (!extensionTestsEnv.EXTENSION_ID) {
        extensionTestsEnv.EXTENSION_ID = extensionIdEnv;
    }
    if (extensionTestsEnv.EXTENSION_ID === defaultExtensionId && !extensionTestsEnv.SKIP_CSHARP_TESTS) {
        extensionTestsEnv.SKIP_CSHARP_TESTS = "1";
    }
    const skipDefaultServerPath = extensionTestsEnv.VBNET_SKIP_DEFAULT_SERVER_PATH === "1";
    if (!skipDefaultServerPath && !extensionTestsEnv.VBNET_SERVER_PATH && fs.existsSync(defaultServerPath)) {
        extensionTestsEnv.VBNET_SERVER_PATH = defaultServerPath;
    }

    const captureLogs = process.env.CAPTURE_VSCODE_LOGS === "1";
    const captureTrace = process.env.CAPTURE_VBNET_TRACE === "1";
    const extensionLogId = extensionIdEnv;
    const initialCodePids = listCodePids();
    if (process.env.VSCODE_KILL_BEFORE_TESTS === "1" && initialCodePids.length > 0) {
        console.log(`Killing pre-existing Code.exe processes: ${initialCodePids.join(", ")}`);
        killCodePids(initialCodePids);
    }
    let runError: unknown;
    try {
        await runTests({
            vscodeExecutablePath,
            extensionDevelopmentPath,
            extensionTestsPath,
            launchArgs,
            extensionTestsEnv,
        });
    } catch (error) {
        runError = error;
    } finally {
        if (captureLogs) {
            const logsDir = path.join(userDataDir, "logs");
            if (fs.existsSync(logsDir)) {
                const runs = fs
                    .readdirSync(logsDir, { withFileTypes: true })
                    .filter((entry) => entry.isDirectory())
                    .map((entry) => entry.name)
                    .sort();
                const latest = runs[runs.length - 1];
                if (latest) {
                    const destRoot = path.resolve(__dirname, "..", "logs");
                    const destPath = path.join(destRoot, latest);
                    fs.mkdirSync(destRoot, { recursive: true });
                    if (!fs.existsSync(destPath)) {
                        fs.cpSync(path.join(logsDir, latest), destPath, { recursive: true });
                        console.log(`Copied VS Code logs to ${destPath}`);
                    }

                        if (captureTrace) {
                            const summaryLines: string[] = [];
                            const exthostRoot = path.join(destPath, "window1", "exthost");
                            let traceFound = false;

                            if (fs.existsSync(exthostRoot)) {
                                const extensionLogRoot = path.join(exthostRoot, extensionLogId);
                                if (fs.existsSync(extensionLogRoot)) {
                                    const logFiles = fs.readdirSync(extensionLogRoot);
                                    for (const file of logFiles) {
                                        if (!file.toLowerCase().endsWith(".log")) {
                                            continue;
                                        }

                                        const source = path.join(extensionLogRoot, file);
                                        const destFile = path.join(destPath, file.replace(/\s+/g, "_"));
                                        fs.copyFileSync(source, destFile);
                                        summaryLines.push(`Extension log: ${source}`);

                                        if (file.toLowerCase().includes("trace")) {
                                            traceFound = true;
                                        }
                                    }
                                }

                                const outputFolders = fs
                                    .readdirSync(exthostRoot, { withFileTypes: true })
                                    .filter((entry) => entry.isDirectory() && entry.name.startsWith("output_logging_"))
                                    .map((entry) => entry.name);
                                for (const folder of outputFolders) {
                                    const folderPath = path.join(exthostRoot, folder);
                                    const files = fs.readdirSync(folderPath);
                                    for (const file of files) {
                                        if (!file.toLowerCase().endsWith(".log")) {
                                            continue;
                                        }

                                        if (file.toLowerCase().includes("vb.net") || file.toLowerCase().includes("vbnet")) {
                                            const source = path.join(folderPath, file);
                                            const destFile = path.join(destPath, "vbnet-lsp-trace.log");
                                            fs.copyFileSync(source, destFile);
                                            summaryLines.push(`Trace log: ${source}`);
                                            traceFound = true;
                                        }
                                    }
                                }
                            }

                            if (!traceFound) {
                                summaryLines.push("Trace log not found in extension or output_logging folders.");
                            }

                            const summaryPath = path.join(destPath, "vbnet-output-summary.txt");
                            fs.writeFileSync(summaryPath, summaryLines.join("\n") + "\n");
                        }
                }
            }
        }

        const finalCodePids = listCodePids();
        const newCodePids = finalCodePids.filter((pid) => !initialCodePids.includes(pid));
        if (newCodePids.length > 0) {
            console.log(`Code.exe processes started during test: ${newCodePids.join(", ")}`);
        }

        if (process.env.VSCODE_KILL_ON_EXIT === "1" && newCodePids.length > 0) {
            console.log(`Killing Code.exe processes started during test.`);
            killCodePids(newCodePids);
        }
    }

    if (runError) {
        throw runError;
    }
}

function listCodePids(): number[] {
    if (process.platform !== "win32") {
        return [];
    }

    try {
        const output = cp.execSync('tasklist /FI "IMAGENAME eq Code.exe" /FO CSV /NH', {
            encoding: "utf8",
            stdio: ["ignore", "pipe", "ignore"],
        });
        return output
            .split(/\r?\n/)
            .map((line) => line.trim())
            .filter((line) => line.length > 0 && !line.startsWith("INFO:"))
            .map((line) => line.replace(/^"|"$/g, ""))
            .map((line) => line.split('","'))
            .map((parts) => Number.parseInt(parts[1], 10))
            .filter((pid) => !Number.isNaN(pid));
    } catch {
        return [];
    }
}

function killCodePids(pids: number[]) {
    if (process.platform !== "win32" || pids.length === 0) {
        return;
    }

    for (const pid of pids) {
        cp.spawnSync("taskkill", ["/F", "/PID", pid.toString()], { stdio: "ignore" });
    }
}

main().catch((err) => {
    console.error(err);
    process.exit(1);
});
