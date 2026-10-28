# The controller UI

A fullscreen, controller-driven screen on the PC itself, in the spirit of Steam's Big Picture. It shows
your games, apps, media keys, running programs and the PC's power and display scale, and is operated
with an Xbox controller. It is a web page served by this app (`/couch.html`), shown in a frameless
WebView2 window that exists only while it is open.

## Opening and closing it

* Press the **Xbox (Guide) button** on a controller. It works over a running game too. Press it again,
  or **B**, to close it.
* Or tray icon, then **Open controller UI**.
* To have it open when PC Control starts, set `"couch": { "openAtStart": true }` in `config.json`.
  (Use the tray's **Start with Windows** to start PC Control itself with Windows.)

Windows' Game Bar also listens to the Xbox button. If both appear, turn off Settings, Gaming, Game
Bar, "Allow your controller to open Game Bar". If you prefer not to use the button at all, set
`"couch": { "guideButton": false }` and use the tray item.

```json
{ "couch": { "enabled": true, "openAtStart": false, "guideButton": true, "seeThrough": 0.5, "sounds": true } }
```

`seeThrough` (0 to 0.7) is how much of the screen as it was just before the UI opened, for example a
game, shows through behind it, blurred. 0 is solid. The picture is taken when the UI opens; it is not
live. (Windows cannot give a web view a truly transparent window, so this is a picture.)

`sounds` turns the small tick, chime and back sounds on or off. They are made by the page itself (no
sound files) and are quiet.

## Using it

| Control | Does |
| --- | --- |
| D-pad or left stick | Move the focus |
| A | Select (play, launch, press, switch to) |
| B | Close the controller UI |
| LB / RB | Previous / next screen |
| X / Y | Extra actions on some items, shown in the footer (for example Y = favorite an app) |
| Start | Switch the Apps screen between tiles and a list (remembered) |

The top bar shows each connected controller with its battery: three bars, a percentage, red when it is nearly
empty and a pulsing bar while it charges. A controller on a cable says "Wired". It is read through Windows' gamepad
API, which also works for Bluetooth controllers (the older XInput calls report those as "disconnected"), and is
checked every 30 seconds. With several controllers the order is the order they connected in.

The keyboard works too, for trying it without a controller: arrow keys, Enter, Esc, Q / E for
LB / RB, X and Y.

## Screens

* **Games** (the controller icon): your RPCS3 and PCSX2 games, one shelf each, with their covers, "Continue playing" first. The focused game's
  background picture is shown behind. A starts the game fullscreen and hides the UI.
* **Apps**: favorites, recent and all Start menu apps, with their icons, as small tiles or as a list
  (Start switches).
* **Media**: a small remote in the bottom right. Big play/pause in the middle with previous, next
  and 10-second jumps around it; below, 5-second jumps, the PC's volume (down, mute, up), and the
  player (fullscreen, its own volume, Esc). The footer says what the focused button does.
* **Running**: an Alt+Tab for the controller. Every open window is a card with a picture of the window itself,
  its title and its program, and the focused window's picture is blurred behind the screen. A switches to it,
  X closes it, Y force closes it (press twice). Start switches to a plain list (remembered). A minimised window
  has nothing to draw, so it shows its first letter and a "Minimized" tag; a few windows (some games, protected
  windows) cannot be drawn this way either and show the letter too. Pictures are fresh each time the screen opens.
* **Performance**: CPU, GPU, memory, disk and network use, and the busiest programs; X changes the
  sort. See [performance.md](performance.md).
* **PC**: cards of list rows, like Windows settings. **Session** (lock, sleep, sign out), **Power**
  (restart, shut down) and **Display** (display scale, shows the current value). Each row says what it
  does. Sign out, restart and shut down turn red on the first press with "Press A again to confirm" and a
  shrinking bar for the 4 seconds left; a restart or shutdown then counts down 15 seconds in a banner
  at the top, with a Cancel button.

## Games from RPCS3

Games come from RPCS3's own list (`config/games.yml`), with titles from each game's `PARAM.SFO`, and
the cover (`ICON0.PNG`) and background (`PIC1.PNG`) from the game folder. Nothing in RPCS3 is changed.
A game is started with `rpcs3.exe --no-gui --fullscreen <game folder>`.

RPCS3 is found automatically (a running copy, then usual folders such as `Downloads\rpcs3*`). The
PC Control window on the PC (tray icon, Open PC Control) has an **RPCS3** section at the bottom. It shows
what was found and how many games it lists. **Choose rpcs3.exe…** sets the location, **Find it
automatically** goes back to detection, and **Look again** searches again. A change applies at once and is
saved in `config.json`. You can also edit the file by hand, and to turn off fullscreen:

```json
{ "games": { "rpcs3Path": "D:\\Emulators\\RPCS3", "rpcs3Fullscreen": true } }
```

If RPCS3 is not found the Games screen says so and the phone's `gameLauncher` capability is off.

## Games from PCSX2

PS2 games come from PCSX2 (the Qt version, `pcsx2-qt.exe`). The game folders are read from PCSX2's own
settings (`inis\PCSX2.ini`, under `[GameList]`), and every disc image in them is a game (`.iso`, `.chd`,
`.cso`, `.gz`, `.bin`, `.img`, `.mdf`, `.nrg`). PCSX2's data folder is `Documents\PCSX2`, or the program's
own folder when it is portable (a `portable.ini` is there).

* **Titles:** the serial (for example SLUS-21300) is read off the disc image (its `SYSTEM.CNF`) and looked up
  in PCSX2's own `resources\GameIndex.yaml`, so the names match PCSX2's own list. If a name is not found (or
  the image is a compressed `.chd`, whose inside is not read), the file name is used.
* **Covers:** PCSX2 keeps covers in `Documents\PCSX2\covers`, named after the serial or the title
  (`SLUS-21300.jpg`). The same files are used here, and so is a `cover.png` / `folder.jpg` next to the game.
  A game that has none gets its box art downloaded once, by serial, from the public
  [xlenore/ps2-covers](https://github.com/xlenore/ps2-covers) collection on GitHub, and kept in that same
  `covers` folder (so PCSX2 shows it too). Only the serial is sent, as part of the web address. Turn it off with
  `"pcsx2DownloadCovers": false`; then, or when there is no picture for the game, it shows its first letter.
  The box art is also blurred behind the screen while the game is focused, in place of a background picture.
* **Starting a game:** `pcsx2-qt.exe -nogui -fullscreen -- <disc image>`: no main window, only the game, and PCSX2
  closes when the game ends.
* **BIOS:** PCSX2 cannot run anything without a PS2 BIOS. If it cannot find one, starting a game would only
  open an error, so PC Control does not start it and says why instead (in the launch message and in the desktop
  window). The BIOS folder in `PCSX2.ini` is relative to PCSX2's data folder, so moving PCSX2 can leave it
  pointing nowhere; fix it in PCSX2 under Settings, BIOS.

PCSX2 is found the same way as RPCS3 (a running copy, then usual folders such as `Downloads\pcsx2*` and
`Downloads\apps\pcsx2*`), and the PC Control window has a PCSX2 section next to the RPCS3 one. In `config.json`:

```json
{ "games": { "pcsx2Path": "D:\\Emulators\\PCSX2", "pcsx2Fullscreen": true, "pcsx2DownloadCovers": true } }
```

## How it works, and what to know

* The page is the same web technology as the phone remote and uses the same API, so the theme, apps,
  media, power and process features are shared.
* The window pairs itself with a one-time key that only this app and its own window know, and only
  from this PC (`POST /api/pair/local`). It shows up as "PC controller UI" in the device list.
* The Guide button is read through Windows' XInput library, because browsers cannot see it. Only
  Xbox-style (XInput) controllers are watched.
* While the UI is open over a game, the game can still receive the controller's buttons too. (Pausing
  the game while the UI is open is planned but off for now.)
* A game in true exclusive fullscreen cannot be drawn over; the UI will take the screen from it.
* There is no on-screen keyboard yet, so there is no text search in the controller UI.
