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

### Update 2026-01-13 (debug harness termination)

Debug harness updates:
- Debug test now launches without `stopAtEntry` and waits for a natural terminate event, with a fallback stop if termination doesn’t arrive.

VS Code headless run (debug only):
- Command: `npm test` from `_test/codex-tests/clients/vscode` with `EXTENSION_VSIX=src/extension/vbnet-language-support.vsix`, `NETCOREDBG_PATH=_external/netcoredbg/bin/netcoredbg.exe`, `SKIP_VBNET_SMOKE=1`, `SKIP_CSHARP_TESTS=1`, `FIXTURE_WORKSPACE=test/TestProjects/DebugConsole`.
- Result: PASS (workspace open + debug session start/terminate).
- Note: VS Code printed `Failed command 'threads' : 0x80004005` during the debug session, but the test completed successfully.

### Update 2026-01-13 (signature help)

Server updates:
- Added signature help handler + capability, with Roslyn-backed results and a semantic-model fallback.

Tests:
- `dotnet test test/VbNet.LanguageServer.Tests --filter FullyQualifiedName~SignatureHelp`
- Result: PASS (3 tests).

### Update 2026-01-13 (semantic tokens baseline)

Server updates:
- Added semantic tokens full/range handlers and Roslyn classification mapping.

Tests:
- `dotnet test test/VbNet.LanguageServer.Tests --filter FullyQualifiedName~SemanticTokens`
- Result: PASS (3 tests).

### Update 2026-01-13 (code actions baseline + VS Code harness)

Server updates:
- Added `textDocument/codeAction` with source actions for VB `Option` statements.

Tests:
- `dotnet test test/VbNet.LanguageServer.Tests --filter FullyQualifiedName~CodeActions`
- Result: PASS (3 tests).

VS Code headless run (smoke):
- Command: `npm test` from `_test/codex-tests/clients/vscode` with `EXTENSION_VSIX=src/extension/vbnet-language-support.vsix`, `SKIP_CSHARP_TESTS=1`, `FIXTURE_WORKSPACE=_test/codex-tests/vbnet-lsp/fixtures/services`, `FIXTURE_FILE=_test/codex-tests/vbnet-lsp/fixtures/services/ServiceSamples.vb`.
- Result: PASS (core services + signature help + code action presence).
- Note: VS Code still printed `Failed command 'threads' : 0x80004005` during the debug session, but the test completed successfully.

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

### Update 2026-01-13 (member enumeration tightened)

Server updates:
- Syntax fallback now enumerates immediate type members instead of all descendants to avoid nested duplication.

Tests:
- `dotnet test test/VbNet.LanguageServer.Tests --filter FullyQualifiedName~SymbolsServiceTests` (PASS, 7 tests).

### Update 2026-01-13 (project search cap setting)

Server updates:
- `vbnet.workspace.maxProjectResults` now limits server-side project discovery.

Tests:
- `dotnet test test/VbNet.LanguageServer.Tests --filter FullyQualifiedName~WorkspaceManagerTests` (PASS, 13 tests).

### Update 2026-01-13 (open-doc symbols only for VB)

Server updates:
- Workspace symbol fallback now ignores non-VB open documents.

Tests:
- `dotnet test test/VbNet.LanguageServer.Tests --filter FullyQualifiedName~SymbolsServiceTests` (PASS, 7 tests).

### Update 2026-01-13 (cancel pending diagnostics on disable)

Server updates:
- Disabling diagnostics now cancels pending debounce timers and computations.

Tests:
- `dotnet test test/VbNet.LanguageServer.Tests --filter FullyQualifiedName~DiagnosticsServiceTests` (PASS, 11 tests).

### Update 2026-01-13 (late association on change)

Server updates:
- Document changes now attempt late workspace association to keep buffers in sync sooner.

Tests:
- `dotnet test test/VbNet.LanguageServer.Tests --filter FullyQualifiedName~DocumentManagerTests` (PASS, 9 tests).

### Update 2026-01-13 (closed-doc refresh resilience)

Server updates:
- Closed-document refresh now tolerates I/O and access errors.

Tests:
- `dotnet test test/VbNet.LanguageServer.Tests --filter FullyQualifiedName~DocumentManagerTests` (PASS, 9 tests).

### Update 2026-01-13 (solution candidate de-duplication)

Server updates:
- Workspace discovery now de-duplicates nearby solution candidates.

