param(
    [string]$ServerProject = 'src\VbNet.LanguageServer\VbNet.LanguageServer.csproj',
    [string]$BuildConfiguration = 'Debug',
    [string]$DotnetPath = 'dotnet',
    [string]$Transport = 'pipe',
    [string]$LogLevel = 'Information',
    [string]$TestFilePath = '_test\codex-tests\vbnet-lsp\fixtures\basic\Basic.vb',
    [string]$SnapshotRoot = '_test\codex-tests\vbnet-lsp\snapshots',
    [switch]$SkipBuild,
    [switch]$SkipSnapshot
)

$ErrorActionPreference = 'Stop'

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
    param([string]$ServerPath, [string]$RootPath, [string]$TestFile)

    $args = @(
        '--serverPath', $ServerPath,
        '--dotnetPath', $DotnetPath,
        '--logLevel', $LogLevel,
        '--transport', $Transport,
        '--rootPath', $RootPath,
        '--testFile', $TestFile
    )

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

$rootPath = Split-Path (Resolve-Path $TestFilePath)
Run-SmokeTest -ServerPath $serverPath -RootPath $rootPath -TestFile $TestFilePath
