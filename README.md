# PC Control

A Windows 11 tray app that turns your phone into a TV-style remote for the PC: trackpad, keyboard,
app and game launcher, cursor magnifier and a DPI toggle. The phone opens a web page served by the PC
over the home Wi-Fi; there is nothing to install on the phone.

Status: **milestone 1 of 9** (skeleton and pairing). See the build order in the project plan.

## Layout

| Path | Contents |
| --- | --- |
| `src/PcControl.Core` | Models, pairing, network rules, protocol. No Win32 code. |
| `src/PcControl.Host` | Tray app, Kestrel server, auth, WebSocket handler |
| `src/PcControl.Web` | TypeScript phone page (Vite). Embedded into the executable at build time. |
| `tests/PcControl.Tests` | xUnit unit tests and integration tests against the real server |
| `docs/protocol` | The versioned protocol contract |

## Build and run

Needs the .NET 10 SDK and Node.js 20+.

```powershell
dotnet build            # also builds the phone page (npm ci + vite build)
dotnet test
dotnet run --project src/PcControl.Host
```

Settings live in `%APPDATA%\PcControl\config.json` (created on first run), logs in
`%APPDATA%\PcControl\logs`, paired devices (token hashes only) in `devices.json`.
