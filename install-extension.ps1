# VB.NET Language Support - Install Script
# Run this script to build and install the extension for testing

param(
    [switch]$SkipBuild,
    [switch]$UseStdio,
    [string]$TestProject = ""
)

$ErrorActionPreference = "Stop"
$ProjectRoot = $PSScriptRoot

Write-Host "======================================" -ForegroundColor Cyan
Write-Host " VB.NET Language Support - Installer" -ForegroundColor Cyan
Write-Host "======================================" -ForegroundColor Cyan
Write-Host ""

# Step 1: Build Language Server
Write-Host "[1/5] Building Language Server..." -ForegroundColor Yellow
$ServerPath = Join-Path $ProjectRoot "src\VbNet.LanguageServer\bin\Debug\net10.0\VbNet.LanguageServer.dll"

if (-not $SkipBuild) {
    try {
        # Kill any running language server processes first
        Get-Process -Name "VbNet.LanguageServer" -ErrorAction SilentlyContinue | Stop-Process -Force -ErrorAction SilentlyContinue
        Get-Process -Name "dotnet" -ErrorAction SilentlyContinue | Where-Object { $_.MainWindowTitle -like "*VbNet*" } | Stop-Process -Force -ErrorAction SilentlyContinue
        Start-Sleep -Seconds 1

        Push-Location $ProjectRoot
        dotnet build src/VbNet.LanguageServer -c Debug
        Pop-Location
    }
    catch {
        Write-Host "  Warning: Build had issues, checking if DLL exists..." -ForegroundColor Yellow
    }
}

if (Test-Path $ServerPath) {
    Write-Host "  OK: Server DLL found at $ServerPath" -ForegroundColor Green
} else {
    Write-Host "  ERROR: Server DLL not found!" -ForegroundColor Red
    Write-Host "  Please build manually: dotnet build src/VbNet.LanguageServer" -ForegroundColor Red
    exit 1
}

# Step 2: Build Extension
Write-Host ""
Write-Host "[2/5] Building VS Code Extension..." -ForegroundColor Yellow
$ExtensionDir = Join-Path $ProjectRoot "src\extension"

Push-Location $ExtensionDir
if (-not $SkipBuild) {
    npm run compile
    if ($LASTEXITCODE -ne 0) {
        Write-Host "  ERROR: TypeScript compilation failed!" -ForegroundColor Red
        Pop-Location
        exit 1
    }
}
Write-Host "  OK: Extension compiled" -ForegroundColor Green
Pop-Location

# Step 3: Create VS Code settings for the workspace
Write-Host ""
Write-Host "[3/5] Configuring VS Code settings..." -ForegroundColor Yellow
$VsCodeDir = Join-Path $ProjectRoot ".vscode"
if (-not (Test-Path $VsCodeDir)) {
    New-Item -ItemType Directory -Path $VsCodeDir | Out-Null
}

