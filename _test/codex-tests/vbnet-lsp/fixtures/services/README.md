# VB.NET Services Fixtures

This folder contains fixture files and manifests used to scaffold tests for
upcoming language services (completion, hover, definition, references, rename,
and symbols). These are planning artifacts and are not executed yet.

## Markers

Markers are inline comments with the format:

```
' MARKER: <id>
```

The intended test harness should locate the marker, compute the LSP position
immediately before the comment on that line, and issue the corresponding
request listed in `service-tests.json`.
