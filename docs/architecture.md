# VB.NET Language Support Architecture

**Single Source of Truth for Architectural Decisions**

Version: 1.1
Last Updated: 2026-01-10
Status: Living Document

## Document Purpose

This document serves as the **single authoritative source** for all architectural decisions, patterns, and design rationale for the VB.NET Language Support project. It replaces traditional ADR (Architecture Decision Record) files by maintaining all decisions inline with full context.

**Update Policy**: This document MUST be updated with every architectural change or decision.

---

## Table of Contents

1. [System Overview](#system-overview)
2. [Architecture Principles](#architecture-principles)
3. [Component Architecture](#component-architecture)
4. [Protocol and Transport](#protocol-and-transport)
5. [Language Server Layers](#language-server-layers)
6. [Request Flow](#request-flow)
7. [Workspace and Project Loading](#workspace-and-project-loading)
8. [LSP Feature Implementation](#lsp-feature-implementation)
9. [Concurrency and Threading](#concurrency-and-threading)
10. [Error Handling and Resilience](#error-handling-and-resilience)
11. [Debugging Integration](#debugging-integration)
12. [Configuration System](#configuration-system)
13. [Testing Strategy](#testing-strategy)
14. [Architectural Decisions](#architectural-decisions)
15. [Reference Implementations](#reference-implementations)

---

## 1. System Overview

### 1.1 High-Level Architecture

VB.NET Language Support consists of two primary components:

```
┌─────────────────────────────────────┐
│   VS Code Extension (TypeScript)    │
│  ┌──────────────────────────────┐   │
│  │   LSP Client                 │   │
│  │   - Extension activation     │   │
│  │   - Configuration management │   │
│  │   - Command registration     │   │
│  │   - UI integration           │   │
│  └────────────┬─────────────────┘   │
└───────────────┼─────────────────────┘
                │ Named Pipes / stdio (JSON-RPC)
┌───────────────┼─────────────────────┐
│               ▼                     │
│   Language Server (C#/.NET)         │
│  ┌──────────────────────────────┐   │
│  │   Protocol Layer             │   │
│  │   Server Core                │   │
│  │   Workspace Layer            │   │
│  │   Language Services          │   │
│  │   Host / CLI                 │   │
│  └──────────────────────────────┘   │
│                                     │
│   Based on Roslyn Workspace API     │
└─────────────────────────────────────┘
```

### 1.2 Design Philosophy

**Reference Model**: This architecture directly mirrors the ["C# for Visual Studio Code" extension](https://github.com/dotnet/vscode-csharp), which provides core C# language support.

**Key Principles**:
- **Never guess** - Always verify C# extension behavior through source code inspection
- **Roslyn-first** - Use Roslyn as the single source of truth for VB.NET semantics
- **LSP compliance** - Strict adherence to Language Server Protocol specification
- **Fail gracefully** - Partial functionality is better than complete failure
- **Performance by design** - Immutable snapshots, async-first, cancellation-aware

---

## 2. Architecture Principles

### 2.1 Core Principles

1. **Single Source of Truth: Roslyn**
   - No custom parsing or semantic analysis
   - All language intelligence comes from Roslyn Workspace API
   - MSBuildWorkspace for project loading

2. **Protocol Purity**
   - LSP-only communication (no custom extensions in MVP)
   - Named pipes (primary) and stdio (secondary) transport support
   - JSON-RPC 2.0 message framing

3. **Performance First**
   - Immutable Roslyn snapshots for thread safety
   - Background thread execution for Roslyn operations
   - CancellationToken propagation throughout
   - Debounced diagnostics and incremental updates

4. **Graceful Degradation**
   - Partial functionality when project load fails
   - Continue serving open documents even without full solution context
   - Clear error messages without crashing

5. **Agentic-Friendly**
   - Clear layer separation for LLM understanding
   - Comprehensive inline documentation
   - Predictable patterns and conventions

### 2.2 Architectural Constraints

- **No OmniSharp protocol extensions** (Phase 1)
- **VB.NET only** (mixed-language deferred to Phase 4)
- **Named pipes primary, stdio secondary** (no TCP or other transports)
- **.NET 10.0+ target framework** for language server
- **UTF-16 position encoding** (Roslyn compatibility)

---

## 3. Component Architecture

### 3.1 VS Code Extension (TypeScript)

**Location**: `src/extension/`

**Responsibilities**:
- Extension activation and lifecycle management
- Launch and manage language server process
- LSP client initialization and communication
- VS Code command registration
- Configuration management
- Debug Adapter Protocol (DAP) integration with netcoredbg
- Status bar and output channel management

**Key Files** (to be implemented):
- `extension.ts` - Extension activation entry point
- `languageClient.ts` - LSP client setup and lifecycle
- `commands/` - VS Code command implementations
- `features/` - VS Code UI integration features

**Dependencies**:
- `vscode` - VS Code extension API
- `vscode-languageclient` - LSP client library
- `vscode-debugadapter` - DAP integration (Phase 2)

### 3.2 Language Server (C#/.NET)

**Location**: `src/VbNet.LanguageServer/`

**Canonical Name**: `VbNet.LanguageServer` (use consistently across all docs, build scripts, and code)

**Target Framework**: .NET 10.0+

**Architecture**: Five-layer design (detailed in Section 5)

---

## 4. Protocol and Transport

### 4.1 Transport Layer

**Primary Transport**: Named Pipes (following C# extension pattern)
- Server spawns and outputs pipe name as JSON to stdout: `{"pipeName":"..."}`
- Client connects to named pipe using platform-appropriate API
- Bidirectional communication over the pipe
- Must work correctly on Windows, macOS, and Linux

**Secondary Transport**: stdio (standard input/output)
- **stdin**: JSON-RPC requests and notifications from client
- **stdout**: JSON-RPC responses and notifications to client
- **stderr**: Logging and diagnostic output only

**Transport Abstraction**:
- `ITransport` interface for transport-agnostic communication
- `NamedPipeTransport` implementation (primary)
- `StdioTransport` implementation (secondary)
- Transport selection via CLI argument or configuration

**Rationale**:
- Named pipes match C# extension behavior for full parity
- stdio provides maximum compatibility as fallback
- Abstraction layer enables clean support for both
- Named pipes historically problematic cross-platform - focused engineering required

### 4.2 Message Format

**Protocol**: JSON-RPC 2.0

**Message Structure**:
```
Content-Length: {byte count}\r\n
\r\n
{JSON-RPC message}
```

**Position Encoding**: UTF-16 (Roslyn default)

**Rationale**: UTF-16 encoding ensures compatibility with Roslyn's internal position tracking and avoids conversion overhead.

### 4.3 Capability Negotiation

**Strategy**: Advertise only implemented and tested features

**Implementation**:
- Dynamic capability building based on feature flags
- Conservative initial capabilities (MVP features only)
- Expand capabilities in later phases

---

## 5. Language Server Layers

The language server is organized into five distinct layers, each with clear responsibilities:

### 5.1 Layer 1: Protocol Layer

**Location**: `src/VbNet.LanguageServer/Protocol/`

**Responsibilities**:
- JSON-RPC message framing and parsing
- stdio transport management
- LSP request/notification dispatch
- Message validation and error handling

**Key Components** (to be implemented):
- `JsonRpcTransport.cs` - stdio communication
- `LspMessageHandler.cs` - Message dispatch
- `LspTypes.cs` - LSP type definitions

### 5.2 Layer 2: Server Core

**Location**: `src/VbNet.LanguageServer/Core/`

**Responsibilities**:
- Lifecycle management (`initialize`, `initialized`, `shutdown`, `exit`)
- Request routing to appropriate handlers
- Concurrency management and thread safety
- CancellationToken propagation
- Telemetry hooks (optional, off by default)

**Key Components** (to be implemented):
- `LanguageServer.cs` - Main server class
- `RequestRouter.cs` - Route requests to handlers
- `ServerLifecycle.cs` - Lifecycle management

### 5.3 Layer 3: Workspace Layer

**Location**: `src/VbNet.LanguageServer/Workspace/`

**Responsibilities**:
- MSBuildWorkspace-based solution/project loading
- Document buffer management and synchronization
- URI ↔ Roslyn DocumentId mapping
- Incremental text change application
- Project reload with debouncing
- File system watching

**Key Components** (to be implemented):
- `WorkspaceManager.cs` - MSBuildWorkspace lifecycle
- `DocumentManager.cs` - Document tracking and sync
- `ProjectLoader.cs` - Solution/project loading logic
- `FileSystemWatcher.cs` - File change detection

**Design Pattern**: Immutable Roslyn Solution snapshots for thread safety

### 5.4 Layer 4: Language Services Layer

**Location**: `src/VbNet.LanguageServer/Services/`

**Responsibilities**:
- LSP feature implementations using Roslyn APIs
- Translation between LSP types and Roslyn types
- Feature-specific business logic

**Services** (to be implemented):

**Phase 1 (MVP)**:
- `DiagnosticsService.cs` - Publish diagnostics
- `CompletionService.cs` - IntelliSense completion
- `HoverService.cs` - Symbol hover information
- `DefinitionService.cs` - Go to definition
- `ReferencesService.cs` - Find references
- `RenameService.cs` - Symbol rename
- `SymbolsService.cs` - Document and workspace symbols

**Phase 2**:
- `FormattingService.cs` - Document formatting
- `CodeActionsService.cs` - Quick fixes and refactorings
- `SemanticTokensService.cs` - Enhanced syntax highlighting
- `SignatureHelpService.cs` - Parameter hints

**Phase 3**:
- `InlayHintsService.cs` - Inline type hints
- `CallHierarchyService.cs` - Call hierarchy navigation
- `TypeHierarchyService.cs` - Type hierarchy navigation

### 5.5 Layer 5: Host / CLI

**Location**: `src/VbNet.LanguageServer/Program.cs`

**Responsibilities**:
- Process entry point
- Command-line argument parsing
- Logging configuration
- Server startup and shutdown

---

## 6. Request Flow

### 6.1 Typical Request Flow

```
1. User types in VS Code
   ↓
2. VS Code extension sends LSP textDocument/didChange
   ↓
3. Protocol Layer receives and parses JSON-RPC message
   ↓
4. Server Core routes to DocumentManager
   ↓
5. Workspace Layer applies incremental text changes
   ↓
6. Diagnostics debounce timer starts (300ms default)
   ↓
7. Timer expires, DiagnosticsService queries Roslyn
   ↓
8. Roslyn analyzes on background thread
   ↓
9. Results translated to LSP Diagnostic[] format
   ↓
10. Protocol Layer sends textDocument/publishDiagnostics
    ↓
11. VS Code displays errors in Problems panel
```

### 6.2 Cancellation Flow

Every request handler must honor `CancellationToken`:

```csharp
public async Task<CompletionList> GetCompletionAsync(
    CompletionParams params,
    CancellationToken cancellationToken)
{
    // Get Roslyn document
    var document = GetDocument(params.TextDocument.Uri);

    // All Roslyn calls must pass the token
    var semanticModel = await document
        .GetSemanticModelAsync(cancellationToken);

    // Check for cancellation before expensive operations
    cancellationToken.ThrowIfCancellationRequested();

    // Translate results
    return TranslateToLspCompletionList(items);
}
```

---

## 7. Workspace and Project Loading

### 7.1 Solution Discovery

**Strategy**:
1. Search workspace root for `.sln` files
2. If multiple found, use nearest to root
3. If no `.sln`, search for `.vbproj` files
4. Allow client to override via configuration

**Implementation** (verified from C# extension):
- Pattern matching: `**/*.sln`, `**/*.vbproj`
- Depth-first search from workspace root
- Cache discovery results until file system changes

### 7.2 MSBuildWorkspace Loading

```csharp
using Microsoft.CodeAnalysis.MSBuild;

// Initialize MSBuildWorkspace
var workspace = MSBuildWorkspace.Create();

// Register for diagnostics
workspace.WorkspaceFailed += OnWorkspaceFailed;

// Load solution
var solution = await workspace.OpenSolutionAsync(
    solutionPath,
    cancellationToken: ct);

// Filter VB.NET projects only (Phase 1)
var vbProjects = solution.Projects
    .Where(p => p.Language == LanguageNames.VisualBasic);
```

### 7.3 Reload Strategy

**Triggers**:
- `.sln` file changes
- `.vbproj` file changes
- `Directory.Build.props` changes
- `Directory.Build.targets` changes

**Implementation**:
- File system watcher on relevant patterns
- Debounced reload (1000ms default)
- Preserve open document buffers across reloads
- Incremental reload where possible

---

## 8. LSP Feature Implementation

### 8.1 Text Synchronization

**Implementation**: Incremental sync (`TextDocumentSyncKind.Incremental`)

**Handlers**:
- `textDocument/didOpen` - Create document buffer
- `textDocument/didChange` - Apply incremental edits
- `textDocument/didClose` - Remove buffer
- `textDocument/didSave` - Optional trigger for diagnostics

**Roslyn Integration**:
```csharp
// Apply incremental change to Roslyn document
var sourceText = await document.GetTextAsync(ct);
var changes = contentChanges.Select(change =>
    new TextChange(
        GetTextSpan(change.Range, sourceText),
        change.Text));

var newText = sourceText.WithChanges(changes);
var newDocument = document.WithText(newText);
```

### 8.2 Diagnostics

**Strategy**: Push model with debouncing

**Implementation**:
- Debounce timer: 300ms default (configurable)
- Trigger on `didOpen`, `didChange`, `didSave`
- Query Roslyn for all diagnostics
- Filter by severity and suppression rules
- Publish via `textDocument/publishDiagnostics`

**Roslyn Integration**:
```csharp
var compilation = await project.GetCompilationAsync(ct);
var diagnostics = compilation.GetDiagnostics(ct);

// Include analyzer diagnostics
var analyzers = project.AnalyzerReferences
    .SelectMany(r => r.GetAnalyzers(LanguageNames.VisualBasic));
var analysisResults = await compilation
    .WithAnalyzers(analyzers.ToImmutableArray())
    .GetAllDiagnosticsAsync(ct);
```

### 8.3 Completion

**Features**:
- Keywords, symbols, members, locals
- Commit characters (`.`, `(`, `<`, etc.)
- Optional `completionItem/resolve` for expensive details

**Roslyn Integration**:
```csharp
var completionService = CompletionService.GetService(document);
var completionList = await completionService
    .GetCompletionsAsync(document, position, ct);

// Translate to LSP CompletionItem
var items = completionList.Items.Select(item =>
    new CompletionItem
    {
        Label = item.DisplayText,
        Kind = TranslateCompletionKind(item.Tags),
        InsertText = item.DisplayText,
        // Defer expensive details to resolve
        Data = SerializeCompletionData(item)
    });
```

### 8.4 Other Features

See [PROJECT_PLAN.md Section 5](../PROJECT_PLAN.md#5-lsp-features-detailed) for detailed specifications of all LSP features.

---

## 9. Concurrency and Threading

### 9.1 Threading Model

**Principle**: Async-first, no blocking calls

**Pattern**:
- LSP request handlers are `async Task<T>`
- All Roslyn calls use `Async` variants
- CancellationToken passed to all operations
- No `Task.Wait()` or `Task.Result` usage

### 9.2 Roslyn Snapshot Immutability

**Key Insight**: Roslyn Solution/Document objects are immutable snapshots

**Benefits**:
- Thread-safe without locking
- Multiple concurrent operations on same snapshot
- No race conditions from document updates

**Pattern**:
```csharp
// Get current snapshot
var solution = workspace.CurrentSolution;
var document = solution.GetDocument(documentId);

// Query on background thread
var result = await Task.Run(async () =>
{
    var semanticModel = await document
        .GetSemanticModelAsync(ct);
    return AnalyzeSymbols(semanticModel, ct);
}, ct);
```

### 9.3 Workspace Locks

**Minimal Locking Strategy**:
- Only lock during document buffer updates
- Use `SemaphoreSlim` for async-compatible locking
- Short critical sections only

---

## 10. Error Handling and Resilience

### 10.1 Failure Modes

**Project Load Failure**:
- **Behavior**: Log error, continue with partial workspace
- **User Experience**: Show notification, suggest troubleshooting
- **Partial Functionality**: Serve open documents without full project context

**Missing SDK**:
- **Detection**: MSBuildWorkspace.WorkspaceFailed event
- **Behavior**: Log diagnostic, suggest SDK installation
- **Fallback**: Basic syntax-only features

**Analyzer Exception**:
- **Behavior**: Catch, log, continue without that analyzer
- **User Experience**: No crash, but missing diagnostics from failed analyzer

**Invalid LSP Parameters**:
- **Behavior**: Return LSP error response
- **Logging**: Log validation failure details

### 10.2 Error Logging

**Levels**:
- `Trace`: Request/response details (disabled by default)
- `Debug`: Internal state changes
- `Info`: Lifecycle events, project load
- `Warn`: Recoverable errors, fallback behavior
- `Error`: Unrecoverable errors, critical failures

**Output**: stderr only (never stdout)

---

## 11. Debugging Integration

### 11.1 Debugger: Samsung netcoredbg

**Repository**: https://github.com/Samsung/netcoredbg

**Integration Point**: Debug Adapter Protocol (DAP)

**Architecture**:
```
VS Code Debug UI
       ↓
  DAP (JSON-RPC)
       ↓
Extension Debug Adapter
       ↓
  netcoredbg Process
       ↓
  .NET Runtime
```

**Phase**: Phase 2 (post-MVP)

### 11.2 Why netcoredbg?

**Rationale**:
- **Open source** (MIT license) - aligns with project philosophy
- **DAP-compliant** - standard protocol, no proprietary extensions
- **Cross-platform** - Windows, macOS, Linux
- **Active maintenance** - Samsung continues development
- **Replaces proprietary** - Microsoft's debugger is closed-source

### 11.3 Debug Features (Phase 2)

- Breakpoints (line, conditional)
- Step in/out/over
- Variable inspection
- Call stack navigation
- Watch expressions
- Exception handling

---

## 12. Configuration System

### 12.1 Configuration Sources

**Priority Order** (highest to lowest):
1. LSP client settings (VS Code workspace settings)
2. LSP initialization options
3. Server defaults

**No config file in MVP** - add in Phase 2+ if needed

### 12.2 Key Settings

See [docs/configuration.md](configuration.md) for user-facing documentation.

**Internal Implementation**:
- Settings class with strongly-typed properties
- Validation on initialization
- Dynamic reload via `workspace/didChangeConfiguration`

---

## 13. Testing Strategy

### 13.1 Test Pyramid

```
     /\
    /  \    E2E Tests (DWSIM validation)
   /────\
  /      \  Integration Tests (LSP sequences)
 /────────\
/          \ Unit Tests (Roslyn adapters, protocol)
────────────
```

### 13.2 Test Projects

1. **SmallProject** (~10 files)
   - Fast iteration
   - Language feature coverage
   - Unit test validation

2. **MediumProject** (~50 files)
   - Multi-project solution
   - Cross-project references
   - Integration test baseline

3. **DWSIM** (100+ files)
   - Real-world complexity
   - Performance benchmarking
   - Regression detection

**Location**: `test/TestProjects/`

### 13.3 Test Coverage Targets

- **Unit tests**: >70% code coverage
- **Integration tests**: All MVP features
- **E2E tests**: DWSIM validation suite
- **Performance tests**: Meet targets from PROJECT_PLAN.md Section 21.1

---

## 14. Architectural Decisions

This section replaces traditional ADR files. All decisions are documented here with full context.

### 14.1 Decision: Follow C# Extension Architecture

**Date**: 2026-01-09
**Status**: Accepted

**Context**:
The "C# for Visual Studio Code" extension (github.com/dotnet/vscode-csharp) provides open-source C# language support through LSP.

**Decision**:
Mirror the C# extension architecture for VB.NET language support.

**Rationale**:
- C# extension is 100% open source (MIT)
- Provides all core LSP features needed for language support
- Proven architecture with Roslyn integration
- Simpler architecture focused on language features

**Verification Strategy**:
Always verify behavior against C# extension source code - never guess.

### 14.2 Decision: Samsung netcoredbg for Debugging

**Date**: 2026-01-09
**Status**: Accepted

**Context**:
Need debugger integration for VB.NET projects. Microsoft's debugger is proprietary.

**Decision**:
Use Samsung netcoredbg (github.com/Samsung/netcoredbg) as the debugger.

**Rationale**:
- Open source (MIT license)
- DAP-compliant (standard protocol)
- Cross-platform support
- Active maintenance by Samsung
- No proprietary dependencies

**Implementation**: Phase 2

### 14.3 Decision: VB.NET Only (Phase 1-3)

**Date**: 2026-01-09
**Status**: Accepted

**Context**:
Real-world solutions often mix C# and VB.NET projects.

**Decision**:
Phase 1-3 support VB.NET only. Defer mixed-language to Phase 4.

**Rationale**:
- Simpler initial implementation
- Better VB.NET-specific optimizations
- Faster time to MVP
- Can add C# tolerance later without breaking changes

**Future**: Phase 4 will load full solutions but serve only VB documents.

### 14.4 Decision: MSBuildWorkspace for Project Loading

**Date**: 2026-01-09
**Status**: Accepted

**Context**:
Need to load .sln and .vbproj files to provide semantic analysis.

**Decision**:
Use Roslyn's MSBuildWorkspace exclusively (no custom project loading).

**Rationale**:
- Roslyn's official project loading mechanism
- Handles SDK-style and legacy projects
- Respects MSBuild props/targets
- Matches C# extension approach
- No need to reinvent project parsing

**Trade-off**: Requires .NET SDK installation (acceptable constraint).

### 14.5 Decision: Named Pipes Primary, stdio Secondary

**Date**: 2026-01-10
**Status**: Accepted (Updated)

**Context**:
LSP supports multiple transports (stdio, TCP, named pipes, etc.). The C# extension uses named pipes as its primary transport for performance reasons. Named pipe support has historically been a pain point across platforms.

**Decision**:
Support both transports from Phase 1:
- **Named pipes**: Primary transport, must work correctly on all platforms (Windows, macOS, Linux)
- **stdio**: Secondary transport, simpler fallback option

**Rationale**:
- **Parity with C# extension**: Named pipes are how the C# extension communicates
- **Performance**: Named pipes offer better performance than stdio for large payloads
- **Cross-platform correctness**: Engineering effort focused on getting named pipes right on all platforms from the start
- **Fallback option**: stdio provides maximum compatibility when needed
- **Abstraction layer**: Design transport abstraction to cleanly support both

**Implementation**:
- Transport abstraction interface in Protocol layer
- Named pipe implementation following C# extension patterns (server outputs pipe name as JSON, client connects)
- stdio implementation for compatibility
- Smoke tests for both transports on all platforms

**Verification**: Must match C# extension named pipe behavior exactly on Windows, macOS, and Linux

### 14.6 Decision: UTF-16 Position Encoding

**Date**: 2026-01-09
**Status**: Accepted

**Context**:
LSP supports UTF-8, UTF-16, and UTF-32 position encodings.

**Decision**:
Use UTF-16 position encoding exclusively.

**Rationale**:
- Roslyn uses UTF-16 internally
- No conversion overhead
- Matches C# extension behavior
- Common in LSP ecosystem (VS Code default)

### 14.7 Decision: Incremental Text Sync

**Date**: 2026-01-09
**Status**: Accepted

**Context**:
LSP supports full-document or incremental synchronization.

**Decision**:
Use incremental sync (TextDocumentSyncKind.Incremental).

**Rationale**:
- Better performance for large files
- Less network traffic
- Roslyn supports efficient incremental updates
- Matches C# extension behavior

### 14.8 Decision: Debounced Diagnostics

**Date**: 2026-01-09
**Status**: Accepted

**Context**:
Running diagnostics on every keystroke can be expensive.

**Decision**:
Debounce diagnostic updates with 300ms default delay (configurable).

**Rationale**:
- Reduces unnecessary Roslyn queries
- Better responsiveness during typing
- Matches C# extension behavior (verified from source)
- User can configure if needed

### 14.9 Decision: No Telemetry in MVP

**Date**: 2026-01-09
**Status**: Accepted

**Context**:
Telemetry can help understand usage patterns and performance issues.

**Decision**:
No telemetry in Phase 1-2. Reconsider in Phase 3.

**Rationale**:
- Privacy-first approach
- No infrastructure requirement
- Focus on core features
- Community can opt-in later if desired

**Future**: If added, must be opt-in with clear disclosure.

### 14.10 Decision: Single Architecture Document

**Date**: 2026-01-09
**Status**: Accepted

**Context**:
Traditional projects use many ADR files, leading to fragmentation.

**Decision**:
Maintain this single architecture.md as source of truth (no separate ADRs).

**Rationale**:
- Easier for LLM/agentic comprehension
- All context in one place
- Reduces documentation drift
- Simpler maintenance

**Requirement**: MUST update this document with every architectural change.

### 14.11 Decision: Multi-Editor Testing (Emacs)

**Date**: 2026-01-09
**Status**: Accepted

**Context**:
Language Server Protocol (LSP) is editor-agnostic, but testing primarily in VS Code could miss protocol compliance issues.

**Decision**:
Test against multiple editors: VS Code (primary), Cursor, and Emacs (lsp-mode).

**Rationale**:
- **Protocol compliance validation** - Emacs lsp-mode is a different LSP client implementation
- **Broader compatibility** - Ensures language server works beyond VS Code ecosystem
- **Automated testing** - Emacs supports batch-mode testing on CI (Linux)
- **Community value** - VB.NET developers use diverse editors

**Implementation**:
- GitHub Actions workflow with Ubuntu + Emacs lsp-mode
- Automated LSP feature tests in Emacs batch mode
- Validates core features (completion, hover, diagnostics, navigation)

**Phase**: MVP (Phase 1) - include in initial testing strategy

---

## 15. Reference Implementations

### 15.1 Primary Reference: C# Extension

**Repository**: https://github.com/dotnet/vscode-csharp
**License**: MIT (open source)

**Use Cases**:
- Verify LSP behavior (never guess!)
- Extract TypeScript extension patterns
- Understand Roslyn integration approach
- Study debugger adapter patterns
- Validate solution loading logic

**Key Files to Study**:
- Extension activation and LSP client setup
- Language server launch and management
- Diagnostics update strategy
- EditorConfig integration
- Debug adapter implementation

### 15.2 Test Reference: DWSIM

**Repository**: https://github.com/DanWBR/dwsim
**License**: GPL-3.0

**Use Cases**:
- Performance benchmarking
- Real-world validation
- Regression testing
- Edge case discovery

**Size**: 100+ VB.NET files, complex domain logic

**Fork Strategy**: Create organizational fork for automated CI testing.

### 15.3 Debugger Reference: netcoredbg

**Repository**: https://github.com/Samsung/netcoredbg
**License**: MIT

**Use Cases**:
- DAP protocol understanding
- Debugger configuration
- Feature implementation guide

**Integration**: Phase 2

---

## Appendix A: Glossary

- **LSP**: Language Server Protocol - standard protocol for editor-language communication
- **DAP**: Debug Adapter Protocol - standard protocol for debugger communication
- **Roslyn**: Microsoft's .NET compiler platform with rich APIs
- **MSBuildWorkspace**: Roslyn component for loading MSBuild-based projects
- **DocumentId**: Roslyn's internal identifier for source documents
- **Semantic Model**: Roslyn's semantic analysis result for a document
- **Immutable Snapshot**: Read-only point-in-time view of solution/document state
- **JSON-RPC**: Remote procedure call protocol encoded in JSON
- **stdio**: Standard input/output streams for inter-process communication
- **UTF-16**: 16-bit Unicode encoding (Roslyn default)
- **Debouncing**: Delaying action until a pause in events
- **CancellationToken**: .NET mechanism for cooperative cancellation

---

## Appendix B: Decision Log (Quick Reference)

| ID | Date | Decision | Status |
|----|------|----------|--------|
| 14.1 | 2026-01-09 | Follow C# Extension Architecture | Accepted |
| 14.2 | 2026-01-09 | Samsung netcoredbg for Debugging | Accepted |
| 14.3 | 2026-01-09 | VB.NET Only (Phase 1-3) | Accepted |
| 14.4 | 2026-01-09 | MSBuildWorkspace for Project Loading | Accepted |
| 14.5 | 2026-01-10 | Named Pipes Primary, stdio Secondary | **Updated** |
| 14.6 | 2026-01-09 | UTF-16 Position Encoding | Accepted |
| 14.7 | 2026-01-09 | Incremental Text Sync | Accepted |
| 14.8 | 2026-01-09 | Debounced Diagnostics | Accepted |
| 14.9 | 2026-01-09 | No Telemetry in MVP | Accepted |
| 14.10 | 2026-01-09 | Single Architecture Document | Accepted |
| 14.11 | 2026-01-09 | Multi-Editor Testing (Emacs) | Accepted |
| - | 2026-01-10 | Canonical naming: VbNet.LanguageServer | Accepted |

---

## Appendix C: Implementation Status

**Last Updated**: 2026-01-10

### Layer Implementation Progress

| Layer | Status | Components |
|-------|--------|------------|
| Protocol | ✅ Complete | ITransport, NamedPipeTransport, StdioTransport, JsonRpcTypes, LspTypes, MessageDispatcher |
| Server Core | ✅ Complete | LanguageServer (lifecycle, routing, state management) |
| Workspace | ✅ Complete | WorkspaceManager, DocumentManager |
| Services | 🔄 In Progress | DiagnosticsService ✅ |
| Host/CLI | ✅ Complete | Program.cs with argument parsing |

### Test Coverage

| Component | Tests |
|-----------|-------|
| JsonRpcTypes | 7 tests |
| LspTypes | 8 tests |
| DocumentManager | 6 tests |
| **Total** | **21 tests passing** |

### Key Commits

| Commit | Description |
|--------|-------------|
| fa87716 | Phase 1 scaffold (Protocol, Core, Host layers) |
| 24c54d3 | Workspace Layer (WorkspaceManager, DocumentManager) |
| 859efca | DiagnosticsService with debouncing |

---

## Appendix D: Update History

| Date | Version | Changes |
|------|---------|---------|
| 2026-01-09 | 1.0 | Initial architecture document created during Phase 0 bootstrap |
| 2026-01-10 | 1.1 | Updated transport decision (named pipes primary, stdio secondary); Fixed naming consistency (VbNet.LanguageServer); Added Decision Log appendix |
| 2026-01-10 | 1.2 | Added Implementation Status appendix; Protocol, Core, Workspace, Host layers complete; DiagnosticsService implemented |

---

**This document is a living document. Update with every architectural change.**
