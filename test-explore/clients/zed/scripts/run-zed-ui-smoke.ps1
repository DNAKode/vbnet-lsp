param(
    [string]$ZedPath = 'zed',
    [string]$WorkspacePath = 'test-explore/clients/zed/fixtures/single-file',
    [string]$UserDataDir = '',
    [switch]$RequireUiAutomation
)

$ErrorActionPreference = 'Stop'

& (Join-Path $PSScriptRoot 'run-zed-smoke.ps1') `
    -ZedPath $ZedPath `
    -WorkspacePath $WorkspacePath `
    -UserDataDir $UserDataDir

if ($RequireUiAutomation) {
    throw "Zed UI automation requires a stable command or OS automation harness. The probe smoke passed, but hover/completion/debug UI assertions were not executed."
}

Write-Host "Zed probe smoke passed. UI assertions are skipped because no stable Zed UI automation path is configured."
