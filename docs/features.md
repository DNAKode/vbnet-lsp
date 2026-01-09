# Feature Support Matrix

**VB.NET Language Support - LSP Features and Roadmap**

Version: 1.0
Last Updated: 2026-01-09

## Table of Contents

1. [Overview](#overview)
2. [Feature Status Legend](#feature-status-legend)
3. [LSP Features](#lsp-features)
4. [Debugging Features](#debugging-features)
5. [VS Code Integration](#vs-code-integration)
6. [Roadmap](#roadmap)
7. [Comparison with C# Extension](#comparison-with-c-extension)

---

## 1. Overview

This document provides a comprehensive view of LSP features supported by VB.NET Language Support, their implementation status, and roadmap.

**Current Phase**: Phase 0 (Bootstrap)
**Target MVP**: Phase 1 features

---

## 2. Feature Status Legend

| Status | Meaning |
|--------|---------|
| ✅ Implemented | Feature is fully implemented and tested |
| 🚧 In Progress | Feature is currently being developed |
| 📋 Planned | Feature is planned for upcoming phase |
| ❌ Not Planned | Feature is not currently on roadmap |
| ⚠️ Partial | Feature is partially implemented |

---

## 3. LSP Features

### Text Synchronization

| Feature | Status | Phase | Notes |
|---------|--------|-------|-------|
| `textDocument/didOpen` | 📋 Planned | Phase 1 | Open document notification |
| `textDocument/didChange` (incremental) | 📋 Planned | Phase 1 | Incremental sync for performance |
| `textDocument/didClose` | 📋 Planned | Phase 1 | Close document notification |
| `textDocument/didSave` | 📋 Planned | Phase 1 | Save notification (optional trigger) |
| `textDocument/willSave` | ❌ Not Planned | N/A | Not required for MVP |
| `textDocument/willSaveWaitUntil` | ❌ Not Planned | N/A | Not required for MVP |

---

### Diagnostics

| Feature | Status | Phase | Notes |
|---------|--------|-------|-------|
| `textDocument/publishDiagnostics` | 📋 Planned | Phase 1 | Push model with debouncing |
| Syntax errors | 📋 Planned | Phase 1 | Via Roslyn parser |
| Semantic errors | 📋 Planned | Phase 1 | Via Roslyn semantic analysis |
| Analyzer diagnostics | 📋 Planned | Phase 1 | Roslyn analyzer support |
| `workspace/diagnostic` (pull model) | ❌ Not Planned | N/A | Defer to future phases |

**Debouncing**: 300ms default (configurable via `vbnetLs.debounceMs`)

---

### Language Features

#### Completion

| Feature | Status | Phase | Notes |
|---------|--------|-------|-------|
| `textDocument/completion` | 📋 Planned | Phase 1 | Keywords, symbols, members, locals |
| `completionItem/resolve` | 📋 Planned | Phase 1 | Lazy load documentation |
| Commit characters | 📋 Planned | Phase 1 | `.`, `(`, `<`, etc. |
| Snippets | ❌ Not Planned | N/A | Use VS Code built-in snippets |

**Completion Kinds Supported**:
- Keywords (`Dim`, `If`, `Function`, etc.)
- Local variables
- Parameters
- Fields and properties
- Methods
- Classes and interfaces
- Namespaces
- Enums

---

#### Hover

| Feature | Status | Phase | Notes |
|---------|--------|-------|-------|
| `textDocument/hover` | 📋 Planned | Phase 1 | Symbol signature and documentation |
| Quick info | 📋 Planned | Phase 1 | Type information |
| XML documentation | 📋 Planned | Phase 1 | From `<summary>`, `<param>`, etc. |

---

#### Signature Help

| Feature | Status | Phase | Notes |
|---------|--------|-------|-------|
| `textDocument/signatureHelp` | 📋 Planned | Phase 2 | Parameter hints |
| Multiple overloads | 📋 Planned | Phase 2 | All method overloads |
| Active parameter highlighting | 📋 Planned | Phase 2 | Current parameter |

---

#### Navigation

| Feature | Status | Phase | Notes |
|---------|--------|-------|-------|
| `textDocument/definition` | 📋 Planned | Phase 1 | Go to definition |
| `textDocument/typeDefinition` | 📋 Planned | Phase 2 | Go to type definition |
| `textDocument/implementation` | 📋 Planned | Phase 3 | Go to implementation |
| `textDocument/declaration` | 📋 Planned | Phase 2 | Go to declaration |
| `textDocument/references` | 📋 Planned | Phase 1 | Find all references |
| `textDocument/documentHighlight` | 📋 Planned | Phase 2 | Highlight symbol occurrences |

---

#### Symbols

| Feature | Status | Phase | Notes |
|---------|--------|-------|-------|
| `textDocument/documentSymbol` | 📋 Planned | Phase 1 | Outline view, breadcrumbs |
| `workspace/symbol` | 📋 Planned | Phase 1 | Find symbol in workspace |
| Hierarchical symbols | 📋 Planned | Phase 1 | Nested classes, methods |
| Symbol kinds | 📋 Planned | Phase 1 | Class, Method, Field, etc. |

**Supported Symbol Kinds**:
- Module
- Class
- Interface
- Enum
- Method
- Property
- Field
- Variable
- Namespace

---

#### Rename

| Feature | Status | Phase | Notes |
|---------|--------|-------|-------|
| `textDocument/prepareRename` | 📋 Planned | Phase 1 | Validate rename target |
| `textDocument/rename` | 📋 Planned | Phase 1 | Cross-file rename |
| Local variable rename | 📋 Planned | Phase 1 | Within single file |
| Symbol rename across projects | 📋 Planned | Phase 1 | Multi-file rename |

---

### Formatting

| Feature | Status | Phase | Notes |
|---------|--------|-------|-------|
| `textDocument/formatting` | 📋 Planned | Phase 2 | Format entire document |
| `textDocument/rangeFormatting` | 📋 Planned | Phase 2 | Format selection |
| `textDocument/onTypeFormatting` | 📋 Planned | Phase 3 | Format on typing (`;`, `}`, etc.) |
| EditorConfig support | 📋 Planned | Phase 2 | Respect `.editorconfig` |

---

### Code Actions

| Feature | Status | Phase | Notes |
|---------|--------|-------|-------|
| `textDocument/codeAction` | 📋 Planned | Phase 2 | Quick fixes and refactorings |
| `codeAction/resolve` | 📋 Planned | Phase 2 | Lazy compute edit |
| Quick fixes (diagnostics) | 📋 Planned | Phase 2 | Fix errors/warnings |
| Refactorings | 📋 Planned | Phase 3 | Extract method, etc. |

**Planned Code Actions** (Phase 2):
- Fix imports
- Remove unused imports
- Implement interface
- Generate constructor
- Add null checks

**Planned Refactorings** (Phase 3):
- Extract method
- Extract local variable
- Inline variable
- Rename symbol

---

### Semantic Tokens

| Feature | Status | Phase | Notes |
|---------|--------|-------|-------|
| `textDocument/semanticTokens/full` | 📋 Planned | Phase 2 | Full document tokens |
| `textDocument/semanticTokens/range` | 📋 Planned | Phase 2 | Range-based tokens |
| `textDocument/semanticTokens/full/delta` | 📋 Planned | Phase 3 | Incremental updates |

**Token Types**:
- Namespace, Class, Interface, Enum, Struct
- Method, Property, Field, Parameter, Variable
- Keyword, Operator, Comment
- String, Number

---

### Inlay Hints

| Feature | Status | Phase | Notes |
|---------|--------|-------|-------|
| `textDocument/inlayHint` | 📋 Planned | Phase 3 | Inline hints |
| `inlayHint/resolve` | 📋 Planned | Phase 3 | Lazy compute hint |
| Type hints | 📋 Planned | Phase 3 | `Dim x = ...` shows type |
| Parameter name hints | 📋 Planned | Phase 3 | Method call parameter names |

---

### Call Hierarchy

| Feature | Status | Phase | Notes |
|---------|--------|-------|-------|
| `textDocument/prepareCallHierarchy` | 📋 Planned | Phase 3 | Prepare call hierarchy |
| `callHierarchy/incomingCalls` | 📋 Planned | Phase 3 | Find callers |
| `callHierarchy/outgoingCalls` | 📋 Planned | Phase 3 | Find callees |

---

### Type Hierarchy

| Feature | Status | Phase | Notes |
|---------|--------|-------|-------|
| `textDocument/prepareTypeHierarchy` | 📋 Planned | Phase 3 | Prepare type hierarchy |
| `typeHierarchy/supertypes` | 📋 Planned | Phase 3 | Base classes/interfaces |
| `typeHierarchy/subtypes` | 📋 Planned | Phase 3 | Derived classes/implementations |

---

### Folding Ranges

| Feature | Status | Phase | Notes |
|---------|--------|-------|-------|
| `textDocument/foldingRange` | 📋 Planned | Phase 2 | Code folding |
| Method folding | 📋 Planned | Phase 2 | Collapse methods |
| Region folding | 📋 Planned | Phase 2 | `#Region` / `#End Region` |
| Comment folding | 📋 Planned | Phase 2 | Multi-line comments |

---

### Code Lens

| Feature | Status | Phase | Notes |
|---------|--------|-------|-------|
| `textDocument/codeLens` | 📋 Planned | Phase 3 | Inline code actions |
| `codeLens/resolve` | 📋 Planned | Phase 3 | Lazy compute lens |
| References count | 📋 Planned | Phase 3 | Show reference count |
| Run tests | 📋 Planned | Phase 3 | Test runner integration |

---

### Workspace Features

| Feature | Status | Phase | Notes |
|---------|--------|-------|-------|
| `workspace/didChangeConfiguration` | 📋 Planned | Phase 1 | Reload settings |
| `workspace/didChangeWatchedFiles` | 📋 Planned | Phase 1 | File system events |
| `workspace/executeCommand` | 📋 Planned | Phase 2 | Custom commands |
| Multi-root workspaces | 📋 Planned | Phase 4 | Multiple folders |

---

## 4. Debugging Features

**Debugger**: Samsung netcoredbg (open source)
**Protocol**: Debug Adapter Protocol (DAP)
**Phase**: Phase 2

| Feature | Status | Phase | Notes |
|---------|--------|-------|-------|
| Launch configuration | 📋 Planned | Phase 2 | Start debugging |
| Attach to process | 📋 Planned | Phase 2 | Attach debugger |
| Breakpoints (line) | 📋 Planned | Phase 2 | Set breakpoints |
| Conditional breakpoints | 📋 Planned | Phase 4 | Advanced breakpoints |
| Step in / out / over | 📋 Planned | Phase 2 | Code stepping |
| Variable inspection | 📋 Planned | Phase 2 | View variables |
| Watch expressions | 📋 Planned | Phase 4 | Evaluate expressions |
| Call stack navigation | 📋 Planned | Phase 2 | Navigate stack frames |
| Exception handling | 📋 Planned | Phase 2 | Break on exceptions |

---

## 5. VS Code Integration

### Extension Features

| Feature | Status | Phase | Notes |
|---------|--------|-------|-------|
| VB syntax highlighting | ✅ Implemented | N/A | Uses VS Code built-in |
| File association (.vb) | 📋 Planned | Phase 1 | Extension activation |
| Status bar integration | 📋 Planned | Phase 1 | Show server status |
| Output panel | 📋 Planned | Phase 1 | Show logs |
| Command palette commands | 📋 Planned | Phase 1 | Restart server, etc. |
| Configuration UI | 📋 Planned | Phase 1 | Settings integration |
| Problem panel integration | 📋 Planned | Phase 1 | Show diagnostics |

---

### Commands

| Command | Status | Phase | Description |
|---------|--------|-------|-------------|
| `VB.NET: Restart Language Server` | 📋 Planned | Phase 1 | Restart server process |
| `VB.NET: Show Output` | 📋 Planned | Phase 1 | Open output panel |
| `VB.NET: Select Solution` | 📋 Planned | Phase 1 | Choose .sln file |
| `VB.NET: Reload Projects` | 📋 Planned | Phase 2 | Reload workspace |

---

## 6. Roadmap

### Phase 1: MVP (Q1 2026)

**Goal**: Core language features

✅ **Completed**:
- Project planning
- Repository setup
- Documentation

📋 **In Progress**:
- Language server bootstrap
- LSP protocol implementation
- Roslyn integration

📋 **Planned**:
- Text synchronization
- Diagnostics
- Completion
- Hover
- Definition and references
- Rename
- Document and workspace symbols

**Release**: v0.1.0 (alpha)

---

### Phase 2: Enhanced Editing (Q2 2026)

**Goal**: Productivity features

📋 **Planned**:
- Document formatting
- Range formatting
- Code actions (quick fixes)
- Semantic tokens
- Signature help
- Folding ranges
- Debugging integration (netcoredbg)

**Release**: v0.2.0 (beta)

---

### Phase 3: Advanced Features (Q3 2026)

**Goal**: Advanced navigation and productivity

📋 **Planned**:
- Inlay hints
- Call hierarchy
- Type hierarchy
- Code lens
- On-type formatting
- Advanced refactorings
- Performance optimization

**Release**: v1.0.0 (stable)

---

### Phase 4: Enterprise Features (Q4 2026)

**Goal**: Complex scenarios

📋 **Planned**:
- Mixed-language solutions (VB + C#)
- Multi-root workspaces
- Advanced debugging (conditional breakpoints, watch expressions)
- Workspace-wide operations
- Advanced refactorings

**Release**: v1.1.0

---

## 7. Comparison with C# Extension

**Reference**: [C# for Visual Studio Code](https://github.com/dotnet/vscode-csharp)

| Feature Category | C# Extension | VB.NET Language Support (MVP) |
|------------------|--------------|-------------------------------|
| Text Synchronization | ✅ Incremental | 📋 Planned (Phase 1) |
| Diagnostics | ✅ Real-time | 📋 Planned (Phase 1) |
| Completion | ✅ Full | 📋 Planned (Phase 1) |
| Hover | ✅ Full | 📋 Planned (Phase 1) |
| Signature Help | ✅ Full | 📋 Planned (Phase 2) |
| Go to Definition | ✅ Full | 📋 Planned (Phase 1) |
| Find References | ✅ Full | 📋 Planned (Phase 1) |
| Rename | ✅ Full | 📋 Planned (Phase 1) |
| Symbols | ✅ Full | 📋 Planned (Phase 1) |
| Formatting | ✅ Full | 📋 Planned (Phase 2) |
| Code Actions | ✅ Full | 📋 Planned (Phase 2) |
| Semantic Tokens | ✅ Full | 📋 Planned (Phase 2) |
| Inlay Hints | ✅ Full | 📋 Planned (Phase 3) |
| Call Hierarchy | ✅ Full | 📋 Planned (Phase 3) |
| Type Hierarchy | ✅ Full | 📋 Planned (Phase 3) |
| Debugging | ✅ Proprietary | 📋 netcoredbg (Phase 2) |
| Razor Support | ✅ Yes | ❌ Not Applicable |
| Mixed C#/VB | ⚠️ Limited | 📋 Planned (Phase 4) |

---

## Known Limitations (MVP)

- **No Razor/XAML support** - VB.NET only
- **No OmniSharp protocol** - LSP only
- **Single-root workspaces only** - Multi-root in Phase 4
- **VB.NET projects only** - Mixed C#/VB in Phase 4
- **No proprietary features** - Fully open source

---

## Feature Requests

To request a feature or vote on existing requests:
- [GitHub Issues](https://github.com/YOUR-ORG/vbnet-lsp/issues)
- [GitHub Discussions](https://github.com/YOUR-ORG/vbnet-lsp/discussions)

---

## Additional Resources

- [Architecture Documentation](architecture.md)
- [Development Guide](development.md)
- [Configuration Guide](configuration.md)
- [LSP Specification](https://microsoft.github.io/language-server-protocol/)
- [C# Extension Source](https://github.com/dotnet/vscode-csharp)

---

**Last Updated**: 2026-01-09

**Maintained by**: VB.NET Language Support Contributors
