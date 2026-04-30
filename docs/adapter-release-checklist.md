# Adapter Release Checklist

Use this checklist when publishing adapter repositories (`vbnet-lsp.nvim`,
`vbnet-eglot`, and future adapters such as `vbnet-zed`) from the snapshots in
this monorepo.

## 1. Sync Snapshots To Adapter Repos

Run from this repository root:

```powershell
powershell -NoProfile -ExecutionPolicy Bypass -File ./adapters/scripts/export-adapter-repos.ps1 `
  -NvimRepoPath ../vbnet-lsp.nvim `
  -EmacsRepoPath ../vbnet-eglot `
  -Clean
```

If you want to preview only:

```powershell
powershell -NoProfile -ExecutionPolicy Bypass -File ./adapters/scripts/export-adapter-repos.ps1 `
  -NvimRepoPath ../vbnet-lsp.nvim `
  -EmacsRepoPath ../vbnet-eglot `
  -Clean -DryRun
```

## 2. Validate In This Monorepo

1. Run `.github/workflows/editor-adapters.yml` (or local harnesses).
2. Confirm adapter docs still match current server CLI usage.
3. Confirm no adapter code duplicates server-side language logic.

## 3. Validate In Adapter Repositories

1. Push branch in `vbnet-lsp.nvim` and confirm adapter CI passes.
2. Push branch in `vbnet-eglot` and confirm adapter CI passes.
3. Review changelog/release notes for user-visible behavior.

## 4. Publish

1. Create and push adapter tags (`v*`) in each adapter repository.
2. Confirm GitHub release workflows completed.
3. For Emacs, submit/update MELPA recipe as needed:
   - `adapters/emacs/vbnet-eglot/melpa-recipe`

### Zed Publishing Notes

For Zed, use the same version as the server release. The release source should
be the adapter snapshot from the matching `vbnet-lsp` tag, not whatever happens
to be on a downstream development branch.

Initial Zed publishing can use a manual approval step:

1. Confirm the `vbnet-lsp` GitHub release exists for `vX.Y.Z`.
2. Confirm all platform language-server artifacts are present.
3. Mirror `adapters/zed/vbnet-zed` from `vbnet-lsp@vX.Y.Z` to
   `vbnet-zed/main`.
4. Pin the Zed adapter default server download to `vX.Y.Z`.
5. Tag `vbnet-zed` as `vX.Y.Z`.

After the first stable Zed publishing cycle, revisit this checklist and the
release workflows. The desired long-term direction is a single `vbnet-lsp`
release that publishes server artifacts and updates downstream editor
repositories for all supported platforms when validation passes.

## 5. Post-Release

1. Add release links to this repo docs if needed.
2. Run a final smoke check against latest server release artifacts.