$TransportType = if ($UseStdio) { "stdio" } else { "auto" }
$SettingsPath = Join-Path $VsCodeDir "settings.json"
$Settings = @{
    "vbnet.server.path" = $ServerPath.Replace("\", "/")
    "vbnet.server.transportType" = $TransportType
    "vbnet.trace.server" = "verbose"
}

# Merge with existing settings if present
if (Test-Path $SettingsPath) {
    try {
        $ExistingSettings = Get-Content $SettingsPath -Raw | ConvertFrom-Json -AsHashtable
        foreach ($key in $Settings.Keys) {
            $ExistingSettings[$key] = $Settings[$key]
        }
        $Settings = $ExistingSettings
    }
    catch {
        Write-Host "  Warning: Could not parse existing settings, creating new file" -ForegroundColor Yellow
    }
}

$Settings | ConvertTo-Json -Depth 10 | Set-Content $SettingsPath
Write-Host "  OK: Settings written to .vscode/settings.json" -ForegroundColor Green
Write-Host "      Server path: $ServerPath" -ForegroundColor Gray
Write-Host "      Transport: $TransportType" -ForegroundColor Gray

# Step 4: Create launch configuration for debugging extension
Write-Host ""
Write-Host "[4/5] Creating launch configuration..." -ForegroundColor Yellow
$LaunchPath = Join-Path $VsCodeDir "launch.json"

# Write launch.json with proper escaping for VS Code variables
$LaunchJson = @'
{
    "version": "0.2.0",
    "configurations": [
        {
            "name": "Run VB.NET Extension",
            "type": "extensionHost",
            "request": "launch",
            "args": [
                "--extensionDevelopmentPath=${workspaceFolder}/src/extension"
            ],
            "outFiles": [
                "${workspaceFolder}/src/extension/out/**/*.js"
            ],
            "preLaunchTask": "npm: compile - src/extension"
        },
        {
            "name": "Run VB.NET Extension (with test project)",
            "type": "extensionHost",
            "request": "launch",
            "args": [
                "--extensionDevelopmentPath=${workspaceFolder}/src/extension",
                "${workspaceFolder}/test/TestProjects/SmallProject"
            ],
            "outFiles": [
                "${workspaceFolder}/src/extension/out/**/*.js"
            ],
            "preLaunchTask": "npm: compile - src/extension"
        }
    ]
}
'@
$LaunchJson | Set-Content $LaunchPath -Encoding UTF8
Write-Host "  OK: Launch configuration created" -ForegroundColor Green

# Step 5: Create tasks configuration
$TasksPath = Join-Path $VsCodeDir "tasks.json"
$TasksJson = @'
{
    "version": "2.0.0",
    "tasks": [
        {
            "type": "npm",
            "script": "compile",
            "path": "src/extension",
            "problemMatcher": ["$tsc"],
            "label": "npm: compile - src/extension",
            "group": "build"
        },
        {
            "type": "npm",
            "script": "watch",
            "path": "src/extension",
            "problemMatcher": ["$tsc-watch"],
            "label": "npm: watch - src/extension",
            "isBackground": true
        },
        {
            "label": "Build Language Server",
            "type": "shell",
            "command": "dotnet",
            "args": ["build", "src/VbNet.LanguageServer", "-c", "Debug"],
            "group": "build",
            "problemMatcher": "$msCompile"
        },
        {
            "label": "Build All",
            "dependsOn": ["Build Language Server", "npm: compile - src/extension"],
            "group": {
                "kind": "build",
                "isDefault": true
            },
            "problemMatcher": []
        }
    ]
}
'@
$TasksJson | Set-Content $TasksPath -Encoding UTF8

Write-Host ""
Write-Host "[5/5] Setup Complete!" -ForegroundColor Yellow
Write-Host ""
Write-Host "======================================" -ForegroundColor Cyan
Write-Host " Next Steps" -ForegroundColor Cyan
Write-Host "======================================" -ForegroundColor Cyan
Write-Host ""
Write-Host "Option A: Debug the extension (recommended for testing)" -ForegroundColor White
Write-Host "  1. Open this folder in VS Code: code ." -ForegroundColor Gray
Write-Host "  2. Press F5 to launch Extension Development Host" -ForegroundColor Gray
Write-Host "  3. In the new window, open a VB.NET project" -ForegroundColor Gray
Write-Host ""
Write-Host "Option B: Test with a sample project" -ForegroundColor White

$SmallProjectPath = Join-Path $ProjectRoot "test\TestProjects\SmallProject"
if (Test-Path $SmallProjectPath) {
    Write-Host "  Sample project available at:" -ForegroundColor Gray
    Write-Host "  $SmallProjectPath" -ForegroundColor Cyan
}

Write-Host ""
Write-Host "Troubleshooting:" -ForegroundColor White
Write-Host "  - Check Output panel > 'VB.NET' for server logs" -ForegroundColor Gray
Write-Host "  - Check Output panel > 'VB.NET LSP Trace' for LSP messages" -ForegroundColor Gray
Write-Host "  - Settings are in .vscode/settings.json" -ForegroundColor Gray
Write-Host ""

# If test project specified, offer to open it
if ($TestProject -ne "" -and (Test-Path $TestProject)) {
    Write-Host "Opening test project: $TestProject" -ForegroundColor Cyan
    code $ProjectRoot --goto $TestProject
}
