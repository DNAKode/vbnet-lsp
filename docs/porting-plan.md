# `VB.NET` Porting Plan (C# -> VB.NET) — Completed

Version: 1.0
Last Updated: 2026-01-19
Status: Completed

## Summary

- The language server, tests, and harnesses have been ported to `VB.NET`.
- The C# implementation, C# tests, and C# harnesses have been removed from the repo.
- The extension now ships the `VB.NET` server only; C# selection flags were retired.

## Scope (Completed)

Ported and retained:
- `src/VbNet.LanguageServer.Vb`
- `test/VbNet.LanguageServer.Tests.Vb`
- `test/VbNet.Extension.Tests.Vb`
- `test-explore/vbnet-lsp`

Unchanged:
- `src/extension` (TypeScript)
- `_external/` reference repositories

## Final Validation Steps (Completed)

- CI-safe tests pass for VB.NET projects.
- VS Code + Emacs harnesses execute against the VB.NET server.
- C# implementations and selection flags were removed after parity confidence.

## Archive Notes

- The original 4-way C#/VB.NET cross-test matrix is now obsolete and removed.
- Keep this document as a historical record of the migration effort.

## Notes

- Update `test-explore/TEST_RESULTS.md` for substantial exploratory runs.
