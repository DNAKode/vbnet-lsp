# `VB.NET` Language Support

First-class Visual Basic (.NET) support for Visual Studio Code, powered by a custom language server.

## Features
- IntelliSense (completion, signature help, hover)
- Diagnostics (push + pull)
- Formatting and rename
- Code actions, including Roslyn-backed Extract Method
- Symbol search and document symbols
- Semantic tokens and folding
- Type definition + implementation
- Call/type hierarchy + document highlights
- Selection ranges + document links
- Best-effort fallback support for old-style / non-SDK VB.NET Framework project files
- Debugging with bundled netcoredbg

## Getting Started
1) Open a `VB.NET` project (`.sln`, `.slnf`, or `.vbproj`).
2) The language server starts automatically and scans your workspace.
3) Configure settings under `vbnet.*` if needed.

## Debugging
This extension bundles `netcoredbg` for supported platforms. To override with a custom debugger binary, set:
`vbnet.debugger.path`

Note: On macOS arm64 the bundled debugger is sourced from the Cliffback community build (see notices in the extension package).

## Troubleshooting
When sharing diagnostics or logs in GitHub issues, set `vbnet.output.language` to `en-US` to request English output from the language server, .NET CLI, MSBuild, and Roslyn where supported.

## Resources
- Repository: https://github.com/DNAKode/vbnet-lsp
- Issues: https://github.com/DNAKode/vbnet-lsp/issues

