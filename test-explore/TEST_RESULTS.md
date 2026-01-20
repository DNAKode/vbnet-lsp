Date: 2026-01-20
Author: Codex (GPT-5) acting as test reviewer
Host: Windows (C:\Work\vbnet-lsp)

# Test Results

## Current status

- C# implementation and C# harnesses have been removed; all test commands below are VB.NET-only.
- Update this file after significant exploratory runs.

## Recommended test commands (VB.NET only)

### CI-safe unit/manifest tests

```powershell
# Language server tests
 dotnet test test\VbNet.LanguageServer.Tests.Vb\VbNet.LanguageServer.Tests.Vb.vbproj -c Release

# Extension manifest tests
 dotnet test test\VbNet.Extension.Tests.Vb\VbNet.Extension.Tests.Vb.vbproj -c Release
```

### LSP smoke harness

```powershell
 test-explore\vbnet-lsp\run-tests.ps1
```

### VS Code harness (headless)

```powershell
cd test-explore\clients\vscode
npm test
```

Optional flags:
- `SKIP_VBNET_DEBUG=1` to skip debugger suite.
- `SKIP_VBNET_SMOKE=1` to skip LSP smoke suite.
- `FIXTURE_WORKSPACE` to point at a fixture workspace (use `test-explore/vbnet-lsp/fixtures/services` for LSP smoke).

### Emacs harness

```powershell
 test-explore\clients\emacs\run-tests.ps1 -Suite vbnet
```

## Recent runs

### 2026-01-20 — test-explore suite (all, VB.NET)

Command:
- `test-explore\run-tests.ps1`

Outcome: PASS (with non-fatal warnings)
Notes:
- LSP smoke: PASS (snapshot `test-explore/vbnet-lsp/snapshots/20260120-102607`).
- Emacs eglot: PASS, shutdown timeout after server exit (non-fatal). Log: `test-explore/clients/emacs/logs/emacs-eglot-20260120T102611.log`.
- DWSIM smoke: PASS, but no solution or VB.NET projects detected in `_external/dwsim` (workspace scan only).

## Previous runs

### 2026-01-19 — VS Code harness (VB.NET server)

Commands (from `test-explore/clients/vscode`):
- `VBNET_SERVER_PATH=src\VbNet.LanguageServer.Vb\bin\Debug\net10.0\VbNet.LanguageServer.dll CAPTURE_VSCODE_LOGS=1 CAPTURE_VBNET_TRACE=1 VSCODE_KILL_BEFORE_TESTS=1 VSCODE_KILL_ON_EXIT=1 npm test`

Outcome: PASS (14 passing, 5 pending)
Notes:
- Named-pipe run verified by setting `vbnet.server.transportType=namedPipe` in the fixture settings.
- Non-fatal DAP warning: `Failed command 'threads'` during debug startup.
- Log bundles: `test-explore/clients/vscode/logs/20260119T224840`, `test-explore/clients/vscode/logs/20260119T224915`.

### 2026-01-19 — LSP smoke harness (VB.NET)

Command:
- `test-explore\vbnet-lsp\run-tests.ps1`

Outcome: PASS
Notes:
- Snapshots recorded under `test-explore/vbnet-lsp/snapshots/`.

### 2026-01-19 — CI-safe tests (VB.NET only)

Commands:
- `dotnet test test\VbNet.LanguageServer.Tests.Vb\VbNet.LanguageServer.Tests.Vb.vbproj -c Release`
- `dotnet test test\VbNet.Extension.Tests.Vb\VbNet.Extension.Tests.Vb.vbproj -c Release`

Outcome: PASS (135/135, 3/3)

## Protocol anomalies (latest run)
Run: Suite=all Transport=pipe

None detected.
## Timing summary (latest run)
Run: Suite=all Transport=pipe

- [DWSIM] server_starting (191.17 ms)
- [DWSIM] initialize_response (424.01 ms)
- [DWSIM] didOpen_sent (908.5 ms)
