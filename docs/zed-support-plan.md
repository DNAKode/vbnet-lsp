# Zed Support Plan

Last updated: 2026-04-30

## Reference Baseline

Before implementing each milestone, re-check the current Zed documentation and
one or two maintained Zed extension repositories. The extension API and
publishing rules are still evolving enough that stale assumptions can cause
review delays.

Primary references:

- Zed language extensions: https://zed.dev/docs/extensions/languages
- Zed debugger extensions: https://zed.dev/docs/extensions/debugger-extensions
- Zed extension publishing: https://zed.dev/docs/extensions/developing-extensions
- Zed language configuration: https://zed.dev/docs/configuring-languages
- Zed CLI reference: https://zed.dev/docs/reference/cli.html
- Zed extension installation locations: https://zed.dev/docs/extensions/installing-extensions
- Zed C# extension example: https://github.com/zed-extensions/csharp

The plan below records the intended shape, but implementation should verify the
exact `extension.toml` schema, `zed_extension_api` method signatures, and
debugger metadata fields against the current docs at the time of coding.
As of 2026-04-30, the official docs describe opening files/workspaces with the
Zed CLI, installing dev extensions through the UI/action, checking `Zed.log`,
and launching with `zed --foreground`. They do not document a stable headless
extension-test CLI. Treat fully automated Zed-in-the-loop tests as a tiered
strategy that starts with log/probe-based smoke coverage and adopts a headless
or command-driven harness when Zed documents one.

## Goal

Provide first-class VB.NET support in Zed while keeping `vbnet-lsp` as the
canonical development repository. The publishable Zed extension repository
(`DNAKode/vbnet-zed`) should exist from the beginning, but it should be treated
as generated distribution output mirrored from this repository.

The Zed extension version should match the language server release version.

First-class means:

- `.vb` files are detected as VB.NET without user file-association hacks.
- Tree-sitter highlighting, outline, folding, indentation, bracket matching, and
  text objects work before the language server has started.
- LSP features work against real `.sln`, `.slnx`, and `.vbproj` workspaces, not
  only single files.
- Mixed VB.NET/C# repositories do not lose C# support or start duplicate C#
  tooling.
- Debugging works through Zed's DAP UI for normal VB.NET console/project
  workflows.
- The extension is installable through Zed's extension registry and testable as
  a local dev extension.
- Release versions, server downloads, and downstream mirroring are predictable.

## Naming And IDs

Use stable names up front. Zed publishing guidance discourages extension IDs and
display names containing the word "zed", so `vbnet-zed` should be the GitHub
repository name only.

Recommended identifiers:

- Downstream repo: `DNAKode/vbnet-zed`
- Extension id: `vbnet`
- Extension name: `VB.NET`
- Language name: `VB.NET`
- Grammar id: `vbnet`
- Language server id: `vbnet-ls`
- Debug adapter id: `netcoredbg`
- Debug locator id: `vbnet`

Do not register aliases or language-server entries for C#.

## Repository Layout

Canonical source in this repository:

```text
adapters/zed/vbnet-zed/
  extension.toml
  Cargo.toml
  Cargo.lock
  src/lib.rs
  src/server.rs
  src/debug.rs
  src/platform.rs
  src/workspace.rs
  README.md
  LICENSE
  MIRRORING.md
  debug_adapter_schemas/netcoredbg.json
  languages/vbnet/config.toml
  languages/vbnet/brackets.scm
  languages/vbnet/highlights.scm
  languages/vbnet/outline.scm
  languages/vbnet/folds.scm
  languages/vbnet/indents.scm
  languages/vbnet/overrides.scm
  languages/vbnet/textobjects.scm
  languages/vbnet/semantic_token_rules.json

test-explore/clients/zed/
  README.md
  fixtures/
    single-file/
    vbproj/
    sln/
    slnx/
    mixed-vb-csharp/
    debug-console/
    tree-sitter/
  scripts/
    run-zed-smoke.ps1
    run-zed-smoke.sh
    run-zed-ui-smoke.ps1
    run-zed-ui-smoke.sh
  logs/
  probes/
    lsp-probe/
    dap-probe/
```

Downstream repository:

```text
DNAKode/vbnet-zed
```

The downstream repository should contain a clear `MIRRORING.md` notice: edits
belong in `DNAKode/vbnet-lsp/adapters/zed/vbnet-zed`, not directly in the
distribution repository.

Do not commit language-server binaries, `netcoredbg` binaries, downloaded
archives, local Zed caches, or generated parser build artifacts unless Zed's
extension registry explicitly requires a generated parser artifact.

