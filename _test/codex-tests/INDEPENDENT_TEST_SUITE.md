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
- Fixture projects and deterministic test data:
  - SmallProject and MediumProject in `test/TestProjects`.
  - DWSIM in `_test/dwsim` (cloned, not shipped).
  - Optional additional OSS VB.NET project(s) for feature correctness.
- Telemetry-free default operation; tests must disable any telemetry.
- CI workflows for Windows, Linux, macOS.

External dependencies:
- .NET SDK (targeted version).
- VS Code for extension tests.
- netcoredbg binary for debugger tests (Phase 2).
- Emacs and lsp-mode for multi-editor tests (Phase 1 or Phase 2).

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

## Risk areas and mitigations

Risk: Transport mismatch (stdio vs named pipes).
Mitigation: Make harness transport-agnostic; run transport tests based on configured server mode.

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
