# Neovim (VB.NET LSP) Prototype Notes

This document captures a minimal Neovim setup snippet for the VB.NET language server,
modeled after patterns used by `roslyn.nvim`, but scoped to our VB.NET server. It is
not a published plugin and exists only for reference and future work.

## Goals

- Mirror roslyn.nvim-style root detection (search for `.sln/.slnf/.slnx/.vbproj`).
- Allow filewatching behavior toggles without changing server code.
- Keep VS Code behaviors isolated and untouched.

## Minimal Neovim Config (built-in LSP)

```lua
local function root_dir(fname)
    local start = vim.fs.dirname(fname)
    local found = vim.fs.find({ ".sln", ".slnf", ".slnx", ".vbproj" }, {
        path = start,
        upward = true,
        stop = vim.loop.os_homedir(),
    })
    if #found > 0 then
        return vim.fs.dirname(found[1])
    end
    return start
end

local function setup_vbnet(filewatching)
    local capabilities = vim.lsp.protocol.make_client_capabilities()

    if filewatching == "off" or filewatching == "server" then
        capabilities.workspace = capabilities.workspace or {}
        capabilities.workspace.didChangeWatchedFiles = {
            -- If "off", we strip watcher registrations on the client.
            dynamicRegistration = (filewatching == "off"),
        }
    end

    vim.lsp.start({
        name = "vbnet",
        cmd = {
            "dotnet",
            "C:\\path\\to\\VbNet.LanguageServer.dll",
            "--stdio",
            "--logLevel",
            "Information",
        },
        root_dir = root_dir(vim.api.nvim_buf_get_name(0)),
        capabilities = capabilities,
        handlers = {
            ["client/registerCapability"] = function(err, res, ctx)
                if filewatching == "off" and res and res.registrations then
                    for _, reg in ipairs(res.registrations) do
                        if reg.method == "workspace/didChangeWatchedFiles" then
                            reg.registerOptions.watchers = {}
                        end
                    end
                end
                return vim.lsp.handlers["client/registerCapability"](err, res, ctx)
            end,
        },
    })
end

-- "auto" | "server" | "off"
setup_vbnet("auto")
```

## Notes

- The VB.NET server does **not** implement Roslyn-specific `solution/open` or `project/open`
  notifications. Root detection relies on standard LSP workspace discovery.
- Filewatching is controlled from the client side only; the server already supports
  `workspace/didChangeWatchedFiles`.
- For headless validation, use the Neovim harness under `test-explore/clients/nvim`.
