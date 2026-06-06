use std::path::{Path, PathBuf};

use zed_extension_api as zed;

use crate::{DEBUG_ADAPTER_ID, DEBUG_LOCATOR_ID, platform};

const NETCOREDBG_VERSION: &str = "3.1.3-1062";

pub(crate) fn get_dap_binary(
    adapter_name: String,
    config: zed::DebugTaskDefinition,
    user_provided_debug_adapter_path: Option<String>,
    worktree: &zed::Worktree,
) -> zed::Result<zed::DebugAdapterBinary> {
    ensure_adapter(&adapter_name)?;

    let command = user_provided_debug_adapter_path
        .or_else(|| find_local_dev_debug_adapter(&worktree.root_path()))
        .or_else(|| download_debug_adapter().ok().flatten())
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

pub(crate) fn run_dap_locator(
    locator_name: String,
    build_task: zed::TaskTemplate,
) -> zed::Result<zed::DebugRequest> {
    if locator_name != DEBUG_LOCATOR_ID {
        return Err(format!(
            "Unsupported debug locator '{locator_name}'. Expected '{DEBUG_LOCATOR_ID}'."
        ));
    }

    let cwd = build_task.cwd.clone().unwrap_or_else(|| ".".to_string());
    let project_path = project_path_from_dotnet_args(&build_task.args)
        .map(|path| absolutize_path(&cwd, &path))
        .or_else(|| single_vb_project_in(&cwd))
        .ok_or_else(|| {
            format!(
                "Could not infer a VB.NET project from debug task '{}'. Add an explicit program path to the Zed debug configuration.",
                build_task.label
            )
        })?;

    let program = find_debug_program_for_project(&project_path).ok_or_else(|| {
        format!(
            "Could not find a built debug target for '{}'. Build the project first or add an explicit program path.",
            project_path.display()
        )
    })?;

    Ok(zed::DebugRequest::Launch(zed::LaunchRequest {
        program: path_to_string(program),
        cwd: project_path.parent().map(path_to_string_ref),
        args: Vec::new(),
        envs: build_task.env,
    }))
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
        "Could not find netcoredbg. Install '{binary}' on PATH, configure the debug adapter path in the Zed debug task, use a repo checkout with _external/netcoredbg/bin/{binary}, or use a platform supported by the curated netcoredbg downloads."
    )
}

fn download_debug_adapter() -> zed::Result<Option<String>> {
    let Some(url) = platform::debug_adapter_asset_url() else {
        return Ok(None);
    };

    let install_dir = debug_adapter_install_dir(url);
    if let Some(debugger) = find_debug_adapter_under(&PathBuf::from(&install_dir)) {
        return Ok(Some(absolutize_extension_path(debugger)));
    }

    zed::download_file(url, &install_dir, download_type_for_url(url))?;

    let debugger = find_debug_adapter_under(&PathBuf::from(&install_dir)).ok_or_else(|| {
        format!(
            "Downloaded netcoredbg from {url}, but no {} executable was found under '{install_dir}'.",
            platform::debug_adapter_binary_name()
        )
    })?;
    let debugger = absolutize_extension_path(debugger);

    if !debugger.ends_with(".exe") {
        zed::make_file_executable(&debugger)?;
    }

    Ok(Some(debugger))
}

fn debug_adapter_install_dir(url: &str) -> String {
    let asset_name = url.rsplit('/').next().unwrap_or("netcoredbg");
    let asset_stem = asset_name
        .strip_suffix(".tar.gz")
        .or_else(|| asset_name.strip_suffix(".zip"))
        .unwrap_or(asset_name);
    format!("debug-adapter/netcoredbg/{NETCOREDBG_VERSION}/{asset_stem}")
}

fn download_type_for_url(url: &str) -> zed::DownloadedFileType {
    if url.ends_with(".tar.gz") {
        zed::DownloadedFileType::GzipTar
    } else if url.ends_with(".zip") {
        zed::DownloadedFileType::Zip
    } else {
        zed::DownloadedFileType::Uncompressed
    }
}

fn find_debug_adapter_under(root: &Path) -> Option<String> {
    find_file_under(root, platform::debug_adapter_binary_name()).map(path_to_string)
}

fn absolutize_extension_path(path: String) -> String {
    let path = PathBuf::from(path);
    if path.is_absolute() {
        return path_to_string(path);
    }

    std::env::current_dir()
        .map(|current_dir| path_to_string(current_dir.join(&path)))
        .unwrap_or_else(|_| path_to_string(path))
}

