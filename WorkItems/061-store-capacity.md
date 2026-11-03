# 061 — Defuse the store-capacity time bomb

**Status**: done
**Related**: audit §2, #062 (store cleanup), #063 (data-store tests), engine `f467933`

## Goal
`ComponentStore<T>` allocates fixed-size arrays at construction and never grows, so `Create()`
throws `ExceededDataTypeCapacityException` once the registered capacity is reached. The default map
registers `ModelData` at 64 and `Position` at 128 — two normal ~40-model armies crash at army
creation with no warning. Done = a normal-sized multi-army game no longer crashes on capacity, and
the fix doesn't break save/load or the host↔client wire (both of which rely on `DataReference`
index/generation identity).

## Notes
- 2026-06-21: Picked Option B (growable stores) over Option A (raise defaults + assert). Folding in
  the #062 dead-code deletions since they sit in the same two files. Branch `061-store-capacity` in
  both repos.

## Decisions
- **Growable, not bigger ceiling.** `DataReference` is `{TypeID, Index, Generation}` — pure value
  identity, no raw array pointers — so reallocating the backing arrays leaves every existing
  reference valid. Capacity in `GetDefault()` becomes an initial hint, not a hard cap.
- **Two grow sites, not in `IsValid`.** `Create()` grows when full; `CreateFromReference()` grows to
  fit a foreign index (the save-replay / network path feeds in references whose index may exceed the
  local capacity if the source store had grown). `IsValid()` stays strict so genuine stale references
  still fail loudly.
- **Network compat.** Capacities are never sent on the wire — host and client both call
  `GetDefault()`. With growth, a client receiving a host reference past its capacity grows to fit, so
  the two stay consistent without any new handshake.
- **Save compat.** The save format already records per-type capacities (`GetTypeMapWithCapacities`),
  so a grown store just records the larger number; older saves load into the recorded (smaller) size
  and grow as needed.
- Out of scope (flagged, not silently cut): store thread-safety (audit §2, belongs to #062); the
  positional-TypeID redesign (#062). A modest sanity bound guards the grow path against a corrupt
  huge index OOMing.

## Outcome
Shipped Option B. `ComponentStore<T>` grows by doubling (`EnsureCapacity`): `Create` grows when no
free slot exists, `CreateFromReference` grows to fit a high foreign index (save/network replay),
`IsValid` left strict so stale references still fail. A `MAX_CAPACITY` (1<<24) bound keeps a corrupt
foreign index from attempting a runaway allocation. Default capacities in `GetDefault()` are now
just hints. Folded in the #062 dead-code deletions that lived in the same files. Added
`Tests/GameDataStoreTests.cs` (growth / pre-existing-reference survival / generation reuse /
foreign-reference grow / negative-index reject) — covers the store half of #063. Engine suite
620/0, full build clean, headless smoke exits 0. Engine commit `f467933`; superproject bump in the
same PR. Out of scope and still open: store thread-safety (#062) and the positional-TypeID redesign
(#062).
