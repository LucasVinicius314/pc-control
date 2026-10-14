# WebSocket `/ws`

Same origin as the page: `ws://<pc-address>:<port>/ws`. The handshake needs the device token (cookie
or bearer) and obeys the origin rules in [pairing.md](pairing.md).

## Framing

- **Text frames** carry one JSON object each: `{"type": "<name>", ...}`. Maximum 4096 bytes. A larger
  message closes the socket with status 1009.
- **Binary frames** are reserved for input (milestone 2) and magnifier frames (milestone 5). Until
  then the server answers a binary frame with `error` / `unsupported_binary`.
- The server never closes the socket because of a malformed or unknown message; it replies with
  `error` and carries on.

## Server → client

| `type` | Fields | When |
| --- | --- | --- |
| `hello` | `protocolVersion`, `deviceId`, `deviceName`, `paused` | Once, right after the handshake |
| `pong` | — | Reply to `ping` |
| `paused` | `paused` (bool) | The tray kill switch changed |
| `error` | `code` | See below |

`error.code` values: `invalid_message` (not JSON, or no string `type`), `unknown_type`,
`unsupported_binary`, `paused` (input was dropped because the kill switch is on).

## Client → server

| `type` | Fields | Effect |
| --- | --- | --- |
| `ping` | — | Server answers `pong` |

## Keep-alive and reconnecting

- The client sends `ping` every 10 s and treats no `pong` within 6 s as a dead connection. Phones
  drop sockets when they sleep, so the client must reconnect on its own (backoff 0.5 s up to 8 s) and
  retry as soon as the page becomes visible again.
- The server also sends protocol-level WebSocket pings every 15 s.

## Close codes

| Code | Meaning | Client action |
| --- | --- | --- |
| 1000 | Normal close | Reconnect if the page is still open |
| 1009 | Message too big | Fix the client |
| **4401** | Token revoked or unknown | Stop retrying and show the pairing screen |

## Safety on disconnect

From milestone 2 the server releases every held key, modifier and mouse button the moment a socket
closes, for any reason, so nothing stays stuck on the PC.