fn find_file_under(root: &Path, file_name: &str) -> Option<PathBuf> {
    for entry in std::fs::read_dir(root).ok()? {
        let path = entry.ok()?.path();
        if path.is_dir() {
            if let Some(found) = find_file_under(&path, file_name) {
                return Some(found);
            }
        } else if path.file_name().and_then(|name| name.to_str()) == Some(file_name) {
            return Some(path);
        }
    }

    None
}

fn find_local_dev_debug_adapter(worktree_root: &str) -> Option<String> {
    find_local_dev_debug_adapter_with_binary(worktree_root, platform::debug_adapter_binary_name())
}

fn find_local_dev_debug_adapter_with_binary(worktree_root: &str, binary: &str) -> Option<String> {
    let mut current = PathBuf::from(worktree_root);

    loop {
        for relative in [
            PathBuf::from("_external")
                .join("netcoredbg")
                .join("bin")
                .join(binary),
            PathBuf::from("_external").join("bin").join(binary),
        ] {
            let candidate = current.join(relative);
            if candidate.is_file() {
                return Some(path_to_string(candidate));
            }
        }

        if !current.pop() {
            break;
        }
    }

    None
}

fn project_path_from_dotnet_args(args: &[String]) -> Option<String> {
    for (index, arg) in args.iter().enumerate() {
        if arg == "--project" || arg == "-p" {
            return args.get(index + 1).cloned();
        }

        if let Some(value) = arg.strip_prefix("--project=") {
            return Some(value.to_string());
        }

        if arg.ends_with(".vbproj") {
            return Some(arg.clone());
        }
    }

    None
}

fn absolutize_path(cwd: &str, path: &str) -> PathBuf {
    let path = PathBuf::from(path);
    if path.is_absolute() {
        path
    } else {
        PathBuf::from(cwd).join(path)
    }
}

fn single_vb_project_in(cwd: &str) -> Option<PathBuf> {
    let mut projects = Vec::new();
    for entry in std::fs::read_dir(cwd).ok()? {
        let path = entry.ok()?.path();
        if path.extension().and_then(|extension| extension.to_str()) == Some("vbproj") {
            projects.push(path);
        }
    }

    if projects.len() == 1 {
        projects.pop()
    } else {
        None
    }
}

fn find_debug_program_for_project(project_path: &Path) -> Option<PathBuf> {
    let project_dir = project_path.parent()?;
    let assembly_name = project_path.file_stem()?.to_str()?;

    for configuration in ["Debug", "Release"] {
        let configuration_dir = project_dir.join("bin").join(configuration);
        let Some(program) = find_assembly_under(&configuration_dir, assembly_name) else {
            continue;
        };

        return Some(program);
    }

    None
}

fn find_assembly_under(root: &Path, assembly_name: &str) -> Option<PathBuf> {
    for entry in std::fs::read_dir(root).ok()? {
        let path = entry.ok()?.path();
        if path.is_dir() {
            if let Some(found) = find_assembly_under(&path, assembly_name) {
                return Some(found);
            }
        } else if path.file_name().and_then(|name| name.to_str())
            == Some(&format!("{assembly_name}.dll"))
        {
            return Some(path);
        }
    }

    None
}

fn path_to_string(path: PathBuf) -> String {
    path.to_string_lossy().into_owned()
}

fn path_to_string_ref(path: &Path) -> String {
    path.to_string_lossy().into_owned()
}

#[cfg(test)]
mod tests {
    use super::*;
    use std::fs;
    use std::time::{SystemTime, UNIX_EPOCH};

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

    #[test]
    fn project_path_reads_dotnet_project_argument() {
        assert_eq!(
            project_path_from_dotnet_args(&[
                "build".to_string(),
                "--project".to_string(),
                "App.vbproj".to_string()
            ]),
            Some("App.vbproj".to_string())
        );
        assert_eq!(
            project_path_from_dotnet_args(&["run".to_string(), "--project=App.vbproj".to_string()]),
            Some("App.vbproj".to_string())
        );
    }

    #[test]
    fn absolutize_keeps_absolute_paths() {
        let path = absolutize_path(r"C:\repo", r"C:\repo\App.vbproj");
        assert_eq!(path, PathBuf::from(r"C:\repo\App.vbproj"));
    }

