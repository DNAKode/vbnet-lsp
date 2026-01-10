param(
    [ValidateSet('csharp-node','csharp-dotnet','all')][string]$Suite = 'all',
    [ValidateSet('pipe','stdio')][string]$Transport = 'pipe',
    [string]$ServerPath = '_external\roslyn\artifacts\bin\Microsoft.CodeAnalysis.LanguageServer\Release\net10.0\Microsoft.CodeAnalysis.LanguageServer.dll',
    [string]$ProtocolPath = '_external\vscode-csharp\src\lsptoolshost\server\roslynProtocol.ts',
    [string]$SolutionPath = '_external\roslyn\Roslyn.sln',
    [string]$LogDirectory = '_test\codex-tests\csharp-lsp\logs',
    [string]$FixtureSolutionPath = '_test\codex-tests\csharp-lsp\fixtures\basic\Basic.sln',
    [string]$FixtureFilePath = '_test\codex-tests\csharp-lsp\fixtures\basic\Basic\Class1.cs',
    [switch]$FeatureTests
)

$ErrorActionPreference = 'Stop'

function Invoke-CSharpDotnet {
    param([string]$Transport)

    $args = @(
        '--serverPath', $ServerPath,
        '--logDirectory', $LogDirectory,
        '--rootPath', '.',
        '--transport', $Transport,
        '--protocolPath', $ProtocolPath
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
        '--protocolPath', $ProtocolPath
    )

    node -r "$env:NODE_PATH\ts-node\register\transpile-only" _test\codex-tests\csharp-lsp\node-client.ts @args
}

switch ($Suite) {
    'csharp-dotnet' { Invoke-CSharpDotnet -Transport $Transport }
    'csharp-node' { Invoke-CSharpNode }
    'all' {
        Invoke-CSharpDotnet -Transport $Transport
        Invoke-CSharpNode
    }
}
