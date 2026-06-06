param(
    [string]$ZedPath = 'zed',
    [string]$UserDataDir = '',
    [string]$ExtensionPath = 'adapters/zed/vbnet-zed'
)

$ErrorActionPreference = 'Stop'

$repoRoot = [System.IO.Path]::GetFullPath((Join-Path $PSScriptRoot '..\..\..\..'))

if (-not (Get-Command $ZedPath -ErrorAction SilentlyContinue)) {
    throw "Zed was not found as '$ZedPath'. Install Zed or pass -ZedPath."
}

if ($UserDataDir -eq '') {
    $UserDataDir = Join-Path ([System.IO.Path]::GetTempPath()) 'vbnet-zed-profile'
}
$userData = [System.IO.Path]::GetFullPath($UserDataDir)

if ([System.IO.Path]::IsPathRooted($ExtensionPath)) {
    $extension = [System.IO.Path]::GetFullPath($ExtensionPath)
} else {
    $extension = [System.IO.Path]::GetFullPath((Join-Path $repoRoot $ExtensionPath))
}

if (-not (Test-Path -LiteralPath $extension -PathType Container)) {
    throw "Extension path not found: $extension"
}

$runningZed = Get-Process -Name Zed -ErrorAction SilentlyContinue
if ($runningZed) {
    $processList = ($runningZed | ForEach-Object { "$($_.Id) $($_.Path)" }) -join '; '
    throw "Zed is already running, so this helper cannot prepare an isolated --user-data-dir profile. Close existing Zed processes and rerun. Running processes: $processList"
}

New-Item -ItemType Directory -Path $userData -Force | Out-Null

Write-Host "Launching Zed with isolated profile: $userData"
Write-Host "Install the dev extension from: $extension"
Write-Host "In Zed, run 'zed: install dev extension', select that directory, then close Zed."

$process = Start-Process -FilePath $ZedPath `
    -ArgumentList @('--foreground', '--user-data-dir', $userData, $extension) `
    -WorkingDirectory $extension `
    -PassThru

$process.WaitForExit()

$extensionIndex = Join-Path $userData 'extensions/index.json'
if (-not (Test-Path -LiteralPath $extensionIndex -PathType Leaf)) {
    throw "Zed exited, but the isolated profile still has no extensions index: $extensionIndex"
}

$extensionIndexText = Get-Content -LiteralPath $extensionIndex -Raw
if (-not $extensionIndexText.Contains('"vbnet"')) {
    throw "Zed exited, but the isolated profile does not list the VB.NET extension in $extensionIndex."
}

Write-Host "Zed profile is prepared for VB.NET smoke tests: $userData"