## Early Setup

1. Create `DNAKode/vbnet-zed`.
2. Add the canonical adapter snapshot under `adapters/zed/vbnet-zed`.
3. Extend `adapters/scripts/export-adapter-repos.ps1` with:
   - `-ZedRepoPath`
   - `-Clean`
   - `-DryRun`
4. Add a Zed verification script, for example
   `scripts/verify-zed-extension.ps1`, that checks:
   - required files exist
   - `extension.toml` and language config exist
   - Tree-sitter query files exist
   - Rust extension code builds with `cargo check`
   - release version pins match the expected server version when supplied
5. Update packaging and release docs:
   - `docs/downstream-repositories.md`
   - `docs/editor-packaging.md`
   - `docs/adapter-release-checklist.md`
   - this document

Initial downstream setup should create:

- `main` for released extension commits.
- `generated/dev` for automatic mirrors from `vbnet-lsp/master`.
- a repository description that says it is mirrored from `vbnet-lsp`.
- a `MIRRORING.md` file copied from the canonical snapshot.
- branch protection only if it does not make automation painful.

Initial monorepo setup should add:

- `adapters/zed/vbnet-zed/.gitignore` for local Zed/Rust/build caches.
- `test-explore/clients/zed/README.md` with local dev-extension install steps.
- `test-explore/clients/zed/scripts/run-zed-smoke.ps1` and `.sh` for
  log/probe-based Zed smoke tests.
- `test-explore/clients/zed/scripts/run-zed-ui-smoke.ps1` and `.sh` for
  optional UI-driven smoke tests.
- `test-explore/clients/zed/probes/lsp-probe` for recording Zed's LSP traffic
  without depending on the full VB.NET server.
- `test-explore/clients/zed/probes/dap-probe` for recording Zed's DAP traffic
  without depending on `netcoredbg`.
- a placeholder verification script that fails clearly for unimplemented checks
  rather than silently passing.
- documentation of required GitHub credentials for mirroring, without storing
  secrets in the repo.

## Implementation Runbook

An implementation agent should work in this order:

1. Re-check the current Zed language/debugger/publishing docs and inspect one
   maintained language extension.
2. Scaffold `adapters/zed/vbnet-zed` with valid manifest, Rust crate, language
   directory, README, license, mirroring notice, and empty-but-valid query files.
3. Add export support and verify a clean local mirror into `../vbnet-zed`.
4. Add static validation for required files and manifest values.
5. Add a local dev-extension guide and manually install the extension in Zed.
6. Add server discovery and launch using a user-configured local server path.
7. Add `vbnet-ls` PATH lookup.
8. Add pinned release download/extract support.
9. Validate core LSP features against `test/TestProjects/SmallProject`.
10. Build out Tree-sitter grammar/query coverage using fixtures.
11. Add debug adapter metadata and explicit `netcoredbg` launch support.
12. Add project/solution configuration and mixed VB/C# workspace validation.
13. Wire CI checks for build, validation, export dry-run, and release pinning.
14. Mirror to `vbnet-zed/generated/dev`.
15. After the first server release with Zed support, mirror the tagged snapshot
    to `vbnet-zed/main` and submit/update the Zed registry entry.

Each step should leave the repo in a working state. Do not defer docs until the
end; the docs are part of the acceptance criteria for each phase.

## Automated Zed-In-The-Loop Testing Strategy

Testing should deliberately separate what can be verified without Zed from what
must be verified inside Zed. The target is real Zed coverage, but the harness
must be robust enough to run repeatedly without corrupting a developer's normal
Zed profile.

Test tiers:

1. Static validation: manifest, file layout, version pins, query presence,
   `cargo check`, export dry-run.
2. Extension logic tests: pure Rust tests for platform detection, artifact URL
   selection, binary discovery, download/extract decisions, workspace
   heuristics, and debug schema generation.
3. Protocol probe tests outside Zed: run `vbnet-ls` and `netcoredbg` or probe
   adapters directly to verify LSP/DAP behavior without an editor.
4. Zed log/probe smoke tests: launch real Zed against a fixture workspace with
   this dev extension installed and a probe LSP/DAP binary configured. Assert
   from logs and probe JSONL that Zed loaded the extension, selected VB.NET for
   `.vb`, started `vbnet-ls`, sent `initialize`, and opened the expected
   document.
5. Zed real-server smoke tests: launch real Zed with local `VbNet.LanguageServer`
   and assert logs show successful server startup and no extension/LSP failures.
6. Zed UI automation tests: use OS-level UI automation only for scenarios that
   cannot be proven through logs/probes, such as invoking hover, completion,
   rename, formatting, and debugger UI actions.
