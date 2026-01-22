param(
    [string]$NvimRoot = 'test-explore\clients\nvim\nvim',
    [string]$NvimExe = '',
    [string]$VbNetLspDll = 'src\VbNet.LanguageServer.Vb\bin\Debug\net10.0\VbNet.LanguageServer.dll',
    [string]$Workspace = 'test\TestProjects\SmallProject',
    [string]$File = 'test\TestProjects\SmallProject\Module1.vb',
    [ValidateSet('vbnet','csharp','roslyn-vb','all')][string]$Suite = 'vbnet'
)

$ErrorActionPreference = 'Stop'

function Get-NvimExe {
    param([string]$Root, [string]$Override)
    if ($Override -and (Test-Path $Override)) {
        return $Override
    }

    $candidate = Join-Path $Root 'nvim-win64\bin\nvim.exe'
    if (Test-Path $candidate) {
        return $candidate
    }

    return $null
}

function Download-Nvim {
    param([string]$Root)
    $zipUrl = 'https://github.com/neovim/neovim/releases/download/v0.11.0/nvim-win64.zip'
    $zipPath = Join-Path $Root 'nvim.zip'

    if (-not (Test-Path $Root)) {
        New-Item -ItemType Directory -Path $Root | Out-Null
    }

    Write-Host "Downloading Neovim from $zipUrl"
    Invoke-WebRequest -Uri $zipUrl -OutFile $zipPath

    Write-Host "Extracting Neovim..."
    Expand-Archive -Path $zipPath -DestinationPath $Root -Force
    Remove-Item $zipPath -Force
}

$nvimExe = Get-NvimExe -Root $NvimRoot -Override $NvimExe
if (-not $nvimExe) {
    Download-Nvim -Root $NvimRoot
    $nvimExe = Get-NvimExe -Root $NvimRoot -Override $NvimExe
}

if (-not (Test-Path $nvimExe)) {
    throw "Neovim executable not found at $nvimExe"
}

if (-not (Test-Path $VbNetLspDll)) {
    Write-Warning "VB.NET LSP DLL not found at $VbNetLspDll. Build it before running VB.NET tests."
}

$logDir = 'test-explore\clients\nvim\logs'
New-Item -ItemType Directory -Path $logDir -Force | Out-Null
$timestamp = Get-Date -Format "yyyyMMdd-HHmmss"
$env:NVIM_HARNESS_LOG = Join-Path $logDir "nvim-$timestamp.log"

if (Test-Path $VbNetLspDll) {
    $env:VBNET_LSP_DLL = (Resolve-Path $VbNetLspDll).Path
} else {
    $env:VBNET_LSP_DLL = ''
}

$env:VBNET_LSP_WORKSPACE = (Resolve-Path $Workspace).Path
$env:VBNET_LSP_FILE = (Resolve-Path $File).Path

function Invoke-NvimSuite {
    param([string]$RunSuite)

    $env:CODEX_SUITE = $RunSuite
    $scriptPath = Resolve-Path 'test-explore\clients\nvim\nvim-smoke.lua'
    & $nvimExe --headless -u NONE -l $scriptPath
    if ($LASTEXITCODE -ne 0) {
        throw "Neovim LSP smoke test failed. See $env:NVIM_HARNESS_LOG"
    }
}

if ($Suite -eq 'vbnet' -or $Suite -eq 'all') {
    Invoke-NvimSuite -RunSuite 'vbnet'
}

if ($Suite -eq 'csharp' -or $Suite -eq 'roslyn-vb' -or $Suite -eq 'all') {
    $roslynCmd = $env:ROSLYN_LS_CMD
    $roslynDll = $env:ROSLYN_LS_DLL
    if (-not $roslynCmd -and -not $roslynDll) {
        Write-Warning "ROSLYN_LS_CMD or ROSLYN_LS_DLL not set. Skipping Roslyn Neovim smoke test."
    } else {
        if ($Suite -eq 'csharp' -or $Suite -eq 'all') {
            $env:ROSLYN_LSP_WORKSPACE = (Resolve-Path 'test-explore\clients\nvim\fixtures\CSharpSample').Path
            $env:ROSLYN_LSP_FILE = (Resolve-Path 'test-explore\clients\nvim\fixtures\CSharpSample\Program.cs').Path
            Invoke-NvimSuite -RunSuite 'csharp'
        }

        if ($Suite -eq 'roslyn-vb' -or $Suite -eq 'all') {
            $env:ROSLYN_LSP_WORKSPACE = (Resolve-Path 'test\TestProjects\SmallProject').Path
            $env:ROSLYN_LSP_FILE = (Resolve-Path 'test\TestProjects\SmallProject\Module1.vb').Path
            Invoke-NvimSuite -RunSuite 'roslyn-vb'
        }
    }
}
