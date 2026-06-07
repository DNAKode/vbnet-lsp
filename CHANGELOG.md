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

## 0.1.11

### Added
- Workspace context status surfacing for solution mode, single-project mode, Workspace Dev Mode, ambiguous context selection, and empty workspaces.
- `VB.NET: Select Workspace Context` command for choosing Auto-detect, a solution, all discovered projects, or one `.vbproj` as the active language-server context.
- `vbnet.workspace.projectPaths` setting for explicit project-backed workspace context.
- VS Code harness coverage for workspace context reporting and explicit project context changes.

### Changed
- `VB.NET: Select Workspace Solution` now opens the unified workspace context picker for compatibility.
- The status bar now opens workspace context selection and shows the active solution/project context instead of only server state.
- Workspace context setting changes now restart the language client with a debounce so the loaded server context and status bar remain aligned.
- Restore and test commands now respect the selected workspace context when no active file gives a more specific project target.
- Multi-solution workspaces no longer silently select an arbitrary solution when no explicit context is configured.

### Fixed
- Linux/WSL debugger bundling now ensures `libdbgshim.so` is included with netcoredbg.

### Known issues
- Full VS Code LSP smoke coverage can still time out in broader service tests; the focused context harness passes with `SKIP_VBNET_SMOKE=1`.

## 0.1.10

### Added
- Roslyn-backed Extract Method code action support (`refactor.extract`) for project-backed VB.NET documents.
- Legacy non-SDK-style VB.NET project fallback loading, including .NET Framework reference assembly, COM reference, project reference, and NuGet package reference handling where resolvable.
- Zed adapter readiness gates, grammar support checks, and release validation scripts.

### Changed
- Code action resolution now distinguishes source option actions from extract refactor actions and honors LSP `CodeActionContext.only` filtering for `source`, `refactor`, and `refactor.extract`.
- Release and Marketplace packaging now include pre-publish artifact validation to catch cache directories and duplicate server payloads before publishing.

### Fixed
- Legacy/non-SDK-style project files now produce friendlier warnings and continue with best-effort workspace loading instead of silently dropping support.
- VSIX packaging now excludes transient debugger cache files and prevents nested `.server/publish` output from duplicating the bundled server payload.
- Zed grammar metadata now points to the public `DNAKode/tree-sitter-vbnet` source and rejects branch names when a release version pin is expected.

### Known issues
- Extract Method is intentionally Roslyn-only; unsupported documents or selections return no extract action rather than using a synthetic fallback.

## 0.1.9

### Added
- `vbnet.debugger.workarounds.stackTraceNoInterfaceFallback` setting (enabled by default) to keep debug sessions usable when netcoredbg returns `stackTrace` `0x80004002`.

### Changed
- VS Code debug adapter launch now uses a lightweight netcoredbg DAP proxy to apply a targeted fallback for `stackTrace` `E_NOINTERFACE` responses.

### Fixed
- WinForms/debugger flows no longer hard-fail stack expansion on the known upstream netcoredbg `0x80004002` bug path; the adapter now returns an empty stack as a temporary mitigation.

### Known issues
- Upstream netcoredbg tracking: issue https://github.com/Samsung/netcoredbg/issues/215, fix PR https://github.com/Samsung/netcoredbg/pull/216 (remove workaround after fix is merged and released in bundled binaries).

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
