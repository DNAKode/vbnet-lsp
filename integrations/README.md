# Integrations

This folder contains distribution snapshots for downstream integration
repositories that wrap or register the shared VB.NET language server.

Current integrations:

- `claude/vbnet-lsp`: Claude Code plugin snapshot

These snapshots are maintained in this monorepo as source-of-truth content and
mirrored into standalone repositories for distribution.

## Export To Dedicated Integration Repos

Use the export script to mirror snapshots into standalone repositories:

```powershell
powershell -NoProfile -ExecutionPolicy Bypass -File ./integrations/scripts/export-integration-repos.ps1 `
  -ClaudeRepoPath ../vbnet-lsp-claude-plugin `
  -Clean
```

Add `-DryRun` to preview actions.
