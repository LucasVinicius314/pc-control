# PC Control

A Windows 11 tray app (C#, .NET 10) that turns a phone into a TV-style remote, and shows a controller-driven
fullscreen UI on the PC itself. The PC serves two TypeScript web pages from one server on port 48123: the phone
remote (`index.html`) and the controller UI (`couch.html`, shown in a WebView2 window). Work is on
`rewrite/pc-control` and merged to `master`. Leftover work is in `TODO.md`.

## Layout

* `src/PcControl.Core`: logic with no Win32 code (pairing, protocol, input, apps, capture planning). Unit-testable anywhere.
* `src/PcControl.Windows`: Win32 behind Core interfaces (SendInput, GDI and DXGI capture, shell apps, display scale). CsWin32 generates the P/Invoke code from `NativeMethods.txt` / `NativeMethods.json`.
* `src/PcControl.Host`: tray app (WinForms), Kestrel server, auth middleware, WebSocket `/ws`, and `Couch/` (the controller UI's WebView2 window and the Xbox-button watcher wiring).
* `src/PcControl.Web`: both pages (Vite + TypeScript, no framework; `src/couch/` is the controller UI). Built by the Host csproj and embedded in the exe.
* `tests/PcControl.Tests`: xUnit (unit plus integration through `TestServer`). `docs/protocol`: the versioned protocol contract.

## Rules

* Nullable on, warnings are errors, analyzers on. Fix the warning; do not suppress it.
* Every Win32 call sits behind an interface so the rest can be tested with fakes.
* Tray app, not a service (it needs the user's desktop). One instance per data folder.
* Security is a requirement: PIN pairing, hashed device tokens, private-IP only, Host/Origin checks, kill switch ("Pause remote control") that releases input and answers `409 paused`. The phone sends only opaque app IDs and key names from `KeyCatalog`; never raw launch strings or virtual-key codes.
* Protocol changes go in `docs/protocol` (written so an Android app could use it later). Keep README and docs current.
* Out of scope by decision: virtual controller, audio device switcher. The only display feature is the DPI toggle.
* The phone page's CSP blocks inline style attributes; set styles through the CSSOM.
* Colours on the phone page come from CSS variables that `theme.ts` sets from the PC's Windows theme (accent, dark / light). Use `var(--accent)`, `var(--on-accent)` etc., never hard-coded colours, so both modes work.

## Build and test

```powershell
dotnet build                     # also runs npm ci + vite build
dotnet test
(cd src/PcControl.Web; npm test)
```

* Debugging in VS Code locks the build output, so build and test elsewhere with `dotnet test --artifacts-path <dir>`.
* Tests that touch the real PC are off by default: `PCCONTROL_REAL_INPUT=1` (moves the mouse, reads the screen), `PCCONTROL_REAL_DPI_CHANGE=1` (really changes the display scale).
* `PCCONTROL_DATA_DIR` points the app at another data folder (settings, devices, logs). .NET ignores a changed `%APPDATA%`.
* Use the Write/Edit tools for big files; long shell heredocs often fail to parse.

## Things learned the hard way

* Do not set `InvariantGlobalization`; it breaks accent-aware sorting.
* Capture: DXGI Desktop Duplication by default, GDI as fallback (`capture.method` in config.json). GDI with CAPTUREBLT may make the cursor flicker. A rotated monitor can hand over a sideways picture; the rotation is decided from the first real texture's size. A second duplication of one output in a process fails, and the process must be per-monitor DPI aware.
* `ReceiveAsync` cancellation aborts a WebSocket; revoking a device sends a 4401 close frame instead.
* WinForms windows must be created on the UI thread (the pairing window uses a hidden control and `BeginInvoke`).
* Shell COM (icons, app list) needs STA threads; icon rendering is gated by a semaphore.
* Controller UI: a WebView2 window cannot be made see-through (`Form.Opacity` and DWM acrylic both make its content vanish). The "transparency" is a screenshot taken just before it opens (GDI, not DXGI: the first DXGI frame is blank), blurred behind the page. Browsers cannot see the Xbox button; `XInputWatcher` reads it through XInput's undocumented ordinal 100.
* Controller UI layout: `scrollIntoView` also scrolls `overflow:hidden` ancestors, which made the whole page jump sideways. Use `overflow: clip` on the outer containers, and `scroll-padding` so the focused item is never under the tabs or the hints bar. Steam's controller settings can also turn buttons into key presses; the page ignores the keyboard once it has seen a controller.
* Performance: protected programs refuse to be opened, and every refusal is an exception (thousands in a debugger). The sampler asks once per program and remembers who refused.
* The shell tool often fails on long heredocs; write big edits to a script file with the Write tool and run that.
* The user usually has PcControl running (and "Start with Windows" points at the Debug exe), which locks `bin\Debug\...\PcControl.exe`, so a plain build fails to copy it. Never kill their copy; build to another folder (`dotnet build src/PcControl.Host --artifacts-path <dir>`) and run that one. `tests\PcControl.Tests\bin\...\PcControl.exe` is only a copy the test build made, not the app to run.
* To look at a controller UI change, run the built exe with `PCCONTROL_DATA_DIR` set to an empty folder and a `config.json` with another `port`, read the PIN from its window, pair over `/api/pair`, and screenshot `/couch.html` in headless Chrome (1920x1080) with the section stored in `localStorage`. It shows real data from the PC.
* Controller UI scrolling is our own (`couch/smoothscroll.ts`): the browser's smooth `scrollIntoView` is switched off by Windows' "animation effects" setting and cut short by every key press.
* Controller batteries: XInput's `XInputGetBatteryInformation` calls a Bluetooth Xbox controller "disconnected". Windows' gamepad API (`Windows.Gaming.Input.Gamepad`, `TryGetBatteryReport`) has the real percentage, but its list fills a moment after the first look, so the watcher retries soon when a reading is missing.
* Window pictures use `PrintWindow` with `PW_RENDERFULLCONTENT` (the constant is not in CsWin32's enum, so it is cast). Minimised windows draw nothing; an all-black result is treated as no picture.
* Emulators: PCSX2's BIOS folder in `PCSX2.ini` is relative to its data folder, so moving PCSX2 breaks it quietly; the app checks and says so. PS2 covers are downloaded once by serial from a public GitHub collection into PCSX2's `covers` folder (`games.pcsx2DownloadCovers` turns it off). Keep outgoing network calls rare and documented.

## Roadmap

Everything planned is done except the polish items (installer, firewall rule, HTTPS decision) and a few ideas.
They are listed in `TODO.md`. Milestone 6 (a separate game launcher) and 8 (a phone gamepad overlay) were
dropped: games live in the controller UI, and the Xbox controller plugs into the PC.
