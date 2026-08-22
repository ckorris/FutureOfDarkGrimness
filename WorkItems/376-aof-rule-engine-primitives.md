# 376 — AoF rules pt.2: new engine primitives

**Status**: in progress (started 2026-08-22)
**Related**: #375 (data half; feeds this item its list), mirrors #197. Engine submodule work — submodule-first commit cadence, full engine suite green. Reference doc: `/home/chris/Projects/GDF Armies/Age of Fantasy/Special Rules and Spells by Army.md` (local only, do not copy text into the repo).

## Goal

The AoF rule mechanics that cannot be authored with the existing effect/condition
vocabulary get real engine primitives, with integration tests mirroring the nearest
existing `*RuleIntegrationTests`, one vertical slice at a time. Done means: combined with
#375, every rule name the AoF books reference resolves to a definition that actually fires,
and anything deliberately approximated is recorded here as an owner-ruled facet (the #197
discipline).

Known candidates from the 2026-08-22 appraisal (~4 of 306 names; expect the #375 census to
adjust the boundary in both directions):

- **Bloodthirsty Fighter** — attacker gains +1 attack per enemy unmodified block roll of 1
  in melee. New seam: defender's defense dice feeding the attacker's attack count (Shred
  reads block 1s, Predator Fighter adds attacks — neither crosses this way).
- **Retreating Strike** — once per round, wounds dealt when the unit ends a post-melee move
  within 3" of an enemy. Extends the triggered-move family with a move-end proximity
  trigger.
- **Ravage Aura** — grants **Ravage(+1)**: an argumented, additive aura grant. The grant
  path is name-only today (LAT-1 fix made unargumented grants safe, not argument-carrying).
- **Reckless Piercing** — on activation, optional gamble: one die, 2+ round-long AP buff,
  1 round-long enemy AP buff against you. May fall out of the Unpredictable branch
  machinery; adjudicate before building.

