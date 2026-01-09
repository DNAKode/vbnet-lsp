# Configuration Guide

**VB.NET Language Support - User Configuration**

Version: 1.0
Last Updated: 2026-01-09

## Table of Contents

1. [Overview](#overview)
2. [VS Code Settings](#vs-code-settings)
3. [Performance Tuning](#performance-tuning)
4. [Troubleshooting](#troubleshooting)
5. [Advanced Configuration](#advanced-configuration)

---

## 1. Overview

VB.NET Language Support is configured primarily through VS Code settings. All settings can be modified in:

- **UI**: File > Preferences > Settings > Extensions > VB.NET Language Support
- **JSON**: `.vscode/settings.json` (workspace) or User Settings (global)

---

## 2. VS Code Settings

### Core Settings

#### `vbnetLs.enable`
**Type**: `boolean`
**Default**: `true`
**Description**: Enable/disable the VB.NET language server

```json
{
  "vbnetLs.enable": true
}
```

#### `vbnetLs.logLevel`
**Type**: `enum`
**Values**: `"trace"`, `"debug"`, `"info"`, `"warn"`, `"error"`
**Default**: `"info"`
**Description**: Language server logging level

```json
{
  "vbnetLs.logLevel": "info"
}
```

**Usage**:
- `trace`: Maximum verbosity (for debugging LSP protocol)
- `debug`: Internal state changes and detailed operations
- `info`: Lifecycle events, project loading (recommended)
- `warn`: Recoverable errors only
- `error`: Critical errors only

#### `vbnetLs.trace.server`
**Type**: `enum`
**Values**: `"off"`, `"messages"`, `"verbose"`
**Default**: `"off"`
**Description**: LSP message tracing

```json
{
  "vbnetLs.trace.server": "off"
}
```

**Usage**:
- `off`: No LSP tracing
- `messages`: Log LSP request/response names
- `verbose`: Log full LSP message content

View traces: "View > Output" > "VB.NET Language Support"

---

### Feature Toggles

#### `vbnetLs.enableFormatting`
**Type**: `boolean`
**Default**: `true`
**Description**: Enable document formatting

```json
{
  "vbnetLs.enableFormatting": true
}
```

#### `vbnetLs.enableCodeActions`
**Type**: `boolean`
**Default**: `true`
**Description**: Enable code actions and quick fixes

```json
{
  "vbnetLs.enableCodeActions": true
}
```

#### `vbnetLs.semanticTokens`
**Type**: `boolean`
**Default**: `true`
**Description**: Enable semantic syntax highlighting

```json
{
  "vbnetLs.semanticTokens": true
}
```

**Note**: Requires Phase 2 implementation. Disabled in MVP.

#### `vbnetLs.inlayHints`
**Type**: `boolean`
**Default**: `false`
**Description**: Enable inline type and parameter hints

```json
{
  "vbnetLs.inlayHints": false
}
```

**Note**: Requires Phase 3 implementation.

---

### Diagnostics Settings

#### `vbnetLs.diagnosticsMode`
**Type**: `enum`
**Values**: `"openChange"`, `"openSave"`, `"saveOnly"`
**Default**: `"openChange"`
**Description**: When to update diagnostics

```json
{
  "vbnetLs.diagnosticsMode": "openChange"
}
```

**Modes**:
- `openChange`: Update on file open and every change (with debouncing)
- `openSave`: Update on file open and save only
- `saveOnly`: Update only when file is saved

#### `vbnetLs.debounceMs`
**Type**: `integer`
**Range**: 0 - 5000
**Default**: `300`
**Description**: Debounce delay (ms) for diagnostics updates

```json
{
  "vbnetLs.debounceMs": 300
}
```

**Tuning**:
- Lower (100-200ms): Faster feedback, higher CPU usage
- Higher (500-1000ms): Slower feedback, lower CPU usage
- 0: No debouncing (not recommended)

---

### Workspace Settings

#### `vbnetLs.solutionPath`
**Type**: `string`
**Default**: `null` (auto-detect)
**Description**: Explicit path to .sln file

```json
{
  "vbnetLs.solutionPath": "${workspaceFolder}/MySolution.sln"
}
```

**Usage**: Set when workspace contains multiple .sln files

#### `vbnetLs.loadProjectsOnStart`
**Type**: `boolean`
**Default**: `true`
**Description**: Load projects during initialization

```json
{
  "vbnetLs.loadProjectsOnStart": true
}
```

**Note**: Setting to `false` defers project loading until first file is opened.

#### `vbnetLs.maxProjectCount`
**Type**: `integer`
**Default**: `100`
**Description**: Maximum number of projects to load

```json
{
  "vbnetLs.maxProjectCount": 100
}
```

**Usage**: Prevents excessive memory usage on very large solutions.

---

### Performance Settings

#### `vbnetLs.msbuildPath`
**Type**: `string`
**Default**: `null` (auto-detect via MSBuild.Locator)
**Description**: Explicit MSBuild path override

```json
{
  "vbnetLs.msbuildPath": "C:\\Program Files\\dotnet\\sdk\\10.0.100\\MSBuild.dll"
}
```

**Usage**: Set if MSBuild auto-detection fails or you need a specific version.

#### `vbnetLs.maxMemoryMB`
**Type**: `integer`
**Default**: `2048`
**Description**: Maximum memory usage (MB) before warning

```json
{
  "vbnetLs.maxMemoryMB": 2048
}
```

**Note**: Language server will log warnings if this threshold is exceeded.

---

## 3. Performance Tuning

### For Large Solutions (100+ projects)

```json
{
  "vbnetLs.debounceMs": 500,
  "vbnetLs.diagnosticsMode": "openSave",
  "vbnetLs.maxProjectCount": 150,
  "vbnetLs.logLevel": "warn"
}
```

### For Fast Machines / Small Projects

```json
{
  "vbnetLs.debounceMs": 100,
  "vbnetLs.diagnosticsMode": "openChange",
  "vbnetLs.logLevel": "info"
}
```

### For Slow Machines / Limited Memory

```json
{
  "vbnetLs.debounceMs": 1000,
  "vbnetLs.diagnosticsMode": "saveOnly",
  "vbnetLs.maxMemoryMB": 1024,
  "vbnetLs.semanticTokens": false
}
```

---

## 4. Troubleshooting

### Problem: Language server not starting

**Symptoms**: No IntelliSense, no diagnostics

**Check**:
1. "View > Output" > "VB.NET Language Support" for errors
2. Verify .NET SDK is installed: `dotnet --version`
3. Restart VS Code

**Solution**:
```json
{
  "vbnetLs.logLevel": "debug"
}
```

Check output panel for startup errors.

---

### Problem: Slow IntelliSense

**Symptoms**: Long delays for completion/hover

**Check**:
1. Solution size (too many projects?)
2. Memory usage (Task Manager / Activity Monitor)

**Solution**:
```json
{
  "vbnetLs.debounceMs": 500,
  "vbnetLs.maxProjectCount": 50
}
```

---

### Problem: Diagnostics not updating

**Symptoms**: Errors don't appear or are stale

**Check**:
1. `vbnetLs.diagnosticsMode` setting
2. File is saved (if using `saveOnly` mode)

**Solution**:
```json
{
  "vbnetLs.diagnosticsMode": "openChange",
  "vbnetLs.debounceMs": 300
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
  "vbnetLs.msbuildPath": "/path/to/specific/MSBuild.dll"
}
```

---

### Problem: High CPU usage

**Symptoms**: VS Code sluggish, high CPU in Task Manager

**Check**:
1. `vbnetLs.debounceMs` is too low
2. `vbnetLs.diagnosticsMode` is `openChange` with large files

**Solution**:
```json
{
  "vbnetLs.debounceMs": 1000,
  "vbnetLs.diagnosticsMode": "openSave"
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
  "vbnetLs.maxProjectCount": 50,
  "vbnetLs.maxMemoryMB": 1024
}
```

Or close unused files and restart language server:
- Command Palette: "VB.NET: Restart Language Server"

---

## 5. Advanced Configuration

### EditorConfig Support

VB.NET Language Support respects `.editorconfig` files for formatting:

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
        "vbnetLs.solutionPath": "${workspaceFolder}/ProjectA.sln"
      }
    },
    {
      "path": "ProjectB",
      "settings": {
        "vbnetLs.solutionPath": "${workspaceFolder}/ProjectB.sln"
      }
    }
  ]
}
```

**Note**: Multi-root workspace support is Phase 4 feature.

---

### Custom Analyzers

VB.NET Language Support uses Roslyn analyzers from:
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
  // VB.NET Language Support
  "vbnetLs.enable": true,
  "vbnetLs.logLevel": "info",
  "vbnetLs.solutionPath": "${workspaceFolder}/MySolution.sln",
  "vbnetLs.debounceMs": 300,
  "vbnetLs.diagnosticsMode": "openChange",

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

**Note**: VS Code settings override environment variables.

---

## Getting Help

If configuration issues persist:

1. **Check output panel**: "View > Output" > "VB.NET Language Support"
2. **Enable debug logging**: `"vbnetLs.logLevel": "debug"`
3. **Restart language server**: Command Palette > "VB.NET: Restart Language Server"
4. **Check GitHub issues**: https://github.com/DNAKode/vbnet-lsp/issues
5. **Ask for help**: https://github.com/DNAKode/vbnet-lsp/discussions

---

## Additional Resources

- [Development Guide](development.md)
- [Architecture Documentation](architecture.md)
- [Feature Support Matrix](features.md)
- [VS Code Settings Reference](https://code.visualstudio.com/docs/getstarted/settings)

---

**Last Updated**: 2026-01-09

**Maintained by**: VB.NET Language Support Contributors
