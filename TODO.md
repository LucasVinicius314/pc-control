# To do

Things left over, in no particular order. Nothing here is started.

## Polish (the old "milestone 9")

- [ ] **Installer.** One file that installs PC Control properly. "Start with Windows" should then point at the installed copy instead of a debug build.
- [ ] **Firewall rule.** So the phone reaches the PC without a Windows prompt.
- [ ] **HTTPS decision.** The phone page runs over plain http on the home Wi-Fi. Pairing, device tokens and the origin checks protect it, but the traffic is not encrypted. HTTPS means a certificate the phone has to trust; decide whether it is worth it.

## Controller UI

- [ ] **Pause the game while the UI is open over it.** The game still hears the controller's buttons while the overlay is up. (Left out on purpose until the overlay was tested.)
- [ ] **On-screen keyboard.** There is no text search or typing in the controller UI yet.
- [ ] **More game sources.** Steam, Epic, GOG, the Xbox app. RPCS3 is the only one now.
- [ ] **A real transparent overlay.** The blurred screenshot is the best a normal WebView2 window allows. Truly see-through needs WebView2's visual hosting (a DirectComposition window), which is a larger piece of work.

## Task manager

- [ ] **Per-program network speed.** Windows only gives it with administrator rights (event tracing). Disk currently shows each program's total I/O, files and network together.
- [ ] **One line per process** as a mode, instead of grouping by program name.

## Maybe later

- [ ] An Android app that uses the same protocol (`docs/protocol` is written for it).
- [ ] Check the overlay at display scales other than 100%.
