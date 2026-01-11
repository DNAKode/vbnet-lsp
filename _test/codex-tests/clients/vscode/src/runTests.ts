import * as path from "path";
import * as fs from "fs";
import * as cp from "child_process";
import {
    downloadAndUnzipVSCode,
    resolveCliArgsFromVSCodeExecutablePath,
    runTests,
} from "@vscode/test-electron";

async function main() {
    const repoRoot = path.resolve(__dirname, "..", "..", "..", "..");
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
        ? path.resolve(process.env.FIXTURE_WORKSPACE)
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
    const extensionVsix = process.env.EXTENSION_VSIX;
    if (extensionId || extensionVsix) {
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
    if (!extensionTestsEnv.VBNET_SERVER_PATH && fs.existsSync(defaultServerPath)) {
        extensionTestsEnv.VBNET_SERVER_PATH = defaultServerPath;
    }

    const captureLogs = process.env.CAPTURE_VSCODE_LOGS === "1";
    const captureTrace = process.env.CAPTURE_VBNET_TRACE === "1";
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
                        const outputRoot = path.join(destPath, "window1", "exthost");
                        let traceFound = false;

                        if (fs.existsSync(outputRoot)) {
                            const outputFolders = fs
                                .readdirSync(outputRoot, { withFileTypes: true })
                                .filter((entry) => entry.isDirectory() && entry.name.startsWith("output_logging_"))
                                .map((entry) => entry.name);
                            for (const folder of outputFolders) {
                                const folderPath = path.join(outputRoot, folder);
                                const files = fs.readdirSync(folderPath);
                                for (const file of files) {
                                    if (!file.toLowerCase().endsWith(".log")) {
                                        continue;
                                    }

                                    if (file.toLowerCase().includes("vb.net") || file.toLowerCase().includes("vbnet")) {
                                        const source = path.join(folderPath, file);
                                        const dest = path.join(destPath, "vbnet-lsp-trace.log");
                                        fs.copyFileSync(source, dest);
                                        summaryLines.push(`Trace log: ${source}`);
                                        traceFound = true;
                                    }
                                }
                            }
                        }

                        if (!traceFound) {
                            summaryLines.push("Trace log not found in output_logging folders.");
                        }

                        const summaryPath = path.join(destPath, "vbnet-output-summary.txt");
                        fs.writeFileSync(summaryPath, summaryLines.join("\n") + "\n");
                    }
                }
            }
        }
    }

    if (runError) {
        throw runError;
    }
}

main().catch((err) => {
    console.error(err);
    process.exit(1);
});
