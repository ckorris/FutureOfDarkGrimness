# 266 — FDGHost pre-authentication connection limits (public-exposure hardening)

**Status**: done
**Related**: #264 (public listing = strangers and scanners will connect), #189 (broadcast gating +
configurable port), #186 (what those connections' bytes feed into), QF2/QF6 (greeting-timeout
eviction + post-launch join gate — already done), #065 (transport tests — first dent made here).
Engine commit `842c43b`.

## Goal

A host that lists itself publicly (#264) exposes port 6389 to the internet; drive-by scanners and
hostile clients will connect. `FDGHost` already evicts connections that never complete the greeting
(QF2) and caps frames at 16MB (`CommandProtocol.MAX_PAYLOAD_BYTES`), but before a connection has
authenticated it can still:

- be one of an **unlimited number of concurrent accepted connections** (the accept loop has no
  cap, total or per-IP) — each holding a socket, a read loop, and pool-rented buffers;
- send **full-size (16MB) frames before greeting** — a valid greeting is under 4KB, so the
  pre-auth allowance is ~4000x larger than anything legitimate, and N connections x 16MB rented
  arrays is an easy memory-pressure attack from one machine.

Done looks like: (a) a total concurrent-connection cap (e.g. 32) and a small per-IP cap (e.g. 4),
refusing accepts beyond them; (b) a much smaller frame cap (e.g. 64KB) for connections that have
not yet passed the #075 greeting, switching to the full 16MB only after acceptance. Both are
engine (`FDGHost` / `CommandProtocol`) changes — submodule authorization needed before building.

This is availability hardening, not confidentiality: nothing here leaks data. It just keeps a
hostile stranger from wedging a host's memory or socket table while the lobby sits listed.

## Notes

- 2026-07-23 (later): Implemented (engine `842c43b`), same day as filing — engine changes
  authorized by owner. Suite 1996/1996 green (5 new); full build + headless smoke clean.
- 2026-07-23: Filed from #264's security review after reading `FDGHost.StartAsync` (no accept
  cap) and `CommandProtocol.ReadCommandAsync` (single 16MB cap regardless of auth state).

## Decisions

- **The frame cap is a `Func<int>` provider, evaluated after the length prefix arrives, not an int
  captured at call time.** The read for frame N+1 is already parked while frame N's greeting is
  being processed, so a call-time cap would judge the first post-accept payload (an army list,
  legitimately large) by the stale pre-auth limit. TCP ordering makes the provider evaluation safe:
  the length bytes of a legitimate large frame can only arrive after the client saw the join
  acceptance, which the host sends after `MarkClientAuthenticated`. This exact race was hit and
  fixed during implementation; `Authenticated_LargeFrame_IsReceived` pins it.
- Refusal = immediate `client.Close()` before any per-connection allocation: no roster events, no
  read loop, no rented buffers for a refused scanner.
- Per-IP count is derived from the live roster (no separate bookkeeping to drift); null remote
  addresses only bound by the total cap.
- `FDGHost` gained ctor params (caps + listen port, defaulted so `new FDGHost()` is unchanged);
  the port param exists so transport tests run in parallel without colliding — the user-facing
  configurable port remains #189.
- Auth marking is a new `INetworkHost.MarkClientAuthenticated`, called at both greeting-accept
  sites in `LobbyViewModel_Host` (new-game and resume paths), after every reject gate.

## Outcome

Shipped in engine `842c43b`: accepts beyond 32 total / 4 per-IP concurrent connections are closed
immediately; un-greeted connections are capped at 64KB frames (`MAX_PREAUTH_PAYLOAD_BYTES`) and
lifted to the full 16MB at join acceptance on both the new-game and resume paths. Five new
real-TCP loopback tests (`FdgHostConnectionLimitTests`) — the first transport tests against
`FDGHost` (#065 remains open for the wider gap). Live WAN behavior rides on #264's two-machine
test. Caps are compile-time defaults; making them user-configurable was deliberately skipped
(no realistic lobby approaches them).
