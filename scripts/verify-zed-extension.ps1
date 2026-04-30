param(
    [string]$ExtensionPath = 'adapters/zed/vbnet-zed',
    [string]$ExpectedVersion = ''
)

$ErrorActionPreference = 'Stop'

function Assert-File {
    param([Parameter(Mandatory = $true)][string]$Path)

    if (-not (Test-Path -LiteralPath $Path -PathType Leaf)) {
        throw "Required file is missing: $Path"
    }
}

function Assert-Directory {
    param([Parameter(Mandatory = $true)][string]$Path)

    if (-not (Test-Path -LiteralPath $Path -PathType Container)) {
        throw "Required directory is missing: $Path"
    }
}

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
$fullExtensionPath = [System.IO.Path]::GetFullPath((Join-Path $repoRoot $ExtensionPath))

Assert-Directory $fullExtensionPath

$requiredFiles = @(
    'extension.toml',
    'Cargo.toml',
    'src/lib.rs',
    'src/server.rs',
    'src/debug.rs',
    'src/platform.rs',
    'src/workspace.rs',
    'README.md',
    'LICENSE',
    'MIRRORING.md',
    'debug_adapter_schemas/netcoredbg.json',
    'languages/vbnet/config.toml',
    'languages/vbnet/brackets.scm',
    'languages/vbnet/highlights.scm',
    'languages/vbnet/outline.scm',
    'languages/vbnet/folds.scm',
    'languages/vbnet/indents.scm',
    'languages/vbnet/overrides.scm',
    'languages/vbnet/textobjects.scm',
    'languages/vbnet/semantic_token_rules.json'
)

foreach ($relativePath in $requiredFiles) {
    Assert-File (Join-Path $fullExtensionPath $relativePath)
}

$manifestPath = Join-Path $fullExtensionPath 'extension.toml'
$manifest = Get-Content -LiteralPath $manifestPath -Raw
$id = Get-ManifestValue -Manifest $manifest -Key 'id'
$name = Get-ManifestValue -Manifest $manifest -Key 'name'
$version = Get-ManifestValue -Manifest $manifest -Key 'version'

if ($id -ne 'vbnet') {
    throw "Expected extension id 'vbnet', got '$id'."
}

if ($name -ne 'VB.NET') {
    throw "Expected extension name 'VB.NET', got '$name'."
}

if ($ExpectedVersion -ne '' -and $version -ne $ExpectedVersion) {
    throw "Expected Zed extension version '$ExpectedVersion', got '$version'."
}

foreach ($requiredText in @(
    '[grammars.vbnet]',
    '[language_servers.vbnet-ls]',
    '[debug_adapters.netcoredbg]',
    '[debug_locators.vbnet]'
)) {
    if (-not $manifest.Contains($requiredText)) {
        throw "extension.toml is missing required section $requiredText."
    }
}

$languageConfig = Get-Content -LiteralPath (Join-Path $fullExtensionPath 'languages/vbnet/config.toml') -Raw
foreach ($requiredText in @('name = "VB.NET"', 'grammar = "vbnet"', 'path_suffixes = ["vb"]')) {
    if (-not $languageConfig.Contains($requiredText)) {
        throw "languages/vbnet/config.toml is missing '$requiredText'."
    }
}

Push-Location $fullExtensionPath
try {
    cargo check --target wasm32-wasip1
    if ($LASTEXITCODE -ne 0) {
        throw "cargo check failed with exit code $LASTEXITCODE."
    }

    cargo test
    if ($LASTEXITCODE -ne 0) {
        throw "cargo test failed with exit code $LASTEXITCODE."
    }
} finally {
    Pop-Location
}

Write-Host "Zed extension verification passed: $fullExtensionPath"
