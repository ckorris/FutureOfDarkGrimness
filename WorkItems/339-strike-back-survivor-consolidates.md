# 339 — A unit that strikes back and kills its attacker gets a consolidation move

**Status**: in-progress (implemented + tested; awaiting GUI hand-verify)
**Related**: #017 (in-range melee checks / the "charger still consolidates" path), #090 (enemy-checked consolidation), #159 (lenient consolidation coherency)

## Goal
When a charged unit strikes back and wipes out the charger, it consolidates 3" like any other melee
winner. Before this, `ConsolidateStage` keyed off the ATTACKING seat alone: a dead attacker logged
"attacker has no living models - skipping" and the melee ended with the unit that actually won it
standing exactly where it started, while the mirror case (charger wipes out the defender) got its move.

## Notes

- 2026-08-04: **Renumbered 337 -> 339** (reconciliation 50). Filed as 337 off a freshly-fetched
  `origin/master` topping out at 336; origin landed its own 337 (Shaken picker badge) and 338 (Notice
  banner duration) in the minutes between filing and the pre-push fetch. Merged wins, so this item
  yielded. The two pre-renumber commit messages still say "#337".

- 2026-08-04: Implemented and closed in one slice (engine-only).
  - `ConsolidateStage.Enter` now picks the CONSOLIDATING unit rather than assuming the attacker:
    survivor of a wipeout, whichever seat it sat in. Everything downstream (player prompted, rule
    queries for fly-over / terrain-ignoring, enemy + friendly footprints, the request's unit binding)
    reads that unit instead of `context.AttackingUnit`. The skip is now "no living models on either
    side" (mutual destruction), which is the only case where nobody moves.
  - The prompt goes to the surviving unit's own player, i.e. out of turn — same as the strike-back
    offer (`OfferStrikeBackStage` already asks the defending player) and handled by the existing CLI /
    GUI / AI resolvers unchanged, since all three read the unit off the request.
  - Log line now names the unit: `Consolidation: <Unit> - Wipeout (up to 3").`
  - Tests: `ConsolidateStageTests.AttackerWiped_DefenderConsolidatesWithThreeInchCap` (replaces
    `AttackerWiped_NoRequestFiredAndStageExits`, which pinned the bug),
    `AttackerWipedMovesDefender_MovementIsApplied`, `BothSidesWiped_NoRequestFiredAndStageExits`, plus a
    new `StrikeBackConsolidationTests` that drives the real `MeleeStage` graph end to end (charge →
    swing → strike-back → kill → fatigue → consolidation) so the
    `StrikeBackStage.OnAttackerKilled` → `ConsolidateStage` wiring is covered, not just the stage.
  - Verify: engine suite 2812/2812 green, full `dotnet build` clean, headless smoke exit 0.

## Decisions

- **Only the wipeout case is two-sided; disengage stays one-sided.** When both units survive, the
  charger alone falls back 1" — that is the disengage rule, not a symmetric "everyone shuffles", so the
  defender is not prompted there. The survivor rule only changes who consolidates when one side is gone.
- **Mutual destruction skips.** With both units wiped there is nobody to move; the stage logs and exits.
- **Counter (role swap) needed no special handling.** `SwapCombatRoles` puts the charged unit in the
  attacking seat, so a Counter unit that kills the charger was already the "attacker" and consolidated
  correctly; the gap was only the ordinary strike-back path, where the charger stays the attacker.
- The engine-side dice doubles differ in a way worth remembering for melee flow tests:
  `FixedDiceRoller(n)` returns a degenerate histogram (every face reports n), so save/hit arithmetic
  reads oddly; `FixedFaceDiceRoller(face)` is the one that behaves like "every die rolled `face`".
  Save thresholds are clamped to 6, so an unsaveable attack has to be built with a low fixed face
  (Defense 4 + AP 3 vs. a fixed 4) rather than a high threshold vs. a fixed 6.

## Outcome
Shipped 2026-08-04, engine-only. The survivor of a melee wipeout consolidates up to 3" regardless of
which seat it fought from, so a unit that is charged, strikes back and kills the attacker now gets its
move (prompted on its own player, out of turn). Nothing deferred; no app-side change was needed since
all three resolver sets already read the consolidating unit off the request. Not GUI hand-verified —
the out-of-turn consolidation prompt appearing for the defending player is worth one look in the window.
