local log_path = os.getenv("NVIM_HARNESS_LOG") or ""

local function log(msg)
    if log_path == "" then
        return
    end
    local file = io.open(log_path, "a")
    if not file then
        return
    end
    file:write(msg .. "\n")
    file:close()
end

local function fail(msg)
    log("ERROR: " .. msg)
    io.stderr:write(msg .. "\n")
    os.exit(1)
end

local suite = os.getenv("CODEX_SUITE") or "vbnet"
local workspace = ""
local file = ""
local client_id = nil
local pending_project_init = false
local open_bufnr = nil

vim.lsp.set_log_level("trace")
log("Neovim LSP log: " .. vim.lsp.log.get_filename())

local capabilities = vim.lsp.protocol.make_client_capabilities()
capabilities.textDocument = capabilities.textDocument or {}
capabilities.textDocument.diagnostic = { dynamicRegistration = true }

local function start_vbnet()
    local lsp_dll = os.getenv("VBNET_LSP_DLL") or ""
    if lsp_dll == "" then
        fail("VBNET_LSP_DLL not set. Build the server and set the env var.")
    end

    client_id = vim.lsp.start({
        name = "vbnet",
        cmd = { "dotnet", lsp_dll, "--stdio", "--logLevel", "Trace" },
        root_dir = workspace,
        capabilities = capabilities,
        bufnr = open_bufnr,
        on_init = function()
            log("VB.NET LSP initialized")
        end,
        on_exit = function(code, signal, _)
            log(string.format("VB.NET LSP exited (code=%s, signal=%s)", tostring(code), tostring(signal)))
        end,
    })
end

local function start_roslyn()
    local roslyn_cmd = os.getenv("ROSLYN_LS_CMD") or ""
    local roslyn_dll = os.getenv("ROSLYN_LS_DLL") or ""
    local roslyn_log_level = os.getenv("ROSLYN_LS_LOG_LEVEL") or "Information"
    local roslyn_extensions = os.getenv("ROSLYN_LS_EXTENSIONS") or ""
    if roslyn_cmd == "" and roslyn_dll == "" then
        fail("ROSLYN_LS_CMD or ROSLYN_LS_DLL not set.")
    end

    local log_dir = os.getenv("ROSLYN_LS_LOG_DIR")
    if not log_dir or log_dir == "" then
        log_dir = vim.fn.stdpath("cache") .. "/roslyn-ls"
    end
    vim.fn.mkdir(log_dir, "p")

    local function split_paths(value)
        if value == "" then
            return {}
        end
        local sep = package.config:sub(1, 1) == "\\" and ";" or ":"
        local paths = {}
        for part in string.gmatch(value, "([^" .. sep .. "]+)") do
            local trimmed = part:gsub("^%s+", ""):gsub("%s+$", "")
            if trimmed ~= "" then
                table.insert(paths, trimmed)
            end
        end
        return paths
    end

    local extension_args = {}
    for _, ext in ipairs(split_paths(roslyn_extensions)) do
        table.insert(extension_args, "--extension")
        table.insert(extension_args, ext)
    end

    local cmd = nil
    if roslyn_dll ~= "" then
        cmd = { "dotnet", roslyn_dll, "--logLevel", roslyn_log_level, "--extensionLogDirectory", log_dir }
    else
        cmd = { roslyn_cmd, "--logLevel=" .. roslyn_log_level, "--extensionLogDirectory=" .. log_dir }
    end

    for _, arg in ipairs(extension_args) do
        table.insert(cmd, arg)
    end
    table.insert(cmd, "--stdio")

    client_id = vim.lsp.start({
        name = "roslyn",
        cmd = cmd,
        root_dir = workspace,
        capabilities = capabilities,
        bufnr = open_bufnr,
        handlers = {
            ["workspace/projectInitializationComplete"] = function()
                log("Roslyn project initialization complete")
                pending_project_init = true
                if open_bufnr and vim.api.nvim_buf_is_loaded(open_bufnr) then
                    local params = { textDocument = { uri = vim.uri_from_bufnr(open_bufnr) } }
                    vim.lsp.buf_request(open_bufnr, "textDocument/diagnostic", params, function() end)
                end
            end,
            ["window/logMessage"] = function(_, result)
                if result and result.message then
                    log("Roslyn logMessage: " .. result.message)
                end
            end,
            ["window/showMessage"] = function(_, result)
                if result and result.message then
                    log("Roslyn showMessage: " .. result.message)
                end
            end,
        },
        on_init = function()
            log("Roslyn LSP initialized")
        end,
        on_exit = function(code, signal, _)
            log(string.format("Roslyn LSP exited (code=%s, signal=%s)", tostring(code), tostring(signal)))
        end,
    })
