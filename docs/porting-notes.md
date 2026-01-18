# `VB.NET` Porting Notes (C# -> VB.NET)

Version: 0.1
Last Updated: 2026-01-17
Status: Active Log

## Purpose

Capture unexpected or non-obvious porting details discovered while converting the codebase from C# to `VB.NET`. This is intended both as an internal checklist and as potential publishable guidance for others attempting a similar port.

## Notes

- 2026-01-17: Initialized porting notes log.
- 2026-01-17: VB compiler (net10.0) rejects `Utf8JsonReader` in custom `JsonConverter` ("Types with embedded references are not supported in this version of your compiler"). `JsonRpcId` cannot be directly serialized in VB; `JsonRpcRequest/Response.Id` now use `Object` and responses store the raw string/number to keep JSON output correct.
- 2026-01-18: VB string literals do not treat `\r\n` as escapes. Any wire-format or header parsing that relies on CRLF must use `vbCrLf` (e.g., named pipe/stdio `Content-Length` headers).
