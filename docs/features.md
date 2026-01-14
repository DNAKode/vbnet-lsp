# Feature Support Matrix

**`VB.NET` Language Support - LSP Features and Roadmap**

Version: 2.6  
Last Updated: 2026-01-13

## 1. Overview

This document provides a concise view of LSP features supported by `VB.NET` Language Support, their implementation status, and roadmap.

**Current Phase**: Phase 2 (Baseline complete)  
**Test Coverage**: 136 tests passing

## 2. Feature Status Legend

| Status | Meaning |
|--------|---------|
| Implemented | Feature is fully implemented and tested |
| In Progress | Feature is currently being developed |
| Planned | Feature is planned for an upcoming phase |
| Not Planned | Feature is not currently on the roadmap |
| Partial | Feature is partially implemented |

## 3. LSP Features

### Text Synchronization

| Feature | Status | Phase | Notes |
|---------|--------|-------|-------|
| `textDocument/didOpen` | Implemented | Phase 1 | Open document notification |
| `textDocument/didChange` (incremental) | Implemented | Phase 1 | Incremental sync for performance |
| `textDocument/didClose` | Implemented | Phase 1 | Close document notification |
| `textDocument/didSave` | Implemented | Phase 1 | Save notification |
| `textDocument/willSave` | Not Planned | N/A | Not required |
| `textDocument/willSaveWaitUntil` | Not Planned | N/A | Not required |

### Diagnostics

| Feature | Status | Phase | Notes |
|---------|--------|-------|-------|
| `textDocument/publishDiagnostics` | Implemented | Phase 1 | Push model with debouncing |
| Syntax errors | Implemented | Phase 1 | Via Roslyn parser |
| Semantic errors | Implemented | Phase 1 | Via Roslyn semantic analysis |
| Analyzer diagnostics | Implemented | Phase 1 | Roslyn analyzer support |
| `workspace/diagnostic` (pull model) | Not Planned | N/A | Defer to future phases |

**Debouncing**: 300ms default (configurable via `vbnet.debounceMs`)

### Completion

| Feature | Status | Phase | Notes |
|---------|--------|-------|-------|
| `textDocument/completion` | Implemented | Phase 1 | Keywords, symbols, members, locals |
| `completionItem/resolve` | Implemented | Phase 1 | Lazy load documentation |
| Commit characters | Implemented | Phase 1 | `.`, `(`, `<`, etc. |
| Snippets | Not Planned | N/A | Use VS Code built-in snippets |

### Hover

| Feature | Status | Phase | Notes |
|---------|--------|-------|-------|
| `textDocument/hover` | Implemented | Phase 1 | Symbol signature and documentation |
| Quick info | Implemented | Phase 1 | Type information |
| XML documentation | Implemented | Phase 1 | From `<summary>`, `<param>`, etc. |

### Signature Help

| Feature | Status | Phase | Notes |
|---------|--------|-------|-------|
| `textDocument/signatureHelp` | Implemented | Phase 2 | Parameter hints |
| Multiple overloads | Implemented | Phase 2 | All method overloads |
| Active parameter highlighting | Implemented | Phase 2 | Current parameter |

### Code Actions

| Feature | Status | Phase | Notes |
|---------|--------|-------|-------|
| `textDocument/codeAction` | Implemented | Phase 2 | Source actions (Option Strict/Explicit/Infer) |
| `codeAction/resolve` | Planned | Phase 2 | Lazy compute edit |
| Quick fixes (diagnostics) | Planned | Phase 2 | Fix errors/warnings |

### Formatting

| Feature | Status | Phase | Notes |
|---------|--------|-------|-------|
| `textDocument/formatting` | Implemented | Phase 2 | Roslyn formatter |
| `textDocument/rangeFormatting` | Implemented | Phase 2 | Roslyn formatter |

### Symbols and Navigation

| Feature | Status | Phase | Notes |
|---------|--------|-------|-------|
| `textDocument/documentSymbol` | Implemented | Phase 1 | Document outline |
| `workspace/symbol` | Implemented | Phase 1 | Workspace symbol search |
| `textDocument/definition` | Implemented | Phase 1 | Go to definition |
| `textDocument/references` | Implemented | Phase 1 | Find all references |
| `textDocument/rename` | Implemented | Phase 1 | Rename with prepare |

### Semantic Tokens

| Feature | Status | Phase | Notes |
|---------|--------|-------|-------|
| `textDocument/semanticTokens/full` | Implemented | Phase 2 | Full document tokens |
| `textDocument/semanticTokens/range` | Implemented | Phase 2 | Range-based tokens |

### Folding

| Feature | Status | Phase | Notes |
|---------|--------|-------|-------|
| `textDocument/foldingRange` | Implemented | Phase 2 | VB blocks and `#Region` |

## 4. Debugging Features

| Feature | Status | Notes |
|---------|--------|-------|
| netcoredbg integration | Implemented | Requires netcoredbg installed or provided |

## 5. Roadmap (Next)

Phase 3 focuses on inlay hints, call/type hierarchy, code lens (if valuable), test explorer integration, and performance tuning.

