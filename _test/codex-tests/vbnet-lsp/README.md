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

Run diagnostics smoke test (loads a VB solution and expects diagnostics):

```powershell
_test\codex-tests\vbnet-lsp\run-tests.ps1 -Diagnostics
```

Options:
- `-SkipSnapshot`: skip copying server binaries into `snapshots/`.
- `-SkipBuild`: skip `dotnet build` of the server.
- `-DiagnosticsMode`: diagnostics mode to request when running `-Diagnostics` (default `openChange`).
- `-DebounceMs`: diagnostics debounce override in milliseconds (default `300`).
- `-ExpectedDiagnosticCode`: diagnostic code expected in the diagnostics fixture (default `BC30311`).
- `-SendDidSave`: force `textDocument/didSave` notifications (auto-enabled for `openSave`/`saveOnly` diagnostics mode).

## Notes

- The server currently closes the transport on `shutdown`, so the client treats connection loss during shutdown as a graceful exit.
- Snapshots are stored under `_test/codex-tests/vbnet-lsp/snapshots` and are intended for local debugging only.
- Diagnostics tests rely on the fixture solution under `fixtures/diagnostics` and expect at least one diagnostic to be published.