7. Manual release verification: final human check on all supported platforms
   until Zed exposes a stable headless/command automation path.

Do not make tier 6 mandatory on every PR at first. It is expected to be more
fragile than tier 1-5 because current public Zed docs do not expose a stable
headless extension-test runner. Run UI automation as nightly, pre-release, or
manually triggered CI until it proves reliable.

Zed profile isolation:

- Each Zed-in-loop test must use a temporary settings/extensions/work directory.
- Prefer Zed's documented `--user-data-dir <DIR>` CLI option for profile
  isolation. It redirects user data, extensions, logs, and extension work files.
- Use environment variables such as `XDG_DATA_HOME`, `LOCALAPPDATA`, or a
  temporary `HOME` only as fallback mechanisms if a test platform cannot use
  `--user-data-dir`.
- The harness should collect logs from the temporary user-data directory and
  print the exact path on failure.
- Tests must never modify the developer's real settings or installed
  extensions.

Dev-extension installation automation:

- Preferred: use a documented Zed CLI/action if one becomes available for
  installing a dev extension.
- Acceptable early path: document one-time manual dev-extension installation
  for local testing, then run automated smoke tests against that profile.
- CI path to investigate: seed the temporary Zed extension directory with the
  mirrored dev extension only if this layout is documented or verified stable.
  Zed's documented extension directory has `installed` and `work`
  subdirectories; tests may use that knowledge only after confirming it works
  with `--user-data-dir`.
- Last resort: UI automation that opens the command palette, runs
  `zed: install dev extension`, and selects `adapters/zed/vbnet-zed`. Keep this
  opt-in because file-picker automation is inherently platform-specific.

Zed launch harness:

- Open fixture workspaces with
  `zed --foreground --user-data-dir <temp-dir> --wait <workspace>` when the CLI
  supports `--wait` on that platform.
- Capture stdout/stderr and `Zed.log` into `test-explore/clients/zed/logs`.
- Put `.zed/settings.json` in each fixture to force deterministic settings:

  ```json
  {
    "languages": {
      "VB.NET": {
        "language_servers": ["vbnet-ls"]
      }
    },
    "lsp": {
      "vbnet-ls": {
        "binary": {
          "path": "<absolute path to probe or local server>",
          "arguments": ["--stdio"],
          "env": {
            "VBNET_ZED_TEST_LOG": "<absolute path to jsonl log>"
          }
        },
        "initialization_options": {
          "semanticTokens": true,
          "workspace": {
            "solutionPath": ""
          }
        }
      }
    }
  }
  ```

- The probe LSP should log every request/notification as JSONL and provide
  deterministic minimal responses for `initialize`, `textDocument/hover`,
  `textDocument/completion`, `textDocument/definition`, formatting, semantic
  tokens, and shutdown.
- The probe DAP should log `initialize`, `launch`, `attach`, breakpoint, and
  configuration requests and send minimal valid DAP responses/events.

Success criteria for Zed-in-loop smoke tests:

- Zed starts from a clean test profile.
- The dev extension loads without install/build errors.
- Opening `.vb` selects the `VB.NET` language and starts `vbnet-ls`.
- The configured probe or real server receives `initialize` with expected root
  information.
- The opened `.vb` file produces `textDocument/didOpen`.
- For workspace fixtures, initialization options contain the expected explicit
  `.sln`, `.slnx`, or `.vbproj` selection where configured.
- Logs contain no extension panic, WebAssembly build failure, language-server
  start failure, or unhandled DAP error.

Future headless path:

- Track Zed's headless/editor-automation work. If Zed exposes a documented
  headless command or command-execution API, replace OS UI automation with that
  path and promote more Zed-in-loop tests into required CI.

## Mirroring And Release Flow

Development flow:

```text
vbnet-lsp/master -> vbnet-zed/generated/dev
```

Release flow:

```text
vbnet-lsp tag vX.Y.Z -> vbnet-zed/main -> vbnet-zed tag vX.Y.Z
```

Release mirroring should export from the exact `vbnet-lsp` tag. Do not promote
whatever happens to be on `vbnet-zed/generated/dev`.

Initial Zed releases may use a manual approval step before updating
`vbnet-zed/main`. After the first stable publishing cycle, reduce the manual
step. The long-term direction is a full `vbnet-lsp` release that publishes
server artifacts and updates all supported downstream editor repositories for
the same version when validation passes.

Mirroring implementation details:

