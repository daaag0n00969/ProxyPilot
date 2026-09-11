# Contributing

## Dev setup

- Windows x64, .NET 8 SDK
- `dotnet test ProxyPilot.slnx -c Release`

Run the UI elevated. Unprivileged `dotnet test` does not load WinDivert.

## Style

- Match existing C# (file-scoped namespaces, nullable enabled).
- Keep WinDivert filters free of `not (...)` groups — the compiler rejects them (`ERROR_INVALID_PARAMETER` / Win32 87).
- Log Steam/cloud/proxy paths to `FileLog`; do not flood the log with unrelated DNS (Kaspersky, WPAD, …).

## Pull requests

Small, focused diffs. Include a test when you change rules, DNS, or SOCKS framing.
