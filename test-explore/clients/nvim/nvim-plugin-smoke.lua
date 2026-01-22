local log_path = os.getenv("NVIM_PLUGIN_LOG") or ""
local backend = os.getenv("VBNET_PLUGIN_BACKEND") or "vbnet"
local workspace = vim.fn.fnamemodify(os.getenv("VBNET_LSP_WORKSPACE") or "test/TestProjects/SmallProject", ":p")
local file = vim.fn.fnamemodify(os.getenv("VBNET_LSP_FILE") or "test/TestProjects/SmallProject/Module1.vb", ":p")

local function log(msg)
    if log_path == "" then
        return
    end
    local f = io.open(log_path, "a")
    if not f then
        return
    end
    f:write(msg .. "\n")
    f:close()
end

local function fail(msg)
    log("ERROR: " .. msg)
    io.stderr:write(msg .. "\n")
    os.exit(1)
end

local function split_paths(value)
    if not value or value == "" then
        return {}
    end
    local sep = package.config:sub(1, 1) == "\\" and ";" or ":"
    local result = {}
    for part in string.gmatch(value, "([^" .. sep .. "]+)") do
        local trimmed = part:gsub("^%s+", ""):gsub("%s+$", "")
        if trimmed ~= "" then
            table.insert(result, trimmed)
        end
    end
    return result
end

local opts = { backend = backend, root_dir = workspace }
if backend == "vbnet" then
    local lsp_dll = os.getenv("VBNET_LSP_DLL") or ""
    if lsp_dll == "" then
        fail("VBNET_LSP_DLL not set")
    end
    opts.vbnet = { cmd = { "dotnet", lsp_dll, "--stdio", "--logLevel", "Trace" } }
else
    local roslyn_dll = os.getenv("ROSLYN_LS_DLL") or ""
    if roslyn_dll == "" then
        fail("ROSLYN_LS_DLL not set")
    end
    local log_dir = os.getenv("ROSLYN_LS_LOG_DIR") or (vim.fn.stdpath("cache") .. "/roslyn-ls")
    local log_level = os.getenv("ROSLYN_LS_LOG_LEVEL") or "Information"
    local extensions = split_paths(os.getenv("ROSLYN_LS_EXTENSIONS") or "")
    local args = { "--logLevel", log_level, "--extensionLogDirectory", log_dir }
    for _, ext in ipairs(extensions) do
        table.insert(args, "--extension")
        table.insert(args, ext)
    end
    table.insert(args, "--stdio")
    opts.roslyn = {
        cmd = { "dotnet", roslyn_dll },
        args = args,
        solution = os.getenv("ROSLYN_LSP_SOLUTION") or "",
        project = os.getenv("ROSLYN_LSP_PROJECT") or "",
        filewatching = "off",
    }
end

local ok, plugin = pcall(require, "vbnet_lsp")
if not ok then
    fail("Failed to load vbnet_lsp plugin")
end
plugin.setup(opts)

log("Plugin backend: " .. backend)
log("Workspace: " .. workspace)
log("File: " .. file)

vim.cmd("edit " .. vim.fn.fnameescape(file))
vim.bo.filetype = "vb"

local bufnr = vim.api.nvim_get_current_buf()
local client_id

local attached = vim.wait(20000, function()
    for _, client in pairs(vim.lsp.get_clients()) do
        if client.name == "vbnet-lsp" then
            client_id = client.id
            return vim.lsp.buf_is_attached(bufnr, client.id)
        end
    end
    return false
end, 100)

if not attached then
    fail("LSP client did not attach in time")
end

local function request_with_retry(method, params, attempts, delay_ms)
    local remaining = attempts or 5
    local delay = delay_ms or 500
    while remaining > 0 do
        local result = vim.lsp.buf_request_sync(bufnr, method, params, 10000)
        if result and client_id and result[client_id] and result[client_id].result then
            return result[client_id].result
        end
        remaining = remaining - 1
        if remaining > 0 then
            vim.wait(delay)
        end
    end
    return nil
end

local line_index = 4
local line = vim.api.nvim_buf_get_lines(bufnr, line_index, line_index + 1, false)[1] or ""
local col = line:find("DerivedHelper", 1, true)
if not col then
    col = line:find("As ", 1, true)
end
if not col then
    fail("Unable to locate hover position")
end
col = col - 1

local hover = request_with_retry("textDocument/hover", {
    textDocument = { uri = vim.uri_from_bufnr(bufnr) },
    position = { line = line_index, character = col },
}, 6, 500)

if not hover then
    fail("Hover request returned no result")
end

local diagnostics = request_with_retry("textDocument/diagnostic", {
    textDocument = { uri = vim.uri_from_bufnr(bufnr) },
}, 6, 500)

if not diagnostics then
    fail("Diagnostics request returned no result")
end

log("Plugin smoke test completed successfully")
vim.cmd("qa!")
