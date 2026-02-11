# vbnet-lsp.nvim

Thin Neovim adapter for the VB.NET language server.

This package intentionally keeps editor logic small and delegates language
semantics to the server binary.

## Features

- Start the VB.NET server backend over stdio.
- Optional Roslyn backend support for comparison and migration.
- Roslyn solution/project targeting commands:
  - `:VbNetSelectSolution`
  - `:VbNetPickSolution`
- Roslyn source-generated document refresh support.
- Optional Roslyn diagnostic refresh on write/insert leave.

## Usage

### VB.NET backend (default)

```lua
require('vbnet_lsp').setup({
  backend = 'vbnet',
  vbnet = {
    cmd = { 'dotnet', '/path/to/VbNet.LanguageServer.dll', '--stdio' },
  },
})
```

### Roslyn backend

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

## Install

### lazy.nvim

```lua
{
  'your-org/vbnet-lsp.nvim',
  ft = { 'vb', 'cs' },
  config = function()
    require('vbnet_lsp').setup({
      backend = 'vbnet',
      vbnet = {
        cmd = { 'dotnet', '/path/to/VbNet.LanguageServer.dll', '--stdio' },
      },
    })
  end,
}
```

### Native package (vim `pack/*`)

Clone into your Neovim package path and call `require('vbnet_lsp').setup(...)`
from your config.

## Notes

- No `nvim-lspconfig` dependency.
- Keep adapter logic editor-specific and minimal.
- For Roslyn backend packaging, ensure VB assemblies are loaded via dedicated
  extension paths only (avoid duplicate analyzer references).

## Package layout

```
vbnet-lsp.nvim/
|-- lua/vbnet_lsp/init.lua
|-- plugin/vbnet_lsp.lua
|-- doc/vbnet-lsp.txt
|-- README.md
`-- LICENSE
```
