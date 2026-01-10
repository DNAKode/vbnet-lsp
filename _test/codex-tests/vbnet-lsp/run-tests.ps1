param(
    [string]$ServerProject = 'src\VbNet.LanguageServer\VbNet.LanguageServer.csproj',
    [string]$BuildConfiguration = 'Debug',
    [string]$DotnetPath = 'dotnet',
    [string]$Transport = 'pipe',
    [string]$LogLevel = 'Information',
    [string]$TestFilePath = '_test\codex-tests\vbnet-lsp\fixtures\basic\Basic.vb',
    [string]$DiagnosticsRootPath = '_test\codex-tests\vbnet-lsp\fixtures\diagnostics',
    [string]$DiagnosticsFilePath = '_test\codex-tests\vbnet-lsp\fixtures\diagnostics\DiagnosticsSample\Class1.vb',
    [string]$DiagnosticsMode = 'openChange',
    [int]$DebounceMs = 300,
    [string]$ExpectedDiagnosticCode = 'BC30311',
    [switch]$SendDidSave,
    [string]$ProtocolLogPath = '_test\codex-tests\logs\protocol-anomalies.jsonl',
    [string]$SnapshotRoot = '_test\codex-tests\vbnet-lsp\snapshots',
    [switch]$SkipBuild,
    [switch]$SkipSnapshot,
    [switch]$Diagnostics
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

function Snapshot-Server {
    param([string]$OutputDir)

    if (-not (Test-Path $OutputDir)) {
        throw "Server output directory not found: $OutputDir"
    }

    $timestamp = Get-Date -Format "yyyyMMdd-HHmmss"
    $snapshotDir = Join-Path $SnapshotRoot $timestamp
    New-Item -ItemType Directory -Path $snapshotDir | Out-Null
    Copy-Item -Path (Join-Path $OutputDir '*') -Destination $snapshotDir -Recurse
    return $snapshotDir
}

function Run-SmokeTest {
    param([string]$ServerPath, [string]$RootPath, [string]$TestFile, [switch]$ExpectDiagnostics)

    $args = @(
        '--serverPath', $ServerPath,
        '--dotnetPath', $DotnetPath,
        '--logLevel', $LogLevel,
        '--transport', $Transport,
        '--rootPath', $RootPath,
        '--testFile', $TestFile,
        '--protocolLog', $protocolLogFullPath
    )

    if ($ExpectDiagnostics) {
        $shouldSendDidSave = $SendDidSave -or ($DiagnosticsMode -in @('openSave','saveOnly'))
        $args += @(
            '--expectDiagnostics',
            '--diagnosticsTimeoutSeconds', '60',
            '--timeoutSeconds', '150',
            '--workspaceLoadDelaySeconds', '5',
            '--diagnosticsMode', $DiagnosticsMode,
            '--debounceMs', $DebounceMs,
            '--expectDiagnosticCode', $ExpectedDiagnosticCode
        )

        if ($shouldSendDidSave) {
            $args += '--sendDidSave'
        }
    }

    & $DotnetPath run --project _test\codex-tests\vbnet-lsp\VbNetLspSmokeTest\VbNetLspSmokeTest.csproj -- @args
}

if (-not $SkipBuild) {
    Build-Server -ProjectPath $ServerProject -Configuration $BuildConfiguration
}

$outputDir = Get-ServerOutputPath -ProjectPath $ServerProject -Configuration $BuildConfiguration
$serverPath = Join-Path $outputDir 'VbNet.LanguageServer.dll'

if (-not (Test-Path $serverPath)) {
    throw "Server binary not found: $serverPath"
}

if (-not $SkipSnapshot) {
    $snapshot = Snapshot-Server -OutputDir $outputDir
    Write-Host "Snapshot saved to $snapshot"
}

if ($Diagnostics) {
    $rootPath = (Resolve-Path $DiagnosticsRootPath).Path
    Run-SmokeTest -ServerPath $serverPath -RootPath $rootPath -TestFile $DiagnosticsFilePath -ExpectDiagnostics
} else {
    $rootPath = Split-Path (Resolve-Path $TestFilePath)
    Run-SmokeTest -ServerPath $serverPath -RootPath $rootPath -TestFile $TestFilePath
}

$runLabel = if ($Diagnostics) { "VB.NET diagnostics Transport=$Transport" } else { "VB.NET smoke Transport=$Transport" }
& _test\codex-tests\Update-TestResults.ps1 -ProtocolLogPath $protocolLogFullPath -RunLabel $runLabel
