# 337 — Takedown (Sniper): each rifle picks its own target unit

**Status**: CLOSED 2026-08-04 — hand-verified in the running app by the owner
**Related**: #157 (per-shot model picks, superseded here), #314 (Takedown facet correction), #276 (attack-beat
truthfulness / burst indices), #032 (Limited), #028 (resolve-first gating)

## Goal

Owner report: *"Sniper doesn't let you target different units with different rifles. It should."*

A unit firing several Takedown weapons chose ONE target unit for the whole volley. #157 split the volley
into per-copy shots so each sniper picked its own victim MODEL, but every shot stayed locked onto the unit
the first pick had chosen — three snipers could never cover three different targets.

## Decisions (owner sign-off 2026-08-04, before implementation)

- **Flow: one rifle at a time.** Firing a Takedown weapon commits exactly ONE copy; the rest go straight
  back into the shoot action's available pool, so `DetermineCanKeepShootingStage` re-offers the weapon and
  the next rifle chooses afresh through the existing weapon/target picker. Chosen over (a) a new per-shot
  target prompt inside a pre-split volley and (b) model-level targeting ("a unit of 1" rows across every
  enemy unit), because it reuses the whole targeting surface — forecast (#325), gating, CLI + GUI + AI
  resolvers, network — for a small engine change.
  Cost, accepted: firing N rifles at the SAME unit now takes N passes through the picker (the previous
  target stays pre-selected, so it is Enter-Enter).
- **The 2-unit cap still binds.** `MAX_TARGETED_UNITS_PER_SHOOT_ACTION` applies to sniper shots like every
  other weapon: three rifles spread over at most two enemy units, and the unit's other weapons share that
  budget. Takedown's text claims no exemption.

## How it works

- `CombatActionContext.AimPendingAttackOneCopyAtATime(out int copiesHeldBack)` replaces #157's
  `SplitPendingAttackIntoSingleShots`: it re-queues the pending attack as a single copy, puts the
  remainder back in `AvailableWeapons` under the same pool key, and corrects `AlreadyUsedWeapons` to the
  number of copies actually fired. A per-weapon `_copiesFiredThisAction` counter supplies the
  `BurstShotIndex`, so #276's attack beat still rotates carriers across the passes instead of animating
  the same rifle every time. Cleared by `SwapCombatRoles` with the rest of the attack state.
- `ChooseRangedAttackStage` asks `SightRuleQueries.TargetsIndividualModels` after the target commit and
  routes to the one-at-a-time path; the #276 eligible-copy trim is skipped there (it caps a batch, and
  trimming would strip the copies going back into the pool to be aimed elsewhere — the single firing copy's
  carrier is picked from the eligible ones by the attack beat).
- **Limited + Takedown** (BlessedSisters' Crossbow-Mod is the shipped case): new
  `LimitedRules.MarkOneCarrierFired` spends ONE carrier's once-per-game shot per firing, instead of marking
  every carrier — otherwise rifle 1 would burn the shots of the rifles still waiting to be aimed.
- Resolve-first gating (#028/#314) needs no change and now reads better: the unit's ordinary weapons stay
  locked until every Takedown copy has fired or been held.
- `WeaponOption` gained `AimedIndividuallyRule` + `CopiesRemaining` (engine-computed, like `LimitedRule` —
  weapon rules never cross the wire). CLI marks the weapon line
  `[Takedown: 3 left, fires 1 - each picks its own target]` and says hold fire drops all remaining copies;
  the GUI adds a `3 LEFT - AIMED 1 AT A TIME` row badge plus a details-pane sentence. Without this the
  weapon reappearing after firing reads as a bug.
- `DetermineMorePendingShotsStage` deleted and `FireStage.OnFinishedFiring` bound straight to
  `DetermineCanKeepShootingStage`: it existed only to drain a pre-split burst, and the queue can no longer
  hold more than one attack. Its "target destroyed -> remaining shots are discarded" rule goes with it —
  strictly better under the new flow, since the surviving rifles are re-offered and simply find the dead
  unit unfireable, so they aim at something else instead of fizzling.

## Notes

- 2026-08-04: Implemented as above. Engine 2813/0 (+4 new `ChooseRangedAttackStageTests`: one copy fired
  with the rest re-offered and the request carrying the rule + count; two rifles hitting two different
  units with burst indices 0/1; the third rifle still capped at 2 units; Limited+Takedown spending one
  carrier per shot. `TakedownRuleIntegrationTests`' three #157 split tests rewritten for the new flow —
  per-pass model picks through the REAL `BuildTargetListStage`, pool hand-back 2/1/0, and the dead-target
  case now asserting the remaining copies stay available). App-side 1062/0, full `dotnet build` clean.
  Headless: `Scenarios/340-sniper-split-targets.json` (3 Takedown rifles vs 2 enemy units, army
  `Scenarios/armies/340-Snipers.fdgarmy`) driven by hand — rifle 1 -> enemy Snipers, rifle 2 -> enemy
  Spotters, rifle 3 -> enemy Snipers, one model pick each, game ran to completion exit 0 with the AI
  driving its own snipers; default EOF smoke exit 0.
- **Awaiting GUI hand-verification**: fire a 3-rifle sniper unit in the GUI; expect the weapon row to badge
  `3 LEFT - AIMED 1 AT A TIME`, the count to fall 3 -> 2 -> 1 as each rifle fires, a Takedown model pick per
  shot, and the third rifle to find any third enemy unit greyed out with "Already targeting 2 units this
  shoot action."

## Outcome

Closed 2026-08-04. A unit's Takedown rifles now aim independently: firing one commits a single copy and
returns the rest to the shoot action's pool, so each rifle chooses its own target unit through the
existing picker. The 2-unit cap still binds (owner ruling), the ordinary weapons stay locked until every
Takedown copy has fired or been held, and a Limited+Takedown weapon spends one carrier per shot.

Owner hand-verified in the GUI on the `340-sniper-split-targets` save and confirmed it works. The session
log shows the flow end to end through the GUI resolvers: rifle 1 of 3 -> Left Guards (Takedown pick, model
killed), rifle 2 of 2 -> Center Guards (pick, miss), last rifle -> Center Guards (pick, model killed) - one
pass per rifle, the count falling 3 -> 2 -> 1, and two different enemy units hit by one unit's rifles,
which is exactly what the old behaviour could not do.

Commits: engine `ce71a25`; superproject `bfa9b93` (pointer + resolvers + work item), `6301d70` (fixtures).

## Deferred (explicitly, not silently)

- **"Fire all remaining copies at this target" in one pass.** The accepted cost of the chosen flow: a
  player who wants the whole squad on one unit walks the picker once per rifle. If it becomes annoying in
  play, the fix is a per-row count control (or a "fire all" button) on the picker plus a batched commit —
  the engine side would re-introduce a split of the pending attack.
- **Networked re-check.** The request gained two fields; they are plain scalars and ride the existing
  `WeaponOption` serialization (covered by the round-trip tests), but a host/client shoot has not been run
  since.
