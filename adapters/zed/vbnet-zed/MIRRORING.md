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
