param(
    [string]$Version = '',
    [string]$ZedPath = 'zed',
    [string]$UserDataDir = '',
    [string]$WorkspacePath = 'test-explore/clients/zed/fixtures/single-file',
    [string]$RealServerWorkspacePath = 'test/TestProjects/SmallProject',
    [string]$DebugWorkspacePath = 'test-explore/clients/zed/fixtures/debug-console',
    [switch]$IncludeReleaseAssets,
    [switch]$IncludeLiveZed,
    [switch]$IncludeRealServerZed,
    [switch]$IncludeDebugZed,
    [switch]$SkipExtensionInstallCheck
)

$ErrorActionPreference = 'Stop'

$repoRoot = Split-Path -Parent $PSScriptRoot

function Invoke-ZedReadinessStep {
    param(
        [Parameter(Mandatory = $true)][string]$Name,
        [Parameter(Mandatory = $true)][scriptblock]$Script
    )

    Write-Host "==> $Name"
    & $Script
    Write-Host "PASS: $Name"
}

Invoke-ZedReadinessStep -Name 'Zed extension static verification' -Script {
    if ($Version -ne '') {
        & (Join-Path $repoRoot 'scripts/verify-zed-extension.ps1') -ExpectedVersion $Version
    } else {
        & (Join-Path $repoRoot 'scripts/verify-zed-extension.ps1')
    }
}

Invoke-ZedReadinessStep -Name 'Zed protocol probes' -Script {
    & (Join-Path $repoRoot 'scripts/verify-zed-probes.ps1')
}

Invoke-ZedReadinessStep -Name 'Zed real-server protocol smoke' -Script {
    & (Join-Path $repoRoot 'scripts/verify-zed-real-server.ps1')
}

Invoke-ZedReadinessStep -Name 'Zed Tree-sitter parser/query validation' -Script {
    & (Join-Path $repoRoot 'scripts/verify-zed-tree-sitter.ps1')
}

if ($IncludeReleaseAssets) {
    Invoke-ZedReadinessStep -Name 'Zed release asset availability' -Script {
        if ($Version -ne '') {
            & (Join-Path $repoRoot 'scripts/verify-zed-release-assets.ps1') -Version $Version
        } else {
            & (Join-Path $repoRoot 'scripts/verify-zed-release-assets.ps1')
        }
    }
} else {
    Write-Host 'SKIP: Zed release asset availability. Pass -IncludeReleaseAssets after publishing the matching vbnet-lsp release.'
}

if ($IncludeLiveZed -or $IncludeRealServerZed -or $IncludeDebugZed) {
    if ($UserDataDir -eq '') {
        throw 'Pass -UserDataDir for live Zed smoke gates. The profile must already have the VB.NET dev extension installed through zed: install dev extension.'
    }

    $commonZedArgs = @{
        ZedPath = $ZedPath
        UserDataDir = $UserDataDir
    }
    if ($SkipExtensionInstallCheck) {
        $commonZedArgs.SkipExtensionInstallCheck = $true
    }
}

if ($IncludeLiveZed) {
    Invoke-ZedReadinessStep -Name 'Real Zed probe smoke' -Script {
        & (Join-Path $repoRoot 'test-explore/clients/zed/scripts/run-zed-smoke.ps1') @commonZedArgs -WorkspacePath $WorkspacePath
    }
} else {
    Write-Host 'SKIP: Real Zed probe smoke. Pass -IncludeLiveZed with a prepared isolated Zed profile.'
}

if ($IncludeRealServerZed) {
    Invoke-ZedReadinessStep -Name 'Real Zed local-server smoke' -Script {
        & (Join-Path $repoRoot 'test-explore/clients/zed/scripts/run-zed-smoke.ps1') @commonZedArgs -WorkspacePath $RealServerWorkspacePath -Mode RealServer
    }
} else {
    Write-Host 'SKIP: Real Zed local-server smoke. Pass -IncludeRealServerZed with a prepared isolated Zed profile.'
}

if ($IncludeDebugZed) {
    Invoke-ZedReadinessStep -Name 'Real Zed debugger smoke launch' -Script {
        & (Join-Path $repoRoot 'test-explore/clients/zed/scripts/run-zed-debug-smoke.ps1') @commonZedArgs -WorkspacePath $DebugWorkspacePath -Automate
    }
} else {
    Write-Host 'SKIP: Real Zed debugger smoke launch. Pass -IncludeDebugZed with a prepared isolated Zed profile.'
}

Write-Host 'Zed readiness runner completed selected gates.'
