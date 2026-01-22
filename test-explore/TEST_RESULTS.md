Date: 2026-01-20
Author: Codex (GPT-5) acting as test reviewer
Host: Windows (C:\Work\vbnet-lsp)

# Test Results

## Current status

- C# implementation and C# harnesses have been removed; all test commands below are VB.NET-only.
- Update this file after significant exploratory runs.

## Recommended test commands (VB.NET only)

### CI-safe unit/manifest tests

```powershell
# Language server tests
 dotnet test test\VbNet.LanguageServer.Tests.Vb\VbNet.LanguageServer.Tests.Vb.vbproj -c Release

# Extension manifest tests
 dotnet test test\VbNet.Extension.Tests.Vb\VbNet.Extension.Tests.Vb.vbproj -c Release
```

### LSP smoke harness

```powershell
 test-explore\vbnet-lsp\run-tests.ps1
```

### VS Code harness (headless)

```powershell
cd test-explore\clients\vscode
npm test
```

Optional flags:
- `SKIP_VBNET_DEBUG=1` to skip debugger suite.
- `SKIP_VBNET_SMOKE=1` to skip LSP smoke suite.
- `FIXTURE_WORKSPACE` to point at a fixture workspace (use `test-explore/vbnet-lsp/fixtures/services` for LSP smoke).

### Emacs harness

```powershell
 test-explore\clients\emacs\run-tests.ps1 -Suite vbnet
```

## Recent runs

### 2026-01-22 — CI-safe tests (VB.NET only)

Commands:
- `dotnet test test\VbNet.LanguageServer.Tests.Vb\VbNet.LanguageServer.Tests.Vb.vbproj -c Release`
- `dotnet test test\VbNet.Extension.Tests.Vb\VbNet.Extension.Tests.Vb.vbproj -c Release`

Outcome: PASS (150/150, 6/6)

### 2026-01-22 — VS Code harness (VB.NET server, test commands)

Command:
- `cd test-explore\\clients\\vscode; npm test`

Outcome: PASS (14 passing, 5 pending; non-fatal DAP warnings)
Notes:
- DAP warnings: `Failed command 'threads' : 0x80004005`.
- DAP trace: `test-explore/clients/vscode/logs/dap-trace-2026-01-22T203652019Z.log`.

### 2026-01-22 — VS Code harness (VB.NET server, attach command)

Command:
- `cd test-explore\\clients\\vscode; npm test`

Outcome: PASS (14 passing, 5 pending; non-fatal DAP warnings)
Notes:
- DAP warnings: `Failed command 'threads' : 0x80004005`.
- DAP trace: `test-explore/clients/vscode/logs/dap-trace-2026-01-22T202246021Z.log`.

### 2026-01-22 — VS Code harness (VB.NET server, reload command)

Command:
- `cd test-explore\\clients\\vscode; npm test`

Outcome: PASS (14 passing, 5 pending; non-fatal DAP warnings)
Notes:
- DAP warnings: `Failed command 'threads' : 0x80004005`.
- DAP trace: `test-explore/clients/vscode/logs/dap-trace-2026-01-22T201724918Z.log`.

### 2026-01-22 — VS Code harness (VB.NET server, restore commands)

Command:
- `cd test-explore\\clients\\vscode; npm test`

Outcome: PASS (14 passing, 5 pending)
Notes:
- DAP trace: `test-explore/clients/vscode/logs/dap-trace-2026-01-22T200313660Z.log`.

### 2026-01-22 — VS Code harness (VB.NET server, trace commands)

Command:
- `cd test-explore\\clients\\vscode; npm test`

Outcome: PASS (14 passing, 5 pending; non-fatal DAP warnings)
Notes:
- DAP warnings: `Failed command 'threads' : 0x80004005` (during debug session).
- DAP trace: `test-explore/clients/vscode/logs/dap-trace-2026-01-22T195827255Z.log`.

### 2026-01-22 — VS Code harness (VB.NET server)

Command:
- `cd test-explore\\clients\\vscode; npm test`

Outcome: PASS (14 passing, 5 pending)
Notes:
- DAP trace: `test-explore/clients/vscode/logs/dap-trace-2026-01-22T195325297Z.log`.

### 2026-01-20 � test-explore suite (all, VB.NET)

Command:
- `test-explore\run-tests.ps1`

Outcome: PASS (with non-fatal warnings)
Notes:
- LSP smoke: PASS (snapshot `test-explore/vbnet-lsp/snapshots/20260120-102607`).
- Emacs eglot: PASS, shutdown timeout after server exit (non-fatal). Log: `test-explore/clients/emacs/logs/emacs-eglot-20260120T102611.log`.
- DWSIM smoke: PASS, but no solution or VB.NET projects detected in `_external/dwsim` (workspace scan only).

