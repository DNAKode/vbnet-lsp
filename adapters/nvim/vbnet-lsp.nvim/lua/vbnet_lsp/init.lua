local M = {}

local function expand_path(value)
    if not value or value == '' then
        return value
    end
    return vim.fn.fnamemodify(value, ':p')
end

local function normalize_cmd(cmd)
    if type(cmd) == 'string' then
        return { cmd }
    end
    return cmd
end

local function build_roslyn_cmd(opts)
    local cmd = normalize_cmd(opts.cmd or {})
    if #cmd == 0 then
        return nil
    end

    local args = vim.deepcopy(opts.args or {})
    return vim.list_extend(cmd, args)
end

local function build_vbnet_cmd(opts)
    local cmd = normalize_cmd(opts.cmd or {})
    if #cmd == 0 then
        return nil
    end

    local args = vim.deepcopy(opts.args or {})
    return vim.list_extend(cmd, args)
end

local function notify_solution(client, opts)
    if not client then
        return
    end
    if opts.solution and opts.solution ~= '' then
        client.notify('solution/open', { solution = expand_path(opts.solution) })
        return
    end
    if opts.project and opts.project ~= '' then
        client.notify('project/open', { projects = { expand_path(opts.project) } })
    end
end

local function pick_solution(root_dir)
    local candidates = {}
    local sln_files = vim.fn.globpath(root_dir, "**/*.sln", true, true)
    local slnx_files = vim.fn.globpath(root_dir, "**/*.slnx", true, true)
    for _, file in ipairs(sln_files) do
        table.insert(candidates, file)
    end
    for _, file in ipairs(slnx_files) do
        table.insert(candidates, file)
    end
    table.sort(candidates, function(a, b)
        return #a < #b
    end)
    return candidates
end

local function request_source_generated(client, buf)
    local uri = vim.api.nvim_buf_get_name(buf)
    local params = {
        textDocument = { uri = uri },
        resultId = vim.b[buf].resultId,
    }

    local function handler(err, result)
        if err then
            vim.notify(vim.inspect(err), vim.log.levels.ERROR)
            return
        end
        local content = result and result.text or ""
        local normalized = string.gsub(content, "\r\n", "\n")
        local source_lines = vim.split(normalized, "\n", { plain = true })
        vim.bo[buf].modifiable = true
        vim.api.nvim_buf_set_lines(buf, 0, -1, false, source_lines)
        vim.b[buf].resultId = result and result.resultId or nil
        vim.bo[buf].modifiable = false
    end

    client:request("sourceGeneratedDocument/_roslyn_getText", params, handler, buf)
end

local function attach_roslyn_handlers(config, opts)
    config.handlers = config.handlers or {}
    config.handlers["client/registerCapability"] = function(err, res, ctx)
        if opts.filewatching == "off" and res and res.registrations then
            for _, reg in ipairs(res.registrations) do
                if reg.method == "workspace/didChangeWatchedFiles" then
                    reg.registerOptions.watchers = {}
                end
            end
        end
        return vim.lsp.handlers["client/registerCapability"](err, res, ctx)
    end
    config.handlers["workspace/refreshSourceGeneratedDocument"] = function(_, _, ctx)
        local client = vim.lsp.get_client_by_id(ctx.client_id)
        if not client then
            return
        end
        for _, buf in ipairs(vim.api.nvim_list_bufs()) do
            local name = vim.api.nvim_buf_get_name(buf)
            if name:match("^roslyn%-source%-generated://") then
                request_source_generated(client, buf)
            end
        end
    end
end

local function start_client(opts, filetypes)
    local cmd = opts.backend == 'roslyn'
        and build_roslyn_cmd(opts.roslyn)
        or build_vbnet_cmd(opts.vbnet)

    if not cmd then
        vim.notify('[vbnet-lsp] server command is not configured', vim.log.levels.ERROR)
        return nil
    end

    local config = {
        name = 'vbnet-lsp',
        cmd = cmd,
        root_dir = opts.root_dir or vim.fn.getcwd(),
        filetypes = filetypes,
        on_init = function(client)
            if opts.backend == 'roslyn' then
                notify_solution(client, opts.roslyn)
            end
        end,
        reuse_client = function(client, cfg)
            return client.name == cfg.name and client.config.root_dir == cfg.root_dir
        end,
    }

    if opts.backend == 'roslyn' then
        attach_roslyn_handlers(config, opts.roslyn)
    end

    return vim.lsp.start(config)
end

function M.setup(opts)
    opts = opts or {}
    local backend = opts.backend or 'vbnet'
    opts.backend = backend
    opts.vbnet = opts.vbnet or {}
    opts.roslyn = opts.roslyn or {}

    local filetypes = opts.filetypes
    if not filetypes then
        filetypes = backend == 'roslyn' and { 'vb', 'cs' } or { 'vb' }
    end

    vim.api.nvim_create_autocmd('FileType', {
        pattern = filetypes,
        callback = function()
            start_client(opts, filetypes)
        end,
    })

    if backend == 'roslyn' then
        vim.api.nvim_create_autocmd({ 'BufReadCmd' }, {
            pattern = 'roslyn-source-generated://*',
            callback = function(args)
                vim.bo[args.buf].modifiable = true
                vim.bo[args.buf].swapfile = false
                vim.bo[args.buf].filetype = opts.roslyn.filetype or 'cs'
                local client = vim.lsp.get_clients({ name = 'vbnet-lsp' })[1]
                if not client then
                    vim.notify('[vbnet-lsp] Roslyn client not running for source-generated buffer', vim.log.levels.ERROR)
                    return
                end
                request_source_generated(client, args.buf)
            end,
        })

        vim.api.nvim_create_autocmd({ 'BufWritePost', 'InsertLeave' }, {
            pattern = { '*.cs', '*.razor', '*.cshtml', '*.vb' },
            callback = function()
                local client = vim.lsp.get_clients({ name = 'vbnet-lsp' })[1]
                if not client then
                    return
                end
                client:request("textDocument/diagnostic", {
                    textDocument = { uri = vim.uri_from_bufnr(0) },
                }, function() end)
            end,
        })
    end

    vim.api.nvim_create_user_command('VbNetSelectSolution', function()
        if backend ~= 'roslyn' then
            vim.notify('[vbnet-lsp] VbNetSelectSolution is only supported for roslyn backend', vim.log.levels.WARN)
            return
        end
        local client = vim.lsp.get_clients({ name = 'vbnet-lsp' })[1]
        if not client then
            vim.notify('[vbnet-lsp] Roslyn client not running', vim.log.levels.ERROR)
            return
        end
        local solution = vim.fn.input('Solution path: ', opts.roslyn.solution or '', 'file')
        if solution and solution ~= '' then
            opts.roslyn.solution = solution
            notify_solution(client, opts.roslyn)
        end
    end, {})

    vim.api.nvim_create_user_command('VbNetPickSolution', function()
        if backend ~= 'roslyn' then
            vim.notify('[vbnet-lsp] VbNetPickSolution is only supported for roslyn backend', vim.log.levels.WARN)
            return
        end
        local root = opts.root_dir or vim.fn.getcwd()
        local candidates = pick_solution(root)
        if #candidates == 0 then
            vim.notify('[vbnet-lsp] No solution files found under ' .. root, vim.log.levels.WARN)
            return
        end
        vim.ui.select(candidates, { prompt = 'Select solution' }, function(choice)
            if choice and choice ~= '' then
                opts.roslyn.solution = choice
                local client = vim.lsp.get_clients({ name = 'vbnet-lsp' })[1]
                notify_solution(client, opts.roslyn)
            end
        end)
    end, {})
end

return M
