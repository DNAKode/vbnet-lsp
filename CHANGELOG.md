# Changelog

All notable changes to this project will be documented in this file.

## Unreleased

### Added
- _None yet._

### Changed
- _None yet._

### Fixed
- _None yet._

### Known issues
- _None yet._

## 0.1.5

### Added
- Platform-targeted VSIX packaging scripts for Windows/Linux/macOS.
- Configurable netcoredbg bundling via `NETCOREDBG_PATH`/`NETCOREDBG_LICENSE`.
- Curated netcoredbg asset manifest (`src/extension/scripts/netcoredbg-assets.json`) and cache.
- Bundled netcoredbg for all platform VSIX targets (macOS arm64 ships the x64 binary under Rosetta).
- Bundled debugger license from `third_party/netcoredbg/LICENSE`.
- Preview tag `v0.1.1-preview.20260116` published via GitHub Actions.
- 0.1.3 preview published after Marketplace verification retry.

### Changed
- Non-Windows netcoredbg bundles now ensure the debugger is marked executable.
- VSIX publish workflows now preserve netcoredbg asset filenames and use Node 20.
- Package/publish workflows now use curated netcoredbg assets without URL inputs.
- `bundle-debugger` downloads curated assets when `NETCOREDBG_PATH` is not set.

### Fixed
- Signature help now returns multiple overloads after `(` in completion scenarios.
- Breakpoint toggling now works via `F9`/Run -> Toggle Breakpoint in the extension.

## 0.1.1

- Fix completion commit behavior that duplicated keywords (e.g., `AAs`).
- Add deterministic keystroke completion test in VS Code harness.
- Bundle language server into the VSIX by default.
- Bundle Windows netcoredbg into the VSIX and activate debug sessions reliably.
- Ensure marketplace README is included and icon transparency is correct.

**Known issues**
- Debugger is only bundled for Windows in this release; macOS/Linux still require external netcoredbg.

## 0.1.0-alpha

Initial alpha release with:
- Roslyn-backed `VB.NET` language server
- Core LSP features (diagnostics, completion, hover, definition, references, rename, symbols)
- Enhanced editing features (formatting, semantic tokens, signature help, folding ranges)
- Baseline code actions (Option Strict/Explicit/Infer)
- netcoredbg-based debugging integration
- VS Code extension packaging and harness coverage
