#!/usr/bin/env bash
set -euo pipefail

zed_path="${1:-zed}"
workspace_path="${2:-test/TestProjects/SmallProject}"

echo "Zed UI smoke automation is not implemented yet." >&2
echo "Use this entry point when a stable UI automation harness is selected for '${zed_path}' and '${workspace_path}'." >&2
exit 1
