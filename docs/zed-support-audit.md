# Zed Support Completion Audit

Last updated: 2026-05-01

Objective: implement full Zed support according to `docs/zed-support-plan.md`
and stop only after the plan is worked through with all aspects in place and
tested as specified.

## Success Criteria

- Zed extension metadata, language configuration, Rust extension code, README,
  license, and mirroring notice exist under `adapters/zed/vbnet-zed`.
- `.vb` files are detected as `VB.NET` and no C# language server or alias is
  registered by this extension.
- Tree-sitter highlighting, outline, folding, indentation, bracket matching,
  and text objects are implemented and validated against a VB.NET fixture.
- `vbnet-ls` starts from explicit Zed settings, `PATH`, or the pinned
  `DNAKode/vbnet-lsp` release asset for the extension version.
- Workspace settings support `.sln`, `.slnf`, `.slnx`, `.vbproj`,
  single-file, and mixed VB.NET/C# fixtures.
- `netcoredbg` DAP metadata, schema, explicit launch support, and initial debug
  target inference are implemented.
- Zed-specific fixture, probe, and smoke-test assets exist under
  `test-explore/clients/zed`.
- CI validates static layout, manifest values, Rust checks, probe harness,
  Tree-sitter parser/query checks, semantic token rules, export behavior, and
  release pinning.
- `DNAKode/vbnet-zed` is updated from the canonical adapter snapshot.
- `DNAKode/tree-sitter-vbnet` is updated from the canonical
  `tree-sitter-vbnet` grammar snapshot.
- Real Zed-in-loop smoke tests prove that a local dev extension loads in an
  isolated profile and that Zed sends LSP traffic for a `.vb` document.
- Real-server and debugger smoke gates are run before publishing or treating the
  extension as release-proven.
- The pinned GitHub release exists publicly and contains the four server
  archives expected by the Zed download fallback.

## Plan-To-Artifact Checklist

