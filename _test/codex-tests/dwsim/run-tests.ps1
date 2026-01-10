param(
    [string]$ServerProject = 'src\VbNet.LanguageServer\VbNet.LanguageServer.csproj',
    [string]$BuildConfiguration = 'Debug',
    [string]$DotnetPath = 'dotnet',
    [string]$Transport = 'pipe',
    [string]$LogLevel = 'Information',
    [string]$WorkspaceRoot = '_test\dwsim',
    [string]$TestFilePath = '_test\dwsim\DWSIM\ApplicationEvents.vb',
    [string]$ProtocolLogPath = '_test\codex-tests\logs\protocol-anomalies.jsonl',
    [switch]$SkipBuild
)

$ErrorActionPreference = 'Stop'

if ([System.IO.Path]::IsPathRooted($ProtocolLogPath)) {
    $protocolLogFullPath = $ProtocolLogPath
} else {
    $protocolLogFullPath = Join-Path (Resolve-Path '.').Path $ProtocolLogPath
}
New-Item -ItemType Directory -Path (Split-Path $protocolLogFullPath -Parent) -Force | Out-Null
if (-not (Test-Path $protocolLogFullPath)) {
    New-Item -ItemType File -Path $protocolLogFullPath -Force | Out-Null
} else {
    Clear-Content -Path $protocolLogFullPath
}

function Get-ServerOutputPath {
    param([string]$ProjectPath, [string]$Configuration)

    $projectFull = Resolve-Path $ProjectPath
    $projectDir = Split-Path $projectFull
    $outputDir = Join-Path $projectDir "bin\$Configuration\net10.0"
    return $outputDir
}

function Build-Server {
    param([string]$ProjectPath, [string]$Configuration)

    & $DotnetPath build $ProjectPath -c $Configuration
}

if (-not $SkipBuild) {
    Build-Server -ProjectPath $ServerProject -Configuration $BuildConfiguration
}

$outputDir = Get-ServerOutputPath -ProjectPath $ServerProject -Configuration $BuildConfiguration
$serverPath = Join-Path $outputDir 'VbNet.LanguageServer.dll'

if (-not (Test-Path $serverPath)) {
    throw "Server binary not found: $serverPath"
}

$rootPath = (Resolve-Path $WorkspaceRoot).Path
$testFile = (Resolve-Path $TestFilePath).Path

$smokeArgs = @(
    '--serverPath', $serverPath,
    '--dotnetPath', $DotnetPath,
    '--logLevel', $LogLevel,
    '--transport', $Transport,
    '--rootPath', $rootPath,
    '--testFile', $testFile,
    '--protocolLog', $protocolLogFullPath
)

Write-Host "Running DWSIM smoke against: $rootPath"
Write-Host "Test file: $testFile"

$duration = Measure-Command {
    & $DotnetPath run --project _test\codex-tests\vbnet-lsp\VbNetLspSmokeTest\VbNetLspSmokeTest.csproj -- @smokeArgs
}

Write-Host ("DWSIM smoke duration: {0:n2}s" -f $duration.TotalSeconds)

$runLabel = "DWSIM smoke Transport=$Transport"
& _test\codex-tests\Update-TestResults.ps1 -ProtocolLogPath $protocolLogFullPath -RunLabel $runLabel