end

local function init_paths()
    if suite == "vbnet" then
        workspace = os.getenv("VBNET_LSP_WORKSPACE") or "test/TestProjects/SmallProject"
        file = os.getenv("VBNET_LSP_FILE") or "test/TestProjects/SmallProject/Module1.vb"
    elseif suite == "csharp" or suite == "roslyn-vb" then
        workspace = os.getenv("ROSLYN_LSP_WORKSPACE") or "test-explore/clients/nvim/fixtures/CSharpSample"
        file = os.getenv("ROSLYN_LSP_FILE") or "test-explore/clients/nvim/fixtures/CSharpSample/Program.cs"
    else
        fail("Unknown CODEX_SUITE: " .. suite)
    end

    workspace = vim.fn.fnamemodify(workspace, ":p")
    file = vim.fn.fnamemodify(file, ":p")

    log("Suite: " .. suite)
    log("Workspace: " .. workspace)
    log("File: " .. file)
end

init_paths()

vim.cmd("edit " .. vim.fn.fnameescape(file))
if suite == "vbnet" then
    vim.bo.filetype = "vb"
elseif suite == "csharp" then
    vim.bo.filetype = "cs"
else
    vim.bo.filetype = "vb"
end
local bufnr = vim.api.nvim_get_current_buf()
open_bufnr = bufnr

if suite == "vbnet" then
    start_vbnet()
elseif suite == "csharp" or suite == "roslyn-vb" then
    start_roslyn()
end

if not client_id then
    fail("Failed to start LSP client.")
end

local attached = vim.wait(15000, function()
    return vim.lsp.buf_is_attached(bufnr, client_id)
end, 100)

if not attached then
    fail("LSP did not attach to buffer within timeout.")
end

local initialized = vim.wait(15000, function()
    local client = vim.lsp.get_client_by_id(client_id)
    return client and client.initialized
end, 100)

if not initialized then
    fail("LSP did not initialize within timeout.")
end

do
    local client = vim.lsp.get_client_by_id(client_id)
    if client then
        log("Client root_dir: " .. tostring(client.config.root_dir))
    end
end

if suite == "vbnet" then
    local client = vim.lsp.get_client_by_id(client_id)
    if client then
        client:notify("workspace/didChangeConfiguration", {
            settings = {
                vbnet = {
                    ["workspace.ignoreSolutionFiles"] = "true",
                    workspace = {
                        projectSearchPaths = { workspace },
                    },
                },
            },
        })
        log("Sent workspace/didChangeConfiguration to ignore solutions and pin projectSearchPaths.")
        vim.wait(1000)
    end
end

if pending_project_init then
    local params = { textDocument = { uri = vim.uri_from_bufnr(bufnr) } }
    vim.lsp.buf_request(bufnr, "textDocument/diagnostic", params, function() end)
end

local function request_sync(method, params)
    local result = vim.lsp.buf_request_sync(bufnr, method, params, 10000)
    if not result or not result[client_id] then
        return nil
    end
    if result[client_id].error then
        log(string.format("LSP error for %s: %s", method, vim.inspect(result[client_id].error)))
        return nil
    end
    if result[client_id].result == nil then
        log(string.format("LSP nil result for %s", method))
        return nil
    end
    return result[client_id].result
end

local function request_with_retry(method, params, attempts, delay_ms)
    local remaining = attempts or 5
    local delay = delay_ms or 500
    while remaining > 0 do
        local result = request_sync(method, params)
        if result then
            return result
        end
        remaining = remaining - 1
        if remaining > 0 then
            vim.wait(delay)
        end
    end
    return nil
end




local function wait_for_roslyn_project_init(timeout_ms)
    if suite ~= "csharp" and suite ~= "roslyn-vb" then
        return true
    end

    local ok = vim.wait(timeout_ms or 20000, function()
        return pending_project_init
    end, 100)
    if not ok then
        log("Timed out waiting for Roslyn project initialization.")
    end
    return ok
end



if suite == "csharp" then
    local client = vim.lsp.get_client_by_id(client_id)
    if client then
        local solution = os.getenv("ROSLYN_LSP_SOLUTION") or ""
        if solution ~= "" then
            client:notify("solution/open", { solution = vim.fn.fnamemodify(solution, ":p") })
            log("Sent Roslyn solution/open for " .. solution)
        else
            client:notify("project/open", {
                projects = { vim.uri_from_fname(workspace .. "/CSharpSample.csproj") },
            })
            log("Sent Roslyn project/open for CSharpSample.csproj")
        end
    end
