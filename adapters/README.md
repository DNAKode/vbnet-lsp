# Editor Adapters

This folder contains thin editor-specific adapters that launch the shared
VB.NET language server.

- `nvim/vbnet-lsp.nvim`: Neovim adapter package snapshot
- `emacs/vbnet-eglot`: Emacs `eglot` adapter package snapshot
- `zed/vbnet-zed`: Zed extension package snapshot

These adapters are validated in `.github/workflows/editor-adapters.yml` and are
intended for editor-native distribution channels.

## Export To Dedicated Adapter Repos

Use the export script to mirror snapshots into standalone repositories:

```powershell
powershell -NoProfile -ExecutionPolicy Bypass -File ./adapters/scripts/export-adapter-repos.ps1 `
  -NvimRepoPath ../vbnet-lsp.nvim `
  -EmacsRepoPath ../vbnet-eglot `
  -ZedRepoPath ../vbnet-zed `
  -TreeSitterRepoPath ../tree-sitter-vbnet `
  -Clean
```

Add `-DryRun` to preview actions.

`tree-sitter-vbnet` lives at the repository root because it is more general
than the Zed adapter, but it is mirrored with the same script. The monorepo copy
is authoritative; `DNAKode/tree-sitter-vbnet` is distribution output.
