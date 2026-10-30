# 442 — Tactician AI + Combat Calculator parity with 3.6

**Status**: in-progress (2026-10-30)
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

- 2026-10-30 (#442 batch 8 - #419 / #431 / #435 melee-result and strike-order parity, B): `MeleeEstimate.
  ExpectedWoundsMargin` is now `DetermineMeleeWinnerStage`'s comparison: each side's taken total adds the wounds it
  only COUNTS as taken (Scourge - `AttackEstimate.ExpectedCountedWounds`, folded from the same save-complete
  `AddCountedWoundsTaken` ops `AssignWoundsStage` banks) and what it took earlier in the activation
  (`WoundsTakenThisActivation.Peek` on both units, new `MeleeEstimate.Attacker/DefenderWoundsTakenBefore`). No planner
  term reads the margin today (the planner prices melee by value-fraction trades, pre-existing - #419's note), so this
  is estimator parity for the next reader; the shooting resolver's half-strength "breaks" check DOES now allocate
  Scourge's counted wounds virtually (`CombatMath.EstimateVolleyWithCounted`), as the morale check does. The strike
  order fans the CHARGER out with its melee weapons too (`DetermineStrikeOrderStage.ActorWithMeleeWeapons`) and reads
  StrikeLast as well as StrikeFirst, so a 3.6 weapon-scoped Unwieldy (or the unit-scoped grant) swaps the order as
  the stage does. Great Sergeant's AP(+2) needed no change (it rides the champion's weapon copy like Sergeant) - now
  pinned. Pins (`CombatMathPinTests`): Scourge counted wounds vs the engine's bank, the margin with Scourge and with
  2 wounds taken earlier, the Unwieldy charger (and the cannot-swing guard), Great Sergeant's copy vs the stages.
- 2026-10-30 (#442 batch 7 - #429's casting gaps, B): `SpellValuation.CastThreshold / CastSuccessChance` are the
  engine's own number (4+ less `CastRollRules.Net` read-only, less the caster's granted cast tokens peeked, less a
  relay's bonus, clamped 2-6; `CastSpellStage.CastSuccessThreshold` is a new public alias of its private constant),
  replacing the flat 0.5. Targets are reached from every origin `SpellRelay.OriginsFor` offers (a Spell Conduit relay
  measures range and sight from its own models) and the net value takes the best bonus among the origins covering
  every counted target. `TacticianCastAssistResolver` never spends past what a token can still move (2+ floor for a
  boost, 6+ ceiling for a hinder). Spell Drain (new 3.6 rule) is now used: the planner takes a drain-shaped
  before-attack ability (MoraleTestThen -> ConsumeToken(SpellTokens), Foe) first in Choose Action, once per
  activation, when an enemy in reach holds tokens it may fail to keep (expected drain = min(2, held) x P(fail the
  rule-forced test), from batch 6's helper with cause Other, so Banner does not count); a new
  `TacticianBeforeAttackTargetResolver` answers the following target pick with the aimed enemy (or the offered one
  with the most expected drain), never Cancel, and hands every other pick to solo. Pins: `TacticianCastingTests` +4
  (threshold with Spell Amplifier / granted tokens / clamp, net value +1/6 per face, a target only a relay reaches is
  valued at the relay's +1, assist never past the clamp), `TacticianSpellDrainTests` (5, incl. the legality pin).
  Smoke: Blessed Sisters vs Daemons of Lust, tactician mirror, seeds 1-6: the drain fires when the Sacred Mirror
  still holds tokens at the Exo-Suit's activation (seed 6: "Sacred Mirror failed the morale test. Exo-Suit used Spell
  Drain before attacking."), otherwise "no enemy in reach holds tokens to lose" (the Mirror spends all three on its
  own activation). Not built (optional per the brief): a self-boost policy - both bots still reply 0 boost. Mind
  Control stays unused by the AI (steering an ENEMY unit's 6" move needs its own policy), so #434's once-per-round
  gate note has no reader yet - C, deferred.
- 2026-10-30 (#442 batch 6 - morale pass chance, B: #421 + #432 (3) + #435 Banner): new
  `TacticalAnalysis.MoralePassChance` prices `MoraleUtilities.TakeMoraleTest` instead of rolling it - the Quality from
  `ResolveMoraleQuality` (own / joined hero / Tough(9)+ Hero aura; a new `ITableState` overload, the `IGameContext`
  one delegates - D8), the pre-test modifiers through the same `PreMoraleTestContext` (Hive Bond / Hold the Line /
  Valiant's +1 when shot from over 9", Banner's +1 only for a shooting- or melee-triggered test), granted morale tokens
  peeked, the 2-6 clamp, Fearless's 4+ second chance, Shaken = 0, a No Retreat conversion = pass. The shooting
  resolver discounts `MoraleBreakBonus` by it (D7) and now measures the attack the way the hit stages and the ranged
  morale test do (unit to unit, base to base - it used shooter-centre distances, so a "shot from over 9"" arm could
  fire an inch early). Pins: `MoralePassChancePinTests` (11; the engine's test enumerated over all 36 decisive-die
  pairs: plain Q2/4/5, Fearless, Hive Bond at 10"/8", Banner shooting/other, Hero aura at 5"/7", Shaken) and
  `TacticianTargetChoiceTests.Shooting_BreakBonus_PrefersTheMobLessLikelyToPassItsTest` (fails with the flat bonus).
  Not added: a melee-loser morale estimate (the planner never prices the melee-result test, pre-existing - #419's
  note), so Banner / charged-from-over-6" morale arms have no AI reader yet beyond this helper.
- 2026-10-30 (#442 batch 5 - #425's enemy Cumbersome, B): an ENEMY that may not declare a Charge is no charge threat
  anywhere the Tactician prices one - `TacticalAnalysis.MayCharge` (`ActionRestrictionQueries.MayDeclare(..., Charge)`)
  now gates the planner's `EnemyFacts.HasMelee` (retaliation, projected threat, approach staging, the cover habit's
  melee share), the transport bail, the screen lane's melee threats, `TacticalAnalysis.ThreatRangeAgainst` (the kite
  band), the shooting resolver's "threatens us" factor and the ambush-spot retaliation. It still strikes back, so the
  exchange estimates themselves are unchanged. Not touched on purpose: `Learning/PositionEncoder.CheapThreatReach`
  (the shipped Strategist leaf's input; D6). Pins: `TacticianPlannerTests.CumbersomeEnemy_IsNoChargeThreat` (a Hold
  candidate 8" from Cumbersome brutes scores higher than from plain ones), `TacticalAnalysisTests.
  ThreatRange_CumbersomeBrawler_ThreatensNoCharge`, plus `ChargeOriginDistance_IsTheBaseEdgeGap_...` for batch 4's
  helpers.
- 2026-10-30 (#442 batch 4 - #432's charge and save-side arms, B): `AttackContext.ChargeOriginInches` (new) reaches
  `CombatMath`'s hit-modifier, hit-complete AND save-complete contexts for a charging melee swing only (a strike-back
  or a Countered charge carries none, as `CombatActionContext` reports); the save context also gets the shooting
  distance, and a spell's wounds `IsSpell` (it was priced as shooting: Spell Resistance / Knightborn's "spell wounds
  ignored on 2+" never fired in `SpellValuation`, an older gap the same fix closes). `EstimateMelee` takes
  `chargeOriginInches`. Callers: the planner's own charge reads the activation-start gap (`ChargeOriginTo`, the
  engine's snapshot - kept across batch 3's memo reset because the engine measures before that move too); an enemy's
  charge into a candidate endpoint is priced from an approximate edge gap (centroid distance less both units'
  `TacticalAnalysis.Spread`), memoized per half-inch; transport bail / screen lane / alternative-target terms measure
  the real gap; the melee-defender pick reads the planner's snapshot (`MeasuredChargeOrigin`); ambush arrivals use the
  victim's spread; `DeploymentMatchup` keeps 0 (no positions yet). D4, D5. Pins (`CombatMathPinTests`, all against the
  real stages): Ferocious charging from 7" / 5", a Changebound defender charged from 7" / 5", a Knightborn defender
  shot from 10" / 8" (save-side shooting arm), an Infected weapon at 10" / 8" (save-side extra wound), the spell arm
  through `ResolveSpellDamageStage`'s chain, and `EstimateMelee`'s origin reaching the charger only (and vanishing
  under Counter); `TacticianPlannerTests.ChargeCandidate_PricesTheChargeArm_FromTheActivationStartGap` (7" / 5").
  Negative controls: dropping the save context's distance / IsSpell fails 3 (Knightborn 10", Infected 10", spell);
  dropping the hit contexts' origin fails 3 (Ferocious 7", Changebound 7", the EstimateMelee case).
- 2026-10-30 (#442 batch 3 - the activation-start reposition move): the Tactician no longer hands #434's
  activation-start triggered move (Wolfborn / Bounding / Rapid Blink / Nimble / Wave-Step) to solo.
  `TacticianPlanner.AimActivationStartMove` plans the activation from where the unit stands (the search's prescribed
  macro when one is pending) and `TacticianMovementResolver` spends the move toward that plan's endpoint - a rigid
  shift straight at it, longest legal fraction first, or the planner's route-following move when that gets closer (a
  wall); a Hold plan declines (Cancel). Every activation memo is reset afterwards (the board changed). D3. Smokes
  (same 22 games): 0 solo-fallbacks (was 129), 102 moves toward the plan, 26 stays, 3 declines with no legal move,
  0 invalid paths. Quick A/B, tactician vs solorules, pool {wolf, jesters, deepsea, ck, ghosts} (the reposition
  armies), 1000 games each, seeds 5000+: new 86.4% (809-82-109) vs old 86.3% (812-86-102) - neutral at this army
  size, so kept for the fallback-rate and the principle (D3). Tests: `AiTriggeredRequestLegalityTests` +6 (melee
  unit takes the move toward the enemy; wall; enemy inside the standoff; a shooter follows its own plan at 14/22/40").
  Not changed (pre-existing, not 3.6): a triggered move AFTER the plan - Dash at the end of the activation (a move
  since #434, a placement before), Harassing / Hit & Run - still replays the cached plan, which the repair pass turns
  into a near-zero move toward where the unit already stands: the same "stand still" the placement form got.
- 2026-10-30 (#442 batch 2 - goal item 5, Versatile picks): new `TacticianAbilityEffectResolver` answers the
  activation-start "pick one effect" (Versatile Attack / Warrior / Expert, Watchborn, Vinci Tech) by pricing each
  option's helper rule against the best target the unit can shoot (advancing if it must) or charge this activation;
  solo's first option otherwise (D2). Pricing is exact by construction: `CombatMath.EstimateShooting / EstimateMelee /
  EstimateVolley` take an optional hypothetical attacker rule, carried on a rule-only stand-in weapon added as an extra
  Actor participant of the same unit (the evaluator's per-event dedup keeps the unit's own rules firing once), and the
  helper definitions come from a new `TacticianRuleLookup` (core catalog + every army's persisted rule data - what the
  game's own resolver is built from). Pins: `CombatMathPinTests.HypotheticalRule_PricesExactlyLikeTheGrantedRule`
  (x4: Versatile Warrior's Piercing / Precision helpers at 10" and 8", engine stages with the helper attached vs the
  estimate on a bare twin) + the melee no-leak case; `TacticianAbilityEffectResolverTests` (7: AP(+1) into Defense 2+,
  +1 to hit for Quality 5+ into Defense 6+ - options ordered so the old first-option answer fails both - no target,
  wrong bearer, and the #066 legality case). Negative control: with the carrier dropped inside `EstimateVolleyCore`, 4
  of those fail (both 10" pins, both pick tests). Smoke (Wolf Brothers vs Blessed Sisters, tactician mirror): the log
  narrates `pick Versatile Expert for ... -> AP(+1) (AP(+1)=0.125, +1 to hit=0.083)`. Known, pre-existing, not 3.6:
  after the pick the Tactician's bare evaluator cannot read the GRANTED helper (no rule resolver - the #191 gap), so
  its target choice later in the activation prices the attack without the pick (C, deferred: giving the planner's
  evaluator the `TacticianRuleLookup` resolver would price every aura and buff, a behaviour change worth its own
  bench).
- 2026-10-30 (#442 batch 1 - legality, A): 3.6 legality smokes. 14 scratch armies compiled from the #435 scratch books
  (`scratchpad/442/armies/`, built with a throwaway BuilderList compiler so upgrade picks and joined heroes are in:
  Wolf Brothers, Elven Jesters, Goblin Reclaimers (Hazardous picks), Blessed Sisters (Precision Target, Spell Drain),
  Alien Hives (Piercing Target, Regenerative Strength), Knight Brothers (Ravage), Custodian Brothers (Dash), Daemons of
  Lust (Mind Control), Chivalrous Kingdoms (Sergeant/Musician/Banner), Ghostly Undead, Deep-Sea Elves, Ogres (Ravage,
  Great Sergeant), Vampiric Undead (Mind Control, Ravage), Dark Elves (Regenerative Strength)). 11 pairings x
  {solorules, tactician} as FdgLab `smoke` games (both slots the same profile, seed 42): 22/22 exit 0, 0 faults,
  0 re-prompts, 0 "resolver path invalid" declines (the silent-decline path an illegal optional move would take - the
  debug line reaches FdgLab logs). 129 Tactician solo-fallbacks, every one the activation-start reposition move
  ("no cached plan"); 5 legal Cancel declines. Never exercised by either bot: Teleport (neither offers itself the menu
  option), Mind Control / Spell Drain / Tag and Target placement (before-attack abilities - both bots skip them).
  Tests: `AiTriggeredRequestLegalityTests` (5) - the real `ActivationStartStage` with the solo and the Tactician
  movement resolvers answering a Wolfborn move (open board, wall in the way, enemy inside the standoff), asserting the
  engine applied exactly the AI's path or the AI declined; the Tag spend prompt answered by the solo string resolver.
- 2026-10-30 (from #435): (1) Banner now helps only a Morale Test triggered by shooting or melee (`PreMoraleTestContext.Cause`,
  `EMoraleTestCause`); no AI / Combat Calculator code reads Banner or Courage today, but a morale estimate that adds Banner
  should skip spell / rule-forced tests. (2) Ethereal: the AI and the CLI accept the teleport offer and answer the placement
  with "stay put"; since #435 that costs nothing (the -6" needs `TeleportedThisActivation`, stamped only when a model moved),
  but an AI that wants the teleport's 6" must actually place the models, and the movement budget sees the -6" only after the
  teleport (the planner cannot price "teleport, then move 6" less" before choosing it). (3) Wave-Step now raises the
  triggered-move request at activation start (#434's reposition family) - the AI already answers it. (4) Great Sergeant's
  AP(+2) rides the champion's weapon copy like Sergeant's AP(+1); price it wherever CombatMath prices Sergeant.
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

- **D1 - what "no illegal-action retries" is measured as (2026-10-30).** The engine never retries: an invalid reply
  either throws (the game faults) or, for an OPTIONAL triggered move, is silently treated as a decline
  (`GameOperationServices.MoveUnit`, with a "resolver path invalid" debug line). So the gate counts faults, that debug
  line, and the stages' "re-prompting" lines; G3 solo-fallbacks are legal and reported separately as a rate.
- **D2 - the Versatile pick's currency (2026-10-30).** Each option is worth the best single-target value fraction
  (expected wounds / remaining wounds x UnitValue, the planner's `ValueFraction`) the unit can deal this activation
  with the helper hypothetically granted: a shot from here, or after an Advance when out of range, and a charge when
  inside its threat reach. The max over targets, not a sum: the unit attacks one target. Ties (within 0.0001) keep
  the authored first option, so Versatile Reach / Versatile Defense, whose helpers never change an attack estimate at a
  fixed distance, behave exactly as before.
- **D3 - the activation-start move heads for the activation's own plan (2026-10-30).** At activation start there is no
  Choose Action menu and no cached plan yet, so the planner pre-plans against the menu an unrestricted unit normally
  gets (Shoot, Move, Charge, Pass; the generator already drops charges the unit may not declare) and the move goes
  toward the best candidate's endpoint; Hold (or an endpoint under 0.5" away) declines. Rigid shift first because the
  planner's builders re-pack into formation at the end, which on a 1-3" budget ate most of the move (a 3-model column
  advanced 0.9" of 2" in the first test). Kept although the A/B is neutral (86.4% vs 86.3%, N = 1000 each): it removes
  the only systematic G3 fallback the 3.6 smokes showed and is the policy the planner would choose anyway.
- **D4 - the cover habit keeps an origin-free melee threat (2026-10-30).** The #365 habit term is a share in [0, 1]
  (melee that can reach this endpoint / all melee threat), and its denominator is endpoint-independent. Pricing the
  numerator with the endpoint's charge origin (a Ferocious enemy hits harder from over 6") could push the share past
  1 and the habit past its cap, so the share uses the origin-0 estimate; the RETALIATION and projected-threat terms
  use the endpoint's origin.
- **D5 - approximate charge origins where only a centroid exists (2026-10-30).** Candidate endpoints and ambush
  spots have no models placed, so the base-edge gap is the centroid distance less each unit's spread (farthest
  living base edge from its centroid), or the victim's spread plus 1" for a not-yet-placed arriver; memoized per
  half-inch. The planner's own charge and every current-position term measure the real gap. Deployment matchup
  pricing keeps 0 (no charge arm) - it has no positions at all.
- **D6 - the learned evaluator's inputs are left alone (2026-10-30).** `PositionEncoder` feeds the shipped Strategist
  leaf net (`StrategistLeafV1.json`); changing what it encodes (Cumbersome enemies' threat reach, charge arms) would
  drift a trained model's inputs without re-training, which is OWNER REVIEW territory (plan rule 8). Every estimator
  change in this item is in the hand-written policy and `CombatMath`; the net sees exactly what it saw at #417.
- **D7 - the break bonus is discounted, not renormalized (2026-10-30) - OWNER REVIEW.** The shooting resolver's
  half-strength bonus becomes `1 + (MoraleBreakBonus - 1) x P(fail)`, so a Quality 4 mob's 1.3 reads 1.15 and a
  Fearless one's 1.075. That lowers the bonus's average weight (it was a flat 1.3 since #191 A5-4, never tuned against a
  failure rate). The alternative - scaling so a coin-flip test keeps 1.3 (`... x P(fail) / 0.5`) - keeps the tuned
  average but would price a Quality 6 mob's break at 1.5. Chose the literal discount; the bench (D below) is the check.
- **D8 - one engine seam outside `Ai/` (2026-10-30).** `MoraleUtilities.ResolveMoraleQuality(ITableState, IUnit)`
  is a new overload the `IGameContext` one now delegates to (it only ever read `gameContext.TableState`), plus a
  public `FearlessRerollPassesOn` constant - no behaviour change, so the estimator reads the very Quality and
  re-roll the test uses instead of a copy. Flagged because #191's stop-and-ask list names engine changes outside
  `Ai/Tactician/`.

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
