Date: 2026-01-10
Reviewer: Codex (GPT-5) acting as independent test reviewer
Scope: VB.NET Language Server scaffold (Phase 1)

# Independent Test Results

This document captures the current status of independent testing for the VB.NET language server and related harnesses. It is updated to reflect the latest test state and artifacts.

## Current status

- VB.NET LSP smoke harness: executed successfully against the Phase 1 server scaffold over named pipes.
- VB.NET server snapshot: build output captured under `_test/codex-tests/vbnet-lsp/snapshots/20260110-120754`.
- VS Code client harness: installed and validated against the C# extension (baseline), ready to be pointed at the VB.NET extension once available.
- Emacs client harness: connected to Roslyn LSP and VB.NET server over stdio; Roslyn shutdown timed out but the session connected successfully.

## Latest actions

- Added a VB.NET LSP smoke test harness that performs initialize/initialized, text document lifecycle notifications, and shutdown/exit over named pipes or stdio.
- Added a VB.NET fixture file for basic didOpen/didChange/didSave/didClose coverage.
- Added a VB.NET test runner that builds the server, snapshots binaries, and runs the smoke test.
- Added a top-level test runner entry (`-Suite vbnet-lsp`) to invoke the VB.NET harness.
- Executed `_test/codex-tests/vbnet-lsp/run-tests.ps1` (named pipe mode) with text document lifecycle notifications.
- Downloaded Emacs 29.4 portable and ran `eglot` smoke tests for C# and VB.NET over stdio.

## Open items / next steps

- Investigate why the server closes the transport on `shutdown` and decide whether to keep the client-side graceful handling or update the server.
- Track the NU1903 warning for `Microsoft.Build.Tasks.Core` (via MSBuild workspace dependency).
- When the VB.NET extension packaging is ready (VSIX or dev extension path), wire it into the VS Code harness for end-to-end validation.
- Investigate Roslyn LSP shutdown timeout under Emacs `eglot` (likely requires longer timeout or different shutdown sequence).

## Latest run details

- Command: `_test/codex-tests/vbnet-lsp/run-tests.ps1 -SkipSnapshot`
- Transport: named pipe
- Result: `initialize` succeeded, text document lifecycle notifications sent, shutdown completed with connection drop handled as expected.
- Warnings:
  - `NU1903` for `Microsoft.Build.Tasks.Core 17.7.2` vulnerability advisory during build.

### Emacs harness runs

- C# test: Connected to Roslyn LSP over stdio; shutdown request timed out, server exited with status 9; treated as non-fatal for now.
- VB.NET test: Connected to VB.NET server over stdio using `fundamental-mode`; shutdown requested, server exited with status 9.
