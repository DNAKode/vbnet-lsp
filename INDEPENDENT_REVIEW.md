Date: 2026-01-09
Reviewer: Codex (GPT-5) acting as independent reviewer
Context: Reviewed top-level documents, docs/*.md, and selected files in _external/vscode-csharp; no VB.NET source code inspected
External research: Limited web review of VS Code language server extension guide and netcoredbg README

# Independent Project Review: VB.NET Language Support (vbnet-lsp)

This review focuses on the stated goals, requirements, architecture, and project plan as documented in README.md, PROJECT_PLAN.md, and docs/*.md. The intent is to surface risks, inconsistencies, and improvements that will increase development velocity and reduce rework. Where uncertainties exist, I provide likely scenarios and a recommended approach.

## Executive assessment

The project has a strong strategic foundation: clear goal (first-class VB.NET support in VS Code using Roslyn), strong open-source stance, a reference architecture (vscode-csharp), and an explicit multi-phase roadmap. The biggest near-term risks are documentation drift and conflicting assumptions between documents (especially around component naming, transport protocol, and feature defaults). The plan is ambitious but mostly coherent; however, implementation realism and dependency strategy (runtime acquisition, debugger integration, and cross-platform packaging) need sharper decisions to prevent early rework.

## Goals and requirements review

Strengths:
- Goals are explicit and aligned with a known reference (vscode-csharp).
- MVP scope is practical and focused on LSP basics.
- Clear non-goals reduce scope creep.

Gaps and refinements suggested:
- Feature parity with vscode-csharp is a large target; define a tighter "parity subset" for v0.1 (e.g., feature set + specific behavioral equivalence tests).
- Performance targets are well stated but currently lack measurement methodology or tooling commitment; define baseline measurement scripts early so targets drive design rather than being retrofitted.
- Define user-level requirements for debugging (Phase 2) in terms of supported launch scenarios (console app, unit test, web app) to avoid a "DAP only" implementation that misses VB.NET usage patterns.

## Architecture and design review

Strengths:
- Layered architecture is clear and matches Roslyn usage patterns.
- Emphasis on cancellation, immutable snapshots, and debounced diagnostics is sound.
- Single architecture document is appropriate for agentic development and helps reduce ADR sprawl.

Risks and issues:
- Transport mismatch: architecture mandates stdio, but research-plan findings indicate vscode-csharp uses named pipes. This is a material divergence. If parity is a strict goal, either:
  - Document the deviation and why stdio is acceptable, or
  - Adopt the named pipe approach early to avoid later migration cost.
- Component naming drift: documents refer to `VbNet.DevKit.LanguageServer`, `VbNet.LanguageServer`, and `vbnet-ls` interchangeably. This will cause confusion in build scripts, docs, and packaging. Choose one canonical name and align all docs.
- Settings defaults conflict with implementation phases. For example, configuration defaults enable formatting/code actions/semantic tokens even though they are Phase 2/3. This is misleading and will create "features not working" reports. Defaults should be false or gated by capability negotiation until implemented.
- Cross-platform runtime strategy is unclear. The plan suggests .NET 10 and later a runtime acquisition path similar to vscode-csharp, but no concrete decision is documented. This impacts packaging, startup time, and user experience.
- "Single architecture doc" policy can hide decision history. Ensure each decision is timestamped and context-rich, or consider a short "decision log" section in architecture.md to avoid losing rationale over time.

## Reference implementation findings (vscode-csharp)

Key observations from _external/vscode-csharp (client side):
- Transport: the C# extension starts the Roslyn language server as a child process, reads a JSON line with a named pipe, and connects via sockets using SocketMessageReader/Writer (not stdio). This is core to its startup flow.
- Protocol extensions: the client and server exchange several custom methods beyond standard LSP (for example: solution/project open, project initialization complete, on-auto-insert, register solution snapshot, build-only diagnostics, and debugger attach).
- Runtime acquisition: package.json declares dependency on ms-dotnettools.vscode-dotnet-runtime and a large runtimeDependencies list for debugger and other components.
- Workspace trust: activation has a limited mode for untrusted workspaces; full activation waits for trust.
- URI handling: the client converts file URIs to correctly decode Windows drive paths before sending to the server.

Implications:
- If parity is the stated goal, plan for named-pipe transport or explicitly document the divergence.
- Custom LSP methods may be required for project/solution loading and other features to match C# behavior.
- Runtime acquisition and workspace trust behavior are part of the real-world user experience; they should be planned early.
- URI conversion issues are non-trivial on Windows and should be documented and tested.

## External research notes (web)

- The VS Code Language Server Extension Guide shows TransportKind.ipc in its sample client and createConnection over IPC for the server, reinforcing that VS Code clients can use non-stdio transports and that incremental document sync is preferred for performance.
- netcoredbg README confirms Debug Adapter Protocol support and shows the debugger can run with a VS Code interpreter mode and optionally listen on a TCP server port (defaulting to stdio otherwise). This impacts how you package and connect to netcoredbg.

## Project plan and sequencing review

Strengths:
- Phased roadmap is logical and aligned to user value.
- Research-plan is detailed and practical, with a good emphasis on verifying external behaviors.
- CI workflows for multi-editor and performance are ambitious and well-motivated.

Concerns:
- There is a tension between "never guess; verify vscode-csharp behavior" and shipping an MVP quickly. If verification is a hard requirement, plan bandwidth for deep code reading and document extraction before MVP design choices harden.
- Dependencies like netcoredbg and DWSIM are large and can become friction points for contributors. Consider a "minimal contributor path" where these are optional and CI handles heavy validation.
- The plan treats Emacs LSP testing as MVP (Phase 1). This may slow the early iterations. Consider deferring full automated Emacs testing until after LSP core stabilization, while still keeping manual validation.

## Documentation consistency and clarity

Key inconsistencies to resolve:
- Target framework and naming: `VbNet.DevKit.LanguageServer` vs `VbNet.LanguageServer`.
- Build paths in README vs development guide (paths do not match).
- Architecture mandates stdio, while research findings describe named pipes in the reference extension.
- Configuration defaults for non-existent features.
- Multiple documents mention "Phase 0 bootstrap completed" but the repo appears documentation-only. Ensure status matches reality.

Encoding/formatting:
- Several documents contain garbled characters (likely encoding artifacts). This reduces readability and makes it harder for agents and humans to parse. Normalize encoding to UTF-8 and re-save the docs to eliminate these artifacts.

## Testing and quality strategy review

Strengths:
- Good testing pyramid: unit, integration, E2E, performance.
- Explicit performance targets are rare and valuable.

Suggestions:
- Define a minimal "contract test" suite that directly compares observed behavior to vscode-csharp for a handful of requests (e.g., completion item kinds, diagnostic timing, rename edits). This will stabilize parity goals.
- Make DWSIM a separate CI workflow with explicit opt-in to reduce cost for PRs.
- Add "LSP compliance tests" that validate JSON-RPC correctness (responses for unknown methods, error codes, cancellation behavior). These are often the source of client compatibility issues.

## Licensing and compliance considerations

- DWSIM is GPL-3.0. Using it for tests is fine, but avoid shipping it in any distributed artifacts or including it in the repo. Ensure CI scripts clone it at runtime and do not redistribute.
- netcoredbg is MIT, which aligns with your license goals. Ensure any bundling includes proper attribution.

## Recommendations (highest leverage first)

1) Resolve naming and path inconsistencies across docs and planned project structure.
2) Decide and document transport strategy (stdio vs named pipes) with a clear rationale tied to parity and performance goals.
3) Decide on custom protocol methods (or explicit non-goals) for solution/project loading and other Roslyn-specific features.
4) Align configuration defaults with current implementation phases and capability negotiation to reduce user confusion.
5) Define the minimal "parity subset" for v0.1 with concrete test cases.
6) Normalize doc encoding and remove garbled characters to improve comprehension and agentic workflow reliability.
7) Clarify runtime acquisition and cross-platform packaging strategy (dotnet runtime dependency vs bundling).
8) Plan for workspace trust and URI conversion behavior early, especially on Windows.

## Suggested v0.1 parity checklist (draft)

Core activation and transport:
- Language server launches and connects via a defined transport (named pipe or stdio) with documented rationale.
- Non-blocking extension activation (server starts without blocking extension activation).
- Workspace trust behavior documented (full vs limited activation if applicable).

Solution and project loading:
- Solution/project discovery mirrors C# extension heuristics (default solution config, discovery and selection logic).
- Custom open solution/project notification path defined (or explicitly out of scope for MVP).

Core LSP features:
- Incremental text sync, diagnostics, completion (with resolve), hover, go-to-definition, find references, rename, document/workspace symbols.
- Cancellation and progress behavior consistent with Roslyn expectations.

Operational behavior:
- Log level control from client to server.
- Windows URI conversion behavior defined and tested.
- Configuration change handling for diagnostics debounce and feature toggles.

## Questions for follow-up (only if needed)

- What is the canonical name for the language server project and executable (namespace, folder, CLI command)?
- Is stdio chosen for MVP intentionally, or should parity with named pipes be prioritized?
- Will the project target .NET 10 specifically, or consider .NET 8 LTS for early adopters?
- Which usage scenarios are considered "must support" for debugging in Phase 2 (console app, unit test, web app, launch configurations)?
- Do you want CI to enforce parity against vscode-csharp behaviors early, or after MVP stability?
- Will you adopt any custom LSP methods similar to the C# extension, or keep to pure LSP in MVP?
- Do you plan to mirror workspace trust behavior (limited activation) from the C# extension?

## Concluding view

This is a well-structured plan with strong architectural grounding. The main blockers to high-velocity execution are documentation drift and a few unresolved decisions that can cascade into rework. By tightening naming consistency, transport strategy, and feature gating, the project can move quickly with fewer surprises while still preserving its high standards for parity and openness.

## Source references

Local:
- `README.md`
- `PROJECT_PLAN.md`
- `docs/architecture.md`
- `docs/development.md`
- `docs/configuration.md`
- `docs/features.md`
- `docs/research-plan.md`
- `_external/vscode-csharp/src/lsptoolshost/server/roslynLanguageServer.ts`
- `_external/vscode-csharp/src/lsptoolshost/server/roslynProtocol.ts`
- `_external/vscode-csharp/src/activateRoslyn.ts`
- `_external/vscode-csharp/src/main.ts`
- `_external/vscode-csharp/package.json`

Web:
- https://code.visualstudio.com/api/language-extensions/language-server-extension-guide
- https://microsoft.github.io/language-server-protocol/
- https://github.com/Samsung/netcoredbg (README.md)
