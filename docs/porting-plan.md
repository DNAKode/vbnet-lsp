# `VB.NET` Porting Plan (C# -> VB.NET)

Version: 0.1
Last Updated: 2026-01-17
Status: In Progress

## Goals

- Convert the language server, tests, and harnesses from C# to `VB.NET`.
- Preserve behavior and UX parity with the current C# implementation.
- Maintain a parallel, cross-testable phase (C# + `VB.NET`) before decommissioning C#.
- Keep CI fast and reliable; avoid destabilizing exploratory harnesses.

## Scope

Included:
- `src/VbNet.LanguageServer` (C# implementation)
- `test/` (C# unit/integration tests)
- `test-explore/vbnet-lsp` (C# smoke harness)

Excluded:
- `src/extension` (TypeScript)
- `_external/` reference repositories

## High-Level Phases

### Phase 0 — Inventory + Baseline
- Identify all C# surface area and test entry points.
- Define the 4-way cross-test matrix.
- Add porting plan and porting notes documents.

### Phase 1 — Parallel Build Backbone
- Add VB.NET server project with identical namespaces/public types.
- Keep outputs isolated to avoid bin collisions.
- Add to solution file.

### Phase 2 — Test Harness Dual-Targeting
- Introduce MSBuild toggle for C# vs `VB.NET` server selection.
- Add VB.NET test projects mirroring C# tests.
- Port the smoke harness to `VB.NET` and make runner selectable.

### Phase 3 — Incremental Port (Core -> Services -> Tests)
- Port by layers: Protocol -> Core -> Workspace -> Services.
- Keep all tests green for both implementations after each slice.
- Document porting surprises in `docs/porting-notes.md`.

### Phase 4 — Cross-Validation
- Run the full 4-way test matrix.
- Run VS Code + Emacs exploratory harnesses.
- Record results in `test-explore/TEST_RESULTS.md`.

### Phase 5 — Release + Decommission C#
- Ship `VB.NET` implementation.
- Keep C# alongside for a stability window.
- Remove C# implementation/tests after parity confidence.

## 4-Way Cross-Test Matrix

1. C# tests -> C# server
2. C# tests -> `VB.NET` server
3. `VB.NET` tests -> C# server
4. `VB.NET` tests -> `VB.NET` server

## Exit Criteria (Phase 4)

- All CI-safe tests passing in all four directions.
- VS Code harness smoke + diagnostics + services pass against both servers.
- Emacs eglot harness passes (non-fatal shutdown timeouts noted).
- No unexplained behavioral differences between C# and `VB.NET` server.

## Notes

- Keep public `VB.NET` references wrapped in backticks.
- Update `test-explore/TEST_RESULTS.md` for every substantial exploratory run.
- Keep changes incremental and well documented.
