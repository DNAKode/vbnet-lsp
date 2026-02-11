# Downstream Repositories

Last updated: 2026-02-11

## Purpose

This project maintains integration snapshots in the monorepo and mirrors them
into standalone downstream repositories for distribution.

This keeps language-server behavior centralized while making editor/plugin
distribution predictable and independently releasable.

## Source Of Truth

| Integration | Monorepo Snapshot | Downstream Repo | Distribution Channel |
|-------------|-------------------|-----------------|----------------------|
| Neovim adapter | `adapters/nvim/vbnet-lsp.nvim` | `DNAKode/vbnet-lsp.nvim` | Neovim plugin ecosystem |
| Emacs adapter | `adapters/emacs/vbnet-eglot` | `DNAKode/vbnet-eglot` | Emacs package channels |
| Claude plugin | `integrations/claude/vbnet-lsp` | `DNAKode/vbnet-lsp-claude-plugin` | Claude plugin marketplace submission |

## Sync Scripts

### Adapter repos

```powershell
powershell -NoProfile -ExecutionPolicy Bypass -File ./adapters/scripts/export-adapter-repos.ps1 `
  -NvimRepoPath ../vbnet-lsp.nvim `
  -EmacsRepoPath ../vbnet-eglot `
  -Clean
```

### Claude plugin repo

```powershell
powershell -NoProfile -ExecutionPolicy Bypass -File ./integrations/scripts/export-integration-repos.ps1 `
  -ClaudeRepoPath ../vbnet-lsp-claude-plugin `
  -Clean
```

Add `-DryRun` to either script to preview.

## Release And Sync Cadence

1. Publish server changes from this repository:
   - GitHub release artifacts (`release.yml`)
   - NuGet tool package `DNAKode.VbNet.Lsp` (`publish-dotnet-tool.yml`)
2. Sync downstream snapshot repositories from this monorepo.
3. Run validation workflows in both monorepo and downstream repos.
4. Publish downstream repos (tags/releases) only when their user-facing content changes.

## Validation Expectations

- Monorepo adapter checks:
  - `.github/workflows/editor-adapters.yml`
- Monorepo tool publishing:
  - `.github/workflows/publish-dotnet-tool.yml`
- Claude plugin downstream checks:
  - `.github/workflows/ci.yml` in `DNAKode/vbnet-lsp-claude-plugin`
  - Must validate plugin manifest and `vbnet-ls` installability

## Documentation Cross-Links

- Adapter packaging: `docs/editor-packaging.md`
- Adapter release checklist: `docs/adapter-release-checklist.md`
- Claude marketplace plan: `docs/claude-plugin-marketplace.md`
- Integration snapshots: `integrations/README.md`
