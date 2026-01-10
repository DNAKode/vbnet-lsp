param(
    [string]$ResultsPath = '_test\codex-tests\INDEPENDENT_TEST_RESULTS.md',
    [string]$ProtocolLogPath = '_test\codex-tests\logs\protocol-anomalies.jsonl',
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

$resultsFullPath = Resolve-Path $ResultsPath
$protocolEntries = Read-ProtocolLog -Path $ProtocolLogPath
$section = Format-AnomalySection -RunLabel $RunLabel -Entries $protocolEntries

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

Set-Content -Path $resultsFullPath -Value $content -NoNewline
