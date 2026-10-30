# 433 — "Enhanced X" family (~60 names replacing the Boost family) - design fork

**Status**: done 2026-10-30 (Option B everywhere, signed off plan Q6; 112 definitions, 97 engine + 234 app tests; all-90 scratch import Enhanced 0 dead; owner review: D4, D5). Hand-offs in #436 #438 #439 #441 #442 #423.
**Related**: #415, #432 (the base rules Enhanced lifts), #197, #434/#435
**Branch**: `feature/opr-3.6`. Model/effort: Fable 5.1 / xhigh.

## Goal

3.6 replaced every `X Boost` / `X Boost Aura` / `X Boost Buff` with `Enhanced X` (+ Aura / Buff): about 60
names across both systems, each phrased "If this model has X, it gets <better parameter> from X (instead of
only <base parameter>)". Representative 3.6 texts:

- Enhanced Lustbound / Scurry / Agile: "moves +2" on Advance and +4" on Rush/Charge from Lustbound (instead of
  only +1" and +2")".
- Enhanced Changebound / Machine-Fog / Screened / Empyrean Spirit: "enemies attacking them always get -1 to hit
  rolls from Changebound (instead of only when shooting/charging from over 9"/6" away)".
- Enhanced Bloodborn / Clan Warrior / Predator / Primal / Royal Warrior: "extra attacks on successful unmodified
  hit results of 5-6 (instead of only on 6)".
- Enhanced Fortified / Guardian / Ossified / Warden: "hits they take count as having AP(-2) (instead of AP(-1))".
- Enhanced Plaguebound / Protected / Self-Repair / Cursed Undead: "ignore wounds on rolls of 5-6 (instead of 6+)".
- Enhanced Darkborn / Shadowborn / Shrouded / Wild Veil: "-8"/-4" range to a min. of 6" (instead of -4"/-2")".
- Enhanced Highborn / Extra Reach / Royal Legion: "+8"/+4" range (instead of +4"/+2")".
- Enhanced Nimble / Rapid Blink / Wave-Step / Wolfborn: "may move by up to 2D3" (instead of D3")".
- Enhanced Guerrilla / Harassing / Hit & Run: "may move by up to 6" (instead of 3")".
- Enhanced Steadfast / Vale Oath: "stops being Shaken on rolls of 3+ (instead of 4+)".
- Enhanced Mischievous / Scrapper / Vicious / Bestial: "re-roll successful unmodified Defense results of 5-6
  (instead of only 6)".
- Enhanced Knightborn / Warding / Angelic Blessing: "always ignore shooting/charging wounds on 6+ (instead of
  only when shot/charged from over 9"/6")".
- Enhanced Versatile Warrior / Watchborn / Vinci Tech: "always gets either AP(+1) or +1 to hit (instead of only
  over 9"/6")".
- Enhanced X Aura: "This model and its unit get Enhanced X"; Enhanced X Buff: once-per-activation grant to a
  friendly unit within 12" (next time the effect would apply).

Every Enhanced rule is inert unless the bearer (or most models, for unit-gated bases) also has X.

## Signed-off design (2026-10-30, Chris)

- Chris accepted the mixed A/B recommendation and asked whether Reliable sets a "replace the value, then apply
  modifiers" precedent that would avoid A. It does: Reliable is `Effect.QualityFloor(2)` at the hit-roll modifier hook,
  "before other modifiers" (Reliable + a -1 = 3+). The same keep-best-then-modifiers shape is already what the threshold
  sinks do (`TokenClearRollSink` keeps the best clear threshold; `ignoreWoundOnRoll` folds as best; range ops sum with
  the largest floor). So every threshold family is a B entry with the better value, folded as best, and ordinary
  modifiers still apply after the fold. Pin that ordering for one family (an Enhanced Steadfast unit with a -1 to its
  roll still needs 4+).
