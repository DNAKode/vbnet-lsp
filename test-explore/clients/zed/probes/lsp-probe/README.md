# Zed LSP Probe

This minimal LSP server records JSON-RPC messages to the path in
`VBNET_ZED_TEST_LOG` and returns deterministic responses for Zed smoke tests.

Run it with:

```powershell
dotnet run --project test-explore/clients/zed/probes/lsp-probe/VbNet.Zed.LspProbe.csproj
```
