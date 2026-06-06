#!/usr/bin/env bash
set -euo pipefail

zed_path="${1:-zed}"
workspace_path="${2:-test-explore/clients/zed/fixtures/single-file}"
timeout_seconds="${3:-25}"
zed_extra_args=("${@:4}")
mode="${VBNET_ZED_SMOKE_MODE:-probe}"
local_server_path="${VBNET_ZED_LOCAL_SERVER_PATH:-}"

repo_root="$(cd "$(dirname "${BASH_SOURCE[0]}")/../../../.." && pwd)"
workspace="$(cd "${repo_root}/${workspace_path}" && pwd)"
logs="${repo_root}/test-explore/clients/zed/logs"
user_data="${VBNET_ZED_USER_DATA_DIR:-}"
if [[ -z "${user_data}" ]]; then
  user_data="$(mktemp -d "${TMPDIR:-/tmp}/vbnet-zed-profile.XXXXXX")"
fi
smoke_workspace="${workspace}"
if [[ "${VBNET_ZED_USE_FIXTURE_SETTINGS:-}" != "1" ]]; then
  smoke_workspace="$(mktemp -d "${TMPDIR:-/tmp}/vbnet-zed-smoke-workspace.XXXXXX")"
  cp -R "${workspace}/." "${smoke_workspace}/"
  mkdir -p "${smoke_workspace}/.zed"
  probe_project="${repo_root}/test-explore/clients/zed/probes/lsp-probe/VbNet.Zed.LspProbe.csproj"
  if [[ "${mode}" == "real-server" ]]; then
    if [[ -z "${local_server_path}" ]]; then
      if [[ -f "${repo_root}/src/VbNet.LanguageServer.Vb/bin/Debug/net10.0/VbNet.LanguageServer" ]]; then
        local_server_path="${repo_root}/src/VbNet.LanguageServer.Vb/bin/Debug/net10.0/VbNet.LanguageServer"
      elif [[ -f "${repo_root}/src/VbNet.LanguageServer.Vb/bin/Debug/net10.0/VbNet.LanguageServer.exe" ]]; then
        local_server_path="${repo_root}/src/VbNet.LanguageServer.Vb/bin/Debug/net10.0/VbNet.LanguageServer.exe"
      elif [[ -f "${repo_root}/src/VbNet.LanguageServer.Vb/bin/Debug/net10.0/VbNet.LanguageServer.dll" ]]; then
        local_server_path="${repo_root}/src/VbNet.LanguageServer.Vb/bin/Debug/net10.0/VbNet.LanguageServer.dll"
      else
        echo "Local VB.NET language server was not found. Build src/VbNet.LanguageServer.Vb/VbNet.LanguageServer.Vb.vbproj -c Debug or set VBNET_ZED_LOCAL_SERVER_PATH." >&2
        exit 1
      fi
    fi
    if [[ ! -f "${local_server_path}" ]]; then
      echo "Local server path not found: ${local_server_path}" >&2
      exit 1
    fi
  fi
  solution_path=""
  project_path=""
  if [[ -f "${workspace}/.zed/settings.json" ]]; then
    solution_path="$(sed -n 's/.*"solutionPath"[[:space:]]*:[[:space:]]*"\([^"]*\)".*/\1/p' "${workspace}/.zed/settings.json" | head -n 1)"
    project_path="$(sed -n 's/.*"projectPath"[[:space:]]*:[[:space:]]*"\([^"]*\)".*/\1/p' "${workspace}/.zed/settings.json" | head -n 1)"
  fi
  workspace_settings=""
  if [[ -n "${solution_path}" ]]; then
    workspace_settings=', "workspace": { "solutionPath": "'"${solution_path}"'" }'
  elif [[ -n "${project_path}" ]]; then
    workspace_settings=', "workspace": { "projectPath": "'"${project_path}"'" }'
  fi
  if [[ "${mode}" == "real-server" ]]; then
    real_server_log="${smoke_workspace}/zed-real-server.stderr.log"
    launcher_path="${smoke_workspace}/.zed/vbnet-real-server-launch.sh"
    if [[ "${local_server_path}" == *.dll ]]; then
      cat >"${launcher_path}" <<SH
#!/usr/bin/env bash
exec dotnet '${local_server_path}' "\$@" 2>>'${real_server_log}'
SH
    else
      cat >"${launcher_path}" <<SH
#!/usr/bin/env bash
exec '${local_server_path}' "\$@" 2>>'${real_server_log}'
SH
    fi
    chmod +x "${launcher_path}"
    server_path="${launcher_path}"
    server_args='"--stdio", "--logLevel", "Debug"'
    server_env='"VBNET_LS_TRACE_TRANSPORT": "1"'
  else
    server_path="dotnet"
    server_args='"run", "--project", "'"${probe_project}"'"'
    server_env='"VBNET_ZED_TEST_LOG": "zed-lsp-probe.jsonl"'
  fi

  cat >"${smoke_workspace}/.zed/settings.json" <<JSON
{
  "languages": {
    "VB.NET": {
      "language_servers": ["vbnet-ls"]
    }
  },
  "lsp": {
    "vbnet-ls": {
      "binary": {
        "path": "${server_path}",
        "arguments": [${server_args}],
        "env": {
          ${server_env}
        }
      },
      "initialization_options": {
        "semanticTokens": true${workspace_settings}
      }
    }
  }
}
JSON
fi
probe_log="${smoke_workspace}/zed-lsp-probe.jsonl"

if ! command -v "${zed_path}" >/dev/null 2>&1; then
  echo "Zed was not found as '${zed_path}'. Install Zed or pass the Zed binary path." >&2
  exit 1
fi

mkdir -p "${logs}"
if [[ "${mode}" != "real-server" ]]; then
  rm -f "${probe_log}"
fi

extension_index="${user_data}/extensions/index.json"
if [[ "${VBNET_ZED_SKIP_EXTENSION_INSTALL_CHECK:-}" != "1" ]]; then
  if [[ ! -f "${extension_index}" ]]; then
    echo "The selected Zed profile does not have an extensions index: ${extension_index}" >&2
    echo "Start Zed once with --user-data-dir ${user_data}, install the VB.NET dev extension from adapters/zed/vbnet-zed, close Zed, then rerun this script with VBNET_ZED_USER_DATA_DIR=${user_data}." >&2
    echo "Set VBNET_ZED_SKIP_EXTENSION_INSTALL_CHECK=1 only when intentionally debugging profile bootstrap." >&2
    exit 1
  fi

  if ! grep -q '"vbnet"' "${extension_index}"; then
    echo "The selected Zed profile does not list the VB.NET extension in ${extension_index}." >&2
    echo "Install the VB.NET dev extension from adapters/zed/vbnet-zed in that profile, close Zed, then rerun this script." >&2
    exit 1
  fi
fi

if pgrep -x zed >/dev/null 2>&1 || pgrep -x Zed >/dev/null 2>&1; then
  echo "Zed is already running, so this smoke test cannot start an isolated --user-data-dir profile." >&2
  echo "Close existing Zed processes and rerun." >&2
  exit 1
fi

(
  cd "${smoke_workspace}"
  "${zed_path}" --foreground --user-data-dir "${user_data}" "${zed_extra_args[@]}" "${smoke_workspace}" >"${logs}/zed-smoke.stdout.log" 2>"${logs}/zed-smoke.stderr.log"
) &
zed_pid=$!

deadline=$((SECONDS + timeout_seconds))
while (( SECONDS < deadline )); do
  if [[ "${mode}" != "real-server" ]] && [[ -f "${probe_log}" ]] && grep -q '"method":"textDocument/didOpen"' "${probe_log}"; then
    break
  fi
  sleep 0.5
done

if kill -0 "${zed_pid}" >/dev/null 2>&1; then
  kill "${zed_pid}" >/dev/null 2>&1 || true
  wait "${zed_pid}" >/dev/null 2>&1 || true
fi

if grep -q 'zed is already running' "${logs}/zed-smoke.stdout.log" "${logs}/zed-smoke.stderr.log" 2>/dev/null; then
  echo "Zed reported that another instance is already running, so the temporary profile was not used." >&2
  echo "Close existing Zed processes and rerun. Logs: ${logs}; user data: ${user_data}" >&2
  exit 1
fi

if [[ -d "${user_data}/logs" ]]; then
  find "${user_data}/logs" -maxdepth 1 -type f -name '*.log' -exec cp -f {} "${logs}/" \;
fi

if grep -Eqi 'failed to start language server|language server.*exited|extension panic|WebAssembly.*failed|Could not find VB.NET language server|Unhandled exception|panic' "${logs}/zed-smoke.stdout.log" "${logs}/zed-smoke.stderr.log" "${logs}"/*.log 2>/dev/null; then
  echo "Zed smoke saw a startup failure pattern. Logs: ${logs}; user data: ${user_data}; workspace: ${smoke_workspace}" >&2
  exit 1
fi

if [[ "${mode}" == "real-server" ]]; then
  real_server_log="${smoke_workspace}/zed-real-server.stderr.log"
  if [[ ! -f "${real_server_log}" ]]; then
    echo "Real server stderr log was not created, so Zed did not start the configured VB.NET language server." >&2
    echo "Logs: ${logs}; user data: ${user_data}; workspace: ${smoke_workspace}" >&2
    exit 1
  fi

  if ! grep -q 'VB.NET Language Server' "${real_server_log}"; then
    echo "Real server stderr log does not contain the VB.NET startup banner. Server log: ${real_server_log}" >&2
    exit 1
  fi

  echo "Zed real-server smoke completed launch window. Local server: ${local_server_path}"
  echo "Real server stderr log: ${real_server_log}"
  echo "Zed stdout/stderr and copied Zed.log files: ${logs}"
elif [[ ! -f "${probe_log}" ]]; then
  echo "Probe log was not created. Ensure the VB.NET dev extension is installed in the selected Zed profile." >&2
  echo "Logs: ${logs}; user data: ${user_data}; workspace: ${smoke_workspace}" >&2
  exit 1
else
  grep -q '"method":"initialize"' "${probe_log}"
  grep -q '"method":"textDocument/didOpen"' "${probe_log}"

  echo "Zed smoke passed. Probe log: ${probe_log}"
  echo "Zed stdout/stderr and copied Zed.log files: ${logs}"
fi

if [[ "${smoke_workspace}" != "${workspace}" ]]; then
  if [[ "${VBNET_ZED_KEEP_SMOKE_WORKSPACE:-}" == "1" ]]; then
    echo "Zed smoke workspace retained: ${smoke_workspace}"
  else
    rm -rf "${smoke_workspace}"
  fi
fi
