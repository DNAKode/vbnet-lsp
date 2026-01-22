# Roslyn LSP vs VB.NET LSP: Technical Comparison

This document captures a technical, code-backed comparison between the Roslyn LSP server
and our VB.NET LSP server, using the local `_external/roslyn` source tree as the reference.
It clarifies the boundary between the Roslyn LSP layer, editor wrappers, and compiler/workspace
services.

## Summary

- Roslyn's LSP server is shared for C# and VB at the LSP layer (many handlers are exported for both).
- VB support is real in Roslyn LSP (VB unit tests exist for multiple features), but client
  wrappers (e.g., `roslyn.nvim`) are C#-oriented by default and do not expose VB out of the box.
- Some C#-only functionality exists in Roslyn LSP (e.g., decompiled source, Razor co-hosting),
  but we do not implement those for VB either.
- Our VB LSP implements call/type hierarchy (and other features) that are not implemented
  in Roslyn's LSP layer (protocol types exist, but no handlers in the Roslyn LSP code).

## Roslyn LSP "Tower" (Boundary)

Roslyn's LSP server is an LSP host that sits above the compiler workspaces:

Editor -> Editor wrapper (VS Code C# extension / roslyn.nvim / other)  
-> Roslyn LSP server (`Microsoft.CodeAnalysis.LanguageServer`)  
-> Roslyn Workspace & Language Services (C#/VB)

Key boundary: the LSP server is shared; language-specific behavior is driven by Roslyn
workspace services and the client wrapper's configuration (filetypes, initialization,
workspace selection).

## Evidence: Roslyn LSP shared C# + VB

Roslyn's LSP layer exports many handlers as C# + VB services:

- `CSharpVisualBasicLanguageServerFactory`  
  `_external/roslyn/src/LanguageServer/Protocol/CSharpVisualBasicLanguageServerFactory.cs`
- Example shared handlers (many use `[ExportCSharpVisualBasicStatelessLspService]`):
  - `CompletionHandler.cs`
  - `GoToDefinitionHandler.cs`
  - `FormatDocumentHandler.cs`
  - `CodeActionsHandler.cs`

## Evidence: VB support in Roslyn LSP tests

VB unit tests exist in Roslyn LSP's ProtocolUnitTests:

- CodeLens (VB):  
  `_external/roslyn/src/LanguageServer/ProtocolUnitTests/CodeLens/VisualBasicCodeLensTests.cs`
- Inlay hints (VB):  
  `_external/roslyn/src/LanguageServer/ProtocolUnitTests/InlayHint/VisualBasicInlayHintTests.cs`
- OnAutoInsert (VB):  
  `_external/roslyn/src/LanguageServer/ProtocolUnitTests/OnAutoInsert/OnAutoInsertTests.cs`

This shows VB is explicitly tested in the Roslyn LSP layer.

## C#-only features in Roslyn LSP (not VB)

These features exist in Roslyn LSP only for C#:

1) Decompiled source support (C# only)  
   `CSharpCodeDecompilerDecompilationService` is exported only for C#:
   `_external/roslyn/src/LanguageServer/Protocol/Features/DecompiledSource/CSharpCodeDecompilerDecompilationService.cs`

2) Razor co-hosting (C# only)  
   Razor endpoints and co-hosting exist for C# only (no VB Razor).
   This shows up in Roslyn server packaging and in client wrappers (`roslyn.nvim`
   registers `cs`/`razor` filetypes only).

Note: We do not implement these in our VB server either, so these are not
"Roslyn C# features we already match in VB."

## Where our VB LSP goes beyond Roslyn LSP

Our VB LSP implements call hierarchy and type hierarchy as LSP endpoints:

- `src/VbNet.LanguageServer.Vb/Services/CallHierarchyService.vb`
- `src/VbNet.LanguageServer.Vb/Services/TypeHierarchyService.vb`

In Roslyn LSP, the protocol types exist (e.g., `Methods.Navigation.cs`),
but handlers for call/type hierarchy are not present in the LSP layer
(`_external/roslyn/src/LanguageServer/Protocol/Handler` has no call/type hierarchy handlers).
This suggests these endpoints are not implemented in Roslyn LSP (for any language).

## Practical VB gaps are often in client wrappers

Example: `roslyn.nvim` registers only `cs` and `razor` filetypes by default:

- `_external/roslyn.nvim/lsp/roslyn.lua`

So VB might be supported by the server, but not exposed by the wrapper.
This is an important boundary when deciding whether to reuse Roslyn LSP.

## What this implies for boundary decisions

- If we rely on Roslyn LSP, we must also deliver editor-specific wrappers that
  expose VB correctly (filetypes, initialization, solution/project selection).
- If we keep our own VB LSP, we control VB parity directly and can add missing
  features (e.g., call/type hierarchy) without waiting on Roslyn LSP.

## Open: empirical C# vs VB gaps in Roslyn LSP

From static code inspection alone, most LSP handlers are shared across C# + VB.
To prove C#-only LSP behaviors vs VB, we need runtime comparisons:

1) Run Roslyn LSP against VB test projects via the Neovim harness.
2) Compare outputs with Roslyn LSP C# requests (same feature set).
3) Document actual gaps (missing responses, degraded results, different shapes).