Tests:
- `dotnet test test/VbNet.LanguageServer.Tests --filter FullyQualifiedName~WorkspaceManagerTests` (PASS, 13 tests).

### Update 2026-01-13 (zero debounce fast path)

Server updates:
- Diagnostics now run immediately when `vbnet.debounceMs` is set to 0.

Tests:
- `dotnet test test/VbNet.LanguageServer.Tests --filter FullyQualifiedName~DiagnosticsServiceTests` (PASS, 11 tests).

### Update 2026-01-13 (syntax member name guards)

Server updates:
- Syntax fallback skips empty member names in workspace/document symbol results.

Tests:
- `dotnet test test/VbNet.LanguageServer.Tests --filter FullyQualifiedName~SymbolsServiceTests` (PASS, 7 tests).

### Update 2026-01-13 (empty change guard)

Server updates:
- Ignore empty `didChange` payloads to avoid spurious version bumps.

Tests:
- `dotnet test test/VbNet.LanguageServer.Tests --filter FullyQualifiedName~DocumentManagerTests` (PASS, 10 tests).

### Update 2026-01-13 (diagnostics file path normalization)

Server updates:
- Diagnostics path handling now normalizes Windows `file:///c:/...` URIs to local paths.

Tests:
- `dotnet test test/VbNet.LanguageServer.Tests --filter FullyQualifiedName~DiagnosticsServiceTests` (PASS, 11 tests).

### Update 2026-01-13 (diagnostics mode + debounce settings)

Server/extension updates:
- Added `vbnet.diagnosticsMode` and `vbnet.debounceMs` settings with server-side behavior.

Tests:
- `dotnet test test/VbNet.LanguageServer.Tests --filter FullyQualifiedName~DiagnosticsServiceTests` (PASS, 11 tests).

VS Code headless run (smoke):
- Command: `npm test` from `_test/codex-tests/clients/vscode` with the services fixture and trace capture enabled.
- Result: PASS (all VB.NET smoke tests).
- Log bundle: `_test/codex-tests/clients/vscode/logs/20260113T004738`

### Update 2026-01-13 (debug harness)

Debugging updates:
- Added VS Code harness debug launch test for netcoredbg (skips if binary is unavailable).

VS Code headless run (debug workspace):
- Command: `npm test` from `_test/codex-tests/clients/vscode` with `FIXTURE_WORKSPACE=test/TestProjects/DebugConsole`, `SKIP_VBNET_SMOKE=1`, `SKIP_CSHARP_TESTS=1`.
- Result: SKIP (netcoredbg not found in environment).
- Log bundle: `_test/codex-tests/clients/vscode/logs/20260113T010208`
### Update 2026-01-13 (folding ranges + netcoredbg build + debug harness)

Server updates:
- Added folding range support for VB blocks and #Region pairs (textDocument/foldingRange).

Tests:
- `dotnet test test/VbNet.LanguageServer.Tests --filter FullyQualifiedName~FoldingRangeServiceTests` (PASS).

Debugger build:
- Built netcoredbg from `_external/netcoredbg` using Visual Studio 2022 CMake generator; binaries installed to `_external/netcoredbg/bin`.

VS Code debug harness:
- Debug harness updated to tolerate configuration update failures when setting `vbnet.debugger.path`.
- Run attempt failed because another VS Code instance was running (VS Code test runner limitation).
- Log bundle: `_test/codex-tests/clients/vscode/logs/20260113T074304`.
### Update 2026-01-13 (formatting + harness checks)

Server updates:
- Added document/range formatting via Roslyn Formatter with LSP options mapping and post-format trimming rules.

Tests:
- `dotnet test test/VbNet.LanguageServer.Tests --filter FullyQualifiedName~FormattingServiceTests` (PASS).

VS Code headless runs:
- Debug workspace run (netcoredbg) succeeded; debug session did not terminate cleanly and logged a threads command error. Logs: `_test/codex-tests/clients/vscode/logs/20260113T094602`, `_test/codex-tests/clients/vscode/logs/20260113T094915`.
- Services fixture run (smoke + folding + formatting) PASS; debug test still reports termination timeout. Log bundle: `_test/codex-tests/clients/vscode/logs/20260113T095205`.

### Update 2026-01-13 (netcoredbg rebuild + debug harness rerun)