    #[test]
    fn run_dap_locator_finds_built_debug_dll() {
        let root = temp_test_dir("built-debug-dll");
        let project = root.join("DebugConsole.vbproj");
        let program = root
            .join("bin")
            .join("Debug")
            .join("net10.0")
            .join("DebugConsole.dll");

        fs::create_dir_all(program.parent().unwrap()).unwrap();
        fs::write(&project, "<Project />").unwrap();
        fs::write(&program, "").unwrap();

        let request = run_dap_locator(
            DEBUG_LOCATOR_ID.to_string(),
            zed::TaskTemplate {
                label: "dotnet build DebugConsole".to_string(),
                command: "dotnet".to_string(),
                args: vec![
                    "build".to_string(),
                    "--project".to_string(),
                    path_to_string(project.clone()),
                ],
                env: Vec::new(),
                cwd: Some(path_to_string(root.clone())),
            },
        )
        .unwrap();

        match request {
            zed::DebugRequest::Launch(launch) => {
                assert_eq!(PathBuf::from(launch.program), program);
                assert_eq!(launch.cwd.map(PathBuf::from), Some(root.clone()));
            }
            zed::DebugRequest::Attach(_) => panic!("expected launch request"),
        }

        remove_temp_dir(root);
    }

    #[test]
    fn run_dap_locator_reports_missing_build_output() {
        let root = temp_test_dir("missing-build-output");
        let project = root.join("DebugConsole.vbproj");
        fs::write(&project, "<Project />").unwrap();

        let error = run_dap_locator(
            DEBUG_LOCATOR_ID.to_string(),
            zed::TaskTemplate {
                label: "dotnet build DebugConsole".to_string(),
                command: "dotnet".to_string(),
                args: vec!["build".to_string(), path_to_string(project.clone())],
                env: Vec::new(),
                cwd: Some(path_to_string(root.clone())),
            },
        )
        .unwrap_err();

        assert!(error.contains("Could not find a built debug target"));
        assert!(error.contains("Build the project first"));

        remove_temp_dir(root);
    }

    #[test]
    fn local_dev_debug_adapter_searches_repo_external_bins() {
        let root = temp_test_dir("netcoredbg");
        let workspace = root.join("test-explore").join("clients").join("zed");
        let debugger_dir = root.join("_external").join("netcoredbg").join("bin");
        fs::create_dir_all(&workspace).unwrap();
        fs::create_dir_all(&debugger_dir).unwrap();

        let debugger = debugger_dir.join("netcoredbg.exe");
        fs::write(&debugger, "").unwrap();

        assert_eq!(
            find_local_dev_debug_adapter_with_binary(&path_to_string(workspace), "netcoredbg.exe"),
            Some(path_to_string(debugger))
        );

        remove_temp_dir(root);
    }

    #[test]
    fn debug_adapter_install_dir_removes_archive_suffix() {
        assert_eq!(
            debug_adapter_install_dir("https://example.test/netcoredbg-win64.zip"),
            "debug-adapter/netcoredbg/3.1.3-1062/netcoredbg-win64"
        );
        assert_eq!(
            debug_adapter_install_dir("https://example.test/netcoredbg-linux-amd64.tar.gz"),
            "debug-adapter/netcoredbg/3.1.3-1062/netcoredbg-linux-amd64"
        );
    }

    #[test]
    fn find_file_under_searches_extracted_debugger_tree() {
        let root = temp_test_dir("find-netcoredbg");
        let debugger_dir = root.join("nested").join("bin");
        fs::create_dir_all(&debugger_dir).unwrap();
        let debugger = debugger_dir.join("netcoredbg.exe");
        fs::write(&debugger, "").unwrap();

        assert_eq!(
            find_file_under(&root, "netcoredbg.exe"),
            Some(debugger.clone())
        );

        remove_temp_dir(root);
    }

    #[test]
    fn absolutize_extension_path_resolves_relative_downloads() {
        let current_dir = std::env::current_dir().unwrap();
        let path = absolutize_extension_path(
            PathBuf::from("debug-adapter")
                .join("netcoredbg")
                .join("netcoredbg.exe")
                .to_string_lossy()
                .into_owned(),
        );

        assert_eq!(
            path,
            path_to_string(
                current_dir
                    .join("debug-adapter")
                    .join("netcoredbg")
                    .join("netcoredbg.exe")
            )
        );
    }

    fn temp_test_dir(name: &str) -> PathBuf {
        let unique = SystemTime::now()
            .duration_since(UNIX_EPOCH)
            .unwrap()
            .as_nanos();
        let dir = std::env::temp_dir().join(format!("vbnet-zed-{name}-{unique}"));
        fs::create_dir_all(&dir).unwrap();
        dir
    }

    fn remove_temp_dir(path: PathBuf) {
        let _ = fs::remove_dir_all(path);
    }
}