- Export should delete stale files in the destination while preserving `.git`.
- Export should stamp or preserve `MIRRORING.md`.
- Export should support `-DryRun` and print every copied/removed path.
- CI should fail if the canonical snapshot cannot be exported cleanly.
- Release mirroring must verify that the target server artifacts exist before
  pushing `vbnet-zed/main`.
- The generated commit message should include the source commit or tag, for
  example `Mirror Zed adapter from DNAKode/vbnet-lsp v0.2.0`.
- Direct commits in `vbnet-zed` should be treated as emergency-only and
  backported to the canonical snapshot immediately.

## Phase 1: Zed Extension Skeleton

Build the publishable extension shape first:

- `extension.toml`
- `Cargo.toml`
- Rust extension entry point in `src/lib.rs`
- VB.NET language config
- minimal Tree-sitter query files
- README and mirroring notice
- export script support
- verification script

`extension.toml` should include, after checking current Zed schema:

- id: `vbnet`
- name: `VB.NET`
- description: first-class VB.NET language support
- version: same as the server release
- repository: `https://github.com/DNAKode/vbnet-zed`
- grammar registration for `vbnet`
- language-server registration for `vbnet-ls` scoped only to `VB.NET`
- debug-adapter registration for `netcoredbg` once debugging starts

The language config should include:

- `name = "VB.NET"`
- `grammar = "vbnet"`
- `path_suffixes = ["vb"]`
- `line_comments = ["' "]`
- four-space indentation defaults
- bracket/autoclose rules appropriate for VB strings, parentheses, XML literals,
  attributes, and comments

The server launch path should prefer:

1. user-configured binary path
2. `vbnet-ls` on `PATH`
3. downloaded release artifact pinned to the extension/server version

Launch the server over stdio:

```text
vbnet-ls --stdio
```

or, for extracted release archives:

```text
VbNet.LanguageServer --stdio
```

Fallback to `dotnet VbNet.LanguageServer.dll --stdio` only when needed.

Server download implementation should map host platform to current release
artifacts:

- Windows x64: `vbnet-language-server-win-x64.zip`
- Linux x64: `vbnet-language-server-linux-x64.tar.gz`
- macOS x64: `vbnet-language-server-osx-x64.tar.gz`
- macOS arm64: `vbnet-language-server-osx-arm64.tar.gz`

If Zed runs on a platform without a published server artifact, fail with an
actionable message that tells the user to install `DNAKode.VbNet.Lsp` as a
global .NET tool or configure a local server path.

Acceptance criteria for this phase:

- Zed can install the extension as a dev extension.
- Opening a `.vb` file selects `VB.NET`.
- The extension does not require bundled server binaries.
- Static validation and `cargo check` pass.
- Export to `../vbnet-zed` works in dry-run and real modes.

Automated Zed-in-loop test for this phase:

- Fixture: `test-explore/clients/zed/fixtures/single-file`.
- Server: probe LSP configured through fixture `.zed/settings.json`.
- Launch: real Zed with a temporary profile and the dev extension installed.
- Assert from `Zed.log` and probe JSONL:
  - extension loads
  - `.vb` file opens as `VB.NET`
  - `vbnet-ls` command is requested
  - probe receives `initialize`
  - probe receives `textDocument/didOpen` for the fixture `.vb`
  - no extension build or startup errors are logged

## Phase 2: Core LSP Support

Validate that Zed can use the existing server for:

- initialize/shutdown
- diagnostics
- hover
- completion
- go to definition
- references
- rename
- formatting
- semantic tokens
- document symbols
- folding ranges

Minimum LSP implementation path:

1. Use an explicit local server path first; do not start with download logic.
2. Launch over stdio with `--stdio`.
3. Confirm `stderr` logging appears in Zed logs or document where it appears.
4. Confirm `initialize` uses `rootUri`/workspace folders in a way the server
   already understands.
5. Add initialization options only after basic startup is stable.
6. Add PATH and release-download discovery after local path works.

Project and solution handling must be treated as an early compatibility area,
not a polish item. This was a major source of VS Code integration complexity,
and Zed has its own workspace and language-server activation model.

Zed workspace investigation:

- Confirm how Zed chooses language servers when a workspace contains both VB.NET
  and C# files.
- Confirm whether Zed can scope this extension to `.vb` buffers while still
  using `.sln`, `.slnx`, and `.vbproj` files for workspace discovery.
- Confirm whether Zed exposes enough workspace context to choose between
  multiple solutions.
- Confirm how Zed settings should represent an explicit solution path.
- Confirm how multi-root or multi-worktree projects should be represented.
- Confirm how Zed handles multiple language servers attached to related .NET
  projects in the same repository.

