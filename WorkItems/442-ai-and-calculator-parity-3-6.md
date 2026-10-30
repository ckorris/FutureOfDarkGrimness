# 442 — Tactician AI + Combat Calculator parity with 3.6

**Status**: todo
**Related**: #415, #191 (Tactician umbrella), #364 (melee threat reach), #360 (Indirect pricing), #066 (AI resolver legality tests), #325 (forecast), the phase-2 items it mirrors (#417, #422, #423, #424, #425)
**Branch**: `feature/opr-3.6`. Model/effort: Opus 5.5 / high. Run after phase 2 lands; read `docs/ai-agent-plan.md`.

## Goal

The AI and the Combat Calculator model the 3.6 game, not the 3.5 one. Both re-implement parts of the rules
for estimation, so each phase-2 change needs a mirror here:

1. Melee threat reach uses the 3" strike range (#417) and includes Assault weapons as melee attacks (#424);
   shooting threat includes Assault weapons at their range.
2. Expected damage: Impact resolves first and is cancelled by Fatigue (#422); Brace(X) reduces incoming
   Impact; Parry(X) reduces expected melee hits by the 6+ fraction (#423); Fatigue removes 6-triggers (#418);
   melee-loser prediction uses wounds taken (#419) incl. Fear and Scourge.
3. Action legality: Cumbersome units never plan Rush/Charge or advance-and-shoot; Indirect pricing is
   Hold-only for the LoS bypass (#425); Scout/Vanguard placement respects the 9" enemy clauses (#426).
4. Range-conditional army rules (#432): the AI's "attack from over 9"/6"" preference where it is cheap to add
   (at least do not price a Devout unit's 6-trigger when charging from 5").
5. New AI resolver for the Parry pick (#423 lands the EOF default; this item adds a legality test per #066's
   pattern) and for Versatile Expert/Warrior activation picks (#434: default AP(+1) vs Tough targets, +1 to
   hit otherwise).
6. The `--ai-profile` bench (#191's gate) re-run with 3.6 armies to confirm no regression in win rate vs the
   random/A baseline; record numbers here.

"Done" = the above with unit tests on the estimators and the bench numbers recorded.

## Acceptance

Estimator tests green; bench numbers within the #191 gate; a headless `--all-ai` game with 3.6 armies exits 0
with no illegal-action retries in the log.

## Code anchors (survey 2026-10-29)

- `FdgRaylib/` AI controllers and resolvers (`AiController`, per-request AI resolvers), `MeleeThreatReach`,
  `FdgRaylib/Rendering/CombatCalc/`. See the appended survey rows below.

### Survey rows (code survey 2026-10-29)

- `Ai/Tactician/CombatMath.cs` (:21/:119 Indirect, ~:240 Impact, :244-253 Counter, :253/:571/:596 weapon split,
  :347-348 Fatigue, :356 hit context, :402 cover, :658 rule scope); `TacticalAnalysis.cs:96` (`MeleeThreatReach`),
  :131/:350/:372; `TacticianPlanner.cs:808/1124/1363/1370`; `Search/TacticianActionSpace.cs:148`;
  `TacticianWeights.cs:220`; `AiUnitClassifier.cs:14-15`; `MacroActionGenerator.cs:198`; `SpellValuation.cs:166-184`;
  `Learning/PositionEncoder.cs:491`. AI resolvers: `Ai/Resolvers/AiConsolidationMoveResolver.cs:57`,
  `AiStringSelectionResolver.cs:185`, `Resolvers/TacticianPlaceObjectsResolver.cs:24`, `AiAircraftAdvanceResolver`.
- `BuiltInAssets/StrategistLeafV1.json` holds AI weights trained on 3.5 armies - decide re-train vs accept and
  record it.
- Calculator: `Calculator/CombatCalculator.cs:117-119` (Fatigue), :374 (weapon split); `FdgRaylib/Rendering/
  CombatCalc/`; tests `CombatCalculatorTests.cs`, `CombatCalculatorScreenTests.cs`.

## Notes

- 2026-10-30 (from #434): AI / calculator gaps the GDF sweep leaves: (1) Ravage is not priced by `CombatMath` at all
  (no `DealAutoWounds` reader); if you add it, use `ResolveImpactHitsStage.ChargerParticipant` (a joined hero's Ravage),
  respect the once-per-round `TokenType.StrikeDiceUsed` gate and the Fatigue condition, and price Regenerative Strength's
  4+ token pool. (2) Hazardous: the self-wounds now land on the models that fired (one die's worth each, overflow lost)
  and are worth Blast(X) / Deadly(X) - the AI does not weigh losing the gunner. (3) The activation-time reposition family
  (Wolfborn / Bounding / Rapid Blink / Nimble / Dash) raises the ordinary movement request at activation start; the AI
  answers it like a Harassing move with no plan for it. (4) Tag tokens (`SingleSpendHitBonus` / `SingleSpendApBonus`) are
  claimed one per attack; the AI takes the spend-first default; `CombatMath` does not read them (nor the Spotters'). Tag /
  Target target picking is the generic loop. (5) Mind Control's per-target once-per-round gate is invisible to the AI, which
  may spend it on a unit that cannot be moved again. (6) Slayer / Ranged Slayer now need a Hold: `CombatMath` passes its
  context's `AttackerMoved` into the hit-complete context (done), but its move-then-shoot planning should value the Hold.
  (7) Dodge / Spell Resistance / Entrenched / Guardian price through the shared evaluator already.
- 2026-10-30 (from #447): Impact(X) is per model now (X dice per living model carrying it; a joined hero's own Impact
  adds; Brace subtracts from the total). Done (pinned): `CombatMath.EstimateMelee` and the calculator's read-only
  pre-check take the charge-contact participants from the stage (`ResolveImpactHitsStage.ChargerParticipant` /
  `ChargeContactParticipants`), so both sum per model through the same effect, and the calculator's Impact row shows
  the pool as a chip ("5 models x Impact(1)"); pins `CombatMathPinTests.EstimateMelee_ImpactDice_SumPerModel_
  AJoinedHerosImpactAdds` (+2 re-pinned) and `CombatCalculatorTests` (+2). The #422 D8 note below is resolved. Open for
  you: (1) if the owner overrules #447 D1 (count only the models in melee range at charge contact), `CombatMath` has no
  charge-contact positions and needs its own approximation; (2) multi-model Impact units are worth far more on the
  charge than before (the built-in 5 Warriors with Impact(2) went from 2 dice to 10) - re-run the `--ai-profile` bench
  (goal item 6) with that in mind; (3) nothing else in `Ai/` reads Impact's X directly (grepped).
- 2026-10-30 (from #446): Dangerous terrain "or that activate in it". A unit with any living model's base in a
  `Dangerous` piece now takes the #420 test (one d6 per wound to destroy it, each 1 a wound) FIRST THING in its
  activation (`ActivatedInDangerousTerrainStage`), and one test per activation covers both triggers (a move out
  across the piece afterwards does not test again; `DangerousTerrainTestedThisActivation`). Parity: nothing to
  rewire - no estimator prices Dangerous terrain at all (no `ETerrainType.Dangerous` reader in `Ai/`, `Calculator/`,
  `FdgRaylib/Rendering/CombatCalc/`), and the search's rollouts run the real stages, so searched lines include the
  activation test. Pricing gaps for you, none changed in #446: (1) move scoring (`MovementPlanner`,
  `TacticianMovementResolver`, the solo `AiDefineMovementResolver`) neither avoids Dangerous pieces on the way nor
  values ENDING a move outside one - a unit that stops in lava now pays a full-pool test at its next activation, so
  an end-position penalty of about (wounds to destroy) / 6 expected wounds (scaled by the unit's value) would be the
  cheapest term; (2) activation order: a unit already standing in Dangerous terrain could be activated early to move
  out (it tests once either way this activation, but leaving stops the next one), or late if it is about to die
  anyway; (3) Flying/Aircraft are exempt (`MovementRuleQueries.IgnoresAllTerrain`), Strider is not.
- 2026-10-30 (from #421): Hero morale 3.6 (Tough(9)+ Hero aura: friendly units within 6" may test morale at the Hero's
  Quality while the Hero isn't Shaken; a joined hero's Quality is now also only lent while not Shaken, and only when
  better). Parity: nothing to rewire - no AI or Calculator code estimates morale Quality (no reader of
  `HeroStatRules.GetMoraleQuality`; checked `Ai/`, `Calculator/`, `Simulation/`), and the search's rollouts run the real
  morale stages, which now read `MoraleUtilities.ResolveMoraleQuality`. Pricing gaps for you, none changed in #421:
  (1) `TacticianRangedAttackResolver.cs:79` applies the flat `TacticianWeights.MoraleBreakBonus` to a target that
  would reach half strength without asking how likely it is to fail - discounting by the pass chance at
  `MoraleUtilities.ResolveMoraleQuality(ctx, target)` would make a target sheltered by a Tough(9)+ Hero (or its joined
  hero) correctly less attractive to break; (2) positioning: nothing values keeping own units within 6" of a friendly
  Tough(9)+ Hero, or prefers to shoot/charge enemy units outside their Hero's 6" (or to kill/Shake the aura Hero
  first). Both are optional value terms, not legality.

- 2026-10-30 (from #426): deployment placement parity. Done (pinned): every AI placement honours the new keep-out
  discs through `PlacementDistanceRules` - Scout's "over 9" from non-Scout enemies" (`AiPlacement_FindsTheLegalStrip_
  BehindAnEnemyLine`), Reinforcement's 9" band + 9" enemy minimum (`TheCopy_AiPlacement_StaysInTheNineInchBand_...`),
  and Vanguard/Fanatic/Drakesworn, which are now PLACEMENTS: `AiPlaceObjectsResolver.AdvanceAfterDeploying` answers a
  deploy-time reposition (`RepositionPlacement.DeployRepositionTaskName`) with a rigid shift toward the nearest enemy
  model, as far as legal, keeping the 1" standoff (parity with the old 9" Vanguard move, which the solo movement
  resolver marched at the nearest enemy; the Tactician inherits it). Gaps for you, none changed in #426: (1) the
  reposition aim is "nearest enemy", not objective-aware like the Tactician's deployment aim (`PreferredBlockCenter`) -
  a Scout-style aim (forward edge of the reach, toward the lane's objective) would fit; (2) the Tactician's Scout aim
  ignores the keep-out until the spiral search shoves it - aiming 9"+ off the nearest non-Scout enemy would place it
  deliberately; (3) the alternation (players alternate placing Scout/Ambush units) is invisible to the AI: it places
  its next unit in list order, never choosing WHICH unit to place in reply (the engine offers no pick, #426 D6);
  (4) the Combat Calculator has no deployment facet, nothing to do there.
- 2026-10-30 (from #425): Hold-vs-Advance parity status. Done (pinned): `CombatMath.EstimateShooting` exempts an
  Indirect weapon from a cut lane only on a Hold (it passes the candidate's `AttackerMoved` to
  `SightRuleQueries.IgnoresTerrain`; `TacticianLineOfSightTests.EstimateShooting_BlockedSight_IndirectAfterMoving_NeedsTheLane`);
  the 3.5 "-1 after moving" is no longer priced (it was the catalog's, now gone); the planner never generates a
  charge/charge-approach macro for a unit that may not Charge (`MacroActionGenerator` via
  `ActionRestrictionQueries.MayDeclare`), never credits a volley to an Advance plan of a Cumbersome unit
  (`TacticianPlanner.CanShootAfter`), and the activation urgency prices only the Hold volley for it
  (`TacticianPhantomVolleyTests.Urgency_CumbersomeShooter_PricesOnlyTheHoldVolley`); Cumbersome move budgets are
  Advance-only everywhere the planner reads them (`MovementRuleQueries`). Not modelled, yours: (1) ENEMY Cumbersome
  units are still priced as charge threats (`FactsOf(enemy).HasMelee`, `TacticalAnalysis.ChargeBudget` /
  `MeleeThreatReach` ignore the restriction) - gate them on `ActionRestrictionQueries.MayDeclare(enemy, ..., Charge)`;
  (2) #360 facet 1 is now Artillery's: moving forfeits the 3.6 defensive -1 (`MovedThisRound`) and the retaliation
  term still evaluates incoming fire with the unit's CURRENT tokens, so a move candidate keeps the -1 for free; (3)
  the urgency kill term prices only the advance-then-shoot volley for an Indirect unit - a Hold volley through a wall
  (Indirect's line-of-sight bypass) is not considered there (the planner's Score does consider it, Hold candidates
  carry `AttackerMoved: false`); (4) cover: `AttackContext.DefenderInCover` is caller-supplied and positional, so the
  planner does not know that an Indirect Hold shot ignores Sight-Blocker cover but keeps Cover-terrain cover
  (`ChooseRangedAttackRequest` rows now carry that per weapon - `TacticianRangedAttackResolver` reads them); (5) the
  Combat Calculator has an "attacker moved" input but no Hold/Advance labelling, so a user pricing an Indirect shot
  sees no difference except through cover/LoS, which the calculator does not model.
- 2026-10-30 (from #429): casting pricing gaps, none changed there (D7). (1) `SpellValuation.BaseCastSuccessChance`
  is a flat 0.5: it ignores Spell Amplifier (`CastRollRules.Net(caster, evaluator, live: false)`), the caster's granted
  cast tokens (`GrantedRollModifiers.PeekNet(caster, ERollKind.Cast)`), the Spell Conduit relay bonus and boost/hinder.
  The engine's own number is what `CastSpellStage.PickSpell` puts in `ChooseSpellRequest.BaseThreshold` (4+ less rules
  and granted, clamped [2, 6]); the chance is `(7 - threshold) / 6`. (2) The valuation measures range/LoS from the
  caster only - no relay origin (`SpellRelay.OriginsFor`) - so a target only a Spell Conduit reaches is valued 0 though
  castable. (3) No AI boost policy (solo and Tactician reply 0 boost); `ChooseSpellRequest.MaxUsefulBoost` is there to
  use. (4) `TacticianCastAssistResolver` prices a token at 1/6 of the spell's value: it ignores the [2, 6] clamp (a
  token past the 2+ floor buys nothing) and the caster's own modifiers. (5) Both bots skip before-attack abilities, so
  the AI never uses Spell Drain (new 3.6 book rule: enemy within 18" in LoS takes a Morale Test, loses 2 spell tokens on
  a fail); a cheap policy is "the in-sight enemy Caster holding the most tokens". LoS itself is per spell already,
  pinned by `TacticianCastingTests.Casting_ReadsLineOfSightFromTheSpell_NotTheCaster`.
- 2026-10-29 (from #419): melee-result parity. The engine's loser is now the unit that took the most wounds during
  the ACTIVATION: the melee's own wounds plus any it took earlier in the activation (a Dangerous Terrain test on the
  charge move, a Breath Attack / spell / Surprise Attack / Storm on the target; ledger `WoundsTakenThisActivation`,
  snapshotted as `CombatActionContext.Attacker/DefenderWoundsTakenBeforeMelee`). Fear(X) now lands on the enemy's
  taken total - numerically the same, so `CombatMath.MeleeEstimate.ExpectedWoundsMargin` (impact + swings + own Fear
  vs return + enemy Fear) is still exact for the exchange (comment updated, no code change). Not mirrored: (1) wounds a
  side already took earlier in the activation (read `WoundsTakenThisActivation.Peek` on both units when estimating
  mid-activation, e.g. after a before-attack ability); (2) the charger's expected Dangerous Terrain wounds on its
  charge path (`IUnit.GetWoundsToDestroy() / 6` when the path crosses Dangerous terrain); (3) Scourge, already noted below. The planner
  prices melee by `ValueFraction` margins and never prices the melee-result morale test itself (pre-existing), so
  the gap is small. The Combat Calculator computes no melee winner.
- 2026-10-29 (from #427): parity status. **Unstoppable's modifier immunity needs nothing here**: it lives in the
  evaluator's first pass (`RuleEvaluator.ApplyNegativeModifierImmunity`), and `CombatMath` / the calculator run the same
  evaluation with the weapon as an Actor participant, so a Stealth / Fortified / Evasive target is priced away for an
  Unstoppable weapon and the weapon's own Imprecise still bites; pinned against the real stage chain in
  `CombatMathPinTests.UnstoppableWeapon_*`. Known gap that stays: `CombatMath` never prices GRANTED hit tokens at all
  ("Not mirrored" in `EstimateVolley`), so an enemy "-1 to hit once" spell is invisible to the estimate whether or not the
  weapon is Unstoppable (the engine stage and `ShootingForecast` do honour it). **Spell Amplifier**: the AI's boost
  sizing reads `ChooseSpellRequest.BaseThreshold`, which now includes the passive bonus; the Tactician does not plan
  around WHICH spell to cast for the odds, unchanged. **Aircraft**: pile-in is engine-automatic (no AI choice); the AI's
  own consolidation and movement resolvers already tag Aircraft footprints uncontactable.
- 2026-10-29 (from #432): pricing of the range conditions (`attackingFromOver` / `attackedFromOver {9, 6}`). Done: the
  shooting forecast (`ShootingForecast`) and every real stage read the shooting distance; `CombatMath`'s hit contexts
  carry `DistanceInches`, so the shooting arm of the hit-side rules (Devout's extra hit, Havocbound's AP, Changebound's
  -1, Guarded's +1) prices; the charge budgets the AI plans with include Highborn's +2" (`TacticalAnalysis.ChargeBudget`)
  and Darkborn's -2" (`EffectiveChargeDistanceAgainst`), and `EffectiveRange` folds the range half. Not modelled:
  (1) `CombatMath`'s melee contexts pass no `ChargeOriginDistanceInches`, so no charge arm prices (a Ferocious charge
  looks like a plain one, a Changebound defender looks unprotected against a charge); (2) its `SaveRollCompleteContext`
  passes no distance, so the save-side shooting arms (Infected / Violent / Warbound / Destroyer's extra wound,
  Knightborn / Warding / Angelic Blessing's 6+ ignore) never price; (3) the morale estimates ignore Hive Bond / Hold
  the Line / Valiant's +1; (4) the Combat Calculator has no charge-distance input, so its charge arms never fire.
  Each is one argument on an existing context.
- 2026-10-29 (from #431): parity status of the weapon-rule family. Done: Pierce's positive-modifier ignore folds
  through the shared `RollToHitStage.WholeAttackSaveModifier` in `CombatMath` and `ShootingForecast`; the AP and
  re-roll gates run through the same evaluation. Not modelled: Scourge's counted wounds in `CombatMath`'s melee
  result / morale estimate and in the Combat Calculator, and Unwieldy (unit- or weapon-scoped) in `CombatMath`'s
  strike order (pre-existing for the unit-scoped one - only the defender's StrikeFirst is read).
- 2026-10-29 (from #422): goal item 2's Impact/Brace/Counter half is done - `CombatMath.EstimateMelee` reads Impact,
  Brace and Counter through the same evaluation the stages run, so the 3.6 Fatigue gates and the Counter/Brace split
  reached it with no code change; pinned in `CombatMathPinTests` (fatigued Impact charger, Brace defender, fatigued
  Counter defender) and `CombatCalculatorTests` (fatigued Impact charger, no Impact row). Open owner question #422
  D8: Impact(X) is rolled once per unit, not per model, in the engine and so in the AI.
- 2026-10-29 (from #417): goal item 1's strike-range half is done - `TacticalAnalysis.MeleeThreatReach`,
  `TacticianPlanner.MeleeReachOf` and `PositionEncoder.CheapThreatReach` read `MELEE_RANGE_INCHES_HORIZONTAL`,
  now 3". Open for this item: the shipped Strategist leaf net (`MlpPositionEvaluator`) was trained when
  `CheapThreatReach` added 2"; it now adds 3" for melee units (same schema, inputs drifted) - re-train or at
  least re-measure it in the goal-6 bench (#417 D5). One Tactician pin moved (#417 D3, the corridor scene).
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
