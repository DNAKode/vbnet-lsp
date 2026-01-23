# `VB.NET` Language Support Project Plan

## Project Overview

**Project Name:** `VB.NET` Language Support
**GitHub Organization:** DNA Kode (DNAKode)
**Repository:** vbnet-lsp
**Display Name:** `VB.NET` Language Support
**VS Code Extension Name:** `VB.NET` Language Support
**Language Server Command:** vbnet-ls (internal component)
**Root Namespace:** VbNet.LanguageServer
**Target Framework:** .NET 10.0+ (latest LTS)
**Distribution:** VS Code Marketplace, Open VSX Registry
**License:** MIT (fully open source)

### Summary

An open-source, Roslyn-backed extension providing first-class `VB.NET` language support for VS Code and compatible editors (Cursor, VSCodium, etc.). This project **directly mirrors the "C# for Visual Studio Code" extension architecture** (github.com/dotnet/vscode-csharp), which is the open-source foundation for C# language support. The implementation follows modern Roslyn LSP patterns and is designed as lasting infrastructure for `VB.NET` development.

### Key Differentiation

- **100% open source** under MIT license (matching the C# extension model)
- **`VB.NET` focused** (mirroring how C# extension handles C#)
- **Open-source debugger** using Samsung's netcoredbg instead of proprietary debugger
- **No proprietary components** (unlike C# Dev Kit which adds closed-source features)

---

## 1. Goals and Scope

### 1.1 Primary Goal

**Provide definitive `VB.NET` language support for VS Code and compatible editors**, achieving feature parity with the **"C# for Visual Studio Code" extension** while remaining fully open source.

### 1.2 Project Philosophy

- **No guessing:** Always verify C# extension behavior through source code inspection (github.com/dotnet/vscode-csharp)
- **Agentic-first development:** Designed for minimal manual intervention, with clear task sequencing
- **Lasting infrastructure:** Build for long-term `VB.NET` community support
- **Documentation as code:** Maintain comprehensive, LLM-friendly documentation throughout
- **Open-source debugger:** Use netcoredbg (DNAKode fork tracking Samsung) instead of proprietary Microsoft debugger

### 1.3 Must-Haves (MVP / Phase 1)

- **Correct `VB.NET` semantics via Roslyn** (no custom parser)
- **LSP architecture matching C# extension** (github.com/dotnet/vscode-csharp)
- **Solution and project loading** for `.sln` and `.vbproj`
- **Incremental text synchronization** with versioned documents
- **Core IDE features:**
  - Diagnostics (matching C# extension's behavior)
  - Completion (with optional resolve)
  - Hover (symbol info + documentation)
  - Go to Definition
  - Find References
  - Rename (with prepareRename)
  - Document and Workspace Symbols
- **Debugging integration** with netcoredbg (open-source debugger)
- **Performance validated against DWSIM** (large real-world `VB.NET` codebase)
- **VS Code extension** packaged and published to Marketplace

### 1.4 Nice-to-Haves (Post-MVP / Phases 2-4)

- Document and range formatting
- Code actions and quick fixes
- Semantic tokens (syntax highlighting)
- Signature help
- Inlay hints
- Call hierarchy and type hierarchy
- Folding ranges
- Code lens
- Test Explorer integration (following C# extension patterns)
- Robust multi-root workspace handling

### 1.5 Explicit Non-Goals (Initial Phase)

- C# language support
- Razor / XAML / ASP.NET language layers
- OmniSharp protocol extensions
- Mixed C# and VB solutions (deferred to Phase 4)
- Editor-specific UI features beyond LSP
- Proprietary or closed-source components
- **Proprietary extension features** (project system UI, solution explorer enhancements, etc.)

---

## 2. C# Extension Research Findings

### 2.1 Architecture (Verified from Official Sources)

**"C# for Visual Studio Code"** (github.com/dotnet/vscode-csharp) consists of:

- **VS Code Extension (TypeScript)** - 97.5% of codebase
  - Extension activation and lifecycle
  - LSP client initialization
  - Configuration management
  - Debugger integration (wraps proprietary debugger)
  - Command palette commands
  - Status bar integration

- **Language Server (Roslyn-based)**
  - Roslyn workspace management
  - LSP protocol implementation
  - Language service adapters for `VB.NET`/C#
  - MSBuild project loading

- **Dependencies:**
  - Roslyn (open source) - semantic analysis and compilation
  - MSBuild integration for project loading
  - LSP server architecture
  - Optional: OmniSharp (legacy support, being phased out)

### 2.2 Key Differences for `VB.NET` Language Support

| Aspect | C# Extension | `VB.NET` Language Support |
|--------|------------|----------------|
| Language | C# (+ some VB support) | `VB.NET` only (focused) |
| Debugger | Proprietary Microsoft debugger | **netcoredbg (open source)** |
| Razor support | Yes | No (not applicable) |
| License | MIT (open source) | MIT (open source) |
| Development | Microsoft team | Community-driven, agentic |
| Dependencies | Standalone C# support | **Standalone `VB.NET` support** |

### 2.3 C# Extension Reference Repository

We will **fork and host the C# extension** (github.com/dotnet/vscode-csharp) as a reference implementation for:
- Architecture verification
- Behavior validation
- Pattern extraction
- TypeScript extension structure
- LSP integration patterns
- Debugger adapter patterns (adapted for netcoredbg)

### 2.4 Debugger Strategy

**netcoredbg** (github.com/DNAKode/netcoredbg, tracks Samsung):
- Open-source .NET debugger
- Implements Debug Adapter Protocol (DAP)
- Supports .NET Core / .NET 5+
- Cross-platform (Windows, macOS, Linux)
- **Replaces proprietary Microsoft debugger** used by C# extension

**Rule:** Never guess C# extension behavior - always verify against source (github.com/dotnet/vscode-csharp).

---

## 3. Target Environments

### 3.1 Editors
- VS Code and derivatives (Cursor, VSCodium, etc.)
- Any LSP-compatible client that supports stdio language servers
- **Validated with:** VS Code (automated) and Emacs (eglot) smoke coverage; Cursor/VSCodium planned

### 3.2 Runtimes
- **Server:** .NET 10.0+ (latest LTS, forward-compatible)
- **Roslyn and MSBuild components:** Compatible with .NET 10+
- **Requires:** .NET SDK installed (not bundled)

### 3.3 Operating Systems
- Windows (primary development)
- macOS
- Linux
- **Performance testing on all three platforms**

---

## 4. Architecture

**Architectural Principle:** Mirror C# extension architecture (github.com/dotnet/vscode-csharp) with `VB.NET` language services.

### 4.1 Component Structure

**1. VS Code Extension (TypeScript)**
- Extension activation and lifecycle
- LSP client initialization and management
- Command registration and UI integration
- Configuration and settings management
- Language client bootstrapping
- **Debugging integration with netcoredbg** (Debug Adapter Protocol)
- Status bar and output channel management

**2. Language Server (`vbnet-ls`, VB.NET/.NET)**
The language server is organized into five distinct layers:

#### **Protocol Layer**
- JSON-RPC framing and stdio transport
- LSP request/notification dispatch
- Capability negotiation and feature toggles
- UTF-16 position encoding (Roslyn compatibility)

#### **Server Core**
- Lifecycle management: `initialize`, `initialized`, `shutdown`, `exit`
- Request routing and handler registration
- Concurrency and cancellation policy
- Telemetry hooks and tracing infrastructure

#### **Workspace Layer**
- MSBuildWorkspace-based loading for `.sln` and `.vbproj`
- Document buffer management
- URI ↔ DocumentId mapping
- Incremental application of text changes
- Project and solution reload strategy with debouncing

#### **Language Services Layer (Roslyn Adapters)**
- Completion, hover, signature help
- Definition, references, implementations
- Rename and rename prepare
- Document and workspace symbols
- Formatting (document and range)
- Diagnostics and code actions
- Semantic tokens and classification

#### **Host / CLI**
- Process startup and command-line interface
- Logging configuration
- Tool configuration file discovery
- Health and self-check endpoints (LSP-friendly)

### 4.2 Request Flow (Conceptual)

1. User action in VS Code triggers LSP request
2. Extension (TypeScript) sends request to language server via stdio
3. Language server receives request via JSON-RPC
4. Server Core validates state and obtains workspace solution snapshot
5. Language Service adapter translates LSP parameters into Roslyn `VB.NET` API calls
6. Roslyn processes request on background thread with immutable snapshots
7. Results translated back into LSP types
8. Response sent via stdout to extension
9. Extension updates VS Code UI
10. `CancellationToken` honored end-to-end

### 4.3 Architecture Documentation

**Single Authoritative Document:** `docs/architecture.md`
- Maintained as single source of truth
- Updated with every architectural change
- Comprehensive enough for agentic (LLM) development
- Includes diagrams, component interactions, data flows
- No separate ADR files - all decisions documented inline

---

## 5. Reference Repositories

### 5.1 C# Extension (Primary Reference)
- **Repository:** github.com/dotnet/vscode-csharp
- **Fork for:** Architecture patterns, LSP/DAP integration, TypeScript structure
- **Verify:** Solution loading, diagnostics, completion, debugger integration

### 5.2 netcoredbg (Debugger)
- **Repository:** github.com/DNAKode/netcoredbg (tracks Samsung/netcoredbg)
- **Purpose:** Open-source .NET debugger (DAP-compliant)
- **Integration:** Replace Microsoft's proprietary debugger

### 5.3 DWSIM (Test Project)
- **Repository:** github.com/DanWBR/dwsim
- **Fork for:** Performance benchmarking, real-world validation
- **Size:** Large `VB.NET` codebase (100+ files)

---

## 6. Implementation Roadmap

### Phase 1 (MVP)
**Goal:** Core language server with essential features

- Workspace load for `.sln` and `.vbproj`
- Incremental text sync
- Diagnostics (push model)
- Completion
- Hover
- Definition and references
- Rename
- Basic symbols (document + workspace)

### Phase 2
**Goal:** Enhanced editing and debugging

- Formatting (document and range)
- Code actions (quick fixes + selected refactors)
- Semantic tokens
- Signature help
- Folding ranges
- **Debugging integration with netcoredbg** (DAP)

**Status (2026-01-13):** Formatting, code actions, semantic tokens, signature help, folding ranges, and netcoredbg debug harness coverage are implemented.

**Follow-ups (2026-01-18):**
- Bundle netcoredbg into all platform VSIX outputs using curated assets (done; see `src/extension/scripts/netcoredbg-assets.json`).
- macOS arm64 currently ships the x64 netcoredbg binary under Rosetta; native arm64 builds are blocked by coreclr Darwin support and remain an open investigation.

### Phase 3
**Goal:** Advanced navigation and productivity

**Status (2026-01-23):** Advanced navigation and pull diagnostics implemented. Extension UX backlog completed. Remaining focus: Test Explorer integration, performance tuning, and release polish.

**Completed (LSP features):**
- Call hierarchy
- Type hierarchy
- Type definition + implementation
- Document highlights, selection ranges, and document links
- Pull diagnostics (document + workspace)

**Extension UX backlog (completed, non-Razor/XAML/Blazor):**
- Solution/project picker command for multi-solution workspaces (active selection).
- "Show Logs" and "Toggle LSP Trace" commands for quick diagnostics capture.
- Restore commands (workspace or selected project) with clear status notifications.
- Activation for `.slnf` and `.slnx` to match solution filter usage in large repos.
- File nesting defaults for VB artifacts (e.g., `.Designer.vb`, `.g.vb`, `.g.i.vb`, `.generated.vb`, `.AssemblyInfo.vb`, `My*.vb`, `*.resx -> .Designer.vb`).
- "Reload Workspace" command to clear caches and re-open the solution.
- Attach-to-process picker command and richer debug configuration snippets (infer program via `projectPath`).
- "Run Tests in Context" and "Debug Tests in Context" commands (debug uses `dotnet test` for now).
- package.json settings aligned with documented options (e.g., `vbnet.logLevel`, `vbnet.msbuildPath`, `vbnet.maxMemoryMB`).
- SDK-style `net4x` guidance (warnings + docs) and MSBuild override wiring.

**Remaining / deferred in Phase 3:**
- Test Explorer integration (VS Code Testing API).
- Performance tuning and indexing improvements (ongoing).
- Document the exact Roslyn package versions used by the extension in README.md.
- Inlay hints (deferred pending stable Roslyn APIs).
- Multi-root workspace coverage (tests + UX).
- Smarter MSBuild selection warnings for x64/arm64 mismatches (macOS/WSL guidance).
- Workspace status surfacing (loaded project count, caps, and memory budget warnings).
- Debounced workspace reloads for file watcher bursts.
- Semantic tokens caching per document version.
- Debugger startup diagnostics (surface stderr + targeted hints).
- CI packaging validation: Linux netcoredbg bundle must include libdbgshim.so.

**Recently completed (extension settings wiring):**
- Respect `vbnet.enable` to skip activation or stop the server.
- Wire `vbnet.enableFormatting`, `vbnet.enableCodeActions`, and `vbnet.semanticTokens` toggles.
- Implement `vbnet.loadProjectsOnStart`, `vbnet.maxProjectCount`, and `vbnet.maxMemoryMB` behavior.

**Testing backlog (planned additions):**
- InitializationOptions unit coverage for feature toggles + workspace caps.
- Workspace reload tests for didChangeConfiguration (solutionPath/projectPaths/search paths).
- Large-solution/scale fixture to validate project caps + warnings.
- Multi-root workspace harness coverage (server chooses correct root per folder).
- Packaging tests for bundled debugger assets (libdbgshim present, executable bit set).

#### Extension Release Readiness (candidate for first stable release)

- Decide whether Test Explorer integration is required for 1.0 or deferred to 1.1.
- Validate cross-platform VSIX packaging (Windows/macOS/Linux) with bundled netcoredbg.
- Run DWSIM performance validation and document results.
- Ensure docs/configuration and README accurately reflect shipped settings.
- Verify extension commands + activation events in CI (manifest tests + VS Code harness).
- Confirm Roslyn package versions in README for transparency.

### Phase 4
**Goal:** Enterprise and complex scenarios

- Mixed-language solutions (serve VB documents, tolerate C# projects)
- Multi-root workspaces
- Advanced refactorings
- Workspace-wide operations
- Advanced debugging features (conditional breakpoints, watch expressions, etc.)

---

## 7. Repository Structure

**GitHub Organization:** DNA Kode (DNAKode)
**Repository Name:** `vbnet-lsp`

```
vbnet-lsp/
|-- _external/                          # Gitignored - reference repos + large inputs
|   |-- vscode-csharp/                  # C# extension (architecture reference)
|   |-- netcoredbg/                     # Samsung debugger (DAP reference)
|   |-- roslyn/                         # Roslyn source (optional)
|   `-- dwsim/                          # Large `VB.NET` project for testing
|-- test-explore/                  # Tracked - exploratory harnesses (logs excluded)
|   `-- clients/                        # VS Code / Emacs harnesses, etc.
|-- src/
|   |-- extension/                      # VS Code extension (TypeScript)
|   |   |-- src/
|   |   |   |-- extension.ts            # Extension activation
|   |   |   |-- languageClient.ts       # LSP client initialization
|   |   |   |-- commands/               # Command implementations
|   |   |   `-- features/               # VS Code UI integration
|   |   |-- package.json                # Extension manifest
|   |   `-- tsconfig.json
|   `-- VbNet.LanguageServer.Vb/        # Language server (VB.NET/.NET)
|       |-- Protocol/                   # LSP protocol layer
|       |-- Core/                       # Server core and routing
|       |-- Workspace/                  # MSBuild and workspace management
|       |-- Services/                   # Language service adapters
|       `-- Program.vb                  # Entry point
|-- test/
|   |-- VbNet.LanguageServer.Tests.Vb/  # Server unit tests (VB.NET)
|   |-- VbNet.Extension.Tests.Vb/       # Extension manifest tests (VB.NET)
|   `-- TestProjects/                   # Small test projects (tracked)
|       |-- SmallProject/               # ~5-10 files
|       `-- MediumProject/              # ~50 files, multi-project
|-- docs/
|   |-- architecture.md                 # SINGLE SOURCE OF TRUTH
|   |-- development.md                  # Dev setup and workflow
|   |-- configuration.md                # User configuration guide
|   |-- features.md                     # Feature support matrix
|   `-- research-plan.md                # External repo examination plans
|-- .github/
|   `-- workflows/
|       |-- ci.yml                      # Build, test, lint
|       |-- integration.yml             # DWSIM integration tests
|       |-- emacs-lsp.yml               # Emacs lsp-mode validation (Linux)
|       |-- performance.yml             # Nightly performance tests
|       `-- release.yml                 # Publish to marketplaces
|-- scripts/
|   |-- bootstrap.sh                    # Initial setup script
|   |-- test-dwsim.sh                   # DWSIM test runner
|   `-- package-extension.sh            # Package VSIX
|-- README.md
|-- LICENSE                             # MIT
|-- CONTRIBUTING.md                     # Issue-focused contribution
|-- CODE_OF_CONDUCT.md
`-- vbnet-lsp.sln                       # Main solution file
```
**Note**: _external/ is gitignored and must be set up locally. 	ests-exploratory/ is tracked, but its logs and downloaded runtimes are excluded.
See `docs/development.md` Section 2.1 for setup instructions.

---

## 8. Dependencies

**Language Server (VB.NET/.NET):**
- `Microsoft.CodeAnalysis.VisualBasic.Workspaces` (Roslyn)
- `Microsoft.Build.Locator` (MSBuild discovery)
- `Microsoft.CodeAnalysis.Workspaces.MSBuild`
- JSON-RPC library (StreamJsonRpc or custom)
- Logging framework (Microsoft.Extensions.Logging)

**VS Code Extension (TypeScript):**
- `vscode` (VS Code extension API)
- `vscode-languageclient` (LSP client)
- `vscode-debugadapter` (DAP integration)

**Debugger:**
- netcoredbg (bundled with extension in Phase 2)
- Implements Debug Adapter Protocol (DAP)

---

## 9. Success Criteria

### 9.1 Performance Targets (MVP)

| Metric | Target | Measurement |
|--------|--------|-------------|
| Startup time (medium solution, ~20 projects) | < 5 seconds | Time from launch to initialized |
| First diagnostic (after file open) | < 500ms | Time from didOpen to publishDiagnostics |
| Completion latency | < 100ms (p95) | Time from completion request to response |
| Memory usage (medium solution) | < 500 MB | Working set after initial load |
| Memory growth over 8 hours | < 20% | Long-running stability test |

### 9.2 Quality Targets (MVP)

| Metric | Target |
|--------|--------|
| Code coverage (unit tests) | > 70% |
| Integration test pass rate | 100% |
| Critical bugs (P0/P1) | 0 before release |
| Cross-platform test pass rate | 100% on Windows, macOS, Linux |
| Multi-editor validation | VS Code (automated) + Emacs (eglot) smoke; Cursor/VSCodium planned |

### 9.3 Multi-Editor Testing

**Editors to Validate**:
- **VS Code** - Primary development environment (Windows, macOS, Linux)
- **Cursor** - Agentic coding focus
- **Emacs (lsp-mode)** - LSP protocol compliance validation

**Automated Testing**:
- GitHub Actions CI on Linux (Ubuntu latest) with Emacs lsp-mode
- Automated LSP feature tests via Emacs batch mode
- Validates LSP protocol compliance beyond VS Code specifics

---

## 10. Documentation Strategy

**Philosophy:** Minimal documentation set (5 documents max), optimized for LLM consumption.

### Core Documents:

1. **README.md** - Project overview, quick start, installation
2. **docs/architecture.md** - Single source of truth (replaces ADRs)
3. **docs/development.md** - Dev environment, build, test, CI/CD
4. **docs/configuration.md** - Settings, troubleshooting, performance tuning
5. **docs/features.md** - LSP feature matrix, roadmap, limitations

**Maintenance:**
- Update with every architectural change
- LLM-friendly structure (clear headers, complete context)
- Version controlled with code

---

## 11. Key Architectural Decisions

### 11.1 Follow C# Extension Architecture
- C# extension is open source (MIT)
- Provides core language support without proprietary features
- We mirror the language support foundation

### 11.2 Open-Source Debugger (netcoredbg)
- Replaces proprietary Microsoft debugger
- Implements Debug Adapter Protocol (DAP)
- Bundled with extension for better UX
 - **Cross-platform bundling implemented**: platform VSIX packages include `.debugger/netcoredbg` and `LICENSE.netcoredbg`.
 - **Curated assets**: `src/extension/scripts/netcoredbg-assets.json` maps each VSIX target to a vetted binary.
 - **WSL2/Linux test plan (hosted or local)**:
   - Provision Ubuntu (WSL2 or hosted VM) with .NET SDK + Node.js.
   - Run `npm run bundle-debugger -- --target linux-x64` before the VS Code harness.
   - Build VSIX for `linux-x64` and install into VS Code Remote - WSL.
   - Run `test-explore/clients/vscode` with `EXTENSION_VSIX` set to the Linux VSIX and verify debug harness (including inferred program path).
   - Capture logs and DAP traces for parity with Windows runs.

### 11.3 `VB.NET` Only
- Focused scope (no mixed-language complexity)
- Simpler architecture
- Better `VB.NET`-specific optimizations

### 11.4 Agentic Development
- Designed for minimal manual intervention
- Clear task sequencing
- Comprehensive, LLM-friendly documentation

---

## 12. Implementation Status

### Phase 0: Bootstrap - ✅ Complete

1. ✅ Create comprehensive project plan
2. ✅ Create GitHub organization (DNAKode)
3. ✅ Initialize repository with structure
4. ✅ Fork reference repositories
5. ✅ Set up development environment
6. ✅ Create initial documentation
7. ✅ Verify C# extension behaviors

### Phase 1: MVP - ✅ Complete

**All core language services implemented and tested (as of 2026-01-23: 156 server tests + 7 extension manifest tests passing)**:

1. ✅ Protocol Layer (JSON-RPC, LSP types, transports)
2. ✅ Server Core (lifecycle, routing, state management)
3. ✅ Workspace Layer (WorkspaceManager, DocumentManager)
4. ✅ Language Services:
   - ✅ DiagnosticsService (real-time with debouncing)
   - ✅ CompletionService (with resolve)
   - ✅ HoverService (with XML documentation)
   - ✅ DefinitionService (Go to Definition)
   - ✅ ReferencesService (Find All References)
   - ✅ RenameService (with prepare)
   - ✅ SymbolsService (Document + Workspace symbols)

### Phase 1 Follow-ups (Production Quality)

**Remaining items to align with C# extension/Roslyn behavior**:
- LSP request cancellation (`$/cancelRequest`) should cancel in-flight handlers consistently (verify end-to-end).
- Completion resolve uses an approximate position; align with Roslyn completion item resolution.
- Completion items should apply Roslyn text changes (`TextEdit`/`AdditionalTextEdits`) instead of plain insert text.

**Completed follow-ups**:
- Honor `vbnet.diagnostics.enable` and `vbnet.completion.enable` settings in client/server behavior.
- Handle `workspace/didChangeWatchedFiles` to keep the workspace in sync with external edits.

### Phase 2: Enhanced Editing - Complete

**Implemented features**:
- Code formatting (document and range)
- Code actions (baseline source actions)
- Semantic tokens
- Signature help
- Folding ranges
- Debugging integration (netcoredbg)

**Remaining items**:
- Diagnostic quick fixes (Roslyn analyzer fixes surfaced as code actions)

---

## 13. Verification Strategy

**Never guess - always verify:**

- **C# extension source code** (github.com/dotnet/vscode-csharp) - primary reference
- **netcoredbg source and docs** (github.com/DNAKode/netcoredbg) - debugger integration
- Official Microsoft Roslyn and LSP documentation
- Empirical testing and behavioral comparison
- Document all findings in docs/architecture.md

---

**Plan Version:** 4.2 (Phase 3 in progress)
**Last Updated:** 2026-01-23
**Status:** Phase 3 in progress
**Key Change:** Extension UX backlog completed; test explorer + performance + release polish next
**License:** MIT (fully open source)




