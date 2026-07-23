# 266 — FDGHost pre-authentication connection limits (public-exposure hardening)

**Status**: todo
**Related**: #264 (public listing = strangers and scanners will connect), #189 (broadcast gating +
configurable port), #186 (what those connections' bytes feed into), QF2/QF6 (greeting-timeout
eviction + post-launch join gate — already done)

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

- 2026-07-23: Filed from #264's security review after reading `FDGHost.StartAsync` (no accept
  cap) and `CommandProtocol.ReadCommandAsync` (single 16MB cap regardless of auth state).

## Decisions

(none yet)

## Outcome

(open)
