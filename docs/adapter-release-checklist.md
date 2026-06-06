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
  -ZedRepoPath ../vbnet-zed `
  -TreeSitterRepoPath ../tree-sitter-vbnet `
  -Clean
```

If you want to preview only:

```powershell
powershell -NoProfile -ExecutionPolicy Bypass -File ./adapters/scripts/export-adapter-repos.ps1 `
  -NvimRepoPath ../vbnet-lsp.nvim `
  -EmacsRepoPath ../vbnet-eglot `
  -ZedRepoPath ../vbnet-zed `
  -TreeSitterRepoPath ../tree-sitter-vbnet `
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
2. Confirm all platform language-server artifacts are present:

   ```powershell
   scripts\verify-zed-release-assets.ps1 -Version X.Y.Z
   ```

3. Run the Zed readiness runner with the release and live-Zed gates enabled
   after preparing an isolated Zed profile with the dev extension installed:

   ```powershell
   scripts\verify-zed-readiness.ps1 `
     -Version X.Y.Z `
     -IncludeReleaseAssets `
     -IncludeLiveZed `
     -IncludeRealServerZed `
     -IncludeDebugZed `
     -ZedPath C:\Programs\Zed\Zed.exe `
     -UserDataDir $profile `
     -WorkspacePath test-explore\clients\zed\fixtures\single-file `
     -RealServerWorkspacePath test\TestProjects\SmallProject `
     -DebugWorkspacePath test-explore\clients\zed\fixtures\debug-console
   ```

4. Mirror `tree-sitter-vbnet` from `vbnet-lsp@vX.Y.Z` to
   `tree-sitter-vbnet/main`.
5. Record the mirrored grammar commit SHA or tag.
6. Mirror `adapters/zed/vbnet-zed` from `vbnet-lsp@vX.Y.Z` to
   `vbnet-zed/main`.
7. Verify `extension.toml`, `Cargo.toml`, and the Zed adapter release download
   pin all match `X.Y.Z` / `vX.Y.Z`.
8. Verify `extension.toml` points to `https://github.com/DNAKode/tree-sitter-vbnet`
   with the mirrored grammar commit SHA or tag, not a local `file://` URL.
9. Tag `vbnet-zed` as `vX.Y.Z`.
10. Tag `tree-sitter-vbnet` as `vX.Y.Z` if grammar content changed for this
    release.

After the first stable Zed publishing cycle, revisit this checklist and the
release workflows. The desired long-term direction is a single `vbnet-lsp`
release that publishes server artifacts and updates downstream editor
repositories for all supported platforms when validation passes.

## 5. Post-Release

1. Add release links to this repo docs if needed.
2. Run a final smoke check against latest server release artifacts.
