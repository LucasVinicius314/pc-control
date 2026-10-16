# PC Control

A Windows 11 tray app that turns your phone into a TV-style remote for the PC, and gives the PC a
controller-driven fullscreen screen of its own. The phone opens a web page served by the PC over the home
Wi-Fi; there is nothing to install on the phone.

**On the phone:** trackpad and keyboard, app launcher and PowerToys Run, running programs (switch to or
close), open a link, a media remote, screen previews and screenshot, display scale, power actions with a
confirmation and a cancellable countdown, and a small task manager. The page follows your Windows theme.

**On the PC:** press the Xbox button on a controller for a Big-Picture-style screen with your RPCS3 games,
apps, media remote, running programs, performance, and power. Right-click the tray icon for
**Start with Windows** and **Open controller UI**.

What is left is in [TODO.md](TODO.md). How the parts work:
[launcher](docs/launcher.md), [display scale](docs/display-scale.md), [links, power and programs](docs/system.md),
[controller UI](docs/couch.md), [performance](docs/performance.md), [protocol](docs/protocol/README.md).

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

### Controller UI

Press the Xbox button on a controller (or use the tray menu) to open a fullscreen, controller-driven
screen on the PC with your RPCS3 games, apps, media keys, running programs and power options. See
[docs/couch.md](docs/couch.md).

### Performance

A small task manager on the phone (PC, then Performance) and in the controller UI: CPU, GPU, memory,
disk and network use, and the same per program. See [docs/performance.md](docs/performance.md).

### Theme

The phone page follows the PC's Windows theme: the accent colour you picked in Settings and dark or
light mode. It asks the PC (`GET /api/theme`) when it opens and each time it comes back to the
foreground, remembers the last answer so there is no flash of other colours, and nudges an accent
that would be hard to see on the background. If the PC cannot be asked it keeps the default blue.

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
