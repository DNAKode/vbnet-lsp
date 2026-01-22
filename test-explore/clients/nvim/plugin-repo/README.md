# VB.NET LSP Neovim Plugin (Prototype)

This is a minimal Neovim plugin skeleton for VB.NET language server support.
It is intended as a starting point for publishing a first-class Neovim plugin
once we solidify backend options and UX details.

## Features (initial)

- Start a VB.NET LSP or Roslyn LSP backend using `vim.lsp.start`.
- Optional `solution/open` or `project/open` notification for Roslyn.
- `:VbNetSelectSolution` command to retarget the Roslyn server.
- `:VbNetPickSolution` for simple solution discovery.
- Source-generated document support (`roslyn-source-generated://`).
- Diagnostic refresh on `BufWritePost`/`InsertLeave` (Roslyn backend).

## Usage

### VB.NET server (default)

```lua
require('vbnet_lsp').setup({
  backend = 'vbnet',
  vbnet = {
    cmd = { 'dotnet', '/path/to/VbNet.LanguageServer.dll', '--stdio' },
  },
})
```

### Roslyn server

```lua
require('vbnet_lsp').setup({
  backend = 'roslyn',
  roslyn = {
    cmd = { 'dotnet', '/path/to/Microsoft.CodeAnalysis.LanguageServer.dll', '--stdio', '--logLevel', 'Information', '--extensionLogDirectory', '/path/to/logs' },
    args = {
      '--extension', '/path/to/roslyn-vb/Microsoft.CodeAnalysis.VisualBasic.Workspaces.dll',
      '--extension', '/path/to/roslyn-vb/Microsoft.CodeAnalysis.VisualBasic.Features.dll',
    },
    solution = '/path/to/YourSolution.sln',
    filewatching = 'off',
  },
})
```

### lazy.nvim example

```lua
{
  'your-org/vbnet-lsp.nvim',
  ft = { 'vb', 'cs' },
  config = function()
    require('vbnet_lsp').setup({
      backend = 'roslyn',
      roslyn = {
        cmd = { 'dotnet', '/path/to/Microsoft.CodeAnalysis.LanguageServer.dll', '--stdio', '--logLevel', 'Information', '--extensionLogDirectory', '/tmp/roslyn-ls' },
        args = {
          '--extension', '/path/to/roslyn-vb/Microsoft.CodeAnalysis.VisualBasic.Workspaces.dll',
          '--extension', '/path/to/roslyn-vb/Microsoft.CodeAnalysis.VisualBasic.Features.dll',
        },
        filewatching = 'off',
      },
    })
  end,
}
```

## Notes

- This module does not depend on `nvim-lspconfig`.
- It is intentionally minimal; we will expand once the UX is validated.
- For Roslyn, ensure VB assemblies are *only* in the extension directory to
  avoid duplicate analyzer references.

## Structure (publishable repo)

```
vbnet-lsp.nvim/
├─ lua/vbnet_lsp/init.lua
├─ plugin/vbnet_lsp.lua
├─ doc/vbnet-lsp.txt
├─ README.md
└─ LICENSE
```
