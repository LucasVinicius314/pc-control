# PC Control

A Windows 11 tray app that turns your phone into a TV-style remote for the PC: trackpad, keyboard,
app and game launcher, cursor magnifier and a DPI toggle. The phone opens a web page served by the PC
over the home Wi-Fi; there is nothing to install on the phone.

Status: **milestones 1 to 5 of 9, plus media controls** (pairing, trackpad and keyboard, app launcher
and PowerToys Run, display scale toggle, screen previews and screenshot, a media panel with
play/pause, tracks, seek, fullscreen and volume). Still to come: URL / power / processes, the controller
overlay (which will also hold the game features) and the installer. Right-click the tray icon and tick **Start with Windows** to start it at sign-in (it uses the per-user Run key, so Windows' own Startup settings can switch it off too). See [docs/launcher.md](docs/launcher.md) and
[docs/display-scale.md](docs/display-scale.md) for how those work and what to do if they stop.

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

### How the screen is captured

The previews read the screen with DXGI Desktop Duplication: Windows says when something changed
and where, so a still screen costs nothing, and the mouse cursor is left alone. If that is not
possible (some remote sessions, protected content, HDR) the app falls back to the older GDI capture
by itself and writes one line about it to the log. The older method re-reads the whole screen every
time and can make the cursor flicker on some systems, so it is only worth forcing for a comparison:

```json
{ "capture": { "method": "gdi" } }
```

`"auto"` is the default. The log's first lines say which method is in use.

### Tests that touch the real PC

`SendInputInjectorTests` moves the actual mouse and `GdiScreenCapturerTests` reads the real screen,
so both are off unless you ask for them:

```powershell
$env:PCCONTROL_REAL_INPUT = "1"; dotnet test --filter "SendInputInjectorTests|GdiScreenCapturerTests|DxgiCapturerMatchesGdi|DisplayScaleBackendTests|DisplayScaleReport|ShellAppCatalogTests"
```

`RealDisplayScaleChange` really switches the primary monitor to 200% and back (windows on it will
resize), so it has its own switch:

```powershell
$env:PCCONTROL_REAL_DPI_CHANGE = "1"; dotnet test --filter RealDisplayScaleChange
```
