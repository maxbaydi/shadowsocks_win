# VibeShadowsocks

VibeShadowsocks is a WinUI 3 (.NET 8) desktop wrapper for `shadowsocks-rust` (`sslocal.exe`).

The app does not implement any Shadowsocks protocol/crypto logic. It only manages `sslocal.exe`, routing modes, PAC, tray, hotkey, and transactional Windows proxy settings.

## Solution Layout

- `src/VibeShadowsocks.App` - WinUI 3 UI, MVVM, DI host, pages.
- `src/VibeShadowsocks.Core` - domain models, validation, PAC generator/parser, single `ConnectionOrchestrator`.
- `src/VibeShadowsocks.Infrastructure` - `sslocal` process management, WinINet proxy transaction, PAC server, settings, secure storage, diagnostics.
- `src/VibeShadowsocks.Platform` - Win32 integration: tray, hotkey, single-instance activation.
- `tests/VibeShadowsocks.Tests` - xUnit tests for orchestrator, PAC, atomic write.

## Prerequisites

- Windows 10/11 x64.
- Visual Studio 2022 with WinUI 3 / Windows App SDK tooling.
- .NET SDK 8.x.

## sslocal.exe placement

Use one of the options:

1. Put `sslocal.exe` into `tools/sslocal/sslocal.exe` near app binaries.
2. Set full path in **Settings** page (`sslocal.exe path`).

## Build and Run

From repository root:

```powershell
dotnet clean
dotnet build -c Release
dotnet test -c Release
```

If WinUI build fails via `dotnet build`, use MSBuild from VS Build Tools:

```powershell
msbuild VibeShadowsocks.sln /p:Configuration=Release /m
```

## Routing Modes

- `Off`: no system proxy/PAC changes.
- `Global`: applies `ProxyServer=socks=127.0.0.1:<port>`.
- `PAC`: applies `AutoConfigURL` (local managed PAC or remote HTTPS PAC URL).

## Crash Safety

- All connect/disconnect/proxy/PAC operations go through one `ConnectionOrchestrator` queue.
- `SystemProxyManager` uses transaction snapshot + dirty flag.
- On failure/unexpected `sslocal` exit/startup recovery, proxy is rolled back to pre-connect snapshot.

## Settings and Secrets

- Settings are stored in `%LocalAppData%\VibeShadowsocks\settings.json` with schema versioning.
- Password secrets are stored separately using DPAPI in `%LocalAppData%\VibeShadowsocks\secrets`.
- Passwords and raw `ss://` secrets are not logged.

## Autostart

Autostart toggle is present in settings UI. Integrating with Windows startup registration can be added in `Platform` layer.

## Known Limitations

- Some applications ignore WinINet system proxy settings.
- Remote PAC and remote rule sources require HTTPS.
- WinUI build requires Windows App SDK tooling on the machine.
