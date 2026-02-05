param(
    [string]$HelixExe = '',
    [string]$ServerExe = '',
    [string]$Workspace = 'test-explore\vbnet-lsp\fixtures\services',
    [string]$File = 'ServiceSamples.vb'
)

$ErrorActionPreference = 'Stop'

function Resolve-HelixExe {
    param([string]$Override)

    if ($Override) {
        if (Test-Path $Override) {
            return (Resolve-Path $Override).Path
        }
        Write-Warning "Helix exe not found at $Override"
    }

    $cmd = Get-Command hx -ErrorAction SilentlyContinue
    if ($cmd -and $cmd.Path) {
        return $cmd.Path
    }

    $knownPaths = @(
        Join-Path $env:USERPROFILE 'scoop\\apps\\helix\\current\\hx.exe',
        'C:\\ProgramData\\chocolatey\\bin\\hx.exe',
        'C:\\Program Files\\Helix\\hx.exe'
    )

    foreach ($candidate in $knownPaths) {
        if (Test-Path $candidate) {
            return (Resolve-Path $candidate).Path
        }
    }

    return $null
}

function Resolve-ServerExe {
    param([string]$Override)

    if ($Override) {
        if (Test-Path $Override) {
            return (Resolve-Path $Override).Path
        }
        Write-Warning "Server exe not found at $Override"
    }

    $localCandidates = @(
        'src\VbNet.LanguageServer.Vb\bin\Debug\net10.0\VbNet.LanguageServer.exe',
        'src\VbNet.LanguageServer.Vb\bin\Release\net10.0\VbNet.LanguageServer.exe'
    )

    foreach ($candidate in $localCandidates) {
        if (Test-Path $candidate) {
            return (Resolve-Path $candidate).Path
        }
    }

    $extensionRoots = @(
        Join-Path $env:USERPROFILE '.vscode\extensions',
        Join-Path $env:USERPROFILE '.vscode-insiders\extensions'
    )

    foreach ($root in $extensionRoots) {
        if (-not (Test-Path $root)) {
            continue
        }

        $candidates = Get-ChildItem -Path $root -Directory -Filter 'dnakode.vbnet-language-support-*' | Sort-Object Name -Descending
        foreach ($candidate in $candidates) {
            $serverPath = Join-Path $candidate.FullName '.server\VbNet.LanguageServer.exe'
            if (Test-Path $serverPath) {
                return (Resolve-Path $serverPath).Path
            }
        }
    }

    return $null
}

$helixExe = Resolve-HelixExe -Override $HelixExe
if (-not $helixExe) {
    Write-Error "Helix (hx) not found. Add it to PATH or pass -HelixExe."
    exit 1
}

$workspacePath = (Resolve-Path $Workspace).Path
if (-not (Test-Path $workspacePath)) {
    Write-Error "Workspace not found at $Workspace"
    exit 1
}

$serverExe = Resolve-ServerExe -Override $ServerExe
if (-not $serverExe) {
    Write-Warning "VB.NET language server executable not found. Update the generated languages.toml manually."
    $serverCommand = 'C:/path/to/VbNet.LanguageServer.exe'
} else {
    $serverCommand = $serverExe.Replace('\', '/')
}

$helixDir = Join-Path $workspacePath '.helix'
New-Item -ItemType Directory -Path $helixDir -Force | Out-Null

$languagesToml = @"
[language-server.vbnet-lsp]
command = \"$serverCommand\"
args = [\"--stdio\"]

[[language]]
name = \"vb\"
scope = \"source.vb\"
file-types = [\"vb\"]
language-id = \"vb\"
language-servers = [\"vbnet-lsp\"]
"@

Set-Content -Path (Join-Path $helixDir 'languages.toml') -Value $languagesToml -Encoding ASCII
Write-Host "Wrote $helixDir\languages.toml"

$targetFile = Join-Path $workspacePath $File
if (-not (Test-Path $targetFile)) {
    Write-Warning "Target file not found at $targetFile. Opening workspace root instead."
    $targetFile = $workspacePath
}

Push-Location $workspacePath
try {
    & $helixExe $targetFile
} finally {
    Pop-Location
}
