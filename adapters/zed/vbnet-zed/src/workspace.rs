use zed_extension_api as zed;

pub(crate) fn preferred_solution_path(worktree: &zed::Worktree) -> Option<String> {
    for candidate in [
        "ZedSlnxFixture.slnx",
        "ZedSlnfFixture.slnf",
        "ZedSlnFixture.sln",
        "ZedMixed.sln",
        "SmallProject.slnx",
        "SmallProject.sln",
        "VbNet.LanguageServer.slnx",
        "VbNet.LanguageServer.sln",
        "ZedFixture.vbproj",
        "SmallProject.vbproj",
    ] {
        if worktree.read_text_file(candidate).is_ok() {
            return Some(join_root_path(&worktree.root_path(), candidate));
        }
    }

    None
}

fn join_root_path(root: &str, relative_path: &str) -> String {
    let separator = if root.contains('\\') { "\\" } else { "/" };
    format!(
        "{}{}{}",
        root.trim_end_matches(['/', '\\']),
        separator,
        relative_path
    )
}

#[cfg(test)]
mod tests {
    use super::*;

    #[test]
    fn joins_windows_root_paths() {
        assert_eq!(
            join_root_path(r"C:\repo\", "Project.sln"),
            r"C:\repo\Project.sln"
        );
    }

    #[test]
    fn joins_unix_root_paths() {
        assert_eq!(join_root_path("/repo/", "Project.sln"), "/repo/Project.sln");
    }
}
