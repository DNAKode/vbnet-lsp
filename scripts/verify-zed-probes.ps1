param(
    [string]$Configuration = 'Release'
)

$ErrorActionPreference = 'Stop'

$repoRoot = [System.IO.Path]::GetFullPath((Join-Path $PSScriptRoot '..'))
$lspProject = Join-Path $repoRoot 'test-explore/clients/zed/probes/lsp-probe/VbNet.Zed.LspProbe.csproj'
$dapProject = Join-Path $repoRoot 'test-explore/clients/zed/probes/dap-probe/VbNet.Zed.DapProbe.csproj'
$harnessProject = Join-Path $repoRoot 'test-explore/clients/zed/probes/probe-harness/VbNet.Zed.ProbeHarness.csproj'
$realServerHarnessProject = Join-Path $repoRoot 'test-explore/clients/zed/probes/real-server-harness/VbNet.Zed.RealServerHarness.csproj'

dotnet build $lspProject -c $Configuration
if ($LASTEXITCODE -ne 0) {
    throw "LSP probe build failed with exit code $LASTEXITCODE."
}

dotnet build $dapProject -c $Configuration
if ($LASTEXITCODE -ne 0) {
    throw "DAP probe build failed with exit code $LASTEXITCODE."
}

dotnet build $harnessProject -c $Configuration
if ($LASTEXITCODE -ne 0) {
    throw "Zed probe protocol harness build failed with exit code $LASTEXITCODE."
}

dotnet build $realServerHarnessProject -c $Configuration
if ($LASTEXITCODE -ne 0) {
    throw "Zed real-server protocol harness build failed with exit code $LASTEXITCODE."
}

dotnet run --project $harnessProject -c $Configuration --no-restore -- $repoRoot
if ($LASTEXITCODE -ne 0) {
    throw "Zed probe protocol harness failed with exit code $LASTEXITCODE."
}
