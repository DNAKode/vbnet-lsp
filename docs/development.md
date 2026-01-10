# Development Guide

**VB.NET Language Support - Developer Documentation**

Version: 1.0
Last Updated: 2026-01-09

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
    - C# for Visual Studio Code
    - ESLint
    - Prettier

- **Git**
  - Download: https://git-scm.com/

### Optional Tools

- **Samsung netcoredbg** (for debugging testing)
  - Repository: https://github.com/Samsung/netcoredbg
  - Installation: Follow platform-specific instructions

- **Emacs** with lsp-mode (for multi-editor testing)
  - Emacs: https://www.gnu.org/software/emacs/
  - lsp-mode: https://emacs-lsp.github.io/lsp-mode/

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
dotnet build src/VbNet.LanguageServer

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

# Samsung netcoredbg - Open-source .NET debugger
# DAP protocol reference, debugger integration patterns
git clone https://github.com/Samsung/netcoredbg.git
# Size: ~50MB, Clone time: <1 minute

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
# Should show: vscode-csharp/  netcoredbg/
```

### _test/ - Test Infrastructure

Contains test projects for validation and performance testing. DWSIM is particularly important for real-world performance validation.

```bash
# Create directory (if not exists)
mkdir -p _test
cd _test

# DWSIM - Large real-world VB.NET codebase
# Performance benchmarking, real-world validation, edge case discovery
git clone https://github.com/DanWBR/dwsim.git
# Size: ~500MB, Clone time: 5-10 minutes
# Note: Contains 100+ VB.NET files across multiple projects

cd ..
```

**Verification**:
```bash
ls _test/
# Should show: dwsim/

# Verify DWSIM VB.NET content
find _test/dwsim -name "*.vb" | head -20
```

### _test/codex-tests/ - Independent Verification (DO NOT USE DIRECTLY)

**IMPORTANT**: The `_test/codex-tests/` directory contains an independent test verification suite maintained separately from regular development. This suite is used for external validation of the language server by an independent reviewer.

**Rules for this directory:**
1. **DO NOT run tests from here** during regular development
2. **DO NOT modify files** in this directory
3. **Read-only access** - You may read the test suite and results documents to understand testing status
4. **Exclude from commits** - When committing, do not stage changes from `_test/codex-tests/`
5. **Use test/VbNet.LanguageServer.Tests/** for regular development testing instead

**What you CAN do:**
- Read `_test/codex-tests/INDEPENDENT_TEST_SUITE.md` to understand test coverage
- Read `_test/codex-tests/INDEPENDENT_TEST_RESULTS.md` to see independent verification status
- Act on findings from these documents (fix issues found by independent testing)

### Directory Structure After Setup

```
vbnet-lsp/
├── _external/                    # Gitignored - reference repos
│   ├── vscode-csharp/           # C# extension (primary reference)
│   └── netcoredbg/              # Samsung debugger
├── _test/                        # Gitignored - test infrastructure
│   ├── codex-tests/             # Independent verification (DO NOT USE)
│   └── dwsim/                   # Large VB.NET test project
├── src/                          # Tracked - our source code
├── test/                         # Tracked - our test code (USE THIS!)
└── docs/                         # Tracked - documentation
```

### Why These Are Gitignored

1. **Size**: Reference repos are large (vscode-csharp ~200MB, roslyn ~2GB)
2. **External ownership**: These repos are maintained by others
3. **Reproducibility**: Clone commands are documented; anyone can recreate
4. **Cleanliness**: Keeps our repo focused on our code

### Updating Reference Repositories

Periodically update to get latest changes:

```bash
# Update C# extension reference
cd _external/vscode-csharp && git pull && cd ../..

# Update netcoredbg reference
cd _external/netcoredbg && git pull && cd ../..

# Update DWSIM test project
cd _test/dwsim && git pull && cd ../..
```

---

## 3. Building the Project

### Build Language Server

```bash
# Debug build
dotnet build src/VbNet.LanguageServer

# Release build
dotnet build src/VbNet.LanguageServer -c Release

# Publish for distribution
dotnet publish src/VbNet.LanguageServer -c Release -o publish
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
# Run all language server tests
dotnet test src/VbNet.LanguageServer.Tests

# Run with coverage
dotnet test src/VbNet.LanguageServer.Tests --collect:"XPlat Code Coverage"

# Run specific test
dotnet test --filter "FullyQualifiedName~CompletionServiceTests"
```

### Integration Tests

```bash
# Run end-to-end tests
dotnet test test/VbNet.IntegrationTests

# Run against DWSIM project
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

### Multi-Editor Tests (Emacs)

```bash
# Requires Emacs and lsp-mode installed
./scripts/test-emacs-lsp.sh
```

---

## 5. Debugging

### Debugging the Language Server

#### From VS Code

1. Open the project in VS Code
2. Set breakpoints in C# code
3. Press F5 or use "Run > Start Debugging"
4. Select ".NET Core Launch (Language Server)" configuration

#### Attach to Running Process

1. Start the language server manually:
   ```bash
   dotnet run --project src/VbNet.LanguageServer
   ```
2. In VS Code: "Run > Attach to Process"
3. Select the `VbNet.LanguageServer` process

#### Logging

Enable detailed logging by setting environment variable:

```bash
export VBNET_LS_LOG_LEVEL=Trace
dotnet run --project src/VbNet.LanguageServer
```

Logs are written to stderr.

### Debugging the VS Code Extension

#### Extension Development Host

1. Open `src/extension` in VS Code
2. Press F5 or use "Run > Start Debugging"
3. Select "Extension" launch configuration
4. A new VS Code window opens (Extension Development Host)
5. Open a VB.NET project in the Extension Development Host
6. Set breakpoints in TypeScript code

