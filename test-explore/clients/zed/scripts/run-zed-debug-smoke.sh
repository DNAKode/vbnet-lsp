#!/usr/bin/env bash
set -euo pipefail

zed_path="${1:-zed}"
workspace_path="${2:-test-explore/clients/zed/fixtures/debug-console}"
netcoredbg_path="${VBNET_ZED_NETCOREDBG_PATH:-}"

repo_root="$(cd "$(dirname "${BASH_SOURCE[0]}")/../../../.." && pwd)"
workspace="$(cd "${repo_root}/${workspace_path}" && pwd)"
user_data="${VBNET_ZED_USER_DATA_DIR:-${TMPDIR:-/tmp}/vbnet-zed-profile}"

if ! command -v "${zed_path}" >/dev/null 2>&1; then
  echo "Zed was not found as '${zed_path}'. Install Zed or pass the Zed binary path." >&2
  exit 1
fi

dotnet build "${workspace}/DebugConsole.vbproj" -c Debug
if [[ ! -f "${workspace}/bin/Debug/net10.0/DebugConsole.dll" ]]; then
  echo "Debug fixture did not produce bin/Debug/net10.0/DebugConsole.dll." >&2
  exit 1
fi

if [[ -n "${netcoredbg_path}" ]]; then
  if [[ "${netcoredbg_path}" != /* ]]; then
    netcoredbg_path="${repo_root}/${netcoredbg_path}"
  fi
  if [[ ! -f "${netcoredbg_path}" ]]; then
    echo "netcoredbg path not found: ${netcoredbg_path}" >&2
    exit 1
  fi
elif [[ "${VBNET_ZED_SKIP_NETCOREDBG_CHECK:-}" != "1" ]] && ! command -v netcoredbg >/dev/null 2>&1; then
  echo "netcoredbg was not found on PATH. The Zed extension will use its repo-local or curated downloaded netcoredbg fallback unless VBNET_ZED_NETCOREDBG_PATH is set." >&2
fi

mkdir -p "${user_data}"
extension_index="${user_data}/extensions/index.json"
if [[ "${VBNET_ZED_SKIP_EXTENSION_INSTALL_CHECK:-}" != "1" ]]; then
  if [[ ! -f "${extension_index}" ]] || ! grep -q '"vbnet"' "${extension_index}"; then
    echo "The selected Zed profile does not list the VB.NET extension in ${extension_index}." >&2
    echo "Start Zed once with --user-data-dir ${user_data}, install adapters/zed/vbnet-zed as a dev extension, close Zed, then rerun." >&2
    exit 1
  fi
fi

if pgrep -x zed >/dev/null 2>&1 || pgrep -x Zed >/dev/null 2>&1; then
  echo "Zed is already running, so this debug smoke cannot start an isolated --user-data-dir profile." >&2
  echo "Close existing Zed processes and rerun." >&2
  exit 1
fi

cat <<TEXT
Launching Zed debug fixture: ${workspace}
Manual debug smoke steps:
  1. Run 'debugger: start'.
  2. Select 'Debug VB.NET console'.
  3. Verify netcoredbg starts, the build task runs, and debug console output includes 'from-zed'.
  4. Run task 'dotnet run DebugConsole' and verify output includes 'from-zed-task'.
  5. For attach, update processId in .zed/debug.json to a running fixture process and select 'Attach VB.NET console'.
TEXT

if [[ -n "${netcoredbg_path}" ]]; then
  echo "netcoredbg path checked for this run: ${netcoredbg_path}"
  echo "Ensure Zed's netcoredbg adapter path is configured to that binary before starting the debug session."
else
  echo "netcoredbg path: extension default resolution (repo-local, curated download, then PATH)."
fi

cd "${workspace}"
exec "${zed_path}" --foreground --user-data-dir "${user_data}" "${workspace}"
