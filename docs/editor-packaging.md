# Editor Adapter Packaging

This project separates server distribution from editor integration:

- Server binaries are published as GitHub Release artifacts.
- Editor adapters stay thin and are distributed via editor-native channels.

## Packaging Boundaries

### Server (shared across editors)

- Artifact source: `.github/workflows/release.yml`
- Deliverables: `vbnet-language-server-*` archives
- Transport: stdio (default) and named pipes

### VS Code

- Artifact source: `.github/workflows/release.yml`
- Deliverables: `vbnet-language-support-*.vsix`

### Neovim adapter

- Source snapshot: `adapters/nvim/vbnet-lsp.nvim`
- Validation: `.github/workflows/editor-adapters.yml` (`nvim-smoke` job)
- Distribution target: dedicated plugin repository and Neovim plugin managers

### Emacs adapter (`eglot`)

- Source snapshot: `adapters/emacs/vbnet-eglot`
- Validation: `.github/workflows/editor-adapters.yml` (`emacs-smoke` job)
- Distribution target: dedicated adapter repository, then MELPA/package-vc
- MELPA recipe template: `adapters/emacs/vbnet-eglot/melpa-recipe`

## Thin Adapter Rules

- Keep editor code limited to startup/configuration concerns.
- Do not duplicate language semantics already implemented by the server.
- Prefer standard LSP behavior first; add editor-specific glue only when needed.
- Keep server release cadence independent from adapter release cadence.

## Release Workflow (recommended)

1. Publish language server artifacts from this repo.
2. Update adapter snapshots from this repo into dedicated editor adapter repos.
   Use:

   ```powershell
   powershell -NoProfile -ExecutionPolicy Bypass -File ./adapters/scripts/export-adapter-repos.ps1 -NvimRepoPath ../vbnet-lsp.nvim -EmacsRepoPath ../vbnet-eglot -Clean
   ```
3. Tag and publish adapters through editor-native channels.
4. Run smoke checks in this repo before and after adapter releases.

Detailed step-by-step: [Adapter Release Checklist](adapter-release-checklist.md).
