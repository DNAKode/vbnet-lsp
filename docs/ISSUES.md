# Issues Log

This file tracks reported issues to revisit later. No fixes are applied here.

## Marketplace listing
- Icon still shows a white background outside rounded corners in marketplace listing (should be transparent).
- README is not showing in the marketplace listing.
- Categories: currently includes "Programming Languages" (ok), "Snippets" (questionable), "Linters" (maybe).

## Completion behavior
- (Fixed) Typing `Dim x As ` could result in `Dim x AAs `.
- (Fixed) Reported to occur when tab-completing through the suggestion list as well.

## Debugging
- Debugging does not appear to work (details to capture later).
- Track upstream netcoredbg stackTrace `0x80004002` bug/fix weekly:
  - issue: https://github.com/Samsung/netcoredbg/issues/215
  - PR: https://github.com/Samsung/netcoredbg/pull/216
  - action: drop `vbnet` proxy workaround once a fixed netcoredbg release is available and bundled.

## Release notes
- Start a published changelog and include a "Known Issues" section.
