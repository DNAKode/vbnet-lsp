# Roslyn LSP Packaging (VS Code Extension)

This document describes how we package the Roslyn LSP server and Visual Basic
assemblies for the VS Code extension. The goal is to allow selecting either
backend (`vbnet` or `roslyn`) without changing the extension binaries.

## Overview

We ship two Roslyn-related folders inside the extension:

- `.roslyn/` ? base Roslyn LSP server (RID-specific)
- `.roslyn-vb/` ? VB assemblies used as `--extension` inputs

This layout avoids duplicate analyzer references and mirrors the working
Neovim experiment: the Roslyn server loads VB language services only when the
VB assemblies are provided via `--extension`, *and only if those assemblies are
not also present in the base directory*.

## NuGet Sources

We download artifacts from NuGet:

- `Microsoft.CodeAnalysis.LanguageServer.<rid>`
- `Microsoft.CodeAnalysis.VisualBasic`
- `Microsoft.CodeAnalysis.VisualBasic.Workspaces`
- `Microsoft.CodeAnalysis.VisualBasic.Features`

The Roslyn LSP packages are RID-specific and currently prerelease-only. The
VB packages have stable releases and must match the Roslyn LSP version line to
avoid binding issues.

## Current Versions (as of 2026-01-21)

- Roslyn LSP (RID-specific): prerelease only (no stable).
  Example: `Microsoft.CodeAnalysis.LanguageServer.win-x64` `5.0.0-1.25277.114`.
- VB packages (stable): `5.0.0`.
- VB packages (prerelease): `5.0.0-2.final`.

## Scripted Packaging

Use the Node script:

```bash
# Auto-detect RID from the current platform
npm run bundle-roslyn

# Explicit RID
npm run bundle-roslyn:win32-x64
npm run bundle-roslyn:linux-x64
npm run bundle-roslyn:darwin-x64
npm run bundle-roslyn:darwin-arm64
```

The script is `src/extension/scripts/copy-roslyn-server.js` and:

1. Downloads the Roslyn LSP package for the RID.
2. Extracts `content/LanguageServer/<rid>` into `.roslyn/`.
3. Downloads VB packages and copies `Microsoft.CodeAnalysis.VisualBasic*.dll/xml`
   into `.roslyn-vb/`.

Temporary NuGet downloads are cached under `.roslyn-downloads/` and excluded
from VSIX packaging by `.vscodeignore`.

## CI Caching & Validation

- CI caches `.roslyn-downloads/` keyed by the packaging script hash and target.
- Packaging runs `npm run validate-roslyn` to ensure `.roslyn` and `.roslyn-vb`
  are coherent (server present, VB assemblies only in the extension folder).

## Version Overrides

Defaults are pinned in the script but can be overridden via args or environment:

```bash
# CLI args
node scripts/copy-roslyn-server.js --rid win-x64 --lsp-version <ver> --vb-version <ver>

# Environment
export VBNET_ROSLYN_RID=linux-x64
export VBNET_ROSLYN_LSP_VERSION=5.0.0-1.25277.114
export VBNET_ROSLYN_VB_VERSION=5.0.0
```

## Runtime Configuration

To use Roslyn from the extension, set:

```json
{
  "vbnet.server.backend": "roslyn",
  "vbnet.roslyn.server.extensionPath": "<path to .roslyn-vb>"
}
```

If you leave `vbnet.roslyn.server.extensionPath` empty, the extension uses the
bundled `.roslyn-vb` directory.

## Notes

- The Roslyn LSP binaries are **RID-specific**; you must bundle the correct
  RID for each VSIX target.
- The VB assemblies **must not** exist in the Roslyn base directory or the
  server will crash with duplicate analyzer references.
- Keep versions aligned across Roslyn LSP + VB packages to avoid missing
  dependencies.
