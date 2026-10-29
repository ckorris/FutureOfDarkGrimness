# 411 — The renderer's overlays don't know army-embedded rules (false "no definition in the registry")

**Status**: implemented + tested; awaiting GUI hand-verify
**Related**: #408 (where it surfaced), #095 (army rule data persisted on ArmyData), #059 (embedded definitions), #328 (token chips)

## Goal

The table tooltip and the in-game army list resolve rules with the same definitions the game uses: the
core catalog plus each army's embedded book rules. A book rule granted mid-game is shown with its
description and valence, answers capability queries, and logs no false warning.

## Notes

- 2026-10-28: Found while the owner hand-verified #408 in `Scenarios/408-versatile-backout`. Picking Versatile
  Attack's AP(+1) logged `Granted rule 'Versatile Attack (Piercing)' on Veteran Prime Master has no definition
  in the registry - the grant does nothing.` The game was unaffected: the engine's evaluator has the
  Prime Brothers book rule, and headless never logs it. `TableTooltipOverlay` and `ArmyListOverlay` each
  built a CORE-ONLY resolver (`CoreRuleCatalog.CreateResolver()`), and the tooltip's transport
  capability query walked the unit's grants through it.
  - **Why it matters.** `RuleDiagnostics.WarnOnce` dedupes by rule name process-wide, so the false
    alarm would hide a genuine one later. Token chips also showed every book-rule grant with no
    description and Neutral valence.
- 2026-10-28: Number taken from `origin/master` at `2d40cee` after `git fetch`: index + archive max **410**.
  No collision.
- 2026-10-28: **Implemented.** New `FdgRaylib/Rendering/DisplayRuleSet`: the core catalog plus every
  `ArmyData`'s persisted rule definitions.
  - It absorbs each army once its data is present, which suits clients, where the data arrives by
    replication.
  - It resets when the set of armies changes, so one game's book rules never leak into the next.
  - Both overlays refresh it at the top of `Draw`; once every army is absorbed, a refresh is just a count
    compare.
  - Tests: `DisplayRuleSetTests` (a book rule granted to a unit resolves and answers a capability query
    with no warning; core still answers without army data). App 2999 green, headless smoke exit 0.

## Decisions

## Outcome

Pending GUI hand-verify: in `Scenarios/408-versatile-backout`, pick a Versatile Attack mode. No `[rules] ...
no definition` line should appear in the log, and the unit's token chip should show the mode's description.
