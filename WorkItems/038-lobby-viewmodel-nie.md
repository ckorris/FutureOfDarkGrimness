# 038 — Resolve LobbyViewModel_Host NotImplementedException paths

**Status**: done
**Related**: #036, #037 (closed together on branch `036-037-readiness-concurrent`); #007, #014 (prior dead-`/* */`-block removals)

## Goal
The index cited two live `NotImplementedException` paths in `LobbyViewModel_Host` (originally at `:288` and `:400`). Resolve them.

## Notes
- 2026-06-28: **The two live NIE paths no longer exist.** As the lobby evolved (network-slot assignment, player-slot work), the code at the old `:288`/`:400` grew into real implementations. The only `NotImplementedException` left in the file sat inside a `/* … */` block (lines 636–672): a commented-out `GetPlayerSlots` using a 2-arg `PlayerSlot(int, int)` constructor that no longer exists (current ctor takes 5 args) — it couldn't compile, so it was pure vestige. Deleted the whole dead block, matching the #007/#014 precedent. No live `NotImplementedException` remains in the engine outside the `Condition`/`Effect` "not yet evaluable/applyable" sentinels and a test fixture. Engine suite 843/0, build clean, headless exit 0.

## Decisions
- Closed as **already-resolved + dead-code cleanup** rather than an implementation task. The live problem the item described was gone; the remaining NIE was unreachable commented code, so the honest close is to delete it and tick the box.

## Outcome
Deleted the dead commented-out `GetPlayerSlots` block (the last `NotImplementedException` in the file). The live NIE paths the item targeted were already superseded by real lobby code. Closed.
