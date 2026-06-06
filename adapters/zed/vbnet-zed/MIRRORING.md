# Mirroring

This repository is generated from:

```text
DNAKode/vbnet-lsp/adapters/zed/vbnet-zed
```

Make source changes in `vbnet-lsp`, then mirror them to `DNAKode/vbnet-zed`
with:

```powershell
powershell -NoProfile -ExecutionPolicy Bypass -File ./adapters/scripts/export-adapter-repos.ps1 `
  -ZedRepoPath ../vbnet-zed `
  -Clean
```

Do not commit language-server binaries, debugger binaries, downloaded archives,
local Zed profile data, or Rust build outputs to this repository.

The VB.NET Tree-sitter grammar is not authored in this repository. Its
authoritative source is:

```text
DNAKode/vbnet-lsp/tree-sitter-vbnet
```

The public grammar mirror is `DNAKode/tree-sitter-vbnet`. Zed release manifests
should reference that public mirror with a pinned `rev`; grammar source changes
still start in `vbnet-lsp`.
