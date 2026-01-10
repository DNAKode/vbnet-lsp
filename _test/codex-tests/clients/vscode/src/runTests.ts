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

    await runTests({
        vscodeExecutablePath,
        extensionDevelopmentPath,
        extensionTestsPath,
        launchArgs,
    });
}

main().catch((err) => {
    console.error(err);
    process.exit(1);
});
