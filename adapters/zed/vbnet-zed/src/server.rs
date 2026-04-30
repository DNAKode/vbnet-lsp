use std::collections::HashMap;

use zed::settings::LspSettings;
use zed_extension_api as zed;

use crate::{LANGUAGE_SERVER_ID, platform, workspace};

pub(crate) fn language_server_command(
    language_server_id: &zed::LanguageServerId,
    worktree: &zed::Worktree,
) -> zed::Result<zed::Command> {
    ensure_language_server(language_server_id)?;

    let settings = LspSettings::for_worktree(LANGUAGE_SERVER_ID, worktree).unwrap_or_default();
    let mut args = Vec::new();
    let mut env = worktree.shell_env();

    let command = if let Some(binary) = settings.binary {
        if let Some(arguments) = binary.arguments {
            args.extend(arguments);
        }

        if let Some(extra_env) = binary.env {
            merge_env(&mut env, extra_env);
        }

        binary.path.or_else(|| find_server_on_path(worktree))
    } else {
        find_server_on_path(worktree)
    }
    .ok_or_else(missing_server_message)?;

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
        "Could not find VB.NET language server. Install '{primary}' on PATH, or configure lsp.{LANGUAGE_SERVER_ID}.binary.path in Zed settings. Fallback PATH name checked: '{fallback}'."
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
}
