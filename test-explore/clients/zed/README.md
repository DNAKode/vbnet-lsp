# Zed Client Exploration

This directory contains Zed-specific smoke-test scaffolding for the VB.NET
extension.

The current public Zed docs describe installing a dev extension from the UI or
the `zed: install dev extension` action and checking `Zed.log`; they do not
document a stable headless extension-test command. The first automated layer
therefore focuses on static verification and reproducible manual/UI smoke steps.

## Static Verification

```powershell
scripts/verify-zed-extension.ps1
```

This validates the extension layout, checks the Zed manifest and language
metadata, runs `cargo check --target wasm32-wasip1`, and runs Rust unit tests.

## Manual Zed Smoke

1. Build the language server:

   ```powershell
   dotnet build src/VbNet.LanguageServer.Vb/VbNet.LanguageServer.Vb.vbproj -c Debug
   ```

2. Start Zed with foreground logging:

   ```powershell
   zed --foreground test/TestProjects/SmallProject
   ```

3. Install the dev extension from:

   ```text
   adapters/zed/vbnet-zed
   ```

4. Configure `lsp.vbnet-ls.binary.path` to the local server executable.

5. Open `Module1.vb` and verify:

   - Zed assigns the language as `VB.NET`.
   - Zed starts `vbnet-ls`.
   - Hover, completion, diagnostics, definition, and symbols respond.
   - Opening `SmallProject.sln` and `SmallProject.slnx` does not start C#
     tooling for VB.NET files.

## Script Stubs

`scripts/run-zed-smoke.ps1` and `scripts/run-zed-ui-smoke.ps1` fail clearly
until a stable Zed automation path is selected.
