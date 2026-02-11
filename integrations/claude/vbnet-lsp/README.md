# vbnet-lsp Claude Plugin

Claude Code plugin for VB.NET language intelligence via `vbnet-ls`.

This repository is a dedicated distribution wrapper around the VB.NET language
server maintained in:

- https://github.com/DNAKode/vbnet-lsp

## What It Provides

- LSP registration for `.vb` files
- `vbnet-ls --stdio` launch contract for Claude Code
- Clear install and validation workflow for the server tool

## Requirements

- .NET SDK 10.0 or later
- Claude Code with plugin support

Install the server tool:

```bash
dotnet tool install --global DNAKode.VbNet.Lsp
```

Verify:

```bash
vbnet-ls --version
vbnet-ls --help
```

## Notes

- NuGet package ID: `DNAKode.VbNet.Lsp`
- Executable command: `vbnet-ls`
- Transport used by this plugin: stdio (`--stdio`)

## Maintenance Model

Source-of-truth for plugin files lives in the monorepo snapshot:

- `integrations/claude/vbnet-lsp` in `https://github.com/DNAKode/vbnet-lsp`

Sync into this repo from the `vbnet-lsp` monorepo root via:

```powershell
powershell -NoProfile -ExecutionPolicy Bypass -File ./integrations/scripts/export-integration-repos.ps1 `
  -ClaudeRepoPath ../vbnet-lsp-claude-plugin `
  -Clean
```

Reference documentation:

- `docs/downstream-repositories.md` in the monorepo
- `docs/claude-plugin-marketplace.md` in the monorepo

## License

MIT. See `LICENSE`.