Debugger build:
- Rebuilt netcoredbg from `_external/netcoredbg` using Visual Studio 2022 CMake generator; binaries installed to `_external/netcoredbg/bin`.

VS Code debug harness:
- Command: `npm test` from `_test/codex-tests/clients/vscode` with `EXTENSION_VSIX=src/extension/vbnet-language-support.vsix`, `FIXTURE_WORKSPACE=test/TestProjects/DebugConsole`, `SKIP_VBNET_SMOKE=1`, `SKIP_CSHARP_TESTS=1`.
- Result: PASS (debug test) after terminating leftover VS Code test processes.
- Log bundle: `_test/codex-tests/clients/vscode/logs/20260113T095205`.

### Update 2026-01-13 (debug DAP trace capture)

Harness update:
- Added DAP message trace capture for the debug harness to record terminate/disconnect sequencing.

VS Code debug harness:
- Command: `npm test` from `_test/codex-tests/clients/vscode` with `EXTENSION_VSIX=src/extension/vbnet-language-support.vsix`, `FIXTURE_WORKSPACE=test/TestProjects/DebugConsole`, `SKIP_VBNET_SMOKE=1`, `SKIP_CSHARP_TESTS=1`, `CAPTURE_DAP_TRACE=1`.
- Result: PASS (debug test).
- DAP trace: `_test/codex-tests/clients/vscode/logs/dap-trace-2026-01-13T113145188Z.log`.

### Update 2026-01-13 (full harness + DAP trace review)

VS Code headless run (services + debug):
- Command: `npm test` from `_test/codex-tests/clients/vscode` with `EXTENSION_VSIX=src/extension/vbnet-language-support.vsix`, `SKIP_CSHARP_TESTS=1`, `CAPTURE_DAP_TRACE=1`.
- Result: PASS (all VB.NET smoke tests + debug harness).
- Note: VS Code logged a `threads` request error after the adapter already reported `exited` + `terminated`.
- DAP trace: `_test/codex-tests/clients/vscode/logs/dap-trace-2026-01-13T113435152Z.log`.

### Update 2026-01-13 (logging cleanup + integration pass)

Server updates:
- Skip duplicate project loads instead of logging errors.
- Signature help no longer logs reflection constraint errors; fallback paths log at debug.
- Fixed `test/TestProjects/SmallProject/Helper.vb` duplication to restore valid syntax.

Tests:
- `dotnet test test/VbNet.LanguageServer.Tests --filter FullyQualifiedName~Integration` (PASS, 46 tests).

VS Code headless run (logs captured):
- Command: `npm test` from `_test/codex-tests/clients/vscode` with `EXTENSION_VSIX=src/extension/vbnet-language-support.vsix`, `SKIP_CSHARP_TESTS=1`, `CAPTURE_VSCODE_LOGS=1`, `CAPTURE_VBNET_TRACE=1`.
- Result: PASS (all VB.NET smoke tests + debug harness).
- Log bundle: `_test/codex-tests/clients/vscode/logs/20260113T141046`.

Emacs eglot run:
- Command: `_test/codex-tests/clients/emacs/run-tests.ps1 -Suite vbnet`.
- Result: PASS (VB.NET eglot smoke).
- Log: `_test/codex-tests/clients/emacs/logs/emacs-eglot-20260113T141735.log`.

### Update 2026-01-13 (web/remote guard + full test sweep)

Extension updates:
- Added a VS Code Web guard to avoid attempting to start the server in virtual workspaces.
- Virtual workspace capability now includes a clear unsupported description.
- README now calls out remote container support and web limitations.

Server updates:
- Reloaded workspace now still fires SolutionChanged when projects are already loaded.
- Restored `test/TestProjects/SmallProject/Helper.vb` to a valid single-method state.

Tests:
- `dotnet test test/VbNet.LanguageServer.Tests` (PASS, 136 tests; warning CS0219 pre-existing).

VS Code headless run (logs captured):
- Command: `npm test` from `_test/codex-tests/clients/vscode` with `EXTENSION_VSIX=src/extension/vbnet-language-support.vsix`, `SKIP_CSHARP_TESTS=1`, `CAPTURE_VSCODE_LOGS=1`, `CAPTURE_VBNET_TRACE=1`, `CAPTURE_DAP_TRACE=1`.
- Result: PASS (all VB.NET smoke tests + debug harness).
- Note: VS Code logged a `threads` request error after debug termination (existing netcoredbg behavior).
- Log bundle: `_test/codex-tests/clients/vscode/logs/20260113T143200`.
- DAP trace: `_test/codex-tests/clients/vscode/logs/dap-trace-2026-01-13T123216937Z.log`.

