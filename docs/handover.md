# Handover Notes

Last updated: 2026-02-11

## Current shipped state
- VS Code extension mitigation for netcoredbg `stackTrace` `0x80004002` is published as pre-release `0.1.9`.
- Main release commit: `a5af19816a0eead013d1d3f293c7d312bf21e7d9` on `master`.
- Publish workflow runs for this commit succeeded:
  - https://github.com/DNAKode/vbnet-lsp/actions/runs/21894476220
  - https://github.com/DNAKode/vbnet-lsp/actions/runs/21894557081

## User-facing communication already posted
- Original issue update (published mitigation + version):  
  https://github.com/DNAKode/vbnet-lsp/issues/5#issuecomment-3882451331

## Upstream netcoredbg tracking
- Bug: https://github.com/Samsung/netcoredbg/issues/215
- Fix PR: https://github.com/Samsung/netcoredbg/pull/216
- Internal tracker issue: https://github.com/DNAKode/vbnet-lsp/issues/6

## Upstream code branch status
- Local `_external/netcoredbg` worktree is clean on `master`.
- Fix branch pushed to fork: `govert/fix/winforms-stacktrace-e-nointerface`.
- PR commit reference: `d6e3b0a`.

## Resume checklist
1. Check `Samsung/netcoredbg#216` for merge status.
2. When merged, identify the first released netcoredbg binary containing the fix.
3. Re-run WinForms stack trace repro against that released binary.
4. Update bundled debugger asset to fixed release.
5. Remove/retire `netcoredbg-proxy.js` workaround and `vbnet.debugger.workarounds.stackTraceNoInterfaceFallback` when safe.
6. Publish a follow-up extension version and close `DNAKode/vbnet-lsp#6`.