VB.NET workspace behavior should support:

- `.sln`
- `.slnx`
- `.slnf` if the server continues to support it
- `.vbproj`
- solution-less `.vbproj` discovery
- explicit user-selected solution/project overrides
- generated/designer files without treating them as primary project roots

Coexistence with C# support:

- Do not register this extension as a C# language provider.
- Do not attempt to own `.cs`, `.csproj`, or C# Roslyn language-server behavior.
- Avoid workspace-wide actions that would start or replace Zed's C# extension
  for C# buffers.
- If a solution contains both `.vbproj` and `.csproj`, prefer loading the
  solution for VB semantic context but only attach the VB.NET language server to
  `.vb` buffers unless Zed requires broader attachment for workspace services.
- Document mixed-language solution behavior clearly, especially when C# files are
  present for shared projects, test projects, or references.

Map Zed settings to server CLI arguments and initialization options where
supported:

- server path
- log level
- MSBuild path
- solution path
- project search paths
- exclude paths
- max project count/results
- diagnostics mode
- semantic tokens toggle
- formatting toggle

If Zed cannot provide interactive solution picking equivalent to VS Code, start
with explicit settings and deterministic auto-detection. Add interactive
commands only after confirming the extension API supports them cleanly.

Workspace acceptance criteria:

- Single `.vb` file opens with syntax support even outside a project.
- Single `.vbproj` workspace loads semantic features.
- Single `.sln` workspace loads semantic features.
- Single `.slnx` workspace loads semantic features.
- Workspace with multiple `.sln`/`.slnx` files can use an explicit setting.
- Mixed solution with `.vbproj` and `.csproj` keeps C# files under Zed's C#
  extension while VB files use `vbnet-ls`.
- Designer/generated files do not cause incorrect root selection.
- Project-load failures are visible and actionable in Zed logs.

Automated Zed-in-loop tests for this phase:

- Probe LSP workspace tests:
  - `fixtures/single-file`
  - `fixtures/vbproj`
  - `fixtures/sln`
  - `fixtures/slnx`
  - `fixtures/mixed-vb-csharp`
- Each fixture should contain `.zed/settings.json` with deterministic
  `lsp.vbnet-ls.binary` and initialization options.
- Assert from probe JSONL:
  - `initialize.rootUri` or workspace folders match the opened fixture
  - explicit `workspace.solutionPath` is passed when configured
  - `.sln` and `.slnx` paths are not confused
  - `.vb` documents produce `didOpen`
  - `.cs` documents in the mixed fixture do not trigger this extension's LSP
    when opened as C#
- Real-server workspace tests:
  - use local `VbNet.LanguageServer.dll` or `vbnet-ls`
  - open `test/TestProjects/SmallProject`
  - open a `.vb` file
  - assert logs show server startup and no project-load crash
- Optional UI automation:
  - trigger hover and completion in a `.vb` buffer
  - assert the probe LSP received `textDocument/hover` and
    `textDocument/completion`
  - keep this opt-in until the automation path is reliable

## Phase 3: Debugging Workstream

Debugging should start early, not after the language extension is complete. The
VS Code debugger integration already proved that debugger packaging, process
selection, launch inference, console behavior, and netcoredbg edge cases can
produce integration-specific issues.

Initial debugging investigation:

- Study Zed debugger extension APIs and current debugger extension examples.
- Determine whether Zed supports the debug adapter protocol path needed by
  `netcoredbg`.
- Confirm how Zed represents launch and attach configurations.
- Identify how extension code can provide or validate debug configurations.
- Decide whether Zed requires separate debugger extension metadata or whether it
  can live in the same VB.NET extension.

Zed debugger scaffolding should include:

- `[debug_adapters.netcoredbg]` in `extension.toml`
- `debug_adapter_schemas/netcoredbg.json`
- implementation of `get_dap_binary`
- implementation of `dap_request_kind`
- implementation of `dap_config_to_scenario` if the new-process modal supports
  the expected .NET launch shape
- debug locator investigation for converting build/test tasks into debug
  scenarios

First implementation target:

- discover user-configured `netcoredbg`
- search `netcoredbg` on `PATH`
- optionally download a pinned `netcoredbg` asset later, if compatible with Zed
  extension policy
- support launch of a compiled `.dll`
- support attach to process if Zed exposes suitable process picking or config
  hooks
- document explicit launch config first; add project inference after the basic
  path is reliable

The first debug schema should support explicit configuration only:

- `type`
- `request`
- `name`
- `program`
- `args`
- `cwd`
- `env`
- `stopAtEntry`
- `justMyCode`
- `enableStepFiltering`

