# Zed Client Exploration

This directory contains Zed-specific fixtures, probes, and smoke-test
scaffolding for the VB.NET extension.

The current public Zed docs describe installing a dev extension from the UI or
the `zed: install dev extension` action and checking `Zed.log`; they do not
document a stable headless extension-test command. The first automated layer
therefore focuses on static verification and reproducible manual/UI smoke steps.

## Static Verification

```powershell
scripts/verify-zed-extension.ps1
scripts/verify-zed-probes.ps1
scripts/verify-zed-real-server.ps1
scripts/verify-zed-tree-sitter.ps1
```

This validates the extension layout, checks the Zed manifest and language
metadata, verifies fixture/probe presence, runs `cargo check --target
wasm32-wasip1`, runs Rust unit tests, builds the LSP/DAP probe projects, and
exercises the probes with protocol-level requests. The Tree-sitter verifier
checks the fixture corpus plus Zed query files against the owned
`tree-sitter-vbnet` grammar project at the repository root.

The real-server protocol smoke starts the actual local `VbNet.LanguageServer`
over stdio, sends `initialize`, `initialized`, `textDocument/didOpen`, and
`shutdown`, and checks startup logs for crashes.

## Probe Fixtures

The workspace fixtures under `fixtures/` cover:

- `single-file`
- `vbproj`
- `sln`
- `slnf`
- `slnx`
- `mixed-vb-csharp`
- `debug-console`
- `tree-sitter`
- `probes/real-server-harness`

The `.zed/settings.json` files in the LSP fixtures point `vbnet-ls` at the
local LSP probe through `dotnet run`. The probe writes JSONL traffic to
`zed-lsp-probe.jsonl` in the opened workspace.

## Manual Zed Smoke

1. Build the language server:

   ```powershell
   dotnet build src/VbNet.LanguageServer.Vb/VbNet.LanguageServer.Vb.vbproj -c Debug
   ```

2. Install the dev extension from:

   ```text
   adapters/zed/vbnet-zed
   ```

3. Start Zed with foreground logging:

   ```powershell
   zed --foreground test/TestProjects/SmallProject
   ```

4. Configure `lsp.vbnet-ls.binary.path` to the local server executable.

5. Open `Module1.vb` and verify:

   - Zed assigns the language as `VB.NET`.
   - Zed starts `vbnet-ls`.
   - Hover, completion, diagnostics, definition, and symbols respond.
   - Opening `SmallProject.sln` and `SmallProject.slnx` does not start C#
     tooling for VB.NET files.

## Probe Smoke

Prepare an isolated Zed profile once, install the dev extension into that
profile, then close Zed:

```powershell
$profile = Join-Path $env:TEMP "vbnet-zed-profile"
test-explore/clients/zed/scripts/prepare-zed-profile.ps1 `
  -ZedPath C:\Programs\Zed\Zed.exe `
  -UserDataDir $profile
```

In that Zed window, run `zed: install dev extension` and select the printed
`adapters/zed/vbnet-zed` path. The helper verifies the selected profile lists
the `vbnet` extension after Zed exits.

After Zed closes, run the smoke against the same profile:

```powershell
test-explore/clients/zed/scripts/run-zed-smoke.ps1 `
  -ZedPath C:\Programs\Zed\Zed.exe `
  -UserDataDir $profile `
  -WorkspacePath test-explore/clients/zed/fixtures/single-file
```

The script starts Zed with `--foreground` and `--user-data-dir`, opens the
fixture, captures stdout/stderr under `logs/`, copies `Zed.log` files from the
isolated profile when present, and asserts the LSP probe saw `initialize` and
`textDocument/didOpen`. Close any existing Zed windows first; current Zed builds
reuse an already-running process instead of starting a second isolated profile.
A fresh profile without `extensions/index.json` is rejected early because
current public Zed docs do not document a non-interactive dev-extension install
command.

By default the script copies the selected fixture to a temporary workspace and
writes `.zed/settings.json` with absolute probe paths while preserving fixture
`workspace.solutionPath` or `workspace.projectPath` initialization options. Pass
`-UseFixtureSettings` or set `VBNET_ZED_USE_FIXTURE_SETTINGS=1` for the shell
script when you want to exercise the checked-in fixture settings exactly.
Temporary workspace copies are deleted after a successful run. Pass
`-KeepSmokeWorkspace` or set `VBNET_ZED_KEEP_SMOKE_WORKSPACE=1` when you need to
inspect the generated settings and logs after a pass.

## Real-Server Smoke

After the probe smoke passes, build the local language server and run the same
isolated Zed harness in real-server mode:

```powershell
dotnet build src/VbNet.LanguageServer.Vb/VbNet.LanguageServer.Vb.vbproj -c Debug

test-explore/clients/zed/scripts/run-zed-smoke.ps1 `
  -ZedPath C:\Programs\Zed\Zed.exe `
  -UserDataDir $profile `
  -WorkspacePath test/TestProjects/SmallProject `
  -Mode RealServer
```

Real-server mode writes generated `.zed/settings.json` that points `vbnet-ls`
at a temporary launcher for the local `VbNet.LanguageServer` with
`--stdio --logLevel Debug`, keeps the same isolated-profile checks, captures Zed
stdout/stderr plus copied `Zed.log` files, and requires the server's own stderr
log to contain the VB.NET startup banner. It is still a real-Zed gate: close
existing Zed windows and install the dev extension into the selected profile
first.

`scripts/run-zed-ui-smoke.ps1` runs the probe smoke first. UI assertions are
skipped by default because Zed does not currently document a stable headless
command path for hover/completion/debug UI actions; pass `-RequireUiAutomation`
when a local UI automation harness is configured and the absence of UI checks
should fail the run.

## Debug Smoke

The `debug-console` fixture includes `.zed/tasks.json` with reusable `dotnet`
build and run tasks, plus `.zed/debug.json` with:

- `Debug VB.NET console`: explicit `netcoredbg` launch with a `dotnet build`
  pre-task and `bin/Debug/net10.0/DebugConsole.dll`.
- `Attach VB.NET console`: attach shape with a placeholder `processId` for
  manual replacement.

Manual debug smoke:

1. Launch the prepared debug fixture:

   ```powershell
   test-explore/clients/zed/scripts/run-zed-debug-smoke.ps1 `
     -ZedPath C:\Programs\Zed\Zed.exe `
     -UserDataDir $profile
   ```

   On Unix-like systems:

   ```bash
   VBNET_ZED_USER_DATA_DIR="$profile" \
     test-explore/clients/zed/scripts/run-zed-debug-smoke.sh zed
   ```

   `-NetcoredbgPath` / `VBNET_ZED_NETCOREDBG_PATH` remain available for testing
   a specific debugger binary. Without an override, the Zed extension resolves
   repo-local `_external` binaries, then curated platform downloads, then `PATH`.

2. Run `debugger: start`.
3. Select `Debug VB.NET console`.
4. Verify Zed starts `netcoredbg`, runs the build task, launches the fixture,
   and writes `from-zed` to the debug console.
5. Run the `dotnet run DebugConsole` task and verify it writes `from-zed-task`.
6. Replace `processId` in `.zed/debug.json` with a running fixture process ID
   and verify attach reaches `netcoredbg`.
