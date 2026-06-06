param(
    [string]$ExtensionPath = 'adapters/zed/vbnet-zed',
    [string]$Repository = 'DNAKode/vbnet-lsp',
    [string]$Version = ''
)

$ErrorActionPreference = 'Stop'

function Get-ManifestValue {
    param(
        [Parameter(Mandatory = $true)][string]$Manifest,
        [Parameter(Mandatory = $true)][string]$Key
    )

    $pattern = "^\s*$([regex]::Escape($Key))\s*=\s*`"([^`"]+)`"\s*$"
    $match = [regex]::Match($Manifest, $pattern, [System.Text.RegularExpressions.RegexOptions]::Multiline)
    if (-not $match.Success) {
        throw "extension.toml is missing string key '$Key'."
    }

    return $match.Groups[1].Value
}

$repoRoot = [System.IO.Path]::GetFullPath((Join-Path $PSScriptRoot '..'))
$manifestPath = [System.IO.Path]::GetFullPath((Join-Path $repoRoot (Join-Path $ExtensionPath 'extension.toml')))
if (-not (Test-Path -LiteralPath $manifestPath -PathType Leaf)) {
    throw "Zed extension manifest not found: $manifestPath"
}

if ($Version -eq '') {
    $manifest = Get-Content -LiteralPath $manifestPath -Raw
    $Version = Get-ManifestValue -Manifest $manifest -Key 'version'
}

$tag = if ($Version.StartsWith('v')) { $Version } else { "v$Version" }
$uri = "https://api.github.com/repos/$Repository/releases/tags/$tag"

try {
    $release = Invoke-RestMethod -Uri $uri -Headers @{ 'User-Agent' = 'vbnet-lsp-zed-release-verifier' }
} catch {
    $details = if ($_.ErrorDetails.Message) { " $($_.ErrorDetails.Message)" } else { '' }
    throw "Could not load GitHub release '$tag' from '$Repository'. Publish the server release before relying on Zed release downloads.$details"
}

$assetNames = @($release.assets | ForEach-Object { $_.name })
$requiredAssets = @(
    'vbnet-language-server-win-x64.zip',
    'vbnet-language-server-linux-x64.tar.gz',
    'vbnet-language-server-osx-x64.tar.gz',
    'vbnet-language-server-osx-arm64.tar.gz'
)

foreach ($requiredAsset in $requiredAssets) {
    if ($assetNames -notcontains $requiredAsset) {
        throw "GitHub release '$tag' is missing Zed server asset '$requiredAsset'."
    }
}

Write-Host "Zed release assets verified: $Repository $tag"
