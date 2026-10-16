# Links, power and closing programs

Three things the phone can do on the PC besides pointing and typing. They sit behind the **System**
button (the power symbol) in the phone's toolbar and can each be switched off in `config.json`:

```json
{ "features": { "urlOpen": true, "power": true, "processKiller": true } }
```

A switched-off feature is hidden on the phone and refused by the PC (`403 feature_disabled`). While
**Pause remote control** is on in the tray, all of them are refused (`409 paused`), except cancelling a
restart or shutdown.

## Open a link

Type or paste an address on the phone and it opens in the PC's default browser. Only `http` and
`https` addresses are accepted; `example.com/watch` is read as `https://example.com/watch`. Files,
scripts, `ms-settings:` links, programs, and addresses with spaces or a user name are refused, because
the address is handed to Windows. The log records the site (`example.com`), not the whole address.

## Power

| Button | What happens |
| --- | --- |
| Lock | Locks the PC at once. |
| Sleep | Puts the PC to sleep at once. |
| Sign out | Needs a second tap, then signs out at once. |
| Restart | Needs a second tap, then restarts after 15 seconds. |
| Shut down | Needs a second tap, then shuts down after 15 seconds. |

The phone asks for the second tap ("Tap again to confirm"), and the PC independently refuses these
three unless the request says `confirm: true`. While a restart or shutdown counts down, the phone
shows the time left and a **Cancel** button (the PC runs `shutdown /a`). From a command prompt on the
PC, `shutdown /a` also cancels it. Windows shows its own countdown message on the PC.

Sleep uses the normal sleep state. If your PC hibernates instead, check Windows' power settings.

## Close a program

The list shows programs that have a window in your session, by window title. **Close** asks the
program to close (it may still ask to save); **Force** ends it immediately and unsaved work is lost.

Safety rules: Windows system processes (Explorer, the shell, services) and PC Control itself are never
listed; the phone never sees process numbers, only opaque ids from the latest list; and a program
whose number was reused by something else since the list was read is left alone.