| Plan requirement | Required artifact or gate | Current evidence | Status |
| --- | --- | --- | --- |
| Re-check current Zed docs and maintained examples before implementation | Zed docs/API note in audit and extension code aligned to current manifest/API shape | Current-docs row below records the 2026-05-01 check; manifest uses Zed language-server and debug-adapter registrations and Rust implements the required extension methods. | Done |
| Use stable names and IDs | `extension.toml`, Rust IDs, debug schema, README | `vbnet`, `VB.NET`, `vbnet`, `vbnet-ls`, `netcoredbg`, and `vbnet` debug locator are checked by `scripts/verify-zed-extension.ps1`. | Done |
| Add canonical adapter snapshot under `adapters/zed/vbnet-zed` | Manifest, Cargo files, Rust sources, language config, query files, debug schema, README, license, mirroring notice | Required files are present and checked by `scripts/verify-zed-extension.ps1`; downstream export-content CI checks all expected files. | Done |
| Extend adapter export support for Zed and Tree-sitter | `adapters/scripts/export-adapter-repos.ps1` with `-ZedRepoPath`, `-TreeSitterRepoPath`, `-Clean`, and `-DryRun` | Export can mirror both `adapters/zed/vbnet-zed` and the canonical root `tree-sitter-vbnet` grammar; CI export dry-run/content checks are wired in `.github/workflows/editor-adapters.yml`. | Done |
| Add Zed verification script | `scripts/verify-zed-extension.ps1` | Verifier checks required files, manifest/config values, query files, semantic token rules, release workflow artifacts, Rust build/tests, probes, fixture builds, and debug schema structure. | Done |
| Add Zed release/readiness runner | `scripts/verify-zed-readiness.ps1` | Runner executes static verification, protocol probes, real-server protocol smoke, Tree-sitter validation, verifies the adapter version pin when `-Version` is supplied, and can opt into release-asset, live-Zed, real-server Zed, and debugger Zed smoke gates. | Done |
| Add local dev-extension guide and smoke scripts | `test-explore/clients/zed/README.md`, `run-zed-smoke.ps1`, `run-zed-smoke.sh`, UI wrappers | README documents isolated profile setup and `zed: install dev extension`; scripts capture Zed stdout/stderr, profile logs, probe JSONL, and fail on startup errors. | Done |
| Add isolated profile preparation helper | `prepare-zed-profile.ps1`, `prepare-zed-profile.sh` | Helpers launch Zed with `--user-data-dir`, instruct the user to run `zed: install dev extension`, and verify the isolated profile lists `vbnet` after Zed exits. | Done |
| Prove dev extension installs and `.vb` opens as `VB.NET` in real Zed | Real Zed smoke using isolated `--user-data-dir` profile with dev extension installed | Harness exists, but current machine has active Zed processes and the selected profile still needs dev-extension installation through Zed UI/action. | Blocked |
| Add server discovery and launch via configured path, `PATH`, and release download | `src/server.rs`, `src/platform.rs`, README settings | Rust implementation and tests cover configured binary, PATH merge behavior, release install directory naming, platform asset mapping, and unsupported targets. | Done |
| Prove public release-download fallback | `scripts/verify-zed-release-assets.ps1 -Version 0.1.9` and a public GitHub release with expected archives | Script exists and currently returns GitHub 404 for `DNAKode/vbnet-lsp` tag `v0.1.9`; release-download path is implemented but not release-proven. | Blocked |
| Validate core LSP protocol against probe and real local server | LSP probe, probe harness, real-server harness | `scripts/verify-zed-probes.ps1` and `scripts/verify-zed-real-server.ps1` pass; real server receives initialize/didOpen/shutdown over stdio outside Zed. | Done |
| Validate core LSP features inside real Zed | Real Zed probe and real-server smoke logs/probe JSONL | Blocked by active Zed processes and prepared-profile requirement. UI feature checks remain manual/optional until Zed exposes stable automation. | Blocked |
| Support `.sln`, `.slnf`, `.slnx`, `.vbproj`, single-file, and mixed VB/C# fixtures | Fixture directories and `.zed/settings.json`; workspace heuristics in `src/workspace.rs` | Fixture inventory is checked by verifier; `.slnf` and debug-console fixtures build; README documents mixed VB/C# behavior. | Done |
| Ensure mixed VB/C# does not register C# ownership | Manifest and README | Verifier checks no C# registration; manifest scopes server to `VB.NET`. | Done |
| Add Tree-sitter grammar decision and query coverage | `docs/zed-tree-sitter-grammar.md`, owned grammar files, query files, corpus fixtures, verifier | `scripts/verify-zed-tree-sitter.ps1` passes across five fixture files and validates parser/query files against the project-owned root `tree-sitter-vbnet` grammar. Known grammar gaps are documented. | Done |
| Add debug adapter metadata and explicit `netcoredbg` launch support | `[debug_adapters.netcoredbg]`, schema, `src/debug.rs`, debug fixture | Verifier checks schema structure and debug fixture/tasks; Rust tests cover request kind, config handling, build-output inference, and missing-build failure. | Done |
| Prove debugger behavior inside real Zed | DAP probe smoke and real `netcoredbg` smoke from Zed UI/debugger | Scripts, fixture, and documentation exist, but actual Zed UI/debugger smoke is blocked until the real Zed isolated-profile gate can run. | Blocked |
| Wire CI checks | `.github/workflows/editor-adapters.yml` | CI covers static Zed verification, probes, real-server protocol smoke, Tree-sitter, release pin, export dry-run/content, and downstream cargo check. | Done |
| Mirror to `vbnet-zed/generated/dev` | Downstream repo `C:\Work\vbnet-zed` | Latest canonical adapter snapshot can be exported to `C:\Work\vbnet-zed`; the grammar is now mirrored separately from `tree-sitter-vbnet`. | Done |
| Mirror to `tree-sitter-vbnet/generated/dev` | Downstream repo `DNAKode/tree-sitter-vbnet` | Planned through `-TreeSitterRepoPath`; the canonical grammar source remains `DNAKode/vbnet-lsp/tree-sitter-vbnet`. | Ready |
| Mirror tagged release to `vbnet-zed/main` and `tree-sitter-vbnet/main` | Public server release artifacts, exact tag export, downstream main/tag, grammar `rev` pin | Not attempted because `v0.1.9` release artifacts are not public and live Zed smoke gates have not passed. | Blocked |

## Evidence

