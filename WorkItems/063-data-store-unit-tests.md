# 063 — Data-store unit tests

**Status**: done
**Related**: #061 (the original store-growth fix), #270 (CreateFromReference vs CreateFromReplay split)

## Goal
Fill the gaps in `ComponentStore<T>` test coverage the 2026-06-10 audit flagged: capacity exhaustion,
generation reuse after `Destroy`, every `IsValid` reason code, and `CreateFromReference` rejection paths.

## Notes
- 2026-09-03: On inspection, `Tests/GameDataStoreTests.cs` already covered capacity exhaustion/growth
  and generation reuse after `Destroy` thoroughly (from #061/#270). The real gaps were: (1) `IsValid`
  reason codes checked only indirectly, through exception message text on a *create* path, rather than
  via a direct `IsValid()` call — `IsNotAssigned` (both "never touched" and "just destroyed, not yet
  reused" shapes, which are genuinely different code paths), `IncorrectType`, `IndexExceedsCapacity`
  (positive-and-negative), and `FutureGeneration` had no direct-call coverage at all; and (2)
  `CreateFromReference`'s sibling rejection branches — `IncorrectType`, `IndexAlreadyAssigned`, and the
  "at-or-behind" half of the generation gate (only "too far ahead" was pinned) — had no coverage.
  Added 9 tests to the existing file filling exactly those gaps; nothing else needed touching.

## Decisions
- Kept scope to `ComponentStore<T>`, matching the existing file's focus. `GameDataStore`'s own
  `TypeNotRegistered` reason lives at a different layer (unregistered-type lookup, not reference
  validity) and is out of scope for this item — flagging here rather than silently including or
  silently dropping it, in case a future item wants it.

## Outcome
Shipped 2026-09-03. `Tests/GameDataStoreTests.cs` grew from 10 to 19 tests, all green (full suite
3166 passed). Pure test addition — no production code touched.