#### Extension Logs

View extension logs:
1. In Extension Development Host: "View > Output"
2. Select "VB.NET Language Support" from dropdown

### Debugging LSP Communication

Enable LSP tracing:

1. VS Code Settings: `vbnetLs.trace.server` = `"verbose"`
2. View LSP messages: "View > Output" > "VB.NET Language Support"

---

## 6. Code Organization

### Language Server Structure

```
src/VbNet.LanguageServer/
├── Protocol/           # LSP protocol layer
│   ├── JsonRpcTransport.cs
│   ├── LspMessageHandler.cs
│   └── LspTypes.cs
├── Core/               # Server core
│   ├── LanguageServer.cs
│   ├── RequestRouter.cs
│   └── ServerLifecycle.cs
├── Workspace/          # Workspace management
│   ├── WorkspaceManager.cs
│   ├── DocumentManager.cs
│   ├── ProjectLoader.cs
│   └── FileSystemWatcher.cs
├── Services/           # LSP features
│   ├── DiagnosticsService.cs
│   ├── CompletionService.cs
│   ├── HoverService.cs
│   ├── DefinitionService.cs
│   └── ... (other services)
└── Program.cs          # Entry point
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
├── VbNet.LanguageServer.Tests/  # Unit tests (C#)
│   ├── Services/                 # Service tests
│   ├── Workspace/                # Workspace tests
│   └── Protocol/                 # Protocol tests
├── VbNet.IntegrationTests/       # E2E tests (C#)
├── extension.test/               # Extension tests (TS)
└── TestProjects/                 # Test projects
    ├── SmallProject/
    ├── MediumProject/
    └── dwsim/                    # Git submodule
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

#### C# Code Style

- Follow standard C# naming conventions (PascalCase for types/methods, camelCase for locals)
- Use `async`/`await` for all I/O operations
- Always pass `CancellationToken` to Roslyn APIs
- Document public APIs with XML comments
- Keep methods focused and small (<50 lines typical)

**Example:**

```csharp
/// <summary>
/// Provides completion items for the specified document position.
/// </summary>
public async Task<CompletionList> GetCompletionAsync(
    CompletionParams params,
    CancellationToken cancellationToken)
{
    var document = GetDocument(params.TextDocument.Uri);
    cancellationToken.ThrowIfCancellationRequested();

    var completionService = CompletionService.GetService(document);
    var completions = await completionService
        .GetCompletionsAsync(document, position, cancellationToken);

    return TranslateToLsp(completions);
}
```

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
        args: ['run', '--project', 'src/VbNet.LanguageServer']
    };

    const clientOptions: LanguageClientOptions = {
        documentSelector: [{ scheme: 'file', language: 'vb' }]
    };

    const client = new LanguageClient(
        'vbnetLanguageServer',
        'VB.NET Language Server',
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

#### ci.yml - Build and Test

Triggers: Push to main, all PRs

```yaml
- Build language server (.NET)
- Build extension (TypeScript)
- Run unit tests
- Run integration tests
- Code coverage report
```

#### emacs-lsp.yml - Multi-Editor Testing

Triggers: Push to main, all PRs

```yaml
- Setup Ubuntu + Emacs + lsp-mode
- Build language server
- Run Emacs LSP tests in batch mode
- Validate core LSP features
```

#### integration.yml - DWSIM Validation

Triggers: Push to main, nightly

```yaml
- Clone DWSIM project
- Load solution with language server
- Measure startup time
- Validate diagnostics
- Test navigation features
```

#### performance.yml - Performance Testing

Triggers: Nightly, manual

```yaml
- Run performance benchmarks
- Memory profiling
- Latency measurements
- Generate performance report
```

#### release.yml - Publish

Triggers: Version tags (v*.*.*)

```yaml
- Build release artifacts
- Package VS Code extension (.vsix)
- Publish to VS Code Marketplace
- Publish to Open VSX Registry
- Create GitHub release
```

### Running CI Locally

```bash
# Install act (GitHub Actions local runner)
# https://github.com/nektos/act

# Run CI workflow
act -j build

# Run integration tests
act -j integration
```

---

## 9. Release Process

### Versioning

Follow Semantic Versioning (SemVer 2.0):
- **MAJOR**: Breaking changes
- **MINOR**: New features (backward compatible)
- **PATCH**: Bug fixes

### Pre-Release Checklist

- [ ] All tests passing (unit, integration, E2E)
- [ ] Documentation updated
- [ ] CHANGELOG.md updated
- [ ] Performance targets met
- [ ] No P0/P1 bugs open
- [ ] Cross-platform testing complete (Windows, macOS, Linux)
- [ ] Multi-editor testing complete (VS Code, Emacs)

### Release Steps

1. **Update version number**
   ```bash
   # Update version in:
   # - src/VbNet.LanguageServer/VbNet.LanguageServer.csproj
   # - src/extension/package.json
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

3. **Commit version bump**
   ```bash
   git add .
   git commit -m "chore: Bump version to 1.0.0"
   git tag v1.0.0
   git push origin main --tags
   ```

4. **CI automatically publishes:**
   - Builds release artifacts
   - Packages extension
   - Publishes to marketplaces
   - Creates GitHub release

5. **Announce release**
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
2. Select "VB.NET Language Support" from dropdown
3. Look for error messages

#### "Language server not responding"

**Solution**: Restart language server

1. VS Code Command Palette (Ctrl+Shift+P)
2. "VB.NET: Restart Language Server"

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
- [LSP Specification](https://microsoft.github.io/language-server-protocol/)
- [Roslyn API Documentation](https://github.com/dotnet/roslyn/tree/main/docs)

---

**Last Updated**: 2026-01-10

**Maintained by**: VB.NET Language Support Contributors
