# Editor Adapters

This folder contains thin editor-specific adapters that launch the shared
VB.NET language server.

- `nvim/vbnet-lsp.nvim`: Neovim adapter package snapshot
- `emacs/vbnet-eglot`: Emacs `eglot` adapter package snapshot

These adapters are validated in `.github/workflows/editor-adapters.yml` and are
intended for editor-native distribution channels.

## Export To Dedicated Adapter Repos

Use the export script to mirror snapshots into standalone repositories:

```powershell
powershell -NoProfile -ExecutionPolicy Bypass -File ./adapters/scripts/export-adapter-repos.ps1 `
  -NvimRepoPath ../vbnet-lsp.nvim `
  -EmacsRepoPath ../vbnet-eglot `
  -Clean
```

Add `-DryRun` to preview actions.
