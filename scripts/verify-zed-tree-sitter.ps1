param(
    [string]$ExtensionPath = 'adapters/zed/vbnet-zed',
    [string]$FixturePath = 'test-explore/clients/zed/fixtures/tree-sitter',
    [string]$GrammarPath = 'tree-sitter-vbnet',
    [string]$TreeSitterCliPackage = 'tree-sitter-cli@0.22.6'
)

$ErrorActionPreference = 'Stop'

$repoRoot = [System.IO.Path]::GetFullPath((Join-Path $PSScriptRoot '..'))
$fullExtensionPath = [System.IO.Path]::GetFullPath((Join-Path $repoRoot $ExtensionPath))
$fixtureRoot = [System.IO.Path]::GetFullPath((Join-Path $repoRoot $FixturePath))
$grammarRoot = [System.IO.Path]::GetFullPath((Join-Path $repoRoot $GrammarPath))

function Invoke-CapturedNative {
    param(
        [Parameter(Mandatory = $true)][string]$Command,
        [Parameter(Mandatory = $true)][string[]]$Arguments
    )

    $previousPreference = $ErrorActionPreference
    $ErrorActionPreference = 'Continue'
    try {
        $output = & $Command @Arguments 2>&1 | Out-String
        return @{
            ExitCode = $LASTEXITCODE
            Output = $output
        }
    } finally {
        $ErrorActionPreference = $previousPreference
    }
}

if (-not (Get-Command npm -ErrorAction SilentlyContinue)) {
    throw "npm is required for Zed Tree-sitter validation."
}

if (-not (Test-Path -LiteralPath $grammarRoot -PathType Container)) {
    throw "Owned Tree-sitter grammar path not found: $grammarRoot"
}

foreach ($requiredGrammarFile in @(
    'package.json',
    'grammar.js',
    'src/grammar.json',
    'src/node-types.json',
    'src/parser.c'
)) {
    $path = Join-Path $grammarRoot $requiredGrammarFile
    if (-not (Test-Path -LiteralPath $path -PathType Leaf)) {
        throw "Owned Tree-sitter grammar is missing generated file: $path"
    }
}

$package = Get-Content -LiteralPath (Join-Path $grammarRoot 'package.json') -Raw | ConvertFrom-Json
if ($package.name -ne 'tree-sitter-vbnet') {
    throw "Owned Tree-sitter grammar package name must be tree-sitter-vbnet, got '$($package.name)'."
}

if (-not (Test-Path -LiteralPath $fixtureRoot)) {
    throw "Tree-sitter fixture path not found: $fixtureRoot"
}

if (Test-Path -LiteralPath $fixtureRoot -PathType Leaf) {
    $fixtures = @(Get-Item -LiteralPath $fixtureRoot)
} else {
    $fixtures = @(Get-ChildItem -LiteralPath $fixtureRoot -Filter '*.vb' -File | Sort-Object Name)
}

if ($fixtures.Count -eq 0) {
    throw "Tree-sitter fixture path contains no .vb files: $fixtureRoot"
}

Push-Location $grammarRoot
try {
    foreach ($fixture in $fixtures) {
        $parse = Invoke-CapturedNative -Command 'npx' -Arguments @('--yes', $TreeSitterCliPackage, 'parse', $fixture.FullName, '--quiet')
        if ($parse.ExitCode -ne 0) {
            throw "tree-sitter parse failed for $($fixture.Name) with exit code $($parse.ExitCode).`n$($parse.Output)"
        }
        if ($parse.Output.Contains('ERROR')) {
            throw "tree-sitter parse reported errors for $($fixture.FullName).`n$($parse.Output)"
        }

        foreach ($query in Get-ChildItem -LiteralPath (Join-Path $fullExtensionPath 'languages/vbnet') -Filter '*.scm') {
            $queryResult = Invoke-CapturedNative -Command 'npx' -Arguments @('--yes', $TreeSitterCliPackage, 'query', $query.FullName, $fixture.FullName, '--quiet')
            if ($queryResult.ExitCode -ne 0) {
                throw "tree-sitter query failed for $($query.Name) on $($fixture.Name) with exit code $($queryResult.ExitCode).`n$($queryResult.Output)"
            }
        }
    }
} finally {
    Pop-Location
}

Write-Host "Zed Tree-sitter validation passed: $($fixtures.Count) fixture(s) using owned grammar at $GrammarPath"
