if vim.g.loaded_vbnet_lsp_plugin ~= nil then
    return
end
vim.g.loaded_vbnet_lsp_plugin = true

vim.api.nvim_create_autocmd('FileType', {
    pattern = { 'vb', 'cs' },
    callback = function()
        if package.loaded['vbnet_lsp'] == nil then
            return
        end
    end,
})
