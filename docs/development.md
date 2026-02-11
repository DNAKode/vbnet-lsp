# Development Guide

**`VB.NET` Language Support - Developer Documentation**

Version: 1.0
Last Updated: 2026-02-05

## Table of Contents

1. [Prerequisites](#prerequisites)
2. [Environment Setup](#environment-setup)
3. [Building the Project](#building-the-project)
4. [Running Tests](#running-tests)
5. [Debugging](#debugging)
6. [Code Organization](#code-organization)
7. [Development Workflow](#development-workflow)
8. [CI/CD Pipeline](#cicd-pipeline)
9. [Release Process](#release-process)

---

## 1. Prerequisites

### Required Tools

- **.NET 10.0 SDK** or later
  - Download: https://dotnet.microsoft.com/download
  - Verify: `dotnet --version` (should be 10.0+)

- **Node.js 18.0** or later
  - Download: https://nodejs.org/
  - Verify: `node --version` (should be 18.0+)
  - Verify: `npm --version`

- **Visual Studio Code** 1.80.0 or later
  - Download: https://code.visualstudio.com/
  - Recommended extensions:
    - ESLint
    - Prettier

- **Git**
  - Download: https://git-scm.com/

### Line Endings (Repo Policy)

This repo defines line endings in `.gitattributes` to avoid local Git warnings and keep checkouts consistent:
- Default text files use LF.
- Windows scripts (`.bat`, `.cmd`) use CRLF.

This policy is local to this repository and does not change machine-wide Git settings.

### Bundled Components (No Manual Install Needed)

- **netcoredbg** (bundled for debugging)
  - Included in the VSIX for all supported platforms; users do not need to install it separately.
  - Curated binaries are referenced in `src/extension/scripts/netcoredbg-assets.json`.
  - Advanced override: set `NETCOREDBG_PATH` to use a custom build during packaging or local dev.
  - Fork for macOS arm64 investigation: https://github.com/DNAKode/netcoredbg
  - Community macOS arm64 builds: https://github.com/Cliffback/netcoredbg-macOS-arm64.nvim

### Optional Tools

- **Emacs** with eglot (current harness); lsp-mode optional for future coverage
  - Emacs: https://www.gnu.org/software/emacs/
  - lsp-mode: https://emacs-lsp.github.io/lsp-mode/
- **Helix** (manual LSP smoke checks; no headless harness yet)
  - Helix: https://helix-editor.com/

---

## 2. Environment Setup

### Clone the Repository

```bash
git clone https://github.com/DNAKode/vbnet-lsp.git
cd vbnet-lsp
```

### Initialize Submodules

```bash
# DWSIM test project (git submodule)
git submodule update --init --recursive
```

### Restore Dependencies

```bash
# Restore .NET dependencies
dotnet restore

# Restore Node.js dependencies for extension
cd src/extension
npm install
cd ../..
```

### Verify Setup

```bash
# Build language server
dotnet build src/VbNet.LanguageServer.Vb

# Build VS Code extension
cd src/extension
npm run compile
cd ../..

# Run tests
dotnet test
```

---

## 2.1 Local Development Directories

**IMPORTANT**: The following directories are gitignored and must be set up locally. These setup steps must be followed exactly to recreate the development environment.

### _external/ - Reference Repositories

Contains cloned reference repositories for architecture verification and pattern extraction. These are read-only references - we never modify them.

```bash
# Create directory (if not exists)
mkdir -p _external
cd _external

# C# for Visual Studio Code - PRIMARY REFERENCE
# Architecture patterns, LSP integration, TypeScript extension structure
git clone https://github.com/dotnet/vscode-csharp.git
# Size: ~200MB, Clone time: 2-5 minutes

# csharp-ls (standalone C# LSP reference)
# CLI surface, stdio startup conventions, and editor-wrapper patterns
# (Neovim/Emacs/VS Code plugin ecosystem)
git clone https://github.com/razzmatazz/csharp-language-server.git
# Size: ~20MB, Clone time: <1 minute

# netcoredbg (DNAKode fork) - Open-source .NET debugger
# DAP protocol reference, debugger integration patterns, macOS arm64 investigation
git clone https://github.com/DNAKode/netcoredbg.git
# Size: ~50MB, Clone time: <1 minute

# netcoredbg macOS arm64 community build (reference + releases)
git clone https://github.com/Cliffback/netcoredbg-macOS-arm64.nvim.git
# Size: ~5MB, Clone time: <1 minute

# (Optional) Roslyn - Deep compiler reference
# Only clone if needed for deep Roslyn API investigation
# git clone https://github.com/dotnet/roslyn.git
# Size: ~2GB, Clone time: 10-20 minutes
# WARNING: Very large repository

cd ..
```

**Verification**:
```bash
ls _external/
# Should show: vscode-csharp/  csharp-language-server/  netcoredbg/
```

### _external/ - Test Inputs (DWSIM)

Store large test inputs and reference repos under `_external/`. These are gitignored and can be re-cloned as needed.

```bash
# Create directory (if not exists)
mkdir -p _external
cd _external

# DWSIM - Large real-world `VB.NET` codebase
# Performance benchmarking, real-world validation, edge case discovery
git clone https://github.com/DanWBR/dwsim.git
# Size: ~500MB, Clone time: 5-10 minutes
# Note: Contains 100+ `VB.NET` files across multiple projects

cd ..
```

**Verification**:
```bash
ls _external/
# Should show: vscode-csharp/  netcoredbg/  dwsim/

# Verify DWSIM `VB.NET` content
find _external/dwsim -name "*.vb" | head -20
```

### test-explore/ - Unified Verification (Use in improvement cycles)

**IMPORTANT**: The `test-explore/` directory contains the headless harnesses for VS Code, `VB.NET` LSP, and Emacs. These are now part of the normal improvement cycle.

**Rules for this directory:**
1. **Run tests from here** as part of iterative improvement cycles
2. **Update harnesses when needed** to improve reliability and coverage
3. **Record outcomes** in `test-explore/TEST_RESULTS.md`
4. **Exclude incidental artifacts** (logs, downloaded runtimes) from commits
5. **Also use test/VbNet.LanguageServer.Tests.Vb/** for unit coverage

**What you SHOULD do:**
- Run `test-explore` harnesses during fixes and regressions
- Capture VS Code logs when needed (`CAPTURE_VSCODE_LOGS=1`, `CAPTURE_VBNET_TRACE=1`)
- Update `test-explore/TEST_RESULTS.md` with test outcomes
- Follow `test-explore/README.md` for log retention and harness conventions

### Directory Structure After Setup

```
vbnet-lsp/
|-- _external/                    # Gitignored - reference repos + large inputs
|   |-- vscode-csharp/           # C# extension (primary reference)
|   |-- csharp-language-server/  # Standalone C# LSP reference (CLI + editor wrappers)
|   |-- netcoredbg/              # netcoredbg fork (macOS arm64 builds)
|   |-- netcoredbg-macOS-arm64.nvim/ # netcoredbg macOS arm64 community build
|   |-- roslyn/                  # Roslyn source (optional)
|   `-- dwsim/                   # Large `VB.NET` test project
|-- test-explore/           # Tracked - exploratory harnesses (logs excluded)
|-- src/                         # Tracked - our source code
|-- test/                        # Tracked - our test code (USE THIS!)
`-- docs/                        # Tracked - documentation
```

### Why These Are Gitignored

1. **Size**: Reference repos and inputs are large (vscode-csharp ~200MB, roslyn ~2GB, DWSIM ~500MB)
2. **External ownership**: These repos are maintained by others
3. **Reproducibility**: Clone commands are documented; anyone can recreate
4. **Cleanliness**: Keeps our repo focused on our code

### Updating Reference Repositories

Periodically update to get latest changes:

```bash
# Update C# extension reference
cd _external/vscode-csharp && git pull && cd ../..

# Update standalone C# language server reference
cd _external/csharp-language-server && git pull && cd ../..

# Update netcoredbg reference (DNAKode fork)
cd _external/netcoredbg && git pull && cd ../..

# Update netcoredbg macOS arm64 community build reference
cd _external/netcoredbg-macOS-arm64.nvim && git pull && cd ../..

# Update DWSIM test project
cd _external/dwsim && git pull && cd ../..
```

---

## 3. Building the Project

### Build Language Server

```bash
# Debug build
dotnet build src/VbNet.LanguageServer.Vb

# Release build
dotnet build src/VbNet.LanguageServer.Vb -c Release

# Publish for distribution
dotnet publish src/VbNet.LanguageServer.Vb -c Release -o publish
```

### Build VS Code Extension

```bash
cd src/extension

# Compile TypeScript
npm run compile

# Watch mode (for development)
npm run watch

# Package extension (.vsix)
npm run package
```

---

## 4. Running Tests

### Unit Tests (.NET)

```bash
# Run language server tests
dotnet test test/VbNet.LanguageServer.Tests.Vb/VbNet.LanguageServer.Tests.Vb.vbproj

# Run extension manifest tests (CI-safe, no VS Code required)
dotnet test test/VbNet.Extension.Tests.Vb/VbNet.Extension.Tests.Vb.vbproj

# Run with coverage
dotnet test test/VbNet.LanguageServer.Tests.Vb/VbNet.LanguageServer.Tests.Vb.vbproj --collect:"XPlat Code Coverage"

# Run specific test
dotnet test --filter "FullyQualifiedName~CompletionServiceTests"
```

### Integration Tests

```bash
# Run the DWSIM validation script (optional)
./scripts/test-dwsim.sh
```

### Extension Tests (TypeScript)

```bash
cd src/extension

# Run extension tests
npm test

# Run tests in watch mode
npm run test:watch
```

### Debugging Harness (VS Code)

The VS Code harness can exercise netcoredbg-based debug sessions using the bundled debugger (or an override).

```powershell
# Build debug fixture
dotnet build test/TestProjects/DebugConsole/DebugConsole.vbproj

# Prepare bundled debugger (or set NETCOREDBG_PATH to override)
cd src/extension
npm run bundle-debugger -- --target win32-x64
cd ../../test-explore/clients/vscode

# Run harness (skips debug test if netcoredbg is missing)
$env:FIXTURE_WORKSPACE = "test/TestProjects/DebugConsole"
$env:NETCOREDBG_PATH = "C:\\tools\\netcoredbg\\netcoredbg.exe" # optional override
npm test
```

### Packaging netcoredbg for different platforms

The extension bundles netcoredbg via `npm run bundle-debugger`. If `NETCOREDBG_PATH` is not set, the script downloads a curated binary listed in `src/extension/scripts/netcoredbg-assets.json` and copies it into `.debugger/` alongside `LICENSE.netcoredbg`. macOS arm64 uses the Cliffback community build, with an extra notice file copied into `.debugger/`.

```bash
# PowerShell
cd src/extension
npm run bundle-debugger -- --target linux-x64
```

To override the curated binary (for example, a local build), set:

```bash
# PowerShell
$env:NETCOREDBG_PATH = "C:\\path\\to\\netcoredbg"
$env:NETCOREDBG_LICENSE = "C:\\path\\to\\LICENSE"
cd src/extension
npm run bundle-debugger
```

`NETCOREDBG_PATH` can point to either `netcoredbg.exe` (Windows) or `netcoredbg` (Linux/macOS). The file is copied into `.debugger/` with the same filename so platform-specific VSIX builds can include the correct binary.

**Tip:** Build platform-specific VSIX packages on the same OS when possible so file permissions (executable bit) are preserved for `netcoredbg`.

### Multi-Editor Tests (Emacs)

```bash
# Requires Emacs (eglot is built-in)
./test-explore/clients/emacs/run-tests.ps1 -Suite vbnet
```

### Helix Manual Smoke Check (stdio)

Helix uses stdio for LSP. Use the project-local `languages.toml` template and the helper script:

```powershell
test-explore/clients/helix/run-helix.ps1
```

If `hx` is not on PATH, pass the path explicitly:

```powershell
test-explore/clients/helix/run-helix.ps1 -HelixExe C:\Tools\Helix\hx.exe -ServerExe C:\path\to\VbNet.LanguageServer.exe
```

---

## WSL/Linux Test Notes

When running the VS Code harness inside WSL:
- Ensure Node.js 20, .NET 10 (local install under `~/.dotnet` is fine), and `xvfb` are installed.
- Prepare the bundled Linux debugger with `npm run bundle-debugger -- --target linux-x64` (or set `NETCOREDBG_PATH` to a Linux-built netcoredbg binary).
- Use `xvfb-run -a` to provide a headless display.
- VS Code CLI may print a WSL warning prompt; it’s safe to continue in CI-style runs.

Example (WSL):

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

---

## macOS arm64 Debugger Validation Plan (In Progress)

1. **Baseline first (Windows + Linux)**  
   - Ensure `test/` suites pass on Windows.  
   - Ensure VS Code harness debug tests pass on Windows.  
   - Ensure WSL/Linux harness debug tests pass (using Linux VSIX).

2. **Manual macOS arm64 validation (local machine)**  
   - Use the `package-vsix` workflow to fetch the `darwin-arm64` VSIX.  
   - Install VSIX locally on macOS arm64 and run the VS Code harness (debug test included).  
   - Record results in `test-explore/TEST_RESULTS.md`.

3. **GitHub Actions macOS arm64 pilot**  
   - Create a `workflow_dispatch` action targeting `macos-14` runners.  
   - Steps: install Node/.NET, bundle debugger (`npm run bundle-debugger -- --target darwin-arm64`), build debug fixture, run VS Code harness tests.  
   - Keep the job gated on green Windows + Linux harness runs to avoid spurious macOS failures.

4. **Stabilize + expand**  
   - Add debugger-specific assertions/log capture if macOS flakiness appears.  
   - Promote to a scheduled or PR-triggered workflow only after multiple green runs.

## Future Test-Explore Notes

- Consider adding NeoVim coverage for `VB.NET` LSP + debugger integration (possibly with `neotest`), across all supported platforms.
- Track Helix automation options; current coverage is manual only (stdio config under `test-explore/clients/helix`).

## 5. Debugging

### Debugging the Language Server

#### From VS Code

1. Open the project in VS Code
2. Set breakpoints in VB.NET code
3. Press F5 or use "Run > Start Debugging"
4. Select ".NET Core Launch (Language Server)" configuration

#### Attach to Running Process

1. Start the language server manually:
   ```bash
   dotnet run --project src/VbNet.LanguageServer.Vb
   ```
2. In VS Code: "Run > Attach to Process"
3. Select the `VbNet.LanguageServer` process

#### Logging

Enable detailed logging by setting environment variable:

```bash
export VBNET_LS_LOG_LEVEL=Trace
dotnet run --project src/VbNet.LanguageServer.Vb
```

Logs are written to stderr.

### Debugging the VS Code Extension

#### Extension Development Host

1. Open `src/extension` in VS Code
2. Press F5 or use "Run > Start Debugging"
3. Select "Extension" launch configuration
4. A new VS Code window opens (Extension Development Host)
5. Open a `VB.NET` project in the Extension Development Host
6. Set breakpoints in TypeScript code

#### Extension Logs

View extension logs:
1. In Extension Development Host: "View > Output"
2. Select "`VB.NET` Language Support" from dropdown

### Debugging LSP Communication

Enable LSP tracing:

1. VS Code Settings: `vbnet.trace.server` = `"verbose"`
2. View LSP messages: "View > Output" > "`VB.NET` Language Support"

---

## 6. Code Organization

### Language Server Structure

```
src/VbNet.LanguageServer.Vb/
├── Protocol/           # LSP protocol layer
│   ├── JsonRpcTransport.vb
│   ├── LspMessageHandler.vb
│   └── LspTypes.vb
├── Core/               # Server core
│   ├── LanguageServer.vb
│   ├── RequestRouter.vb
│   └── ServerLifecycle.vb
├── Workspace/          # Workspace management
│   ├── WorkspaceManager.vb
│   ├── DocumentManager.vb
│   ├── ProjectLoader.vb
│   └── FileSystemWatcher.vb
├── Services/           # LSP features
│   ├── DiagnosticsService.vb
│   ├── CompletionService.vb
│   ├── HoverService.vb
│   ├── DefinitionService.vb
│   └── ... (other services)
└── Program.vb          # Entry point
```

### Extension Structure

```
src/extension/
├── src/
│   ├── extension.ts            # Activation entry point
│   ├── languageClient.ts       # LSP client setup
│   ├── commands/               # VS Code commands
│   └── features/               # UI integrations
├── package.json                # Extension manifest
└── tsconfig.json               # TypeScript config
```

### Test Structure

```
test/
├── VbNet.LanguageServer.Tests.Vb/  # Unit tests (VB.NET)
│   ├── Services/                   # Service tests
│   ├── Workspace/                  # Workspace tests
│   └── Protocol/                   # Protocol tests
├── VbNet.Extension.Tests.Vb/       # Extension manifest tests (VB.NET)
├── extension.test/                 # Extension tests (TS)
└── TestProjects/                   # Test projects
    ├── SmallProject/
    ├── MediumProject/
    └── dwsim/                      # Git submodule
```

---

## 7. Development Workflow

### Making Changes

1. **Create a branch**
   ```bash
   git checkout -b feature/your-feature-name
   ```

2. **Make changes** following code conventions

3. **Write tests** for new functionality

4. **Run tests** to ensure nothing broke
   ```bash
   dotnet test
   cd src/extension && npm test
   ```

5. **Build and verify**
   ```bash
   dotnet build -c Release
   cd src/extension && npm run compile
   ```

6. **Commit changes**
   ```bash
   git add .
   git commit -m "feat: Add feature description"
   ```

7. **Push and create pull request**
   ```bash
   git push origin feature/your-feature-name
   ```

### Code Conventions

#### VB.NET Code Style

- Follow standard VB.NET naming conventions (PascalCase for types/methods, camelCase for locals)
- Use `Async`/`Await` for all I/O operations
- Always pass `CancellationToken` to Roslyn APIs
- Document public APIs with XML comments
- Keep methods focused and small (<50 lines typical)

#### TypeScript Code Style

- Use TypeScript strict mode
- Prefer `const` over `let`
- Use async/await for asynchronous operations
- Follow VS Code extension API patterns

**Example:**

```typescript
export async function activate(context: vscode.ExtensionContext) {
    const serverOptions: ServerOptions = {
        command: 'dotnet',
        args: ['run', '--project', 'src/VbNet.LanguageServer.Vb']
    };

    const clientOptions: LanguageClientOptions = {
        documentSelector: [{ scheme: 'file', language: 'vb' }]
    };

    const client = new LanguageClient(
        'vbnetLanguageServer',
        '`VB.NET` Language Server',
        serverOptions,
        clientOptions
    );

    await client.start();
    context.subscriptions.push(client);
}
```

### Commit Message Format

Follow Conventional Commits:

```
<type>(<scope>): <description>

[optional body]

[optional footer]
```

**Types:**
- `feat`: New feature
- `fix`: Bug fix
- `docs`: Documentation changes
- `refactor`: Code refactoring
- `test`: Adding or updating tests
- `chore`: Build/tooling changes
- `perf`: Performance improvements

**Examples:**
```
feat(completion): Add keyword completion support

fix(diagnostics): Prevent duplicate diagnostics on file save

docs(architecture): Update LSP feature implementation section

test(integration): Add DWSIM performance benchmarks
```

---

## 8. CI/CD Pipeline

### GitHub Actions Workflows

#### ci.yml - Fast Tests (Windows)

Triggers: Push to `master`/`main`, all PRs
Current scope: Windows-only (multi-platform planned).

```yaml
- Run language server unit/integration tests (test/VbNet.LanguageServer.Tests.Vb)
- Run extension manifest checks (test/VbNet.Extension.Tests)
- Bundle Roslyn LSP (win-x64) and validate `.roslyn` + `.roslyn-vb` layout
```

#### Editor Adapter Validation

- **editor-adapters.yml**: Multi-editor smoke validation (Neovim + Emacs harnesses on Windows)

#### Planned (not yet in repo)

- **integration.yml**: DWSIM validation
- **performance.yml**: Nightly performance checks

#### Packaging and Release Workflows

Workflows available via `workflow_dispatch` (manual trigger):
- **editor-adapters.yml**: Run Neovim + Emacs smoke tests against current server build.
- **package-vsix.yml**: Build a VSIX artifact for a selected target.
- **publish-vsix.yml**: Build and publish a VSIX to the Marketplace.
- **release.yml**: Build standalone language server archives + VSIX files and publish a GitHub Release.
- **publish-dotnet-tool.yml**: Pack and publish the `DNAKode.VbNet.Lsp` global tool package (command: `vbnet-ls`) to NuGet.

Adapter repo sync/publish guidance:
- `adapters/scripts/export-adapter-repos.ps1`
- `docs/adapter-release-checklist.md`

All workflows bundle the curated netcoredbg assets listed in `src/extension/scripts/netcoredbg-assets.json`,
bundle Roslyn LSP assets via NuGet, and validate that `.roslyn` + `.roslyn-vb` are present in the VSIX.
`publish-vsix.yml` requires the `VSCE_PAT` secret (Marketplace PAT with publish rights).
`publish-dotnet-tool.yml` publishes only when `NUGET_API_KEY` is configured.

### CI Duration Note (Tracking)

CI runs on GitHub Actions have been longer than expected in some recent runs. Track this and investigate if a single test or project dominates runtime (or if the overall duration is acceptable for the coverage). If needed, profile test durations and split the workflow or add test filters.

##### Running the workflows

1. Open the GitHub Actions tab.
2. Select **editor-adapters**, **package-vsix**, **publish-vsix**, **release**, or **publish-dotnet-tool**.
3. Click **Run workflow** and set:
   - `target`: `win32-x64` (default) or one of the listed targets.
4. For **publish-vsix**, ensure the `marketplace` environment is approved and `VSCE_PAT` is set.
5. For **release**, provide a `tag` (for example `v0.1.9`) and choose `pre_release`/`draft` flags.
6. For **publish-dotnet-tool**, provide `version` only when running manually without a `v*` tag context.

### Running CI Locally

```bash
# Install act (GitHub Actions local runner)
# https://github.com/nektos/act

# Run CI workflow
act -j test

# Planned integration workflow (when added)
# act -j integration
```

---

## 9. Release Process

### Versioning

Follow Semantic Versioning (SemVer 2.0):
- **MAJOR**: Breaking changes
- **MINOR**: New features (backward compatible)
- **PATCH**: Bug fixes

#### Development Preview Policy

- Every Marketplace publish must use a new version (no reusing a version number).
- During active development, bump the PATCH version for each preview publish (e.g., 0.1.2, 0.1.3, ...).
- Keep `"preview": true` in `src/extension/package.json` for pre-release channels.
- Tag preview publishes with a date-stamped Git tag (example: `v0.1.2-preview.YYYYMMDD`).
- When a feature set is stable, bump MINOR and keep the same workflow; drop `"preview": true` only when ready for a non-preview release.

### Pre-Release Checklist

- [ ] All tests passing (unit, integration, E2E)
- [ ] Documentation updated
- [ ] CHANGELOG.md updated (root) and synced into `src/extension/CHANGELOG.md` (run `npm run package` or `npm run copy-changelog`)
- [ ] Performance targets met
- [ ] No P0/P1 bugs open
- [ ] Cross-platform testing complete (Windows, macOS, Linux)
- [ ] Multi-editor testing complete (VS Code, Emacs)

### Release Steps

1. **Update version number**
   ```bash
   # Update version in:
   # - src/extension/package.json
   # Dotnet tool package version comes from the git tag (vX.Y.Z -> X.Y.Z)
   ```

2. **Update CHANGELOG.md**
   ```markdown
   ## [1.0.0] - 2026-01-10

   ### Added
   - Feature 1
   - Feature 2

   ### Fixed
   - Bug fix 1
   ```

   Then sync it into the extension bundle:

   ```bash
   cd src/extension
   npm run copy-changelog
   cd ../..
   ```

3. **Commit version bump**
   ```bash
   git add .
   git commit -m "chore: Bump version to 1.0.0"
   git tag v1.0.0
   git push origin main --tags
   ```

4. **Run the release workflow**
   - Push a tag (`v*`) to trigger `.github/workflows/release.yml`, or run it manually via `workflow_dispatch`.
   - The workflow builds language server archives (`win-x64`, `linux-x64`, `osx-x64`, `osx-arm64`) and VSIX packages, then attaches them to a GitHub Release.

5. **Optional: publish dotnet tool package**
   - Run `.github/workflows/publish-dotnet-tool.yml`, or rely on the same `v*` tag trigger.
   - Ensure `NUGET_API_KEY` is configured to publish to NuGet.

6. **Optional: publish to Marketplace**
   - Run `.github/workflows/publish-vsix.yml` for the target you want to publish.
   - Ensure the `marketplace` environment is approved and `VSCE_PAT` is set.

7. **Announce release**
   - Update README.md
   - Post to discussions/announcements

---

## Troubleshooting

### Common Issues

#### "MSBuild not found"

**Solution**: Install .NET SDK and ensure `dotnet` is in PATH

```bash
dotnet --version  # Verify installation
```

#### "Extension fails to activate"

**Solution**: Check extension logs

1. "View > Output" in VS Code
2. Select "`VB.NET` Language Support" from dropdown
3. Look for error messages

#### "Language server not responding"

**Solution**: Restart language server

1. VS Code Command Palette (Ctrl+Shift+P)
2. "`VB.NET`: Restart Language Server"

Or check if server process is running:

```bash
ps aux | grep vbnet-ls  # Linux/macOS
tasklist | findstr vbnet-ls  # Windows
```

#### "Tests fail with 'SDK not found'"

**Solution**: Set MSBuildPath explicitly

```bash
export MSBuildPath=/path/to/dotnet/sdk/10.0.100/MSBuild.dll
dotnet test
```

---

## Additional Resources

- [Architecture Documentation](architecture.md)
- [Configuration Guide](configuration.md)
- [Feature Support Matrix](features.md)
- [Project Plan](../PROJECT_PLAN.md)
- [C# Extension Reference](https://github.com/dotnet/vscode-csharp)
- [csharp-ls Reference](https://github.com/razzmatazz/csharp-language-server)
- [LSP Specification](https://microsoft.github.io/language-server-protocol/)
- [Roslyn API Documentation](https://github.com/dotnet/roslyn/tree/main/docs)

---

**Last Updated**: 2026-02-05

**Maintained by**: `VB.NET` Language Support Contributors





