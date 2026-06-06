#!/usr/bin/env bash
set -euo pipefail

zed_path="${1:-zed}"
extension_path="${2:-adapters/zed/vbnet-zed}"

repo_root="$(cd "$(dirname "${BASH_SOURCE[0]}")/../../../.." && pwd)"
user_data="${VBNET_ZED_USER_DATA_DIR:-${TMPDIR:-/tmp}/vbnet-zed-profile}"

if ! command -v "${zed_path}" >/dev/null 2>&1; then
  echo "Zed was not found as '${zed_path}'. Install Zed or pass the Zed binary path." >&2
  exit 1
fi

if [[ "${extension_path}" != /* ]]; then
  extension_path="${repo_root}/${extension_path}"
fi
extension_path="$(cd "${extension_path}" && pwd)"

if pgrep -x zed >/dev/null 2>&1 || pgrep -x Zed >/dev/null 2>&1; then
  echo "Zed is already running, so this helper cannot prepare an isolated --user-data-dir profile." >&2
  echo "Close existing Zed processes and rerun." >&2
  exit 1
fi

mkdir -p "${user_data}"

cat <<TEXT
Launching Zed with isolated profile: ${user_data}
Install the dev extension from: ${extension_path}
In Zed, run 'zed: install dev extension', select that directory, then close Zed.
TEXT

cd "${extension_path}"
"${zed_path}" --foreground --user-data-dir "${user_data}" "${extension_path}"

extension_index="${user_data}/extensions/index.json"
if [[ ! -f "${extension_index}" ]] || ! grep -q '"vbnet"' "${extension_index}"; then
  echo "Zed exited, but the isolated profile does not list the VB.NET extension in ${extension_index}." >&2
  exit 1
fi

echo "Zed profile is prepared for VB.NET smoke tests: ${user_data}"