Emacs eglot run:
- Command: `_test/codex-tests/clients/emacs/run-tests.ps1 -Suite vbnet`.
- Result: PASS (VB.NET eglot smoke; jsonrpc reports exit status 9 on shutdown).
- Log: `_test/codex-tests/clients/emacs/logs/emacs-eglot-20260113T143237.log`.

### Update 2026-01-13 (emacs harness logging tweak)

Emacs updates:
- Adjusted eglot harness logging to avoid hard failures when jsonrpc-process is unavailable.

Emacs eglot run:
- Command: `_test/codex-tests/clients/emacs/run-tests.ps1 -Suite vbnet`.
- Result: PASS (VB.NET eglot smoke; jsonrpc reports exit status 9 on shutdown).
- Log: `_test/codex-tests/clients/emacs/logs/emacs-eglot-20260113T150821.log`.

Dev container smoke check:
- Attempted to run a local dev container smoke test, but Docker was not available on this host (skipped).

### Update 2026-01-13 (dev container setup)

Dev container setup:
- Added `.devcontainer/devcontainer.json` to make a repeatable .NET + Node dev container environment.
- Dev container smoke check still pending due to Docker unavailability on this host.

### Update 2026-01-13 (virtual workspace guard + full rerun)

Extension updates:
- Added a virtual workspace guard using non-file workspace folder schemes.

Test project fix:
- Restored `test/TestProjects/SmallProject/Helper.vb` to valid syntax after accidental duplication.

Tests:
- `dotnet test test/VbNet.LanguageServer.Tests` (PASS, 136 tests; warning CS0219 pre-existing).

VS Code headless run (logs captured):
- Command: `npm test` from `_test/codex-tests/clients/vscode` with `EXTENSION_VSIX=src/extension/vbnet-language-support.vsix`, `SKIP_CSHARP_TESTS=1`, `CAPTURE_VSCODE_LOGS=1`, `CAPTURE_VBNET_TRACE=1`, `CAPTURE_DAP_TRACE=1`.
- Result: PASS (all VB.NET smoke tests + debug harness).
- Log bundle: `_test/codex-tests/clients/vscode/logs/20260113T151947`.
- DAP trace: `_test/codex-tests/clients/vscode/logs/dap-trace-2026-01-13T132005650Z.log`.

Emacs eglot run:
- Command: `_test/codex-tests/clients/emacs/run-tests.ps1 -Suite vbnet`.
- Result: PASS (VB.NET eglot smoke; jsonrpc reports exit status 9 on shutdown).
- Log: `_test/codex-tests/clients/emacs/logs/emacs-eglot-20260113T152027.log`.

### Update 2026-01-13 (Marketplace pre-release published)

Release:
- Command: `vsce publish --pre-release -p $env:VSCODE_PAT` from `src/extension`.
- Result: Published `dnakode.vbnet-language-support` v0.1.0 (pre-release).
- Marketplace listing: `https://marketplace.visualstudio.com/items?itemName=dnakode.vbnet-language-support`.

### Update 2026-01-13 (completion prefix text edit)

Tests:
- `dotnet test test/VbNet.LanguageServer.Tests/VbNet.LanguageServer.Tests.csproj -c Release -v minimal --filter FullyQualifiedName~GetCompletionAsync_KeywordCompletion_ReplacesPrefix`
- Result: PASS (1 test).

### Update 2026-01-13 (typing completion harness)

VS Code headless run (smoke + typing completion):
- Command: `npm test` from `_test/codex-tests/clients/vscode` with `EXTENSION_VSIX=src/extension/vbnet-language-support.vsix`, `VBNET_SERVER_PATH=src/VbNet.LanguageServer/bin/Release/net10.0/VbNet.LanguageServer.dll`, `SKIP_CSHARP_TESTS=1`, `CAPTURE_VSCODE_LOGS=1`, `CAPTURE_VBNET_TRACE=1`.
- Result: PASS (typing completion test added).
- Log bundle: `_test/codex-tests/clients/vscode/logs/20260113T214345`.

### Update 2026-01-13 (bundled server harness run)

