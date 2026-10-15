# WebSocket `/ws`

Same origin as the page: `ws://<pc-address>:<port>/ws`. The handshake needs the device token (cookie
or bearer) and obeys the origin rules in [pairing.md](pairing.md).

## Framing

- **Text frames** carry one JSON object each: `{"type": "<name>", ...}`. Maximum 4096 bytes. A larger
  message closes the socket with status 1009.
- **Binary frames from the phone** carry high-rate input (mouse movement and scrolling). They are
  always exactly 5 bytes; see [Binary input frames](#binary-input-frames). The server answers a
  malformed one with `error` / `invalid_message`.
- **Binary frames from the PC** are pictures for the previews; see [Previews](#previews).
- The server never closes the socket because of a malformed or unknown message; it replies with
  `error` and carries on.

## Server → client

| `type` | Fields | When |
| --- | --- | --- |
| `hello` | `protocolVersion`, `deviceId`, `deviceName`, `paused` | Once, right after the handshake |
| `pong` | — | Reply to `ping` |
| `paused` | `paused` (bool) | The tray kill switch changed |
| `error` | `code` | A message was refused, see below |
| `viewStatus` | `stream`, `state` | A preview stream changed state, see [Previews](#previews) |

`error.code` values:

| Code | Meaning |
| --- | --- |
| `invalid_message` | Not JSON, no string `type`, a missing or wrongly typed field, or a malformed binary frame |
| `unknown_type` | A message type this server does not know |
| `unknown_key` | A key `code` that is not in the key list below |
| `unknown_button` | A mouse button other than `left`, `right`, `middle` |
| `invalid_value` | A number out of range (see the limits below) |
| `too_many_held_keys` | More than 16 keys held down at once |
| `feature_disabled` | The feature is switched off in the PC's config (for previews: `features.screenshot`) |

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
| `view` | see [Previews](#previews) | Starts, changes or stops a live picture stream |

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

## Previews

Two live picture streams let the phone see the PC screen. Both are off until the phone asks, and
both are covered by the `screenshot` feature toggle and capability (`magnifier` follows it).

| Stream | What it shows |
| --- | --- |
| `magnifier` | A small region of the screen around the mouse cursor, at full resolution. The phone enlarges it, so the zoom is the ratio between the size it shows and the size it asked for. |
| `display` | A whole monitor, scaled down to the requested width |

### Starting and stopping

```json
{"type":"view","stream":"magnifier","enabled":true,"monitor":-1,"width":93,"height":53,"fps":15}
{"type":"view","stream":"display","enabled":true,"monitor":-1,"width":720,"fps":5}
{"type":"view","stream":"display","enabled":false}
```

| Field | Meaning | Limits (out-of-range values are clamped, not refused) |
| --- | --- | --- |
| `stream` | `magnifier` or `display` | required |
| `enabled` | true to stream, false to stop | required |
| `monitor` | Monitor index from `GET /api/monitors`, or `-1` to follow the cursor (default) | -1 to 15 |
| `width`, `height` | Magnifier: the region in PC pixels (default 200 x 120). Display: only `width` is used, the height follows the monitor's shape (default 720) | magnifier 16-800 x 16-600; display 160-1920 wide, never over 900 tall |
| `fps` | Pictures per second (default 15 magnifier, 8 display). The cursor messages are not limited by this. | magnifier 1-20; display 1-10 |

Each phone has at most one stream of each kind; a new `view` message replaces the old one. A phone
should send `enabled: false` when its page is hidden, and send its `view` messages again after every
reconnect, since the PC forgets streams when the socket closes. Streams also stop by themselves when
the socket closes.

The PC skips a picture when nothing changed since the last one (same pixels, same cursor position).

### Picture frames (PC to phone, binary)

| Bytes | Meaning |
| --- | --- |
| 0 | `0x10` magnifier or `0x11` display |
| 1 | Index of the monitor the picture shows (as in `GET /api/monitors`) |
| 2-3 | Cursor X inside the picture, little-endian `int16` (-1 when the cursor is not in it) |
| 4-5 | Cursor Y inside the picture, little-endian `int16` (-1 when the cursor is not in it) |
| 6- | A JPEG (quality 65) |

The captured picture never contains the mouse cursor. For the **magnifier**, the phone draws a
marker at the position in the header. Near a screen edge the region is moved back inside the monitor
instead of showing black, so the cursor is no longer in the middle: always use the header position.

For the **display**, do not use the header position. Pictures arrive a few times a second and are
always slightly old, so the cursor is sent separately and much faster, see below. The display
picture is scaled down to fit the requested `width` and at most 900 pixels tall, so a portrait
monitor gives a narrow picture.

### Cursor messages (PC to phone, binary)

While a `display` stream is on, the PC also sends the cursor position on its own. The phone draws
its marker as an overlay and moves it for every message, independent of the pictures.

| Bytes | Meaning |
| --- | --- |
| 0 | `0x12` |
| 1 | Index of the monitor the cursor is on, or `0xFF` when it is on none |
| 2-3 | X in that monitor, little-endian `uint16`, 0 (left edge) to 65535 (right edge) |
| 4-5 | Y in that monitor, little-endian `uint16`, 0 (top edge) to 65535 (bottom edge) |

The PC checks the cursor up to 60 times a second and sends a message only when it moved. When the
phone itself moves the mouse (a `move` frame), the PC checks immediately instead of waiting for the
next poll, so the marker follows the finger with very little delay. The phone should show the marker
only when the monitor in the message equals the monitor of the picture it is showing, and should
apply at most one update per animation frame.

### Stream state

`viewStatus` (`stream`, `state`) is sent when a stream's state changes, and once when it starts:

| `state` | Meaning |
| --- | --- |
| `running` | Pictures are flowing |
| `paused` | The tray kill switch is on. No pictures are captured; they resume by themselves. |
| `locked` | The PC is locked or showing a UAC prompt, so the screen cannot be read |
| `failed` | The screen could not be captured (for example no monitor) |

Exclusive-fullscreen games may produce black pictures; borderless fullscreen works.

## Kill switch

While the tray's **Pause remote control** is on, the PC ignores all input messages and frames
silently (no `error` per message), has already released everything held, and stops capturing the
screen for the previews. `ping` still works. The
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
