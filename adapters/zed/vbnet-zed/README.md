# VB.NET for Zed

This is the Zed extension snapshot for `vbnet-lsp`.

Development happens in `DNAKode/vbnet-lsp` under
`adapters/zed/vbnet-zed`. The standalone `DNAKode/vbnet-zed` repository is
intended to be generated from this directory.

## Local Development

1. Install Rust with `rustup`.
2. Install the Wasm target:

   ```powershell
   rustup target add wasm32-wasip1
   ```

3. Build the extension crate:

   ```powershell
   cargo check --target wasm32-wasip1
   ```

4. Make a local VB.NET language server available on `PATH` as `vbnet-ls`, or
   configure it in Zed settings:

   ```json
   {
     "lsp": {
       "vbnet-ls": {
         "binary": {
           "path": "C:\\Work\\vbnet-lsp\\src\\VbNet.LanguageServer.Vb\\bin\\Debug\\net10.0\\VbNet.LanguageServer.exe"
         }
       }
     }
   }
   ```

5. In Zed, run `zed: install dev extension` and select this directory.

For detailed implementation milestones, see
`docs/zed-support-plan.md` in the monorepo.

## Current Capability

- Registers `.vb` files as `VB.NET`.
- Registers `vbnet-ls` for VB.NET only.
- Passes Zed LSP settings through to the language server.
- Provides a netcoredbg debug adapter registration and schema.
- Uses the currently available external VB.NET tree-sitter grammar as an early
  bootstrap while the project-owned grammar workstream is built.

The Tree-sitter query files are deliberately conservative placeholders until the
VB.NET grammar schema is fully validated and owned by this project.
