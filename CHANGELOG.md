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

## 0.1.8

### Added
- Initialization options now include feature toggles and workspace caps for diagnostics/completion/formatting/code actions/semantic tokens and project loading limits.

### Changed
- Explicit project paths now take precedence over ancestor solution discovery when configured.

### Fixed
- VS Code workspace initialization now honors configured project paths for loading.

## 0.1.7

### Added
- Advanced navigation: call hierarchy, type hierarchy, type definition, implementation.
- Document highlight, selection range, and document link support.
- Pull diagnostics (`textDocument/diagnostic`, `workspace/diagnostic`).
- CodeAction resolve support.

### Changed
- Hover and signature help documentation formatting (summary/params/returns).
- Completion sorting now respects Roslyn sortText with stable fallback.
- Formatting defaults now honor trim/EOF options when omitted.

### Fixed
- Diagnostic tags now surface `Unnecessary` and `Deprecated` where applicable.
- Rename prepare uses identifier spans for better range precision.
- Invalid server path override now falls back to bundled server.

## 0.1.6

### Added
- VS Code harness connection-health suite and deeper stdio stream tracing for diagnostics.
- Named-pipe fixture settings tracked for the services workspace.

### Changed
- Server version now reports 0.1.6 in LSP initialize responses.
- VSIX packaging now bundles the VB.NET server implementation by default.

### Fixed
- VB stdio transport now emits proper CRLF headers so VS Code can parse initialize responses.

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
