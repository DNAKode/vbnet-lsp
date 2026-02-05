Date: 2026-01-10
Author: Codex (GPT-5) acting as independent test reviewer
Scope: Comprehensive automated test suite plan for VB.NET Language Support (vbnet-lsp)
Alignment: Matches PROJECT_PLAN.md phases, docs/*, and the independent review recommendations

# Codex Test Suite Plan

This document defines a comprehensive, automated test suite for the VB.NET Language Support project. It is independent of current implementation details but maps to the planned architecture, phases, and features. The suite is designed to be incremental, allowing phased adoption while maintaining a coherent end-to-end quality strategy.

## Objectives and test philosophy

Primary objectives:
- Ensure performance targets, stability over time, and cross-platform compatibility.
- Provide automation-first coverage across protocol, workspace, and language feature layers.
- Enable incremental adoption without blocking development velocity.

Philosophy:
- Prefer contract-style tests that encode expected behavior over brittle UI tests.
- Validate protocol compliance and server robustness under failure and cancellation.
- Isolate layers where possible (unit and component tests), then confirm full integration with LSP and editor harnesses.
- Keep tests deterministic and non-interactive; all required inputs are provided by fixtures or scripted steps.

## Exploratory themes (test-explore)

Run via `test-explore/run-tests.ps1 -Theme <name>`:

| Theme | Purpose | Suites |
| --- | --- | --- |
| `core` | Fast LSP protocol/regression sanity | `vbnet-lsp` |
| `editors` | Client behavior validation | `emacs`, `vscode`, `nvim`, `helix` (manual) |
| `scale` | Large-solution robustness/perf | `dwsim`, `vscode-dwsim` |
| `all` | Full exploratory sweep | all of the above |

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
- Pure VB test projects using MSTest/xUnit/NUnit (match project standard).
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
- Commands (restart server, select solution, show output, restore, reload, attach, test runs).
- Configuration changes (debounce and diagnostics mode).
- Workspace trust behavior (limited activation if applicable).
- Test Explorer integration (controller creation + solution/project test items).
  - Note: `createTestObserver` is a proposed API; automated assertions should use a manual verification note or a dev build with proposals enabled.

Harness:
- VS Code extension test runner (`@vscode/test-electron`) with scripted test fixtures.
- Use non-interactive command execution and synthetic workspaces.

Additional validation matrix (production readiness focus):
- Transport selection (`vbnet.server.transportType`): verify stdio/namedPipe/auto still serve core requests.
- Server path override (`vbnet.server.path` + `VBNET_SERVER_PATH`): ensure override works and invalid path logs a warning then falls back to bundled server.
- Feature toggles: `vbnet.completion.enable` disables completion; `vbnet.diagnostics.enable` disables publishDiagnostics.
- Trace level (`vbnet.trace.server`): ensure verbose logging is enabled and trace output is captured for post-run analysis.
- Commands: `vbnet.restartServer` and `vbnet.showOutputChannel` are registered and functional.
- Workspace trust: untrusted workspace activates in limited mode; trusted workspace runs full features.
- Activation events: verify `onLanguage:vb` and `workspaceContains` (.vbproj/.sln) trigger activation.
- Error handling: missing runtime / server crash yields actionable user-facing errors; restart recovers.

### 5) Multi-editor protocol tests (Emacs, others)

Purpose: Validate LSP compliance beyond VS Code.

Targets:
- LSP request/response behavior under different clients.
- Feature coverage of MVP LSP actions.

Harness:
- Emacs lsp-mode batch testing in CI (Phase 1 or Phase 2 depending on scope).
- Optional future: Neovim LSP harness if adopted by users.
- Helix manual smoke checks with `test-explore/clients/helix` (stdio only; no headless harness yet).

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
- Use `_external/dwsim/DWSIM.sln` as primary workspace root.
- Start with a single file open (e.g., `DWSIM/ApplicationEvents.vb`) and smoke LSP lifecycle.
- Run a DWSIM-specific service manifest to probe hover/definition/references/symbols without modifying the DWSIM source.
- Add timing capture in the DWSIM harness and record results in `TEST_RESULTS.md`.
- Extend to diagnostics once publishDiagnostics is working in smaller fixtures.
- Track external restore requirements (NuGet packages) and record missing-package diagnostics as part of readiness gates.
- Capture timing milestones (server start, solution load, first didOpen) in the DWSIM harness and record them in `TEST_RESULTS.md`.

Scaffolding:
- `test-explore/dwsim/run-tests.ps1` invokes the VB.NET LSP smoke harness with a DWSIM root and service manifest.
- Optional VS Code headless open check using the VS Code harness with `FIXTURE_WORKSPACE=_external/dwsim`.
- Service tests (Phase 1): drive requests from `test-explore/vbnet-lsp/fixtures/services/service-tests.json` once services are implemented.

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
- call hierarchy and type hierarchy.
- type definition and implementation.
- document highlights, selection ranges, and document links.
- pull diagnostics (document/workspace).
- code lens.
- on-type formatting.
- performance tuning with large solutions.
- inlay hints (deferred pending stable Roslyn APIs).

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
- Protocol anomaly logging (JSONL) and automatic inclusion in `TEST_RESULTS.md` after each run.
- Service-level fixture manifest for Phase 1 services (completion, hover, definition, references, rename, symbols).
- Fixture projects and deterministic test data:
  - SmallProject and MediumProject in `test/TestProjects`.
  - DWSIM in `_external/dwsim` (cloned, not shipped).
  - Optional additional OSS VB.NET project(s) for feature correctness.
  - VB.NET fixtures for smoke testing text sync (`vbnet-lsp/fixtures/basic`).
  - VB.NET diagnostics fixture solution (`vbnet-lsp/fixtures/diagnostics`) with intentional compile errors.
  - VB.NET services fixture (`vbnet-lsp/fixtures/services`) with markers and a JSON manifest for service requests.
- Telemetry-free default operation; tests must disable any telemetry.
- CI workflows for Windows, Linux, macOS.

External dependencies:
- .NET SDK (targeted version).
- VS Code for extension tests.
- bundled netcoredbg (or `NETCOREDBG_PATH` override) for debugger tests (Phase 2).
- Emacs and lsp-mode for multi-editor tests (Phase 1 or Phase 2).

Alternate client strategy (Phase 1-2 planning):
- VS Code extension tests via `@vscode/test-electron` to run the VB.NET extension in a controlled, automated instance of VS Code. This gives realistic extension activation, workspace trust, and editor integrations. Source: https://code.visualstudio.com/api/working-with-extensions/testing-extension
- Proposed harness steps for VS Code:
  1) Use `@vscode/test-electron` to download a pinned VS Code build in CI.
  2) Launch with isolated directories (`--extensions-dir`, `--user-data-dir`, `--disable-extensions` except target).
  3) Install the extension under test (local VSIX or dev path).
  4) Open a fixture workspace, wait for activation, then use VS Code APIs to trigger hover/definition/completion and verify results.
- Emacs batch tests with ERT + lsp-mode (headless) to validate basic LSP compliance and non-UI flows. Use a minimal Emacs config plus lsp-mode setup in CI, open a workspace, wait for diagnostics, then execute hover/definition/completion requests through `lsp-request`.
- Keep the current LSP harness as the fast baseline (transport + protocol correctness) and treat VS Code/Emacs as integration tiers, not the primary gate.
- A minimal VS Code harness scaffold now exists under `test-explore/clients/vscode` to run smoke tests inside VS Code using `@vscode/test-electron`.
- A minimal Emacs harness now exists under `test-explore/clients/emacs` using built-in `eglot` for stdio-based tests.
- Future Emacs expansion: evaluate `lsp-mode` for richer client coverage once a non-interactive package install path is available; retain `eglot` as the zero-dependency baseline.
- Future Neovim expansion: mirror the `roslyn.nvim` approach with a VB.NET-backed client and a
  `test-explore/clients/nvim` harness once the local reference clone exists.
  - Status: initial Neovim harness scaffolded under `test-explore/clients/nvim` (headless smoke).
  - Optional reference runs: C# Roslyn server and VB.NET Roslyn server can be exercised via the Neovim harness for parity checks.
  - Current reference source: Mason custom registry `Crashdummyy/roslynLanguageServer` (version tracked in `TEST_RESULTS.md`).

Data capture:
- Standard test logs for LSP request/response.
- Performance metrics capture (startup time, diagnostics latency, completion latency).
- Memory usage snapshots during E2E runs.

## Execution plan by phase

Phase 0 (Bootstrap):
- Define LSP test harness scaffold.
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
Mitigation: Make harness transport-agnostic; run transport tests based on configured server mode. Validate both stdio and named pipes against the VB.NET server.

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

What is already implemented in `test-explore/vbnet-lsp`:
- VB.NET LSP smoke harness (`VbNetLspSmokeTest`) that performs initialize/initialized, text document lifecycle notifications, and shutdown/exit over named pipes or stdio.
- A VB.NET test runner (`vbnet-lsp/run-tests.ps1`) that builds the server, snapshots binaries, and runs the smoke test.
- A basic VB.NET fixture file used for didOpen/didChange/didSave/didClose coverage.
- A diagnostics fixture solution with a VB compile error, used to validate `textDocument/publishDiagnostics`.
- Diagnostics mode in the smoke harness that waits for `textDocument/publishDiagnostics` on the fixture file and retries didOpen/didChange once if none arrive.
- Diagnostics settings injection (via `workspace/configuration` and `workspace/didChangeConfiguration`) plus expected diagnostic code checks in the smoke harness.
- Diagnostics harness can optionally send `textDocument/didSave` for `openSave`/`saveOnly` mode validation.
- Service fixture scaffolding for Phase 1 services: `test-explore/vbnet-lsp/fixtures/services/` and `service-tests.json`.

Key paths and artifacts:
- VS Code harness scaffold: `test-explore/clients/vscode/`
- VB.NET smoke harness: `test-explore/vbnet-lsp/VbNetLspSmokeTest/`
- VB.NET fixtures: `test-explore/vbnet-lsp/fixtures/basic/`
- VB.NET diagnostics fixture: `test-explore/vbnet-lsp/fixtures/diagnostics/`
- VB.NET services fixtures: `test-explore/vbnet-lsp/fixtures/services/`
- VB.NET snapshots: `test-explore/vbnet-lsp/snapshots/`
- DWSIM harness: `test-explore/dwsim/`

Validated behavior:
- Feature tests against the fixture solution succeed (completion/hover/definition/references/document symbols), though the project initialization notification timed out during the run.
- VB.NET smoke harness runs against the Phase 1 server scaffold, including text document lifecycle notifications; connection drop during shutdown is handled on the client side.
- VB.NET diagnostics smoke test currently does not receive any `textDocument/publishDiagnostics` for the diagnostic fixture (failure noted in results).

Known issues / TODO for future agents:
- Project initialization completion did not arrive within the current timeout when using the fixture solution. Consider investigating the `workspace/projectInitializationComplete` notification timing or increasing the feature timeout.
- VS Code extension install can fail with EPERM rename during `code --install-extension`. Clearing `clients/vscode/.vscode-test/extensions` and rerunning resolved the issue.
- VB.NET diagnostics smoke test did not receive `textDocument/publishDiagnostics` after workspace load and didOpen/didChange (even after a retry). The harness now fails fast with a clear error; diagnostics publication likely needs investigation in the server.

How to continue quickly:
1) Run named-pipe smoke test with solution/open.  
2) Run fixture feature tests.  

Local-only items (do not commit):
None.

Research notes:
- VS Code extension testing guidance reviewed (testing-extension docs), for the planned alternate client harness using `@vscode/test-electron`.
- Local experiment: `code --version` succeeds (VS Code 1.107.1 installed at `C:\Programs\Microsoft VS Code\bin\code.cmd`), so a VS Code CLI-based harness is feasible on this machine.






