# 182 — Move through friendly units, but not stop on them

**Status**: DONE (implementation) 2026-07-11, **UNVERIFIED in the GUI**. Split and shipped as #205 (engine +
AI + CLI resolvers: `ValidateEndsOnFriendly`, friendly footprints threaded through `ValidatePaths`) and #212
(GUI: pass through friendlies, block only ending-on, in single + group mode). The rule and its integration
tests are in place and green; the GUI feel still needs Chris's eyeball, so this stays UNVERIFIED until then.

## Outcome

Implemented across #205 (engine authoritative check + AI/CLI resolvers back off; `EndedOnFriendlyUnit`
validation with a not-newly-overlapping guard; engine integration tests `EndsOnFriendlyValidationTests`) and
#212 (`GuiDefineMovementResolver`: friendlies are no longer pass-through clamps, and the Done gate rejects
ending on a friendly). Pass-through allowed, ending-on rejected, no standoff for friendlies - exactly this
item's goal. Remaining: GUI hand-verification (both single and group move).
**Related**: #011 (move-through-enemy + standoff — the enemy analog to mirror), #089/#090 (enemy-check on AI/consolidation/executor paths), #150 (`BaseShapeGeometry.SurfaceGap2D` true-footprint overlap), #155 (movement GUI preview clamps), #018 (pile-in)

## Goal
A moving model may pass **through** friendly models freely (no blocking, no standoff), but no model may **end** its move with its base overlapping a model of a *different friendly unit*. "Done" = movement validation rejects any path whose end-state overlaps a friendly (non-self) unit's footprint, while paths that merely cross friendly footprints en route are legal; the GUI/CLI/AI movement resolvers all honor it (preview clamps/​warns rather than throwing), and there's an integration test mirroring the nearest `*RuleIntegrationTests` that pins both halves (crossing allowed, ending-overlapped rejected).

## Notes
Newest on top.

- 2026-07-07: Filed. **Current behavior:** movement validation in `MovementUtilities` only ever considers *enemy* footprints — `GetEnemyModelFootprints` filters to `!alliedPlayers.Contains(a.PlayerID)` (MovementUtilities.cs ~line 181), and `ValidateMovingThroughEnemyUnits` is the only unit-vs-unit check. Friendly units from *other* units are entirely absent from validation, so today a model can both pass through **and** stop stacked on a friendly unit with no error. This item adds only the missing "can't stop on them" half — pass-through is already unrestricted and should stay that way. Same-unit models are governed by cohesion/formation packing, not this check.

## Decisions
Open questions to resolve before/while building (surface forks per the working conventions):

- **"Stop on" = base overlap at end-of-move.** Reuse the enemy *ending-stacked* end-state test (`BaseShapeGeometry.SurfaceGap2D` on the true facing-aware footprint, gap `< 0` = overlap) against friendly models, but WITHOUT the enemy pass-through guard and WITHOUT the 1" standoff (friendly units have no standoff). Confirm this matches the intended GDF/OPR reading (models can move through friends but must end clear of them).
- **Which models count as "friendly, not self":** every living model of an allied `PlayerID` (team-aware, like `GetEnemyModelFootprints`) minus the moving unit's own models. A hero-joined unit is one `IUnit`, so its merged models are "self" and excluded (they're a cohesion concern, per #018/#159), not blockers here.
- **Data plumbing:** likely a `FriendlyModelFootprint` set (or a reuse of the footprint struct with a friendly flag) supplied by `DefinePathStage` and threaded through the `ValidatePaths` overloads the same way #011 threaded `EnemyModelFootprint`. Decide whether to generalize the existing enemy machinery vs. add a parallel friendly path.
- **Resolver/preview coverage:** GUI (`GuiDefineMovementResolver` single + group), CLI (`DefineMovementPathResolver`), and AI (`AiDefineMovementResolver`) must all pass friendly footprints and the GUI preview should clamp/warn (mirror #155's enemy/terrain clamp) rather than let the stage throw. Consolidation + executor (Vanguard/Strafing) paths — decide whether they need the friendly end-overlap check too (they did for enemies, #090).
- **Out of scope / adjacent:** deploy-time friendly overlap (deployment placement is a separate resolver path; #048 handles impassible-terrain overlap, not unit-on-unit) — note whether it needs the same guard or is already prevented.

## Outcome
_(open)_