This is the next best step for evidence-based decisions.

## Empirical Neovim harness notes (local)

- C# via Roslyn LSP works under the Neovim harness (hover + completion).
- VB via Roslyn LSP currently fails on hover with "Document is null" even after
  `project/open` for `SmallProject.vbproj`.
- Retrying with `solution/open` using `SmallProject.slnx` (a minimal solution wrapper)
  still resulted in "Document is null".
- Using a classic `SmallProject.sln` and logging server messages showed a stronger signal:
  the Roslyn LSP build from the Mason registry reports "The language 'Visual Basic' is not supported"
  and fails to load the VB project at all.
- We tried augmenting the Mason build by copying `Microsoft.CodeAnalysis.VisualBasic*` assemblies
  from NuGet (4.14.0) into a separate folder, but Roslyn still reported VB as unsupported.
  This suggests the build expects matching Roslyn versions (5.4.0-2.*) or additional components.
- We built the Roslyn LSP from source (Release) plus VB assemblies from the same repo, assembled
  a coherent folder (`_external/roslyn-ls-built-win-x64`), and verified C# works in Neovim.
  VB still reports "The language 'Visual Basic' is not supported" even after clearing the MEF
  cache and enabling Trace logging. This indicates the current LanguageServer composition
  does not expose VB language services, even when the VB assemblies are present.
- We then rebuilt via Roslyn's full `Build.cmd` flow (Release + lspEditor), assembled a fresh
  `_external/roslyn-ls-built-full-win-x64`, and added runtime diagnostics in `Program.cs`.
  The diagnostic file reports:
  `SupportedLanguages: TypeScript, C#, Razor` and `Supports Visual Basic: False`,
  even though the VB assemblies are present in the server directory.
- Additional MEF export diagnostics confirm VB services are not being composed:
  `ILanguageService` exports include only `C#, Razor, TypeScript` and
  `ILanguageServiceFactory` exports include only `C#`.
- A reflection probe over `Microsoft.CodeAnalysis.VisualBasic.*` assemblies shows VB
  language service exports are present in source builds (e.g., 34+ services in
  `VisualBasic.Workspaces` and 58+ in `VisualBasic.Features`), but they are not
  loaded into the LSP MEF catalog at runtime.
- Attempting to pass VB assemblies via `--extension` (using the Neovim harness) caused
  the server to crash during workspace creation with a duplicate analyzer reference
  error. This is because the language server adds all `Microsoft.CodeAnalysis.*.dll`
  from the base directory *and* extension assemblies as solution-level analyzers, so
  supplying VB assemblies as extensions while they still exist in the base directory
  creates duplicates. See
  `_external/roslyn/src/LanguageServer/Microsoft.CodeAnalysis.LanguageServer/HostWorkspace/LanguageServerWorkspaceFactory.cs`.
