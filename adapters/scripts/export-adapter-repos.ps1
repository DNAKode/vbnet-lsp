param(
    [string]$NvimRepoPath = '',
    [string]$EmacsRepoPath = '',
    [string]$ZedRepoPath = '',
    [string]$TreeSitterRepoPath = '',
    [switch]$Clean,
    [switch]$DryRun
)

$ErrorActionPreference = 'Stop'

function Get-FullPath {
    param([Parameter(Mandatory = $true)][string]$Path)
    return [System.IO.Path]::GetFullPath($Path)
}

function Remove-DirectoryContents {
    param(
        [Parameter(Mandatory = $true)][string]$Path,
        [switch]$DryRun
    )

    if (-not (Test-Path $Path)) {
        return
    }

    Get-ChildItem -LiteralPath $Path -Force |
        Where-Object { $_.Name -ne '.git' } |
        ForEach-Object {
            if ($DryRun) {
                Write-Host "DRY-RUN remove: $($_.FullName)"
            } else {
                Remove-Item -LiteralPath $_.FullName -Recurse -Force
            }
        }
}

function Ensure-Directory {
    param(
        [Parameter(Mandatory = $true)][string]$Path,
        [switch]$DryRun
    )

    if (Test-Path $Path) {
        return
    }

    if ($DryRun) {
        Write-Host "DRY-RUN mkdir: $Path"
    } else {
        New-Item -ItemType Directory -Path $Path -Force | Out-Null
    }
}

function Copy-DirectoryContents {
    param(
        [Parameter(Mandatory = $true)][string]$Source,
        [Parameter(Mandatory = $true)][string]$Destination,
        [switch]$DryRun
    )

    $excludedNames = @(
        '.git',
        'target',
        'node_modules',
        '.zed',
        'work',
        'grammars',
        'extension.wasm',
        'parser.obj'
    )

    Get-ChildItem -LiteralPath $Source -Force | ForEach-Object {
        if ($excludedNames -contains $_.Name) {
            Write-Host "  Skipping generated/cache path: $($_.FullName)"
            return
        }

        $target = Join-Path $Destination $_.Name
        if ($DryRun) {
            Write-Host "DRY-RUN copy: $($_.FullName) -> $target"
        } else {
            Copy-Item -LiteralPath $_.FullName -Destination $target -Recurse -Force
        }
    }
}

function Sync-Adapter {
    param(
        [Parameter(Mandatory = $true)][string]$Name,
        [Parameter(Mandatory = $true)][string]$Source,
        [Parameter(Mandatory = $true)][string]$Destination,
        [switch]$Clean,
        [switch]$DryRun
    )

    if (-not (Test-Path $Source)) {
        throw "Source path not found for '$Name': $Source"
    }

    Write-Host "Syncing '$Name'"
    Write-Host "  Source:      $Source"
    Write-Host "  Destination: $Destination"

    Ensure-Directory -Path $Destination -DryRun:$DryRun

    if ($Clean) {
        Remove-DirectoryContents -Path $Destination -DryRun:$DryRun
    } else {
        Write-Host "  Clean mode disabled: existing files may remain in destination."
    }

    Copy-DirectoryContents -Source $Source -Destination $Destination -DryRun:$DryRun
}

$repoRoot = Get-FullPath (Join-Path $PSScriptRoot '..\..')
$tasks = @()

if ($NvimRepoPath -ne '') {
    $tasks += @{
        Name = 'vbnet-lsp.nvim'
        Source = Get-FullPath (Join-Path $repoRoot 'adapters\nvim\vbnet-lsp.nvim')
        Destination = Get-FullPath $NvimRepoPath
    }
}

if ($EmacsRepoPath -ne '') {
    $tasks += @{
        Name = 'vbnet-eglot'
        Source = Get-FullPath (Join-Path $repoRoot 'adapters\emacs\vbnet-eglot')
        Destination = Get-FullPath $EmacsRepoPath
    }
}

if ($ZedRepoPath -ne '') {
    $tasks += @{
        Name = 'vbnet-zed'
        Source = Get-FullPath (Join-Path $repoRoot 'adapters\zed\vbnet-zed')
        Destination = Get-FullPath $ZedRepoPath
    }
}

if ($TreeSitterRepoPath -ne '') {
    $tasks += @{
        Name = 'tree-sitter-vbnet'
        Source = Get-FullPath (Join-Path $repoRoot 'tree-sitter-vbnet')
        Destination = Get-FullPath $TreeSitterRepoPath
    }
}

if ($tasks.Count -eq 0) {
    throw "No destination provided. Set -NvimRepoPath, -EmacsRepoPath, -ZedRepoPath, and/or -TreeSitterRepoPath."
}

foreach ($task in $tasks) {
    Sync-Adapter `
        -Name $task.Name `
        -Source $task.Source `
        -Destination $task.Destination `
        -Clean:$Clean `
        -DryRun:$DryRun
}

Write-Host "Adapter sync complete."
