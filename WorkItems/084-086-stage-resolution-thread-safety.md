# 084 / 086 — Stage-resolution thread safety + per-connection write lock

Audit §5 (#084) and §6 (#086). Two explicitly-coupled networking fixes done together: #084's
`RunContinuationsAsynchronously` removes the accidental serialization that was masking the #086
write races, so the write lock must land in the same change.

## Goal

- **#084** — make `RequestMessageSender` thread-safe: `_pendingTaskAndResolvers` is mutated from
  the engine thread (add) and the bus/network read thread (remove) with no lock; its
  `TaskCompletionSource`s lacked `RunContinuationsAsynchronously`, so replies resumed engine stage
  code synchronously on the network read loop.
- **#086** — serialize per-connection writes: `CommandProtocol.WriteCommandAsync` does three
  separate stream writes (magic / length / payload) and there was no per-connection send lock, so
  concurrent sends (data sync + beats + requests + log relay) could interleave bytes inside a frame
  and corrupt the stream.

## Decisions

- **2026-06-13** — #086 write serialization: chose **`SemaphoreSlim(1,1)` per connection** over an
  outbound queue + single writer task. Rationale: the bug is byte interleaving inside a frame, which
  a per-connection mutex fixes precisely with a ~small, reviewable change; it keeps the existing
  `WriteCommandAsync`-throws-to-caller error path intact (callers' try/catch unchanged) and avoids
  the queue option's pooled-buffer-ownership handoff and indirect error propagation. The queue is a
  later upgrade behind the same `SendCommandTo*` surface if throughput ever justifies it.
- **2026-06-13** — #084 duplicate-reply handling: switched the reply/error handlers from
  `TryGetValue` + later `Remove` to an atomic `TryRemove` claim, so a duplicate/concurrent reply
  can't double-invoke a resolver. Kept the throw-on-unknown-TaskID behavior unchanged — converting
  that to log-and-ignore is **#085**, deliberately left in scope for that item.

## Notes

- **2026-06-13** — Implemented both. Engine commit `bbe9e19`.
  - `RequestMessageSender`: `ConcurrentDictionary<TaskID, SuccessAndFailActions>`; `TryAdd`;
    `TryRemove` atomic claim in both handlers; TCS created with
    `TaskCreationOptions.RunContinuationsAsynchronously`.
  - `FDGHost`: `_connectedClients` now maps to a private `ClientConnection { TcpClient, SemaphoreSlim }`;
    new `WriteLockedAsync` helper wraps every `WriteCommandAsync`; `SendCommandToSingleClientAsync`
    grabs the connection under the existing `_connectedClients` lock before writing.
  - `FDGClient`: single `_writeLock` SemaphoreSlim around its `WriteCommandAsync`.
  - Verified: engine suite 416/0, full `dotnet build` clean, headless smoke exits 0 (tie).
  - Networking concurrency itself is not exercised by the local/headless path (LocalMessageBus), so
    these are not covered by an automated test yet — a loopback host+client concurrent-send test is
    tracked separately as **#065**.
