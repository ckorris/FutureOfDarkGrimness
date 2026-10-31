# 085 — Tolerate unknown/duplicate `TaskID` replies

Audit §5. Third of the four HIGH-PRIORITY networking items; follows #084/#086 in the same file.

## Goal

`RequestMessageSender.OnReceivedReplyMessage` / `OnReceivedErrorMessage` threw an
`ArgumentException` when a reply/error arrived for a `TaskID` that wasn't pending. These handlers
run inside message-bus dispatch with no surrounding try/catch, so a stray or replayed reply (e.g.
a duplicate from a flaky connection) would propagate out and tear down the connection. Make it
idempotent: log-and-ignore the unknown/duplicate, throw only in DEBUG so the condition still
surfaces during development.

## Decisions

- **2026-06-13** — kept the `#if DEBUG throw / #else return` split exactly as the audit specified
  rather than always-ignoring: in a Debug build an unknown TaskID is a genuine bug worth surfacing
  loudly; in Release (production) tolerance is what protects the connection. The engine test suite
  builds Debug, so it still exercises the throw path.
- **2026-06-13** — logging via `ITextOutput` (threaded into the ctor from `FDGServer`, which already
  builds a `PlayerLogSender` `textOutput` two lines above the `RequestMessageSender` construction)
  rather than `Debug.WriteLine`, so the ignore is visible in normal logs. Tests pass
  `new EmptyTextOutput()`.
- The atomic `TryRemove` claim that makes "already-resolved" detection correct under concurrency
  landed with #084 — #085 only changes what happens when the claim misses.

## Notes

- **2026-06-13** — Implemented. Engine commit `846d955`.
  - `RequestMessageSender`: new `ITextOutput _textOutput` ctor param; both handlers log-and-ignore
    on a missed `TryRemove`, `#if DEBUG` throw / `#else` return.
  - `FDGServer`: passes the existing `textOutput` into the ctor.
  - `Tests/RequestSystemTests.cs`: two ctor sites pass `new EmptyTextOutput()`.
  - Verified: engine suite 424/0 (Debug), Release build clean (exercises the `#else` path), full
    build clean, headless smoke exits 0.
  - No new automated test for the ignore path — a deterministic duplicate-reply test belongs with
    the loopback networking fixture tracked as **#065**.
