# Emacs LSP Client Harness (Planned + Initial Experiment)

This folder contains a headless Emacs-based LSP client harness. It is intended to validate LSP servers outside VS Code, using `eglot` (built-in in Emacs 29+).

## Goals

- Validate LSP initialization and basic requests using a non-VS Code editor.
- Provide a portable, CI-friendly client for protocol compliance tests.
- Exercise Roslyn C# LSP and the VB.NET LSP scaffold over stdio.

## Structure

- `run-tests.ps1`: bootstraps Emacs (portable zip), runs batch tests.
- `eglot-smoke.el`: Emacs batch script that connects to LSP servers and runs a few requests.
- `emacs/`: local portable Emacs download (not intended for commit).

## Usage (manual)

```powershell
# Download emacs and run both C# and VB.NET smoke tests
_test\codex-tests\clients\emacs\run-tests.ps1

# Run only the C# test
_test\codex-tests\clients\emacs\run-tests.ps1 -Suite csharp
```

Environment variables:
- `ROSLYN_LSP_DLL`: path to `Microsoft.CodeAnalysis.LanguageServer.dll`.
- `VBNET_LSP_DLL`: path to `VbNet.LanguageServer.dll`.

## Notes

- This harness uses `eglot` to avoid external package installs.
- The VB.NET server currently does not implement hover/completion handlers; the VB test only validates lifecycle and didOpen/didClose.
- The C# test uses the Roslyn LSP server in stdio mode (not the VS Code extension host).
- Emacs 29.4 portable is downloaded into `clients/emacs/emacs` when missing (do not commit).
- The Roslyn LSP server responds to stdio init under Emacs, but shutdown currently times out; this is treated as non-fatal in the harness.
- The VB.NET test uses `fundamental-mode` to avoid requiring extra VB major-mode packages.