| Requirement | Evidence | Status |
| --- | --- | --- |
| Adapter scaffold and manifest | `adapters/zed/vbnet-zed/extension.toml`, `Cargo.toml`, `.gitignore`, `src/*.rs`, `README.md`, `LICENSE`, `MIRRORING.md`; checked by `scripts/verify-zed-extension.ps1`. | Done |
| Stable names and IDs | Manifest id `vbnet`, name `VB.NET`, language server `vbnet-ls`, debug adapter `netcoredbg`; checked by verifier. | Done |
| No C# registration | Manifest registers only `VB.NET`; README documents mixed VB.NET/C# behavior; checked by verifier. | Done |
| Tree-sitter queries | Query files under `languages/vbnet/*.scm`; validated by `scripts/verify-zed-tree-sitter.ps1` against the five-file corpus in `test-explore/clients/zed/fixtures/tree-sitter`. | Done |
| Grammar decision note | `docs/zed-tree-sitter-grammar.md`. | Done |
| Server discovery and pinned release | `src/server.rs` implements configured binary, PATH lookup, GitHub release lookup, download/extract, and `--stdio`; checked by verifier and Rust tests. | Done |
| Platform release assets | `src/platform.rs` maps win-x64, linux-x64, osx-x64, and osx-arm64 release artifacts; `scripts/verify-zed-extension.ps1` also checks `.github/workflows/release.yml` publishes matching language-server archives. | Done |
| Published release assets | `scripts/verify-zed-release-assets.ps1` checks the pinned GitHub release contains all four Zed server archives. `v0.1.9` currently reports `release not found`, so release-download behavior is implemented but not release-proven. | Blocked |
| Workspace fixtures | `test-explore/clients/zed/fixtures/{single-file,vbproj,sln,slnf,slnx,mixed-vb-csharp}` with `.zed/settings.json`; checked by verifier. | Done |
| Debug adapter support | `src/debug.rs`, `src/lib.rs`, `debug_adapter_schemas/netcoredbg.json`, `test-explore/clients/zed/fixtures/debug-console/.zed/{debug,tasks}.json`, and `run-zed-debug-smoke.{ps1,sh}`; checked by verifier with structural schema validation, 13 Rust tests including debug locator built-DLL inference and missing-build failure behavior, debug-console fixture build, and explicit manual debug-smoke launch scripts with PATH or explicit netcoredbg path checks. | Done |
| Probe harness | `test-explore/clients/zed/probes/{lsp-probe,dap-probe,probe-harness,real-server-harness}`, `scripts/verify-zed-probes.ps1`, and `scripts/verify-zed-real-server.ps1`; protocol harness covers LSP probe, DAP launch/attach sessions, and actual local `VbNet.LanguageServer` stdio initialize/didOpen/shutdown smoke. | Done |
| CI | `.github/workflows/editor-adapters.yml` runs `scripts/verify-zed-readiness.ps1` for the required non-interactive Zed gates, plus export, release pin, and cargo checks. Export checks cover manifest, Cargo metadata, license, all Rust sources, debug schema, language config, all query files, semantic token rules, README, and mirroring notice. | Done |
| Documentation | `adapters/zed/vbnet-zed/README.md`, `test-explore/clients/zed/README.md`, `docs/editor-packaging.md`, and `docs/adapter-release-checklist.md`. | Done |
| Current Zed docs/API check | Re-checked Zed language-extension, debugger-extension, developing-extension, and installing-extension docs on 2026-05-01; current docs still require `languages/*/config.toml`, language-server manifest registration, Rust `language_server_command`, DAP methods, debug locators, and dev-extension installation through `zed: install dev extension`. | Done |
| Downstream mirror | `DNAKode/vbnet-zed` is generated from `adapters/zed/vbnet-zed`; `DNAKode/tree-sitter-vbnet` is generated from the root `tree-sitter-vbnet` grammar. Both mirrors are distribution output; authoritative changes stay in `DNAKode/vbnet-lsp`. | Done |
| Real Zed probe smoke | `test-explore/clients/zed/scripts/run-zed-smoke.ps1` can launch Zed with `--foreground` and `--user-data-dir`, verifies the selected profile contains the `vbnet` extension, copies `Zed.log` files from the isolated profile, and checks probe JSONL for `initialize` plus `textDocument/didOpen`. Current machine has existing Zed processes that prevent isolated launch, and the profile must first be prepared through Zed's documented dev-extension UI/action. | Blocked |
| Real Zed server smoke | `test-explore/clients/zed/scripts/run-zed-smoke.ps1 -Mode RealServer` and `run-zed-smoke.sh` generate settings for a local `VbNet.LanguageServer` launcher using `--stdio --logLevel Debug`, capture the server's own stderr, require the VB.NET startup banner, copy `Zed.log` files, and fail on known startup/error log patterns across stdout/stderr and copied Zed logs. Execution is blocked by the same running Zed processes and prepared-profile requirement as the probe smoke. | Blocked |
| Real Zed UI/debug smoke | DAP probe, debug fixture, and `run-zed-debug-smoke.{ps1,sh}` exist, but UI/debug automation remains gated on a successful isolated Zed launch and stable command/UI automation. | Blocked |

## Latest Local Verification

- `scripts/verify-zed-extension.ps1`: passed.
- `scripts/verify-zed-probes.ps1`: passed on 2026-05-01.
- `scripts/verify-zed-real-server.ps1`: passed on 2026-05-01.
- `scripts/verify-zed-tree-sitter.ps1`: passed across 5 fixture files using the owned `tree-sitter-vbnet` grammar.
- `scripts/verify-zed-readiness.ps1 -Version 0.1.9`: passed the selected
  non-interactive gates on 2026-05-01, including adapter version-pin
  verification, static verification, protocol probes, real-server protocol
  smoke, and Tree-sitter parser/query validation. Release and live-Zed gates
  were intentionally skipped by default and remain listed below as blockers;
  the skipped live gates now include probe, real-server, and debugger Zed smoke.
