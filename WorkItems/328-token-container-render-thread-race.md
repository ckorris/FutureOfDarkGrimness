# 328 — Token reads crashed the renderer: `GetAllTokens` handed out the live list

**Status**: in-progress
**Related**: #151 (token display data, the reader), #327 (the session that surfaced it)

## Goal

The render thread must be able to read a unit's tokens every frame while the engine mutates them, without
throwing. `ITokenContainer`'s enumerable reads return snapshots taken under a lock, so no caller can
observe a collection mid-mutation. Done when a reader/writer stress test runs clean and the reported
crash cannot recur from this seam.

## Notes

- 2026-08-02: Reported from a live game (owner). After activating a melee unit and moving it out of
  melee range, the other player's client died mid-draw:

  ```
  System.InvalidOperationException: Collection was modified; enumeration operation may not execute.
     at FdgRaylib.Rendering.TokenChipRenderer.ResolveVisible(...) TokenChipRenderer.cs:60
     at FdgRaylib.Rendering.TableTooltipOverlay.DrawUnitOverlays()  TableTooltipOverlay.cs:347
     at FdgRaylib.Rendering.RaylibRenderer.Run()                    RaylibRenderer.cs:581
  ```

  Root cause: `TokenContainer.GetAllTokens()` returned `_tokens` itself (or, with a type filter, a lazy
  `Where` view over it), so `foreach` in the chip renderer walked the live list while a rule on the engine
  thread added or removed a token. Fixed by locking every read and write and returning snapshots.
  Verified: the two new tests fail against the pre-fix container and pass after.

## Decisions

- **Fixed in the engine, at the container, not in the renderer.** The obvious app-side patch — `.ToList()`
  at the call site — does not work: the copy itself enumerates the live list, so it can throw in exactly
  the same place. Only the owner of the collection can hand out a consistent view, so the snapshot has to
  come from inside the lock that also guards mutation. Several call sites (`DetermineHitRollStage`,
  `GrantedRollModifiers`, `TokenClearService`) already carried defensive `.ToList()` calls, which is
  evidence the leaky contract had been noticed before and papered over rather than fixed.

- **Cost accepted: an allocation per read.** Reads are frequent (rule evaluation walks `RuleGrant` tokens
  repeatedly), but the lists hold a handful of entries and the empty case returns `Array.Empty`. A
  turn-based game at 30 FPS does not need the sharing, and correctness is not negotiable against a crash
  that ends the session.

- **Events fire outside the lock.** Token handlers can call back into the container, so invoking under it
  invites a deadlock. `AddToken` and `RemoveMatching` now decide under the lock and announce after.

- **Not caused by #325, but exposed by it.** The race predates that work; holding dice briefly let the
  engine run further ahead of the renderer, which widened the window. The pacing revert narrows the window
  back to where it was — it does not close it, which is why this is fixed properly rather than left.

- **The wider class is NOT fixed here.** Any render-thread walk of an engine-owned collection has the same
  hazard (unit lists, model lists, rule collections). This item fixes the one seam that crashed and pins
  the contract on `ITokenContainer`; a systematic audit of render-thread reads is worth its own item.

## Outcome

_(pending)_
