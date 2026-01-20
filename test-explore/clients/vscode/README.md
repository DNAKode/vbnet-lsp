# VS Code Client Harness (Planning Scaffold)

This folder contains a minimal `@vscode/test-electron` harness that can run LSP-related smoke tests inside a real VS Code instance. It is intended for integration testing of the `VB.NET` extension.

## Scope

- Launch VS Code in an isolated profile.
- Install the target extension from Marketplace (ID) or a local VSIX.
- Open a fixture workspace.
- Execute hover/definition/completion/document symbols through VS Code APIs.

## Usage (manual)

```powershell
cd test-explore\clients\vscode
npm install
npm run compile

# Run tests with the VB.NET extension
$env:VSCODE_EXECUTABLE = "C:\Programs\Microsoft VS Code\Code.exe"
npm test
```

Optional environment variables:
- `VSCODE_EXECUTABLE`: path to `code.exe` or a VS Code build.
- `EXTENSION_ID`: extension id to install (default `dnakode.vbnet-language-support`).
- `EXTENSION_VSIX`: local VSIX path (used instead of `EXTENSION_ID`).
- `EXTENSION_DEV_PATH`: path to a dev extension to load (defaults to `src/extension`).
- `FIXTURE_WORKSPACE`: workspace folder to open.
- `FIXTURE_FILE`: file to use for LSP requests.
- `VBNET_SERVER_PATH`: override language server path for `VB.NET` tests.
- `NETCOREDBG_PATH`: override debugger path for `VB.NET` debug tests.
- `SKIP_VBNET_SMOKE`: set to `1` to skip `VB.NET` LSP smoke tests.
- `SKIP_VBNET_DEBUG`: set to `1` to skip `VB.NET` debug tests.
- `VSCODE_KILL_BEFORE_TESTS`: set to `1` to terminate existing Code.exe before tests.
- `VSCODE_KILL_ON_EXIT`: set to `1` to terminate Code.exe spawned by the harness.
- `VBNET_DWSIM`: set to `1` to enable the DWSIM smoke suite (requires `_external/dwsim`).

## Common runs

Run `VB.NET` LSP smoke tests only:

```powershell
$env:SKIP_VBNET_DEBUG = "1"
npm test
```

Run `VB.NET` debug tests only:

```powershell
$env:SKIP_VBNET_SMOKE = "1"
$env:FIXTURE_WORKSPACE = "test\TestProjects\DebugConsole"
npm test
```

Run DWSIM smoke tests (requires `_external/dwsim`):

```powershell
$env:VBNET_DWSIM = "1"
$env:FIXTURE_WORKSPACE = "_external\\dwsim"
npm test
```

## Notes

- This harness is intentionally minimal; it does not replace the fast LSP harness.
- Use isolated `--user-data-dir` and `--extensions-dir` to keep tests hermetic.
- Extend tests by adding more fixture workspaces and assertions.
- If extension installation fails with an EPERM rename, delete `clients/vscode/.vscode-test/extensions` and rerun.
- LSP smoke tests expect files under `test-explore/vbnet-lsp/fixtures`; if you set `FIXTURE_WORKSPACE` elsewhere, keep `SKIP_VBNET_SMOKE=1` to avoid standalone-document failures.