end
if suite == "roslyn-vb" then
    local client = vim.lsp.get_client_by_id(client_id)
    if client then
        local project = os.getenv("ROSLYN_LSP_PROJECT") or ""
        local solution = os.getenv("ROSLYN_LSP_SOLUTION") or ""
        local sln_path = solution ~= "" and vim.fn.fnamemodify(solution, ":p") or (workspace .. "/SmallProject.sln")
        local slnx_path = workspace .. "/SmallProject.slnx"
        if vim.fn.filereadable(sln_path) == 1 then
            client:notify("solution/open", {
                solution = vim.uri_from_fname(sln_path),
            })
            log("Sent Roslyn solution/open for " .. vim.fn.fnamemodify(sln_path, ":t"))
        elseif solution ~= "" then
            log("Roslyn solution not found at " .. sln_path)
        elseif vim.fn.filereadable(slnx_path) == 1 then
            client:notify("solution/open", {
                solution = vim.uri_from_fname(slnx_path),
            })
            log("Sent Roslyn solution/open for SmallProject.slnx")
        elseif project ~= "" then
            client:notify("project/open", {
                projects = { vim.uri_from_fname(project) },
            })
            log("Sent Roslyn project/open for " .. project)
        else
            client:notify("project/open", {
                projects = { vim.uri_from_fname(workspace .. "/SmallProject.vbproj") },
            })
            log("Sent Roslyn project/open for SmallProject.vbproj")
        end
    end
end



wait_for_roslyn_project_init()

local function find_col(line_text, text)
    local start_col = line_text:find(text, 1, true)
    if not start_col then
        return nil
    end
    return start_col - 1
end

local function find_line_with_text(buf, text)
    local lines = vim.api.nvim_buf_get_lines(buf, 0, -1, false)
    for i, line in ipairs(lines) do
        if line:find(text, 1, true) then
            return i - 1, line
        end
    end
    return nil, nil
end

