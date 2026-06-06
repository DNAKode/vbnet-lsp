use std::collections::HashMap;
use std::path::{Path, PathBuf};

use zed::settings::LspSettings;
use zed_extension_api as zed;

use crate::{LANGUAGE_SERVER_ID, platform, workspace};

const SERVER_REPOSITORY: &str = "DNAKode/vbnet-lsp";
const SERVER_VERSION: &str = concat!("v", env!("CARGO_PKG_VERSION"));

pub(crate) fn language_server_command(
    language_server_id: &zed::LanguageServerId,
    worktree: &zed::Worktree,
) -> zed::Result<zed::Command> {
    ensure_language_server(language_server_id)?;

    let settings = LspSettings::for_worktree(LANGUAGE_SERVER_ID, worktree).unwrap_or_default();
    let mut args = Vec::new();
    let mut env = worktree.shell_env();

    let mut command = if let Some(binary) = settings.binary {
        if let Some(arguments) = binary.arguments {
            args.extend(arguments);
        } else {
            args.push("--stdio".to_string());
        }

        if let Some(extra_env) = binary.env {
            merge_env(&mut env, extra_env);
        }

        binary
            .path
            .or_else(|| find_server_on_path(worktree))
            .map(ResolvedServer::Direct)
    } else {
        args.push("--stdio".to_string());
        find_server_on_path(worktree).map(ResolvedServer::Direct)
    };

    if command.is_none() {
        command = match download_release_server(language_server_id) {
            Ok(downloaded) => downloaded,
            Err(err) => {
                zed::set_language_server_installation_status(
                    language_server_id,
                    &zed::LanguageServerInstallationStatus::Failed(err.clone()),
                );
                return Err(err);
            }
        };
    }

    let command = command.ok_or_else(missing_server_message)?;

    let command = match command {
        ResolvedServer::Direct(command) => command,
        ResolvedServer::DotnetDll(dll_path) => {
            args.insert(0, dll_path);
            "dotnet".to_string()
        }
    };

    zed::set_language_server_installation_status(
        language_server_id,
        &zed::LanguageServerInstallationStatus::None,
    );

    Ok(zed::Command { command, args, env })
}

pub(crate) fn language_server_initialization_options(
    language_server_id: &zed::LanguageServerId,
    worktree: &zed::Worktree,
) -> zed::Result<Option<serde_json::Value>> {
    ensure_language_server(language_server_id)?;

    let settings = LspSettings::for_worktree(LANGUAGE_SERVER_ID, worktree).unwrap_or_default();
    Ok(settings.initialization_options)
}

pub(crate) fn language_server_workspace_configuration(
    language_server_id: &zed::LanguageServerId,
    worktree: &zed::Worktree,
) -> zed::Result<Option<serde_json::Value>> {
    ensure_language_server(language_server_id)?;

    let settings = LspSettings::for_worktree(LANGUAGE_SERVER_ID, worktree).unwrap_or_default();
    let mut configuration = settings.settings.unwrap_or_else(|| serde_json::json!({}));

    if let Some(object) = configuration.as_object_mut() {
        object
            .entry("workspace".to_string())
            .or_insert_with(|| serde_json::json!({}));

        if let Some(workspace_object) = object
            .get_mut("workspace")
            .and_then(|value| value.as_object_mut())
        {
            if let Some(solution_path) = workspace::preferred_solution_path(worktree) {
                workspace_object
                    .entry("solutionPath".to_string())
                    .or_insert_with(|| serde_json::Value::String(solution_path));
            }
        }
    }

    Ok(Some(configuration))
}

fn ensure_language_server(language_server_id: &zed::LanguageServerId) -> zed::Result<()> {
    if language_server_id.as_ref() != LANGUAGE_SERVER_ID {
        return Err(format!(
            "Unsupported language server '{language_server_id}'. Expected '{LANGUAGE_SERVER_ID}'."
        ));
    }

    Ok(())
}

fn find_server_on_path(worktree: &zed::Worktree) -> Option<String> {
    worktree
        .which(platform::language_server_binary_name())
        .or_else(|| worktree.which(platform::language_server_fallback_binary_name()))
}

enum ResolvedServer {
    Direct(String),
    DotnetDll(String),
}

