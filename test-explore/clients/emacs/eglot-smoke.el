(setq inhibit-message nil)
(setq vc-handled-backends nil)
(require 'eglot)
(require 'cc-mode)
(require 'jsonrpc)

(setq jsonrpc-request-timeout 20)
(setq eglot-events-buffer-size 2000000)

(defun codex--normalize-glob (glob)
  (if (and (listp glob) (plist-member glob :pattern))
      (plist-get glob :pattern)
    glob))

(advice-add
 'eglot--glob-compile
 :around
 (lambda (orig glob &rest args)
   (apply orig (codex--normalize-glob glob) args)))

(defvar codex--log-file nil)

(defun codex--log (format-string &rest args)
  (let ((message-text (apply #'format format-string args)))
    (message "%s" message-text)
    (when codex--log-file
      (with-temp-buffer
        (insert message-text "\n")
        (write-region (point-min) (point-max) codex--log-file 'append 'silent)))))

(cl-defmethod eglot-handle-request
  (server (_method (eql client/registerCapability)) &key registrations)
  (codex--log "Ignoring registerCapability request (%s registrations)" (length registrations))
  nil)

(defun codex--dump-events (server name)
  (when (and codex--log-file (fboundp 'jsonrpc-events-buffer))
    (condition-case err
        (let ((events (jsonrpc-events-buffer server)))
          (when (buffer-live-p events)
            (with-current-buffer events
              (write-region
               (point-min)
               (point-max)
               codex--log-file
               'append
               'silent)
              (codex--log "Captured jsonrpc events for %s" name))))
      (error
       (codex--log "Failed to capture jsonrpc events for %s: %s" name err)))))

(defun codex--wait-for-event (server regex timeout-seconds)
  (let ((deadline (+ (float-time) timeout-seconds))
        (found nil))
    (while (and (not found) (< (float-time) deadline))
      (when (fboundp 'jsonrpc-events-buffer)
        (let ((events (jsonrpc-events-buffer server)))
          (when (buffer-live-p events)
            (with-current-buffer events
              (save-excursion
                (goto-char (point-min))
                (setq found (re-search-forward regex nil t)))))))
      (unless found
        (sleep-for 0.5)))
    found))

(defun codex--file-uri (path)
  (concat "file:///" (replace-regexp-in-string "\\\\" "/" (expand-file-name path) t t)))

(defun codex--position-at (buffer token)
  (with-current-buffer buffer
    (goto-char (point-min))
    (let ((found (search-forward token nil t)))
      (unless found
        (error "Token not found: %s" token))
      (let* ((pos (- (point) (length token)))
             (line (1- (line-number-at-pos pos)))
             (col (progn (goto-char pos) (current-column))))
        (list :line line :character col)))))

(defun codex--send-did-open (server file-path language-id)
  (let ((text (with-temp-buffer
                (insert-file-contents file-path)
                (buffer-string)))
        (uri (codex--file-uri file-path)))
    (jsonrpc-notify server 'textDocument/didOpen
                    `(:textDocument (:uri ,uri :languageId ,language-id :version 1 :text ,text)))))

(defun codex--request (server method params)
  (let ((rpc-method (if (stringp method) (intern method) method)))
    (condition-case err
        (cons t (jsonrpc-request server rpc-method params))
    (jsonrpc-error
     (codex--log "Request %s failed: %s" method err)
     (cons nil nil))
    (error
     (codex--log "Request %s failed: %s" method err)
     (cons nil nil)))))

(defun codex--request-with-timeout (server method params timeout-seconds)
  (let ((result (cons nil nil)))
    (with-timeout (timeout-seconds
                   (codex--log "Request %s timed out after %ss" method timeout-seconds))
      (setq result (codex--request server method params)))
    result))

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

(defun codex--shutdown-server (server name)
  (with-timeout (10
                 (codex--log "Shutdown timed out for %s (forcing process exit)." name)
                 (let ((proc (and (fboundp 'jsonrpc-process) (jsonrpc-process server))))
                   (when (and proc (process-live-p proc))
                     (ignore-errors (delete-process proc)))))
    (condition-case err
        (eglot-shutdown server)
      (error
       (codex--log "Shutdown request failed for %s: %s" name err)))))

(defun codex--run-eglot-test (name mode server-command file-path request-hover strict solution-path)
  (condition-case err
      (let ((buffer (find-file-noselect file-path)))
        (unwind-protect
            (with-current-buffer buffer
              (let ((success t)
                    (default-directory (file-name-directory (expand-file-name file-path)))
                    (process-connection-type (string= name "csharp")))
                (funcall mode)
                (when (string= name "vbnet")
                  (setq-local eglot-language-id "vb")
                  (let* ((project-path (expand-file-name "SmallProject.vbproj" default-directory))
                         (init-options `(:workspace
                                         (:projectPaths [,project-path]
                                          :projectSearchPaths [,default-directory]
                                          :ignoreSolutionFiles t
                                          :maxProjectResults 5))))
                    (setq server-command (append server-command (list :initializationOptions init-options)))
                    (codex--log "Using initializationOptions for VB.NET: %s" init-options)))
                (setq eglot-server-programs `((,mode . ,server-command)))
                (codex--log "eglot server programs: %s" eglot-server-programs)
                (codex--log "major-mode: %s" major-mode)
                (let ((server (apply #'eglot--connect (eglot--guess-contact))))
                  (sleep-for 3)
                  (unless server
                    (error "No eglot server for %s (mode=%s)" name major-mode))
                  (unwind-protect
                      (progn
                        (when (and solution-path (string= name "csharp"))
                          (let ((solution-uri (codex--file-uri solution-path)))
                            (codex--log "Sending solution/open for %s" solution-uri)
                            (jsonrpc-notify server 'solution/open `(:solution ,solution-uri))
                            (sleep-for 2)))
                        (when (string= name "vbnet")
                          (if (codex--wait-for-event server "Project loaded:" 30)
                              (codex--log "VB.NET project load detected")
                            (codex--log "VB.NET project load not detected within timeout"))
                          (sleep-for 2))
                        (when request-hover
                          (codex--send-did-open server file-path (if (string= name "csharp") "csharp" "vb"))
                          (sleep-for 1)
                          (when (string= name "vbnet")
                            (sleep-for 8))
                          (let* ((request-timeout (if (string= name "vbnet") 20 10))
                                 (jsonrpc-request-timeout (if (string= name "vbnet") 60 jsonrpc-request-timeout))
                                 (pos (codex--position-at buffer "Add(1, 2)"))
                                 (params `(:textDocument (:uri ,(codex--file-uri file-path))
                                           :position ,pos))
                                 (hover (codex--request-with-timeout server "textDocument/hover" params request-timeout))
                                 (definition (codex--request-with-timeout server "textDocument/definition" params request-timeout)))
                            (when (or (not (car hover)) (null (cdr hover)) (eq (cdr hover) :null))
                              (when strict
                                (setq success nil))
                              (codex--log "Hover response empty for %s" name))
                            (when (or (not (car definition)) (null (cdr definition)) (eq (cdr definition) :null))
                              (when strict
                                (setq success nil))
                              (codex--log "Definition response empty for %s" name)))))
                    (progn
                      (if (codex--server-live-p server)
                          (codex--shutdown-server server name)
                        (codex--log "Shutdown timed out for %s after server exit." name))
                      (codex--log-server-exit server name)
                      (codex--dump-events server name))))
                success))
          (ignore-errors (kill-buffer buffer))))
    (error
     (codex--log "Test crashed for %s: %S" name err)
     nil)))

(defun codex-run ()
  (condition-case err
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
             (csharp-log-dir (expand-file-name "../../../_external/csharp-lsp/logs" root))
             (fixture-basic (expand-file-name "../../../_external/csharp-lsp/fixtures/basic/Basic/Class1.cs" root))
             (csharp-sln (expand-file-name "../../../_external/csharp-lsp/fixtures/basic/Basic.sln" root))
             (vb-fixture (expand-file-name "../../../test/TestProjects/SmallProject/Helper.vb" root)))
        (make-directory log-dir t)
        (when (and roslyn-lsp csharp-log-dir)
          (make-directory csharp-log-dir t))
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
            (list "dotnet" roslyn-lsp "--stdio" "--logLevel" "Information" "--extensionLogDirectory" csharp-log-dir)
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
          (kill-emacs (if overall-success 0 1))))
    (error
     (message "Emacs eglot harness error: %S" err)
     (kill-emacs 1))))

(codex-run)
