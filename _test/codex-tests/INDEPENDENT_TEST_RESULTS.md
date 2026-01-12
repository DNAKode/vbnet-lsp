Date: 2026-01-11
Author: Codex (GPT-5) acting as independent test reviewer
Scope: Fresh independent test run for VB.NET LSP + VS Code extension
Host: Windows (C:\Work\vbnet-lsp)

# Independent Test Results

## High-level status

- VB.NET LSP service tests pass end-to-end with token-aware positions and expanded coverage (additional completion/hover/definition/references + multi-file rename).
- VS Code extension integration tests now cover settings/commands; document symbols still returned empty, but completion-disable now behaves as expected after suppressing word-based suggestions in the headless run.
- Diagnostics automation still fails to receive publishDiagnostics for the diagnostics fixture.
- VS Code automation requires elevated permissions in this environment to launch Code.exe.

## Test runs and outcomes

### 1) VB.NET LSP service tests (standalone harness)

Command pattern:
- `dotnet _test\codex-tests\vbnet-lsp\VbNetLspSmokeTest\bin\Debug\net10.0\VbNetLspSmokeTest.dll --serverPath src\VbNet.LanguageServer\bin\Debug\net10.0\VbNet.LanguageServer.dll --dotnetPath dotnet --logLevel Trace --transport stdio --rootPath _test\codex-tests\vbnet-lsp\fixtures\services --timeoutSeconds 60 --serviceManifest _test\codex-tests\vbnet-lsp\fixtures\services\service-tests.json --serviceTestId <id> --serviceTimeoutSeconds 45 --serviceLog _test\codex-tests\logs\service-tests-20260111-182639.jsonl --protocolLog _test\codex-tests\logs\protocol-anomalies-20260111-182639.jsonl --timingLog _test\codex-tests\logs\timing-20260111-182639.jsonl`

Expanded coverage (additional tests added):
- completion_calc (calc.Add)
- hover_extratype (ExtraType)
- definition_greeter
- references_greeter_class
- references_title

Expanded test validation:
- completion_calc: PASS (`_test/codex-tests/logs/service-tests-20260111-202008.jsonl`)

Baseline results (from full run before expansion):
- completion_text: PASS (117 items returned)
- completion_extension: PASS (DoubleIt present)
- hover_text: PASS
- definition_add: PASS
- references_greet: PASS (5 references)
- rename_sum: PASS (1 file)
- rename_greeter: PASS (2 files)
- symbols_document: PASS (6 symbols)
- symbols_workspace: PASS (4 symbols)

Notes:
- Multi-file rename coverage added via `GreeterConsumer.vb` and `rename_greeter`.
- Additional references coverage added via `ExtraConsumer.vb` and `references_title`.

Logs:
- `_test/codex-tests/logs/service-tests-20260111-182639.jsonl`
- `_test/codex-tests/logs/service-tests-20260111-202008.jsonl`
- `_test/codex-tests/logs/protocol-anomalies-20260111-182639.jsonl`
- `_test/codex-tests/logs/timing-20260111-182639.jsonl`

### 2) VS Code extension integration tests (headless)

Harness: `_test/codex-tests/clients/vscode` using `@vscode/test-electron` with the local VSIX.

Key configuration used:
- Extension VSIX: `src/extension/vbnet-language-support.vsix`
- Extension id: `dnakode.vbnet-language-support`
- Server path via env: `VBNET_SERVER_PATH=src\VbNet.LanguageServer\bin\Debug\net10.0\VbNet.LanguageServer.dll`
- Fixture workspace: `_test/codex-tests/vbnet-lsp/fixtures/services`
- Fixture file: `_test/codex-tests/vbnet-lsp/fixtures/services/ServiceSamples.vb`
- Workspace settings: `_test/codex-tests/vbnet-lsp/fixtures/services/.vscode/settings.json` (stdio + verbose trace)
- C# harness tests skipped via `SKIP_CSHARP_TESTS=1`.
- Log capture enabled via `CAPTURE_VSCODE_LOGS=1` and `CAPTURE_VBNET_TRACE=1`.

Outcome (headless run): FAIL
- PASS: extension installed and activated
- FAIL: document symbols returned empty in core services test
- PASS: restart command applied settings and hover still works
- FAIL: completion.disable setting ignored (completions still returned)

Log paths:
- Copied log bundle: `_test/codex-tests/clients/vscode/logs/20260111T214206`
- Trace summary: `_test/codex-tests/clients/vscode/logs/20260111T214206/vbnet-output-summary.txt`

Notes:
- Trace export did not find a `VB.NET` output log file; summary reports that no trace log was found in `output_logging` folders.

### 2b) VS Code extension integration tests (headless, after harness updates)

Harness changes:
- Default VS Code workspace now points to the VB.NET fixtures when `EXTENSION_ID` is `dnakode.vbnet-language-support` and skips C# tests.
- Completion toggle test now disables word-based suggestions to avoid non-LSP completion noise.

