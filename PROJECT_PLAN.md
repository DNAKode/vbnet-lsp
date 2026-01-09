# VB.NET Language Support Project Plan

## Project Overview

**Project Name:** VB.NET Language Support
**GitHub Organization:** TBD (new organization to be created)
**Repository:** vbnet-lsp
**Display Name:** VB.NET Language Support
**VS Code Extension Name:** VB.NET Language Support
**Language Server Command:** vbnet-ls (internal component)
**Root Namespace:** VbNet.LanguageServer
**Target Framework:** .NET 10.0+ (latest LTS)
**Distribution:** VS Code Marketplace, Open VSX Registry
**License:** MIT (fully open source)

### Summary

An open-source, Roslyn-backed extension providing first-class VB.NET language support for VS Code and compatible editors (Cursor, VSCodium, etc.). This project **directly mirrors the "C# for Visual Studio Code" extension architecture** (github.com/dotnet/vscode-csharp), which is the open-source foundation for C# language support. The implementation follows modern Roslyn LSP patterns and is designed as lasting infrastructure for VB.NET development.

### Key Differentiation

- **100% open source** under MIT license (matching the C# extension model)
- **VB.NET focused** (mirroring how C# extension handles C#)
- **Open-source debugger** using Samsung's netcoredbg instead of proprietary debugger
- **No proprietary components** (unlike C# Dev Kit which adds closed-source features)

---

## 1. Goals and Scope

### 1.1 Primary Goal

**Provide definitive VB.NET language support for VS Code and compatible editors**, achieving feature parity with the **"C# for Visual Studio Code" extension** while remaining fully open source.

### 1.2 Project Philosophy

- **No guessing:** Always verify C# extension behavior through source code inspection (github.com/dotnet/vscode-csharp)
- **Agentic-first development:** Designed for minimal manual intervention, with clear task sequencing
- **Lasting infrastructure:** Build for long-term VB.NET community support
- **Documentation as code:** Maintain comprehensive, LLM-friendly documentation throughout
- **Open-source debugger:** Use Samsung netcoredbg instead of proprietary Microsoft debugger

### 1.3 Must-Haves (MVP / Phase 1)

- **Correct VB.NET semantics via Roslyn** (no custom parser)
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
- **Performance validated against DWSIM** (large real-world VB.NET codebase)
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
  - Language service adapters for VB.NET/C#
  - MSBuild project loading

- **Dependencies:**
  - Roslyn (open source) - semantic analysis and compilation
  - MSBuild integration for project loading
  - LSP server architecture
  - Optional: OmniSharp (legacy support, being phased out)

### 2.2 Key Differences for VB.NET Language Support

| Aspect | C# Extension | VB.NET Language Support |
|--------|------------|----------------|
| Language | C# (+ some VB support) | VB.NET only (focused) |
| Debugger | Proprietary Microsoft debugger | **Samsung netcoredbg (open source)** |
| Razor support | Yes | No (not applicable) |
| License | MIT (open source) | MIT (open source) |
| Development | Microsoft team | Community-driven, agentic |
| Dependencies | Standalone C# support | **Standalone VB.NET support** |

### 2.3 C# Extension Reference Repository

We will **fork and host the C# extension** (github.com/dotnet/vscode-csharp) as a reference implementation for:
- Architecture verification
- Behavior validation
- Pattern extraction
- TypeScript extension structure
- LSP integration patterns
- Debugger adapter patterns (adapted for netcoredbg)

### 2.4 Debugger Strategy

**Samsung netcoredbg** (github.com/Samsung/netcoredbg):
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
- **Validated with:** VS Code, Cursor (agentic coding focus), Emacs (lsp-mode)

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

**Architectural Principle:** Mirror C# extension architecture (github.com/dotnet/vscode-csharp) with VB.NET language services.

### 4.1 Component Structure

**1. VS Code Extension (TypeScript)**
- Extension activation and lifecycle
- LSP client initialization and management
- Command registration and UI integration
- Configuration and settings management
- Language client bootstrapping
- **Debugging integration with netcoredbg** (Debug Adapter Protocol)
- Status bar and output channel management

**2. Language Server (`vbnet-ls`, C#/.NET)**
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
5. Language Service adapter translates LSP parameters into Roslyn VB.NET API calls
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

### 5.2 Samsung netcoredbg (Debugger)
- **Repository:** github.com/Samsung/netcoredbg
- **Purpose:** Open-source .NET debugger (DAP-compliant)
- **Integration:** Replace Microsoft's proprietary debugger

### 5.3 DWSIM (Test Project)
- **Repository:** github.com/DanWBR/dwsim
- **Fork for:** Performance benchmarking, real-world validation
- **Size:** Large VB.NET codebase (100+ files)

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

### Phase 3
**Goal:** Advanced navigation and productivity

- Inlay hints
- Call hierarchy
- Type hierarchy
- Code lens (if valuable)
- Test Explorer integration
- Performance tuning and indexing improvements

### Phase 4
**Goal:** Enterprise and complex scenarios

- Mixed-language solutions (serve VB documents, tolerate C# projects)
- Multi-root workspaces
- Advanced refactorings
- Workspace-wide operations
- Advanced debugging features (conditional breakpoints, watch expressions, etc.)

---

## 7. Repository Structure

**GitHub Organization:** TBD (new organization)
**Repository Name:** `vbnet-lsp`

```
vbnet-lsp/
├── src/
│   ├── extension/                      # VS Code extension (TypeScript)
│   │   ├── src/
│   │   │   ├── extension.ts            # Extension activation
│   │   │   ├── languageClient.ts       # LSP client initialization
│   │   │   ├── commands/               # Command implementations
│   │   │   └── features/               # VS Code UI integration
│   │   ├── package.json                # Extension manifest
│   │   └── tsconfig.json
│   │
│   ├── VbNet.LanguageServer/           # Language server (C#/.NET)
│   │   ├── Protocol/                   # LSP protocol layer
│   │   ├── Core/                       # Server core and routing
│   │   ├── Workspace/                  # MSBuild and workspace management
│   │   ├── Services/                   # Language service adapters
│   │   └── Program.cs                  # Entry point
│   │
│   └── VbNet.LanguageServer.Protocol/  # Shared LSP types (optional)
│
├── test/
│   ├── extension.test/                 # Extension unit tests (TypeScript)
│   ├── VbNet.LanguageServer.Tests/     # Server unit tests (C#)
│   ├── VbNet.IntegrationTests/         # End-to-end tests
│   └── TestProjects/
│       ├── SmallProject/               # ~5-10 files
│       ├── MediumProject/              # ~50 files, multi-project
│       └── dwsim/                      # Git submodule to DWSIM fork
│
├── docs/
│   ├── README.md                       # Documentation index
│   ├── architecture.md                 # SINGLE SOURCE OF TRUTH
│   ├── development.md                  # Dev setup and workflow
│   ├── configuration.md                # User configuration guide
│   └── features.md                     # Feature support matrix
│
├── .github/
│   └── workflows/
│       ├── ci.yml                      # Build, test, lint
│       ├── integration.yml             # DWSIM integration tests
│       ├── emacs-lsp.yml               # Emacs lsp-mode validation (Linux)
│       ├── performance.yml             # Nightly performance tests
│       └── release.yml                 # Publish to marketplaces
│
├── scripts/
│   ├── bootstrap.sh                    # Initial setup script
│   ├── test-dwsim.sh                   # DWSIM test runner
│   └── package-extension.sh            # Package VSIX
│
├── README.md
├── LICENSE                             # MIT
├── CONTRIBUTING.md                     # Issue-focused contribution
├── CODE_OF_CONDUCT.md
└── vbnet-lsp.sln                       # Main solution file
```

---

## 8. Dependencies

**Language Server (C#/.NET):**
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
- Samsung netcoredbg (bundled with extension in Phase 2)
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
| Multi-editor validation | VS Code, Cursor, Emacs (lsp-mode) |

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

### 11.3 VB.NET Only
- Focused scope (no mixed-language complexity)
- Simpler architecture
- Better VB.NET-specific optimizations

### 11.4 Agentic Development
- Designed for minimal manual intervention
- Clear task sequencing
- Comprehensive, LLM-friendly documentation

---

## 12. Next Steps (Phase 0: Bootstrap)

### Immediate Actions:

1. ✅ Create comprehensive project plan
2. ⏳ Create GitHub organization (choose name)
3. ⏳ Initialize repository with structure above
4. ⏳ Fork reference repositories:
   - github.com/dotnet/vscode-csharp (C# extension)
   - github.com/DanWBR/dwsim (test project)
   - Clone github.com/Samsung/netcoredbg (debugger)
5. ⏳ Set up development environment:
   - .NET 10 SDK
   - Node.js/TypeScript tooling
   - VS Code debugging configuration
   - Install netcoredbg for testing
6. ⏳ Create initial documentation:
   - README.md
   - docs/architecture.md skeleton
   - docs/development.md
   - docs/debugger.md (netcoredbg integration)
7. ⏳ Verify C# extension behaviors:
   - Solution selection logic
   - Diagnostics update strategy
   - EditorConfig integration
   - Debugger integration patterns (DAP)
   - Document all findings in docs/architecture.md

---

## 13. Verification Strategy

**Never guess - always verify:**

- **C# extension source code** (github.com/dotnet/vscode-csharp) - primary reference
- **netcoredbg source and docs** (github.com/Samsung/netcoredbg) - debugger integration
- Official Microsoft Roslyn and LSP documentation
- Empirical testing and behavioral comparison
- Document all findings in docs/architecture.md

---

**Plan Version:** 3.0 (Final - C# Extension Focus)
**Last Updated:** 2026-01-09
**Status:** Ready for implementation
**Key Change:** Shifted from C# Dev Kit to C# extension as reference model + added netcoredbg
**License:** MIT (fully open source)
