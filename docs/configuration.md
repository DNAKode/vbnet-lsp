# Configuration Guide

**`VB.NET` Language Support - User Configuration**

Version: 1.1
Last Updated: 2026-01-22

## Table of Contents

1. [Overview](#overview)
2. [VS Code Settings](#vs-code-settings)
3. [Performance Tuning](#performance-tuning)
4. [Troubleshooting](#troubleshooting)
5. [Advanced Configuration](#advanced-configuration)

---

## 1. Overview

`VB.NET` Language Support is configured primarily through VS Code settings. All settings can be modified in:

- **UI**: File > Preferences > Settings > Extensions > `VB.NET` Language Support
- **JSON**: `.vscode/settings.json` (workspace) or User Settings (global)

### Commands

- **`VB.NET: Select Workspace Solution`** — choose a `.sln/.slnf/.slnx` file and update
  `vbnet.workspace.solutionPath`. Pick "Auto-detect" to clear the override.
- **`VB.NET: Show Logs`** — opens the main output channel and the LSP trace channel.
- **`VB.NET: Toggle LSP Trace`** — toggles `vbnet.trace.server` between `off` and `verbose`.
- **`VB.NET: Restore Workspace`** — runs `dotnet restore` using the selected solution (or workspace root).
- **`VB.NET: Restore Project`** — picks a `.vbproj` file and runs `dotnet restore` for it.
- **`VB.NET: Run Tests`** — runs `dotnet test` using the active `.vb` context, selected solution, or project.
- **`VB.NET: Debug Tests (Preview)`** — runs `dotnet test` and notes that debug attach is not yet implemented.
- **`VB.NET: Reload Workspace`** — requests a workspace reload without restarting the extension.
- **`VB.NET: Attach Debugger to Process`** — picks a running process and starts an attach session.

### Explorer File Nesting Defaults

The extension contributes file nesting patterns so common VB artifacts are grouped in the
Explorer (for example, `.Designer.vb`, `.g.vb`, `.g.i.vb`, `.generated.vb`, `.AssemblyInfo.vb`,
and `My*.vb` under their primary `.vb` file). You can override these defaults via
`explorer.fileNesting.patterns` in your settings.

---

## 2. VS Code Settings

### Core Settings

#### `vbnet.enable`
**Type**: `boolean`
**Default**: `true`
**Description**: Enable/disable the `VB.NET` language server

```json
{
  "vbnet.enable": true
}
```

#### `vbnet.logLevel`
**Type**: `enum`
**Values**: `"trace"`, `"debug"`, `"info"`, `"warn"`, `"error"`
**Default**: `"info"`
**Description**: Language server logging level

```json
{
  "vbnet.logLevel": "info"
}
```

**Usage**:
- `trace`: Maximum verbosity (for debugging LSP protocol)
- `debug`: Internal state changes and detailed operations
- `info`: Lifecycle events, project loading (recommended)
- `warn`: Recoverable errors only
- `error`: Critical errors only

#### `vbnet.trace.server`
**Type**: `enum`
**Values**: `"off"`, `"messages"`, `"verbose"`
**Default**: `"off"`
**Description**: LSP message tracing

```json
{
  "vbnet.trace.server": "off"
}
```

**Usage**:
- `off`: No LSP tracing
- `messages`: Log LSP request/response names
- `verbose`: Log full LSP message content

View traces: "View > Output" > "`VB.NET` Language Support"

---

### Server Overrides (Advanced)

#### `vbnet.server.path`
**Type**: `string`
**Default**: `""` (use bundled server)
**Description**: Override the language server binary path. Recommended only for local development.

```json
{
  "vbnet.server.path": "C:\\path\\to\\VbNet.LanguageServer.dll"
}
```

**Note**: Prefer setting this in workspace settings when developing the server. For a new user experience, leave it unset so the extension uses the bundled server.

#### `vbnet.server.backend`
**Type**: `enum`
**Values**: `"vbnet"`, `"roslyn"`
**Default**: `"vbnet"`
**Description**: Select which language server backend to launch.

```json
{
  "vbnet.server.backend": "vbnet"
}
```

**Notes**:
- `vbnet` uses the bundled VB.NET language server (current default).
- `roslyn` uses the Roslyn language server with VB assemblies loaded via extension path.

#### `vbnet.roslyn.server.path`
**Type**: `string`
**Default**: `""` (use bundled Roslyn server)
**Description**: Override the Roslyn language server binary path.

```json
{
  "vbnet.roslyn.server.path": "C:\\path\\to\\Microsoft.CodeAnalysis.LanguageServer.dll"
}
```

#### `vbnet.roslyn.server.extensionPath`
**Type**: `string`
**Default**: `""`
**Description**: Directory containing Roslyn VB extension assemblies (`Microsoft.CodeAnalysis.VisualBasic*.dll`).

```json
{
  "vbnet.roslyn.server.extensionPath": "C:\\path\\to\\roslyn-vb-extension"
}
```

---

### Feature Toggles

#### `vbnet.enableFormatting`
**Type**: `boolean`
**Default**: `true`
**Description**: Enable document formatting

```json
{
  "vbnet.enableFormatting": true
}
```

#### `vbnet.enableCodeActions`
**Type**: `boolean`
**Default**: `true`
**Description**: Enable code actions and quick fixes

```json
{
  "vbnet.enableCodeActions": true
}
```

#### `vbnet.semanticTokens`
**Type**: `boolean`
**Default**: `true`
**Description**: Enable semantic syntax highlighting

```json
{
  "vbnet.semanticTokens": true
}
```

**Note**: Requires Phase 2 implementation. Disabled in MVP.

#### `vbnet.inlayHints`
**Type**: `boolean`
**Default**: `false`
**Description**: Enable inline type and parameter hints

```json
{
  "vbnet.inlayHints": false
}
```

**Note**: Deferred pending stable Roslyn public APIs.

---

### Debugger Settings (Extension)

**Launch configuration note**: Debugging requires a `program` path to a compiled `.dll`. If you omit it, the extension will attempt to infer it when a matching `bin/Debug/**/<Assembly>.dll` is found; if multiple `.vbproj` files are present, you’ll be prompted to choose. You can also use placeholders like `<target-framework>` and `<project-name>` (e.g., `${workspaceFolder}/bin/Debug/<target-framework>/<project-name>.dll`).

**Project hinting**: You can set `projectPath` in your launch configuration to point at a `.vbproj` and let the extension infer the `program` path.

#### `vbnet.debugger.path`
**Type**: `string`
**Default**: `""`
**Description**: Path to the `netcoredbg` executable. Leave empty to use the bundled debugger (or PATH when running from source).

```json
{
  "vbnet.debugger.path": "C:\\tools\\netcoredbg\\netcoredbg.exe"
}
```

#### `vbnet.debugger.args`
**Type**: `string[]`
**Default**: `[]`
**Description**: Extra command line arguments passed to `netcoredbg` (for example, `--engineLogging=/tmp/netcoredbg.log`).

```json
{
  "vbnet.debugger.args": ["--engineLogging=C:\\temp\\netcoredbg.log"]
}
```

---

### Diagnostics Settings

#### `vbnet.diagnosticsMode`
**Type**: `enum`
**Values**: `"openChange"`, `"openSave"`, `"saveOnly"`
**Default**: `"openChange"`
**Description**: When to update diagnostics

```json
{
  "vbnet.diagnosticsMode": "openChange"
}
```

**Modes**:
- `openChange`: Update on file open and every change (with debouncing)
- `openSave`: Update on file open and save only
- `saveOnly`: Update only when file is saved

#### `vbnet.debounceMs`
**Type**: `integer`
**Range**: 0 - 5000
**Default**: `300`
**Description**: Debounce delay (ms) for diagnostics updates

```json
{
  "vbnet.debounceMs": 300
}
```

**Tuning**:
- Lower (100-200ms): Faster feedback, higher CPU usage
- Higher (500-1000ms): Slower feedback, lower CPU usage
- 0: No debouncing (not recommended)

---

### Workspace Settings

#### `vbnet.workspace.solutionPath`
**Type**: `string`
**Default**: `""` (auto-detect)
**Description**: Explicit path to a `.sln` file. Leave empty to auto-detect.

```json
{
  "vbnet.workspace.solutionPath": "${workspaceFolder}/MySolution.sln"
}
```

#### `vbnet.workspace.ignoreSolutionFiles`
**Type**: `boolean`
**Default**: `false`
**Description**: Ignore `.sln` files and load `.vbproj` files directly (solution-less mode).

```json
{
  "vbnet.workspace.ignoreSolutionFiles": true
}
```

#### `vbnet.workspace.projectSearchPaths`
**Type**: `string[]`
**Default**: `[]` (workspace root)
**Description**: Directories to scan for `.vbproj` files when not loading a solution.

```json
{
  "vbnet.workspace.projectSearchPaths": [
    "test/TestProjects/MediumProject"
  ]
}
```

#### `vbnet.workspace.excludePaths`
**Type**: `string[]`
**Default**: `["_external", "test-explore", "build", "node_modules", ".git", ".vscode"]`
**Description**: Directory names to exclude when scanning for `.vbproj` files.

```json
{
  "vbnet.workspace.excludePaths": [
    "_external",
    "test-explore",
    "build"
  ]
}
```

#### `vbnet.solutionPath` (legacy)
**Type**: `string`
**Default**: `null` (auto-detect)
**Description**: Legacy setting; use `vbnet.workspace.solutionPath` instead.

#### `vbnet.workspace.projectFilesExcludePattern`
**Type**: `string`
**Default**: `"**/node_modules/**,**/.git/**,**/bower_components/**"`
**Description**: Comma-separated glob patterns used by the extension when searching for solutions/projects.

```json
{
  "vbnet.workspace.projectFilesExcludePattern": "**/_external/**,**/test-explore/**,**/node_modules/**,**/.git/**"
}
```

#### `vbnet.workspace.maxProjectResults`
**Type**: `number`
**Default**: `250`
**Description**: Maximum number of `.vbproj` files to consider when no solution is present.

#### `vbnet.loadProjectsOnStart`
**Type**: `boolean`
**Default**: `true`
**Description**: Load projects during initialization

```json
{
  "vbnet.loadProjectsOnStart": true
}
```

**Note**: Setting to `false` defers project loading until first file is opened.

#### `vbnet.maxProjectCount`
**Type**: `integer`
**Default**: `100`
**Description**: Maximum number of projects to load

```json
{
  "vbnet.maxProjectCount": 100
}
```

**Usage**: Prevents excessive memory usage on very large solutions.

---

### Performance Settings

#### `vbnet.msbuildPath`
**Type**: `string`
**Default**: `null` (auto-detect via MSBuild.Locator)
**Description**: Explicit MSBuild path override

```json
{
  "vbnet.msbuildPath": "C:\\Program Files\\dotnet\\sdk\\10.0.100\\MSBuild.dll"
}
```

**Usage**: Set if MSBuild auto-detection fails or you need a specific version.

#### `vbnet.maxMemoryMB`
**Type**: `integer`
**Default**: `2048`
**Description**: Maximum memory usage (MB) before warning

```json
{
  "vbnet.maxMemoryMB": 2048
}
```

**Note**: Language server will log warnings if this threshold is exceeded.

---

## 3. Performance Tuning

### For Large Solutions (100+ projects)

```json
{
  "vbnet.debounceMs": 500,
  "vbnet.diagnosticsMode": "openSave",
  "vbnet.maxProjectCount": 150,
  "vbnet.logLevel": "warn"
}
```

### For Fast Machines / Small Projects

```json
{
  "vbnet.debounceMs": 100,
  "vbnet.diagnosticsMode": "openChange",
  "vbnet.logLevel": "info"
}
```

### For Slow Machines / Limited Memory

```json
{
  "vbnet.debounceMs": 1000,
  "vbnet.diagnosticsMode": "saveOnly",
  "vbnet.maxMemoryMB": 1024,
  "vbnet.semanticTokens": false
}
```

---

## 4. Troubleshooting

### Problem: Language server not starting

**Symptoms**: No IntelliSense, no diagnostics

**Check**:
1. "View > Output" > "`VB.NET` Language Support" for errors
2. Verify .NET SDK is installed: `dotnet --version`
3. Restart VS Code

**Solution**:
```json
{
  "vbnet.logLevel": "debug"
}
```

Check output panel for startup errors.

---

### Problem: Debugging says program path is missing or not found

**Symptoms**: “`VB.NET` debugging requires a program path” or “debug program not found”.

**Check**:
1. Build the project (`dotnet build` or VS Code build task).
2. Confirm the output DLL path under `bin/Debug/<target-framework>/`.
3. Optionally set `projectPath` in `launch.json` so the extension can infer `program`.

**Solution**:
```json
{
  "type": "vbnet",
  "request": "launch",
  "name": "`VB.NET` Launch",
  "projectPath": "${workspaceFolder}/YourProject.vbproj"
}
```

---

### Problem: Slow IntelliSense

**Symptoms**: Long delays for completion/hover

**Check**:
1. Solution size (too many projects?)
2. Memory usage (Task Manager / Activity Monitor)

**Solution**:
```json
{
  "vbnet.debounceMs": 500,
  "vbnet.maxProjectCount": 50
}
```

---

### Problem: Diagnostics not updating

**Symptoms**: Errors don't appear or are stale

**Check**:
1. `vbnet.diagnosticsMode` setting
2. File is saved (if using `saveOnly` mode)

**Solution**:
```json
{
  "vbnet.diagnosticsMode": "openChange",
  "vbnet.debounceMs": 300
}
```

---

### Problem: MSBuild errors on project load

**Symptoms**: "Project load failed" in output

**Check**:
1. .NET SDK version matches project target framework
2. MSBuild can load project:
   ```bash
   dotnet build YourProject.vbproj
   ```

**Solution**:
```json
{
  "vbnet.msbuildPath": "/path/to/specific/MSBuild.dll"
}
```

---

### Problem: SDK-style .NET Framework (net4x) projects fail to load

**Symptoms**: "Visual Basic not supported" or missing reference assemblies for net4x targets.

**Check (Windows)**:
1. Install the .NET Framework targeting packs or Visual Studio Build Tools.
2. Set `vbnet.msbuildPath` to a full MSBuild (from Visual Studio/Build Tools).
3. If targeting packs are unavailable, add `Microsoft.NETFramework.ReferenceAssemblies` to the project.

**Check (non-Windows)**:
- SDK-style net4x targets are not supported. Retarget to modern .NET or build on Windows.

**Tip**: If restores are missing, use **"VB.NET: Restore Workspace"** or **"VB.NET: Restore Project"**.

---

### Problem: High CPU usage

**Symptoms**: VS Code sluggish, high CPU in Task Manager

**Check**:
1. `vbnet.debounceMs` is too low
2. `vbnet.diagnosticsMode` is `openChange` with large files

**Solution**:
```json
{
  "vbnet.debounceMs": 1000,
  "vbnet.diagnosticsMode": "openSave"
}
```

---

### Problem: High memory usage

**Symptoms**: Language server using >2GB RAM

**Check**:
1. Solution size (how many projects?)
2. Number of open files

**Solution**:
```json
{
  "vbnet.maxProjectCount": 50,
  "vbnet.maxMemoryMB": 1024
}
```

Or close unused files and restart language server:
- Command Palette: "`VB.NET`: Restart Language Server"

---

## 5. Advanced Configuration

### EditorConfig Support

`VB.NET` Language Support respects `.editorconfig` files for formatting:

```ini
# .editorconfig
root = true

[*.vb]
indent_style = space
indent_size = 4
end_of_line = crlf
charset = utf-8
trim_trailing_whitespace = true
insert_final_newline = true
```

**Supported properties**:
- `indent_style` (space, tab)
- `indent_size` (number)
- `end_of_line` (lf, crlf)
- `charset` (utf-8, utf-16)
- `trim_trailing_whitespace` (true, false)
- `insert_final_newline` (true, false)

---

### Multi-Root Workspace Configuration

For multi-root workspaces, configure per folder:

```json
// .code-workspace
{
  "folders": [
    {
      "path": "ProjectA",
      "settings": {
        "vbnet.solutionPath": "${workspaceFolder}/ProjectA.sln"
      }
    },
    {
      "path": "ProjectB",
      "settings": {
        "vbnet.solutionPath": "${workspaceFolder}/ProjectB.sln"
      }
    }
  ]
}
```

**Note**: Multi-root workspace support is Phase 4 feature.

---

### Custom Analyzers

`VB.NET` Language Support uses Roslyn analyzers from:
1. Project `<PackageReference>` to analyzers
2. Global `.globalconfig` file

**Example .globalconfig**:
```ini
is_global = true

# Severity overrides
dotnet_diagnostic.CA1001.severity = warning
dotnet_diagnostic.CA2007.severity = none
```

---

### Workspace-Specific Settings Example

```json
// .vscode/settings.json (workspace)
{
  // `VB.NET` Language Support
  "vbnet.enable": true,
  "vbnet.logLevel": "info",
  "vbnet.solutionPath": "${workspaceFolder}/MySolution.sln",
  "vbnet.debounceMs": 300,
  "vbnet.diagnosticsMode": "openChange",

  // Editor preferences
  "editor.formatOnSave": true,
  "editor.formatOnType": false,

  // File associations
  "files.associations": {
    "*.vb": "vb"
  },

  // Auto-save
  "files.autoSave": "afterDelay",
  "files.autoSaveDelay": 1000
}
```

---

## Configuration Migration (Future)

When upgrading to new versions, configuration may change. Check release notes for:
- Deprecated settings
- New settings
- Changed defaults

---

## Environment Variables

Advanced users can set environment variables for the language server:

```bash
# Linux/macOS
export VBNET_LS_LOG_LEVEL=Trace
export VBNET_LS_MAX_MEMORY_MB=4096

# Windows (PowerShell)
$env:VBNET_LS_LOG_LEVEL="Trace"
$env:VBNET_LS_MAX_MEMORY_MB="4096"
```

Additional environment variables (Roslyn backend):

```bash
# Linux/macOS
export VBNET_ROSLYN_SERVER_PATH=/path/to/Microsoft.CodeAnalysis.LanguageServer.dll
export VBNET_ROSLYN_EXTENSION_PATH=/path/to/roslyn-vb-extension

# Windows (PowerShell)
$env:VBNET_ROSLYN_SERVER_PATH="C:\\path\\to\\Microsoft.CodeAnalysis.LanguageServer.dll"
$env:VBNET_ROSLYN_EXTENSION_PATH="C:\\path\\to\\roslyn-vb-extension"
```

**Note**: VS Code settings override environment variables.

---

## Getting Help

If configuration issues persist:

1. **Check output panel**: "View > Output" > "`VB.NET` Language Support"
2. **Enable debug logging**: `"vbnet.logLevel": "debug"`
3. **Restart language server**: Command Palette > "`VB.NET`: Restart Language Server"
4. **Check GitHub issues**: https://github.com/DNAKode/vbnet-lsp/issues
5. **Ask for help**: https://github.com/DNAKode/vbnet-lsp/discussions

---

## Additional Resources

- [Development Guide](development.md)
- [Architecture Documentation](architecture.md)
- [Feature Support Matrix](features.md)
- [VS Code Settings Reference](https://code.visualstudio.com/docs/getstarted/settings)

---

**Last Updated**: 2026-01-22

**Maintained by**: `VB.NET` Language Support Contributors


