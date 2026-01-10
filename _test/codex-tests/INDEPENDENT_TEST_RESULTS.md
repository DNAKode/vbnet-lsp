Date: 2026-01-10
Reviewer: Codex (GPT-5) acting as independent test reviewer
Scope: VB.NET Language Server scaffold (Phase 1)

# Independent Test Results

This document captures the current status of independent testing for the VB.NET language server and related harnesses. It is updated to reflect the latest test state and artifacts.

## Current status

- VB.NET LSP smoke harness: executed successfully against the Phase 1 server scaffold over named pipes.
- VB.NET diagnostics smoke test: currently failing to receive `textDocument/publishDiagnostics` from the server after workspace load and didOpen/didChange (see latest run).
- VB.NET server snapshot: build output captured under `_test/codex-tests/vbnet-lsp/snapshots/20260110-125504` (latest).
- VS Code client harness: installed and validated against the C# extension (baseline), ready to be pointed at the VB.NET extension once available.
- Emacs client harness: connected to Roslyn LSP and VB.NET server over stdio; Roslyn shutdown timed out but the session connected successfully.
- Protocol anomaly log: now emitted to `_test/codex-tests/logs/protocol-anomalies.jsonl` and auto-summarized after each run.
- DWSIM harness: scaffolded under `_test/codex-tests/dwsim` for large-solution smoke and timing.

## Latest actions