Then add convenience fields:

- `projectPath`
- `framework`
- `configuration`
- `buildBeforeLaunch`
- `processId` or attach equivalent

Known risks to test early:

- console mode differences from VS Code
- Windows path and quoting behavior
- attach behavior on Windows, macOS, and Linux
- `netcoredbg` executable permissions on macOS/Linux
- failure mode when the target project has not been built
- stack trace/interface fallback behavior already handled in the VS Code
  adapter
- how Zed surfaces debug adapter errors to users

Debugging validation checklist:

- launch built console app from `.vbproj` output
- launch with explicit `program`, `cwd`, `args`, and `env`
- stop at entry
- set and hit breakpoints in `.vb`
- step over/into/out
- inspect locals
- evaluate expressions where supported
- read debug console output
- attach to running .NET process
- verify graceful failure when `netcoredbg` is missing

Debugging acceptance criteria:

- Users can start a debug session from an explicit `program` path.
- Breakpoints bind in `.vb` files.
- Launch failures point to build output, missing `program`, or missing
  `netcoredbg`, not generic adapter failure.
- Attach is documented as supported or unsupported for each platform based on
  testing.
- The extension does not regress normal LSP startup when debug support is
  unavailable.

Automated Zed-in-loop tests for this phase:

- DAP probe test:
  - configure `netcoredbg` debug adapter to point at `dap-probe`
  - launch Zed with `fixtures/debug-console`
  - use UI automation or a future Zed command API to start an explicit launch
    configuration
  - assert probe JSONL records `initialize`, breakpoint setup if applicable,
    `launch`, and `configurationDone`
- Real `netcoredbg` smoke test:
  - build `test/TestProjects/DebugConsole`
  - configure explicit `program`, `cwd`, `args`, and `env`
  - start through Zed
  - assert logs show the adapter started and exited cleanly
  - verify breakpoints bind manually at first; promote to automated UI smoke
    only after the DAP probe path is stable
- Failure-path test:
  - configure a missing `netcoredbg`
  - assert Zed surfaces an actionable adapter error and LSP startup still works

This workstream should run alongside LSP and Tree-sitter work so debugger
constraints can influence packaging and settings before the extension shape
hardens.

## Phase 4: Tree-sitter Grammar Workstream

Tree-sitter is a first-class deliverable, not just syntax coloring. Zed depends
on Tree-sitter for the editing feel around highlighting, outline, folding,
indentation, selections, and structural navigation. A weak grammar will make the
extension feel second-class even if the LSP is strong.

The project should do a detailed grammar investigation before committing to an
upstream grammar dependency.

The grammar decision should be made explicitly in a short design note under
`test-explore/clients/zed` or `docs/`, not buried in a commit message.

Investigation tasks:

- Evaluate existing VB.NET Tree-sitter grammars, including maintenance status,
  license, generated parser quality, and coverage.
- Compare grammar behavior against real VB.NET syntax using this repository's
  `.vb` files and dedicated fixtures.
- Identify gaps around:
  - XML documentation comments
  - XML literals
  - preprocessor directives
  - attributes
  - generic type and method syntax
  - nullable/reference syntax as applicable to VB.NET projects
  - LINQ/query expressions
  - multiline statements and implicit continuations
  - lambda expressions
  - object and collection initializers
  - `WithEvents`, `Handles`, and `AddHandler`/`RemoveHandler`
  - `Async`/`Await`, iterators, and `Yield`
  - `#Region` folding
  - partial classes/modules
  - designer-generated VB files
  - legacy VB.NET constructs still common in real projects
- Decide whether to:
  - contribute heavily to an existing grammar,
  - fork and maintain a `tree-sitter-vbnet` grammar,
  - or create an authoritative grammar under the DNAKode organization.

Evaluation criteria for grammar candidates:

- parser generation works on Windows, Linux, and macOS
- license is compatible with Zed extension publication
- grammar repository has clear ownership and release/tag strategy
- node names are stable and descriptive enough for Zed queries
- error recovery is acceptable while typing incomplete code
- external scanners, if present, are small and portable
- corpus tests are easy to run in CI
- grammar can represent VB-specific constructs without treating them as generic
  identifiers or comments

Authoritative grammar target:

- parse the full practical VB.NET language used by modern and legacy projects
- keep syntax nodes stable enough for Zed queries
- include a broad corpus of real-world fixtures
- include parser tests for every major VB.NET construct
- document unsupported or ambiguous constructs explicitly
- upstream improvements where possible if we build on an existing grammar

Recommended grammar development loop:

