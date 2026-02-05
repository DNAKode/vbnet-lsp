# test-explore

Exploratory, headless, and multi-environment harnesses for `VB.NET` language support. These are **not** CI tests and may depend on VS Code, WSL, or external tooling. Keep the harness scripts and fixtures tracked; keep runtime artifacts out of git.

## What belongs here

- Headless VS Code runs and keystroke injection
- WSL/Linux/macOS exploratory scripts
- DWSIM and large-solution exploration harnesses
- Fuzz and stress scripts

## What does NOT belong here

- CI/unit/integration tests (those live under `test/`)
- Build outputs, logs, or downloaded runtimes

## Log retention policy

- Keep only the most recent **5** log bundles per harness (VS Code, Emacs, LSP smoke) and any logs explicitly referenced in `TEST_RESULTS.md`.
- Summarize older runs in `TEST_RESULTS.md` and delete their logs.
- Logs live under `test-explore/**/logs` and are intentionally gitignored.

## Quick entry points

- `test-explore/run-tests.ps1` (supports themes: `core`, `editors`, `scale`, `all`)
- `test-explore/vbnet-lsp/run-tests.ps1`
- `test-explore/clients/vscode/README.md`
- `test-explore/clients/emacs/README.md`
- `test-explore/clients/nvim/README.md`
- `test-explore/clients/helix/README.md`

## Results

- Record exploratory outcomes in `test-explore/TEST_RESULTS.md`.

