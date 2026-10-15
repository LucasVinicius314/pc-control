# App launcher and PowerToys Run

The **Apps** button on the phone page opens the Start menu as a list: favorites and recently used
apps on top, then every app alphabetically, with their icons. Tap an app to start it on the PC. Type
in the search box to filter as you type; "Go" on the phone keyboard starts the best match.

## Where the list comes from

The list is Windows' own Apps folder (`shell:AppsFolder`), read through the shell, which is the same
set PowerShell's `Get-StartApps` returns. It includes Store apps, classic programs, shortcuts you
made, and links such as game launchers. On the development PC the two lists were compared directly:
381 entries on both sides, every name and launch id identical (`ShellAppCatalogTests`).

The list is read again each time the launcher opens, so newly installed apps show up. Icons are
converted to PNG once and kept in `%APPDATA%\PcControl\icons` for 12 hours.

## How apps are identified (and why the phone cannot start anything else)

Windows identifies Start menu entries by strings that are awkward or unsafe to pass around: a packaged
app id, a file path, a GUID-prefixed path, a `steam://` link. Two apps can also share a name. So:

- The phone only ever sees an opaque **id**: 16 hex characters from a hash of the launch string.
- To start an app the phone sends that id; the PC looks it up in the list it just read and starts the
  matching entry. An id that is not in the list, or a raw launch string, is refused with `404`.
- Nothing from the phone ever reaches a command line.

Launches are logged with the device name. Starting apps is refused while the tray's **Pause remote
control** switch is on (`409 paused`); so are the PowerToys Run button and the display scale toggle.

## Favorites and recent apps

Kept in `%APPDATA%\PcControl\apps.json` (human readable, written atomically). The last 20 launches are
remembered (`launcher.recentCount`, 1 to 50). An app that is uninstalled drops out of Recent but stays a
favorite, in case it comes back. A damaged file is moved aside as `apps.json.corrupt` and the app starts
with empty lists instead of refusing to start.

## Search

Done on the phone, over the list it already has. A word matches best as a whole name, then as the
start of the name, the start of a word, anywhere in a word, as the first letters of consecutive words
("vsc" for Visual Studio Code, "gc" for Google Chrome), or as letters in order that are not too spread
out ("vscode"). Every word you type must match. Case and accents are ignored, so "cafe" finds "Café".

## PowerToys Run button

The button in the launcher presses the shortcut that opens PowerToys Run on the PC, then opens the
phone's keyboard so you can type into it.

Setting: `launcher.powerToysRunShortcut` in `%APPDATA%\PcControl\config.json`.

- `"auto"` (default): read the shortcut from PowerToys' own settings
  (`%LOCALAPPDATA%\Microsoft\PowerToys\PowerToys Run\settings.json`, `open_powerlauncher`) every time
  the button is pressed. If you change it in PowerToys, the button follows without restarting this app.
  If PowerToys is not found, Alt+Space (PowerToys' default) is used.
- Or write it yourself: `"Alt+Space"`, `"Win+Shift+Space"`, `"Ctrl+Alt+K"`. Modifiers are Ctrl, Alt,
  Shift and Win; the key is a letter, digit, F-key or a name such as Space or Enter.

On the development PC PowerToys Run is set to **Win+Shift+Space**, not Alt+Space, which is why "auto"
is the default: a fixed Alt+Space would have done nothing there. Pressing the injected shortcut was
checked against the real PowerToys, and its Run window did open.
