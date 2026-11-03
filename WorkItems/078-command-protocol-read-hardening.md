# 078 — `CommandProtocol` read hardening

**Status**: done
**Related**: audit §13.25; engine `975d857` / merge `75f9f1d`, bump (superproject)

## Goal
`ReadCommandAsync` trusted the inbound 4-byte payload-length prefix with no upper bound, so one corrupt/hostile frame could rent a near-`int.MaxValue` buffer from the shared `ArrayPool`. Also missing a `ConfigureAwait(false)` on the payload read. Done when the length is clamped to a sane maximum (reject before renting) and the await is configured.

## Notes
- 2026-06-21: Added `MAX_PAYLOAD_BYTES = 16 * 1024 * 1024` (16 MB). `ReadCommandAsync` now throws `IOException` for `payloadLength < 0 || > MAX_PAYLOAD_BYTES` before the `ArrayPool.Rent`. Added the missing `.ConfigureAwait(false)` on the payload `ReadExactlyAsync`. Widened both `WriteCommandAsync`/`ReadCommandAsync` from `NetworkStream` to `Stream` (only `Stream` members were used) so the clamp is unit-testable with `MemoryStream` — `NetworkStream` callers (`FDGHost`/`FDGClient`) are unaffected. New `CommandProtocolTests`: round-trip, oversized length, negative length, bad magic. Suite 611/0.

## Decisions
- **16 MB cap**: comfortably above the largest real frame (full-state sync) while turning a ~2 GB rent into an immediate rejection. Hardcoded const rather than a setting — no caller needs to tune it, and a configurable limit is speculative generality.
- **Widen to `Stream`**: chose testability over leaving the `NetworkStream`-typed signature. Zero behavioral change for production callers; enabled the four protocol tests that the networking layer otherwise can't exercise without real sockets (see #065 for the broader networking-fixture gap).
- **Did not** address the pre-existing pooled-buffer leak (rented array isn't returned to the pool on a mid-read throw) — out of scope for this item; the cap makes the worst-case rent bounded regardless.

## Outcome
Inbound frames are bounded at 16 MB and rejected cleanly with `IOException`; the payload read no longer captures the sync context. Protocol now has unit coverage. The rented-buffer-on-throw cleanup remains a latent nit, not tracked separately.
