# VB.NET LSP Smoke Test

This folder contains a minimal smoke harness for the VB.NET language server scaffold.

## What this tests

- Named pipe (or stdio) transport startup.
- LSP lifecycle (`initialize`, `initialized`, `shutdown`, `exit`).
- Text document lifecycle notifications (`didOpen`, `didChange`, `didSave`, `didClose`) using a VB fixture file.

## Run the smoke test

```powershell
_test\codex-tests\vbnet-lsp\run-tests.ps1
```

Options:
- `-SkipSnapshot`: skip copying server binaries into `snapshots/`.
- `-SkipBuild`: skip `dotnet build` of the server.

## Notes

- The server currently closes the transport on `shutdown`, so the client treats connection loss during shutdown as a graceful exit.
- Snapshots are stored under `_test/codex-tests/vbnet-lsp/snapshots` and are intended for local debugging only.
