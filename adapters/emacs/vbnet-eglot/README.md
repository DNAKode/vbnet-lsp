# vbnet-eglot (thin Emacs adapter)

`vbnet-eglot` is a minimal Emacs package that wires the `VB.NET` language server
into Emacs `eglot`.

This package intentionally contains only editor wiring. Language behavior lives
in the server binaries published from this repository.

## Install

### package-vc (Emacs 29+)

```elisp
(package-vc-install
 '(vbnet-eglot :url "https://github.com/DNAKode/vbnet-lsp" :lisp-dir "adapters/emacs/vbnet-eglot"))
```

### MELPA (planned)

After moving this directory into a dedicated adapter repository, publish through
MELPA as `vbnet-eglot`.

## Configure

```elisp
(require 'vbnet-eglot)

;; Either keep dotnet host:
(setq vbnet-eglot-server-program '("dotnet" "/path/to/VbNet.LanguageServer.dll" "--stdio"))

;; Or use app host:
;; (setq vbnet-eglot-server-program '("/path/to/VbNet.LanguageServer" "--stdio"))

(vbnet-eglot-register)
```

Then open a `.vb` file and run:

```elisp
M-x eglot-ensure
```

If your VB major mode is not in `vbnet-eglot-major-modes`, add it:

```elisp
(add-to-list 'vbnet-eglot-major-modes 'your-vb-mode)
```