fn download_release_server(
    language_server_id: &zed::LanguageServerId,
) -> zed::Result<Option<ResolvedServer>> {
    let Some(asset_name) = platform::release_asset_name() else {
        return Ok(None);
    };

    let install_dir = release_install_dir(SERVER_VERSION, asset_name);
    if let Some(server) = server_from_install_dir(&install_dir) {
        return Ok(Some(server));
    }

    let release = zed::github_release_by_tag_name(SERVER_REPOSITORY, SERVER_VERSION)?;
    let asset = release
        .assets
        .iter()
        .find(|asset| asset.name == asset_name)
        .ok_or_else(|| {
            format!(
                "Release {SERVER_VERSION} in {SERVER_REPOSITORY} does not contain required asset '{asset_name}'."
            )
        })?;

    zed::set_language_server_installation_status(
        language_server_id,
        &zed::LanguageServerInstallationStatus::Downloading,
    );

    zed::download_file(
        &asset.download_url,
        &install_dir,
        download_type_for_asset(asset_name),
    )?;

    let server = server_from_install_dir(&install_dir).ok_or_else(|| {
        format!(
            "Downloaded '{asset_name}' from {SERVER_REPOSITORY} {SERVER_VERSION}, but no VbNet.LanguageServer executable or DLL was found in '{install_dir}'."
        )
    })?;

    if let ResolvedServer::Direct(command) = &server {
        if !command.ends_with(".exe") {
            zed::make_file_executable(command)?;
        }
    }

    Ok(Some(server))
}

fn release_install_dir(version: &str, asset_name: &str) -> String {
    let asset_stem = asset_name
        .strip_suffix(".tar.gz")
        .or_else(|| asset_name.strip_suffix(".zip"))
        .unwrap_or(asset_name);
    format!("language-server/{version}/{asset_stem}")
}

fn download_type_for_asset(asset_name: &str) -> zed::DownloadedFileType {
    if asset_name.ends_with(".tar.gz") {
        zed::DownloadedFileType::GzipTar
    } else if asset_name.ends_with(".zip") {
        zed::DownloadedFileType::Zip
    } else {
        zed::DownloadedFileType::Uncompressed
    }
}

fn server_from_install_dir(install_dir: &str) -> Option<ResolvedServer> {
    let root = PathBuf::from(install_dir);
    let executable = root.join(platform::language_server_fallback_binary_name());
    if file_exists(&executable) {
        return Some(ResolvedServer::Direct(path_to_string(executable)));
    }

    let dll = root.join("VbNet.LanguageServer.dll");
    if file_exists(&dll) {
        return Some(ResolvedServer::DotnetDll(path_to_string(dll)));
    }

    None
}

fn file_exists(path: &Path) -> bool {
    std::fs::metadata(path)
        .map(|metadata| metadata.is_file())
        .unwrap_or(false)
}

fn path_to_string(path: PathBuf) -> String {
    path.to_string_lossy().into_owned()
}

fn merge_env(env: &mut Vec<(String, String)>, extra_env: HashMap<String, String>) {
    for (key, value) in extra_env {
        if let Some((_, existing_value)) = env
            .iter_mut()
            .find(|(existing_key, _)| existing_key == &key)
        {
            *existing_value = value;
        } else {
            env.push((key, value));
        }
    }
}

fn missing_server_message() -> String {
    let primary = platform::language_server_binary_name();
    let fallback = platform::language_server_fallback_binary_name();
    format!(
        "Could not find VB.NET language server. Install '{primary}' on PATH, configure lsp.{LANGUAGE_SERVER_ID}.binary.path in Zed settings, or publish the {SERVER_VERSION} release artifact for this platform. Fallback PATH name checked: '{fallback}'."
    )
}

#[cfg(test)]
mod tests {
    use super::*;

    #[test]
    fn merge_env_overrides_existing_values() {
        let mut env = vec![("A".to_string(), "1".to_string())];
        merge_env(
            &mut env,
            HashMap::from([("A".to_string(), "2".to_string())]),
        );
        assert_eq!(env, vec![("A".to_string(), "2".to_string())]);
    }

    #[test]
    fn merge_env_appends_new_values() {
        let mut env = vec![("A".to_string(), "1".to_string())];
        merge_env(
            &mut env,
            HashMap::from([("B".to_string(), "2".to_string())]),
        );
        assert!(env.contains(&("A".to_string(), "1".to_string())));
        assert!(env.contains(&("B".to_string(), "2".to_string())));
    }

    #[test]
    fn release_install_dir_removes_archive_suffix() {
        assert_eq!(
            release_install_dir("v0.1.9", "vbnet-language-server-win-x64.zip"),
            "language-server/v0.1.9/vbnet-language-server-win-x64"
        );
        assert_eq!(
            release_install_dir("v0.1.9", "vbnet-language-server-linux-x64.tar.gz"),
            "language-server/v0.1.9/vbnet-language-server-linux-x64"
        );
    }
}
