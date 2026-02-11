# Claude Plugin Marketplace Plan (`vbnet-lsp`)

Last updated: 2026-02-11

## 1. Fit Assessment

`vbnet-lsp` is a strong fit for the Claude plugin marketplace LSP category:

- It is a standalone LSP server with stdio support (`--stdio`), which matches marketplace `lspServers` expectations.
- It targets a distinct language (`.vb`) not currently covered by built-in language-server entries.
- It now supports a stable global command (`vbnet-ls`) via a NuGet dotnet-tool package (`DNAKode.VbNet.Lsp`), which matches how existing LSP entries rely on PATH-installed CLIs.

## 2. Current Marketplace Structure (Reference)

Recent marketplace entries are defined in:

- `anthropics/claude-plugins-official/.claude-plugin/marketplace.json`

For language servers, the plugin entry includes:

- `"strict": false`
- `"lspServers"` with:
  - `command`
  - optional `args`
  - `extensionToLanguage`

## 2.1 Plugin Repository Layout

Standalone plugin repository target:

- `https://github.com/DNAKode/vbnet-lsp-claude-plugin`

Source-of-truth snapshot in this monorepo:

- `integrations/claude/vbnet-lsp`

Sync mechanism:

- `integrations/scripts/export-integration-repos.ps1`
- `docs/downstream-repositories.md`

## 3. Proposed Marketplace Entry

```json
{
  "name": "vbnet-lsp",
  "description": "VB.NET language server for code intelligence and diagnostics",
  "version": "1.0.0",
  "author": {
    "name": "DNAKode"
  },
  "source": "./plugins/vbnet-lsp",
  "category": "development",
  "strict": false,
  "lspServers": {
    "vbnet-ls": {
      "command": "vbnet-ls",
      "args": ["--stdio"],
      "extensionToLanguage": {
        ".vb": "vb"
      },
      "startupTimeout": 120000
    }
  }
}
```

## 4. Proposed Plugin README (`plugins/vbnet-lsp/README.md`)

```md
# vbnet-lsp

VB.NET language server for Claude Code, providing Roslyn-backed code intelligence and diagnostics.

## Supported Extensions
`.vb`

## Installation

### Via .NET tool (recommended)
`dotnet tool install --global DNAKode.VbNet.Lsp`

### Update
`dotnet tool update --global DNAKode.VbNet.Lsp`

## Requirements
- .NET SDK 10.0 or later

## More Information
- https://github.com/DNAKode/vbnet-lsp
- https://www.nuget.org/packages/DNAKode.VbNet.Lsp
```

## 5. Submission Path

Two viable paths:

1. Submit through Anthropic's plugin directory submission form: https://clau.de/plugin-directory-submission
2. Open a PR against `anthropics/claude-plugins-official` adding:
   - `plugins/vbnet-lsp/README.md`
   - `/.claude-plugin/marketplace.json` entry

Use whichever Anthropic currently requests for new third-party entries.

## 6. Up-to-Date Mechanism

This repository now supports tool publishing from tags:

- Workflow: `.github/workflows/publish-dotnet-tool.yml`
- Package: NuGet dotnet tool `DNAKode.VbNet.Lsp` (command: `vbnet-ls`)
- Command remains stable for marketplace config: `vbnet-ls --stdio`

Operationally:

1. Cut release tag `vX.Y.Z`
2. `publish-dotnet-tool.yml` packs/publishes `DNAKode.VbNet.Lsp` (when `NUGET_API_KEY` is set)
3. Users run `dotnet tool update --global DNAKode.VbNet.Lsp`

No marketplace metadata change is needed per release unless command/arguments/file-extension mapping changes.

## 6.1 Package ID and Command Strategy

For NuGet/dotnet tools, package ID and installed command are separate:

- `PackageId` can be organization-prefixed (for example, `DNAKode.VbNet.Lsp`)
- `ToolCommandName` can stay short and stable (`vbnet-ls`)

That means we can use a provenance-friendly package ID without changing the
marketplace command configuration (`vbnet-ls --stdio`).

## 7. Public Posting Attribution Template

For any GitHub issue/PR/comment posted with this account by an agent, start with:

*Posted by Codex agent on behalf of @govert*