- `test-explore/clients/zed/scripts/run-zed-debug-smoke.ps1`: PowerShell
  syntax check passed on 2026-05-01 after adding explicit `-NetcoredbgPath`
  support.
- `test-explore/clients/zed/scripts/run-zed-debug-smoke.ps1 -ZedPath C:\Programs\Zed\Zed.exe -SkipExtensionInstallCheck -SkipNetcoredbgCheck`:
  built the debug-console fixture on 2026-05-01, then blocked because Zed
  processes `30076` and `34084` were already running from `C:\Programs\Zed\Zed.exe`.
- `test-explore/clients/zed/scripts/run-zed-debug-smoke.sh`: `bash -n` passed
  on 2026-05-01 after adding `VBNET_ZED_NETCOREDBG_PATH` support; WSL printed
  a non-fatal systemd user-session warning.
- `test-explore/clients/zed/scripts/prepare-zed-profile.ps1`: PowerShell
  syntax check passed on 2026-05-01.
- `test-explore/clients/zed/scripts/prepare-zed-profile.ps1 -ZedPath C:\Programs\Zed\Zed.exe`:
  blocked on 2026-05-01 because Zed processes `30076` and `34084` were already
  running from `C:\Programs\Zed\Zed.exe`.
- `test-explore/clients/zed/scripts/prepare-zed-profile.sh`: `bash -n` passed
  on 2026-05-01; WSL printed a non-fatal systemd user-session warning.
- `Get-Command netcoredbg`: did not find `netcoredbg` on PATH on 2026-05-01,
  so the live debugger smoke also requires installing/configuring netcoredbg.
- Canonical Zed adapter snapshot matched `C:\Work\vbnet-zed` by SHA-256 file
  comparison on 2026-05-01, excluding `.git` and `target`.
- `scripts/verify-zed-release-assets.ps1`: blocked because `DNAKode/vbnet-lsp`
  does not currently have a `v0.1.9` GitHub release; rechecked on 2026-05-01
  and GitHub returned 404 for `DNAKode/vbnet-lsp` tag `v0.1.9`.
- Expanded local Zed export-content check matching `.github/workflows/editor-adapters.yml`: passed.
- `cargo check --target wasm32-wasip1` in `adapters/zed/vbnet-zed`: passed.
- `cargo check --target wasm32-wasip1` in `C:\Work\vbnet-zed`: passed on 2026-05-01.
- `cargo test` in `C:\Work\vbnet-zed`: passed, 13 tests.
- `test-explore/clients/zed/fixtures/slnf/ZedSlnfFixture.slnf`: builds with
  `dotnet build -c Release` and is enforced by `scripts/verify-zed-extension.ps1`.
- `test-explore/clients/zed/fixtures/debug-console`: builds and produces `bin/Debug/net10.0/DebugConsole.dll`.
- `test-explore/clients/zed/scripts/run-zed-smoke.ps1 -ZedPath C:\Programs\Zed\Zed.exe -SkipExtensionInstallCheck`: blocked because Zed processes `30076` and `34084` are already running from `C:\Programs\Zed\Zed.exe`, so the script cannot start an isolated `--user-data-dir` profile; rechecked on 2026-05-01 with the smoke harness itself.
- `test-explore/clients/zed/scripts/run-zed-smoke.ps1 -Mode RealServer -ZedPath C:\Programs\Zed\Zed.exe -SkipExtensionInstallCheck`: blocked by the same running Zed processes before launch.
- Default isolated profile `C:\Users\GovertvanDrimmelen\AppData\Local\Temp\vbnet-zed-profile` currently lists only the built-in HTML extension, so it has not yet been prepared with the VB.NET dev extension.

## Remaining Gate

Close all existing Zed windows, create or choose a test Zed profile, launch Zed
with that profile, install the dev extension through `zed: install dev
extension`, install or configure `netcoredbg` for debug smoke, close all Zed
windows, then run:

```powershell
$profile = Join-Path $env:TEMP "vbnet-zed-profile"
powershell -NoProfile -ExecutionPolicy Bypass -File test-explore\clients\zed\scripts\run-zed-smoke.ps1 `
  -ZedPath C:\Programs\Zed\Zed.exe `
  -UserDataDir $profile `
  -WorkspacePath test-explore\clients\zed\fixtures\single-file
```

After the probe smoke passes, run the real-server and debugger smoke checklist
from `docs/zed-support-plan.md` before publishing or mirroring a tagged release
to `vbnet-zed/main`.