### 2026-01-21 — Neovim harness (C# Roslyn reference)

Command:
- ROSLYN_LS_DLL=C:\Work\vbnet-lsp\_external\roslyn-ls-mason-win-x64\Microsoft.CodeAnalysis.LanguageServer.dll test-explore\clients\nvim\run-tests.ps1 -Suite csharp

Outcome: PASS
Notes:
- Roslyn server build from Mason custom registry: Crashdummyy/roslynLanguageServer version 5.4.0-2.26062.9.
- Log: `test-explore/clients/nvim/logs/nvim-20260121-060030.log`.

### 2026-01-21 â€” Neovim harness (VB.NET Roslyn reference)

Command:
- ROSLYN_LS_DLL=C:\Work\vbnet-lsp\_external\roslyn-ls-mason-win-x64\Microsoft.CodeAnalysis.LanguageServer.dll test-explore\clients\nvim\run-tests.ps1 -Suite roslyn-vb

Outcome: FAIL (hover)
Notes:
- Hover returned error "Document is null" for Module1.vb after project open; retry did not resolve.
- Follow-up with `solution/open` using `SmallProject.slnx` still returned "Document is null".
- After creating `SmallProject.sln` and adding instrumentation, Roslyn reported "The language 'Visual Basic' is not supported" during semantic tokens/diagnostics/hover, and failed to load `SmallProject.vbproj`.
- Log: `test-explore/clients/nvim/logs/nvim-20260121-083635.log`.

### 2026-01-21 â€” Neovim harness (VB.NET Roslyn reference, augmented package)

Command:
- ROSLYN_LS_DLL=C:\Work\vbnet-lsp\_external\roslyn-ls-mason-win-x64-vb\Microsoft.CodeAnalysis.LanguageServer.dll test-explore\clients\nvim\run-tests.ps1 -Suite roslyn-vb

Outcome: FAIL (hover)
Notes:
- Augmented the Mason Roslyn LSP folder with `Microsoft.CodeAnalysis.VisualBasic*.dll` from NuGet (version 4.14.0, net8.0).
- Roslyn still reports "The language 'Visual Basic' is not supported" and cannot load the VB project.
- Log: `test-explore/clients/nvim/logs/nvim-20260121-104802.log`.

### 2026-01-21 — Neovim harness (Roslyn LSP built from source)

Command:
- ROSLYN_LS_DLL=C:\Work\vbnet-lsp\_external\roslyn-ls-built-win-x64\Microsoft.CodeAnalysis.LanguageServer.dll test-explore\clients\nvim\run-tests.ps1 -Suite csharp
- ROSLYN_LS_DLL=C:\Work\vbnet-lsp\_external\roslyn-ls-built-win-x64\Microsoft.CodeAnalysis.LanguageServer.dll test-explore\clients\nvim\run-tests.ps1 -Suite roslyn-vb

Outcome:
- C#: PASS
- VB: FAIL (hover)
Notes:
- Built `Microsoft.CodeAnalysis.LanguageServer` and VB assemblies from `_external/roslyn` (Release), then assembled `_external/roslyn-ls-built-win-x64`.
- C# log: `test-explore/clients/nvim/logs/nvim-20260121-105808.log`.
- VB log: `test-explore/clients/nvim/logs/nvim-20260121-105843.log`.
- Retested with MEF cache removed and Trace logging; VB still reported "The language 'Visual Basic' is not supported".
- Trace log: `test-explore/clients/nvim/logs/nvim-20260121-110428.log`.\n
### 2026-01-21 — Roslyn build.ps1 full build (Release, lspEditor)

Command:
- _external\roslyn\Build.cmd -restore -configuration Release -lspEditor -msbuildEngine dotnet

Outcome: PASS
Notes:
- One restore error reported: failed to download `Microsoft.CodeAnalysis.SemanticSearch.Extensions.5.0.0-2.25415.3` from the dnceng feed due to SSL disconnect, but the overall build still completed successfully.

### 2026-01-21 — Neovim harness (Roslyn LSP built full + diagnostics)

Command:
- ROSLYN_LS_DLL=C:\Work\vbnet-lsp\_external\roslyn-ls-built-full-win-x64\Microsoft.CodeAnalysis.LanguageServer.dll test-explore\clients\nvim\run-tests.ps1 -Suite csharp
- ROSLYN_LS_DLL=C:\Work\vbnet-lsp\_external\roslyn-ls-built-full-win-x64\Microsoft.CodeAnalysis.LanguageServer.dll test-explore\clients\nvim\run-tests.ps1 -Suite roslyn-vb

