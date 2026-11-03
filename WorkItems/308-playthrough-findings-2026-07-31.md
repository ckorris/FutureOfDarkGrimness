# 308 — Playthrough findings, 2026-07-31

**Status**: in-progress
**Related**: #197 (Mobile Artillery / MovedThisRound), #151 (token display), #248 (resolver Back), #237 (shoot pre-select)

## Goal
Six observations from a 2026-07-31 GUI playthrough, each resolved or explicitly parked:

1. **"Moved" token visible in game.** It should be hidden unless the bearer actually carries a rule that reads it.
2. **Cannot back out of shooting.** Back must be offered while nothing has fired this shoot action — and likewise after picking a unit to deploy.
3. **Target does not carry across weapons.** After the first weapon fires, the next weapon should start with the same target pre-selected when it is still a legal target.
4. **Deadly appeared to lose save dice.** Traced to Blast (see 5); no separate Deadly change.
5. **Blast's model-count cap was applied to the volley total, not per hit.** Owner ruling: the cap bounds ONE hit's fan-out and the multiplied hits stack.
6. **Morale/Fearless dice on an already-Shaken defender.** Parked — the engine's Shaken auto-fail short-circuit is present and tested; needs a repro save.

## Notes

- 2026-07-31: **(2b) Deployment back-out — shipped.** `DeployUnitStage`'s placement is now `allowCancel: true` with a new `BackToChooseUnit` binding wired to `ChooseUnitToDeployStage` (NOT to `DetermineNextDeployPlayerStage` — backing out is not the player's turn ending). `ChooseUnitToDeployStage` records `CurrentDeployingUnitPoolIndex` when it pulls the unit out, so the back-out re-inserts it at the slot the player found it in rather than at the end of the menu. New `PlacementCommitGuard.RequestClearPlacementOrCancel` (null on cancel) keeps the overlap guard on the committed path while letting the caller own the undo.
  - `PlaceObjectsRequest.CancelHint` added: the GUI resolver's Back tooltip was hard-coded to Disembark's "the unit stays aboard its transport", which became false the moment a second placement allowed cancelling. Every `allowCancel: true` site now words its own.
  - Safe for automated play: the CLI placement resolver only cancels on a typed "back" (EOF auto-places), and the AI resolvers never cancel.

- 2026-07-31: **(2a) Shoot back-out + (3) sticky target — shipped.** `ChooseRangedAttackRequest` gained `AllowCancel` and `PreviousTarget`, both set by the stage. The root cause of "cannot back out of shooting" was app-side: `GuiChooseRangedAttackResolver` kept a `_firesThisAction` counter reset only when the request's `AttackingUnit` **changed**, so any unit that had fired once never saw Back again — including on every later activation. The engine already knew the answer (`AlreadyUsedWeapons`), so it now says it, and the same test guards the Back button and the stage's own no-valid-shots fall-back. `PreferredTargetIndex` ranks the previous target above #237's sole-target pre-select (evidence of intent beats absence of alternatives) and never overrides an `UnselectableReason`.
  - A `Cancelled` arriving after a weapon has fired now ends the shoot rather than returning to Choose Action — otherwise a stale/ill-behaved resolver could hand a unit that already shot a second action from the menu.

- 2026-07-31: **(1) "Moved" token — shipped.** `TokenDefinition.VisibleOnlyWhenRead` + `TokenReadership.IsReadByAnyRule` (walks unit/model/weapon rules for a `TokenPresent` condition, recursing through And/Or/Not) + `TokenDisplay.ResolveProminence`. `ResolveVisible` takes the bearer so the app can ask. Deliberately CONDITIONS only — a rule that stamps a token is not a reader, and counting effects would make the movement stage's universal stamp mark everything as "read". A null bearer leaves the token visible (the caller can't prove it's unread). `MobileArtilleryShippedDataTests` pins that the one rule that *does* read it keeps its chip, against shipped book data.

- 2026-07-31: **(5) Blast — shipped.** `RollToHitStage` computed `min(hits * X, livingModels)`, so an A3 Blast(3) into a 3-model unit produced 3 hits instead of 9, deleting save dice the defender owed. Now `hits * min(X, livingModels)` (floored at 1 so a 0-living-model target is a no-op rather than an erasure). The `HitGroupSource` carries the EFFECTIVE multiplier so the save beat's arithmetic adds up ("3 hits x2 (Blast) = 6"). `Ai/Tactician/CombatMath` mirrors the same change — `CombatMathPinTests.Blast_CappedAtModelCount_MatchesEngine` pins the two against each other and caught the drift immediately. Four new cases in `BlastRuleIntegrationTests` cover the owner's exact examples (9 on 3 models, 6 on 2 models, effective-multiplier tag, dead models don't widen the cap).
  - Test-double gotcha: `FixedDiceRoller` reports `TotalRolls == 1` for ANY roll count, which silently collapses a multi-attack volley to one hit and hides per-hit stacking. The fixture had to move to `FixedFaceDiceRoller`, which honours the count.

- 2026-07-31: **(4) Deadly — no change.** Saves are rolled per HIT (`DetermineSaveRollsNeededStage` emits one `PendingSaveRolls` per `SuccessfulHitInfo`); Deadly multiplies WOUNDS afterwards, at `Shooting_OnPreApplyWound`, and `ConfineToClumps` applies the no-carry-over cap after the saves are already rolled. Nothing in the shooting chain clamps save dice to the target's remaining wounds. Owner agreed the observed "2 saves where 3 were expected" is the Blast total-cap above.

## Decisions

- **Blast's cap is per hit, and multiplied hits stack** (owner-ruled 2026-07-31). An A3 Blast(3) deals 9 into a 3-model unit and 6 into a 2-model unit. The rulebook wording ("no more times than there are models in the target unit") bounds a single hit's fan-out, not the volley.
- The beat carries the **effective** multiplier, not the authored one, so a capped Blast reads truthfully on screen; the authored value stays in the text log.
- **A resolver must not derive whether backing out is legal.** Both Back bugs in this batch were the app inferring engine state (a fire counter; a hard-coded tooltip). The request now carries the fact and the wording. Anything a resolver "remembers across requests" is a bug waiting for a repeat activation.
- **Token visibility is a per-bearer question, not a per-type one.** Hiding "Moved" outright would have taken the Mobile Artillery chip with it; a flag plus a readership walk keeps a future book rule working with no code change.

## Outcome
Five of the six shipped, each with tests (engine 2515 / app 844 green, headless smoke exits 0). Item 6 (morale + Fearless dice on an already-Shaken defender) is **deliberately parked**, not dropped: the engine's Shaken auto-fail short-circuit is in `MoraleUtilities.TakeMoraleTest` (since `3a2a6de`, 2026-06-28) and `ShakenAlwaysFailsMoraleTests` proves no die is rolled, so the report does not match any path found by inspection. Owner elected to re-observe with a repro save rather than have it hunted blind. Candidates to check first when a repro exists: the melee morale path (`RollForMoraleStage`), a spell-driven test (`CastSpellStage`), a rule-driven one (`GameOperationServices.MoraleTestThen`), and whether the unit was Shaken at the moment of the test or made Shaken by that volley.
