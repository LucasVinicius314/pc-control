# WebSocket `/ws`

Same origin as the page: `ws://<pc-address>:<port>/ws`. The handshake needs the device token (cookie
or bearer) and obeys the origin rules in [pairing.md](pairing.md).

## Framing

- **Text frames** carry one JSON object each: `{"type": "<name>", ...}`. Maximum 4096 bytes. A larger
  message closes the socket with status 1009.
- **Binary frames** carry high-rate input (mouse movement and scrolling). They are always exactly 5
  bytes; see [Binary input frames](#binary-input-frames). The server answers a malformed one with
  `error` / `invalid_message`.
- The server never closes the socket because of a malformed or unknown message; it replies with
  `error` and carries on.

## Server → client

| `type` | Fields | When |
| --- | --- | --- |
| `hello` | `protocolVersion`, `deviceId`, `deviceName`, `paused` | Once, right after the handshake |
| `pong` | — | Reply to `ping` |
| `paused` | `paused` (bool) | The tray kill switch changed |
| `error` | `code` | A message was refused, see below |

`error.code` values:

| Code | Meaning |
| --- | --- |
| `invalid_message` | Not JSON, no string `type`, a missing or wrongly typed field, or a malformed binary frame |
| `unknown_type` | A message type this server does not know |
| `unknown_key` | A key `code` that is not in the key list below |
| `unknown_button` | A mouse button other than `left`, `right`, `middle` |
| `invalid_value` | A number out of range (see the limits below) |
| `too_many_held_keys` | More than 16 keys held down at once |

## Client → server

| `type` | Fields | Effect |
| --- | --- | --- |
| `ping` | — | Server answers `pong` |
| `button` | `button`: `left` \| `right` \| `middle`; `action`: `down` \| `up` \| `click` | Mouse button. `click` is ignored while that button is held. |
| `key` | `code` (see [Keys](#keys)); `action`: `down` \| `up` \| `press` | Keyboard key. `press` is down then up. |
| `text` | `text` (at most 256 UTF-16 characters) | Types the text as Unicode characters, whatever the PC's keyboard layout. `\n` becomes Enter, `\t` becomes Tab, other control characters are dropped. |
| `zoom` | `steps`: integer from -10 to 10, not 0 | Ctrl + mouse wheel. Positive zooms in. |
| `settings` | `sensitivity`: number from 0.25 to 4 | Cursor speed multiplier for this connection (default 1) |
| `releaseAll` | — | Releases every key and button this connection holds |

Typed text is never logged on the PC.

### Binary input frames

All integers are little-endian `int16`.

| Byte 0 | Bytes 1–2 | Bytes 3–4 | Meaning |
| --- | --- | --- | --- |
| `0x01` | `dx` | `dy` | **Move.** Finger movement in phone (CSS) pixels, at most ±2000. The PC applies the pointer curve and `sensitivity`. |
| `0x02` | `vertical` | `horizontal` | **Scroll.** Wheel units, 120 per notch, at most ±1200. Positive vertical scrolls up, positive horizontal scrolls right. |

Clients should sum movement and send at most one frame per animation frame (about 60 per second).
Frames sent while the connection is not open are dropped, never queued.

### Keys

`code` follows the browser's `KeyboardEvent.code` names:

`KeyA`–`KeyZ`, `Digit0`–`Digit9`, `F1`–`F12`, `Escape`, `Tab`, `Enter`, `Backspace`, `Space`,
`Insert`, `Delete`, `Home`, `End`, `PageUp`, `PageDown`, `ArrowLeft`, `ArrowUp`, `ArrowRight`,
`ArrowDown`, `ContextMenu`, `ShiftLeft`, `ShiftRight`, `ControlLeft`, `ControlRight`, `AltLeft`,
`AltRight`, `MetaLeft`, `MetaRight` (the Windows key), `Minus`, `Equal`, `BracketLeft`,
`BracketRight`, `Semicolon`, `Quote`, `Backquote`, `Backslash`, `Comma`, `Period`, `Slash`.

Anything else is refused with `unknown_key`: a phone cannot send arbitrary virtual-key codes.
Media keys arrive in milestone 7.

## Kill switch

While the tray's **Pause remote control** is on, the PC ignores all input messages and frames
silently (no `error` per message), and has already released everything held. `ping` still works. The
server tells every phone with a `paused` message, and tells new phones in `hello`.

## Keep-alive and reconnecting

- The client sends `ping` every 10 s and treats no `pong` within 6 s as a dead connection. Phones
  drop sockets when they sleep, so the client must reconnect on its own (backoff 0.5 s up to 8 s) and
  retry as soon as the page becomes visible again.
- The server also sends protocol-level WebSocket pings every 15 s.
- After a reconnect the client sends `settings` again; connection state is not remembered.

## Close codes

| Code | Meaning | Client action |
| --- | --- | --- |
| 1000 | Normal close | Reconnect if the page is still open |
| 1009 | Message too big | Fix the client |
| **4401** | Token revoked or unknown | Stop retrying and show the pairing screen |

## Safety on disconnect

The moment a socket closes, for any reason (clean close, dropped Wi-Fi, phone sleep, revocation,
server shutdown), the server releases every key, modifier and mouse button that connection held, so
nothing stays stuck on the PC. Each connection tracks its own held input.

## Limits of input injection

Windows blocks a normal process from sending input to windows run as administrator (Task Manager,
UAC prompts, some emulators) and to the lock screen. The PC logs a warning at most every 30 seconds
when this happens; the phone gets no error.
