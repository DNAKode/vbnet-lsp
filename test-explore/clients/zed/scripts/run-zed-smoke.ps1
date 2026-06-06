param(
    [string]$ZedPath = 'zed',
    [string]$WorkspacePath = 'test-explore/clients/zed/fixtures/single-file',
    [string]$UserDataDir = '',
    [int]$TimeoutSeconds = 25,
    [string]$LogsPath = 'test-explore/clients/zed/logs',
    [string[]]$ZedArgs = @(),
    [ValidateSet('Probe', 'RealServer')]
    [string]$Mode = 'Probe',
    [string]$LocalServerPath = '',
    [switch]$SkipExtensionInstallCheck,
    [switch]$UseFixtureSettings,
    [switch]$KeepSmokeWorkspace
)

$ErrorActionPreference = 'Stop'

function Copy-ZedLogFiles {
    param(
        [Parameter(Mandatory = $true)][string]$UserData,
        [Parameter(Mandatory = $true)][string]$Destination
    )

    $copied = @()
    $candidateRoots = @(
        (Join-Path $UserData 'logs'),
        (Join-Path $UserData 'Logs')
    )

    foreach ($candidateRoot in $candidateRoots) {
        if (-not (Test-Path -LiteralPath $candidateRoot -PathType Container)) {
            continue
        }

        foreach ($logFile in Get-ChildItem -LiteralPath $candidateRoot -File -Filter '*.log' -ErrorAction SilentlyContinue) {
            $destinationPath = Join-Path $Destination ("zed-profile-" + $logFile.Name)
            Copy-Item -LiteralPath $logFile.FullName -Destination $destinationPath -Force
            $copied += $destinationPath
        }
    }

    return $copied
}

$repoRoot = [System.IO.Path]::GetFullPath((Join-Path $PSScriptRoot '..\..\..\..'))
$workspace = [System.IO.Path]::GetFullPath((Join-Path $repoRoot $WorkspacePath))
$logs = [System.IO.Path]::GetFullPath((Join-Path $repoRoot $LogsPath))

if (-not (Get-Command $ZedPath -ErrorAction SilentlyContinue)) {
    throw "Zed was not found as '$ZedPath'. Install Zed or pass -ZedPath."
}

if (-not (Test-Path -LiteralPath $workspace -PathType Container)) {
    throw "Workspace path not found: $workspace"
}

if ($Mode -eq 'RealServer' -and $UseFixtureSettings) {
    throw "-Mode RealServer cannot be combined with -UseFixtureSettings because the script must generate settings for the selected local server."
}

if ($Mode -eq 'RealServer' -and $LocalServerPath -eq '') {
    $defaultExe = Join-Path $repoRoot 'src/VbNet.LanguageServer.Vb/bin/Debug/net10.0/VbNet.LanguageServer.exe'
    $defaultDll = Join-Path $repoRoot 'src/VbNet.LanguageServer.Vb/bin/Debug/net10.0/VbNet.LanguageServer.dll'
    if (Test-Path -LiteralPath $defaultExe -PathType Leaf) {
        $LocalServerPath = $defaultExe
    } elseif (Test-Path -LiteralPath $defaultDll -PathType Leaf) {
        $LocalServerPath = $defaultDll
    } else {
        throw "Local VB.NET language server was not found. Build src/VbNet.LanguageServer.Vb/VbNet.LanguageServer.Vb.vbproj -c Debug or pass -LocalServerPath."
    }
}

if ($Mode -eq 'RealServer') {
    if ([System.IO.Path]::IsPathRooted($LocalServerPath)) {
        $LocalServerPath = [System.IO.Path]::GetFullPath($LocalServerPath)
    } else {
        $LocalServerPath = [System.IO.Path]::GetFullPath((Join-Path $repoRoot $LocalServerPath))
    }
    if (-not (Test-Path -LiteralPath $LocalServerPath -PathType Leaf)) {
        throw "Local server path not found: $LocalServerPath"
    }
}

if ($UserDataDir -eq '') {
    $UserDataDir = Join-Path ([System.IO.Path]::GetTempPath()) ("vbnet-zed-profile-" + [guid]::NewGuid().ToString("N"))
}
$userData = [System.IO.Path]::GetFullPath($UserDataDir)