VS Code headless run (uses bundled .server, no VBNET_SERVER_PATH):
- Command: `npm test` from `_test/codex-tests/clients/vscode` with `EXTENSION_DEV_PATH=src/extension`, `VBNET_SKIP_DEFAULT_SERVER_PATH=1`, `EXTENSION_VSIX=src/extension/vbnet-language-support.vsix`, `SKIP_CSHARP_TESTS=1`, `CAPTURE_VSCODE_LOGS=1`, `CAPTURE_VBNET_TRACE=1`.
- Result: PASS (typing completion test).
- Log bundle: `_test/codex-tests/clients/vscode/logs/20260113T215752`.

### Update 2026-01-13 (0.1.1 release smoke)

VS Code headless run (bundled server + debugger):
- Command: `npm test` from `_test/codex-tests/clients/vscode` with `EXTENSION_VSIX=src/extension/vbnet-language-support.vsix`, `EXTENSION_DEV_PATH=src/extension`, `VBNET_SKIP_DEFAULT_SERVER_PATH=1`, `SKIP_CSHARP_TESTS=1`, `CAPTURE_VSCODE_LOGS=1`, `CAPTURE_VBNET_TRACE=1`.
- Result: PASS (all VB.NET smoke tests + debug harness).
- Log bundle: `_test/codex-tests/clients/vscode/logs/20260113T222824`.

### Update 2026-01-13 (Marketplace pre-release published 0.1.1)

Release:
- Command: `vsce publish --pre-release -p $env:VSCODE_PAT` from `src/extension`.
- Result: Published `dnakode.vbnet-language-support` v0.1.1 (pre-release).
- Marketplace listing: `https://marketplace.visualstudio.com/items?itemName=dnakode.vbnet-language-support`.

### Update 2026-01-13 (debug program inference)

Extension updates:
- Debug configuration provider now attempts to infer `program` when missing and a single `.vbproj` exists with a built `bin/Debug/**/<Assembly>.dll`.
- Debug harness adds a launch test that omits `program` to verify inference.

VS Code headless run (debug workspace):
- Command: `npm test` from `_test/codex-tests/clients/vscode` with `FIXTURE_WORKSPACE=test/TestProjects/DebugConsole`.
- Result: FAIL (VS Code reported another instance running: "Running extension tests from the command line is currently only supported if no other instance of Code is running.").
- Note: Earlier run timed out before completion while VS Code was still running; rerun needed after closing other VS Code instances.

### Update 2026-01-13 (debug program template resolution)

Extension updates:
- Debug configuration provider now resolves `<target-framework>` and `<project-name>` placeholders in `program` values.
- Debug harness adds a launch test that uses a template program path.

VS Code headless run (debug workspace):
- Command: `npm test` from `_test/codex-tests/clients/vscode` with `FIXTURE_WORKSPACE=test/TestProjects/DebugConsole`.
- Result: FAIL (VS Code reported another instance running: "Running extension tests from the command line is currently only supported if no other instance of Code is running.").

### Update 2026-01-13 (debug project selection prompt)

Extension updates:
- When `program` is missing and multiple `.vbproj` files exist, the debug configuration provider now prompts to select the project to debug.

VS Code headless run (debug workspace):
- Command: `npm test` from `_test/codex-tests/clients/vscode` with `FIXTURE_WORKSPACE=test/TestProjects/DebugConsole`.
- Result: FAIL (VS Code reported another instance running: "Running extension tests from the command line is currently only supported if no other instance of Code is running.").

### Update 2026-01-14 (debug inference + template resolution re-test)

Extension updates:
- Debug inference now logs discovery paths and can fall back to a lone DLL under `bin/Debug`.
- Debugger launch schema no longer requires `program` (inference can run).

Harness updates:
- `EXTENSION_VSIX` is resolved relative to repo root when provided as a relative path.

VS Code headless run (debug workspace):
- Command: `npm test` from `_test/codex-tests/clients/vscode` with `FIXTURE_WORKSPACE=test/TestProjects/DebugConsole`, `SKIP_VBNET_SMOKE=1`, `SKIP_CSHARP_TESTS=1`, `CAPTURE_VSCODE_LOGS=1`, `CAPTURE_VBNET_TRACE=1`, `CAPTURE_DAP_TRACE=1`, `EXTENSION_VSIX=C:\Work\vbnet-lsp\src\extension\vbnet-language-support.vsix`.
- Result: PASS (debug session launch, inferred program launch, template launch).
- Log bundle: `_test/codex-tests/clients/vscode/logs/20260114T050032`.