Outcome (headless run): PASS with warning
- PASS: extension installed and activated
- PASS: core services (hover/definition/references/completion) and rename
- PASS: restart command applies settings and hover works
- PASS: completion.disable respected (no completions)
- WARN: document symbols still returned empty (test logs warning and continues)
- SKIP: C# harness tests (SKIP_CSHARP_TESTS=1)

Log paths:
- Copied log bundle: `_test/codex-tests/clients/vscode/logs/20260111T220738`
- Trace summary: `_test/codex-tests/clients/vscode/logs/20260111T220738/vbnet-output-summary.txt`

### 3) Diagnostics automation (standalone harness)

Command:
- `_test\codex-tests\vbnet-lsp\run-tests.ps1 -Diagnostics -Transport stdio`

Outcome: FAIL
- Diagnostics not received after retries; `publishDiagnostics` never arrived.
- Build step failed with `CreateAppHost` access denied on `apphost.exe` before the diagnostics run. The harness proceeded using existing outputs, but the diagnostics still did not publish.

## Current issues / risks

1) VS Code document symbols returned empty in the headless extension run (integration gap or timing issue).
2) `vbnet.completion.enable` now passes in headless runs after disabling word-based suggestions, but the underlying LSP toggle behavior still warrants verification outside the harness.
3) Diagnostics publish path is still failing for the diagnostics fixture (no `publishDiagnostics` after retry).
4) VS Code automation requires elevated permissions to launch Code.exe in this environment.
5) Trace export from VS Code does not currently capture the VB.NET LSP trace channel; only host logs are present.
6) Build occasionally fails with `apphost.exe` access denied in `src/VbNet.LanguageServer\obj` (file lock or permission issue).

## Overall assessment

- Core VB.NET services are working in the standalone LSP harness and most VS Code interactions, but VS Code document symbols are still empty in headless runs and the completion toggle needs validation outside the word-based-suggestions workaround.
- Diagnostics remain the main functional gap for independent verification.

## Suggested follow-ups

1) Investigate why document symbols are empty in VS Code headless runs; compare with LSP harness behavior and verify server readiness timing.
2) Implement config handling for `vbnet.completion.enable` (client-side gating or server initialization option) and re-test.
3) Investigate why `publishDiagnostics` is not emitted for the diagnostics fixture; compare with successful service runs to check project load and diagnostics triggers.
4) Add a small hook or logging mechanism to explicitly export the VB.NET trace channel (if the extension writes to a log file, confirm the filename and location).
5) Resolve the intermittent `apphost.exe` access denied issue by ensuring no running server locks the build output before diagnostics runs.

## Protocol anomalies (latest run)
Run: VB.NET services Transport=pipe

None detected.
## Timing summary (latest run)
Run: VB.NET services Transport=pipe

- [n/a] server_starting (484.9 ms)
- [n/a] initialize_response (725.66 ms)

### Update 2026-01-12 (trace capture + fuzz runs)

Diagnostics + services:
- Diagnostics harness now receives publishDiagnostics with expected code after handler + fixture updates.
- Service tests pass with readiness wait and per-test retry.

VS Code trace capture:
- Extension log files now copied from `window1/exthost/<extensionId>` when `CAPTURE_VBNET_TRACE=1`.
- Example summary: `_test/codex-tests/clients/vscode/logs/20260112T234828/vbnet-output-summary.txt` (includes `VB.NET LSP Trace.log`).

Fuzz runs (10 rounds, SKIP_VBNET_SMOKE=1):
- Workspaces: MediumProject, DebugConsole, DWSIM root, DWSIM, DWSIM.ExtensionMethods, DWSIM.Apps.TCPServer, DWSIM.Drawing, DWSIM\Utilities\PressureSafetyValveSizing, DWSIM\Utilities\TrueCriticalPoint, DWSIM\Utilities\LLEEnvelope.
- Document symbols returned empty in some deep subfolder workspaces (PressureSafetyValveSizing, TrueCriticalPoint, LLEEnvelope).
- Workspace symbol queries returned empty across all fuzz runs (queries: Program/ApplicationEvents/TwoDimChartControl/OxyPlot/TCPServer/Point/FrmPsvSize/FrmCritpt/FormLLEDiagram).

Fuzz log bundles:
- `_test/codex-tests/clients/vscode/logs/20260112T235307`
- `_test/codex-tests/clients/vscode/logs/20260112T235325`
- `_test/codex-tests/clients/vscode/logs/20260112T235342`
- `_test/codex-tests/clients/vscode/logs/20260112T235400`
- `_test/codex-tests/clients/vscode/logs/20260112T235418`
- `_test/codex-tests/clients/vscode/logs/20260112T235435`
- `_test/codex-tests/clients/vscode/logs/20260112T235453`
- `_test/codex-tests/clients/vscode/logs/20260112T235512`
- `_test/codex-tests/clients/vscode/logs/20260112T235527`
- `_test/codex-tests/clients/vscode/logs/20260112T235542`

