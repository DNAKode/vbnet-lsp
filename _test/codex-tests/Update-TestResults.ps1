param(
    [string]$ResultsPath = '_test\codex-tests\INDEPENDENT_TEST_RESULTS.md',
    [string]$ProtocolLogPath = '_test\codex-tests\logs\protocol-anomalies.jsonl',
    [string]$TimingLogPath = '_test\codex-tests\logs\timing.jsonl',
    [string]$RunLabel = ''
)

$ErrorActionPreference = 'Stop'

function Read-ProtocolLog {
    param([string]$Path)

    if (-not (Test-Path $Path)) {
        return @()
    }

    $lines = Get-Content -Path $Path | Where-Object { $_.Trim() -ne '' }
    $entries = @()
    foreach ($line in $lines) {
        try {
            $entries += ($line | ConvertFrom-Json)
        } catch {
            $entries += [pscustomobject]@{
                timestamp = (Get-Date).ToString('o')
                harness = 'unknown'
                severity = 'error'
                message = "Malformed protocol log line: $line"
            }
        }
    }

    return $entries
}

function Read-TimingLog {
    param([string]$Path)

    if ([string]::IsNullOrWhiteSpace($Path)) {
        return @()
    }

    if (-not (Test-Path $Path)) {
        return @()
    }

    $lines = Get-Content -Path $Path | Where-Object { $_.Trim() -ne '' }
    $entries = @()
    foreach ($line in $lines) {
        try {
            $entries += ($line | ConvertFrom-Json)
        } catch {
            $entries += [pscustomobject]@{
                timestamp = (Get-Date).ToString('o')
                label = 'unknown'
                name = 'parse_error'
                elapsedMs = 0
            }
        }
    }

    return $entries
}

function Format-AnomalySection {
    param(
        [string]$RunLabel,
        [object[]]$Entries
    )

    $title = "## Protocol anomalies (latest run)"
    $labelLine = if ([string]::IsNullOrWhiteSpace($RunLabel)) { "" } else { "`nRun: $RunLabel" }
    if ($Entries.Count -eq 0) {
        return "$title$labelLine`n`nNone detected.`n"
    }

    $lines = @()
    $lines += "$title$labelLine"
    $lines += ""
    foreach ($entry in $Entries) {
        $severity = if ($entry.severity) { $entry.severity } else { 'warn' }
        $harness = if ($entry.harness) { $entry.harness } else { 'unknown' }
        $message = if ($entry.message) { $entry.message } else { 'No message' }
        $timestamp = if ($entry.timestamp) { $entry.timestamp } else { (Get-Date).ToString('o') }
        $lines += "- [$severity] [$harness] $message ($timestamp)"
    }

    return ($lines -join "`n") + "`n"
}

function Format-TimingSection {
    param(
        [string]$RunLabel,
        [object[]]$Entries
    )

    $title = "## Timing summary (latest run)"
    $labelLine = if ([string]::IsNullOrWhiteSpace($RunLabel)) { "" } else { "`nRun: $RunLabel" }
    if ($Entries.Count -eq 0) {
        return "$title$labelLine`n`nNo timing data recorded.`n"
    }

    $lines = @()
    $lines += "$title$labelLine"
    $lines += ""
    foreach ($entry in $Entries) {
        $name = if ($entry.name) { $entry.name } else { 'unknown' }
        $label = if ($entry.label) { $entry.label } else { 'n/a' }
        $elapsed = if ($null -ne $entry.elapsedMs) { [math]::Round([double]$entry.elapsedMs, 2) } else { 0 }
        $lines += "- [$label] $name ($elapsed ms)"
    }

    return ($lines -join "`n") + "`n"
}

$resultsFullPath = Resolve-Path $ResultsPath
$protocolEntries = Read-ProtocolLog -Path $ProtocolLogPath
$timingEntries = Read-TimingLog -Path $TimingLogPath
$section = Format-AnomalySection -RunLabel $RunLabel -Entries $protocolEntries
$timingSection = Format-TimingSection -RunLabel $RunLabel -Entries $timingEntries

$content = Get-Content -Path $resultsFullPath -Raw
$pattern = [regex]::Escape("## Protocol anomalies (latest run)")

if ($content -match "## Protocol anomalies \(latest run\)[\s\S]*?(?=`n## |\Z)") {
    $content = [regex]::Replace(
        $content,
        "## Protocol anomalies \(latest run\)[\s\S]*?(?=`n## |\Z)",
        $section.TrimEnd()
    )
} else {
    $content = $content.TrimEnd() + "`n`n" + $section.TrimEnd() + "`n"
}

$timingPattern = [regex]::Escape("## Timing summary (latest run)")
if ($content -match "## Timing summary \(latest run\)[\s\S]*?(?=`n## |\Z)") {
    $content = [regex]::Replace(
        $content,
        "## Timing summary \(latest run\)[\s\S]*?(?=`n## |\Z)",
        $timingSection.TrimEnd()
    )
} else {
    $content = $content.TrimEnd() + "`n`n" + $timingSection.TrimEnd() + "`n"
}

Set-Content -Path $resultsFullPath -Value $content -NoNewline
