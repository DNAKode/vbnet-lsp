# DWSIM Test Harness (Planning Scaffold)

This folder contains a minimal harness for running the VB.NET LSP smoke test
against the DWSIM workspace. It is intended for timing and scale validation and
is non-destructive (read-only).

## Usage

```powershell
test-explore\dwsim\run-tests.ps1
```

## Notes

- Uses `_external/dwsim` as the workspace root and `DWSIM/ApplicationEvents.vb` as the
  initial test file.
- Intended to evolve into performance and functional testing at scale.
- Captures timing events (server start, solution load, didOpen) into
  `test-explore/logs/timing.jsonl` and summarizes them in the test results.