New-Item -ItemType Directory -Path $logs -Force | Out-Null
New-Item -ItemType Directory -Path $userData -Force | Out-Null

$configDir = Join-Path $userData 'config'
New-Item -ItemType Directory -Path $configDir -Force | Out-Null
$profileSettingsPath = Join-Path $configDir 'settings.json'
if (-not (Test-Path -LiteralPath $profileSettingsPath -PathType Leaf)) {
    $profileSettingsJson = @"
{
  "session": {
    "trust_all_worktrees": true
  }
}
"@
    [System.IO.File]::WriteAllText($profileSettingsPath, $profileSettingsJson, [System.Text.UTF8Encoding]::new($false))
} else {
    $profileSettingsText = Get-Content -LiteralPath $profileSettingsPath -Raw
    if ($profileSettingsText -notmatch '"trust_all_worktrees"\s*:\s*true') {
        throw "The selected Zed smoke profile has settings.json but does not enable session.trust_all_worktrees. Enable it for this isolated test profile or use a fresh profile. Settings: $profileSettingsPath"
    }
}

if (-not $SkipExtensionInstallCheck) {
    $extensionIndex = Join-Path $userData 'extensions/index.json'
    if (-not (Test-Path -LiteralPath $extensionIndex -PathType Leaf)) {
        throw "The selected Zed profile does not have an extensions index: $extensionIndex. Start Zed once with --user-data-dir $userData, install the VB.NET dev extension from adapters/zed/vbnet-zed, close Zed, then rerun this script with the same -UserDataDir. Pass -SkipExtensionInstallCheck only when intentionally debugging profile bootstrap."
    }

    $extensionIndexText = Get-Content -LiteralPath $extensionIndex -Raw
    if (-not $extensionIndexText.Contains('"vbnet"')) {
        throw "The selected Zed profile does not list the VB.NET extension in $extensionIndex. Install the VB.NET dev extension from adapters/zed/vbnet-zed in that profile, close Zed, then rerun this script."
    }
}

$smokeWorkspace = $workspace
$createdSmokeWorkspace = $false
$realServerLog = $null
if (-not $UseFixtureSettings) {
    $smokeWorkspace = Join-Path ([System.IO.Path]::GetTempPath()) ("vbnet-zed-smoke-workspace-" + [guid]::NewGuid().ToString("N"))
    $createdSmokeWorkspace = $true
    Copy-Item -LiteralPath $workspace -Destination $smokeWorkspace -Recurse -Force

    $settingsDir = Join-Path $smokeWorkspace '.zed'
    New-Item -ItemType Directory -Path $settingsDir -Force | Out-Null
    $initializationOptions = [ordered]@{
        semanticTokens = $true
    }
    $sourceSettings = Join-Path $workspace '.zed/settings.json'
    if (Test-Path -LiteralPath $sourceSettings -PathType Leaf) {
        $sourceSettingsJson = Get-Content -LiteralPath $sourceSettings -Raw | ConvertFrom-Json
        $sourceInitializationOptions = $sourceSettingsJson.lsp.'vbnet-ls'.initialization_options
        if ($sourceInitializationOptions) {
            $initializationOptions = $sourceInitializationOptions
        }
    }
    if ($Mode -eq 'RealServer') {
        $initializationOptions.loadProjectsOnStart = $false
    }

    if ($Mode -eq 'Probe') {
        $probeProject = [System.IO.Path]::GetFullPath((Join-Path $repoRoot 'test-explore/clients/zed/probes/lsp-probe/VbNet.Zed.LspProbe.csproj'))
        $binary = [ordered]@{
            path = 'dotnet'
            arguments = @('run', '--project', $probeProject)
            env = [ordered]@{
                VBNET_ZED_TEST_LOG = 'zed-lsp-probe.jsonl'
            }
        }
    } else {
        $realServerLog = Join-Path $smokeWorkspace 'zed-real-server.stderr.log'
        $launcherPath = Join-Path $settingsDir 'vbnet-real-server-launch.ps1'
        $escapedServerPath = $LocalServerPath.Replace("'", "''")
        $escapedRealServerLog = $realServerLog.Replace("'", "''")
        if ([System.IO.Path]::GetExtension($LocalServerPath).Equals('.dll', [System.StringComparison]::OrdinalIgnoreCase)) {
            @"
`$ErrorActionPreference = 'Stop'
& dotnet '$escapedServerPath' @args 2>> '$escapedRealServerLog'
exit `$LASTEXITCODE
"@ | Set-Content -LiteralPath $launcherPath -Encoding utf8
        } else {
            @"
`$ErrorActionPreference = 'Stop'
& '$escapedServerPath' @args 2>> '$escapedRealServerLog'
exit `$LASTEXITCODE
"@ | Set-Content -LiteralPath $launcherPath -Encoding utf8
        }

        $binary = [ordered]@{
            path = 'powershell'
            arguments = @('-NoProfile', '-ExecutionPolicy', 'Bypass', '-File', $launcherPath, '--stdio', '--logLevel', 'Debug')
            env = [ordered]@{
                VBNET_LS_TRACE_TRANSPORT = '1'
            }
        }
    }

    $generatedSettings = [ordered]@{
        languages = [ordered]@{
            'VB.NET' = [ordered]@{
                language_servers = @('vbnet-ls')
            }
        }
        lsp = [ordered]@{
            'vbnet-ls' = [ordered]@{
                binary = $binary
                initialization_options = $initializationOptions
            }
        }
    }
    $generatedSettingsJson = $generatedSettings | ConvertTo-Json -Depth 16
    [System.IO.File]::WriteAllText((Join-Path $settingsDir 'settings.json'), $generatedSettingsJson, [System.Text.UTF8Encoding]::new($false))
}

