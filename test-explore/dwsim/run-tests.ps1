param(
    [string]$ServerProject = 'src\VbNet.LanguageServer.Vb\VbNet.LanguageServer.Vb.vbproj',
    [string]$BuildConfiguration = 'Debug',
    [string]$DotnetPath = 'dotnet',
    [string]$Transport = 'pipe',
    [string]$LogLevel = 'Information',
    [string]$WorkspaceRoot = '_external\dwsim',
    [string]$TestFilePath = '_external\dwsim\DWSIM\ApplicationEvents.vb',
    [string]$ProtocolLogPath = 'test-explore\logs\protocol-anomalies.jsonl',
    [string]$TimingLogPath = 'test-explore\logs\timing.jsonl',
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

if ([System.IO.Path]::IsPathRooted($TimingLogPath)) {
    $timingLogFullPath = $TimingLogPath
} else {
    $timingLogFullPath = Join-Path (Resolve-Path '.').Path $TimingLogPath
}
New-Item -ItemType Directory -Path (Split-Path $timingLogFullPath -Parent) -Force | Out-Null
if (-not (Test-Path $timingLogFullPath)) {
    New-Item -ItemType File -Path $timingLogFullPath -Force | Out-Null
} else {
    Clear-Content -Path $timingLogFullPath
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
$solutionPath = Join-Path $rootPath 'DWSIM.sln'
$serviceManifest = 'test-explore\dwsim\service-tests.json'
$timestamp = Get-Date -Format "yyyyMMdd-HHmmss"
$serviceLogPath = "test-explore\logs\dwsim-service-tests-$timestamp.jsonl"

$smokeArgs = @(
    '--serverPath', $serverPath,
    '--dotnetPath', $DotnetPath,
    '--logLevel', $LogLevel,
    '--transport', $Transport,
    '--rootPath', $rootPath,
    '--workspaceProjectPath', $solutionPath,
    '--workspaceProjectSearchPath', $rootPath,
    '--workspaceExcludePath', '.git;bin;obj',
    '--workspaceIgnoreSolutionFiles', 'false',
    '--workspaceMaxProjectResults', '200',
    '--workspaceLoadDelaySeconds', '10',
    '--testFile', $testFile,
    '--serviceManifest', $serviceManifest,
    '--serviceTimeoutSeconds', '120',
    '--serviceLog', $serviceLogPath,
    '--protocolLog', $protocolLogFullPath,
    '--timingLog', $timingLogFullPath,
    '--timingLabel', 'DWSIM'
)

Write-Host "Running DWSIM smoke against: $rootPath"
Write-Host "Test file: $testFile"

$duration = Measure-Command {
    & $DotnetPath run --project test-explore\vbnet-lsp\VbNetLspSmokeTest.Vb\VbNetLspSmokeTest.Vb.vbproj -- @smokeArgs
}

Write-Host ("DWSIM smoke duration: {0:n2}s" -f $duration.TotalSeconds)

$runLabel = "DWSIM smoke Transport=$Transport"
& test-explore\Update-TestResults.ps1 -ProtocolLogPath $protocolLogFullPath -TimingLogPath $timingLogFullPath -RunLabel $runLabel