if suite == "vbnet" then
    local line_index = 4 -- 0-based line index for Module1.vb
    local line = vim.api.nvim_buf_get_lines(bufnr, line_index, line_index + 1, false)[1] or ""

    local derived_col = find_col(line, "DerivedHelper")
    if not derived_col then
        fail("Unable to locate 'DerivedHelper' in test line.")
    end

    local warmup_diag = request_with_retry("textDocument/diagnostic", {
        textDocument = { uri = vim.uri_from_bufnr(bufnr) },
    }, 6, 500)
    if not warmup_diag then
        log("Warmup diagnostics returned no result before hover.")
    end

    local symbols = request_with_retry("textDocument/documentSymbol", {
        textDocument = { uri = vim.uri_from_bufnr(bufnr) },
    }, 4, 500)
    if not symbols then
        log("DocumentSymbol returned no result before hover.")
    end

    local hover = request_with_retry("textDocument/hover", {
        textDocument = { uri = vim.uri_from_bufnr(bufnr) },
        position = { line = line_index, character = derived_col },
    }, 6, 500)

    if not hover then
        fail("Hover request returned no result.")
    end

    local base_col = find_col(line, "BaseHelper")
    if not base_col then
        fail("Unable to locate 'BaseHelper' in test line.")
    end

    local definition = request_with_retry("textDocument/definition", {
        textDocument = { uri = vim.uri_from_bufnr(bufnr) },
        position = { line = line_index, character = base_col },
    }, 6, 500)

    if not definition or (type(definition) == "table" and #definition == 0) then
        fail("Definition request returned no result.")
    end

    local links = request_with_retry("textDocument/documentLink", {
        textDocument = { uri = vim.uri_from_bufnr(bufnr) },
    }, 4, 500)
    if not links or #links == 0 then
        fail("DocumentLink request returned no result.")
    end

    local helper_path = workspace .. "/Helper.vb"
    if vim.fn.filereadable(helper_path) == 0 then
        fail("Helper.vb not found at " .. helper_path)
    end

    vim.cmd("edit " .. vim.fn.fnameescape(helper_path))
    vim.bo.filetype = "vb"
    local helper_buf = vim.api.nvim_get_current_buf()
    log("Helper buffer: " .. tostring(vim.api.nvim_buf_get_name(helper_buf)))

    vim.lsp.buf_attach_client(helper_buf, client_id)

    local helper_attached = vim.wait(10000, function()
        return vim.lsp.buf_is_attached(helper_buf, client_id)
    end, 100)
    if not helper_attached then
        fail("LSP did not attach to Helper.vb within timeout.")
    end

    local helper_line_index, helper_line = find_line_with_text(helper_buf, "Add(1, 2)")
    if helper_line_index == nil then
        fail("Unable to locate 'Add(1, 2)' in Helper.vb.")
    end
    local add_col = find_col(helper_line, "Add(1, 2)")
    if not add_col then
        fail("Unable to locate 'Add(1, 2)' column in Helper.vb.")
    end

    local signature = vim.lsp.buf_request_sync(helper_buf, "textDocument/signatureHelp", {
        textDocument = { uri = vim.uri_from_bufnr(helper_buf) },
        position = { line = helper_line_index, character = add_col + 4 },
    }, 10000)

    if not signature or not signature[client_id] or not signature[client_id].result then
        fail("SignatureHelp request returned no result.")
    end

    local completion = vim.lsp.buf_request_sync(helper_buf, "textDocument/completion", {
        textDocument = { uri = vim.uri_from_bufnr(helper_buf) },
        position = { line = helper_line_index, character = add_col + 4 },
    }, 10000)

    local completion_result = completion and completion[client_id] and completion[client_id].result or nil
    if not completion_result then
        fail("Completion request returned no result.")
    end

    local items = completion_result.items or completion_result
    if type(items) ~= "table" or #items == 0 then
        fail("Completion request returned no items.")
    end

    local first_item = items[1]
    if first_item and first_item.data then
        local resolved = vim.lsp.buf_request_sync(helper_buf, "completionItem/resolve", first_item, 10000)
        local resolved_item = resolved and resolved[client_id] and resolved[client_id].result or nil
        if not resolved_item then
            fail("Completion resolve returned no result.")
        end
    end

    local code_actions = vim.lsp.buf_request_sync(helper_buf, "textDocument/codeAction", {
        textDocument = { uri = vim.uri_from_bufnr(helper_buf) },
        range = { start = { line = 0, character = 0 }, ["end"] = { line = 0, character = 0 } },
        context = { diagnostics = {} },
    }, 10000)

    local code_action_result = code_actions and code_actions[client_id] and code_actions[client_id].result or nil
    if not code_action_result or type(code_action_result) ~= "table" or #code_action_result == 0 then
        fail("CodeAction request returned no results.")
    end

    local action = code_action_result[1]
    if action and action.data then
        local resolved_action = vim.lsp.buf_request_sync(helper_buf, "codeAction/resolve", action, 10000)
        local resolved = resolved_action and resolved_action[client_id] and resolved_action[client_id].result or nil
        if not resolved then
            fail("CodeAction resolve returned no result.")
        end
    end
elseif suite == "csharp" then
    local line_index, line = find_line_with_text(bufnr, "Console.WriteLine")
    if line_index == nil then
        fail("Unable to locate 'Console.WriteLine' in Program.cs.")
    end
    local console_col = find_col(line, "Console")
    if not console_col then
        fail("Unable to locate 'Console' in test line.")
    end

    local hover = request_with_retry("textDocument/hover", {
        textDocument = { uri = vim.uri_from_bufnr(bufnr) },
        position = { line = line_index, character = console_col },
    }, 6, 500)

    if not hover then
        fail("Hover request returned no result.")
    end

    local completion = request_with_retry("textDocument/completion", {
        textDocument = { uri = vim.uri_from_bufnr(bufnr) },
        position = { line = line_index, character = console_col + 2 },
    }, 6, 500)

    local items = completion and (completion.items or completion) or nil
    if not items or type(items) ~= "table" or #items == 0 then
        fail("C# completion request returned no items.")
    end
else
    local line_index, line = find_line_with_text(bufnr, "Dim helper As")
    if line_index == nil then
        fail("Unable to locate 'Dim helper As' in Module1.vb.")
    end

    local as_col = find_col(line, "As ")
    if not as_col then
        fail("Unable to locate 'As ' in test line.")
    end

    local hover = request_with_retry("textDocument/hover", {
        textDocument = { uri = vim.uri_from_bufnr(bufnr) },
        position = { line = line_index, character = as_col + 4 },
    }, 6, 500)

    if not hover then
        fail("Roslyn VB hover request returned no result.")
    end

    local completion = request_with_retry("textDocument/completion", {
        textDocument = { uri = vim.uri_from_bufnr(bufnr) },
        position = { line = line_index, character = as_col + 4 },
    }, 6, 500)

    local items = completion and (completion.items or completion) or nil
    if not items or type(items) ~= "table" or #items == 0 then
        fail("Roslyn VB completion request returned no items.")
    end
end

local diagnostics = request_with_retry("textDocument/diagnostic", {
    textDocument = { uri = vim.uri_from_bufnr(bufnr) },
}, 6, 500)
if not diagnostics then
    fail("Pull diagnostics request returned no result.")
end

log("Neovim smoke test completed successfully (" .. suite .. ").")
vim.cmd("qa!")
