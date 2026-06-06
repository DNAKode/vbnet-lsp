#!/usr/bin/env bash
set -euo pipefail

zed_path="${1:-zed}"
workspace_path="${2:-test-explore/clients/zed/fixtures/single-file}"
require_ui="${3:-false}"

"$(dirname "${BASH_SOURCE[0]}")/run-zed-smoke.sh" "${zed_path}" "${workspace_path}"

if [[ "${require_ui}" == "true" ]]; then
  echo "Zed UI automation requires a stable command or OS automation harness. The probe smoke passed, but hover/completion/debug UI assertions were not executed." >&2
  exit 1
fi

echo "Zed probe smoke passed. UI assertions are skipped because no stable Zed UI automation path is configured."
