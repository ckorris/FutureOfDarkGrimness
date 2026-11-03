# 419 — Melee result 3.6: loser = most wounds taken this activation; Fear(X) rewording

**Status**: done 2026-10-29 (owner review asked on D1, D3). Engine commit `c77d964f`.
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

- 2026-10-29: done. Engine `c77d964f` (submodule `feature/opr-3.6`).
  - Survey confirmed: the old tally already equalled "wounds taken by the other side" for everything inside
    the melee (Impact, Counter strikes, swings, strike-backs, the striker's own Hazardous overheat), because it
    measured each unit's remaining-wounds drop since the melee snapshot (attacker at context construction,
    defender at `SetDefender`, before Impact). The gap was only wounds taken BEFORE the snapshot in the same
    activation, plus the Fear/Scourge bookkeeping polarity and the wording.
  - Engine: new activation wound ledger `Rules/Dispatch/WoundsTakenThisActivation.cs`
    (`TokenType.WoundsTakenThisActivation`, Invisible magnitude, RoundEnd backstop), fed at the two seams wounds
    land through - `ApplyWoundsStage` (every attacker-caused wound) and `CasualtyPresentation.ApplyAndPresent`
    (Dangerous Terrain, spillout, Hazardous / No Retreat via `ApplyUnitWounds`, reflect; its signature now takes
    the `IUnit`) - and swept table-wide in `MainUnitActionStage.Enter` and `ReconcileEndOfActivationStage`.
    `CombatActionContext` snapshots it as `AttackerWoundsTakenBeforeMelee` / `DefenderWoundsTakenBeforeMelee`
    (melee only; swapped by `SwapCombatRoles`). `DetermineMeleeWinnerStage` totals
    `WoundsTakenBeforeMelee + (RemainingWoundsAtStart - RemainingWounds)` per side, adds each side's Fear to the
    OPPONENT's total and Scourge's counted wounds (`CountedWounds.Take`, kept) to the struck unit's own; tie = no
    loser, unchanged.
  - Fear(X): catalog description is now the 3.6 text verbatim; `RuleOperation.ExtraMeleeWoundCount.Describe()`
    (the `--trace-rules` narration) reads "made enemies count as having taken +X wound(s) in the melee result";
    doc comments on the effect/operation/hook/context say "taken". No supplement or `AofBookOverrides` definition
    reuses `extraMeleeWoundCount`, and the 3.6.0 snapshots carry no other Fear-like rule (only Fear and Scourge
    mention "who won"), so no data changed.
  - Presentation: there is no melee-result banner or GUI overlay - the result is a log line, shown by the CLI and
    the GUI console alike. It now reads `Melee result: A took N wounds, B took M wounds - B took more and loses
    the melee.` (or `- a tie, nobody tests morale.`), plus `X already took N wounds earlier this activation - they
    count.` and `B counts as having taken +X wounds (A's Fear).` ASCII only. Same-named units (mirror matches)
    get their owners' names, else their seats.
  - AI/Calculator (item 5): `CombatMath.EstimateMelee`'s margin is already "defender taken - attacker taken"
    incl. Impact and Fear (numerically unchanged by the polarity flip; comment updated). Not mirrored: wounds
    taken earlier in the activation - hand-off note in #442. The Combat Calculator computes no melee winner.
  - Tests: engine 3725 -> 3742 passed (1 skipped); app 3066 -> 3066. New `MeleeResultRuleIntegrationTests` (6);
    `DetermineMeleeWinnerStageTests` +5 (renamed to "took"); `FearRuleIntegrationTests` +2 (wording, both-sides
    tie); `CounterRuleIntegrationTests` +1 (pre-melee snapshot swap); `RuleOperationDescribeTests` pins updated
    (+1 case). Every new test was broken on purpose and seen to fail (pre-melee portion dropped, ledger read live
    = double count, each seam unfed, each sweep removed, the Counter swap skipped, Fear back to "own side",
    the defender's Fear dropped, the Fear log reverted, label disambiguation removed, and a strike-exchange-only
    tally for the acceptance test).
  - No GUI resolver touched, so no GUI hand-verify is owed.
