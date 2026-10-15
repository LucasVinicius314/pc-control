# Display scale toggle

The phone page has a button that switches a monitor between two scales (100% and 200% by default)
and shows the current one. This is what makes it work, and what to do if it stops.

## How the scale is read and changed

Windows has no documented way for a program to change the display scale. This app tries three
backends in order (`src/PcControl.Windows`):

| Backend | Reads | Changes | Uses |
| --- | --- | --- | --- |
| `display-config` | yes | yes | `DisplayConfigGetDeviceInfo` / `DisplayConfigSetDeviceInfo` with the request types -3 (read) and -4 (set). Microsoft has not put these in the SDK; the numbers and packet layouts come from the open-source [windows-DPI-scaling-sample](https://github.com/lihas/windows-DPI-scaling-sample) that SetDPI is built on. |
| `effective-dpi` | yes | no | The documented `GetDpiForMonitor`. Independent of the undocumented calls, so it keeps working after a Windows update and is used to confirm what a change did. |
| `script` | no | yes | A script or program you name in `dpi.fallbackScriptPath`. Only used if the first backend cannot change the scale. |

Windows counts scales in steps from the monitor's recommended value, not in percentages. The step
table is 100, 125, 150, 175, 200, 225, 250, 300, 350, 400, 450, 500. The packet layouts (20-byte header,
32-byte read packet, 24-byte write packet) are checked at run time, and on this PC the table was
cross-checked against `GetDpiForMonitor` for every monitor (the `DisplayScaleBackendTests` tests).

## Startup self-test

When the app starts it reads the target monitor's scale. If no backend can answer, or nothing is set
up to change it, the toggle is switched off: the button disappears from the phone page, the tray log
(`%APPDATA%\PcControl\logs`) says why, and `GET /api/dpi` returns `available: false` with a
`reason`. It never guesses.

## What the toggle does

- A tap on the button sends `POST /api/dpi/toggle`. If the monitor is at the low scale it goes to the
  high one; at any other scale it goes to the low one.
- The answer comes only after Windows reports the new scale (up to about 4 seconds), so the badge
  never shows a change that did not happen.
- Two quick taps do not undo each other: the second one is refused (`409 busy`).
- A monitor that does not allow the high scale (they differ: the largest depends on resolution and
  shape) is refused with the reason, and nothing is changed.
- Some apps stay blurry until they are restarted. That is how Windows handles scale changes; the
  page says so.

## Settings (`%APPDATA%\PcControl\config.json`)

```json
"dpi": {
  "monitor": "primary",
  "lowPercent": 100,
  "highPercent": 200,
  "fallbackScriptPath": null
}
```

- `monitor`: `"primary"` (default), `"cursor"` (whichever monitor the mouse is on) or a monitor number
  from 0 (left to right). Monitors can allow different scales, so pick the one you actually want to
  change.
- `lowPercent`, `highPercent`: any two scales from the table above, low smaller than high.
- `fallbackScriptPath`: see below. The path comes only from this file, never from the phone.

## If it stops working

A Windows update can change the undocumented calls. Then the log shows "Display scale toggle switched
off". Options:

1. Point `dpi.fallbackScriptPath` at your own script (for example the AutoHotkey one that drives
   Settings). The toggle then uses it to change the scale, and still confirms the result through
   `GetDpiForMonitor`.
2. Report it, with the Windows version, so the calls can be updated.

## Not built: driving Settings with UI Automation

The project plan listed UI Automation of the Settings app as a second fallback. It is not built:
the control names depend on the Windows display language, they change between Windows versions, and
it cannot be tested reliably here. A user script as the fallback covers the same need with less to
break. It can be added if the main path breaks and no script will do.
