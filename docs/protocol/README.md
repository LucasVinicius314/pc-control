# PC Control protocol — version 1

This folder is the contract between the PC app and every phone client (the web page today, a native
Android app later). The PC holds all the logic; clients stay thin. A change that breaks this
contract bumps `protocolVersion` (see `ProtocolInfo.Version` in `PcControl.Core`).

| File | What it covers |
| --- | --- |
| [openapi.yaml](openapi.yaml) | REST endpoints |
| [websocket.md](websocket.md) | The live `/ws` channel |
| [pairing.md](pairing.md) | PIN / QR pairing and tokens |
| [gestures.md](gestures.md) | Trackpad gesture rules every phone client follows |

## Compatibility rules

- `GET /api/info` is public and returns `protocolVersion`. A client must check it before pairing and
  refuse to talk to a PC with a higher major version than it knows.
- New optional fields and new message types may be added without a version bump. Clients must ignore
  fields and message types they do not know.
- Every client adapts its screens to `GET /api/capabilities` instead of assuming a feature exists.

## Implemented so far

- Milestone 1: pairing, session, capabilities and the WebSocket handshake with `hello`, `ping`/`pong`,
  `paused` and `error`.
- Milestone 2: input over the WebSocket (mouse move and scroll frames, buttons, keys, text, zoom,
  sensitivity), release-on-disconnect, and the gesture rules.

Every other endpoint in the plan is added with its milestone and appears here first.
