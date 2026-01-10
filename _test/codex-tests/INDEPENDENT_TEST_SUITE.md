Date: 2026-01-10
Author: Codex (GPT-5) acting as independent test reviewer
Scope: Comprehensive automated test suite plan for VB.NET Language Support (vbnet-lsp)
Alignment: Matches PROJECT_PLAN.md phases, docs/*, and the independent review recommendations

# Codex Independent Test Suite Plan

This document defines a comprehensive, automated test suite for the VB.NET Language Support project. It is independent of current implementation details but maps to the planned architecture, phases, and features. The suite is designed to be incremental, allowing phased adoption while maintaining a coherent end-to-end quality strategy.

## Objectives and test philosophy

Primary objectives:
- Validate correctness and parity with the C# extension behavior for the agreed feature subset.
- Ensure performance targets, stability over time, and cross-platform compatibility.
- Provide automation-first coverage across protocol, workspace, and language feature layers.
- Enable incremental adoption without blocking development velocity.

Philosophy:
- Prefer contract-style tests that encode expected behavior over brittle UI tests.
- Validate protocol compliance and server robustness under failure and cancellation.
- Isolate layers where possible (unit and component tests), then confirm full integration with LSP and editor harnesses.
- Keep tests deterministic and non-interactive; all required inputs are provided by fixtures or scripted steps.

## Test hierarchy and scope

### 1) Unit tests (fast, isolated)

Purpose: Validate correctness of core algorithms and conversions without LSP or OS dependencies.

Targets:
- Roslyn adapter logic (completion, hover, definition, references, rename, symbols).
- Text change application (incremental sync, range application, version tracking).
- LSP type translations (positions, ranges, locations, diagnostics, completion kinds).
- Cancellation behavior and task orchestration.
- Diagnostic debouncing logic and throttling policies.
- Configuration parsing and validation defaults.

Artifacts:
- Pure C# test projects using MSTest/xUnit/NUnit (match project standard).
- Deterministic fixture projects (SmallProject and MediumProject).

### 2) Component tests (server-in-process)

Purpose: Validate server behavior without VS Code; ensure LSP handlers respond correctly.

Targets:
- JSON-RPC/LSP routing, initialize lifecycle, shutdown/exit.
- Capability negotiation responses for enabled features.
- Workspace loading flows for .sln and .vbproj.
- Diagnostics push behavior under edits and saves.
- Diagnostics mode configuration (`openChange`, `openSave`, `saveOnly`) and debounce timing.
- Diagnostics debounce timing (expect publish after configured delay).
- Custom protocol methods (if adopted) for solution/project loading.
- Error handling paths: missing SDK, malformed requests, workspace reload.

Harness:
- In-process server with simulated LSP client using a JSON-RPC test harness.
- Message recordings for expected request/response sequences.

### 3) LSP integration tests (black-box server)

Purpose: Validate language server from the outside over the real transport.

Targets:
- Transport startup (stdio or named pipe, per decision).
- Incremental text synchronization, versioning and concurrency.
- Core LSP features and their shape (completion, hover, definition, references, rename, symbols).
- Document and workspace diagnostics timing and correctness.
- Diagnostics mode matrix (default `openChange`, `openSave`, `saveOnly`) with explicit settings payloads.
- PublishDiagnostics shape, severity mapping, and code description links (VB error codes).
- Cancellation and timeout handling.
- Windows-specific URI conversion correctness.

Harness:
- Standalone LSP test runner that launches server executable and communicates over the selected transport.
- Captures response timings for latency targets.

### 4) Extension integration tests (VS Code)

Purpose: Validate extension activation, configuration, and editor integrations.

Targets:
- Activation events (.vb file open, workspace containing .sln/.vbproj).
- Output channels and log level propagation.
- Commands (restart server, select solution, show output).
- Configuration changes (debounce and diagnostics mode).
- Workspace trust behavior (limited activation if applicable).

Harness:
- VS Code extension test runner (`@vscode/test-electron`) with scripted test fixtures.
- Use non-interactive command execution and synthetic workspaces.

### 5) Multi-editor protocol tests (Emacs, others)

Purpose: Validate LSP compliance beyond VS Code.

Targets:
- LSP request/response behavior under different clients.
- Feature coverage of MVP LSP actions.

Harness:
- Emacs lsp-mode batch testing in CI (Phase 1 or Phase 2 depending on scope).
- Optional future: Neovim LSP harness if adopted by users.

### 6) End-to-end tests (real-world projects)

Purpose: Validate behavior, performance, and stability at scale.

Targets:
- DWSIM (large VB.NET codebase) for performance, memory, and stability.
- One or two medium-sized open-source VB.NET projects for feature correctness.
- Mixed solutions (Phase 4) to ensure VB-only behavior in mixed-language solutions.

Harness:
- Automated CLI to open solutions, trigger analysis, and collect metrics.
- Repeatable performance runs for regression tracking.

### DWSIM-specific plan (Phase 1-2)

Objectives:
- Validate VB.NET LSP workspace discovery on a large, multi-project solution.
- Establish baseline timings (startup, solution load, first diagnostics).
- Exercise text sync stability against real-world source files.

Approach:
- Use `_test/dwsim/DWSIM.sln` as primary workspace root.
- Start with a single file open (e.g., `DWSIM/ApplicationEvents.vb`) and smoke LSP lifecycle.
- Add timing capture in the DWSIM harness and record results in `INDEPENDENT_TEST_RESULTS.md`.
- Extend to diagnostics once publishDiagnostics is working in smaller fixtures.
- Track external restore requirements (NuGet packages) and record missing-package diagnostics as part of readiness gates.

Scaffolding:
- `_test/codex-tests/dwsim/run-tests.ps1` invokes the VB.NET LSP smoke harness with a DWSIM root.
- Optional VS Code headless open check using the VS Code harness with `FIXTURE_WORKSPACE=_test/dwsim`.

## Feature coverage map

Core LSP features (Phase 1):
- textDocument sync: didOpen, didChange (incremental), didClose, didSave.
- diagnostics: push model with debounce and severity mapping.
- completion: items, commit characters, resolve.
- hover: quick info and documentation.
- definition and references.
- rename and prepareRename.
- document/workspace symbols.

Phase 2 features:
- formatting (document/range), EditorConfig adherence.
- code actions and resolves.
- semantic tokens (full/range).
- signature help.
- debugging integration with netcoredbg (launch, attach).
- folding ranges.

Phase 3 features:
- inlay hints.
- call hierarchy and type hierarchy.
- code lens.
- on-type formatting.
- performance tuning with large solutions.

Phase 4 features:
- mixed-language solutions (serve VB only).
- multi-root workspaces.
- advanced debugging features (conditional breakpoints, watch).

Protocol aspects:
- initialization options and capability negotiation.
- cancellation, progress, error handling, and request timeouts.
- custom methods if adopted (solution/project open, build diagnostics, etc.).

## Test infrastructure and tooling

Required infrastructure:
- LSP test harness (Node or .NET) capable of transport-agnostic communication.
- Local Roslyn LSP smoke test harness (experimental) to validate starting a locally built LSP server without VS Code.
- Protocol method source-of-truth loading (read roslynProtocol.ts) to exercise custom extension methods in tests.
- Protocol anomaly logging (JSONL) and automatic inclusion in `INDEPENDENT_TEST_RESULTS.md` after each run.
- Fixture projects and deterministic test data:
  - SmallProject and MediumProject in `test/TestProjects`.
  - DWSIM in `_test/dwsim` (cloned, not shipped).
  - Optional additional OSS VB.NET project(s) for feature correctness.
  - C# fixtures for harness validation (`fixtures/basic`, `fixtures/linq`, `fixtures/generics`).
  - VB.NET fixtures for smoke testing text sync (`vbnet-lsp/fixtures/basic`).
  - VB.NET diagnostics fixture solution (`vbnet-lsp/fixtures/diagnostics`) with intentional compile errors.
- Telemetry-free default operation; tests must disable any telemetry.
- CI workflows for Windows, Linux, macOS.

External dependencies:
- .NET SDK (targeted version).
- VS Code for extension tests.
- netcoredbg binary for debugger tests (Phase 2).
- Emacs and lsp-mode for multi-editor tests (Phase 1 or Phase 2).
- Roslyn repo (optional, for local baseline builds of Microsoft.CodeAnalysis.LanguageServer).

Alternate client strategy (Phase 1-2 planning):
- VS Code extension tests via `@vscode/test-electron` to run the C# (and later VB) extension in a controlled, automated instance of VS Code. This gives realistic extension activation, workspace trust, and editor integrations. Source: https://code.visualstudio.com/api/working-with-extensions/testing-extension
- Proposed harness steps for VS Code:
  1) Use `@vscode/test-electron` to download a pinned VS Code build in CI.
  2) Launch with isolated directories (`--extensions-dir`, `--user-data-dir`, `--disable-extensions` except target).
  3) Install the extension under test (local VSIX or dev path).
  4) Open a fixture workspace, wait for activation, then use VS Code APIs to trigger hover/definition/completion and verify results.
- Emacs batch tests with ERT + lsp-mode (headless) to validate basic LSP compliance and non-UI flows. Use a minimal Emacs config plus lsp-mode setup in CI, open a workspace, wait for diagnostics, then execute hover/definition/completion requests through `lsp-request`.
- Keep the current LSP harness as the fast baseline (transport + protocol correctness) and treat VS Code/Emacs as integration tiers, not the primary gate.
- A minimal VS Code harness scaffold now exists under `_test/codex-tests/clients/vscode` to run smoke tests inside VS Code using `@vscode/test-electron`.
- A minimal Emacs harness now exists under `_test/codex-tests/clients/emacs` using built-in `eglot` for stdio-based tests.
- Future Emacs expansion: evaluate `lsp-mode` for richer client coverage once a non-interactive package install path is available; retain `eglot` as the zero-dependency baseline.

Data capture:
- Standard test logs for LSP request/response.
- Performance metrics capture (startup time, diagnostics latency, completion latency).
- Memory usage snapshots during E2E runs.

## Execution plan by phase

Phase 0 (Bootstrap):
- Define LSP test harness scaffold.
- Validate a minimal stdio-based LSP handshake against a locally built Roslyn language server.
- Unit tests for text change application and LSP type conversions.
- Component tests for initialize/shutdown and configuration parsing.

Phase 1 (MVP):
- LSP integration tests for core features and incremental sync.
- Extension integration tests for activation and commands.
- Performance baseline on MediumProject and DWSIM (automated, non-blocking).
- Optional Emacs lsp-mode validation in CI (if feasible without slowing MVP).

Phase 2:
- Add formatting, code actions, semantic tokens, signature help tests.
- Debugger integration tests using netcoredbg (launch/attach).
- Expand E2E performance suite; add memory growth tests.

Phase 3:
- Add inlay hints, hierarchy, and code lens tests.
- Expand latency and stability tests under long-running sessions.

Phase 4:
- Multi-root and mixed-language solution tests.
- Advanced debugging features and workspace-scale operations.

## Parity and regression strategy

Parity subset definition:
- Establish a minimal, versioned parity checklist (v0.1) aligned to MVP features.
- For each parity item, add a contract test that compares outputs against expected norms (shape, counts, invariants).
- Avoid direct full-output diff when unstable; prefer invariant-based assertions.

Regression gates:
- Fast unit and component tests on every PR.
- LSP integration and extension tests on PRs and nightly.
- Performance and DWSIM tests nightly or on-demand due to cost.

## Non-interactive automation requirements

- No prompts; all configuration provided via settings, environment variables, or test harness parameters.
- Solution selection must be deterministic (explicit solution path in tests).
- Debugger tests must run with predefined launch configs.
- All external repositories cloned via scripts in CI; no manual steps.

## Test policy (independent reviewer)

- Build test infrastructure and test plans without modifying implementation code.
- If extension/server defects are found, record them in test results rather than patching product code.

## Risk areas and mitigations

Risk: Transport mismatch (stdio vs named pipes).
Mitigation: Make harness transport-agnostic; run transport tests based on configured server mode. The current experimental harness validates stdio for Roslyn LSP; extend to named pipes when parity testing begins.

Risk: URI handling on Windows.
Mitigation: Include Windows-specific URI conversion tests in LSP integration suite.

Risk: Large-project tests are slow and flaky.
Mitigation: Separate nightly workflow, strict timeouts, and reduced parallelism for DWSIM.

Risk: Feature gating and defaults drift.
Mitigation: Add tests for capability advertisement and settings-driven feature enabling.

## Implementation prerequisites (planning only)

- Decide language server executable name and entrypoint.
- Finalize transport mechanism (stdio vs named pipes).
- Define custom LSP methods (if any).
- Standardize settings and defaults for feature toggles.
- Document expected error messages and diagnostics format.

## Alignment to project docs and review

This plan aligns with:
- README.md goals (feature parity, performance, open-source debugging).
- PROJECT_PLAN.md phases and success criteria.
- docs/architecture.md layering and LSP flow.
- docs/features.md feature roadmap and status.
- Independent review recommendations: transport strategy, parity subset, runtime acquisition, URI handling, and test gating.

## Summary

This test suite provides a comprehensive, phased path to high confidence: fast unit tests, robust LSP integration, VS Code extension validation, multi-editor protocol verification, and real-world performance checks. It is designed to be automated, incremental, and aligned with the project's architecture and stated goals.

## Status (parking context for future runs)

What is already implemented in `_test/codex-tests/csharp-lsp`:
- A C# LSP smoke harness in C# (`CSharpLspSmokeTest`) that can start a locally built Roslyn LSP server and perform:
  - `initialize`, `initialized`, `shutdown`, `exit` over stdio or named pipes.
  - Optional `solution/open` notification using the method name parsed from the C# extension’s `roslynProtocol.ts` (source of truth).
- Feature-level LSP requests (completion, hover, definition, references, document symbols) against a small fixture solution using a caret marker to pick the test position.
- A README with exact command lines for building Roslyn LSP, running the harness, and executing feature tests.
- A Node client (`node-client.ts`) that connects to the named pipe and uses JSON-RPC over the extension’s transport. It completes the `initialize` + `shutdown` cycle and sends `solution/open` using the method name parsed from `roslynProtocol.ts`.
- A top-level test runner script (`_test/codex-tests/run-tests.ps1`) to rerun the C# harnesses (node and dotnet) consistently.
- Additional fixture solutions to broaden feature coverage: LINQ extension methods and generic interface invocation.

What is already implemented in `_test/codex-tests/vbnet-lsp`:
- VB.NET LSP smoke harness (`VbNetLspSmokeTest`) that performs initialize/initialized, text document lifecycle notifications, and shutdown/exit over named pipes or stdio.
- A VB.NET test runner (`vbnet-lsp/run-tests.ps1`) that builds the server, snapshots binaries, and runs the smoke test.
- A basic VB.NET fixture file used for didOpen/didChange/didSave/didClose coverage.
- A diagnostics fixture solution with a VB compile error, used to validate `textDocument/publishDiagnostics`.
- Diagnostics mode in the smoke harness that waits for `textDocument/publishDiagnostics` on the fixture file and retries didOpen/didChange once if none arrive.
- Diagnostics settings injection (via `workspace/configuration` and `workspace/didChangeConfiguration`) plus expected diagnostic code checks in the smoke harness.
- Diagnostics harness can optionally send `textDocument/didSave` for `openSave`/`saveOnly` mode validation.

Key paths and artifacts:
- Roslyn LSP build output: `_external/roslyn/artifacts/bin/Microsoft.CodeAnalysis.LanguageServer/Release/net10.0/Microsoft.CodeAnalysis.LanguageServer.dll`
- C# extension protocol definitions: `_external/vscode-csharp/src/lsptoolshost/server/roslynProtocol.ts`
- Smoke harness: `_test/codex-tests/csharp-lsp/CSharpLspSmokeTest/`
- Node client: `_test/codex-tests/csharp-lsp/node-client.ts`
- Fixture solution: `_test/codex-tests/csharp-lsp/fixtures/basic/`
- Additional fixtures: `_test/codex-tests/csharp-lsp/fixtures/linq/`, `_test/codex-tests/csharp-lsp/fixtures/generics/`
- VS Code harness scaffold: `_test/codex-tests/clients/vscode/`
- VB.NET smoke harness: `_test/codex-tests/vbnet-lsp/VbNetLspSmokeTest/`
- VB.NET fixtures: `_test/codex-tests/vbnet-lsp/fixtures/basic/`
- VB.NET diagnostics fixture: `_test/codex-tests/vbnet-lsp/fixtures/diagnostics/`
- VB.NET snapshots: `_test/codex-tests/vbnet-lsp/snapshots/`
- DWSIM harness: `_test/codex-tests/dwsim/`

Validated behavior:
- Named pipe connection works from the C# harness and completes LSP handshake.
- `solution/open` notification uses the method name resolved from `roslynProtocol.ts` and is sent after initialization.
- Feature tests against the fixture solution succeed (completion/hover/definition/references/document symbols), though the project initialization notification timed out during the run.
- VS Code client harness runs successfully against the C# extension (`ms-dotnettools.csharp`) using a local VS Code installation, with hover/definition/completion/document symbols passing on the basic fixture.
- VB.NET smoke harness runs against the Phase 1 server scaffold, including text document lifecycle notifications; connection drop during shutdown is handled on the client side.
- Emacs harness connects to Roslyn LSP over stdio and to the VB.NET server over stdio; Roslyn shutdown times out but is treated as non-fatal in the harness.
- Full suite run (`run-tests.ps1 -Suite all`) completes: C# dotnet and node handshakes, VB.NET smoke (pipe), Emacs eglot checks.
- VB.NET diagnostics smoke test currently does not receive any `textDocument/publishDiagnostics` for the diagnostic fixture (failure noted in results).

Known issues / TODO for future agents:
- The C# harness uses StreamJsonRpc and named pipes; no logs are produced under `_test/codex-tests/csharp-lsp/logs` yet (likely due to server logging behavior or paths). Consider passing a writable, absolute log directory and verifying server log output.
- Project initialization completion did not arrive within the current timeout when using the fixture solution. Consider investigating the `workspace/projectInitializationComplete` notification timing or increasing the feature timeout.
- VS Code extension install can fail with EPERM rename during `code --install-extension`. Clearing `clients/vscode/.vscode-test/extensions` and rerunning resolved the issue.
- VB.NET diagnostics smoke test did not receive `textDocument/publishDiagnostics` after workspace load and didOpen/didChange (even after a retry). The harness now fails fast with a clear error; diagnostics publication likely needs investigation in the server.

How to continue quickly:
1) Build Roslyn LSP:  
   `dotnet build _external\roslyn\src\LanguageServer\Microsoft.CodeAnalysis.LanguageServer\Microsoft.CodeAnalysis.LanguageServer.csproj -c Release`
2) Run named-pipe smoke test with solution/open:  
   `dotnet run --project _test\codex-tests\csharp-lsp\CSharpLspSmokeTest\CSharpLspSmokeTest.csproj -- --serverPath _external\roslyn\artifacts\bin\Microsoft.CodeAnalysis.LanguageServer\Release\net10.0\Microsoft.CodeAnalysis.LanguageServer.dll --logDirectory _test\codex-tests\csharp-lsp\logs --rootPath . --transport pipe --solutionPath _external\roslyn\Roslyn.sln --protocolPath _external\vscode-csharp\src\lsptoolshost\server\roslynProtocol.ts`
3) Run fixture feature tests:  
   `dotnet run --project _test\codex-tests\csharp-lsp\CSharpLspSmokeTest\CSharpLspSmokeTest.csproj -- --serverPath _external\roslyn\artifacts\bin\Microsoft.CodeAnalysis.LanguageServer\Release\net10.0\Microsoft.CodeAnalysis.LanguageServer.dll --logDirectory _test\codex-tests\csharp-lsp\logs --rootPath . --transport pipe --solutionPath _test\codex-tests\csharp-lsp\fixtures\basic\Basic.sln --protocolPath _external\vscode-csharp\src\lsptoolshost\server\roslynProtocol.ts --testFile _test\codex-tests\csharp-lsp\fixtures\basic\Basic\Class1.cs --featureTests`

Local-only items (do not commit):
- `_external/roslyn` clone and build artifacts.
- Any build outputs under `_test/codex-tests/csharp-lsp/CSharpLspSmokeTest/bin` and `obj`.

Research notes:
- VS Code extension testing guidance reviewed (testing-extension docs), for the planned alternate client harness using `@vscode/test-electron`.
- Local experiment: `code --version` succeeds (VS Code 1.107.1 installed at `C:\Programs\Microsoft VS Code\bin\code.cmd`), so a VS Code CLI-based harness is feasible on this machine.
- Added a VS Code harness scaffold with `@vscode/test-electron` and a minimal test host extension; executed successfully against C# extension after installing into an isolated extensions directory.
