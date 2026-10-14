# PC Control

A Windows 11 tray app that turns your phone into a TV-style remote for the PC: trackpad, keyboard,
app and game launcher, cursor magnifier and a DPI toggle. The phone opens a web page served by the PC
over the home Wi-Fi; there is nothing to install on the phone.

Status: **milestone 2 of 9** (trackpad and keyboard). See the build order in the project plan.

## Layout

| Path | Contents |
| --- | --- |
| `src/PcControl.Core` | Models, pairing, network rules, protocol, input logic. No Win32 code. |
| `src/PcControl.Windows` | Win32 implementations (SendInput, DPI awareness) behind Core interfaces |
| `src/PcControl.Host` | Tray app, Kestrel server, auth, WebSocket handler |
| `src/PcControl.Web` | TypeScript phone page (Vite). Embedded into the executable at build time. |
| `tests/PcControl.Tests` | xUnit unit tests and integration tests against the real server |
| `docs/protocol` | The versioned protocol contract |

## Build and run

Needs the .NET 10 SDK and Node.js 20+.

```powershell
dotnet build            # also builds the phone page (npm ci + vite build)
dotnet test
(cd src/PcControl.Web && npm test)   # gesture, keyboard and modifier tests
dotnet run --project src/PcControl.Host
```

Settings live in `%APPDATA%\PcControl\config.json` (created on first run), logs in
`%APPDATA%\PcControl\logs`, paired devices (token hashes only) in `devices.json`. Set the
`PCCONTROL_DATA_DIR` environment variable to use a different folder instead.

### Tests that touch the real PC

`SendInputInjectorTests` moves the actual mouse, so it is off unless you ask for it:

```powershell
$env:PCCONTROL_REAL_INPUT = "1"; dotnet test --filter SendInputInjectorTests
```