$probeLog = Join-Path $smokeWorkspace 'zed-lsp-probe.jsonl'
if ($Mode -eq 'Probe') {
    Remove-Item -LiteralPath $probeLog -Force -ErrorAction SilentlyContinue
}

$stdout = Join-Path $logs 'zed-smoke.stdout.log'
$stderr = Join-Path $logs 'zed-smoke.stderr.log'

$runningZed = Get-Process -Name Zed -ErrorAction SilentlyContinue
if ($runningZed) {
    $processList = ($runningZed | ForEach-Object { "$($_.Id) $($_.Path)" }) -join '; '
    throw "Zed is already running, so this smoke test cannot start an isolated --user-data-dir profile. Close existing Zed processes and rerun. Running processes: $processList"
}

$launchTargets = @($smokeWorkspace)
$firstVbFile = Get-ChildItem -LiteralPath $smokeWorkspace -Filter '*.vb' -File -Recurse -ErrorAction SilentlyContinue |
    Select-Object -First 1
if ($firstVbFile) {
    $launchTargets += $firstVbFile.FullName
}

$process = Start-Process -FilePath $ZedPath `
    -ArgumentList (@('--foreground', '--user-data-dir', $userData) + $ZedArgs + $launchTargets) `
    -WorkingDirectory $smokeWorkspace `
    -RedirectStandardOutput $stdout `
    -RedirectStandardError $stderr `
    -PassThru `
    -WindowStyle Hidden

try {
    $deadline = [DateTime]::UtcNow.AddSeconds($TimeoutSeconds)
    while ([DateTime]::UtcNow -lt $deadline) {
        if ($Mode -eq 'Probe') {
            if ((Test-Path -LiteralPath $probeLog) -and
                (Select-String -LiteralPath $probeLog -Pattern '"method":"textDocument/didOpen"' -Quiet)) {
                break
            }
        } elseif ($Mode -eq 'RealServer') {
            if ($realServerLog -and (Test-Path -LiteralPath $realServerLog -PathType Leaf)) {
                Start-Sleep -Seconds 2
                break
            }
        }
        Start-Sleep -Milliseconds 500
    }
} finally {
    if (-not $process.HasExited) {
        $process.Kill()
        $process.WaitForExit()
    }
}

