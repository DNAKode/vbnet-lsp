# Emacs LSP Client Harness (Planned + Initial Experiment)

This folder contains a headless Emacs-based LSP client harness. It is intended to validate LSP servers outside VS Code, using `eglot` (built-in in Emacs 29+).

## Goals

- Validate LSP initialization and basic requests using a non-VS Code editor.
- Provide a portable, CI-friendly client for protocol compliance tests.
- Exercise the VB.NET language server over stdio.

## Structure

- `run-tests.ps1`: bootstraps Emacs (portable zip), runs batch tests.
- `eglot-smoke.el`: Emacs batch script that connects to LSP servers and runs a few requests.
- `emacs/`: local portable Emacs download (not intended for commit).
- `logs/`: batch run logs from Emacs/eglot (timestamped).

## Usage (manual)

```powershell
# Download emacs and run the VB.NET smoke tests
test-explore\clients\emacs\run-tests.ps1
```

Environment variables:
- `VBNET_LSP_DLL`: path to `VbNet.LanguageServer.dll`.

## Notes

- This harness uses `eglot` to avoid external package installs.
- The VB.NET test validates lifecycle and a minimal set of requests via `eglot`.
- Emacs 29.4 portable is downloaded into `clients/emacs/emacs` when missing (do not commit).
- The VB.NET test uses `fundamental-mode` to avoid requiring extra VB major-mode packages.

