# Research and Examination Plan

**VB.NET Language Support - External Repository Analysis and Test Strategy**

Version: 1.0
Last Updated: 2026-01-09
Status: Active

## Table of Contents

1. [Overview](#overview)
2. [C# Extension Examination Plan](#c-extension-examination-plan)
3. [netcoredbg Examination Plan](#netcoredbg-examination-plan)
4. [DWSIM Test Strategy](#dwsim-test-strategy)
5. [Research Findings Log](#research-findings-log)
6. [Action Items](#action-items)

---

## 1. Overview

### Purpose

This document tracks our systematic examination of reference repositories and test infrastructure. All findings should be documented here and key architectural insights transferred to `docs/architecture.md`.

### Guiding Principle

**Never guess - always verify.** Every architectural decision must be backed by evidence from:
- C# extension source code examination
- LSP specification verification
- Empirical testing

### Repository Locations

| Repository | Local Path | Purpose |
|------------|------------|---------|
| vscode-csharp | `_external/vscode-csharp/` | Primary architecture reference |
| netcoredbg | `_external/netcoredbg/` | Debugger integration reference |
| DWSIM | `_test/dwsim/` | Performance validation |

---

## 2. C# Extension Examination Plan

**Repository**: https://github.com/dotnet/vscode-csharp
**Local Path**: `_external/vscode-csharp/`

### 2.1 High Priority - Extension Architecture (Phase 1)

These are critical for MVP development:

#### Extension Activation
- [ ] **Location**: `src/` TypeScript files
- [ ] **Questions**:
  - How does the extension activate? What triggers it?
  - What file types/patterns trigger activation?
  - How is the language server spawned?
- [ ] **Files to examine**:
  - `package.json` - activation events, contributes
  - `src/main.ts` or `src/extension.ts` - entry point
  - Language client initialization code

#### LSP Client Setup
- [ ] **Questions**:
  - How is the LanguageClient configured?
  - What server options are used (stdio, socket, etc.)?
  - How are capabilities negotiated?
- [ ] **Files to examine**:
  - Language client initialization
  - Server options configuration
  - Client options and document selectors

#### Language Server Launch
- [ ] **Questions**:
  - How is the language server process started?
  - What command-line arguments are passed?
  - How is the server path resolved?
- [ ] **Files to examine**:
  - Server executable location logic
  - Process spawn configuration
  - Environment variable handling

#### Solution/Project Discovery
- [ ] **Questions**:
  - How are .sln files discovered?
  - What happens with multiple solutions?
  - How does the user select a solution?
- [ ] **Files to examine**:
  - Workspace scanning logic
  - Solution picker UI
  - Configuration for solution path

#### Diagnostics Flow
- [ ] **Questions**:
  - Push or pull model for diagnostics?
  - What triggers diagnostic updates?
  - How is debouncing implemented?
- [ ] **Files to examine**:
  - Diagnostic provider implementation
  - Text document sync handlers
  - Debounce/throttle logic

### 2.2 Medium Priority - Core Features (Phase 1-2)

#### Completion
- [ ] **Questions**:
  - How is completion triggered?
  - Is completionItem/resolve used?
  - What commit characters are configured?
- [ ] **Key patterns to extract**:
  - Completion provider structure
  - Item kind mapping
  - Documentation fetching

#### Navigation (Definition, References)
- [ ] **Questions**:
  - How are cross-file references handled?
  - How is the location URI constructed?
  - Multiple definition handling?
- [ ] **Key patterns to extract**:
  - Definition provider structure
  - Reference grouping

#### Hover
- [ ] **Questions**:
  - What information is shown?
  - How is documentation formatted?
  - Markdown rendering approach?

#### Rename
- [ ] **Questions**:
  - prepareRename implementation?
  - Cross-file rename workflow?
  - Conflict detection?

### 2.3 Lower Priority - Enhanced Features (Phase 2+)

#### Formatting
- [ ] EditorConfig integration
- [ ] Formatting options handling

#### Code Actions
- [ ] Quick fix patterns
- [ ] Code action kinds used
- [ ] Lazy resolution strategy

#### Semantic Tokens
- [ ] Token types and modifiers
- [ ] Delta updates

#### Debugging Integration
- [ ] DAP adapter setup
- [ ] Launch configuration schema
- [ ] Debug session lifecycle

### 2.4 Repository Structure Analysis

Document the C# extension structure to inform our own organization:

```
vscode-csharp/
├── src/                    # TypeScript source
│   ├── main.ts             # Entry point (?)
│   ├── features/           # LSP feature implementations (?)
│   └── ...
├── package.json            # Extension manifest
├── tsconfig.json           # TypeScript config
└── ...
```

**Action**: Fill in actual structure after cloning.

---

## 3. netcoredbg Examination Plan

**Repository**: https://github.com/Samsung/netcoredbg
**Local Path**: `_external/netcoredbg/`
**Phase**: Phase 2 (debugging integration)

### 3.1 DAP Protocol Understanding

- [ ] **Questions**:
  - What DAP capabilities does netcoredbg support?
  - How is it launched from an extension?
  - What command-line arguments are required?

### 3.2 Integration Patterns

- [ ] **Questions**:
  - How does the C# extension integrate its debugger?
  - Can we use similar patterns with netcoredbg?
  - What VS Code launch.json configuration is needed?

### 3.3 Platform Considerations

- [ ] **Questions**:
  - Binary distribution per platform?
  - Build requirements if building from source?
  - Version compatibility with .NET versions?

### 3.4 Files to Examine

- [ ] README.md - Usage documentation
- [ ] Command-line help output
- [ ] Example launch configurations
- [ ] DAP capability declarations

---

## 4. DWSIM Test Strategy

**Repository**: https://github.com/DanWBR/dwsim
**Local Path**: `_test/dwsim/`
**Purpose**: Real-world performance validation

### 4.1 Codebase Analysis

First, understand the DWSIM codebase:

- [ ] **Size metrics**:
  - Total .vb file count
  - Total lines of VB.NET code
  - Number of projects in solution
  - Project dependency graph complexity

- [ ] **Complexity metrics**:
  - Largest single file
  - Most complex class hierarchies
  - Cross-project reference patterns

### 4.2 Test Scenarios

#### Startup Performance
| Metric | Target | How to Measure |
|--------|--------|----------------|
| Solution load time | <5s | Time from server start to workspace/initialized |
| First diagnostics | <500ms | Time from didOpen to publishDiagnostics |
| Memory after load | <500MB | Process working set |

#### Feature Performance
| Feature | Target | Test Method |
|---------|--------|-------------|
| Completion | <100ms p95 | Completion request timing |
| Hover | <50ms p95 | Hover request timing |
| Go to Definition | <100ms | Definition request timing |
| Find References | <500ms | References request timing |
| Rename | <1s | Rename request timing |

#### Stability Tests
- [ ] 8-hour run with periodic file edits
- [ ] Memory growth tracking (target: <20% growth)
- [ ] No crashes or hangs

### 4.3 Specific Files to Test

Identify specific DWSIM files for targeted testing:

- [ ] Largest VB.NET file - stress test parsing
- [ ] File with most symbols - completion performance
- [ ] File with deep inheritance - type hierarchy
- [ ] File with many references - reference finding

### 4.4 Baseline Measurements

Before building our language server, establish baselines:

- [ ] How long does `dotnet build` take on DWSIM?
- [ ] How much memory does building consume?
- [ ] What errors/warnings does the build produce?

---

## 5. Research Findings Log

### Format

Each finding should be logged with:
- **Date**: When discovered
- **Source**: File/line or documentation reference
- **Finding**: What was learned
- **Impact**: How this affects our implementation
- **Action**: What we need to do

### Findings

#### [Template - Copy for new findings]
```
### Finding: [Title]
- **Date**: YYYY-MM-DD
- **Source**: `_external/vscode-csharp/path/to/file.ts:123`
- **Finding**: [Description of what was learned]
- **Impact**: [How this affects our implementation]
- **Action**: [What we need to do]
```

---

## 6. Action Items

### Immediate (Before Phase 1 coding)

- [ ] Clone vscode-csharp and examine extension activation
- [ ] Clone DWSIM and measure codebase size
- [ ] Document C# extension package.json structure
- [ ] Identify LSP client initialization pattern
- [ ] Understand language server launch mechanism

### Phase 1

- [ ] Complete all "High Priority" C# extension examination items
- [ ] Establish DWSIM baseline measurements
- [ ] Document all key findings in architecture.md

### Phase 2

- [ ] Clone and examine netcoredbg
- [ ] Plan debugger integration based on findings
- [ ] Test netcoredbg manually with DWSIM

---

## Appendix A: Useful Commands

### Exploring vscode-csharp

```bash
# Find TypeScript entry points
find _external/vscode-csharp -name "*.ts" | xargs grep -l "activate"

# Find package.json activation events
cat _external/vscode-csharp/package.json | jq '.activationEvents'

# Find language client usage
grep -r "LanguageClient" _external/vscode-csharp/src/

# Find how server is spawned
grep -r "spawn\|exec\|fork" _external/vscode-csharp/src/
```

### Exploring DWSIM

```bash
# Count VB.NET files
find _test/dwsim -name "*.vb" | wc -l

# Find largest VB.NET files
find _test/dwsim -name "*.vb" -exec wc -l {} \; | sort -n | tail -20

# Find solution files
find _test/dwsim -name "*.sln"

# Find project files
find _test/dwsim -name "*.vbproj"

# Count total lines of VB.NET
find _test/dwsim -name "*.vb" -exec cat {} \; | wc -l
```

### Exploring netcoredbg

```bash
# Find DAP-related files
find _external/netcoredbg -name "*.cpp" -o -name "*.h" | xargs grep -l "DAP\|DebugAdapter"

# Check README
cat _external/netcoredbg/README.md
```

---

## Appendix B: Reference Links

- [LSP Specification](https://microsoft.github.io/language-server-protocol/specifications/lsp/3.17/specification/)
- [DAP Specification](https://microsoft.github.io/debug-adapter-protocol/specification)
- [vscode-languageclient npm](https://www.npmjs.com/package/vscode-languageclient)
- [Roslyn API Documentation](https://docs.microsoft.com/en-us/dotnet/csharp/roslyn-sdk/)
- [VS Code Extension API](https://code.visualstudio.com/api)

---

**This is a living document. Update with findings as research progresses.**
