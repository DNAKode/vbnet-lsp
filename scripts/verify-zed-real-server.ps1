param(
    [string]$ServerPath = ''
)

$ErrorActionPreference = 'Stop'

$repoRoot = [System.IO.Path]::GetFullPath((Join-Path $PSScriptRoot '..'))
$serverProject = Join-Path $repoRoot 'src/VbNet.LanguageServer.Vb/VbNet.LanguageServer.Vb.vbproj'
$harnessProject = Join-Path $repoRoot 'test-explore/clients/zed/probes/real-server-harness/VbNet.Zed.RealServerHarness.csproj'

dotnet build $serverProject -c Debug
if ($LASTEXITCODE -ne 0) {
    throw "VB.NET language server build failed with exit code $LASTEXITCODE."
}

dotnet build $harnessProject -c Release
if ($LASTEXITCODE -ne 0) {
    throw "Zed real-server protocol harness build failed with exit code $LASTEXITCODE."
}

if ($ServerPath -eq '') {
    $ServerPath = Join-Path $repoRoot 'src/VbNet.LanguageServer.Vb/bin/Debug/net10.0/VbNet.LanguageServer.dll'
}

dotnet run --project $harnessProject -c Release --no-restore -- $repoRoot $ServerPath
if ($LASTEXITCODE -ne 0) {
    throw "Zed real-server protocol harness failed with exit code $LASTEXITCODE."
}