- A workaround is to remove the VB assemblies from the base directory and load them
  only via `--extension`. Using a base folder without `Microsoft.CodeAnalysis.VisualBasic*`
  and an extension folder containing only VB assemblies enables VB successfully in
  Neovim (hover/completion/diagnostics) and yields:
  `SupportedLanguages: C#, Visual Basic, Razor, TypeScript`.

Logs and commands are recorded in `test-explore/TEST_RESULTS.md`.

## Why the "Visual Basic is not supported" message appears

This is not a hard-coded "VB disabled" switch in the LSP layer. It's a MEF composition
issue in the distribution we tested:

- `LanguageServerExportProviderBuilder` builds the MEF catalog from
  `Microsoft.CodeAnalysis*.dll` in the server directory.
  `_external/roslyn/src/LanguageServer/Microsoft.CodeAnalysis.LanguageServer/LanguageServerExportProviderBuilder.cs`
- The Roslyn LSP project only references C# feature assemblies, so the published
  `Microsoft.CodeAnalysis.LanguageServer.deps.json` does **not** list any
  `Microsoft.CodeAnalysis.VisualBasic*` assemblies. Even if you copy VB assemblies
  into the server folder, the default load context will not bind to them, so MEF
  discovery never loads those parts.
  `_external/roslyn/src/LanguageServer/Microsoft.CodeAnalysis.LanguageServer/Microsoft.CodeAnalysis.LanguageServer.csproj`
- The Mason build we tested does **not** include any `Microsoft.CodeAnalysis.VisualBasic*`
  assemblies in its directory, so the MEF catalog does not contain VB language services.
  This means `MefWorkspaceServices` has no VB services to return.
  `_external/roslyn/src/Workspaces/SharedUtilitiesAndExtensions/Workspace/Core/Workspace/Mef/MefWorkspaceServices.cs`
- When a VB document is opened, `HostWorkspaceServices.GetLanguageServices` throws
  `NotSupportedException` for the language name, which yields the message we saw.
  `_external/roslyn/src/Workspaces/Core/Portable/Workspace/Host/HostWorkspaceServices.cs`

This matches the runtime log:
`System.NotSupportedException: The language 'Visual Basic' is not supported.`

## Evidence that Roslyn LSP *can* support VB in source

Despite the distribution issue above, the Roslyn source tree shows explicit VB LSP support:

- The language server is created with `WellKnownLspServerKinds.CSharpVisualBasicLspServer`.
  `_external/roslyn/src/LanguageServer/Microsoft.CodeAnalysis.LanguageServer/LanguageServer/LanguageServerHost.cs`
- The LSP layer exports many handlers as C# + VB services.
  `_external/roslyn/src/LanguageServer/Protocol/Handler/*`
- The language ID mapping includes `.vb` and `vb`.
  `_external/roslyn/src/LanguageServer/Protocol/LanguageInfoProvider.cs`
- VB protocol tests exist (e.g., CodeLens, InlayHints).
  `_external/roslyn/src/LanguageServer/ProtocolUnitTests/CodeLens/VisualBasicCodeLensTests.cs`

So the practical issue in our Neovim experiment is the *packaging* of the Roslyn LSP
server build (missing VB assemblies), not a fundamental absence of VB handlers in the
Roslyn LSP source.

## Source generators: C# only in Roslyn compiler

We attempted to validate source-generated document support in the LSP harness using
a VB fixture. The VB project did not see generated types, and `textDocument/definition`
never resolved to a `roslyn-source-generated://` URI. A quick scan of the Roslyn source
shows source generator driver usage is present in the C# compiler and tests, but not in
the Visual Basic compiler codebase (no `GeneratorDriver`/`ISourceGenerator` references
under `src/Compilers/VisualBasic`). This strongly suggests VB compilation does not run
source generators today, so source-generated document URIs are effectively C#‑only.

We are therefore using a C# fixture to validate the LSP `sourceGeneratedDocument/_roslyn_getText`
path in Neovim, while treating VB source‑generated documents as unsupported for now.

Note: We removed the Neovim SG harness fixtures from this repo to keep the VB test surface
focused and avoid maintaining a C#‑only harness path inside the VB test suite.