Revised risks:
1) Workspace/document symbols appear empty in deep subfolder workspaces without a nearby solution/project; investigate fallback behavior.
2) VS Code fuzz runs show empty workspace symbol results even for small projects; add readiness/diagnostics checks or ensure project load under non-root workspaces.

Follow-up (retry-enabled fuzz checks):
- MediumProject workspace symbol query succeeded after retry (log: `_test/codex-tests/clients/vscode/logs/20260112T235848`).
- DWSIM root workspace symbol query still empty after retry window (log: `_test/codex-tests/clients/vscode/logs/20260112T235906`).

### Update 2026-01-13 (fixture path resolution + VS Code smoke)

Harness fix:
- Resolve `FIXTURE_WORKSPACE`/`FIXTURE_FILE` relative to repo root when passed as relative paths.
- Updated VS Code harness entry and tests to avoid resolving under the VS Code install directory.

VS Code headless run (smoke):
- Command: `npm test` from `_test/codex-tests/clients/vscode` with `EXTENSION_VSIX=src/extension/vbnet-language-support.vsix`, `FIXTURE_WORKSPACE=_test/codex-tests/vbnet-lsp/fixtures/services`, `FIXTURE_FILE=_test/codex-tests/vbnet-lsp/fixtures/services/ServiceSamples.vb`, `CAPTURE_VSCODE_LOGS=1`, `CAPTURE_VBNET_TRACE=1`.
- Result: PASS (workspace open + VB.NET core services + rename + restart + completion toggle).
- Log bundle: `_test/codex-tests/clients/vscode/logs/20260113T001822`

### Update 2026-01-13 (ancestor discovery + deep subfolder fuzz)

Server updates:
- Workspace discovery now probes ancestor directories for `.sln`/`.slnf` and `.vbproj` when the workspace root is a deep subfolder.
- Workspace symbol requests wait briefly for the initial workspace load to complete.

VS Code headless run (deep subfolder fuzz):
- Command: `npm test` from `_test/codex-tests/clients/vscode` with `SKIP_VBNET_SMOKE=1`, `FIXTURE_WORKSPACE=test/TestProjects/SmallProject/Deep`, `FUZZ_FILES=test/TestProjects/SmallProject/Deep/DeepGreeter.vb`, `FUZZ_QUERY=DeepGreeter`, `FUZZ_REQUIRE_SYMBOLS=1`, `CAPTURE_VSCODE_LOGS=1`, `CAPTURE_VBNET_TRACE=1`.
- Result: PASS (document symbols + workspace symbols returned).
- Log bundle: `_test/codex-tests/clients/vscode/logs/20260113T002607`

### Update 2026-01-13 (document symbol fallback + VS Code smoke)

Server updates:
- Document symbol requests now fall back to syntax-only symbols when Roslyn semantic model is not yet available.

VS Code headless run (smoke):
- Command: `npm test` from `_test/codex-tests/clients/vscode` with the services fixture and trace capture enabled.
- Result: PASS (all VB.NET smoke tests).
- Log bundle: `_test/codex-tests/clients/vscode/logs/20260113T002807`

### Update 2026-01-13 (workspace symbols filter + unit tests)

Server updates:
- Workspace symbols now filter to `.vb` sources when solutions include mixed-language projects.

Tests:
- `dotnet test test/VbNet.LanguageServer.Tests --filter FullyQualifiedName~SymbolsServiceTests` (PASS, 7 tests).

### Update 2026-01-13 (.slnf watch + VS Code smoke)

Server/extension updates:
- Added `.slnf` to workspace watchers and reload triggers for file change handling.

VS Code headless run (smoke):
- Command: `npm test` from `_test/codex-tests/clients/vscode` with the services fixture and trace capture enabled.
- Result: PASS (all VB.NET smoke tests).
- Log bundle: `_test/codex-tests/clients/vscode/logs/20260113T003121`

### Update 2026-01-13 (open-doc workspace symbol fallback)

Server updates:
- Workspace symbol requests fall back to open documents when no solution is loaded.

Tests:
- `dotnet test test/VbNet.LanguageServer.Tests --filter FullyQualifiedName~SymbolsServiceTests` (PASS, 7 tests).

### Update 2026-01-13 (project scan pruning + unit tests)

Server updates:
- VB.NET project discovery now skips excluded directories while traversing to reduce expensive scans.

Tests:
- `dotnet test test/VbNet.LanguageServer.Tests --filter FullyQualifiedName~WorkspaceManagerTests` (PASS, 13 tests).

### Update 2026-01-13 (syntax-only document symbols include members)

Server updates:
- Syntax-only document symbols now include immediate members for better fallback results.

Tests:
- `dotnet test test/VbNet.LanguageServer.Tests --filter FullyQualifiedName~SymbolsServiceTests` (PASS, 7 tests).

### Update 2026-01-13 (solution candidate selection)

Server updates:
- Workspace discovery now checks all nearby solutions for VB projects instead of only the first match.

Tests:
- `dotnet test test/VbNet.LanguageServer.Tests --filter FullyQualifiedName~WorkspaceManagerTests` (PASS, 13 tests).
