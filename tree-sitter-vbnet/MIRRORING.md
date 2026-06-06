# Mirroring

This repository is generated from:

```text
DNAKode/vbnet-lsp/tree-sitter-vbnet
```

Make grammar source changes in `vbnet-lsp`, then mirror them to
`DNAKode/tree-sitter-vbnet` with:

```powershell
powershell -NoProfile -ExecutionPolicy Bypass -File ./adapters/scripts/export-adapter-repos.ps1 `
  -TreeSitterRepoPath ../tree-sitter-vbnet `
  -Clean
```

Do not make normal development changes directly in the downstream mirror. Any
emergency downstream fix must be backported to `vbnet-lsp/tree-sitter-vbnet`
before the next mirror.
