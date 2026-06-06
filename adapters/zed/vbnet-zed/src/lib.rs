mod debug;
mod platform;
mod server;
mod workspace;

use zed_extension_api as zed;

pub(crate) const LANGUAGE_SERVER_ID: &str = "vbnet-ls";
pub(crate) const DEBUG_ADAPTER_ID: &str = "netcoredbg";
pub(crate) const DEBUG_LOCATOR_ID: &str = "vbnet";

struct VbNetExtension;

impl zed::Extension for VbNetExtension {
    fn new() -> Self {
        Self
    }

    fn language_server_command(
        &mut self,
        language_server_id: &zed::LanguageServerId,
        worktree: &zed::Worktree,
    ) -> zed::Result<zed::Command> {
        server::language_server_command(language_server_id, worktree)
    }

    fn language_server_initialization_options(
        &mut self,
        language_server_id: &zed::LanguageServerId,
        worktree: &zed::Worktree,
    ) -> zed::Result<Option<serde_json::Value>> {
        server::language_server_initialization_options(language_server_id, worktree)
    }

    fn language_server_workspace_configuration(
        &mut self,
        language_server_id: &zed::LanguageServerId,
        worktree: &zed::Worktree,
    ) -> zed::Result<Option<serde_json::Value>> {
        server::language_server_workspace_configuration(language_server_id, worktree)
    }

    fn get_dap_binary(
        &mut self,
        adapter_name: String,
        config: zed::DebugTaskDefinition,
        user_provided_debug_adapter_path: Option<String>,
        worktree: &zed::Worktree,
    ) -> zed::Result<zed::DebugAdapterBinary> {
        debug::get_dap_binary(
            adapter_name,
            config,
            user_provided_debug_adapter_path,
            worktree,
        )
    }

    fn dap_request_kind(
        &mut self,
        adapter_name: String,
        config: serde_json::Value,
    ) -> zed::Result<zed::StartDebuggingRequestArgumentsRequest> {
        debug::dap_request_kind(adapter_name, config)
    }

    fn dap_config_to_scenario(
        &mut self,
        config: zed::DebugConfig,
    ) -> zed::Result<zed::DebugScenario> {
        debug::dap_config_to_scenario(config)
    }

    fn dap_locator_create_scenario(
        &mut self,
        locator_name: String,
        build_task: zed::TaskTemplate,
        resolved_label: String,
        debug_adapter_name: String,
    ) -> Option<zed::DebugScenario> {
        debug::dap_locator_create_scenario(
            locator_name,
            build_task,
            resolved_label,
            debug_adapter_name,
        )
    }

    fn run_dap_locator(
        &mut self,
        locator_name: String,
        build_task: zed::TaskTemplate,
    ) -> zed::Result<zed::DebugRequest> {
        debug::run_dap_locator(locator_name, build_task)
    }
}

zed::register_extension!(VbNetExtension);
