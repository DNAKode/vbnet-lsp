# C# LSP Smoke Test (Experimental)

This directory contains a minimal harness to validate that a locally built Roslyn
language server can be started and driven via LSP without VS Code.

## What this tests

- Launching `Microsoft.CodeAnalysis.LanguageServer` directly.
- Connecting over stdio.
- Basic LSP handshake (`initialize`, `initialized`, `shutdown`, `exit`).
- Feature requests (completion, hover, definition, references, document symbols) when a test file is provided.

## Prerequisites

- .NET SDK 10 installed.
- Roslyn language server built locally from `_external/roslyn`.

Build the server:

```powershell
dotnet build _external\roslyn\src\LanguageServer\Microsoft.CodeAnalysis.LanguageServer\Microsoft.CodeAnalysis.LanguageServer.csproj -c Release
```

## Run the smoke test (stdio or named pipe)

```powershell
dotnet run --project _test\codex-tests\csharp-lsp\CSharpLspSmokeTest\CSharpLspSmokeTest.csproj `
  --serverPath _external\roslyn\artifacts\bin\Microsoft.CodeAnalysis.LanguageServer\Release\net10.0\Microsoft.CodeAnalysis.LanguageServer.dll `
  --logDirectory _test\codex-tests\csharp-lsp\logs `
  --rootPath . `
  --transport stdio `
  --solutionPath _external\roslyn\Roslyn.sln `
  --protocolPath _external\vscode-csharp\src\lsptoolshost\server\roslynProtocol.ts
```

Expected result:
- The process exits with code 0.
- A log directory is created with server logs.

## Run via the top-level test script

```powershell
_test\codex-tests\run-tests.ps1 -Suite csharp-dotnet -Transport pipe -FeatureTests
```

## Run feature tests against the fixture solution

The fixture is a small C# solution under `fixtures/basic` with a caret marker (`/*caret*/`) used as the test position.

```powershell
dotnet run --project _test\codex-tests\csharp-lsp\CSharpLspSmokeTest\CSharpLspSmokeTest.csproj `
  --serverPath _external\roslyn\artifacts\bin\Microsoft.CodeAnalysis.LanguageServer\Release\net10.0\Microsoft.CodeAnalysis.LanguageServer.dll `
  --logDirectory _test\codex-tests\csharp-lsp\logs `
  --rootPath . `
  --transport pipe `
  --solutionPath _test\codex-tests\csharp-lsp\fixtures\basic\Basic.sln `
  --protocolPath _external\vscode-csharp\src\lsptoolshost\server\roslynProtocol.ts `
  --testFile _test\codex-tests\csharp-lsp\fixtures\basic\Basic\Class1.cs `
  --featureTests
```

If the feature tests are slow, you can increase the feature timeout with `--featureTimeoutSeconds`.

## Run the Node named-pipe client

This uses the C# extension's `roslynProtocol.ts` definitions to send a `solution/open` notification.
It uses JSON-RPC over the named pipe, matching the extension's transport.

```powershell
$env:NODE_PATH = "C:\Work\vbnet-lsp\_external\vscode-csharp\node_modules"
node -r "$env:NODE_PATH\ts-node\register" _test\codex-tests\csharp-lsp\node-client.ts `
  --serverPath _external\roslyn\artifacts\bin\Microsoft.CodeAnalysis.LanguageServer\Release\net10.0\Microsoft.CodeAnalysis.LanguageServer.dll `
  --logDirectory _test\codex-tests\csharp-lsp\logs `
  --solutionPath _external\roslyn\Roslyn.sln
```

## Notes

- The official C# extension uses named pipes; this smoke test uses `--stdio` for simplicity.
- The C# smoke test reads `roslynProtocol.ts` to resolve the `solution/open` method name.
- The Node client uses named pipes and the extension's protocol definitions for `solution/open`.
- You can expand this harness to open documents and request completion/hover for deeper coverage.
