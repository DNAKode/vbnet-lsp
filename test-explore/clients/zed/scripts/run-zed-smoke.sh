#!/usr/bin/env bash
set -euo pipefail

zed_path="${1:-zed}"
workspace_path="${2:-test/TestProjects/SmallProject}"

echo "Zed smoke automation is not implemented yet." >&2
echo "Install the dev extension manually, run '${zed_path} --foreground ${workspace_path}', and follow test-explore/clients/zed/README.md." >&2
exit 1
