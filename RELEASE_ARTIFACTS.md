# Release Artifacts

This repository publishes two artifact families in GitHub Releases:

- Standalone VB.NET language server archives (for non-VS Code LSP clients)
- VSIX extension packages (for VS Code/Cursor/VSCodium manual install)

## Artifact Names

### Language Server Archives

- `vbnet-language-server-win-x64.zip`
- `vbnet-language-server-linux-x64.tar.gz`
- `vbnet-language-server-osx-x64.tar.gz`
- `vbnet-language-server-osx-arm64.tar.gz`

### VSIX Packages

- `vbnet-language-support-win32-x64.vsix`
- `vbnet-language-support-linux-x64.vsix`
- `vbnet-language-support-darwin-x64.vsix`
- `vbnet-language-support-darwin-arm64.vsix`

## Language Server Archive Contents

Language server archives are produced from:

`dotnet publish src/VbNet.LanguageServer.Vb/VbNet.LanguageServer.Vb.vbproj -c Release -r <rid> --self-contained false`

Each archive contains publish output for that RID, including:

- `VbNet.LanguageServer.dll`
- Host executable for the target RID (`VbNet.LanguageServer` or `VbNet.LanguageServer.exe`)
- `.deps.json` and `.runtimeconfig.json`
- Required runtime dependencies copied by `dotnet publish`

## VSIX Package Contents

VSIX packages include:

- Extension runtime (`dist/`, manifest, commands, settings)
- Bundled VB.NET language server under `.server/`
- Bundled Roslyn server under `.roslyn/`
- Bundled Roslyn VB extension assemblies under `.roslyn-vb/`
- Bundled netcoredbg under `.debugger/`

Roslyn packaging rule:

- `.roslyn/` must not include `Microsoft.CodeAnalysis.VisualBasic*` assemblies.
- VB assemblies are loaded from `.roslyn-vb/` only.

## How To Use

### Non-VS Code LSP Clients

1. Download and extract the server archive for your platform.
2. Configure your LSP client to launch one of:

```bash
# App host
./VbNet.LanguageServer --stdio

# Dotnet host
dotnet VbNet.LanguageServer.dll --stdio
```

Optional server arguments:

- `--pipe`
- `--logLevel <Trace|Debug|Information|Warning|Error|Critical>`
- `--msbuildPath <path>`

### VS Code-family Editors (manual install)

Install a VSIX package:

```bash
code --install-extension vbnet-language-support-<target>.vsix
```

Or use UI: `Extensions: Install from VSIX...`

## Automation

GitHub Action workflow:

- `.github/workflows/release.yml`

Behavior:

- Triggered by tag push matching `v*`, or manual `workflow_dispatch`.
- Builds all language server archives and VSIX packages.
- Publishes all artifacts to a GitHub Release for the selected tag.
