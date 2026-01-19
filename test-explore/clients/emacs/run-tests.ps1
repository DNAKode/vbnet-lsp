param(
    [ValidateSet('vbnet','all')][string]$Suite = 'all',
    [string]$EmacsRoot = 'test-explore\clients\emacs\emacs',
    [string]$VbNetLspDll = 'src\VbNet.LanguageServer.Vb\bin\Debug\net10.0\VbNet.LanguageServer.dll'
)

$ErrorActionPreference = 'Stop'

function Get-EmacsExe {
    param([string]$Root)
    $candidate = Join-Path $Root 'bin\emacs.exe'
    if (Test-Path $candidate) {
        return $candidate
    }
    return $null
}

function Download-Emacs {
    param([string]$Root)
    $zipUrl = 'https://ftp.gnu.org/gnu/emacs/windows/emacs-29/emacs-29.4.zip'
    $zipPath = Join-Path $Root 'emacs.zip'

    if (-not (Test-Path $Root)) {
        New-Item -ItemType Directory -Path $Root | Out-Null
    }

    Write-Host "Downloading Emacs from $zipUrl"
    Invoke-WebRequest -Uri $zipUrl -OutFile $zipPath

    Write-Host "Extracting Emacs..."
    Expand-Archive -Path $zipPath -DestinationPath $Root -Force

    $expanded = Get-ChildItem -Path $Root -Directory | Where-Object { $_.Name -like 'emacs-*' } | Select-Object -First 1
    if ($expanded) {
        Get-ChildItem -Path $expanded.FullName -Force | Move-Item -Destination $Root -Force
        Remove-Item -Recurse -Force $expanded.FullName
    }

    Remove-Item $zipPath -Force
}

$emacsExe = Get-EmacsExe -Root $EmacsRoot
if (-not $emacsExe) {
    Download-Emacs -Root $EmacsRoot
    $emacsExe = Get-EmacsExe -Root $EmacsRoot
}

if (-not (Test-Path $emacsExe)) {
    throw "Emacs executable not found at $emacsExe"
}

if ($Suite -eq 'vbnet' -or $Suite -eq 'all') {
    if (-not (Test-Path $VbNetLspDll)) {
        Write-Warning "VB.NET LSP DLL not found at $VbNetLspDll. Build it before running VB.NET tests."
    }
}

if (Test-Path $VbNetLspDll) {
    $env:VBNET_LSP_DLL = (Resolve-Path $VbNetLspDll).Path
} else {
    $env:VBNET_LSP_DLL = ''
}

$scriptPath = Resolve-Path 'test-explore\clients\emacs\eglot-smoke.el'

function Invoke-EmacsSuite {
    param([string]$RunSuite)
    $env:CODEX_SUITE = $RunSuite
    & $emacsExe --batch -l $scriptPath
}

Invoke-EmacsSuite -RunSuite 'vbnet'

