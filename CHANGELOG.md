# Changelog

All notable changes to this project will be documented in this file.

## 0.1.1

- Fix completion commit behavior that duplicated keywords (e.g., `AAs`).
- Add deterministic keystroke completion test in VS Code harness.
- Bundle language server into the VSIX by default.
- Bundle Windows netcoredbg into the VSIX and activate debug sessions reliably.
- Ensure marketplace README is included and icon transparency is correct.

## 0.1.0-alpha

Initial alpha release with:
- Roslyn-backed VB.NET language server
- Core LSP features (diagnostics, completion, hover, definition, references, rename, symbols)
- Enhanced editing features (formatting, semantic tokens, signature help, folding ranges)
- Baseline code actions (Option Strict/Explicit/Infer)
- netcoredbg-based debugging integration
- VS Code extension packaging and harness coverage
