# Pairing and tokens

## Flow

1. The user picks **Show pairing PIN…** in the PC's tray menu. The PC shows a 6-digit PIN and a QR
   code, and lists its home-network address.
2. The phone sends the PIN to `POST /api/pair` together with a device name.
3. The PC checks the PIN, creates a random 256-bit device token and stores **only its SHA-256 hash**.
4. The token reaches the client in one of two ways:
   - **Browser:** an `HttpOnly; SameSite=Strict` cookie named `pc_device` (the default).
   - **Native app:** send `"issueBearerToken": true` and read `token` from the response. Send it as
     `Authorization: Bearer <token>` afterwards. Cookies are never needed.
5. Every REST call and the WebSocket handshake require the token.

## PIN rules

- Six digits, random, single use, expires after `pairing.pinLifetimeMinutes` (default 5).
- A new PIN replaces the old one.
- A PIN is discarded after `pairing.maxFailuresPerPin` wrong guesses (default 5).
- A client IP is locked out for `pairing.lockoutMinutes` after `pairing.maxFailedAttempts` failures
  inside `pairing.attemptWindowMinutes` (`429` with `Retry-After`).

## QR code content

```
http://<pc-address>:<port>/#pin=<six digits>
```

- A phone camera opens it in the browser, and the page pairs automatically. The PIN sits in the URL
  fragment, which browsers never send to the server, so it stays out of request logs.
- A native app parses the same URL: scheme + host + port give the address, `pin` gives the code.

## Revocation

- The user removes a device in the tray (**Paired devices**), or the phone calls `DELETE /api/session`.
- A revoked device's token stops working at once. Its open WebSocket is closed with code **4401**,
  and clients must not retry after 4401.

## Network rules the server enforces

- Only private addresses are served: loopback, `10/8`, `172.16/12`, `192.168/16`, link-local,
  IPv6 unique-local and link-local. Tailscale's `100.64/10` only if `allowTailscaleRange` is on.
- The `Host` header must be an IP address, `localhost`, a single-label name or `*.local`. This
  blocks DNS rebinding.
- State-changing requests that carry an `Origin` header must be same-origin.
- A WebSocket handshake with an `Origin` must be same-origin. One without an `Origin` is accepted
  only with a bearer token, never with a cookie.

## Future (design rules 4 and 7, not built yet)

- Advertise the PC with mDNS (`_pc-control._tcp`) so an app can find it; manual address stays as the
  fallback.
- Optionally let the PC create its own certificate and carry its fingerprint in the QR code.