- Added a VB.NET LSP smoke test harness that performs initialize/initialized, text document lifecycle notifications, and shutdown/exit over named pipes or stdio.
- Added a VB.NET fixture file for basic didOpen/didChange/didSave/didClose coverage.
- Added a VB.NET test runner that builds the server, snapshots binaries, and runs the smoke test.
- Added a top-level test runner entry (`-Suite vbnet-lsp`) to invoke the VB.NET harness.
- Executed `_test/codex-tests/vbnet-lsp/run-tests.ps1` (named pipe mode) with text document lifecycle notifications.
- Downloaded Emacs 29.4 portable and ran `eglot` smoke tests for C# and VB.NET over stdio.
- Ran the full `_test/codex-tests/run-tests.ps1 -Suite all` suite (C# dotnet, C# node, VB.NET smoke, Emacs).
- Ran C# feature tests against the fixture solution using the dotnet harness (`-FeatureTests`).
- Ran the VB.NET diagnostics smoke test with an error fixture solution; no diagnostics were published after two didOpen/didChange cycles.
- Re-ran `_test/codex-tests/run-tests.ps1 -Suite all`; new VB.NET snapshot created at `_test/codex-tests/vbnet-lsp/snapshots/20260110-125504`.
- Extended the VB.NET diagnostics smoke harness to inject diagnostics settings (`workspace/configuration` + `workspace/didChangeConfiguration`) and assert an expected diagnostic code (`BC30311`) when diagnostics are published.
- Re-ran the VB.NET diagnostics smoke test with expected code `BC30311`; still no diagnostics were received after retry.
- Added optional `textDocument/didSave` emission in the diagnostics smoke harness (auto-enabled for `openSave`/`saveOnly` modes).
- Added protocol anomaly logging in both C# harnesses and the VB.NET harness, with automatic summary insertion after test runs.
- Ran C# dotnet harness after crash-proofing updates; completed without protocol anomalies.
- Ran VB.NET diagnostics in `openSave` and `saveOnly` modes (with didSave); still no diagnostics were received.
- Identified DWSIM solution entry points (`_test/dwsim/DWSIM.sln` plus plugin solutions) and added DWSIM smoke scaffolding.
- Verified headless VS Code can open the DWSIM workspace via the VS Code harness (workspace open test passes).
- Ran the DWSIM smoke harness against `_test/dwsim`; workspace load completed with missing NuGet package warnings and unsupported C# project diagnostics.
- Added timing capture for DWSIM (server start, initialize response, solution loading/loaded, didOpen) and recorded the latest timings below.

## Open items / next steps

- Investigate why the server closes the transport on `shutdown` and decide whether to keep the client-side graceful handling or update the server.
- Track the NU1903 warning for `Microsoft.Build.Tasks.Core` (via MSBuild workspace dependency).
- When the VB.NET extension packaging is ready (VSIX or dev extension path), wire it into the VS Code harness for end-to-end validation.
- Investigate Roslyn LSP shutdown timeout under Emacs `eglot` (likely requires longer timeout or different shutdown sequence).
- Investigate Roslyn LSP crashes with `Unexpected value kind: Null` / `Method must be set` in StreamJsonRpc (appears tied to test harness client traffic).

## Latest run details

- Command: `_test/codex-tests/run-tests.ps1 -Suite all`
- Result: all harnesses completed; VB.NET smoke succeeded with connection-drop on shutdown treated as expected; Emacs C# shutdown timed out (non-fatal); Emacs VB.NET connected and shutdown.
- Warnings:
  - `NU1903` for `Microsoft.Build.Tasks.Core 17.7.2` vulnerability advisory during build.
  - VSTHRD warnings in C# and VB.NET harness builds.

### Emacs harness runs

- C# test: Connected to Roslyn LSP over stdio; shutdown request timed out, server exited with status 9; treated as non-fatal for now.
- VB.NET test: Connected to VB.NET server over stdio using `fundamental-mode`; shutdown requested, server exited with status 9.

### C# feature test run

- Command: `_test/codex-tests/run-tests.ps1 -Suite csharp-dotnet -Transport pipe -FeatureTests`
- Result: completion/hover/definition/references/document symbols passed on fixture.
- Note: `workspace/projectInitializationComplete` did not arrive within timeout; tests proceeded anyway.

### VB.NET diagnostics smoke test run

- Command: `_test/codex-tests/vbnet-lsp/run-tests.ps1 -Diagnostics -SkipSnapshot`
- Result: initialize + workspace load succeeded; no `textDocument/publishDiagnostics` received after two open/change cycles; test failed with "Expected diagnostics but none were received."
- Notes: total timeout increased to 150s to allow two diagnostics waits; expected code `BC30311` configured; failure persists, indicating diagnostics publishing is either delayed beyond 60s or not triggered by current document lifecycle.

### VB.NET diagnostics (openSave/saveOnly) runs

- Command: `_test/codex-tests/vbnet-lsp/run-tests.ps1 -Diagnostics -DiagnosticsMode openSave -SkipSnapshot`
- Command: `_test/codex-tests/vbnet-lsp/run-tests.ps1 -Diagnostics -DiagnosticsMode saveOnly -SkipSnapshot`
- Result: didSave notifications were sent; still no diagnostics were received (same failure as openChange).

### C# dotnet harness run

- Command: `_test/codex-tests/run-tests.ps1 -Suite csharp-dotnet`
- Result: initialize/initialized/solution open completed without protocol anomalies logged; shutdown output not shown in console but command returned successfully.

### VS Code headless open (DWSIM)

- Command: `npm test` (with `FIXTURE_WORKSPACE=_test/dwsim`, `EXTENSION_ID=ms-dotnettools.csharp`, `VSCODE_EXECUTABLE=C:\Programs\Microsoft VS Code\Code.exe`)
- Result: workspace open test passed; C# extension hover/definition/completion/symbols tests passed on the fixture file.

### DWSIM smoke harness run

- Command: `_test/codex-tests/dwsim/run-tests.ps1`
- Result: workspace root `_test/dwsim` loaded; server selected `DWSIM.sln`; 31 VB.NET projects loaded; many workspace diagnostics reported for missing NuGet packages and unsupported C# projects.
- Notes: missing packages include `SkiaSharp.1.68.2.1`, `Eto.Forms.2.8.3`, and `MSBuild.ILMerge.Task.1.1.3`; C# projects are expected to be skipped in Phase 1.

## Protocol anomalies (latest run)
Run: DWSIM smoke Transport=pipe

None detected.
## Timing summary (latest run)
Run: DWSIM smoke Transport=pipe

- [DWSIM] server_starting (255.03 ms)
- [DWSIM] initialize_response (453.98 ms)
- [DWSIM] solution_loading (780.79 ms)
- [DWSIM] solution_loaded (6917.42 ms)
- [DWSIM] didOpen_sent (6919.78 ms)
