# 073 — Bus dispatch hardening

Audit §6 / §13.15–17. Part of the "make the bus survivable" cluster (with the now-done #085). Two
failure modes at the network message-bus boundary, both small fixes with large robustness payoff.

## Goal

1. **Exception isolation in dispatch.** `MessageRegistrar.DispatchToHandlers` looped
   `del.DynamicInvoke(message)` with no try/catch, so one throwing handler propagated out of dispatch,
   through the read loop, into `FDGHost.HandleClientAsync`'s catch-all — which closes that client. One
   stray message could drop a player (and used to compound with the unknown-`TaskID` throw fixed under
   #085). Wrap each handler in try/catch + log; carry on with the remaining handlers.
2. **Visible unknown-type discard.** `MessageSerializer.DeserializeMessage` discarded unregistered wire
   types with a `Debug.WriteLine` (compiled out of Release), so version skew vanished without a trace.
   Keep tolerating types this endpoint doesn't handle, but log genuinely-unknown types visibly, once per
   type, via `ITextOutput`.

## Decisions

- **2026-06-14** — Log sink: inject an `ITextOutput` into `MessageRegistrar` / `MessageSerializer`,
  defaulting to a new `ConsoleTextOutput` (real `Console.WriteLine`, survives Release). The bus layer
  sits *below* where any game-level `ITextOutput` is plumbed (the whole network layer uses
  `Debug`/`Console.WriteLine` directly), so threading one from the app through both `LobbyViewModel`
  constructors + their 3 app construction sites was disproportionate to the fix. Console default keeps
  the change engine-local and is consistent with `FDGHost` already using `Console.WriteLine` for the
  disconnect path; tests inject a capturing sink. (Surfaced as a design fork; user chose "inject,
  default to console".) Threading a real game-log sink down can ride a later networking pass if wanted.
- **2026-06-14** — Unwrap `TargetInvocationException` in the handler catch so the log shows the real
  exception type/message, not `DynamicInvoke`'s reflection wrapper.
- **2026-06-14** — Snapshot the handler list (`handlers.ToArray()`) before iterating so a handler that
  (de)registers during dispatch can't mutate the list mid-loop.

## Notes

- **2026-06-14** — Implemented. Engine commit `9330ed1`.
  - `Helpers/ITextOutput.cs`: new `ConsoleTextOutput : ITextOutput`.
  - `MessageRegistrar`: `ITextOutput?` ctor param (default `ConsoleTextOutput`); per-handler try/catch,
    `TargetInvocationException` unwrap, snapshot iteration.
  - `MessageSerializer`: `ITextOutput?` ctor param; `_warnedUnknownTypes` HashSet → warn once per
    unknown type, then return null. Dropped the now-unused `System.Diagnostics` using.
  - `Tests/BusDispatchHardeningTests.cs`: 3 new tests (throwing handler doesn't abort others / isn't
    rethrown / is logged; unregistered type is a no-op; unknown wire type warns once and returns null).
  - Verified: engine suite 514/0, full build clean, headless smoke exits 0.
- **DEFERRED / related (still open):** the `_lastMessageConnectionID` race (#074) and routing decision
  requests to a single connection (#088) are separate audit §6 items, untouched here.