Outcome:
- C#: PASS
- VB: FAIL (hover)
Notes:
- Build assembled from Roslyn artifacts into `_external/roslyn-ls-built-full-win-x64`.
- C# log: `test-explore/clients/nvim/logs/nvim-20260121-114817.log`.
- VB log: `test-explore/clients/nvim/logs/nvim-20260121-115018.log`.
- Diagnostic file: `test-explore/clients/nvim/logs/roslyn-20260121-115017/roslyn-lsp-language-support.txt`.
- Diagnostics show: `SupportedLanguages: TypeScript, C#, Razor` and `Supports Visual Basic: False` even though VB assemblies are present.\n
### 2026-01-21 — Roslyn LSP MEF export diagnostics (deeper dive)

Command:
- ROSLYN_LS_DLL=C:\Work\vbnet-lsp\_external\roslyn-ls-built-full-win-x64\Microsoft.CodeAnalysis.LanguageServer.dll test-explore\clients\nvim\run-tests.ps1 -Suite roslyn-vb (Trace)

Outcome: FAIL (hover)
Notes:
- Diagnostic file: `test-explore/clients/nvim/logs/roslyn-20260121-115632/roslyn-lsp-language-support.txt`.
- MEF exports show **no VB language services**:
  - ILanguageService languages: `C#, Razor, TypeScript`
  - ILanguageServiceFactory languages: `C#`
  - SupportedLanguages: `TypeScript, C#, Razor`
  - Supports Visual Basic: `False`\n## Previous runs

### 2026-01-19 � VS Code harness (VB.NET server)

Commands (from `test-explore/clients/vscode`):
- `VBNET_SERVER_PATH=src\VbNet.LanguageServer.Vb\bin\Debug\net10.0\VbNet.LanguageServer.dll CAPTURE_VSCODE_LOGS=1 CAPTURE_VBNET_TRACE=1 VSCODE_KILL_BEFORE_TESTS=1 VSCODE_KILL_ON_EXIT=1 npm test`

Outcome: PASS (14 passing, 5 pending)
Notes:
- Named-pipe run verified by setting `vbnet.server.transportType=namedPipe` in the fixture settings.
- Non-fatal DAP warning: `Failed command 'threads'` during debug startup.
- Log bundles: `test-explore/clients/vscode/logs/20260119T224840`, `test-explore/clients/vscode/logs/20260119T224915`.

### 2026-01-19 � LSP smoke harness (VB.NET)

Command:
- `test-explore\vbnet-lsp\run-tests.ps1`

Outcome: PASS
Notes:
- Snapshots recorded under `test-explore/vbnet-lsp/snapshots/`.

### 2026-01-19 � CI-safe tests (VB.NET only)

Commands:
- `dotnet test test\VbNet.LanguageServer.Tests.Vb\VbNet.LanguageServer.Tests.Vb.vbproj -c Release`
- `dotnet test test\VbNet.Extension.Tests.Vb\VbNet.Extension.Tests.Vb.vbproj -c Release`

Outcome: PASS (135/135, 3/3)

## Protocol anomalies (latest run)
Run: Theme=core Transport=pipe

None detected.
## Timing summary (latest run)
Run: Theme=core Transport=pipe

- [n/a] server_starting (349.19 ms)
- [n/a] initialize_response (702.24 ms)
- [n/a] didOpen_sent (1191.05 ms)

### 2026-01-20 � LSP smoke harness (VB.NET, core)

Command:
- `test-explore\run-tests.ps1 -Theme core`

Outcome: PASS
Notes:
- Snapshot: `test-explore/vbnet-lsp/snapshots/20260120-220644`.

### 2026-01-21 — Neovim harness (Roslyn LSP full build, cache cleared)

Command:
- ROSLYN_LS_DLL=C:\Work\vbnet-lsp\_external\roslyn-ls-built-full-win-x64\Microsoft.CodeAnalysis.LanguageServer.dll test-explore\clients\nvim\run-tests.ps1 -Suite roslyn-vb (Trace)

Outcome: FAIL (hover)
Notes:
- Cleared `_external/roslyn-ls-built-full-win-x64/cache` before the run; VB still reports "The language 'Visual Basic' is not supported".
- Log: `test-explore/clients/nvim/logs/nvim-20260121-120146.log`.
- Diagnostic file: `C:\Users\GovertvanDrimmelen\AppData\Local\Temp\nvim\roslyn-ls\roslyn-lsp-language-support.txt`.
- Diagnostics still show `SupportedLanguages: C#, Razor, TypeScript` and `Supports Visual Basic: False`.

