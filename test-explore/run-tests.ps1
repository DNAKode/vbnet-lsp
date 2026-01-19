param(
    [ValidateSet('vbnet-lsp','emacs','dwsim','all')][string]$Suite = 'all',
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

switch ($Suite) {
    'vbnet-lsp' { & test-explore\vbnet-lsp\run-tests.ps1 }
    'emacs' { & test-explore\clients\emacs\run-tests.ps1 }
    'dwsim' { & test-explore\dwsim\run-tests.ps1 }
    'all' {
        & test-explore\vbnet-lsp\run-tests.ps1
        & test-explore\clients\emacs\run-tests.ps1
        & test-explore\dwsim\run-tests.ps1
    }
}

$runLabel = if ($Suite -eq 'all') { "Suite=all Transport=$Transport" } else { "Suite=$Suite Transport=$Transport" }
& test-explore\Update-TestResults.ps1 -ProtocolLogPath $protocolLogFullPath -RunLabel $runLabel