- 2026-10-29 (from #431): `DetermineMeleeWinnerStage` now also adds Scourge's counted wounds
  (`CountedWounds.Take(unit)`, a `TokenType.CountedWoundsTaken` magnitude on the STRUCK unit) to what its opponent
  dealt - Fear's polarity, i.e. "enemies count as having taken X". When you rewrite the result as "wounds each
  unit took this activation", fold them into the struck unit's TAKEN total and keep the `Take` (it consumes them);
  `ScourgeRuleIntegrationTests` pins the three melee cases. `ReconcileEndOfActivationStage` sweeps the token off
  every unit at activation end.
- 2026-10-29: filed with the plan.

## Decisions

- **D1 (OWNER REVIEW) - wounds taken before the melee in the same activation count.** "The number of wounds each
  unit took during this activation" is read literally: a Dangerous Terrain test on the charge move counts against
  the charger, and a Breath Attack, spell, Surprise Attack or Storm that struck the target earlier in the
  activation counts against the target. Consequence: a charger that bleeds on its way in is likelier to lose;
  softening a target before charging it is likelier to win. The alternative (count only the melee's own wounds,
  the old behaviour) contradicts the text. (A unit cannot charge twice in one activation here, so "a second
  melee inherits the first one's wounds" does not arise.)
- **D2 - mechanism: ledger for the past, snapshot for the melee.** The pre-melee part comes from the activation
  wound ledger read at the melee snapshot; the melee's own part stays the remaining-wounds drop since the
  snapshot. Rejected: ledger-only (the exchange would then depend on every wound path feeding the seams, and 20+
  pinned tests deal wounds directly); an activation-start remaining-wounds snapshot (a heal during the activation
  - Mend before charging, Reanimation - would offset wounds taken).
- **D3 (OWNER REVIEW) - only wounds that land count, and heals never subtract.** A save, Regeneration-style
  ignore or overkill past a model's last wound never reaches the ledger (overkill was never counted before
  either); a wound healed later in the activation still counts as taken. Inside the melee the snapshot drop would
  net a heal, but no heal fires between the melee snapshot and the result.
- **D4 - ledger scope.** Swept table-wide at activation start (`MainUnitActionStage.Enter`) and end
  (`ReconcileEndOfActivationStage`), RoundEnd as backstop, so round start/end effects and deployment never count.
  Not ledgered: the Reinforcement fall-back removal and the Rout removal (removals, and after the result anyway).
- **D5 - Fear keeps its engine shape.** `Effect.ExtraMeleeWoundCount` (JSON `extraMeleeWoundCount`) and
  `RuleOperation.ExtraMeleeWoundCount` are not renamed (no data churn, old army files keep loading); the stage
  puts the amount on the enemy's taken total, numerically the same as 3.5's "+X dealt". Description = 3.6 text.
- **D6 - Scourge.** Counted wounds join the struck unit's own taken total (numerically what #431 did) and are
  still consumed by `CountedWounds.Take`.
- **D7 - presentation.** The result stays a log line (no banner was there; none added). Wording per the Notes;
  same-named units are labelled with owner names, then seats.

## Ledger

| Facet | Code anchor | Test |
|-------|-------------|------|
| Impact wounds count (acceptance: Impact kills 2, strikes even, defender loses) | `DetermineMeleeWinnerStage.Enter` (snapshot drop) | `MeleeResultRuleIntegrationTests.ImpactKillsTwo_StrikesEven_TheDefenderTookMoreAndLoses` (+ control `SameChargeWithoutImpact_TheChargerTookMoreAndLoses`) |
| Each wound counted once (ledger also sees melee wounds) | `CombatActionContext.WoundsTakenBeforeMelee` read at snapshot only | `DetermineMeleeWinnerStageTests.WoundsLedgeredDuringTheMelee_AreCountedOnce`, the acceptance test's tally line |
| Wound before the snapshot counts - Dangerous Terrain on the charge move (D1) | `CasualtyPresentation.ApplyAndPresent` -> `WoundsTakenThisActivation.Add` | `MeleeResultRuleIntegrationTests.ADangerousTerrainWoundOnTheChargeMove_CountsForTheCharger`, `DetermineMeleeWinnerStageTests.AWoundTakenEarlierThisActivation_Counts` |
| Pre-attack ability wound counts (Breath Attack shape) (D1) | `ApplyWoundsStage` -> `WoundsTakenThisActivation.Add` | `MeleeResultRuleIntegrationTests.ABeforeAttackAbilityWound_CountsForTheUnitItStruck`, `DetermineMeleeWinnerStageTests.AWoundTheDefenderTookEarlierThisActivation_Counts` |
| Ledger scope = one activation (D4) | `ReconcileEndOfActivationStage`, `MainUnitActionStage.Enter` | `MeleeResultRuleIntegrationTests.WoundsFromAnEarlierActivation_DoNotCount`, `TheStartOfAnActivation_ClearsEveryUnitsLedger` |
| Ledger is melee-only | `CombatActionContext.WoundsTakenBeforeMelee` (`_isMelee`) | `DetermineMeleeWinnerStageTests.ARangedContext_DoesNotSnapshotTheLedger` |
| Counter swap keeps snapshots with their units | `CombatActionContext.SwapCombatRoles` | `CounterRuleIntegrationTests.CounterSwap_SwapsTheWoundsTakenBeforeTheMelee` (+ existing `ChargedCounterUnit_StrikesFirst_RolesSwapped`) |
| Fear(X) on the enemy's taken total; tie handling (D5) | `DetermineMeleeWinnerStage.Enter`, `CoreRuleCatalog.Fear` | `FearRuleIntegrationTests` (8 existing, reworded) + `EqualFearOnBothSides_EvenExchange_StaysATie` |
| Fear wording (log, description, narration) | `DetermineMeleeWinnerStage`, `CoreRuleCatalog.Fear`, `RuleOperation.ExtraMeleeWoundCount.Describe` | `FearRuleIntegrationTests.FearLog_SaysTheEnemyCountsAsHavingTakenTheWounds`, `RuleOperationDescribeTests` |
| Scourge counted wounds on the struck unit's own total (D6) | `DetermineMeleeWinnerStage.Enter` (`CountedWounds.Take`) | `ScourgeRuleIntegrationTests` (unchanged, green) |
| Result line wording, same-name labels (D7) | `DetermineMeleeWinnerStage.ResultLabels` / `Wounds` | `DetermineMeleeWinnerStageTests.SameNamedUnits_TheResultLineTellsThemApart`, acceptance test |

## Outcome

Done 2026-10-29 (engine `c77d964f`). The melee loser is the unit that took the most wounds during the
activation - Impact, Counter strikes, swings, strike-backs, self-inflicted wounds, and wounds taken earlier in
the activation (D1) - with Fear(X) counted on the enemy's side and Scourge's counted wounds on the struck unit's.
Tie = no loser. Owner review asked on D1 and D3. Deferred: the Tactician's melee estimate does not price wounds
taken earlier in the activation (#442 note). Nothing else cut.

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
