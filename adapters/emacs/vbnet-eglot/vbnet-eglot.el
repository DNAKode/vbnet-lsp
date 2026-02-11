;;; vbnet-eglot.el --- Eglot integration for VB.NET LSP -*- lexical-binding: t; -*-

;; Author: VB.NET LSP contributors
;; Version: 0.1.0
;; Package-Requires: ((emacs "29.1"))
;; Keywords: languages, lsp, vbnet
;; URL: https://github.com/DNAKode/vbnet-lsp

;;; Commentary:

;; Thin editor adapter for running the VB.NET language server with eglot.
;; This package intentionally keeps editor logic minimal and delegates language
;; intelligence to the server binary.

;;; Code:

(require 'eglot)

(defgroup vbnet-eglot nil
  "Eglot integration for VB.NET language server."
  :group 'tools
  :link '(url-link "https://github.com/DNAKode/vbnet-lsp"))

(defcustom vbnet-eglot-server-program
  '("dotnet" "VbNet.LanguageServer.dll" "--stdio")
  "Command used to start the VB.NET language server.

Set this to an absolute executable path when using app-host builds, for example:

  '(\"/path/to/VbNet.LanguageServer\" \"--stdio\")"
  :type '(repeat string)
  :group 'vbnet-eglot)

(defcustom vbnet-eglot-major-modes
  '(vbnet-mode visual-basic-mode vb-mode)
  "Major modes to associate with `vbnet-eglot-server-program`."
  :type '(repeat symbol)
  :group 'vbnet-eglot)

;;;###autoload
(defun vbnet-eglot-register ()
  "Register VB.NET language server mappings in `eglot-server-programs`."
  (interactive)
  (dolist (mode vbnet-eglot-major-modes)
    (add-to-list 'eglot-server-programs
                 `(,mode . ,vbnet-eglot-server-program))))

;;;###autoload
(defun vbnet-eglot-ensure ()
  "Register mappings and start eglot in the current buffer."
  (interactive)
  (vbnet-eglot-register)
  (call-interactively #'eglot-ensure))

(provide 'vbnet-eglot)

;;; vbnet-eglot.el ends here
