# Feature Support Matrix

**VB.NET Language Support - LSP Features and Roadmap**

Version: 2.3
Last Updated: 2026-01-13

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

**Current Phase**: Phase 2 (In Progress)
**Test Coverage**: 113 tests passing

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
| Syntax errors | ✅ Implemented | Phase 1 | Via Roslyn parser |
| Semantic errors | ✅ Implemented | Phase 1 | Via Roslyn semantic analysis |
| Analyzer diagnostics | ✅ Implemented | Phase 1 | Roslyn analyzer support |
| `workspace/diagnostic` (pull model) | ❌ Not Planned | N/A | Defer to future phases |

**Debouncing**: 300ms default (configurable via `vbnetLs.debounceMs`)

---

### Language Features

#### Completion

| Feature | Status | Phase | Notes |
|---------|--------|-------|-------|
| `textDocument/completion` | 📋 Planned | Phase 1 | Keywords, symbols, members, locals |
| `completionItem/resolve` | 📋 Planned | Phase 1 | Lazy load documentation |
| Commit characters | ✅ Implemented | Phase 1 | `.`, `(`, `<`, etc. |
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
| Quick info | ✅ Implemented | Phase 1 | Type information |
| XML documentation | ✅ Implemented | Phase 1 | From `<summary>`, `<param>`, etc. |

---

#### Signature Help

| Feature | Status | Phase | Notes |
|---------|--------|-------|-------|
| `textDocument/signatureHelp` | ? Implemented | Phase 2 | Parameter hints |
| Multiple overloads | ? Implemented | Phase 2 | All method overloads |
| Active parameter highlighting | ? Implemented | Phase 2 | Current parameter |


