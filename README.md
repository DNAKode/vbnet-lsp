# `VB.NET` Language Support

**First-class `VB.NET` language support for VS Code and compatible editors**

[![License: MIT](https://img.shields.io/badge/License-MIT-yellow.svg)](https://opensource.org/licenses/MIT)

## Overview

`VB.NET` Language Support is a fully open-source extension providing first-class `VB.NET` language support for Visual Studio Code and compatible editors (Cursor, VSCodium, Emacs). Built on Microsoft's Roslyn compiler platform, it delivers modern IDE features through the Language Server Protocol (LSP).

### Key Features

- **Roslyn-powered** semantic analysis and compilation
- **LSP architecture** mirroring the "C# for Visual Studio Code" extension
- **Solution and project loading** for `.sln` and `.vbproj` files
- **Core IDE features:**
  - Real-time diagnostics and error detection
  - IntelliSense completion
  - Go to Definition and Find References
  - Symbol navigation and search
  - Code rename refactoring
  - Hover information
- Advanced navigation (type definition, implementation, call/type hierarchy)
- Document highlights, selection ranges, and document links
- **Open-source debugging** with bundled Samsung netcoredbg (Phase 2)
- **100% MIT licensed** - no proprietary components

## Status

**Current Phase:** Phase 3 - Advanced Navigation & Polish (in progress)
**Version:** 0.1.7 (preview)
**Status:** In active development

### Implemented Features

| Feature | Status |
|---------|--------|
| Text Synchronization | Implemented |
| Diagnostics (real-time errors) | Implemented |
| Completion (IntelliSense) | Implemented |
| Hover (symbol info) | Implemented |
| Go to Definition | Implemented |
| Find All References | Implemented |
| Rename Symbol | Implemented |
| Document Symbols (outline) | Implemented |
| Workspace Symbols (search) | Implemented |
| Solution/Project Loading | Implemented |
| Formatting (document + range) | Implemented |
| Code Actions (baseline) | Implemented |
| Semantic Tokens (full + range) | Implemented |
| Signature Help | Implemented |
| Folding Ranges | Implemented |
| Type Definition | Implemented |
| Implementation | Implemented |
| Document Highlight | Implemented |
| Selection Range | Implemented |
| Document Links | Implemented |
| Call/Type Hierarchy | Implemented |
| Pull Diagnostics (document/workspace) | Implemented |
| Debugging (netcoredbg integration) | Implemented (bundled netcoredbg) |

**Test Coverage:** 150 tests passing

See [PROJECT_PLAN.md](PROJECT_PLAN.md) for the complete roadmap.
## Installation

### Prerequisites

- [.NET 10.0 SDK](https://dotnet.microsoft.com/download) or later
- [Visual Studio Code](https://code.visualstudio.com/) 1.80.0 or later
- [Node.js](https://nodejs.org/) 18.0 or later (for extension development)

The debugger is bundled with the extension; no separate install is required. Advanced users can override it via settings if needed.

### Remote + Web Notes

- **Dev containers/SSH/WSL:** Supported as long as the remote environment has the .NET SDK installed.
- **VS Code Web (vscode.dev/github.dev):** Not supported because the language server requires a .NET runtime and local project system access.
- **Dev container config:** See `.devcontainer/devcontainer.json` for a repeatable setup with .NET + Node.js.

### WSL Quickstart (Testing)

For WSL-based testing, use the Linux VSIX and ensure the bundled debugger is available for the harness:

```bash
export PATH="$HOME/.dotnet:$PATH"
export DOTNET_ROOT="$HOME/.dotnet"
cd /mnt/c/Work/vbnet-lsp/src/extension
npm run bundle-debugger -- --target linux-x64
cd /mnt/c/Work/vbnet-lsp/test-explore/clients/vscode
export CODE_DISABLE_WSL=1
export VSCODE_CLI=1
export DONT_PROMPT_WSL_INSTALL=1
export NO_AT_BRIDGE=1
export DBUS_SESSION_BUS_ADDRESS="unix:path=/dev/null"
xvfb-run -a npm test
```

See [docs/development.md](docs/development.md) for full setup details.

### From Source (Development)

Currently, `VB.NET` Language Support is in early development. To build from source:

```bash
# Clone the repository
git clone https://github.com/DNAKode/vbnet-lsp.git
cd vbnet-lsp

# Build the language server
dotnet build src/VbNet.LanguageServer.Vb

# Run tests
dotnet test
```

See [docs/development.md](docs/development.md) for detailed setup instructions.

## Quick Start

Once installed, the extension automatically activates when you open a \`.vb\` file or a folder containing `VB.NET` projects.

### Opening a `VB.NET` Project

1. Open VS Code
2. Open a folder containing a \`.sln\` or \`.vbproj\` file
3. The extension will automatically discover and load your `VB.NET` projects
4. Start coding with full IntelliSense support

If you have multiple solutions, use the Command Palette action **"VB.NET: Select Workspace Solution"** to choose the active \`.sln/.slnf/.slnx\`.
For diagnostics, use **"VB.NET: Show Logs"** and **"VB.NET: Toggle LSP Trace"**.
If assets are missing, run **"VB.NET: Restore Workspace"** or **"VB.NET: Restore Project"**.
To run tests scoped to the active file, solution, or project, use **"VB.NET: Run Tests"** (or **"VB.NET: Debug Tests (Preview)"**).
To force a reanalysis without restarting VS Code, use **"VB.NET: Reload Workspace"**.
For attaching to a running .NET process, use **"VB.NET: Attach Debugger to Process"**.

## Architecture

`VB.NET` Language Support follows the architecture of the "C# for Visual Studio Code" extension:

- **VS Code Extension (TypeScript)** - Extension activation, LSP client, UI integration
- **Language Server (VB.NET/.NET)** - Roslyn workspace, LSP protocol, language services
- **Samsung netcoredbg** - Open-source .NET debugger (bundled in platform-specific VSIX packages)

See [docs/architecture.md](docs/architecture.md) for detailed architectural information.

## Documentation

- [Architecture](docs/architecture.md) - System architecture and design decisions
- [Development Guide](docs/development.md) - Building, testing, and contributing
- [Configuration](docs/configuration.md) - Settings and customization
- [Roslyn Packaging](docs/roslyn-packaging.md) - Roslyn LSP packaging and layout
- [Feature Support](docs/features.md) - LSP feature matrix and roadmap

## Project Goals

`VB.NET` Language Support aims to provide:

1. **Feature parity** with the "C# for Visual Studio Code" extension
2. **Performance** validated against large real-world codebases (DWSIM)
3. **Stability** through comprehensive testing and validation
4. **Community focus** - welcoming issues, feedback, and contributions

### Key Differentiators

- **100% open source** under MIT license
- **`VB.NET` focused** - optimized specifically for `VB.NET` development
- **Open-source debugger** - Samsung netcoredbg bundled for all supported platforms
- **No proprietary components** - fully transparent and community-driven

## Roadmap

### Phase 1 (MVP) - Complete
- Core language server with essential LSP features
- Solution and project loading
- Diagnostics, completion, navigation
- Symbol search and rename

### Phase 2 (Complete)
- Code formatting
- Code actions (baseline)
- Semantic tokens (enhanced syntax highlighting)
- Signature help
- Folding ranges
- Debugging integration with bundled netcoredbg

### Phase 3 (Next)
- Test explorer integration
- Performance optimization
- Inlay hints (deferred pending stable Roslyn APIs)

### Phase 4
- Mixed-language solution support
- Advanced refactorings
- Multi-root workspace handling

See [PROJECT_PLAN.md](PROJECT_PLAN.md) for the complete implementation roadmap.
## Contributing

We welcome contributions, especially:

- **Issue reports** - bug reports, feature requests, performance issues
- **Testing** - real-world usage feedback and edge case discovery
- **Documentation** - improvements and clarifications
- **Code contributions** - bug fixes and feature implementations

Please see [CONTRIBUTING.md](CONTRIBUTING.md) for guidelines.

## Testing

`VB.NET` Language Support is tested against:

- **Small projects** (~10 files) - unit test validation
- **Medium projects** (~50 files) - integration testing
- **DWSIM** (100+ files) - large real-world `VB.NET` codebase for performance validation
- **Multiple editors** - VS Code (automated) and Emacs (eglot) smoke coverage; Cursor/VSCodium planned

## License

MIT License - see [LICENSE](LICENSE) for details.

## Acknowledgments

- **Microsoft Roslyn team** - for the amazing .NET compiler platform
- **Microsoft C# extension team** - for the open-source LSP architecture reference
- **Samsung netcoredbg team** - for the open-source .NET debugger
- **Cliffback** - for macOS arm64 netcoredbg community builds
- **DWSIM project** - for providing a large real-world `VB.NET` codebase for testing

## Support

- **Issues**: [GitHub Issues](https://github.com/DNAKode/vbnet-lsp/issues)
- **Discussions**: [GitHub Discussions](https://github.com/DNAKode/vbnet-lsp/discussions)
- **Documentation**: [docs/](docs/)

---

**Built with focus on the `VB.NET` community. Designed for lasting infrastructure.**





