use zed_extension_api as zed;

pub(crate) fn preferred_solution_path(worktree: &zed::Worktree) -> Option<String> {
    for candidate in [
        "SmallProject.slnx",
        "SmallProject.sln",
        "VbNet.LanguageServer.slnx",
        "VbNet.LanguageServer.sln",
    ] {
        if worktree.read_text_file(candidate).is_ok() {
            return Some(join_worktree_path(worktree, candidate));
        }
    }

    None
}

fn join_worktree_path(worktree: &zed::Worktree, relative_path: &str) -> String {
    let root = worktree.root_path();
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
    #[test]
    fn placeholder() {
        assert!(true);
    }
}
