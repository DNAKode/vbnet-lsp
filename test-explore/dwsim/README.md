# DWSIM Test Harness (Planning Scaffold)

This folder contains a VB.NET LSP harness for running smoke + service tests
against the DWSIM workspace. It is intended for timing, navigation robustness,
and scale validation and is non-destructive (read-only).

## Usage

```powershell
test-explore\dwsim\run-tests.ps1
```

## Notes

- Uses `_external/dwsim` as the workspace root and `DWSIM.sln` as the workspace
  project path, plus `DWSIM/ApplicationEvents.vb` as the initial test file.
- Runs a DWSIM-specific service manifest (`service-tests.json`) that probes hover,
  definition, references, and symbol search without modifying the DWSIM source.
- Captures timing events (server start, solution load, didOpen) into
  `test-explore/logs/timing.jsonl` and summarizes them in the test results.