- **Grounded Speed** (added 2026-08-22 from #375 C4) — terrain-conditional movement bonus:
  `mostModelsWithinInchesOfTerrain` requires `IHasTerrain`, which `MoveActionDeclaredContext`
  does not provide. Small slice: give the movement-declare context terrain access, then the
  rule itself is plain data (already drafted and reverted in #375 C4 - see its ledger).

- **Grounded Protection (+ Aura)** (added 2026-08-22 from #375 C7) — terrain-conditional
  ignore-wound (5+): same IHasTerrain gap on `SaveRollCompleteContext`. Fix both contexts in
  one slice; both rules are then plain data (drafts in #375 C4/C7 history).

- **Vale Oath Boost (+ Aura)** (added 2026-08-22 from #375 C5) — Shaken recovery at 3+
  instead of 4+. `clearTokenOnRoll` resolves as `InvokeClearTokenOnRoll`, an imperative
  executable that rolls once PER FIRING ENTRY, so base (4+) plus a boosted entry (3+) gives
  two recovery rolls (P 0.833) instead of one at 3+ (0.667). Needs either a threshold-shift
  parameter folded before the roll or a best-threshold-wins fold like WoundIgnoreSink.

Borderline (try as data in #375 first; move here only if the vocabulary refuses):
Shadowborn/Wild Veil Boost min-clamps on range/charge debuffs. RESOLVED as data in #375: Ethereal (rides Effect.Teleport's stage routing +
Slow-style negative movementBonus; C4), Great Sergeant (two addExtraHit hook entries, 5 and
6; C3).

## Notes

- [x] 2026-08-22 S4 Reckless Piercing DONE. Engine (submodule fb16b1d): grantTokenOnRoll
  gains an optional onFailure effect applied to the same unit on a miss (MoraleTestThen
  application pattern; service logs/presents "backfired") - one die, two exclusive outcomes.
  2 engine tests in TargetBonusMarkerTests. Data: base = opt-in ability at activation start
  (single offer -> YesNo, oncePerActivation) rolling 2+ boon / 1 exposed (both tokens roundEnd)
  + two token-gated passive arms at hook 73 (Actor Save -1 out; Subject Save -1 in - the corpus'
  first hostile Subject-seat save modifier, Mobile Artillery shape sign-flipped); Aura confers.
  6 app tests (RecklessPiercingShippedDataTests). Census dead 17 -> 16 (the Aura's 1 ref; base
  was grant-only). Remaining dead: Retreating Strike 14 (owner-deferred), Bloodthirsty Fighter 2.
- [x] 2026-08-22 S3 Ravage Aura DONE - data only, zero engine change (owner ruling). Standalone
  Unit-scoped def at Melee_OnChargeContact: dealAutoWounds literal 1 at 6+, so DealAutoWounds'
  carrier count (all living models) contributes 1 die/model and the stage's threshold-group SUM
  makes it arithmetically identical to upgrading every model to Ravage(X+1). Attachment safe:
  champion items fold rules into unit.SpecialRules at list-compile (ListCompiler.AddGrantedRule),
  so the stage's model-less Actor seat sees it. 3 app tests (RavageAuraShippedDataTests incl. the
  additive-sum proof: Ravage(2)x2 models + aura = 6 dice = Ravage(3)x2). Census dead 20 -> 17
  (-3, the Orcs refs). Remaining dead: Retreating Strike 14 (owner-deferred), Bloodthirsty
  Fighter 2, Reckless Piercing Aura 1 (base Reckless Piercing is grant-only, no direct refs).
- [x] 2026-08-22 S2 Vale Oath Boost DONE. Engine (submodule c90067c): Effect.ClearTokenOnRoll now
  emits a SINK op; TokenClearRollSink folds best (lowest) threshold per token type
  (WoundIgnoreSink discipline, clamp [2,6]); TokenClearRolls makes ONE decisive roll per type via
  the unchanged GameOperationServices.ClearTokenOnRoll (beat + banner intact); stage calls it
  after the executor; lint round-start arm widened. InvokeClearTokenOnRoll executable REMOVED
  (ops are transient, no save impact). 4 new engine tests (base+boost fold to 3, distinct-defs
  fold to 4 = the owner-ruled facet pin, clamp/per-type independence, single-die beat at folded
  threshold). Data: Vale Oath Boost (full band 3, gated tokenPresent Shaken + unitHasRule Vale
  Oath - Boost alone conjures no roll) + Aura. 5 app tests (ValeOathShippedDataTests). Census
  dead 22 -> 20 (-2, exact). Full loop green (329 defs / app 1298 / engine 2979 / GDF
  byte-identical / smoke exit 0).
- [x] 2026-08-22 S1 Grounded contexts DONE. Engine (submodule e74517a): IHasTerrain on
  MoveActionDeclaredContext + SaveRollCompleteContext (optional trailing TerrainPieces, the
  Hit* pattern); terrain threaded through every movement-budget query the AI uses
  (MovementRuleQueries + TacticalAnalysis optional param; TacticianPlanner reuses its #363
  snapshot; ActivationResolver/PlaceObjects/RangedAttack/LaneGeometry/MacroActionGenerator all
  pass table terrain; ChooseActionStage embark reach + DisembarkStage leash too);
  AssignWoundsStage passes the live layout; CombatMath valuation stays deliberately empty
  (commented); RuleFireLint gains terrain-populated variants at both hooks. 6 engine tests
  (GroundedSpeedProtectionRuleIntegrationTests: fires/empty/far x both hooks + validator
  accepts). Data: Grounded Speed (3 entries, per-action actionTypeIs gate - the Ethereal
  triple-count trap - +2/+4/+4), Grounded Protection (ignoreWoundOnRoll 5, Subject), Grounded
  Protection Aura. 8 app tests (GroundedAofShippedDataTests, incl. the no-cross-talk pin).
  Census dead 34 -> 22 (-12, exact: Speed 4 + Protection family 8). Full loop green
  (validate 327 defs / app 1283+8 / engine 2975 / GDF byte-identical / smoke exit 0).
- 2026-08-22: STARTED. Branches: superproject `376-aof-rule-engine-primitives` (off the
  unmerged #375 branch, which holds the AoF supplement this item completes), engine submodule
  `376-aof-rule-primitives` (off pinned b3c47af). Book texts for all 6 mechanics confirmed
  against the reference doc. Engine-seam recon in flight; design forks to be surfaced for
  sign-off before building (slice order proposal to follow).
- 2026-08-22 (#375 C5): Vale Oath Boost (+ Aura) moved here (double-roll composition, above).
- 2026-08-22 (#375 C4): Grounded Speed moved here (context capability gap, above); Ethereal
  and Great Sergeant fell out of the borderline list as data.
- 2026-08-22: Filed from the appraisal residue. Dice invariant applies throughout
  (histograms, never int-locked roll-derived values).

## Decisions

- 2026-08-22 owner sign-offs (AskUserQuestion, after engine-seam recon):
  - **Ravage Aura = data-only standalone def** (no engine change): ResolveRavageWoundsStage
    already groups InvokeDealAutoWounds by threshold and sums dice, so a Unit-scoped def
    contributing 1 die/living model at the same hook+threshold is arithmetically identical to
    every model having Ravage(X+1). Accepted caveats: UI shows it as its own rule; a unit with
    no base Ravage still rolls 1 die/model.
  - **Bloodthirsty Fighter = real bonus swing**: bonus attacks re-enter the shared
    hit->save->wound chain as a child (fresh CombatMetadata, no-chaining guard on metadata);
    weapon rules apply to bonus attacks; AI CombatMath mirrored + pin test.
  - **Grounded Speed = thread terrain to the AI**: optional trailing terrain param on the
    movement budget queries (MovementRuleQueries, TacticalAnalysis.ChargeBudget); callers
    holding table state pass it so planner and executor agree; unreachable spots default empty.
  - **Retreating Strike DEFERRED by owner** ("I need to look into the rule more") - not built
    in this pass; its refs stay dead until the owner rules on the trigger question (charger
    move-back fires no hook today; options recorded in the recon notes above). Re-raise before
    closing the item.
- 2026-08-22 doctrine decisions (in-repo precedent, surfaced not asked; flag to reopen):
  - **Vale Oath Boost = threshold fold**: clearTokenOnRoll becomes a sink op; the round-start
    stage folds best (lowest) threshold per token type and makes ONE decisive roll
    (WoundIgnoreSink + CastSpellStage doctrine). Boost authored at the full band (3), per the
    RerollSink min-threshold rule. Owner-ruled facet: two DISTINCT recovery rules on one unit
    (e.g. Steadfast + Battleborn - no shipped unit has both) now fold to one roll at the best
    threshold instead of rolling twice.
  - **Reckless Piercing = failure arm on grantTokenOnRoll** (MoraleTestThen.OnFailure
    precedent): one die, two outcomes; 2+ arm = round-end boon token, 1 arm = Subject-seat
    self-debuff token (Mobile Artillery shape, sign flipped). Rest is shipped vocabulary
    (single-ability YesNo offer at activation start, roundEnd token clears).

## Outcome
