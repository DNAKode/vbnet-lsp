(setq inhibit-message nil)
(setq vc-handled-backends nil)
(require 'eglot)
(require 'cc-mode)
(require 'jsonrpc)

(defvar codex--log-file nil)

(defun codex--log (format-string &rest args)
  (let ((message-text (apply #'format format-string args)))
    (message "%s" message-text)
    (when codex--log-file
      (with-temp-buffer
        (insert message-text "\n")
        (write-region (point-min) (point-max) codex--log-file 'append 'silent)))))

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

(defun codex--server-live-p (server)
  (let ((proc (jsonrpc-process server)))
    (and proc (process-live-p proc))))

(defun codex--run-eglot-test (name mode server-command file-path request-hover)
  (let ((buffer (find-file-noselect file-path)))
    (unwind-protect
        (with-current-buffer buffer
          (funcall mode)
          (setq eglot-server-programs `((,mode . ,server-command)))
          (codex--log "eglot server programs: %s" eglot-server-programs)
          (codex--log "major-mode: %s" major-mode)
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
               (if (codex--server-live-p server)
                   (codex--log "Shutdown request failed for %s: %s" name err)
                 (codex--log "Shutdown timed out for %s after server exit." name))))))
      (kill-buffer buffer))))

(defun codex-run ()
  (let* ((suite (or (getenv "CODEX_SUITE") "all"))
         (roslyn-lsp (getenv "ROSLYN_LSP_DLL"))
         (vbnet-lsp (getenv "VBNET_LSP_DLL"))
         (root (file-name-directory (or load-file-name buffer-file-name)))
         (log-dir (expand-file-name "logs" root))
         (log-file (expand-file-name
                    (format "emacs-eglot-%s.log" (format-time-string "%Y%m%dT%H%M%S"))
                    log-dir))
         (fixture-basic (expand-file-name "../../csharp-lsp/fixtures/basic/Basic/Class1.cs" root))
         (vb-fixture (expand-file-name "../../vbnet-lsp/fixtures/basic/Basic.vb" root)))
    (make-directory log-dir t)
    (setq codex--log-file log-file)
    (codex--log "Starting Emacs eglot smoke tests (suite=%s)" suite)
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
       nil))
    (codex--log "Emacs eglot smoke tests complete. Log: %s" log-file))
  (kill-emacs 0))

(codex-run)