### Update 2026-01-14 (debug projectPath inference + harness cleanup)

Extension updates:
- Debug configuration schema now exposes `projectPath` to guide inference.
- Debug resolver logs resolved program/cwd and returns a clear error when the program DLL is missing.

Harness updates:
- VS Code harness can optionally track/kill `Code.exe` processes via `VSCODE_KILL_BEFORE_TESTS=1` and `VSCODE_KILL_ON_EXIT=1`.

VS Code headless run (debug workspace):
- Command: `npm test` from `_test/codex-tests/clients/vscode` with `FIXTURE_WORKSPACE=test/TestProjects/DebugConsole`, `SKIP_VBNET_SMOKE=1`, `SKIP_CSHARP_TESTS=1`, `CAPTURE_VSCODE_LOGS=1`, `CAPTURE_VBNET_TRACE=1`, `CAPTURE_DAP_TRACE=1`, `EXTENSION_VSIX=C:\Work\vbnet-lsp\src\extension\vbnet-language-support.vsix`, `VSCODE_KILL_BEFORE_TESTS=1`, `VSCODE_KILL_ON_EXIT=1`.
- Result: PASS (debug launch + inferred program + template program + projectPath inference).
- Note: `threads` DAP request still returns 0x80004005 after termination (netcoredbg behavior).
- Log bundle: `_test/codex-tests/clients/vscode/logs/20260114T050421`.

### Update 2026-01-14 (Linux support scaffolding)

Changes:
- netcoredbg bundling now supports `NETCOREDBG_PATH`/`NETCOREDBG_LICENSE` and sets executable bit on non-Windows.
- Added platform-targeted VSIX packaging scripts and updated developer docs.

Tests:
- Not run (packaging/doc changes only).

### Update 2026-01-14 (WSL2 Linux debug harness)

Server updates:
- Disable NuGet fallback package folders on non-Windows to avoid invalid Windows paths during restore.

WSL setup (logged in `_test/codex-tests/logs/wsl-linux-build-20260114.txt`):
- Installed build deps, clang, Node.js 20, local .NET 10 SDK, and VS Code runtime libs.
- Built netcoredbg in WSL and used `NETCOREDBG_PATH=/home/govert/netcoredbg-wsl/build-linux/src/netcoredbg`.

VS Code headless run (WSL, linux-x64):
- Command: `npm test` in `_test/codex-tests/clients/vscode` with `EXTENSION_VSIX=/mnt/c/Work/vbnet-lsp/src/extension/vbnet-language-support-linux-x64.vsix`, debug-only env flags, and `xvfb-run`.
- Result: PASS (debug launch + inferred program + template + projectPath inference).
- Note: VS Code CLI prints WSL warning prompt and logs DBus errors; bootstrap reports `Unexpected SIGPIPE`, but tests still pass.
- Log bundle: `_test/codex-tests/clients/vscode/logs/20260114T081916`.

### Update 2026-01-14 (WSL2 prompt suppression)

Harness updates:
- VS Code CLI runs now set `DONT_PROMPT_WSL_INSTALL=1` when running under WSL to avoid interactive prompts during extension install/list.

WSL debug run (no prompt):
- Command: `npm test` in `_test/codex-tests/clients/vscode` with `DONT_PROMPT_WSL_INSTALL=1` and `NETCOREDBG_PATH` set to the WSL-built debugger.
- Result: PASS (all VB.NET debug tests).
- Note: DBus warnings and an `Unexpected SIGPIPE` still appear but do not fail tests.
- Log bundle: `_test/codex-tests/clients/vscode/logs/20260114T083146`.

### Update 2026-01-14 (WSL2 dbus noise mitigation)

Harness updates:
- Set `NO_AT_BRIDGE=1`, `DBUS_SESSION_BUS_ADDRESS=unix:path=/dev/null`, and launch flag `--disable-features=UseDbus` under WSL.

WSL debug run:
- Result: PASS (debug tests).
- Note: DBus errors remain (now pointing at `/dev/null`), and SIGPIPE still appears; warnings considered benign for headless runs.
- Log bundle: `_test/codex-tests/clients/vscode/logs/20260114T084756`.
