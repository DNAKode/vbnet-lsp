param(
    [string]$ExtensionPath = 'adapters/zed/vbnet-zed',
    [string]$ExpectedVersion = ''
)

$ErrorActionPreference = 'Stop'

function Assert-File {
    param([Parameter(Mandatory = $true)][string]$Path)

    if (-not (Test-Path -LiteralPath $Path -PathType Leaf)) {
        throw "Required file is missing: $Path"
    }
}

function Assert-Directory {
    param([Parameter(Mandatory = $true)][string]$Path)

    if (-not (Test-Path -LiteralPath $Path -PathType Container)) {
        throw "Required directory is missing: $Path"
    }
}

function Get-ManifestValue {
    param(
        [Parameter(Mandatory = $true)][string]$Manifest,
        [Parameter(Mandatory = $true)][string]$Key
    )

    $pattern = "^\s*$([regex]::Escape($Key))\s*=\s*`"([^`"]+)`"\s*$"
    $match = [regex]::Match($Manifest, $pattern, [System.Text.RegularExpressions.RegexOptions]::Multiline)
    if (-not $match.Success) {
        throw "extension.toml is missing string key '$Key'."
    }

    return $match.Groups[1].Value
}

$repoRoot = [System.IO.Path]::GetFullPath((Join-Path $PSScriptRoot '..'))
$fullExtensionPath = [System.IO.Path]::GetFullPath((Join-Path $repoRoot $ExtensionPath))

Assert-Directory $fullExtensionPath

$requiredFiles = @(
    'extension.toml',
    'Cargo.toml',
    '.gitignore',
    'src/lib.rs',
    'src/server.rs',
    'src/debug.rs',
    'src/platform.rs',
    'src/workspace.rs',
    'README.md',
    'LICENSE',
    'MIRRORING.md',
    'debug_adapter_schemas/netcoredbg.json',
    'languages/vbnet/config.toml',
    'languages/vbnet/brackets.scm',
    'languages/vbnet/highlights.scm',
    'languages/vbnet/outline.scm',
    'languages/vbnet/folds.scm',
    'languages/vbnet/indents.scm',
    'languages/vbnet/overrides.scm',
    'languages/vbnet/textobjects.scm',
    'languages/vbnet/semantic_token_rules.json'
)

$requiredZedTestFiles = @(
    'test-explore/clients/zed/fixtures/single-file/Module1.vb',
    'test-explore/clients/zed/fixtures/single-file/.zed/settings.json',
    'test-explore/clients/zed/fixtures/vbproj/ZedFixture.vbproj',
    'test-explore/clients/zed/fixtures/vbproj/.zed/settings.json',
    'test-explore/clients/zed/fixtures/sln/ZedSlnFixture.sln',
    'test-explore/clients/zed/fixtures/sln/.zed/settings.json',
    'test-explore/clients/zed/fixtures/slnf/Program.vb',
    'test-explore/clients/zed/fixtures/slnf/ZedSlnfFixture.vbproj',
    'test-explore/clients/zed/fixtures/slnf/ZedSlnfFixture.sln',
    'test-explore/clients/zed/fixtures/slnf/ZedSlnfFixture.slnf',
    'test-explore/clients/zed/fixtures/slnf/.zed/settings.json',
    'test-explore/clients/zed/fixtures/slnx/ZedSlnxFixture.slnx',
    'test-explore/clients/zed/fixtures/slnx/.zed/settings.json',
    'test-explore/clients/zed/fixtures/mixed-vb-csharp/ZedMixed.sln',
    'test-explore/clients/zed/fixtures/mixed-vb-csharp/Class1.cs',
    'test-explore/clients/zed/fixtures/mixed-vb-csharp/.zed/settings.json',
    'test-explore/clients/zed/fixtures/debug-console/DebugConsole.vbproj',
    'test-explore/clients/zed/fixtures/debug-console/.zed/debug.json',
    'test-explore/clients/zed/fixtures/debug-console/.zed/tasks.json',
    'test-explore/clients/zed/scripts/prepare-zed-profile.ps1',
    'test-explore/clients/zed/scripts/prepare-zed-profile.sh',
    'test-explore/clients/zed/scripts/run-zed-debug-smoke.ps1',
    'test-explore/clients/zed/scripts/run-zed-debug-smoke.sh',
    'test-explore/clients/zed/probes/lsp-probe/VbNet.Zed.LspProbe.csproj',
    'test-explore/clients/zed/probes/lsp-probe/Program.cs',
    'test-explore/clients/zed/probes/dap-probe/VbNet.Zed.DapProbe.csproj',
    'test-explore/clients/zed/probes/dap-probe/Program.cs',
    'test-explore/clients/zed/probes/probe-harness/VbNet.Zed.ProbeHarness.csproj',
    'test-explore/clients/zed/probes/probe-harness/Program.cs',
    'test-explore/clients/zed/probes/real-server-harness/VbNet.Zed.RealServerHarness.csproj',
    'test-explore/clients/zed/probes/real-server-harness/Program.cs'
)

foreach ($relativePath in $requiredFiles) {
    Assert-File (Join-Path $fullExtensionPath $relativePath)
}

foreach ($relativePath in $requiredZedTestFiles) {
    Assert-File (Join-Path $repoRoot $relativePath)
}

$manifestPath = Join-Path $fullExtensionPath 'extension.toml'
$manifest = Get-Content -LiteralPath $manifestPath -Raw
$id = Get-ManifestValue -Manifest $manifest -Key 'id'
$name = Get-ManifestValue -Manifest $manifest -Key 'name'
$version = Get-ManifestValue -Manifest $manifest -Key 'version'
$grammarRev = Get-ManifestValue -Manifest $manifest -Key 'rev'
$cargoToml = Get-Content -LiteralPath (Join-Path $fullExtensionPath 'Cargo.toml') -Raw
$cargoVersion = Get-ManifestValue -Manifest $cargoToml -Key 'version'

if ($id -ne 'vbnet') {
    throw "Expected extension id 'vbnet', got '$id'."
}

if ($name -ne 'VB.NET') {
    throw "Expected extension name 'VB.NET', got '$name'."
}

if ($ExpectedVersion -ne '' -and $version -ne $ExpectedVersion) {
    throw "Expected Zed extension version '$ExpectedVersion', got '$version'."
}

if ($cargoVersion -ne $version) {
    throw "Expected Cargo.toml version '$version' to match extension.toml, got '$cargoVersion'."
}

if ($ExpectedVersion -ne '' -and ($grammarRev -eq 'main' -or $grammarRev -eq 'master')) {
    throw "Release verification requires extension.toml [grammars.vbnet] rev to be an immutable commit SHA or tag, got '$grammarRev'."
}

foreach ($requiredText in @(
    '[grammars.vbnet]',
    'repository = "https://github.com/DNAKode/tree-sitter-vbnet"',
    '[language_servers.vbnet-ls]',
    '[debug_adapters.netcoredbg]',
    '[debug_locators.vbnet]',
    'language = "VB.NET"',
    'languages = ["VB.NET"]'
)) {
    if (-not $manifest.Contains($requiredText)) {
        throw "extension.toml is missing required section $requiredText."
    }
}

foreach ($forbiddenText in @(
    'CodeAnt-AI',
    'tree-sitter-vb-dotnet',
    'cfca210ce8fdcb5245bd9cd5c47ce0a21a8488d5'
)) {
    if ($manifest.Contains($forbiddenText)) {
        throw "extension.toml must use the owned VB.NET grammar, but still contains '$forbiddenText'."
    }
}

$grammarRoot = Join-Path $repoRoot 'tree-sitter-vbnet'
foreach ($requiredGrammarFile in @(
    'package.json',
    'grammar.js',
    'README.md',
    'MIRRORING.md',
    'LICENSE',
    'src/grammar.json',
    'src/node-types.json',
    'src/parser.c'
)) {
    Assert-File (Join-Path $grammarRoot $requiredGrammarFile)
}

$zedManagedGrammarPath = Join-Path $fullExtensionPath 'grammars/vbnet'
if ((Test-Path -LiteralPath $zedManagedGrammarPath -PathType Container) -and
    -not (Test-Path -LiteralPath (Join-Path $zedManagedGrammarPath '.git') -PathType Container)) {
    throw "adapters/zed/vbnet-zed/grammars/vbnet must be absent or a Zed-managed Git clone; do not check in a plain grammar directory there."
}

$grammarPackage = Get-Content -LiteralPath (Join-Path $grammarRoot 'package.json') -Raw | ConvertFrom-Json
if ($grammarPackage.name -ne 'tree-sitter-vbnet') {
    throw "tree-sitter-vbnet/package.json must declare package name tree-sitter-vbnet."
}

$grammarMirroring = Get-Content -LiteralPath (Join-Path $grammarRoot 'MIRRORING.md') -Raw
foreach ($requiredText in @(
    'DNAKode/vbnet-lsp/tree-sitter-vbnet',
    'DNAKode/tree-sitter-vbnet',
    '-TreeSitterRepoPath'
)) {
    if (-not $grammarMirroring.Contains($requiredText)) {
        throw "tree-sitter-vbnet/MIRRORING.md is missing required mirroring note '$requiredText'."
    }
}

$languageConfig = Get-Content -LiteralPath (Join-Path $fullExtensionPath 'languages/vbnet/config.toml') -Raw
foreach ($requiredText in @(
    'name = "VB.NET"',
    'code_fence_block_name = "vb"',
    'grammar = "vbnet"',
    'path_suffixes = ["vb"]',
    'tab_size = 4',
    'autoclose_before',
    'brackets = ['
)) {
    if (-not $languageConfig.Contains($requiredText)) {
        throw "languages/vbnet/config.toml is missing '$requiredText'."
    }
}

$debugSchema = Get-Content -LiteralPath (Join-Path $fullExtensionPath 'debug_adapter_schemas/netcoredbg.json') -Raw
$debugSchemaJson = $debugSchema | ConvertFrom-Json
if ($debugSchemaJson.type -ne 'object') {
    throw "debug_adapter_schemas/netcoredbg.json must describe an object schema."
}
if (-not ($debugSchemaJson.required -contains 'request')) {
    throw "debug_adapter_schemas/netcoredbg.json must require 'request'."
}
if ($debugSchemaJson.properties.type.const -ne 'netcoredbg') {
    throw "debug_adapter_schemas/netcoredbg.json must constrain type to 'netcoredbg'."
}
if (-not ($debugSchemaJson.properties.request.enum -contains 'launch') -or
    -not ($debugSchemaJson.properties.request.enum -contains 'attach')) {
    throw "debug_adapter_schemas/netcoredbg.json request enum must include launch and attach."
}
if ($debugSchemaJson.properties.args.type -ne 'array' -or
    $debugSchemaJson.properties.args.items.type -ne 'string') {
    throw "debug_adapter_schemas/netcoredbg.json args must be an array of strings."
}
if ($debugSchemaJson.properties.env.type -ne 'object' -or
    $debugSchemaJson.properties.env.additionalProperties.type -ne 'string') {
    throw "debug_adapter_schemas/netcoredbg.json env must be an object with string values."
}
if (-not ($debugSchemaJson.allOf | Where-Object {
            $_.if.properties.request.const -eq 'launch' -and
            ($_.then.anyOf | Where-Object { $_.required -contains 'program' }) -and
            ($_.then.anyOf | Where-Object { $_.required -contains 'projectPath' })
        })) {
    throw "debug_adapter_schemas/netcoredbg.json must require program or projectPath for launch."
}
if (-not ($debugSchemaJson.allOf | Where-Object {
            $_.if.properties.request.const -eq 'attach' -and
            ($_.then.required -contains 'processId')
        })) {
    throw "debug_adapter_schemas/netcoredbg.json must require processId for attach."
}
foreach ($requiredText in @(
    '"program"',
    '"projectPath"',
    '"stopAtEntry"',
    '"justMyCode"',
    '"enableStepFiltering"',
    '"buildBeforeLaunch"',
    '"processId"'
)) {
    if (-not $debugSchema.Contains($requiredText)) {
        throw "debug_adapter_schemas/netcoredbg.json is missing '$requiredText'."
    }
}

$debugFixtureConfig = Get-Content -LiteralPath (Join-Path $repoRoot 'test-explore/clients/zed/fixtures/debug-console/.zed/debug.json') -Raw | ConvertFrom-Json
if (-not ($debugFixtureConfig -is [System.Array]) -or $debugFixtureConfig.Count -lt 2) {
    throw "debug-console/.zed/debug.json must contain launch and attach configurations."
}

$debugFixtureJson = Get-Content -LiteralPath (Join-Path $repoRoot 'test-explore/clients/zed/fixtures/debug-console/.zed/debug.json') -Raw
foreach ($requiredText in @(
    '"adapter": "netcoredbg"',
    '"request": "launch"',
    '"request": "attach"',
    '"program": "bin/Debug/net10.0/DebugConsole.dll"',
    '"command": "dotnet"',
    '"VBNET_ZED_DEBUG_FIXTURE": "1"',
    '"VBNET_ZED_DEBUG_LOG": "zed-debug-fixture.log"',
    '"processId": 0'
)) {
    if (-not $debugFixtureJson.Contains($requiredText)) {
        throw "debug-console/.zed/debug.json is missing '$requiredText'."
    }
}

$debugTaskConfig = Get-Content -LiteralPath (Join-Path $repoRoot 'test-explore/clients/zed/fixtures/debug-console/.zed/tasks.json') -Raw | ConvertFrom-Json
if (-not ($debugTaskConfig -is [System.Array]) -or $debugTaskConfig.Count -lt 2) {
    throw "debug-console/.zed/tasks.json must contain build and run task configurations."
}

$debugTaskJson = Get-Content -LiteralPath (Join-Path $repoRoot 'test-explore/clients/zed/fixtures/debug-console/.zed/tasks.json') -Raw
foreach ($requiredText in @(
    '"label": "dotnet build DebugConsole"',
    '"label": "dotnet run DebugConsole"',
    '"command": "dotnet"',
    '"DebugConsole.vbproj"',
    '"from-zed-task"',
    '"cwd": "$ZED_WORKTREE_ROOT"'
)) {
    if (-not $debugTaskJson.Contains($requiredText)) {
        throw "debug-console/.zed/tasks.json is missing '$requiredText'."
    }
}

$semanticTokenRulesPath = Join-Path $fullExtensionPath 'languages/vbnet/semantic_token_rules.json'
$semanticTokenRules = Get-Content -LiteralPath $semanticTokenRulesPath -Raw | ConvertFrom-Json
if (-not ($semanticTokenRules -is [System.Array]) -or $semanticTokenRules.Count -eq 0) {
    throw "languages/vbnet/semantic_token_rules.json must contain a non-empty JSON array."
}

$semanticTokenTypes = @{}
foreach ($rule in $semanticTokenRules) {
    if (-not $rule.token_type) {
        throw "Every semantic token rule must include token_type."
    }
    if (-not $rule.style -or $rule.style.Count -eq 0) {
        throw "Semantic token rule '$($rule.token_type)' must include a non-empty style array."
    }
    $semanticTokenTypes[$rule.token_type] = $true
}

foreach ($requiredTokenType in @(
    'namespace',
    'type',
    'class',
    'struct',
    'interface',
    'enum',
    'typeParameter',
    'function',
    'method',
    'property',
    'field',
    'event',
    'parameter',
    'variable',
    'keyword',
    'comment',
    'string',
    'number',
    'operator'
)) {
    if (-not $semanticTokenTypes.ContainsKey($requiredTokenType)) {
        throw "semantic_token_rules.json is missing token_type '$requiredTokenType'."
    }
}

$readme = Get-Content -LiteralPath (Join-Path $fullExtensionPath 'README.md') -Raw
foreach ($requiredText in @(
    '## Installation',
    '## Language Server',
    '## Workspace Behavior',
    '## Debugging',
    '## Troubleshooting',
    'workspace.solutionPath',
    'msbuildPath',
    'mixed VB.NET/C#',
    'process:exec',
    'download_file',
    'netcoredbg',
    'DNAKode/vbnet-lsp'
)) {
    if (-not $readme.Contains($requiredText)) {
        throw "README.md is missing required guidance '$requiredText'."
    }
}

foreach ($queryName in @(
    'brackets.scm',
    'highlights.scm',
    'outline.scm',
    'folds.scm',
    'indents.scm',
    'overrides.scm',
    'textobjects.scm'
)) {
    $queryPath = Join-Path $fullExtensionPath "languages/vbnet/$queryName"
    $queryText = Get-Content -LiteralPath $queryPath -Raw
    if ($queryText.Contains('Placeholder')) {
        throw "$queryName still contains placeholder text."
    }
    if (-not $queryText.Contains('@')) {
        throw "$queryName does not contain any Tree-sitter captures."
    }
}

$slnfFixtureJson = Get-Content -LiteralPath (Join-Path $repoRoot 'test-explore/clients/zed/fixtures/slnf/ZedSlnfFixture.slnf') -Raw
foreach ($requiredText in @(
    '"path": "ZedSlnfFixture.sln"',
    '"ZedSlnfFixture.vbproj"'
)) {
    if (-not $slnfFixtureJson.Contains($requiredText)) {
        throw "ZedSlnfFixture.slnf is missing '$requiredText'."
    }
}

$slnfSettingsJson = Get-Content -LiteralPath (Join-Path $repoRoot 'test-explore/clients/zed/fixtures/slnf/.zed/settings.json') -Raw
foreach ($requiredText in @(
    '"solutionPath": "ZedSlnfFixture.slnf"',
    '"language_servers": ["vbnet-ls"]',
    '"VBNET_ZED_TEST_LOG"'
)) {
    if (-not $slnfSettingsJson.Contains($requiredText)) {
        throw "slnf/.zed/settings.json is missing '$requiredText'."
    }
}

$serverSource = Get-Content -LiteralPath (Join-Path $fullExtensionPath 'src/server.rs') -Raw
foreach ($requiredText in @(
    'const SERVER_REPOSITORY: &str = "DNAKode/vbnet-lsp";',
    'const SERVER_VERSION: &str = concat!("v", env!("CARGO_PKG_VERSION"));',
    'github_release_by_tag_name',
    'download_file',
    '--stdio'
)) {
    if (-not $serverSource.Contains($requiredText)) {
        throw "src/server.rs is missing required release/server behavior '$requiredText'."
    }
}

$debugSource = Get-Content -LiteralPath (Join-Path $fullExtensionPath 'src/debug.rs') -Raw
foreach ($requiredText in @(
    'pub(crate) fn run_dap_locator',
    'project_path_from_dotnet_args',
    'find_debug_program_for_project',
    'Could not infer a VB.NET project',
    'NETCOREDBG_VERSION',
    'download_debug_adapter',
    'download_file',
    '_external'
)) {
    if (-not $debugSource.Contains($requiredText)) {
        throw "src/debug.rs is missing required debug locator behavior '$requiredText'."
    }
}

$platformSource = Get-Content -LiteralPath (Join-Path $fullExtensionPath 'src/platform.rs') -Raw
foreach ($requiredText in @(
    'debug_adapter_asset_url_for',
    'github.com/Samsung/netcoredbg',
    'github.com/Cliffback/netcoredbg-macOS-arm64.nvim',
    'netcoredbg-win64.zip',
    'netcoredbg-linux-amd64.tar.gz',
    'netcoredbg-linux-arm64.tar.gz',
    'netcoredbg-osx-amd64.tar.gz',
    'netcoredbg-osx-arm64.tar.gz'
)) {
    if (-not $platformSource.Contains($requiredText)) {
        throw "src/platform.rs is missing required debugger asset behavior '$requiredText'."
    }
}

$libSource = Get-Content -LiteralPath (Join-Path $fullExtensionPath 'src/lib.rs') -Raw
if (-not $libSource.Contains('fn run_dap_locator')) {
    throw "src/lib.rs does not expose run_dap_locator."
}

$smokeScript = Get-Content -LiteralPath (Join-Path $repoRoot 'test-explore/clients/zed/scripts/run-zed-smoke.ps1') -Raw
foreach ($requiredText in @(
    'UseFixtureSettings',
    'Mode = ''Probe''',
    'RealServer',
    'LocalServerPath',
    'Copy-ZedLogFiles',
    'vbnet-real-server-launch.ps1',
    'zed-real-server.stderr.log',
    'Zed.log',
    'VBNET_LS_TRACE_TRANSPORT',
    'sourceInitializationOptions',
    'initialization_options',
    'VbNet.Zed.LspProbe.csproj'
)) {
    if (-not $smokeScript.Contains($requiredText)) {
        throw "run-zed-smoke.ps1 is missing required generated-settings behavior '$requiredText'."
    }
}

$smokeShellScript = Get-Content -LiteralPath (Join-Path $repoRoot 'test-explore/clients/zed/scripts/run-zed-smoke.sh') -Raw
foreach ($requiredText in @(
    'VBNET_ZED_SMOKE_MODE',
    'real-server',
    'vbnet-real-server-launch.sh',
    'zed-real-server.stderr.log',
    'VBNET_ZED_LOCAL_SERVER_PATH',
    'Zed.log',
    'VBNET_LS_TRACE_TRANSPORT',
    'VbNet.Zed.LspProbe.csproj'
)) {
    if (-not $smokeShellScript.Contains($requiredText)) {
        throw "run-zed-smoke.sh is missing required generated-settings behavior '$requiredText'."
    }
}

$debugSmokeScript = Get-Content -LiteralPath (Join-Path $repoRoot 'test-explore/clients/zed/scripts/run-zed-debug-smoke.ps1') -Raw
foreach ($requiredText in @(
    'Debug VB.NET console',
    'Attach VB.NET console',
    'netcoredbg',
    'NetcoredbgPath',
    'Automate',
    'SendKeys',
    'zed-debug-fixture.log',
    '--user-data-dir',
    'from-zed',
    'from-zed-task'
)) {
    if (-not $debugSmokeScript.Contains($requiredText)) {
        throw "run-zed-debug-smoke.ps1 is missing required debug-smoke behavior '$requiredText'."
    }
}

$debugSmokeShellScript = Get-Content -LiteralPath (Join-Path $repoRoot 'test-explore/clients/zed/scripts/run-zed-debug-smoke.sh') -Raw
foreach ($requiredText in @(
    'Debug VB.NET console',
    'Attach VB.NET console',
    'netcoredbg',
    'VBNET_ZED_NETCOREDBG_PATH',
    '--user-data-dir',
    'from-zed',
    'from-zed-task'
)) {
    if (-not $debugSmokeShellScript.Contains($requiredText)) {
        throw "run-zed-debug-smoke.sh is missing required debug-smoke behavior '$requiredText'."
    }
}

$prepareProfileScript = Get-Content -LiteralPath (Join-Path $repoRoot 'test-explore/clients/zed/scripts/prepare-zed-profile.ps1') -Raw
foreach ($requiredText in @(
    'zed: install dev extension',
    '--user-data-dir',
    'extensions/index.json',
    '"vbnet"',
    'adapters/zed/vbnet-zed'
)) {
    if (-not $prepareProfileScript.Contains($requiredText)) {
        throw "prepare-zed-profile.ps1 is missing required profile-preparation behavior '$requiredText'."
    }
}

$prepareProfileShellScript = Get-Content -LiteralPath (Join-Path $repoRoot 'test-explore/clients/zed/scripts/prepare-zed-profile.sh') -Raw
foreach ($requiredText in @(
    'zed: install dev extension',
    '--user-data-dir',
    'extensions/index.json',
    '"vbnet"',
    'adapters/zed/vbnet-zed'
)) {
    if (-not $prepareProfileShellScript.Contains($requiredText)) {
        throw "prepare-zed-profile.sh is missing required profile-preparation behavior '$requiredText'."
    }
}

$exportScript = Get-Content -LiteralPath (Join-Path $repoRoot 'adapters/scripts/export-adapter-repos.ps1') -Raw
foreach ($requiredText in @(
    'TreeSitterRepoPath',
    'tree-sitter-vbnet',
    'extension.wasm',
    'parser.obj',
    'grammars',
    'No destination provided'
)) {
    if (-not $exportScript.Contains($requiredText)) {
        throw "export-adapter-repos.ps1 is missing required Tree-sitter mirror behavior '$requiredText'."
    }
}

$platformSource = Get-Content -LiteralPath (Join-Path $fullExtensionPath 'src/platform.rs') -Raw
foreach ($requiredText in @(
    'vbnet-language-server-win-x64.zip',
    'vbnet-language-server-linux-x64.tar.gz',
    'vbnet-language-server-osx-x64.tar.gz',
    'vbnet-language-server-osx-arm64.tar.gz'
)) {
    if (-not $platformSource.Contains($requiredText)) {
        throw "src/platform.rs is missing release asset mapping '$requiredText'."
    }
}

$releaseWorkflow = Get-Content -LiteralPath (Join-Path $repoRoot '.github/workflows/release.yml') -Raw
foreach ($requiredText in @(
    'rid: win-x64',
    'rid: linux-x64',
    'rid: osx-x64',
    'rid: osx-arm64',
    'vbnet-language-server-${{ matrix.rid }}.${{ matrix.archive_ext }}'
)) {
    if (-not $releaseWorkflow.Contains($requiredText)) {
        throw "release.yml is missing Zed server release artifact behavior '$requiredText'."
    }
}

Push-Location $fullExtensionPath
try {
    cargo check --target wasm32-wasip1
    if ($LASTEXITCODE -ne 0) {
        throw "cargo check failed with exit code $LASTEXITCODE."
    }

    cargo test
    if ($LASTEXITCODE -ne 0) {
        throw "cargo test failed with exit code $LASTEXITCODE."
    }
} finally {
    Pop-Location
}

dotnet build (Join-Path $repoRoot 'test-explore/clients/zed/probes/lsp-probe/VbNet.Zed.LspProbe.csproj') -c Release
if ($LASTEXITCODE -ne 0) {
    throw "LSP probe build failed with exit code $LASTEXITCODE."
}

dotnet build (Join-Path $repoRoot 'test-explore/clients/zed/probes/dap-probe/VbNet.Zed.DapProbe.csproj') -c Release
if ($LASTEXITCODE -ne 0) {
    throw "DAP probe build failed with exit code $LASTEXITCODE."
}

dotnet build (Join-Path $repoRoot 'test-explore/clients/zed/probes/probe-harness/VbNet.Zed.ProbeHarness.csproj') -c Release
if ($LASTEXITCODE -ne 0) {
    throw "Zed probe protocol harness build failed with exit code $LASTEXITCODE."
}

dotnet run --project (Join-Path $repoRoot 'test-explore/clients/zed/probes/probe-harness/VbNet.Zed.ProbeHarness.csproj') -c Release --no-restore -- $repoRoot
if ($LASTEXITCODE -ne 0) {
    throw "Zed probe protocol harness failed with exit code $LASTEXITCODE."
}

dotnet build (Join-Path $repoRoot 'test-explore/clients/zed/probes/real-server-harness/VbNet.Zed.RealServerHarness.csproj') -c Release
if ($LASTEXITCODE -ne 0) {
    throw "Zed real-server protocol harness build failed with exit code $LASTEXITCODE."
}

dotnet build (Join-Path $repoRoot 'test-explore/clients/zed/fixtures/slnf/ZedSlnfFixture.slnf') -c Release
if ($LASTEXITCODE -ne 0) {
    throw "Zed slnf fixture build failed with exit code $LASTEXITCODE."
}

dotnet build (Join-Path $repoRoot 'test-explore/clients/zed/fixtures/debug-console/DebugConsole.vbproj') -c Debug
if ($LASTEXITCODE -ne 0) {
    throw "Zed debug-console fixture build failed with exit code $LASTEXITCODE."
}

$debugFixtureProgram = Join-Path $repoRoot 'test-explore/clients/zed/fixtures/debug-console/bin/Debug/net10.0/DebugConsole.dll'
if (-not (Test-Path -LiteralPath $debugFixtureProgram -PathType Leaf)) {
    throw "Zed debug-console fixture did not produce $debugFixtureProgram."
}

Write-Host "Zed extension verification passed: $fullExtensionPath"
