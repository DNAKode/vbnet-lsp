# VB.NET Language Support - Install VSIX
# Installs the extension globally so it works in any VS Code window

$ErrorActionPreference = "Stop"
$ProjectRoot = $PSScriptRoot
$ServerPath = "C:/Work/vbnet-lsp/src/VbNet.LanguageServer/bin/Debug/net10.0/VbNet.LanguageServer.dll"
$VsixPath = Join-Path $ProjectRoot "src\extension\vbnet-language-support.vsix"

Write-Host "Installing VB.NET Language Support Extension..." -ForegroundColor Cyan
Write-Host ""

# Check VSIX exists
if (-not (Test-Path $VsixPath)) {
    Write-Host "VSIX not found. Building..." -ForegroundColor Yellow
    Push-Location (Join-Path $ProjectRoot "src\extension")
    npm run package:vsix
    Pop-Location
}

# Install the extension
Write-Host "Installing extension to VS Code..." -ForegroundColor Yellow
code --install-extension $VsixPath --force

if ($LASTEXITCODE -eq 0) {
    Write-Host ""
    Write-Host "Extension installed successfully!" -ForegroundColor Green
    Write-Host ""
    Write-Host "IMPORTANT: Configure the server path in VS Code settings:" -ForegroundColor Yellow
    Write-Host ""
    Write-Host "  Add to your VS Code settings.json:" -ForegroundColor White
    Write-Host "  {" -ForegroundColor Gray
    Write-Host "    `"vbnet.server.path`": `"$ServerPath`"," -ForegroundColor Cyan
    Write-Host "    `"vbnet.trace.server`": `"verbose`"" -ForegroundColor Gray
    Write-Host "  }" -ForegroundColor Gray
    Write-Host ""
    Write-Host "Or run this command to set it globally:" -ForegroundColor White
    Write-Host ""
    Write-Host "  code --user-data-dir . --install-extension ... (see below)" -ForegroundColor Gray
    Write-Host ""

    # Copy setting to clipboard
    $SettingJson = "{`"vbnet.server.path`": `"$ServerPath`", `"vbnet.trace.server`": `"verbose`"}"
    $SettingJson | Set-Clipboard
    Write-Host "Settings JSON copied to clipboard!" -ForegroundColor Green
    Write-Host ""
    Write-Host "Paste into your VS Code settings.json (Ctrl+Shift+P > 'Preferences: Open User Settings (JSON)')" -ForegroundColor White
} else {
    Write-Host "Installation failed!" -ForegroundColor Red
    exit 1
}
