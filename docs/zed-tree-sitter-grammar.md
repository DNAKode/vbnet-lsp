# Zed Tree-sitter Grammar Note

Last updated: 2026-05-01

Decision:

- The Zed adapter uses the project-owned `tree-sitter-vbnet` grammar project
  at the repository root.
- That grammar is authoritative for VB.NET syntax in Zed. It is maintained with
  this project instead of depending on `CodeAnt-AI/tree-sitter-vb-dotnet`.
- The public `DNAKode/tree-sitter-vbnet` repository is a downstream mirror. Do
  not make normal grammar changes there; change `DNAKode/vbnet-lsp/tree-sitter-vbnet`
  and mirror out.
- The checked-in Zed manifest points at the public grammar mirror,
  `https://github.com/DNAKode/tree-sitter-vbnet`. Local dev-extension
  validation may temporarily use a `file://` grammar URL before mirroring, but
  local machine paths should not be committed.
- For public Zed publishing, the manifest should use a pinned commit SHA or tag.

Current scope:

- The grammar is intentionally conservative and line-oriented. It covers the
  declaration, literal, expression, attribute, lambda, and block node names that
  the Zed queries require today.
- Generated parser files are checked in so CI can parse/query fixtures without
  relying on native `tree-sitter generate` behavior on Windows.
- Query coverage remains tied to named grammar nodes only.
- The standalone grammar mirror exists so Zed can clone a normal public Git
  repository; it does not change the source-of-truth rule.

Validation performed:

- Regenerated `src/parser.c`, `src/grammar.json`, and `src/node-types.json`
  from the owned `grammar.js`.
- Parsed every fixture in
  `test-explore/clients/zed/fixtures/tree-sitter`.
- Compiled all Zed query files with `tree-sitter query` against every fixture.
- Updated `scripts/verify-zed-tree-sitter.ps1` to validate the owned grammar
  directly instead of packing an npm grammar dependency.

Tracked risks:

- The grammar is not yet a complete VB.NET specification. XML literals, LINQ
  query expressions, detailed preprocessor structure, full generic constraints,
  and every control-flow variant remain expansion targets.
- Zed install and live smoke must still validate that the absolute `file://`
  grammar URL plus `path` field is accepted by the installed Zed build on
  Windows.