1. Create or import the grammar in a dedicated grammar repository or vendored
   test location.
2. Add the fixture corpus before writing Zed queries.
3. Run the grammar parser over fixtures and this repository's `.vb` files.
4. Track parse-error counts per fixture.
5. Fix grammar errors before compensating in Zed queries.
6. Keep query files aligned with stable syntax node names.
7. Add regression fixtures for every parsing bug found in real code.

Recommended grammar test corpus:

```text
test-explore/clients/zed/fixtures/tree-sitter/
  basics.vb
  namespaces-imports.vb
  types.vb
  members.vb
  generics.vb
  attributes.vb
  xml-doc-comments.vb
  xml-literals.vb
  preprocessor.vb
  linq-query.vb
  async-await.vb
  lambdas.vb
  events-handles.vb
  object-initializers.vb
  designer-generated.vb
  regions.vb
  error-recovery.vb
```

Zed query implementation should be developed against that corpus:

- `highlights.scm` for keywords, types, members, literals, comments, XML docs,
  attributes, preprocessor directives, and operators
- `brackets.scm` for parentheses, attributes, strings, XML literals, and any
  bracket pairs the grammar exposes reliably
- `outline.scm` for namespaces, types, methods, properties, fields, events, and
  enum members
- `folds.scm` for namespaces, types, methods, property blocks, control-flow
  blocks, XML doc blocks, and `#Region`
- `indents.scm` for block starts/ends, multiline statements, query expressions,
  object initializers, collection initializers, and XML literals
- `textobjects.scm` for classes/modules/interfaces and methods/properties in Vim
  mode
- `overrides.scm` for string/comment/XML scopes that need different completion
  or autoclose behavior
- `semantic_token_rules.json` if `vbnet-ls` emits token types/modifiers that
  need sensible default styling in Zed

Tree-sitter validation checklist:

- parser produces no errors on representative real files
- parser recovers usefully in incomplete code while editing
- highlighting does not depend on LSP availability
- outline remains stable for large files
- folding matches VB block structure
- indentation handles nested blocks and multiline expressions
- query files are small enough to maintain but complete enough to feel native

Tree-sitter acceptance criteria:

- Highlighting remains useful with the language server disabled.
- Outline shows the main structure of real VB.NET files.
- Folding handles `Namespace`, type blocks, member blocks, control-flow blocks,
  XML documentation blocks, and `#Region`.
- Indentation does not fight normal VB block editing.
- Grammar limitations are documented with examples and tracked as issues.

Automated Zed-in-loop tests for this phase:

- Parser/query tests outside Zed:
  - run the Tree-sitter parser over every file in
    `fixtures/tree-sitter`
  - fail on unexpected parse errors
  - run query validation for `highlights.scm`, `outline.scm`, `folds.scm`,
    `indents.scm`, `brackets.scm`, `textobjects.scm`, and `overrides.scm`
- Zed grammar-load smoke:
  - launch Zed with the language server disabled or pointed at a no-op probe
  - open representative Tree-sitter fixtures
  - assert logs show no grammar/query load failures
  - capture screenshots for manual review on release candidates
- Optional UI automation:
  - open outline panel and verify expected symbols are present if Zed exposes a
    scriptable or inspectable UI path
  - fold/unfold `Namespace`, class/module, method, and `#Region` blocks through
    keyboard commands and capture screenshots
  - keep visual assertions conservative; grammar/query parser checks should be
    the primary automated signal

## Phase 5: Workspace And UX Polish

Improve the end-user experience:

- automatic solution/project discovery behavior documented for Zed
- clear server logs and troubleshooting steps
- settings examples for small and large solutions
- guidance for local server development
- guidance for release-server usage
- user-facing error messages for missing .NET SDK, missing MSBuild, or failed
  project load

README requirements for the Zed extension:

- installation from the Zed registry
- local dev-extension install
- local server development path
- release-server default behavior
- settings examples for explicit solution path and MSBuild path
- mixed VB/C# solution notes
- debugging setup and limitations
- troubleshooting section for server download, .NET SDK, MSBuild, and
  `netcoredbg`
- link back to `DNAKode/vbnet-lsp` for issues and releases

The extension should fail loudly but helpfully. A user should not have to know
the internals of Zed, Roslyn, or MSBuild to understand the next troubleshooting
step.

Automated Zed-in-loop tests for this phase:

- Missing server test:
  - point `vbnet-ls` at a missing binary
  - assert Zed logs contain the expected actionable error
- Missing .NET/MSBuild test:
  - run with a controlled bad `msbuildPath` or missing SDK scenario where
    feasible
  - assert project-load errors are visible and documented