### 2026-01-21 — Neovim harness (Roslyn LSP with VB assemblies passed via --extension)

Command:
- ROSLYN_LS_DLL=C:\Work\vbnet-lsp\_external\roslyn-ls-built-full-win-x64\Microsoft.CodeAnalysis.LanguageServer.dll
  ROSLYN_LS_EXTENSIONS=C:\Work\vbnet-lsp\_external\roslyn-ls-built-full-win-x64\Microsoft.CodeAnalysis.VisualBasic.Workspaces.dll;C:\Work\vbnet-lsp\_external\roslyn-ls-built-full-win-x64\Microsoft.CodeAnalysis.VisualBasic.Features.dll
  test-explore\clients\nvim\run-tests.ps1 -Suite roslyn-vb

Outcome: FAIL (server crash)
Notes:
- Roslyn LSP crashed during workspace initialization with:
  `Microsoft.VisualStudio.Composition.CompositionFailedException` → `ArgumentException: Specified sequence has duplicate items (analyzerReferences[20])`.
- The duplicate analyzer references were caused by adding VB assemblies via `--extension` while they still existed in the base directory (both sets are added as solution-level analyzers).
- Log: `test-explore/clients/nvim/logs/nvim-20260121-120602.log`.
- Full stderr traces captured in `C:\Users\GovertvanDrimmelen\AppData\Local\nvim-data\lsp.log`.

### 2026-01-21 — Neovim harness (Roslyn LSP with VB assemblies in extension dir)

Command:
- ROSLYN_LS_DLL=C:\Work\vbnet-lsp\_external\roslyn-ls-built-full-win-x64-novb\Microsoft.CodeAnalysis.LanguageServer.dll
  ROSLYN_LS_EXTENSIONS=C:\Work\vbnet-lsp\_external\roslyn-ls-vb-extension\Microsoft.CodeAnalysis.VisualBasic.Workspaces.dll;C:\Work\vbnet-lsp\_external\roslyn-ls-vb-extension\Microsoft.CodeAnalysis.VisualBasic.Features.dll
  test-explore\clients\nvim\run-tests.ps1 -Suite roslyn-vb

Outcome: PASS
Notes:
- Created a VB-free base folder (`_external/roslyn-ls-built-full-win-x64-novb`) and a separate extension folder (`_external/roslyn-ls-vb-extension`) containing only VB assemblies.
- This avoids duplicate analyzer references and allows VB services to load via `--extension`.
- Log: `test-explore/clients/nvim/logs/nvim-20260121-130409.log`.
- Diagnostic file: `C:\Users\GovertvanDrimmelen\AppData\Local\Temp\nvim\roslyn-ls\roslyn-lsp-language-support.txt`.
- Diagnostics show VB enabled: `SupportedLanguages: C#, Visual Basic, Razor, TypeScript` and `Supports Visual Basic: True`.

### 2026-01-21 — Neovim plugin smoke (VB.NET backend)

Command:
- VBNET_LSP_DLL=C:\Work\vbnet-lsp\src\VbNet.LanguageServer.Vb\bin\Debug\net10.0\VbNet.LanguageServer.dll
  VBNET_PLUGIN_BACKEND=vbnet
  nvim --headless -u NONE -c "set rtp+=<repo>/test-explore/clients/nvim/plugin-repo" -l test-explore/clients/nvim/nvim-plugin-smoke.lua

Outcome: PASS
Notes:
- Log: `test-explore/clients/nvim/logs/nvim-plugin-20260121-234911.log`.

### 2026-01-21 — Neovim plugin smoke (Roslyn backend)

Command:
- ROSLYN_LS_DLL=C:\Work\vbnet-lsp\_external\roslyn-ls-built-full-win-x64-novb\Microsoft.CodeAnalysis.LanguageServer.dll
  ROSLYN_LS_EXTENSIONS=C:\Work\vbnet-lsp\_external\roslyn-ls-vb-extension\Microsoft.CodeAnalysis.VisualBasic.Workspaces.dll;C:\Work\vbnet-lsp\_external\roslyn-ls-vb-extension\Microsoft.CodeAnalysis.VisualBasic.Features.dll
  ROSLYN_LSP_SOLUTION=C:\Work\vbnet-lsp\test\TestProjects\SmallProject\SmallProject.sln
  VBNET_PLUGIN_BACKEND=roslyn
  nvim --headless -u NONE -c "set rtp+=<repo>/test-explore/clients/nvim/plugin-repo" -l test-explore/clients/nvim/nvim-plugin-smoke.lua

Outcome: PASS
Notes:
- Log: `test-explore/clients/nvim/logs/nvim-plugin-20260121-234944.log`.
