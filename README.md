# VB.NET Language Support

Open-source VB.NET language tooling built around Roslyn:

- A VS Code extension: **VB.NET Language Support**
- Standalone language server binaries for non-VS Code LSP clients
- Thin editor adapters for non-VS Code clients (Neovim, Emacs)

[![License: MIT](https://img.shields.io/badge/License-MIT-yellow.svg)](https://opensource.org/licenses/MIT)

## Choose How To Use It

### 1. VS Code extension (recommended for most users)

- Marketplace: https://marketplace.visualstudio.com/items?itemName=dnakode.vbnet-language-support
- VSIX packages are also attached to GitHub Releases for manual install
- Includes:
  - VB.NET language server backend (default)
  - Optional Roslyn backend support
  - Bundled netcoredbg for debugging

### 2. Standalone language server binaries (non-VS Code clients)

Install as a global .NET tool (recommended):

```bash
dotnet tool install --global DNAKode.VbNet.Lsp
```

Or download from GitHub Releases:

- Releases: https://github.com/DNAKode/vbnet-lsp/releases
- Language server artifacts:
  - `vbnet-language-server-win-x64.zip`
  - `vbnet-language-server-linux-x64.tar.gz`
  - `vbnet-language-server-osx-x64.tar.gz`
  - `vbnet-language-server-osx-arm64.tar.gz`
- VSIX artifacts:
  - `vbnet-language-support-win32-x64.vsix`
  - `vbnet-language-support-linux-x64.vsix`
  - `vbnet-language-support-darwin-x64.vsix`
  - `vbnet-language-support-darwin-arm64.vsix`

Use these with any LSP client that supports stdio or named pipes.

### 3. Editor adapters (thin wrappers)

Use editor-native adapters that launch the standalone language server:

- Neovim adapter source: `adapters/nvim/vbnet-lsp.nvim`
- Emacs `eglot` adapter source: `adapters/emacs/vbnet-eglot`

Packaging guidance for native channels is documented in
[docs/editor-packaging.md](docs/editor-packaging.md).

## What You Get

- Roslyn-backed semantic analysis and project loading (`.sln`, `.slnf`, `.slnx`, `.vbproj`)
- Core language features: diagnostics, completion, hover, definition, references, rename, symbols
- Advanced navigation: type definition, implementation, call hierarchy, type hierarchy
- Editing support: formatting, semantic tokens, signature help, folding ranges, code actions, and Roslyn-backed Extract Method
- Debugger integration in the VS Code extension via bundled netcoredbg

Current implementation details and roadmap are tracked in [PROJECT_PLAN.md](PROJECT_PLAN.md) and [docs/features.md](docs/features.md).

## Quick Start

### VS Code

1. Install the extension from the Marketplace.
2. Open a folder containing a `.sln` or `.vbproj`.
3. Start coding in `.vb` files.

Useful commands:

- `VB.NET: Select Workspace Context`
- `VB.NET: Select Workspace Solution`
- `VB.NET: Show Logs`
- `VB.NET: Toggle LSP Trace`
- `VB.NET: Reload Workspace`

The status bar shows the active VB.NET context: a loaded solution, one selected project,
all discovered projects in Workspace Dev Mode, or `Select Context` when multiple solutions
are present and an explicit choice is needed. Restore and test commands follow the selected
context when no active file gives a more specific project target.

### Non-VS Code (LSP client)

1. Install the global tool (`dotnet tool install --global DNAKode.VbNet.Lsp`) or download and extract the server artifact for your platform from [Releases](https://github.com/DNAKode/vbnet-lsp/releases).
2. Configure your editor/client to launch the server with `--stdio`.

Examples:

```bash
# Global tool
vbnet-ls --stdio

# Linux/macOS (app host)
./VbNet.LanguageServer --stdio

# Linux/macOS/Windows (dotnet host)
dotnet VbNet.LanguageServer.dll --stdio

# Windows (app host)
VbNet.LanguageServer.exe --stdio
```

The server also supports `--pipe` (named pipe transport), `--logLevel`, and `--msbuildPath`.

## Prerequisites

- .NET SDK 10.0 or later
- For VS Code extension development: Node.js 18+
- For VS Code users: VS Code 1.80+

Notes:

- Dev containers / SSH / WSL are supported if .NET SDK is available in the runtime environment.
- VS Code Web (`vscode.dev` / `github.dev`) is not supported.

## Release Automation

GitHub Actions release automation is available in `.github/workflows/release.yml` and publishes:

- Platform-specific standalone language server archives
- Platform-specific VSIX packages
- A GitHub Release containing all artifacts

The dotnet tool package (`DNAKode.VbNet.Lsp`, command: `vbnet-ls`) is built in
`.github/workflows/publish-dotnet-tool.yml` and published to NuGet when
`NUGET_API_KEY` is configured.

Editor adapters are validated separately in `.github/workflows/editor-adapters.yml`
and are intended for editor-native distribution channels (for example, Neovim
plugin managers and MELPA/package-vc for Emacs).

Downstream snapshot sync guidance (adapters + Claude plugin) is documented in
[docs/downstream-repositories.md](docs/downstream-repositories.md).

The release workflow runs on tag push (`v*`) and can also be run manually with `workflow_dispatch`.

## Backend Model

The extension supports backend selection:

- `vbnet` (default)
- `roslyn`

Only one backend is active at a time by design (single active backend) to reduce regression risk.

For Roslyn packaging constraints (`.roslyn` + `.roslyn-vb` split), see [docs/roslyn-packaging.md](docs/roslyn-packaging.md).

## Documentation

- [Architecture](docs/architecture.md)
- [Development Guide](docs/development.md)
- [Configuration](docs/configuration.md)
- [Feature Matrix](docs/features.md)
- [Roslyn Packaging](docs/roslyn-packaging.md)
- [Roslyn Comparison Notes](docs/roslyn-lsp-comparison.md)
- [Editor Adapter Packaging](docs/editor-packaging.md)
- [Adapter Release Checklist](docs/adapter-release-checklist.md)
- [Claude Plugin Marketplace Plan](docs/claude-plugin-marketplace.md)
- [Downstream Repositories](docs/downstream-repositories.md)
- [Release Artifacts](RELEASE_ARTIFACTS.md)

## Development and Testing

Build from source:

```bash
dotnet build src/VbNet.LanguageServer.Vb
dotnet test
cd src/extension && npm ci && npm run compile
```

For full workflows and exploratory harnesses, see:

- [docs/development.md](docs/development.md)
- [test-explore/TEST_SUITE.md](test-explore/TEST_SUITE.md)
- [test-explore/TEST_RESULTS.md](test-explore/TEST_RESULTS.md)

## Contributing

Issues, bug reports, docs improvements, and code contributions are welcome.

- Issues: https://github.com/DNAKode/vbnet-lsp/issues
- Discussions: https://github.com/DNAKode/vbnet-lsp/discussions

## License

MIT - see [LICENSE](LICENSE).