- The "always ... (instead of only when over 9"/6")" families use B's COMPLEMENTARY ARM: the same effect gated on
  `Not(attackedFromOver {9, 6})` / `Not(attackingFromOver ...)` beside the base's own gate. The base definition is never
  edited (A's `Not(HasRule "Enhanced X")` on the base is what made A brittle), and the two gates are disjoint, so the
  pair cannot stack by construction.
- The "5-6 instead of 6" extra-attack families are a second entry on the natural 5, gated on a SUCCESSFUL hit
  (#418 D2: read `IHasUnmodifiedHitRolls.SpecialRuleHitRolls`, never the raw roll); a Fatigued Enhanced unit gets
  nothing from 5s or 6s (pin it). The Bloodborn family's one-extra-hit approximation of "+1 attack" (#434 D7) is
  inherited; building a bonus-attack primitive is a separate OWNER REVIEW decision, not part of this item.
- The Defense re-roll 5-6 family (Mischievous / Scrapper / Vicious / Bestial) needs the re-roll op to take a face set
  or a threshold; a second entry "re-roll successful unmodified 5" is the B shape.
- Movement / AP / range / dice-count / move-distance families: a second delta entry exactly as the #432 and #434 notes
  describe (ranges and movement sum, reposition D3 entries sum into one move, `reduceArmorPenetration 1` twice).
- Every Enhanced rule stays inert without X: condition `HasRule(X)` (unit-gated bases: most models) on every entry.
- Re-establish the no-stack guard that #432 deleted with the Boost family (`BoostRuleCompositionTests`): for every
  family shape, base + Enhanced applies exactly the Enhanced value once.
- Aura and Buff variants follow the supplement's existing `aura` / `addRule ... NextTrigger` grant shapes; the LAT-1
  arity gate in `RuleEvaluator.CollectGrantedRules` must be checked for any Enhanced name that carries an argument.
- Acceptance unchanged: the family's 91 names / 299 refs resolve (scratch 90-book `--rule-coverage` 0 for the family),
  one integration test per family shape plus the no-stack test, Aura and Buff grant.

## The fork (written up 2026-10-29; decided above)

- **Option A - per-rule redefinition.** Each `Enhanced X` is authored as a full definition that carries the
  upgraded effect and a condition `HasRule(X)`, while X's own definition gets `Not(HasRule("Enhanced X"))`
  so they never stack. ~60 definitions, all data, no engine change; brittle (every base-rule edit must be
  mirrored) but transparent in `--trace-rules`.
- **Option B - additive deltas.** `Enhanced X` is authored as the DELTA on top of X: +1"/+2" more movement,
  an extra trigger on 5, an extra AP(-1), an extra D3", the complementary range condition (within 9"/6") for
  the "always" variants, a 3+ in place of 4+ via a roll-modifier token. Base rules untouched; ~60 small
  definitions; needs two or three new primitives (a roll-threshold modifier for token-clear rolls, a
  second-trigger-value for extra-attack rules); `--trace-rules` shows two firings per event.
- **Option C - parameterised rule arguments.** Give the base rules a hidden "tier" argument the Enhanced
  marker raises (engine reads `Arg(tier)` in each effect). Fewest definitions, most engine work, and the
  tier semantics differ per rule family anyway.

Recommendation to present: B for parameter-bump families (movement, AP, ranges, dice, move distance), A for
the "always instead of range-gated" and "5-6 instead of 6" families where a delta is awkward, C rejected.
Stop here until Chris signs off.

## Acceptance (after sign-off)

Every Enhanced name resolves (`--rule-coverage` 0 for the family), one integration test per family shape
(not per name) plus a no-stack test (base + Enhanced never double-applies), Aura and Buff variants grant.

## Code anchors (survey 2026-10-29)

- The 3.5 `X Boost` definitions in `GdfRuleSupplement.json` / `AofRuleSupplement.json` (the shapes being
  replaced), `Effect.AddRule` / `Effect.Aura` grant plumbing, `RuleEvaluator.CollectGrantedRules` (arity gate
  LAT-1). See the appended survey rows below.

### Survey rows (code survey 2026-10-29)

- The 3.5 `X Boost` / `X Boost Aura` / `X Boost Buff` definitions in both supplements are the shapes being
  replaced. Grant plumbing: `Effect.AddRule` / `Effect.Aura` / `Effect.MarkTarget`, `TokenPayload.RuleGrant`,
  `RuleEvaluator.CollectGrantedRules` (~:436-439 skip path; LAT-1 arity gate screens argumented grants - an
  Enhanced rule that must carry an argument needs that gate revisited).

## Notes

- 2026-10-30: **done.** Six engine commits (18b2ad03 primitives, 467ee0db B1 tests, 6bda32ac B2/B3 tests, 8c976fa2 C
  tests, 5ba41f40 D tests, 8a71a697 E tests) and six superproject commits (c053f92, ffdf10d, 70ace28, 12fd18f, 45dec8f,
  23cac1e) plus this ledger commit. Gates: engine 4235 -> 4332 passed (1 skipped, unchanged), app 3389 -> 3623, build 0
  errors, `--validate-rules` 335 -> 447 definitions OK, shipped `--rule-coverage` Dead 36 (Counter-Attack, until #438),
  all-90 scratch import Dead 563 (Enhanced 299 / 91 names + Parry 264) -> 264 (Parry alone; Enhanced 0). Smokes exit 0:
  default `printf "2\n2\n"` army; Daemons of Lust with hand-added Enhanced Lustbound (+ Aura; trace: "Ascended
  Harbinger of Lust's Enhanced Lustbound Aura ... fired -> GrantTokenToUnit"); Daemons of Change with Enhanced
  Changebound (+ Aura; trace: "Avatar of Change's Enhanced Changebound at Shooting_OnHitRollModifier/Subject: fired ->
  ApplyRollModifier" / "added -1 to Hit rolls"); Vampiric Undead (AoF) with Enhanced Cursed Undead (+ Buff; trace:
  "Gremyir & Milyazha's Enhanced Cursed Undead at Shooting_OnSaveRollComplete/Subject: fired -> IgnoreWound" / "ignored
  wounds on a 5+"). Negative controls (each mutation made its pin fail, then restored): (1) Enhanced Bloodborn's 5 face
  on `unmodifiedRollEquals` instead of `unmodifiedRollHits` -> `EnhancedBloodborn_AFiveThatMissed_ScoresNothing` and the
  Fatigue pin fail; (2) Enhanced Darkborn authored as the total {-8, 6, -4} -> the 24"->16" pin fails (reads 12"); (3)
  Enhanced Changebound without `not(attackedFromOver)` -> the "always, once" pin fails at 10" and on a 7" charge (-2).
  Scratch: `scratchpad/433/` (generator `gen_enhanced.py`, `negative_controls.sh`, smoke logs, compiled armies).
- 2026-10-30: **surprising findings.** (a) `--trace-rules` prints no `Movement_On*` hook lines at all - the movement
  budgets are read through the non-logging named queries, so Lustbound / Enhanced Lustbound firing is invisible in a
  trace even when the unit moves (pinned engine-side instead; a trace gap, not an Enhanced bug - candidate small item).
  (b) `FixedDiceRoller` cannot represent a miss (its `TotalRolls` is always 1, so `SubsetAtOrAbove(6)` of a fixed 5
  still counts one hit); the miss pins use `FixedFaceDiceRoller`. (c) `RollToHitStage` hard-codes `IsSpell: false` on
  its hit context (spells never roll to hit), so a spell-exclusion pin on the hit side must go through
  `SyntheticHitResolution.Resolve(..., isSpell: true)`, the real spell path. (d) Every 3.6.0 book carries its Enhanced
  names as UPGRADE options or spell grants - no unit has one as a base rule - so `--book-to-army` base-size armies never
  field one; the smokes hand-added them to the compiled `.fdgarmy` (whose `ruleDefinitions` already embed the Enhanced
  definitions from the book). (e) The item's "ordering pin" for Enhanced Steadfast (a -1 to the recovery roll still
  needs 4+) has no modifier to apply: `GameOperationServices.ClearTokenOnRoll` compares the raw face to the folded
  threshold and nothing in the engine modifies that die (D7).
- 2026-10-30: **inventory** (112 definitions: 64 `Enhanced X`, 42 `Enhanced X Aura`, 6 `Enhanced X Buff`; the 91 dead
  names / 299 refs of the worklist plus the 21 names the 3.6.0 snapshots define without a unit reference - D8). Every
  entry is gated on `unitHasRule X` (plus `mostModelsHaveThisRule` where the base has it); X is never edited. "GDF"
  names live in `GdfRuleSupplement.json` (an AoF book reads it too), "AoF" in `AofRuleSupplement.json`.

  | Shape (recipe) | GDF | AoF |
  |---|---|---|
  | B1 movement: 3 x `movementBonus` +1/+2/+2 (the sink sums -> +2/+4) | Lustbound, Scurry, Agile (catalog base) | - |
  | B1 range shrink: `rangeModifier {-4, 6, -2}` Subject (ops sum, floor = largest) | Darkborn, Shadowborn, Shrouded | Wild Veil |
  | B1 range grow: `rangeModifier {4, 0, 2}` | Highborn, Extra Reach | Royal Legion |
  | B2 AP(-2): `reduceArmorPenetration 1` Subject (the save stage sums) | Guardian, Fortified (catalog base) | Ossified, Warden |
  | B2 ignore 5-6: `ignoreWoundOnRoll 5` Subject (fold keeps best) | Plaguebound, Self-Repair, Protected (catalog base) | Cursed Undead |
  | B2 Shaken 3+: `clearTokenOnRoll Shaken 3` (fold keeps best, one roll) | Steadfast | Vale Oath |
  | B2 re-roll 5-6: `reroll Save onUnmodifiedValue 5` Actor, Unit scope (sink keeps widest) | Mischievous, Scrapper, Vicious | Bestial |
  | B2 post-combat 6": `triggeredMove 6` x2 (PostCombatMoveGate keeps the max) | Harassing, Guerrilla, Hit & Run (catalog bases) | - |
  | B3 2D3: `repositionAtActivation d3 move` (ActivationStartStage sums into one move) | Wolfborn, Nimble, Rapid Blink | Wave-Step |
  | C -1 to hit always: `not(attackedFromOver 9/6)` Subject | Changebound, Machine-Fog, Screened | Empyrean Spirit |
  | C +1 Defense always: `not(attackedFromOver)` Subject | Guarded, Sturdy | - |
  | C ignore 6+ always: `attackedFromOver {0,0} and not(attackedFromOver 9/6)` Subject | Knightborn, Warding | Angelic Blessing |
  | C +1 Morale always: `not(attackedFromOver)` at Morale_OnPreMoraleTest | Hive Bond, Hold the Line, Valiant | - |
  | C extra hit on 6 always: `not(attackingFromOver) and isNotSpell and unmodifiedRollEquals 6` | Devout, Ferocious, Brutal | Lucky |
  | C AP(+1) always: `not(attackingFromOver) and isNotSpell` | Havocbound, Piercing Warrior | - |
  | C extra wound on 1 always: `not(attackingFromOver) and isNotSpell`, Unit scope | Infected, Warbound, Violent | Destroyer |
  | C pick always: two entries on `unitHasRule "X (Piercing)" / "X (Precision)"` + `not(attackingFromOver)` | Versatile Warrior, Watchborn | Vinci Tech |
  | C +1 to hit always: `not(and(not(isMelee), distanceGreaterThan 9))` | Targeting Visor, Precision Marksman | Buccaneer |
  | D extra hit on successful 5: `unmodifiedRollHits 5` -> `addExtraHit 5` | Bloodborn, Clan Warrior, Primal, Predator | Royal Warrior |
  | E Aura: `aura "Enhanced X"` at Lifecycle_OnUnitCreated | 29 (Bloodborn, Brutal, Changebound, Clan Warrior, Darkborn, Devout, Ferocious, Guardian, Guerrilla, Harassing, Havocbound, Highborn, Hive Bond, Hold the Line, Infected, Knightborn, Lustbound, Machine-Fog, Mischievous, Plaguebound, Rapid Blink, Scrapper, Scurry, Shadowborn, Sturdy, Targeting Visor, Warbound, Watchborn, Wolfborn) | 13 (Bestial, Buccaneer, Destroyer, Empyrean Spirit, Lucky, Ossified, Royal Legion, Royal Warrior, Vale Oath, Vinci Tech, Warden, Wave-Step, Wild Veil) |
  | E Buff: before-attack `addRule "Enhanced X" NextTrigger`, Friend 12" | Hold the Line, Primal, Self-Repair, Shadowborn | Angelic Blessing, Cursed Undead |

  Primitives (engine commit 18b2ad03): `Condition.UnmodifiedRollHits(dieValue)` (a natural face that also HIT) reading the
  new `IHasHitRollNeeded` capability - `HitRollCompleteContext.HitRollNeeded`, threaded from `RollToHitStage`, the AI's
  `CombatMath` and the shooting forecast (0 = every rolled die is a hit, the synthetic-hit pipeline's case); and
  `Condition.UnitHasRule` broadened to grants, living models and their weapons (D2). No other engine change; everything
  else is data.
- 2026-10-30: **tests.** Engine: `Enhanced36ConditionTests` (15: the two primitives), `Enhanced36Definitions` (one shape
  per family, the engine-side twin of the JSON), `EnhancedRuleIntegrationTests` (14: sums through the movement budgets and
  the range / charge queries; `EveryEnhancedShape_LintsClean`), `EnhancedCombatRuleIntegrationTests` (67: the folds, the
  complements, the 5-6 family incl. the Fatigue pin, the wrappers, the arity gate - through the real stages),
  `CombatMathPinTests.EnhancedBloodborn_SuccessfulFives_MatchesEngine`. App: `Enhanced36ShippedDataTests` (every one of
  the 112 definitions entry-for-entry with its 3.6.0 text, the gate names resolve in the right universe, the wrappers'
  grant targets read no argument, the inventory is exactly 112, five shipped pairs through the evaluator);
  `RuleSupplementLintTests.EverySupplementRuleFires` covers all 112.
- 2026-10-30: Q6 signed off (Chris): Option B everywhere, see "Signed-off design". Queued after #424.
- 2026-10-30 (from #442): AI / calculator notes for whichever option is chosen (Q6); nothing was built in #442.
  (1) `CombatMath` prices every rule through the evaluator, so an Enhanced variant authored as DATA (its own
  definition, or a parameterised base rule) prices for the AI and the calculator with no AI change; a per-name engine
  mechanism would need a mirror. (2) The Tactician's `TacticianAbilityEffectResolver` prices any "pick one effect"
  helper as a hypothetical attacker rule (`CombatMath`'s `hypotheticalAttackerRule`), so an Enhanced pick shaped like
  Versatile's prices for free. (3) Spells that GRANT an Enhanced rule (Righteous Fury -> Enhanced Wolfborn, Litanies of
  War -> Enhanced Devout) land as `RuleGrant` tokens, which the Tactician's bare evaluator cannot read (no rule
  resolver - a recorded #191 gap); `Ai/Tactician/TacticianRuleLookup` (#442) is the resolver that would close it.
- 2026-10-30 (from #435): **the AoF view of your family.** The 41 AoF 3.6.0 imports leave exactly 58 Enhanced names /
  137 refs dead (all 90 entries: 91 / 299), nothing else but Assault / Parry. AoF base rules whose shapes are now in place
  for an Enhanced upgrade: Ossified / Warden (Guardian's unconditional AP(-1); Enhanced "AP(-2) from Ossified/Warden"),
  Wave-Step (a move, `repositionAtActivation.move: true`; Enhanced 2D3 - #434 D8: a second D3 entry sums into the one
  move), Vale Oath / Unmovable (Steadfast's `clearTokenOnRoll` 4+; Enhanced 3+ - #376 S2's `TokenClearRollSink` keeps the
  best threshold, so a 3+ entry folds into one roll), Cursed Undead (Protected's 6+; Enhanced 5-6 - `ignoreWoundOnRoll`
  folds as best), Bestial (Mischievous; Enhanced re-roll 5-6), Royal Warrior (Predator's one-extra-hit approximation of
  "+1 attack", #434 D7 / #435 D10; Enhanced on 5-6), Buccaneer (Precision Marksman's 9" gate; Enhanced "always"), and the
  #432 AoF rules (Lucky, Destroyer, Vinci Tech, Empyrean Spirit, Angelic Blessing, Wild Veil, Royal Legion). Book quirk:
  Enhanced Vinci Tech's (and Enhanced Versatile Warrior's) 3.6 text says "If this model has Versatile Attack ... from
  Versatile Attack" - read it as Vinci Tech / Versatile Warrior. 38 AoF spells (29 names) grant an Enhanced name once (e.g. Vampiric
  Undead Vigorous Return, Chivalrous Kingdoms Great Hunt; the smokes log "Skipping unimplemented special rule 'Enhanced
  Vale Oath' on spell 'Great Hunt'"). The AoF names: Primal Buff, Changebound (+ Aura), Cursed Undead (+ Buff),
  Havocbound (+ Aura), Lustbound (+ Aura), Plaguebound (+ Aura), Shadowborn (+ Aura), Warbound (+ Aura), Bestial (+ Aura),
  Ferocious (+ Aura), Angelic Blessing (+ Buff), Empyrean Spirit (+ Aura), Harassing (+ Aura), Ossified (+ Aura), Sturdy
  (+ Aura), Wild Veil (+ Aura), Buccaneer (+ Aura), Destroyer (+ Aura), Highborn (+ Aura), Lucky (+ Aura), Royal Warrior
  (+ Aura), Vale Oath (+ Aura), Warden (+ Aura), Wave-Step (+ Aura), Hold the Line (+ Buff), Mischievous (+ Aura), Primal,
  Royal Legion (+ Aura), Scurry (+ Aura), Vinci Tech (+ Aura).
- 2026-10-30 (from #434): **what is still dead is exactly your family.** A scratch import of the 49 GDF 3.6.0 books
  against the finished `GdfRuleSupplement.json` leaves 60 Enhanced names / 162 refs (all 90 entries: 91 names / 299 refs)
  and nothing else of #434's: Enhanced Bloodborn Aura 6, Darkborn Aura 6, Knightborn Aura 6, Primal Buff 6, Wolfborn Aura
  6, Changebound 5, Changebound Aura 5, Lustbound 5, Lustbound Aura 5, Plaguebound 5, Plaguebound Aura 5, Self-Repair
  Buff 5, Warbound 5, Warbound Aura 5, Devout Aura 4, Watchborn Aura 4, Ferocious / Guardian / Guerrilla / Havocbound /
  Hold the Line / Infected / Scurry / Sturdy Aura 3 each, Shadowborn 3, Shadowborn Buff 3, Bloodborn / Clan Warrior Aura /
  Darkborn / Devout / Guardian / Harassing Aura / Knightborn / Machine-Fog Aura / Mischievous Aura / Rapid Blink Aura /
  Scrapper Aura / Shadowborn Aura / Targeting Visor Aura / Watchborn / Wolfborn 2 each, and 1 each Clan Warrior,
  Ferocious, Harassing, Havocbound, Highborn (+ Aura), Hive Bond (+ Aura), Hold the Line, Infected, Machine-Fog,
  Mischievous, Primal, Rapid Blink, Scrapper, Scurry, Self-Repair, Sturdy, Targeting Visor. Base-rule shapes you build on
  that #434 changed: (1) Bounding / Dash / Nimble / Rapid Blink / Wolfborn are a real MOVE now -
  `repositionAtActivation {distance: d3, plusInches, move: true}`; the fold SUMS entries into one move, so "2D3 instead of
  D3" is a second `{d3, 0, move: true}` entry (an old placement-shaped copy summed with it widens the same move).
  (2) Guardian / Primeborn are unconditional (Fortified's `reduceArmorPenetration 1` gated on most models): "AP(-2)
  instead of AP(-1)" is a second `reduceArmorPenetration 1`. (3) Targeting Visor / Precision Marksman: `not(isMelee) and
  distanceGreaterThan 9` +1 to hit; Piercing Marksman adds `isNotSpell`. (4) Plaguebound / Self-Repair / Protected:
  `ignoreWoundOnRoll 6` on most models. (5) The Bloodborn family (Bloodborn, Clan Warrior, Primal, Predator, Predator
  Shooter) is still one extra HIT on a 6, not "+1 attack" (#434 D7) - your 5-6 variants inherit that approximation unless
  you build the bonus-attack primitive. (6) The "X Buff" grants (`addRule ... NextTrigger`) and "X Aura" grants
  (`aura`) follow the supplement's existing shapes.
- 2026-10-29 (from #432): the base rules Enhanced X lifts are now authored on two conditions and one effect - name
  them in whichever option you take. Conditions: `attackingFromOver {shootInches: 9, chargeInches: 6}` (Actor seat:
  Devout, Ferocious, Brutal, Lucky, Havocbound, Piercing Warrior, Infected, Violent, Warbound, Destroyer, and the
  "(Piercing)" / "(Precision)" helpers of Versatile Warrior / Watchborn / Vinci Tech) and `attackedFromOver
  {shootInches: 9, chargeInches: 6}` (Subject seat: Changebound, Machine-Fog, Screened, Empyrean Spirit at the
  hit-modifier hook, Guarded / Sturdy at hit-complete, Knightborn / Warding / Angelic Blessing's 6+ arm at
  save-complete; and the tested unit at `Morale_OnPreMoraleTest` for Hive Bond, Hold the Line, Valiant). Every
  "always ... (instead of only when shooting/charging from over 9"/6")" Enhanced rule removes exactly that conjunct
  (Option A: the same entry with the gate dropped plus a `Not(UnitHasRule)` on the base; Option B: the complementary
  arm, `Not(attackedFromOver ...)` beside the base's own gate). Effect: `rangeModifier {delta, chargeDelta,
  minResultInches}` at `Shooting_OnRangeCheck` - Darkborn / Shadowborn / Shrouded / Wild Veil `{-4, -2, 6}` (Subject),
  Highborn / Extra Reach / Royal Legion `{4, 2, 0}` (Actor). Range ops SUM, and the floor is the largest
  `minResultInches` among reductions, so Option B's "-8"/-4" (instead of -4"/-2")" is a second `{-4, -2, 6}` and
  "+8"/+4"" a second `{4, 2, 0}`; Lustbound / Scurry / Agile are three `movementBonus` entries (+1/+2/+2), whose sink
  also sums. The 3.5 Boost definitions are DELETED (77 names, #432 D9) - read the old shapes from the parent of #432's
  superproject commit if useful - and `BoostRuleCompositionTests`, the guard that caught "authored as the boosted total,
  then ADDED to the base" (Devout + Devout Boost gave two extra hits on one 6), went with them: re-establish that guard
  for the Enhanced family (the item's "no-stack test"). The three Feat helpers ("Speed Feat Boost" etc.) are not Boosts
  and stay.
- 2026-10-29 (from #418, its D2): the 3.5 Boost rules' 5-face entries (`UnmodifiedRollEquals(5)` +
  `AddExtraHit(5)` - Primal / Clan Warrior / Devout / Ferocious / Lucky / Royal Warrior Boost, Great
  Sergeant) add an extra hit for ANY natural 5, including a miss (a Fatigued unit's 5s always miss; so do a
  Quality 6+ unit's). #418 stripped only face 6, per the Fatigue text. The 3.6 Enhanced texts say "extra
  attacks on successful unmodified hit results of 5-6", so whichever option this item takes must gate the 5s
  on SUCCESSFUL hits, and should pin a Fatigued Enhanced unit getting nothing from its 5s or 6s. New
  hit-face primitives must read `IHasUnmodifiedHitRolls.SpecialRuleHitRolls`, not the raw roll.
- 2026-10-29: filed with the plan.

## Decisions

- D1 (signed off by Chris, 2026-10-30): Option B for every family, complementary arm for the "always" families;
  Option A dropped (base rules never edited); Option C rejected. Reliable's `QualityFloor` is the ordering precedent.
- **D2 - `UnitHasRule` reads every carrier (2026-10-30) - OWNER NOTE.** "If this model has X" is `unitHasRule X`, and
  before this item that condition read only the unit's own list, which would have left Enhanced X inert in three book
  shapes: X arriving by aura as a RuleGrant token (Lustbound Aura on a hero), X re-homed onto the weapons (Infected,
  Mischievous - a Weapon-scope rule named at unit level is attached to every weapon and NOT kept on the unit list,
  `GameBootstrap.AttachRulesFromArmyList`), and X relocated onto a joined hero's model. It now reads the unit list, grant
  tokens, living models and their weapons (`Condition.UnitHasRule.Holds`). The only pre-existing users are the catalog's
  3.5 `Harassing Boost` / `Guerrilla Boost`, which now also see an aura-granted base - strictly more correct. Pinned by
  `Enhanced36ConditionTests` and `EnhancedLustbound_ReadsALustboundThatArrivedByAura`.
- **D3 - the 5-6 family's success gate (2026-10-30).** "Extra attacks on SUCCESSFUL unmodified hit results of 5-6": the
  hit-complete context did not know what a hit needed, so it gained `HitRollNeeded` (`IHasHitRollNeeded`), set by
  `RollToHitStage` to the clamped threshold it split the successes with, mirrored in `CombatMath` and the forecast, 0 on
  the synthetic-hit pipeline (every rolled die is a hit there by construction). `Condition.UnmodifiedRollHits(5)` is true
  when a 5 was rolled (as special rules see it - #418's view) and 5 >= needed; a 6 always hits so the base rules keep
  `unmodifiedRollEquals 6`. A Fatigued melee swing needs 6s (the 5s miss) and its 6s do not count for special rules, so
  such a unit gets nothing from either face - pinned through the real DetermineHitRollStage. The base family's
  one-extra-HIT approximation of "+1 attack" (#434 D7) is inherited unchanged; a bonus-attack primitive stays an owner
  decision outside this item.
- **D4 - what "always" covers, per seat (2026-10-30) - OWNER REVIEW.** The complementary arm is the base's effect under
  `not(<the base's range gate>)`, so base + Enhanced are disjoint by construction. Actor-seat complements (Devout /
  Havocbound / Infected families, the Versatile pick's AP half) also carry `isNotSpell`: their base texts describe weapon
  attacks ("its weapons get AP(+1)", "hits from weapons", "results of 6 to hit") and the base's `attackingFromOver` was
  already false for a caster's spell hits (no shooting distance), so a complement without the exclusion would have given
  spells AP(+1), an extra hit per rolled 6 and an extra wound per 1 to block - pinned through `SyntheticHitResolution`.
  Subject-seat complements are literal: Enhanced Guarded / Sturdy's "+1 to Defense rolls always" applies to every save,
  spell wounds included (alternative: add `isNotSpell`, one conjunct); Enhanced Changebound's -1 and the morale +1 apply
  to every attack / every Morale Test, strike-backs and rule-driven tests included. Enhanced Knightborn / Warding /
  Angelic Blessing keep "shooting/charging wounds": `attackedFromOver {0, 0}` ("shot or charged at all") beside
  `not(attackedFromOver {9, 6})`, so a strike-back's wound and a spell's are left to the base (the spell arm's 2+).
- **D5 - Targeting Visor / Precision Marksman / Buccaneer: "always" means melee too (2026-10-30) - OWNER REVIEW.**
  "It always gets +1 to hit rolls from Targeting Visor (instead of only when shooting over 9" away)" - OPR's template
  removes the base's whole condition, as it does for Havocbound (whose "always" covers strike-backs the base never did),
  so the complement is `not(and(not(isMelee), distanceGreaterThan 9))`: +1 to hit in melee and at any shooting range.
  The 3.5 `Targeting Visor Boost` read it as shooting-only ("+1 to hit when shooting, not only over 9""). Alternative: one
  condition change to `and(not(isMelee), not(distanceGreaterThan 9))` (shooting within 9" only); the melee case is pinned
  (`EnhancedTargetingVisor_WithTargetingVisor_PlusOneToHit_Always(0.5, true)`) so the flip is one assertion.
- **D6 - the post-combat 6" family is a keep-best, not a sum (2026-10-30).** `PostCombatMoveGate` coalesces the family's
  `InvokeTriggeredMove` ops into ONE move at the largest budget, so Enhanced Harassing / Guerrilla / Hit & Run are a 6"
  entry beside the base's 3" (a 3" "delta" would give max(3, 3) = 3) - the catalog Boost's shape, the same keep-best
  class as the threshold families. The 3.6 text says "If most models in this unit have Harassing" (the base Harassing has
  no ownership gate), so the Enhanced entries carry `mostModelsHaveThisRule` too.
- **D7 - the Steadfast "ordering pin" is moot (2026-10-30).** The design asked to pin that an Enhanced Steadfast unit
  with a -1 to its recovery roll still needs 4+ (keep-best first, modifiers after). Nothing in the engine modifies the
  Shaken recovery die: `GameOperationServices.ClearTokenOnRoll` rolls a decisive face and compares it to the folded
  threshold, and no rule emits a modifier for it. Pinned instead: base + Enhanced fold to ONE roll at 3+ (never a second
  chance) and a rolled 3 recovers. The Reliable `QualityFloor` precedent stands where modifiers exist (hit rolls).
- **D8 - 112 names, not 91 (2026-10-30).** The worklist is the 91 names units reference (299 refs). The 3.6.0 snapshots
  define 21 more under the same templates with no unit reference today (Enhanced Agile, Brutal (+ Aura), Extra Reach,
  Fortified, Guarded, Hit & Run, Nimble, Piercing Warrior, Precision Marksman, Predator, Protected, Screened, Shrouded,
  Steadfast, Valiant, Versatile Warrior, Vicious, Violent, Warding) - the same shapes, one JSON entry each, and a future
  book or spell may name them - so all 112 are authored and the inventory is pinned at exactly 112.
- **D9 - texts verbatim, OPR typos included (2026-10-30).** As #432 D12 / #434 D1: "This model and its unit Enhanced
  Brutal." / "... Enhanced Ferocious." (no "get"), "they stops being Shaken", and the Versatile Warrior / Watchborn /
  Vinci Tech texts' "If this model has Versatile Attack ... from Versatile Attack" (OPR names the mechanic). Handed to
  #436's parity pass; the shipped-data test pins them, so a normalisation re-pins there.
- **D10 - the weapon-rule bases' Enhanced copies are Unit scope (2026-10-30) - OWNER NOTE.** Mischievous / Scrapper /
  Vicious / Bestial and Infected / Warbound / Violent / Destroyer are Weapon-scope rules ("on its weapons", re-homed onto
  every weapon). Their Enhanced copies are Unit scope with `unitHasRule X` (reading the weapons, D2) because the Aura
  and Buff wrappers grant at unit level and a granted rule is walked with no carrying weapon, so a `weaponHasRule` gate
  could never see a grant. Consequence: a unit with Mischievous on SOME weapons would get the 5-6 band on every weapon's
  hits; no 3.6.0 book builds that (the rules are army-wide "on its weapons" grants).
- **D11 - the catalog Boosts stay for #436 (2026-10-30).** `Harassing Boost` / `Guerrilla Boost` in `CoreRuleCatalog`
  are 3.5 definitions outside this item's data; deleting catalog definitions is #436's job (noted there).

## Outcome

Done 2026-10-30. The OPR 3.6 "Enhanced X" family (112 definitions: 64 Enhanced X, 42 Auras, 6 Buffs) is authored in
the two supplements as Option B deltas on untouched base rules: summed deltas (movement, range), keep-best entries
(AP, wound ignore, Shaken recovery, save re-roll, post-combat move), a second D3 move, complementary "always" arms on
the 9"/6" family, a success-gated 5 face for the extra-attack family, and the Aura / Buff grant wrappers. Two engine
primitives (`UnmodifiedRollHits` + `HitRollNeeded` on the hit context; `UnitHasRule` reading grants, models and
weapons), no base rule edited. Acceptance met: all-90 scratch import Enhanced 0 dead (Dead 264 = Parry, #423), shipped
Dead 36 unchanged (#438), one integration test per family shape with the no-stack and inert pins, the Fatigue pin,
Aura and Buff grant tests, four smokes exit 0 with Enhanced rules firing in the trace. Owner review: D4 (what "always"
covers per seat; Guarded / Sturdy vs spells), D5 (Targeting Visor family "always" includes melee). GUI hand-verify list:
a Daemons of Lust list with a Lust Favour (Enhanced Lustbound) hero shows +2"/+4" move rings; an Enhanced Changebound
unit shows -1 to hit on the forecast at 8"; an Enhanced Bloodborn unit's save beat shows "+1 (Enhanced Bloodborn)" on a
successful 5.

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
