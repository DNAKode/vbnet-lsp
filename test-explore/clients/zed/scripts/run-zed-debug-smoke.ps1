param(
    [string]$ZedPath = 'zed',
    [string]$UserDataDir = '',
    [string]$WorkspacePath = 'test-explore/clients/zed/fixtures/debug-console',
    [string]$NetcoredbgPath = '',
    [int]$TimeoutSeconds = 90,
    [string]$LogsPath = 'test-explore/clients/zed/logs',
    [switch]$Automate,
    [switch]$SkipExtensionInstallCheck,
    [switch]$SkipNetcoredbgCheck
)

$ErrorActionPreference = 'Stop'

function Copy-ZedLogFiles {
    param(
        [Parameter(Mandatory = $true)][string]$UserData,
        [Parameter(Mandatory = $true)][string]$Destination
    )

    $copied = @()
    foreach ($candidateRoot in @((Join-Path $UserData 'logs'), (Join-Path $UserData 'Logs'))) {
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

function Enable-ZedSmokeProfileTrust {
    param([Parameter(Mandatory = $true)][string]$UserData)

    $configDir = Join-Path $UserData 'config'
    New-Item -ItemType Directory -Path $configDir -Force | Out-Null
    $settingsPath = Join-Path $configDir 'settings.json'
    if (-not (Test-Path -LiteralPath $settingsPath -PathType Leaf)) {
        $settingsJson = @"
{
  "session": {
    "trust_all_worktrees": true
  }
}
"@
        [System.IO.File]::WriteAllText($settingsPath, $settingsJson, [System.Text.UTF8Encoding]::new($false))
        return
    }

    $settingsText = Get-Content -LiteralPath $settingsPath -Raw
    if ($settingsText -notmatch '"trust_all_worktrees"\s*:\s*true') {
        throw "The selected Zed smoke profile has settings.json but does not enable session.trust_all_worktrees. Enable it for this isolated test profile or use a fresh profile. Settings: $settingsPath"
    }
}

function Enable-ZedSmokeAutomationKeymap {
    param([Parameter(Mandatory = $true)][string]$UserData)

    $configDir = Join-Path $UserData 'config'
    New-Item -ItemType Directory -Path $configDir -Force | Out-Null
    $keymapPath = Join-Path $configDir 'keymap.json'
    $keymapJson = @"
[
  {
    "context": "Workspace",
    "bindings": {
      "ctrl-alt-shift-d": "debugger::Start"
    }
  }
]
"@
    [System.IO.File]::WriteAllText($keymapPath, $keymapJson, [System.Text.UTF8Encoding]::new($false))
}

function Start-ZedDebugUiAutomation {
    param(
        [Parameter(Mandatory = $true)][System.Diagnostics.Process]$Process,
        [Parameter(Mandatory = $true)][string]$MarkerPath,
        [Parameter(Mandatory = $true)][int]$TimeoutSeconds
    )

    if (-not $IsWindows -and $PSVersionTable.PSEdition -eq 'Core') {
        throw "Automated Zed debug smoke currently supports Windows UI automation only."
    }

    Add-Type -AssemblyName System.Windows.Forms
    Add-Type @"
using System;
using System.Runtime.InteropServices;
public static class Win32ZedSmoke {
    [StructLayout(LayoutKind.Sequential)]
    public struct RECT {
        public int Left;
        public int Top;
        public int Right;
        public int Bottom;
    }

    [StructLayout(LayoutKind.Sequential)]
    public struct INPUT {
        public uint type;
        public INPUTUNION union;
    }

    [StructLayout(LayoutKind.Explicit)]
    public struct INPUTUNION {
        [FieldOffset(0)]
        public KEYBDINPUT keyboard;
    }

    [StructLayout(LayoutKind.Sequential)]
    public struct KEYBDINPUT {
        public ushort wVk;
        public ushort wScan;
        public uint dwFlags;
        public uint time;
        public IntPtr dwExtraInfo;
    }

    [DllImport("user32.dll")]
    public static extern bool SetForegroundWindow(IntPtr hWnd);
    [DllImport("user32.dll")]
    public static extern bool ShowWindow(IntPtr hWnd, int nCmdShow);
    [DllImport("user32.dll")]
    public static extern bool GetWindowRect(IntPtr hWnd, out RECT rect);
    [DllImport("user32.dll")]
    public static extern bool SetCursorPos(int x, int y);
    [DllImport("user32.dll")]
    public static extern void mouse_event(uint dwFlags, uint dx, uint dy, uint dwData, UIntPtr dwExtraInfo);
    [DllImport("user32.dll", SetLastError = true)]
    public static extern uint SendInput(uint nInputs, INPUT[] pInputs, int cbSize);

    const uint INPUT_KEYBOARD = 1;
    const uint KEYEVENTF_KEYUP = 0x0002;
    const uint KEYEVENTF_UNICODE = 0x0004;
    const uint MOUSEEVENTF_LEFTDOWN = 0x0002;
    const uint MOUSEEVENTF_LEFTUP = 0x0004;

    public static void ClickWindowCenter(IntPtr hWnd) {
        RECT rect;
        if (!GetWindowRect(hWnd, out rect)) {
            return;
        }
        int x = rect.Left + ((rect.Right - rect.Left) / 2);
        int y = rect.Top + ((rect.Bottom - rect.Top) / 2);
        SetCursorPos(x, y);
        mouse_event(MOUSEEVENTF_LEFTDOWN, (uint)x, (uint)y, 0, UIntPtr.Zero);
        mouse_event(MOUSEEVENTF_LEFTUP, (uint)x, (uint)y, 0, UIntPtr.Zero);
    }

    public static void PressCtrlShiftP() {
        KeyDown(0x11);
        KeyDown(0x10);
        KeyPress(0x50);
        KeyUp(0x10);
        KeyUp(0x11);
    }

    public static void PressCtrlAltShiftD() {
        KeyDown(0x11);
        KeyDown(0x12);
        KeyDown(0x10);
        KeyPress(0x44);
        KeyUp(0x10);
        KeyUp(0x12);
        KeyUp(0x11);
    }

    public static void PressEnter() {
        KeyPress(0x0D);
    }

    public static void SendText(string text) {
        foreach (char ch in text) {
            INPUT down = KeyboardInput(0, ch, KEYEVENTF_UNICODE);
            INPUT up = KeyboardInput(0, ch, KEYEVENTF_UNICODE | KEYEVENTF_KEYUP);
            SendInput(2, new INPUT[] { down, up }, Marshal.SizeOf(typeof(INPUT)));
        }
    }

    static void KeyPress(ushort vk) {
        KeyDown(vk);
        KeyUp(vk);
    }

    static void KeyDown(ushort vk) {
        INPUT input = KeyboardInput(vk, 0, 0);
        SendInput(1, new INPUT[] { input }, Marshal.SizeOf(typeof(INPUT)));
    }

    static void KeyUp(ushort vk) {
        INPUT input = KeyboardInput(vk, 0, KEYEVENTF_KEYUP);
        SendInput(1, new INPUT[] { input }, Marshal.SizeOf(typeof(INPUT)));
    }

    static INPUT KeyboardInput(ushort vk, ushort scan, uint flags) {
        INPUT input = new INPUT();
        input.type = INPUT_KEYBOARD;
        input.union.keyboard.wVk = vk;
        input.union.keyboard.wScan = scan;
        input.union.keyboard.dwFlags = flags;
        input.union.keyboard.time = 0;
        input.union.keyboard.dwExtraInfo = IntPtr.Zero;
        return input;
    }
}
"@

    $deadline = [DateTime]::UtcNow.AddSeconds($TimeoutSeconds)
    $windowReady = $false
    while ([DateTime]::UtcNow -lt $deadline) {
        $Process.Refresh()
        if ($Process.HasExited) {
            throw "Zed exited before UI automation could start."
        }
        if ($Process.MainWindowHandle -ne [IntPtr]::Zero) {
            [Win32ZedSmoke]::ShowWindow($Process.MainWindowHandle, 9) | Out-Null
            [Win32ZedSmoke]::SetForegroundWindow($Process.MainWindowHandle) | Out-Null
            $windowReady = $true
            break
        }
        Start-Sleep -Milliseconds 250
    }

    if (-not $windowReady) {
        throw "Timed out waiting for the Zed window before UI automation."
    }

    Start-Sleep -Seconds 5
    $shell = New-Object -ComObject WScript.Shell

    function Invoke-ZedDebugStartKeys {
        [Win32ZedSmoke]::ShowWindow($Process.MainWindowHandle, 9) | Out-Null
        [Win32ZedSmoke]::SetForegroundWindow($Process.MainWindowHandle) | Out-Null
        [Win32ZedSmoke]::ClickWindowCenter($Process.MainWindowHandle)
        $shell.AppActivate($Process.Id) | Out-Null
        Start-Sleep -Milliseconds 800
        [Win32ZedSmoke]::PressCtrlAltShiftD()
        $shell.SendKeys('^%+d')
        Start-Sleep -Seconds 2
        [Win32ZedSmoke]::SendText('Debug VB.NET console')
        $shell.SendKeys('Debug VB.NET console')
        Start-Sleep -Milliseconds 700
        [Win32ZedSmoke]::PressEnter()
        $shell.SendKeys('{ENTER}')
    }

    Invoke-ZedDebugStartKeys
    $nextRetry = [DateTime]::UtcNow.AddSeconds(12)

    while ([DateTime]::UtcNow -lt $deadline) {
        if ((Test-Path -LiteralPath $MarkerPath -PathType Leaf) -and
            ((Get-Content -LiteralPath $MarkerPath -Raw) -match 'from-zed')) {
            return
        }

        if ($Process.HasExited) {
            throw "Zed exited before the debug fixture marker was written."
        }

        if ([DateTime]::UtcNow -ge $nextRetry) {
            Invoke-ZedDebugStartKeys
            $nextRetry = [DateTime]::UtcNow.AddSeconds(12)
        }

        Start-Sleep -Milliseconds 500
    }

    throw "Timed out waiting for automated Zed debug launch marker: $MarkerPath"
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

dotnet build (Join-Path $workspace 'DebugConsole.vbproj') -c Debug
if ($LASTEXITCODE -ne 0) {
    throw "Debug fixture build failed with exit code $LASTEXITCODE."
}

$debugProgram = Join-Path $workspace 'bin/Debug/net10.0/DebugConsole.dll'
if (-not (Test-Path -LiteralPath $debugProgram -PathType Leaf)) {
    throw "Debug fixture did not produce $debugProgram."
}

if ($NetcoredbgPath -ne '') {
    if ([System.IO.Path]::IsPathRooted($NetcoredbgPath)) {
        $NetcoredbgPath = [System.IO.Path]::GetFullPath($NetcoredbgPath)
    } else {
        $NetcoredbgPath = [System.IO.Path]::GetFullPath((Join-Path $repoRoot $NetcoredbgPath))
    }

    if (-not (Test-Path -LiteralPath $NetcoredbgPath -PathType Leaf)) {
        throw "netcoredbg path not found: $NetcoredbgPath"
    }
} elseif (-not $SkipNetcoredbgCheck -and -not (Get-Command 'netcoredbg' -ErrorAction SilentlyContinue)) {
    Write-Host "netcoredbg was not found on PATH. The Zed extension will use its repo-local or curated downloaded netcoredbg fallback unless you pass -NetcoredbgPath."
}

if ($UserDataDir -eq '') {
    $UserDataDir = Join-Path ([System.IO.Path]::GetTempPath()) 'vbnet-zed-profile'
}
$userData = [System.IO.Path]::GetFullPath($UserDataDir)
New-Item -ItemType Directory -Path $userData -Force | Out-Null
New-Item -ItemType Directory -Path $logs -Force | Out-Null
Enable-ZedSmokeProfileTrust -UserData $userData
if ($Automate) {
    Enable-ZedSmokeAutomationKeymap -UserData $userData
}

if (-not $SkipExtensionInstallCheck) {
    $extensionIndex = Join-Path $userData 'extensions/index.json'
    if (-not (Test-Path -LiteralPath $extensionIndex -PathType Leaf)) {
        throw "The selected Zed profile does not have an extensions index: $extensionIndex. Start Zed once with --user-data-dir $userData, install the VB.NET dev extension from adapters/zed/vbnet-zed, close Zed, then rerun this script."
    }

    $extensionIndexText = Get-Content -LiteralPath $extensionIndex -Raw
    if (-not $extensionIndexText.Contains('"vbnet"')) {
        throw "The selected Zed profile does not list the VB.NET extension in $extensionIndex."
    }
}

$runningZed = Get-Process -Name Zed -ErrorAction SilentlyContinue
if ($runningZed) {
    $processList = ($runningZed | ForEach-Object { "$($_.Id) $($_.Path)" }) -join '; '
    throw "Zed is already running, so this debug smoke cannot start an isolated --user-data-dir profile. Close existing Zed processes and rerun. Running processes: $processList"
}

Write-Host "Launching Zed debug fixture: $workspace"
if ($NetcoredbgPath -ne '') {
    Write-Host "  netcoredbg path checked for this run: $NetcoredbgPath"
    Write-Host "  Ensure Zed's netcoredbg adapter path is configured to that binary before starting the debug session."
} else {
    Write-Host "  netcoredbg path: extension default resolution (repo-local, curated download, then PATH)."
}

$markerPath = Join-Path $workspace 'zed-debug-fixture.log'
Remove-Item -LiteralPath $markerPath -Force -ErrorAction SilentlyContinue

$stdout = Join-Path $logs 'zed-debug-smoke.stdout.log'
$stderr = Join-Path $logs 'zed-debug-smoke.stderr.log'
$profileLogs = Join-Path $userData 'logs'
if (Test-Path -LiteralPath $profileLogs) {
    Remove-Item -Path (Join-Path $profileLogs '*.log') -Force -ErrorAction SilentlyContinue
}

$process = Start-Process -FilePath $ZedPath `
    -ArgumentList @('--foreground', '--user-data-dir', $userData, $workspace) `
    -WorkingDirectory $workspace `
    -RedirectStandardOutput $stdout `
    -RedirectStandardError $stderr `
    -PassThru

$automationError = $null
try {
    if ($Automate) {
        try {
            Start-ZedDebugUiAutomation -Process $process -MarkerPath $markerPath -TimeoutSeconds $TimeoutSeconds
        } catch {
            $automationError = $_
        }
    } else {
        Write-Host "Manual debug smoke steps:"
        Write-Host "  1. Run 'debugger: start'."
        Write-Host "  2. Select 'Debug VB.NET console'."
        Write-Host "  3. Verify netcoredbg starts, the build task runs, and debug console output includes 'from-zed'."
        Write-Host "  4. Run task 'dotnet run DebugConsole' and verify output includes 'from-zed-task'."
        Write-Host "  5. For attach, update processId in .zed/debug.json to a running fixture process and select 'Attach VB.NET console'."
        return
    }
} finally {
    if ($Automate -and -not $process.HasExited) {
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

foreach ($failurePattern in @(
    'failed to start debug adapter',
    'debug adapter.*exited',
    'extension panic',
    'WebAssembly.*failed',
    'Could not find netcoredbg',
    'Unhandled exception',
    'panic'
)) {
    if (($stdoutText -match $failurePattern) -or ($stderrText -match $failurePattern) -or ($zedLogText -match $failurePattern)) {
        throw "Zed debug smoke saw startup failure pattern '$failurePattern'. Logs: $logs; user data: $userData; workspace: $workspace"
    }
}

if ($automationError) {
    throw "$($automationError.Exception.Message) Logs: $logs; user data: $userData; workspace: $workspace"
}

Write-Host "Zed automated debug smoke passed. Marker: $markerPath"
Write-Host "Zed stdout/stderr and copied Zed.log files: $logs"
