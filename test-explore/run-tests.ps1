param(
    [ValidateSet('vbnet-lsp','emacs','dwsim','vscode','vscode-dwsim','all')][string]$Suite = 'all',
    [ValidateSet('core','editors','scale','all')][string]$Theme,
    [ValidateSet('pipe','stdio')][string]$Transport = 'pipe',
    [string]$ProtocolLogPath = 'test-explore\logs\protocol-anomalies.jsonl'
)

$ErrorActionPreference = 'Stop'
if ([System.IO.Path]::IsPathRooted($ProtocolLogPath)) {
    $protocolLogFullPath = $ProtocolLogPath
} else {
    $protocolLogFullPath = Join-Path (Resolve-Path '.').Path $ProtocolLogPath
}
New-Item -ItemType Directory -Path (Split-Path $protocolLogFullPath -Parent) -Force | Out-Null
if (Test-Path $protocolLogFullPath) {
    Clear-Content -Path $protocolLogFullPath
} else {
    New-Item -ItemType File -Path $protocolLogFullPath -Force | Out-Null
}

function Invoke-Suite {
    param([string]$Name)

    switch ($Name) {
        'vbnet-lsp' { & test-explore\vbnet-lsp\run-tests.ps1 }
        'emacs' { & test-explore\clients\emacs\run-tests.ps1 }
        'dwsim' { & test-explore\dwsim\run-tests.ps1 }
        'vscode' {
            Push-Location test-explore\clients\vscode
            try {
                npm test
            } finally {
                Pop-Location
            }
        }
        'vscode-dwsim' {
            $original = @{
                VBNET_DWSIM = $env:VBNET_DWSIM
                FIXTURE_WORKSPACE = $env:FIXTURE_WORKSPACE
                SKIP_VBNET_SMOKE = $env:SKIP_VBNET_SMOKE
                SKIP_VBNET_DEBUG = $env:SKIP_VBNET_DEBUG
                VBNET_TIMING_LOG = $env:VBNET_TIMING_LOG
            }
            $env:VBNET_DWSIM = '1'
            $env:FIXTURE_WORKSPACE = '_external\dwsim'
            $env:SKIP_VBNET_SMOKE = '1'
            $env:SKIP_VBNET_DEBUG = '1'
            if (-not $env:VBNET_TIMING_LOG) {
                $timestamp = Get-Date -Format "yyyyMMdd-HHmmss"
                $env:VBNET_TIMING_LOG = "test-explore\\logs\\vscode-dwsim-timing-$timestamp.jsonl"
            }
            Push-Location test-explore\clients\vscode
            try {
                npm test
            } finally {
                Pop-Location
                $env:VBNET_DWSIM = $original.VBNET_DWSIM
                $env:FIXTURE_WORKSPACE = $original.FIXTURE_WORKSPACE
                $env:SKIP_VBNET_SMOKE = $original.SKIP_VBNET_SMOKE
                $env:SKIP_VBNET_DEBUG = $original.SKIP_VBNET_DEBUG
                $env:VBNET_TIMING_LOG = $original.VBNET_TIMING_LOG
            }
        }
        'all' {
            Invoke-Suite 'vbnet-lsp'
            Invoke-Suite 'emacs'
            Invoke-Suite 'dwsim'
        }
    }
}

if ($Theme) {
    switch ($Theme) {
        'core' { Invoke-Suite 'vbnet-lsp' }
        'editors' {
            Invoke-Suite 'emacs'
            Invoke-Suite 'vscode'
        }
        'scale' {
            Invoke-Suite 'dwsim'
            Invoke-Suite 'vscode-dwsim'
        }
        'all' {
            Invoke-Suite 'vbnet-lsp'
            Invoke-Suite 'emacs'
            Invoke-Suite 'vscode'
            Invoke-Suite 'dwsim'
            Invoke-Suite 'vscode-dwsim'
        }
    }
} else {
    Invoke-Suite $Suite
}

$runLabel = if ($Theme) { "Theme=$Theme Transport=$Transport" } elseif ($Suite -eq 'all') { "Suite=all Transport=$Transport" } else { "Suite=$Suite Transport=$Transport" }
& test-explore\Update-TestResults.ps1 -ProtocolLogPath $protocolLogFullPath -RunLabel $runLabel

