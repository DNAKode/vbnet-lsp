use zed_extension_api as zed;

use crate::{DEBUG_ADAPTER_ID, DEBUG_LOCATOR_ID, platform};

pub(crate) fn get_dap_binary(
    adapter_name: String,
    config: zed::DebugTaskDefinition,
    user_provided_debug_adapter_path: Option<String>,
    worktree: &zed::Worktree,
) -> zed::Result<zed::DebugAdapterBinary> {
    ensure_adapter(&adapter_name)?;

    let command = user_provided_debug_adapter_path
        .or_else(|| worktree.which(platform::debug_adapter_binary_name()))
        .ok_or_else(missing_debug_adapter_message)?;

    let request = dap_request_kind(adapter_name, parse_config(&config.config)?)?;
    let connection = match config.tcp_connection {
        Some(template) => Some(zed::resolve_tcp_template(template)?),
        None => None,
    };

    Ok(zed::DebugAdapterBinary {
        command: Some(command),
        arguments: vec!["--interpreter=vscode".to_string()],
        envs: worktree.shell_env(),
        cwd: None,
        connection,
        request_args: zed::StartDebuggingRequestArguments {
            configuration: config.config,
            request,
        },
    })
}

pub(crate) fn dap_request_kind(
    adapter_name: String,
    config: serde_json::Value,
) -> zed::Result<zed::StartDebuggingRequestArgumentsRequest> {
    ensure_adapter(&adapter_name)?;

    match config.get("request").and_then(|value| value.as_str()) {
        Some("launch") => Ok(zed::StartDebuggingRequestArgumentsRequest::Launch),
        Some("attach") => Ok(zed::StartDebuggingRequestArgumentsRequest::Attach),
        Some(request) => Err(format!("Unsupported VB.NET debug request '{request}'.")),
        None => Err(
            "VB.NET debug configuration must include request = 'launch' or 'attach'.".to_string(),
        ),
    }
}

pub(crate) fn dap_config_to_scenario(config: zed::DebugConfig) -> zed::Result<zed::DebugScenario> {
    ensure_adapter(&config.adapter)?;

    let (request, mut value) = match config.request {
        zed::DebugRequest::Launch(launch) => {
            let mut value = serde_json::json!({
                "type": DEBUG_ADAPTER_ID,
                "request": "launch",
                "program": launch.program,
                "args": launch.args,
                "env": env_to_object(launch.envs),
            });

            if let Some(cwd) = launch.cwd {
                value["cwd"] = serde_json::Value::String(cwd);
            }

            (zed::StartDebuggingRequestArgumentsRequest::Launch, value)
        }
        zed::DebugRequest::Attach(attach) => {
            let mut value = serde_json::json!({
                "type": DEBUG_ADAPTER_ID,
                "request": "attach",
            });

            if let Some(process_id) = attach.process_id {
                value["processId"] = serde_json::Value::Number(process_id.into());
            }

            (zed::StartDebuggingRequestArgumentsRequest::Attach, value)
        }
    };

    if let Some(stop_on_entry) = config.stop_on_entry {
        value["stopAtEntry"] = serde_json::Value::Bool(stop_on_entry);
    }

    value["name"] = serde_json::Value::String(config.label.clone());

    let request_name = match request {
        zed::StartDebuggingRequestArgumentsRequest::Launch => "launch",
        zed::StartDebuggingRequestArgumentsRequest::Attach => "attach",
    };
    value["request"] = serde_json::Value::String(request_name.to_string());

    Ok(zed::DebugScenario {
        label: config.label,
        adapter: DEBUG_ADAPTER_ID.to_string(),
        build: None,
        config: serde_json::to_string(&value).map_err(|err| err.to_string())?,
        tcp_connection: None,
    })
}

pub(crate) fn dap_locator_create_scenario(
    locator_name: String,
    build_task: zed::TaskTemplate,
    resolved_label: String,
    debug_adapter_name: String,
) -> Option<zed::DebugScenario> {
    if locator_name != DEBUG_LOCATOR_ID || debug_adapter_name != DEBUG_ADAPTER_ID {
        return None;
    }

    let command = build_task.command.to_ascii_lowercase();
    if command != "dotnet" && !command.ends_with("/dotnet") && !command.ends_with("\\dotnet.exe") {
        return None;
    }

    if !build_task
        .args
        .iter()
        .any(|arg| arg == "run" || arg == "build")
    {
        return None;
    }

    let cwd = build_task.cwd.clone().unwrap_or_else(|| ".".to_string());
    let config = serde_json::json!({
        "type": DEBUG_ADAPTER_ID,
        "request": "launch",
        "projectPath": cwd,
        "cwd": cwd,
        "args": [],
        "stopAtEntry": false
    });

    Some(zed::DebugScenario {
        label: format!("Debug {resolved_label}"),
        adapter: DEBUG_ADAPTER_ID.to_string(),
        build: Some(zed::BuildTaskDefinition::Template(
            zed::BuildTaskDefinitionTemplatePayload {
                locator_name: Some(DEBUG_LOCATOR_ID.to_string()),
                template: build_task,
            },
        )),
        config: serde_json::to_string(&config).ok()?,
        tcp_connection: None,
    })
}

fn ensure_adapter(adapter_name: &str) -> zed::Result<()> {
    if adapter_name != DEBUG_ADAPTER_ID {
        return Err(format!(
            "Unsupported debug adapter '{adapter_name}'. Expected '{DEBUG_ADAPTER_ID}'."
        ));
    }

    Ok(())
}

fn parse_config(config: &str) -> zed::Result<serde_json::Value> {
    serde_json::from_str(config)
        .map_err(|err| format!("Failed to parse VB.NET debug config: {err}"))
}

fn env_to_object(envs: Vec<(String, String)>) -> serde_json::Value {
    let mut object = serde_json::Map::new();
    for (key, value) in envs {
        object.insert(key, serde_json::Value::String(value));
    }
    serde_json::Value::Object(object)
}

fn missing_debug_adapter_message() -> String {
    let binary = platform::debug_adapter_binary_name();
    format!(
        "Could not find netcoredbg. Install '{binary}' on PATH, or configure the debug adapter path in the Zed debug task."
    )
}

#[cfg(test)]
mod tests {
    use super::*;

    #[test]
    fn request_kind_reads_launch() {
        let kind = dap_request_kind(
            DEBUG_ADAPTER_ID.to_string(),
            serde_json::json!({ "request": "launch" }),
        )
        .unwrap();
        assert_eq!(kind, zed::StartDebuggingRequestArgumentsRequest::Launch);
    }

    #[test]
    fn request_kind_rejects_unknown_adapter() {
        let error = dap_request_kind(
            "other".to_string(),
            serde_json::json!({ "request": "launch" }),
        )
        .unwrap_err();
        assert!(error.contains("Unsupported debug adapter"));
    }
}
