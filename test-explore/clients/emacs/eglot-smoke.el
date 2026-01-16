(setq inhibit-message nil)
(setq vc-handled-backends nil)
(require 'eglot)
(require 'cc-mode)
(require 'jsonrpc)

(setq jsonrpc-request-timeout 10)
(setq process-connection-type nil)

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

(defun codex--request (server method params)
  (condition-case err
      (cons t (jsonrpc-request server method params))
    (jsonrpc-error
     (codex--log "Request %s failed: %s" method err)
     (cons nil nil))
    (error
     (codex--log "Request %s failed: %s" method err)
     (cons nil nil))))

(defun codex--server-live-p (server)
  (if (fboundp 'jsonrpc-process)
      (let ((proc (jsonrpc-process server)))
        (and proc (process-live-p proc)))
    nil))

(defun codex--log-server-exit (server name)
  (if (fboundp 'jsonrpc-process)
      (let ((proc (jsonrpc-process server)))
        (when proc
          (let ((status (process-status proc)))
            (when (memq status '(exit signal))
              (codex--log "Server process for %s exited (%s) with status %s"
                          name status (process-exit-status proc)))))))
  nil)

(defun codex--run-eglot-test (name mode server-command file-path request-hover strict solution-path)
  (let ((buffer (find-file-noselect file-path)))
    (unwind-protect
        (with-current-buffer buffer
          (let ((success t)
                (default-directory (file-name-directory (expand-file-name file-path))))
            (condition-case err
                (progn
                  (funcall mode)
                  (when (string= name "vbnet")
                    (setq-local eglot-language-id "vb"))
                  (setq eglot-server-programs `((,mode . ,server-command)))
                  (codex--log "eglot server programs: %s" eglot-server-programs)
                  (codex--log "major-mode: %s" major-mode)
                  (let ((server (apply #'eglot--connect (eglot--guess-contact))))
                    (sleep-for 3)
                    (unless server
                      (error "No eglot server for %s (mode=%s)" name major-mode))
                    (when (and solution-path (string= name "csharp"))
                      (let ((solution-uri (codex--file-uri solution-path)))
                        (codex--log "Sending solution/open for %s" solution-uri)
                        (jsonrpc-notify server "solution/open" `(:solution ,solution-uri))
                        (sleep-for 2)))
                    (when request-hover
                      (let* ((pos (codex--position-at buffer "Add(1, 2)"))
                             (params `(:textDocument (:uri ,(codex--file-uri file-path))
                                       :position ,pos))
                             (hover (codex--request server "textDocument/hover" params))
                             (definition (codex--request server "textDocument/definition" params)))
                        (when (or (not (car hover)) (null (cdr hover)) (eq (cdr hover) :null))
                          (when strict
                            (setq success nil))
                          (codex--log "Hover response empty for %s" name))
                        (when (or (not (car definition)) (null (cdr definition)) (eq (cdr definition) :null))
                          (when strict
                            (setq success nil))
                          (codex--log "Definition response empty for %s" name))))
                    (condition-case err
                        (eglot-shutdown server)
                      (jsonrpc-error
                       (if (codex--server-live-p server)
                           (codex--log "Shutdown request failed for %s: %s" name err)
                         (codex--log "Shutdown timed out for %s after server exit." name))))
                    (codex--log-server-exit server name)))
              (error
               (setq success nil)
               (codex--log "Test failed for %s: %s" name err)))
            success))
      (kill-buffer buffer))))

(defun codex-run ()
  (let* ((suite (or (getenv "CODEX_SUITE") "all"))
         (roslyn-lsp (getenv "ROSLYN_LSP_DLL"))
         (root (file-name-directory (or load-file-name buffer-file-name)))
         (default-vbnet (expand-file-name "../../src/VbNet.LanguageServer/bin/Debug/net10.0/VbNet.LanguageServer.dll" root))
         (vbnet-lsp (or (getenv "VBNET_LSP_DLL")
                        (and (file-exists-p default-vbnet) default-vbnet)))
         (log-dir (expand-file-name "logs" root))
         (log-file (expand-file-name
                    (format "emacs-eglot-%s.log" (format-time-string "%Y%m%dT%H%M%S"))
                    log-dir))
         (fixture-basic (expand-file-name "../../../_external/csharp-lsp/fixtures/basic/Basic/Class1.cs" root))
         (csharp-sln (expand-file-name "../../../_external/csharp-lsp/fixtures/basic/Basic.sln" root))
         (vb-fixture (expand-file-name "../../../test/TestProjects/SmallProject/Helper.vb" root)))
    (make-directory log-dir t)
    (setq codex--log-file log-file)
    (codex--log "Env ROSLYN_LSP_DLL=%s" (or roslyn-lsp ""))
    (codex--log "Env VBNET_LSP_DLL=%s" (or vbnet-lsp ""))
    (codex--log "Starting Emacs eglot smoke tests (suite=%s)" suite)
    (let ((overall-success t))
      (when (and (or (string= suite "csharp") (string= suite "all")) roslyn-lsp)
        (codex--log "Running csharp eglot test")
        (setq overall-success
              (and overall-success
       (codex--run-eglot-test
        "csharp"
        'csharp-mode
        (list "dotnet" roslyn-lsp "--stdio" "--logLevel" "Information" "--extensionLogDirectory" (expand-file-name "logs" root))
        fixture-basic
        t
        nil
        csharp-sln))))
      (codex--log "VB.NET enabled=%s" (and (or (string= suite "vbnet") (string= suite "all")) vbnet-lsp))
      (when (and (or (string= suite "vbnet") (string= suite "all")) vbnet-lsp)
        (codex--log "Running vbnet eglot test")
        (setq overall-success
              (and overall-success
                   (codex--run-eglot-test
        "vbnet"
        'fundamental-mode
        (list "dotnet" vbnet-lsp "--stdio" "--logLevel" "Information")
        vb-fixture
        t
        nil
        nil))))
      (codex--log "Emacs eglot smoke tests complete. Log: %s" log-file)
      (kill-emacs (if overall-success 0 1)))))

(codex-run)