- Download fallback test:
  - use a temporary extension work directory
  - force release-artifact discovery/download when network is available
  - assert the extracted server exists, is executable where needed, and starts
    with `--stdio`
  - keep this as a release/pre-release test if network access makes it too slow
    for every PR
- README/settings test:
  - verify documented setting names appear in the extension schema and sample
    `.zed/settings.json` files
  - verify troubleshooting messages in code are reflected in README guidance

## Phase 6: Testing And CI

Pull request validation:

- `dotnet build`
- `dotnet test`
- Zed extension verification script
- `cargo check` for the Zed extension
- export dry run to a temporary folder
- Tree-sitter grammar/query validation when available
- Zed probe smoke on platforms where Zed can be launched safely in CI

CI jobs to add incrementally:

- `zed-verify`: run the Zed verification script.
- `zed-cargo-check`: run `cargo check` in `adapters/zed/vbnet-zed`.
- `zed-export-dry-run`: export to a temporary directory with `-Clean -DryRun`.
- `zed-export-contents`: export to a temporary directory and verify no expected
  files are missing.
- `zed-tree-sitter`: run grammar/parser/query checks once the grammar workflow
  exists.
- `zed-release-pin`: on tags, verify `extension.toml` version and default server
  download version match the tag.
- `zed-probe-smoke`: launch real Zed with a temporary profile and LSP probe,
  initially Linux-only with a manual or nightly trigger.
- `zed-real-server-smoke`: launch real Zed with a local built server, initially
  pre-release/manual because it depends on installed Zed and GUI support.
- `zed-dap-probe-smoke`: launch real Zed with DAP probe once debug adapter
  metadata exists, initially manual/nightly.

Manual Zed smoke checklist:

- open `.vb` file
- confirm language detection
- confirm highlighting before LSP starts
- confirm LSP starts
- hover over symbol
- request completion after `.`
- go to definition
- find references
- rename symbol
- format document
- inspect outline
- fold namespace/type/member/region
- run debugger launch smoke test
- run debugger attach smoke test

Manual smoke test matrix:

- Windows x64
- macOS arm64
- macOS x64 if available
- Linux x64
- local server path
- `vbnet-ls` from PATH
- downloaded release server
- single-file workspace
- `.vbproj` workspace
- `.sln` workspace
- `.slnx` workspace
- mixed VB/C# workspace

Release validation:

- verify all server artifacts exist for the target version
- verify Zed adapter version matches the server version
- verify default server download pin matches the server version
- run required static checks, Tree-sitter parser/query checks, and Zed probe
  smoke tests
- run at least one real-server Zed smoke test before mirroring to `main`
- mirror from `vbnet-lsp@vX.Y.Z` to `vbnet-zed/main`
- tag `vbnet-zed` with `vX.Y.Z`

Zed test artifacts to retain on CI failure:

- captured `Zed.log`
- Zed process stdout/stderr
- probe LSP JSONL
- probe DAP JSONL
- fixture `.zed/settings.json`
- extension manifest and effective version pins
- screenshots from optional UI/visual tests
- extracted server/download directory listing, if download fallback was tested

## Open Questions To Resolve During Implementation

- Does Zed's current extension schema use `language`, `languages`, or both for
  language-server registration? Use the current documented schema when
  scaffolding.
- Does Zed expose enough command/UI API for solution picking, or should solution
  selection remain settings-only initially?
- Can Zed debug locators infer a VB.NET debug target from `dotnet build` or
  `dotnet run` tasks cleanly?
- Should `netcoredbg` be downloaded by the extension, installed separately, or
  reused from the VS Code extension artifact strategy?
- Do we need Linux arm64 server artifacts before publishing broadly to Zed users?
- Should the authoritative Tree-sitter grammar live in this monorepo, a separate
  `DNAKode/tree-sitter-vbnet` repository, or an upstream/forked grammar repo?
- Which semantic token styles need extension-provided defaults in
  `semantic_token_rules.json`?
- Is there a documented non-interactive way to install a dev extension for tests,
  or must early CI use profile seeding/manual preinstallation?
- Can Zed be run reliably under `xvfb-run` on Linux GitHub Actions, including
  WebAssembly extension loading and grammar compilation?
- Which Zed profile directories can be safely redirected per platform without
  modifying a user's real settings?
- Does Zed emit enough log detail to assert language selection and LSP startup,
  or do we need probe-side assertions for all meaningful checks?
- If Zed adds a headless/command-execution test API, which UI automation tests
  should be promoted into required CI first?
