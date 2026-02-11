# Neovim LSP Client Harness (Planned + Initial Experiment)

This folder contains a headless Neovim-based LSP client harness. It validates the VB.NET language server outside VS Code using Neovim's built-in LSP client.

## Goals

- Validate LSP initialization and basic requests using Neovim.
- Provide a portable, CI-friendly client for protocol compliance tests.
- Exercise the VB.NET language server over stdio.

## Structure

- `run-tests.ps1`: bootstraps Neovim (portable zip), runs headless tests.
- `nvim-smoke.lua`: Neovim Lua script that opens a VB.NET file and runs LSP requests.
- `nvim/`: local portable Neovim download (not intended for commit).
- `logs/`: run logs (timestamped).

## Usage (manual)

```powershell
# Download Neovim and run the VB.NET smoke tests
test-explore\clients\nvim\run-tests.ps1
```

Optional C# reference run (requires Roslyn LSP):

```powershell
$env:ROSLYN_LS_DLL = "C:\\path\\to\\Microsoft.CodeAnalysis.LanguageServer.dll"
test-explore\clients\nvim\run-tests.ps1 -Suite csharp
```

Optional VB.NET reference run against Roslyn LSP (requires Roslyn LSP):

```powershell
$env:ROSLYN_LS_DLL = "C:\\path\\to\\Microsoft.CodeAnalysis.LanguageServer.dll"
test-explore\clients\nvim\run-tests.ps1 -Suite roslyn-vb
```

Environment variables:
- `VBNET_LSP_DLL`: path to `VbNet.LanguageServer.dll`.
- `VBNET_LSP_WORKSPACE`: workspace root (defaults to `test\TestProjects\SmallProject`).
- `VBNET_LSP_FILE`: file to open (defaults to `test\TestProjects\SmallProject\Module1.vb`).
- `ROSLYN_LS_CMD`: path to the Roslyn language server executable (optional C# suite).
- `ROSLYN_LS_DLL`: path to `Microsoft.CodeAnalysis.LanguageServer.dll` (optional C# suite).
- `ROSLYN_LS_LOG_DIR`: directory for Roslyn logs (optional C# suite).
- `ROSLYN_LS_LOG_LEVEL`: log verbosity for Roslyn (`Trace`, `Information`, etc.).
- `ROSLYN_LS_EXTENSIONS`: semicolon-separated list of extension assemblies to pass via `--extension` (optional).
- `ROSLYN_LSP_SOLUTION`: override solution path for Roslyn `solution/open` (optional).
- `ROSLYN_LSP_PROJECT`: override project path for Roslyn `project/open` (optional, VB suite).

## Notes

- This harness runs with `-u NONE` for a clean config.
- It uses Neovim's built-in LSP client to start the VB.NET server over stdio.
- To run the optional C# suite, set `ROSLYN_LS_CMD` or `ROSLYN_LS_DLL` and pass `-Suite csharp` or `-Suite all` to `run-tests.ps1`.

## Coverage vs roslyn.nvim

The harness is a **smoke test** (protocol health + a handful of requests), not a full editor UX layer.
Compared to `roslyn.nvim`, it does **not** cover:

- Solution target selection UX
- Source-generated document buffers
- Enhanced code action orchestration

We keep the harness small on purpose; plugin UX work belongs in a dedicated Neovim plugin.

## Adapter Package Snapshot

The publishable Neovim adapter snapshot now lives at:

- `adapters/nvim/vbnet-lsp.nvim`

The harness here remains focused on smoke coverage and does not replace a full
plugin UX package lifecycle.

## Source Generators (VB status)

VB source generators appear unsupported in the Roslyn compiler (no Visual Basic compiler
generator driver usage in source). The harness does not attempt source-generated document
requests for VB and we consider SG support out of scope until Roslyn adds it.