$stdoutText = [string]$(if (Test-Path -LiteralPath $stdout) { Get-Content -LiteralPath $stdout -Raw } else { '' })
$stderrText = [string]$(if (Test-Path -LiteralPath $stderr) { Get-Content -LiteralPath $stderr -Raw } else { '' })
$copiedZedLogs = Copy-ZedLogFiles -UserData $userData -Destination $logs
$zedLogText = ''
foreach ($zedLog in $copiedZedLogs) {
    if (Test-Path -LiteralPath $zedLog -PathType Leaf) {
        $zedLogText += [Environment]::NewLine
        $zedLogText += [string](Get-Content -LiteralPath $zedLog -Raw)
    }
}
if (($stdoutText -like '*zed is already running*') -or ($stderrText -like '*zed is already running*')) {
    throw "Zed reported that another instance is already running, so the temporary profile was not used. Close existing Zed processes and rerun. Logs: $logs; user data: $userData"
}

$zedLogWorkspace = $smokeWorkspace.Replace('\', '\\')
if (($zedLogText -like "*Worktree `"$smokeWorkspace`" is not trusted*") -or
    ($zedLogText -like "*Worktree `"$zedLogWorkspace`" is not trusted*")) {
    throw "Zed did not trust the smoke workspace, so it did not start vbnet-ls. Open the workspace in Zed with the selected profile, trust it, close Zed, then rerun. Workspace: $smokeWorkspace; Logs: $logs; user data: $userData"
}

foreach ($failurePattern in @(
    'failed to start language server',
    'language server.*exited',
    'extension panic',
    'WebAssembly.*failed',
    'Could not find VB.NET language server',
    'Unhandled exception',
    'panic'
)) {
    if (($stdoutText -match $failurePattern) -or ($stderrText -match $failurePattern) -or ($zedLogText -match $failurePattern)) {
        throw "Zed smoke saw startup failure pattern '$failurePattern'. Logs: $logs; user data: $userData; workspace: $smokeWorkspace"
    }
}

if ($Mode -eq 'RealServer') {
    if (-not (Test-Path -LiteralPath $realServerLog -PathType Leaf)) {
        throw "Real server stderr log was not created, so Zed did not start the configured VB.NET language server. Logs: $logs; user data: $userData; workspace: $smokeWorkspace"
    }

    $realServerLogText = [string]$(Get-Content -LiteralPath $realServerLog -Raw)
    $zedStartedRealServer = $zedLogText.Contains('starting language server process') -and
        $zedLogText.Contains('vbnet-real-server-launch.ps1') -and
        $zedLogText.Contains($zedLogWorkspace)
    if (-not $zedStartedRealServer) {
        throw "Zed log does not show the real VB.NET server launcher starting for this workspace. Logs: $logs; user data: $userData; workspace: $smokeWorkspace"
    }

    if (($realServerLogText -match 'Language server crashed') -or
        ($realServerLogText -match 'Unhandled exception') -or
        ($realServerLogText -match 'panic')) {
        throw "Real server stderr log contains a startup failure. Server log: $realServerLog"
    }

    Write-Host "Zed real-server smoke completed launch window. Local server: $LocalServerPath"
    Write-Host "Real server stderr log: $realServerLog"
    Write-Host "Zed stdout/stderr and copied Zed.log files: $logs"
} elseif (-not (Test-Path -LiteralPath $probeLog)) {
    throw "Probe log was not created. Ensure the VB.NET dev extension is installed in the selected Zed profile. Logs: $logs; user data: $userData; workspace: $smokeWorkspace"
} else {

    $probeText = Get-Content -LiteralPath $probeLog -Raw
    foreach ($required in @('"method":"initialize"', '"method":"textDocument/didOpen"')) {
        if (-not $probeText.Contains($required)) {
            throw "Probe log is missing $required. Probe log: $probeLog"
        }
    }

    Write-Host "Zed smoke passed. Probe log: $probeLog"
    Write-Host "Zed stdout/stderr and copied Zed.log files: $logs"
}

if ($createdSmokeWorkspace -and -not $KeepSmokeWorkspace) {
    Remove-Item -LiteralPath $smokeWorkspace -Recurse -Force -ErrorAction SilentlyContinue
} elseif ($createdSmokeWorkspace) {
    Write-Host "Zed smoke workspace retained: $smokeWorkspace"
}
