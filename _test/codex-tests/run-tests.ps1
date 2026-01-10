param(
    [ValidateSet('csharp-node','csharp-dotnet','vbnet-lsp','emacs','all')][string]$Suite = 'all',
    [ValidateSet('pipe','stdio')][string]$Transport = 'pipe',
    [string]$ServerPath = '_external\roslyn\artifacts\bin\Microsoft.CodeAnalysis.LanguageServer\Release\net10.0\Microsoft.CodeAnalysis.LanguageServer.dll',
    [string]$ProtocolPath = '_external\vscode-csharp\src\lsptoolshost\server\roslynProtocol.ts',
    [string]$SolutionPath = '_external\roslyn\Roslyn.sln',
    [string]$LogDirectory = '_test\codex-tests\csharp-lsp\logs',
    [string]$FixtureSolutionPath = '_test\codex-tests\csharp-lsp\fixtures\basic\Basic.sln',
    [string]$FixtureFilePath = '_test\codex-tests\csharp-lsp\fixtures\basic\Basic\Class1.cs',
    [string]$ProtocolLogPath = '_test\codex-tests\logs\protocol-anomalies.jsonl',
    [switch]$FeatureTests
)

$ErrorActionPreference = 'Stop'
if ([System.IO.Path]::IsPathRooted($ProtocolLogPath)) {
    $protocolLogFullPath = $ProtocolLogPath
} else {
    $protocolLogFullPath = Join-Path (Resolve-Path '.').Path $ProtocolLogPath
}
New-Item -ItemType Directory -Path (Split-Path $protocolLogFullPath -Parent) -Force | Out-Null
if (Test-Path $protocolLogFullPath) {
    Clear-Content -Path $protocolLogFullPath
} else {
    New-Item -ItemType File -Path $protocolLogFullPath -Force | Out-Null
}

function Invoke-CSharpDotnet {
    param([string]$Transport)

    $args = @(
        '--serverPath', $ServerPath,
        '--logDirectory', $LogDirectory,
        '--rootPath', '.',
        '--transport', $Transport,
        '--protocolPath', $ProtocolPath,
        '--protocolLog', $protocolLogFullPath
    )

    if ($FeatureTests) {
        $args += @('--solutionPath', $FixtureSolutionPath, '--testFile', $FixtureFilePath, '--featureTests')
    } else {
        $args += @('--solutionPath', $SolutionPath)
    }

    dotnet run --project _test\codex-tests\csharp-lsp\CSharpLspSmokeTest\CSharpLspSmokeTest.csproj -- @args
}

function Invoke-CSharpNode {
    $nodePath = Join-Path (Resolve-Path '.').Path '_external\vscode-csharp\node_modules'
    $env:NODE_PATH = $nodePath

    $args = @(
        '--serverPath', $ServerPath,
        '--logDirectory', $LogDirectory,
        '--rootPath', '.',
        '--solutionPath', $SolutionPath,
        '--protocolPath', $ProtocolPath,
        '--protocolLog', $protocolLogFullPath
    )

    node -r "$env:NODE_PATH\ts-node\register\transpile-only" _test\codex-tests\csharp-lsp\node-client.ts @args
}

switch ($Suite) {
    'csharp-dotnet' { Invoke-CSharpDotnet -Transport $Transport }
    'csharp-node' { Invoke-CSharpNode }
    'vbnet-lsp' { & _test\codex-tests\vbnet-lsp\run-tests.ps1 }
    'emacs' { & _test\codex-tests\clients\emacs\run-tests.ps1 }
    'all' {
        Invoke-CSharpDotnet -Transport $Transport
        Invoke-CSharpNode
        & _test\codex-tests\vbnet-lsp\run-tests.ps1
        & _test\codex-tests\clients\emacs\run-tests.ps1
    }
}

$runLabel = if ($Suite -eq 'all') { "Suite=all Transport=$Transport" } else { "Suite=$Suite Transport=$Transport" }
& _test\codex-tests\Update-TestResults.ps1 -ProtocolLogPath $protocolLogFullPath -RunLabel $runLabel
