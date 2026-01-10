# VS Code Client Harness (Planning Scaffold)

This folder contains a minimal `@vscode/test-electron` harness that can run LSP-related smoke tests inside a real VS Code instance. It is intended for integration testing of the C# and later VB.NET extensions.

## Scope

- Launch VS Code in an isolated profile.
- Install the target extension from Marketplace (ID) or a local VSIX.
- Open a fixture workspace.
- Execute hover/definition/completion/document symbols through VS Code APIs.

## Usage (manual)

```powershell
cd _test\codex-tests\clients\vscode
npm install
npm run compile

# Install C# extension from Marketplace and run tests
$env:EXTENSION_ID = "ms-dotnettools.csharp"
$env:VSCODE_EXECUTABLE = "C:\Programs\Microsoft VS Code\Code.exe"
npm test
```

Optional environment variables:
- `VSCODE_EXECUTABLE`: path to `code.exe` or a VS Code build.
- `EXTENSION_ID`: extension id to install (default `ms-dotnettools.csharp`).
- `EXTENSION_VSIX`: local VSIX path (used instead of `EXTENSION_ID`).
- `EXTENSION_DEV_PATH`: path to a dev extension to load (defaults to `clients/vscode/extension`).
- `FIXTURE_WORKSPACE`: workspace folder to open.
- `FIXTURE_FILE`: file to use for LSP requests.

## Notes

- This harness is intentionally minimal; it does not replace the fast LSP harness.
- Use isolated `--user-data-dir` and `--extensions-dir` to keep tests hermetic.
- Extend tests by adding more fixture workspaces and assertions.
- If extension installation fails with an EPERM rename, delete `clients/vscode/.vscode-test/extensions` and rerun.
