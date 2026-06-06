# Zed DAP Probe

This minimal debug adapter records DAP requests to the path in
`VBNET_ZED_DAP_TEST_LOG` and returns deterministic success responses for Zed
debug smoke tests.

Run it with:

```powershell
dotnet run --project test-explore/clients/zed/probes/dap-probe/VbNet.Zed.DapProbe.csproj
```
