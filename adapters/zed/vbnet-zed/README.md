# VB.NET for Zed

VB.NET language support for Zed, generated from the canonical `vbnet-lsp`
repository.

Development happens in `DNAKode/vbnet-lsp` under
`adapters/zed/vbnet-zed`. The standalone `DNAKode/vbnet-zed` repository is
mirrored distribution output.

The VB.NET Tree-sitter grammar is authored in `DNAKode/vbnet-lsp` under
`tree-sitter-vbnet`. The standalone `DNAKode/tree-sitter-vbnet` repository is
also mirrored distribution output and exists so Zed can clone the grammar from
a public repository.

## Installation

After publication, install `VB.NET` from Zed's extension registry.

For local development, install Rust with `rustup`, add the Wasm target, then
install this directory as a dev extension:

```powershell
rustup target add wasm32-wasip1
cargo check --target wasm32-wasip1
```

In Zed, run `zed: install dev extension` and select:

```text
adapters/zed/vbnet-zed
```

## Language Server

The extension starts `vbnet-ls` over stdio. Server resolution order is:

1. `lsp.vbnet-ls.binary.path` from Zed settings.
2. `vbnet-ls` or `VbNet.LanguageServer` on `PATH`.
3. The pinned `DNAKode/vbnet-lsp` GitHub Release asset matching this extension
   version.

Local server development example:

```json
{
  "lsp": {
    "vbnet-ls": {
      "binary": {
        "path": "C:\\Work\\vbnet-lsp\\src\\VbNet.LanguageServer.Vb\\bin\\Debug\\net10.0\\VbNet.LanguageServer.exe",
        "arguments": ["--stdio"]
      }
    }
  }
}
```

Workspace settings pass through to the server through Zed's LSP settings APIs.
Use explicit settings when a workspace contains multiple solutions:

```json
{
  "languages": {
    "VB.NET": {
      "language_servers": ["vbnet-ls"],
      "tab_size": 4
    }
  },
  "lsp": {
    "vbnet-ls": {
      "settings": {
        "workspace": {
          "solutionPath": "src/App.slnx",
          "projectSearchPaths": ["src"],
          "excludePaths": ["bin", "obj"]
        },
        "msbuildPath": "C:\\Program Files\\Microsoft Visual Studio\\2022\\Community\\MSBuild\\Current\\Bin\\MSBuild.exe",
        "diagnostics": {
          "mode": "workspace"
        },
        "semanticTokens": true,
        "formatting": true,
        "logLevel": "Information"
      }
    }
  }
}
```

## Workspace Behavior

- `.vb` files are associated with `VB.NET`.
- The extension registers only the `VB.NET` language and `vbnet-ls`.
- It does not register aliases or language servers for C#.
- mixed VB.NET/C# solutions can use the `.sln` or `.slnx` for VB semantic
  context while C# buffers remain owned by Zed's C# support.
- When automatic solution discovery is ambiguous, configure
  `workspace.solutionPath` explicitly.

## Debugging

The extension registers the `netcoredbg` debug adapter and a schema for
explicit launch/attach settings. The adapter resolves `netcoredbg` from:

1. a path provided through Zed's debug task UI,
2. a repo-local development checkout under `_external/netcoredbg/bin` or
   `_external/bin`,
3. a curated platform download matching the VS Code extension's
   `netcoredbg-assets.json` pins,
4. `PATH`.

Explicit launch example:

```json
{
  "adapter": "netcoredbg",
  "label": "Debug VB.NET console",
  "config": {
    "type": "netcoredbg",
    "request": "launch",
    "name": "Debug VB.NET console",
    "program": "bin/Debug/net10.0/MyApp.dll",
    "cwd": "$ZED_WORKTREE_ROOT",
    "args": [],
    "env": {},
    "stopAtEntry": false,
    "justMyCode": true,
    "enableStepFiltering": true
  }
}
```

The debug locator can infer a program from simple `dotnet build` or `dotnet run`
tasks when a single VB.NET project output is available. Use an explicit
`program` for multi-project workspaces or nonstandard output paths.

Attach requires an explicit `processId` where Zed exposes attach configuration.
Platform attach behavior still needs real-Zed smoke validation before it is
treated as release-proven.

## Current Capability

- Registers `.vb` files as `VB.NET`.
- Registers `vbnet-ls` for VB.NET only.
- Provides Tree-sitter queries for highlighting, outline, folding,
  indentation, bracket matching, and text objects using the owned VB.NET
  grammar.
- Passes Zed LSP initialization options and workspace configuration through to
  the server.
- Resolves the server from configured path, `PATH`, or pinned release download.
- Provides a `netcoredbg` debug adapter registration, schema, and initial debug
  locator.
- Resolves the debugger from an explicit adapter path, repo-local development
  binaries, curated `netcoredbg` downloads, or `PATH`.

## Troubleshooting

- Missing server: install `DNAKode.VbNet.Lsp` as a .NET tool, put `vbnet-ls` on
  `PATH`, configure `lsp.vbnet-ls.binary.path`, or publish the matching GitHub
  Release server archive.
- Download blocked: allow Zed's `download_file` capability for
  `github.com/DNAKode/vbnet-lsp`, `github.com/Samsung/netcoredbg`, and
  `github.com/Cliffback/netcoredbg-macOS-arm64.nvim`, then retry the operation.
- Local server or debugger launch blocked: allow Zed's `process:exec`
  capability for the configured `vbnet-ls`, `dotnet`, or `netcoredbg` command.
- Unsupported platform: build the server locally and configure
  `lsp.vbnet-ls.binary.path`.
- Missing .NET SDK or MSBuild: install the .NET SDK used by the project and, if
  auto-detection fails, configure `msbuildPath`.
- Project load failures: check `Zed.log` and language server stderr output for
  MSBuild, SDK, or solution-selection errors.
- Missing debugger: install `netcoredbg` on `PATH`, configure the debug adapter
  path explicitly, or retry with network access so the curated debugger asset can
  be downloaded.
- Breakpoints do not bind: build the project first and launch the compiled
  Debug `.dll` with matching source files.

Issues and server releases are tracked in `DNAKode/vbnet-lsp`.
