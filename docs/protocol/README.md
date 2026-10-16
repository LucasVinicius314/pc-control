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

- Milestone 5 (pulled forward): live previews over the WebSocket (mini preview around the cursor and
  whole-display preview), `GET /api/monitors` and `GET /api/screenshot`.

- Milestone 3: the app launcher (`/api/apps`, icons, launch, favorites) and the PowerToys Run button
  (`POST /api/powertoys-run`). Actions are refused with `409 paused` while the kill switch is on.
- Milestone 4: `GET /api/dpi` and `POST /api/dpi/toggle` (display scale 100% / 200%), and the
  `dpiToggle` capability, which is only on when the startup self-test passed.

- Milestone 7: `POST /api/url` (open a web link on the PC), `GET/POST /api/power` and
  `DELETE /api/power/pending` (lock, sleep, restart, shut down, sign out), `GET /api/processes` and
  `POST /api/processes/{id}/close` (close a program), plus the media keys (see websocket.md). The
  capabilities `sendUrl`, `power` and `processKiller` are on unless switched off in the config's
  `features`. See [../system.md](../system.md).

- `GET /api/theme`: the PC's accent colour and dark / light mode, so a client can match the PC.

Every other endpoint in the plan is added with its milestone and appears here first.
