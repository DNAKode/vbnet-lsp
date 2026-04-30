use zed_extension_api as zed;

pub(crate) fn language_server_binary_name() -> &'static str {
    match zed::current_platform().0 {
        zed::Os::Windows => "vbnet-ls.exe",
        _ => "vbnet-ls",
    }
}

pub(crate) fn language_server_fallback_binary_name() -> &'static str {
    match zed::current_platform().0 {
        zed::Os::Windows => "VbNet.LanguageServer.exe",
        _ => "VbNet.LanguageServer",
    }
}

pub(crate) fn debug_adapter_binary_name() -> &'static str {
    match zed::current_platform().0 {
        zed::Os::Windows => "netcoredbg.exe",
        _ => "netcoredbg",
    }
}

#[allow(dead_code)]
pub(crate) fn release_asset_name(version: &str) -> String {
    let (os, arch) = zed::current_platform();
    format!("vbnet-ls-{version}-{}.zip", platform_rid(os, arch))
}

pub(crate) fn platform_rid(os: zed::Os, arch: zed::Architecture) -> &'static str {
    match (os, arch) {
        (zed::Os::Windows, zed::Architecture::X8664) => "win-x64",
        (zed::Os::Windows, zed::Architecture::Aarch64) => "win-arm64",
        (zed::Os::Linux, zed::Architecture::X8664) => "linux-x64",
        (zed::Os::Linux, zed::Architecture::Aarch64) => "linux-arm64",
        (zed::Os::Mac, zed::Architecture::X8664) => "osx-x64",
        (zed::Os::Mac, zed::Architecture::Aarch64) => "osx-arm64",
        _ => "unsupported",
    }
}

#[cfg(test)]
mod tests {
    use super::*;

    #[test]
    fn platform_rids_cover_release_targets() {
        assert_eq!(
            platform_rid(zed::Os::Windows, zed::Architecture::X8664),
            "win-x64"
        );
        assert_eq!(
            platform_rid(zed::Os::Linux, zed::Architecture::Aarch64),
            "linux-arm64"
        );
        assert_eq!(
            platform_rid(zed::Os::Mac, zed::Architecture::Aarch64),
            "osx-arm64"
        );
    }
}
