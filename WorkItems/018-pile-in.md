# 018 — Pile In move

**Status**: in-progress (branch `018-pile-in` in repo and submodule)
**Related**: WorkItemsList #018, #017 (in-range checks consume the result), #019 (consolidation is the sibling reactive-move)

## Goal

Replace the no-op body of `PileInStage` with the GF v3.5.1 pile-in move:

> "Once all charging models have moved, all models from the target unit that are not in base contact with a charging model must move by up to 3" to get into base contact with a charging model, or as close as possible, maintaining unit coherency." (GF Beginner's Guide v3.5.1, p.9)

Done when: a charge into a unit whose models aren't all already in base contact causes the non-engaged defender models to slide up to 3" toward the nearest charging model (stopping at BTB), without breaking the defender's unit coherency (1" / 9"). Existing melee flow (`DetermineInRangeAttackersStage` → strike sequence) then resolves with the new positions. NUnit coverage in the submodule's Tests/.

## Approach

Auto-resolved entirely inside `PileInStage` — no new `IStageTaskRequest`, no CLI/GUI/AI resolvers. (Decision 2026-05-17 with user: pile-in is deterministic enough that a defender prompt isn't worth the UI cost; the rule's player discretion is collapsed to "nearest charging model, straight line, stop at BTB".)

`PileInStage.Enter` does:

1. Snapshot the living-model lists: charging-unit models (= `context.AttackingUnit`) and defending-unit models (= `context.DefendingUnit`).
2. Compute "needs to pile in": a defender model needs pile-in iff its 2D base-to-base distance to *every* charging model is > epsilon (0.01"). Models already in BTB stay put.
3. For each needs-pile-in defender model, compute a tentative new position:
   - Nearest charging model = argmin of 2D base-to-base distance.
   - Step along the line from defender center toward that charging model's center; stop when b2b = 0 (centers separated by sum of radii) or after 3", whichever comes first.
   - Don't overlap any other model. If the tentative position has b2b < 0 with any non-target model (other defender, other charger), shorten the step so b2b = 0 with the closest such obstruction.
3a. **Impassable terrain**: if the straight segment from the defender's current position to its tentative new position crosses any `Impassible`-flagged terrain piece, skip pile-in for that model (the rule's "as close as possible" caps at zero in this case).
4. Build the set of would-be final positions (moved defenders use new pos, others use current pos). Strict coherency check across the full defender unit:
   - Every model has at least one unit-mate within 1.0" (b2b 3D, using `DistanceUtilities.GetBaseToBaseDistanceInches_3D` — same call the GUI movement resolver uses).
   - Max pairwise distance ≤ 9.0".
5. If coherency holds, accept. If not, greedily revert moves: pick the moved defender whose new position contributes most to the violation (farthest from its nearest unit-mate, or farthest in the worst pair) and revert it to its original position. Re-check. Repeat until coherency holds or no moved defenders remain.
6. Apply final positions via `ModelData.SetPosition` on each moved model. Log a single summary line ("N defender models piled in toward charging unit").
7. `OnPiledIn.Activate(context)`.

If `defender` and `attacker` are already fully engaged (no models needed pile-in), short-circuit to step 7. Existing no-op behavior is correct for that case and we shouldn't log a misleading "skipping" message.

### Why this design

- **No new request type / resolver** keeps the PR small and avoids networking work — `NetworkedRequestMessageReceiver` doesn't need to learn a new type because no request crosses the wire.
- **Greedy coherency revert** is simple and matches the rule's "as close as possible, maintaining unit coherency" subordination — coherency wins over BTB.
- **2D distances** because the engine's Position is currently planar (no model y-axis used outside vertical-melee TODOs); revisit when item 022 (vertical melee) lands.

### Tests (submodule's Tests/)

Add `PileInTests.cs`:

1. Single defender model already in BTB → no move.
2. Single defender model 1.5" from sole charging model → moves to BTB.
3. Single defender model 4" from sole charging model → moves 3" toward it, ends ~1" b2b.
4. Two-model defender unit, one in BTB and one 2" away → second one piles in to BTB; first one is unchanged.
5. Defender unit where naive pile-in would put one model >1" from any unit-mate → that model's pile-in is reverted; coherency holds; remaining models still pile in.
6. Pile-in path blocked by another defender → step shortened so bases meet, no overlap.

### Known limitations / followups

- Vertical distance not considered (engine-wide; tracked under item 022).
- Defender player has no input — auto-resolved. If we ever want manual pile-in, add a `PileInRequest` and the matching CLI/GUI resolvers; the engine-side logic above becomes the AI/default path.

## Notes

- 2026-05-17: Implemented `PileInUtilities.ComputePileInMoves` + rewired `PileInStage`. Added 7 NUnit cases (`Tests/PileInTests.cs`) covering already-BTB, in-range and over-range pile-in, mixed unit, impassable-terrain block, defender-blocked-by-defender step shortening, and coherency invariant. All 121 submodule tests pass, full solution builds.
- 2026-05-17: Pulled from index, work item file created, plan signed off.

## Decisions

- 2026-05-17: Auto-resolve, no defender prompt — user preferred speed over fidelity. The rule's player discretion (which charging model to base, which path) is collapsed to "nearest, straight line". Pile-in is reactive and short enough that this is unlikely to feel wrong.
- 2026-05-17: Strict coherency — user chose this over "best-effort". Implementation: greedy revert, not constraint solver.
- 2026-05-17: No new `IStageTaskRequest` type, since the stage resolves itself. Avoids resolver inventory growth and network plumbing.

## Outcome

_TBD_
