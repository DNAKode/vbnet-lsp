(setq inhibit-message nil)
(setq vc-handled-backends nil)
(require 'eglot)
(require 'cc-mode)

(defun codex--file-uri (path)
  (concat "file:///" (replace-regexp-in-string "\\\\" "/" (expand-file-name path) t t)))

(defun codex--position-at (buffer token)
  (with-current-buffer buffer
    (goto-char (point-min))
    (search-forward token nil t)
    (let* ((pos (point))
           (line (1- (line-number-at-pos pos)))
           (col (current-column)))
      (list :line line :character col))))

(defun codex--run-eglot-test (name mode server-command file-path request-hover)
  (let* ((buffer (find-file-noselect file-path)))
    (with-current-buffer buffer
      (funcall mode)
      (setq eglot-server-programs `((,mode . ,server-command)))
      (message "eglot server programs: %s" eglot-server-programs)
      (message "major-mode: %s" major-mode)
      (let ((server (apply #'eglot--connect (eglot--guess-contact))))
        (sleep-for 1)
        (unless server
          (error "No eglot server for %s (mode=%s)" name major-mode))
        (when request-hover
          (let* ((pos (codex--position-at buffer "Add("))
                 (params `(:textDocument (:uri ,(codex--file-uri file-path))
                           :position ,pos))
                 (response (jsonrpc-request server "textDocument/hover" params)))
            (when (or (null response) (eq response :null))
              (error "Hover response empty for %s" name))))
        (condition-case err
            (eglot-shutdown server)
          (jsonrpc-error
           (message "Shutdown request failed for %s: %s" name err)))))
    (kill-buffer buffer)))

(defun codex-run ()
  (let* ((suite (or (getenv "CODEX_SUITE") "all"))
         (roslyn-lsp (getenv "ROSLYN_LSP_DLL"))
         (vbnet-lsp (getenv "VBNET_LSP_DLL"))
         (root (file-name-directory (or load-file-name buffer-file-name)))
         (fixture-basic (expand-file-name "../../csharp-lsp/fixtures/basic/Basic/Class1.cs" root))
         (vb-fixture (expand-file-name "../../vbnet-lsp/fixtures/basic/Basic.vb" root)))
    (when (and (or (string= suite "csharp") (string= suite "all")) roslyn-lsp)
      (codex--run-eglot-test
       "csharp"
       'csharp-mode
       (list "dotnet" roslyn-lsp "--stdio" "--logLevel" "Information" "--extensionLogDirectory" (expand-file-name "logs" root))
       fixture-basic
       nil))
    (when (and (or (string= suite "vbnet") (string= suite "all")) vbnet-lsp)
      (codex--run-eglot-test
       "vbnet"
       'fundamental-mode
       (list "dotnet" vbnet-lsp "--stdio" "--logLevel" "Information")
       vb-fixture
       nil)))
  (kill-emacs 0))

(codex-run)
