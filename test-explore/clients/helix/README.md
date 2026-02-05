# Helix (manual LSP check)

This folder holds a Helix `languages.toml` snippet for running the VB.NET language server over stdio.
Helix does not have a supported headless test harness yet, so this is a manual smoke check.

## Quick start (manual)

1) Build or locate the server executable (`VbNet.LanguageServer.exe`).
2) Copy `test-explore/clients/helix/.helix` into the workspace you want to open.
3) Edit `.helix/languages.toml` and set the `command` path to the server.
4) Launch Helix in that workspace (example below).

Example fixture:
- Workspace: `test-explore/vbnet-lsp/fixtures/services`
- File: `ServiceSamples.vb`

Example command:
```
hx ServiceSamples.vb
```

## Convenience script

`run-helix.ps1` will:
- Try to locate `hx` on PATH (or use `-HelixExe`).
- Try to locate the server exe from your VS Code extension install (or use `-ServerExe`).
- Create a workspace-local `.helix/languages.toml`.
- Launch Helix in the fixture workspace.

Example:
```
# If hx is on PATH and the extension is installed
./test-explore/clients/helix/run-helix.ps1

# Explicit paths
./test-explore/clients/helix/run-helix.ps1 -HelixExe C:\Tools\Helix\hx.exe -ServerExe C:\path\to\VbNet.LanguageServer.exe
```
