# 419 — Melee result 3.6: loser = most wounds taken this activation; Fear(X) rewording

**Status**: todo
**Related**: #415, #091 (morale core), #009, #175 (Fear/Fearless gating), #422 (Impact timing), #431 (Scourge adds "taken" wounds)
**Branch**: `feature/opr-3.6`. Model/effort: Opus 5.5 / medium.

## Goal

Core Rules v3.6.0, Melee Results: "Units in melee don't take Morale Tests from wounds at the end of an
activation. Instead, they compare the number of wounds each unit took during this activation, and the unit
with the highest total loses, and must take a Morale Test. If the test is failed whilst the unit is down to
half or less of its total size or Tough value, then it Routs [...], else it is Shaken instead."
Fear(X) v3.6.0: "When in melee with this model, enemies count as having taken +X wounds when checking who won."
(3.5: "This model counts as having dealt +X wounds".)

"Done" = the melee-winner comparison counts wounds TAKEN by each side during the whole activation - impact
hits (#422 moves them before the strikes), Counter strikes, regular strikes, strike-backs, spell/ability
wounds dealt during the activation, Scourge's "count as having taken 2 extra wounds" (#431) - and Fear(X)
adds X to the ENEMY's taken total. Tie = no loser (unchanged). Confirm whether the engine's current
"wounds caused" tally already equals "wounds taken by the other side" (it is the same number unless some
wounds were dealt outside the strike exchange, e.g. Impact or a pre-attack ability) and fix the gaps.

## What changes

1. Engine: `AssignMeleeMoralePenaltyStage` / `MoraleUtilities` winner computation; make the tally a
   per-unit "wounds taken this activation" accumulator fed by `ApplyWoundsStage` (the choke point #042's
   audit identified) rather than a strike-exchange count. Rout/Shaken split unchanged.
2. Fear(X): re-author as "+X to the opponent's taken count" (semantically identical to +X dealt, but the
   catalog description and any log line must say taken).
3. Tests: Impact wounds count toward the loser decision; a pre-attack ability wound counts; Fear control;
   `FearRuleIntegrationTests` wording.
4. Presentation: the melee-result banner/log already states the counts - update wording to "took".

## Acceptance

Tests above green; a charge where Impact kills two models and the strikes are even is lost by the defender.

## Code anchors (survey 2026-10-29)

- `MainUnitActionStage/MoraleUtilities.cs` (melee-winner + Rout), `MeleeStage/AssignMeleeMoralePenaltyStage`,
  `CoreRuleCatalog.Fear`. See the appended survey rows below for exact lines.

### Survey rows (code survey 2026-10-29)

Findings that shape the item: the engine ALREADY computes the winner from wounds each side took since the
melee snapshot (`DefenderRemainingWoundsAtStart - RemainingWounds()`), so Impact and Counter wounds count.
What "during this activation" adds is wounds taken BEFORE the melee snapshot in the same activation (e.g. a
Dangerous Terrain test on the charge move) - decide and record; plus Scourge's synthetic "taken" wounds (#431)
and the Fear wording.

- `MeleeStage/DetermineMeleeWinnerStage/DetermineMeleeWinnerStage.cs:30-31` (tally), :37-44 (Fear), :46-61
  (compare; tie = no test), :78-90 `SumExtraMeleeWounds` (`RuleOperation.ExtraMeleeWoundCount`).
- Snapshots `MeleeStage/CombatActionContext.cs:399` (attacker, at construction), `:510` `SetDefender` (before
  Impact), swapped by the Counter role swap :529-530.
- Fear `CoreRuleCatalog.cs:646-656` (`Melee_OnMeleeResolution` -> `Effect.ExtraMeleeWoundCount(Arg 0)`), effect
  `Effect.cs:999`. Loser morale `DetermineMoraleSaveNeededStage.cs:26-37`; Rout/Shaken
  `AssignMoralePenaltyStage/AssignMeleeMoralePenaltyStage.cs:40-49`.
- Tests: `DetermineMeleeWinnerStageTests.cs`, `FearRuleIntegrationTests.cs` (8), `AssignMeleeMoralePenaltyStageTests.cs`,
  `CounterRuleIntegrationTests.cs:47` (asserts the swapped snapshot).

## Notes

- 2026-10-29 (from #431): `DetermineMeleeWinnerStage` now also adds Scourge's counted wounds
  (`CountedWounds.Take(unit)`, a `TokenType.CountedWoundsTaken` magnitude on the STRUCK unit) to what its opponent
  dealt - Fear's polarity, i.e. "enemies count as having taken X". When you rewrite the result as "wounds each
  unit took this activation", fold them into the struck unit's TAKEN total and keep the `Take` (it consumes them);
  `ScourgeRuleIntegrationTests` pins the three melee cases. `ReconcileEndOfActivationStage` sweeps the token off
  every unit at activation end.
- 2026-10-29: filed with the plan.

## Decisions

(none yet)

## Outcome

(open)

## Rules for the agent (plan section 1, restated)

- Branch `feature/opr-3.6` in BOTH repos; check `git branch --show-current` in each before committing.
  Never commit, merge or push `master`. Submodule-first commit cadence; subjects start with `NNN:`.
- 3.6 rule text: `~/Projects/GDF Armies/Rules 3.6.0/` (core PDFs' text extracts, common-rules JSON) and the
  3.6.0 book snapshots. Never commit anything from that folder. No 3.5 fallbacks or version flags.
- Vertical slice: engine change + CLI resolver + GUI resolver/overlay + AI resolver legality + an
  integration test mirroring the nearest `*RuleIntegrationTests` + the `docs/` line if a number changed.
- Verify per CLAUDE.md (engine suite green, full build, headless smoke exit 0) and
  `FdgRaylib --rule-coverage FdgRaylib/Assets/Books` not regressing. Never commit red.
- Keep this file's Notes/Decisions current and flip the row in `docs/opr-3.6-upgrade-plan.md` section 5.
  A design fork stops for Chris's sign-off with options + a recommendation written here.
