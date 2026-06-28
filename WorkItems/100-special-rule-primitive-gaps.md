# 100 — Special-rule engine primitives: fill the remaining gaps

**Status**: in-progress
**Related**: #042 (rule architecture / `CoreRuleCatalog`), #026 (army-list resolution), #029/#030/#031/#032 (per-rule umbrellas), #033/#034 (casting + spells — being built in parallel on `033-caster`), #051 (Furious charge gate), #093 (per-model rules), #095 (rule rehydration on resume)
**Branch**: `100-special-rule-primitives` (both repos)

## Goal
Get the engine to the point where **every** army-book special rule and spell can be authored as data against a live primitive — i.e. drive Tier A ("works today") toward 100% by building out the missing primitives. "Done" is: the catalog of primitives below is implemented end-to-end (effect/condition/hook present **and** consumed by a live stage, with an integration test mirroring the nearest existing `*RuleIntegrationTests`), so the only remaining per-rule work is data entry, not engine work. This item is the umbrella; it fragments into per-primitive slices when picked up (and feeds the existing #029–#034 umbrellas).

## How this was scoped
Audited the first five army books (Alien Hives, Battle Brothers, Blessed Sisters, Blood Brothers, Blood Prime Brothers) rule-by-rule against what `Rules/` + `StateMachine/` actually wire today, then generalized across the full 47-army corpus. Source of the corpus is the **off-repo** extract at `../GDF Armies/` (copyrighted OPR material — do **not** commit it or paste its text in here; see that folder's `CLAUDE.md`). Rule names and "Army's SpellName" references are fine; full descriptions are not.

Sample result (Special Rules sections only, 91 rules across the 5 armies):
- **~32% Tier A** — authorable today on live primitives.
- **~43% Tier B** — a *seamed* extension is unfinished (a stub body, an unfired hook, an unconsumed effect, the grant-loop, or cross-unit targeting).
- **~25% Tier C** — needs a genuinely new primitive/subsystem.
- ~75% (A+B) is reachable without inventing new architecture — the bottleneck is finishing seams, then casting.

The percentages are a sample with a judgment B/C boundary; the **primitive list** below is the durable artifact.

## Live baseline (what "Tier A" rests on — do not rebuild)
The 29 rules in `Rules/Dispatch/CoreRuleCatalog.cs` are proven end-to-end. The live primitive vocabulary they exercise: roll modifiers (±to-hit / ±save = AP), extra-hit-on-N, hit/wound multipliers (Blast/Deadly), ignore-wound (Regeneration), suppress-rule (Unstoppable), quality-floor (Reliable), movement bonuses (Fast/Slow), Tough/max-wounds, Impact, strike-first (Counter), target-single-model (Takedown), defer-deployment (Scout/Ambush), reactivate-self (Martial Prowess), strafe deal-hits, triggered-move (Vanguard), ignore cover/LoS. Live conditions: `Always, And, Not, TokenPresent, ActionTypeIs, UnmodifiedRollEquals, DistanceGreaterThan, AfterMoving, IsMelee, IsCharging`. Live hooks: the hit/save/wound sinks, `Movement_OnMoveActionDeclared`/`OnChargeDeclared`/`OnMoveThroughEnemy`, `Melee_OnChargeContact`/`OnCounterTrigger`/`OnMeleeResolution`, `Morale_OnPreMoraleTest`/`OnMoraleTestComplete`, `Lifecycle_OnUnitCreated`, `Deployment_OnPreDeploymentSelect`/`OnUnitDeployed`, `Activation_OnNextActivatorRequested`/`OnActionChoice`/`OnEndOfActivation`.

Rules already expressible on the above but not yet in the catalog (pure data, no engine work): **Evasive, Melee Evasion, Lacerate, Crack, Destructive, Precise, Good Shot, Counter-Attack, Piercing Assault, Piercing Hunter, Point-Blank Piercing/Surge, Guarded, Fortified, Reinforced, Rapid Rush/Advance/Charge, Agile, Quick, Predator/Predator Fighter/Bloodborn/Clan Warrior/Primal, Resistance (base facet)** and the rest of the self-modifier family.

---

## The primitive catalog (the remaining gaps)

### Part 1 — Finish the seam (architecture exists, body/wiring missing)

1. **Granted-rule read-back.** ✅ **DONE — slice 1 (engine `4fb6159`).** `RuleEvaluator` now enumerates each unit's `RuleGrant` tokens, resolves the name back to a `ResolvedRule` via the shared army-load resolver (threaded `FDGServer` → `GameContext` → `RuleEvaluator`, optional so resume/#095 and bare-evaluator tests no-op), and walks granted rules through the same per-rule path as static attachments (seat / condition / argless-dedup / suppression). Lights up *every* Aura rule and the read-back half of every "grant rule X" buff/spell. NOTE: the *write* side still needs the aura rules authored in the catalog, and the **`NextTrigger` "use-once" buff** (FirstTrigger consume-on-fire) is **not** included here — `TokenClearService` doesn't yet handle `FirstTrigger`, and those buffs also need #2; they ride the buff-targeting slice.

2. **Cross-unit activated-ability targeting.** Today every ability passes a precomputed target (self, or the stage-chosen `crossed[0]`); `TargetSelector`'s range/affinity is never resolved, and `Activation_OnPreAttack` is never fired in production. Build: fire pre-attack offers + a resolver that lets the player pick the friendly/enemy unit/model within N". Unlocks the whole **Buff / Mark / Debuff** family ("before attacking, pick one friendly/enemy unit within 12/18…"), plus Mend, Re-Position Artillery, and pre-attack offensive abilities (Alien Hives' Breath Attack).

3. **Stub `Condition` bodies.** ✅ **DONE — slice 2 (engine `e5d3c23`).** All five (`Or`, `UnitHasRule`, `TargetHasRule`, `StatGreaterOrEqualTo`, `TargetMajorityHasTough`) now evaluate. The three target-keyed ones read the defender through a new `IHasTarget` capability (in Definitions, like `IHasActingUnit`) that the hit/save/wound contexts implement; `HookContextCatalog` discovers it by reflection so authored rules validate. `TargetMajorityHasTough` / `StatGreaterOrEqualTo(Tough)` share a living-model majority helper; Quality/Defense read the unit stat. Unlocks the "vs Tough(3)+ / vs Defense X" weapon family (Melee Slayer, Slayer, Shatter, Disintegrate, Purge, Demolish, Tear, Break) and every "**…Boost**: if this unit/model has Y" rule.

4. **Consumers for declared-but-dead effects.**
   - `AddExtraWound` → **Shred** — ✅ **DONE — slice 3 (engine `9ae84dd`).** New `IHasUnmodifiedSaveRolls` capability + `WoundInjectionSink` (wound-side mirror of `HitInjectionSink`); `InsertExtraWounds` is now a `SinkOperation`; `AssignWoundsStage` folds it after Deadly, before Regeneration. Registered in `CoreRuleCatalog`.
   - `RangeModifier` → **Increased Shooting Range / Ranged Shrouding / Darkborn**. **DEFERRED → #102** — range feeds target-eligibility / LoS across several stages + the GUI overlay; invasive multi-site cross-repo threading, not a one-seam finish.
   - `IgnoreTerrainEffects` → **Strider / Flying**. **DEFERRED → #102** (also #029's umbrella) — needs the movement difficult-terrain validator to honour a per-unit flag threaded the way `canMoveThroughEnemies` is, across ~14 files in both repos.
   - `Heal` → **Mend**. ✅ **DONE — landed with #2 slice 2e (engine `37a26c4`).** Mend is a catalog rule now; the `InvokeHeal` consumer lives in `OperationApplier`.
   - `RestrictActions` → **Immobile** (Hold-only). ✅ **DONE (engine `1912b5d`).** `ChooseActionStage` intersects the unit's `RestrictActions` ops at `Activation_OnActionChoice` and grays out the disallowed actions (Immobile → loses Move/Charge, keeps Hold-and-shoot). Contained to one stage. Also serves Artillery's deferred Hold-only facet.
   - `StatModifier` (persistent stat change) — **DEFERRED**; lower-value and coupled to the marker/growth (#13) family. Pair with it.

5. **Fire the dormant hooks.** **DEFERRED — not one-line seam finishes.** Each hook needs a stage to dispatch it *and* a paired consumer rule to be worth firing (e.g. `OnActivationStart` is only useful with the Versatile activation-choice ability; `OnPostShoot`/`OnPostMelee` with the Hit-&-Run optional-move resolver; `OnUnitDestroyed`/`OnRoundStart` with the marker-growth family #13). Better landed *with* the rules that consume them than as bare hook-firings. Catalogued here so they're not lost:
   - `Activation_OnActivationStart` → activation-choice self-grants: **Versatile Attack / Reach / Defense, Watchborn**.
   - `Shooting_OnPostShoot` + `Melee_OnPostMelee` → "move up to 3" after shooting/melee": **Hit & Run, Harassing, Guerrilla, Hit & Run Fighter/Shooter** (the `TriggeredMove` effect is already live; only the trigger is missing).
   - `Shooting_OnUnitDestroyed` → "destroyed an enemy" markers: **Piercing/Precision Frenzy, Vengeance**.
   - `Lifecycle_OnWoundIgnored` → "marker on ignored wound": **Regenerative Strength**.
   - `Round_OnRoundStart` / `Round_OnRoundEnd` → start-of-round un-shake (**Battleborn, Steadfast, Honor Code**), per-round growth markers, and spell-token replenishment (feeds #033).

### Part 2 — New effect / operation primitives

6. **Deferred debuff token** — apply a one-shot roll penalty ("-1 to hit / defense / morale / casting") to a *target* unit's next relevant action. The debuff analog of #1. Unlocks **Precision Debuff, Morale Debuff, Defense Debuff, Speed Debuff, Casting Debuff** (roll-penalty part), and the matching "…Debuff" spells.

7. **Morale-outcome override** — convert a failed morale test into a pass and apply self-wounds. **No Retreat.** (Morale state machine exists post-#091/#021; this is a new outcome-rewrite effect.)

8. **Apply terrain state to a target** — force a target to take a Dangerous-terrain test, or count as standing in Dangerous/Difficult terrain. Builds on the existing terrain-effect stage (`ApplyNonMovementTerrainEffectsStage`). Unlocks **Dangerous Terrain Debuff, Difficult Terrain Debuff** and the Plague "Aura of Pestilence"-style spells.

9. **Apply fatigue to a target** — ✅ **BUILT ON #034** (`Effect.ApplyFatigue`, engine `60d8ae9`) — adopt, don't re-build. A rule/spell makes a chosen unit fatigued. Fatigue state exists (#020); applying it as an effect is new. Unlocks **Fatigue Debuff** and War Disciples' Terrifying Fury.

10. **Dice-pool → hits / auto-wounds** — "roll N dice, each ≥X scores a hit (or an auto-wound) on a target." Generalizes the live `DealHits` to dice-driven counts and to wounds-without-to-hit. Unlocks **Ravage, Breath Attack, Crossing Attack**, the Wormhole **Storm of Change/Lust/Plague/War** rules, and underpins #11.

11. **Reflect-damage** — when the bearer takes wounds in melee, or dies, deal hits back at the attacker. **Retaliate, Self-Destruct, Deathstrike.** New on-wound-taken / on-death hook + deal-hits-at-attacker.

12. **Attack-count modifier producer** — "+X attacks" (flat or marker-scaled) at the #015 `Shooting_OnPreHitRollCount` seam (the seam exists; no producer yet). Unlocks **Regenerative Strength** and clean handling of the "doesn't apply to newly generated attacks" clause on Predator-family rules.

13. **Marker-scaled magnitude** — a `ValueSource.TokenCount` and an effect whose magnitude scales with a marker count; combined with the round/destroy triggers (#5) this gives the **growth/frenzy** family (Piercing/Precision/Defensive Growth, Piercing/Precision Frenzy, Fortified Growth).

14. **Enemy marker-tag + spend-for-bonus** — tag a target with X markers, then let friendly attackers spend the target's markers for +AP / +hit. **Piercing Tag, Precision Tag, Piercing/Precision Target, Precision Spotter, Piercing Spotter.**

15. **Randomized-branch effect** — "roll 1 die: 1-3 → effect A, 4-6 → effect B," applied to all models with the rule for the attack. **Unpredictable / Unpredictable Fighter / Unpredictable Shooter** (and their Marks via #2/#6).

16. **One-shot special-attack injection** — once per game, inject a single extra attack with an authored weapon profile (e.g. Quality 2+, AP(2), Deadly(3), Takedown). **Takedown Strike / Takedown Shot.**

17. **Place / restore a unit mid-game** — create a new unit or restore destroyed models. **Spawn, Reinforcement, Reanimation, Split.** (Touches deployment + table-state lifecycle.)

18. **Move-the-target** — ✅ **BUILT ON #034** (caster-directed `Effect.TriggeredMove`, engine `0e707b7`) — adopt, don't re-build. Force a chosen enemy unit to move a set distance/direction. **Mind Control**; Soul-Snatcher Cults' Deep Hypnosis.

19. **Reactivate-another-unit** — generalize the live self-`Reactivate` to activate a *chosen* friendly unit (the effect's doc already anticipates this). **Coordinate.**

20. **Action-permission modifiers** — (a) allow shooting after a Rush (**Quick Shot, Shift**); (b) strike-order "strikes last" — the inverse of the live `StrikeFirst` (**Unwieldy**, and Unwieldy Debuff via #6).

21. **Setup-phase re-deploy / reposition** — remove and re-place units during/after deployment. **Re-Deployment, Dash** (Re-Position Artillery is the live `TriggeredMove` once #2 lands).

22. **New deploy timings / placements** — `DeferDeployment` variants beyond Scout/Ambush: **Infiltrate** (anywhere >3" from enemy at setup), **Rapid Ambush** (any round incl. the first), **Ambush Beacon** (ignore-distance near a beacon model), **Surprise Attack** (Infiltrate + first-activation burst, also needs #10).

### Part 3 — Subsystem

23. **Casting subsystem (#033 / #034).** Caster(X) spell-token pool per round, cast roll (4+), friendly assist ±1 within 18", and resolution against targets — firing the three already-defined `Casting_*` hooks (none fire today). Plus the caster-support special rules: **Spell Conduit, Spell Accumulator, Caster Group, Casting Buff, Casting Debuff.** This is the single biggest content unlock: all 47 armies have **6 spells each (282 spells)**. Crucially, once casting fires, **most spells reduce to primitives already listed**, so the spell content is mostly data:
   - pure damage → `DealHits`/#10 (Human Defense Force's Searing Burst; Dark Brothers' Lightning Fog; Prime Brothers' Psychic Terror).
   - grant a rule → #1 (Blessed Sisters' Litanies of War; Battle Brothers' Blessed Ammo; any "… Boon").
   - deferred debuff → #6 (Change Disciples' Shifting Form; Blessed Sisters' Burn the Heretic).
   - heal → #4 `Heal`.
   - terrain state → #8 (Plague Disciples' Aura of Pestilence).
   - move the target → #18 (Soul-Snatcher Cults' Deep Hypnosis).
   - single-model nuke → `DealHits` + Takedown-style single-model targeting (Alien Hives' Hive Shriek; Dwarf Guilds' Breaking Rune).

## Suggested sequencing (highest leverage first)
1. **#1 granted-rule read-back** — unlocks all auras + grant-buffs in one change.
2. **#3 stub conditions** — cheap; unlocks the whole "vs Tough/Defense" weapon family and all "…Boost" rules.
3. **#4 dead-effect consumers** — Shred alone touches almost every army.
4. **#2 + #5 hooks** — the cross-unit pre-attack + post-shoot/melee plumbing; unlocks the Buff/Mark/Debuff and after-move families.
5. **#23 casting** — turns 282 spells into mostly data once the above primitives exist.
6. Then the Tier-C one-offs (#6–#22) as their armies come up.

## Part 1 #2 — Design: cross-unit pre-attack targeting (for red-line)

The gateway for the largest rule family — Buffs, Marks, Debuffs, Mend, Re-Position, and pre-attack
offensive abilities ("before attacking, pick one friendly/enemy unit within N…"). Also completes the
*buff* half of slice 1 (the `NextTrigger` grants). Forks settled 2026-06-22:

1. **A dedicated pre-attack stage** (not the #010 custom-action branch). It's a reusable template —
   "fire a hook → offer its abilities → resolve targets → apply" — that other dormant hooks (#5) can copy.
2. **Target selection reuses `SelectionRequest<IUnit>`** with affinity/range/LoS filtering, so the
   existing CLI/GUI/AI resolvers for it come (nearly) free.
3. **`FirstTrigger` buff lifetime = consume-on-fire (Option A):** the buff is spent exactly when its
   granted effect actually applies, and persists if it never triggers — faithful to "next time the effect
   would apply," and self-contained (no dependency on the deferred post-attack hooks).
4. **AI: a simple default** target policy (buffs → self/nearest friendly; debuffs/marks → nearest enemy),
   always producing a legal answer per the #066 AI contract.

### Flow
A new `PreAttackStage` runs once the unit has chosen an attack action, before targets/weapons resolve
(matching `Activation_OnPreAttack`). It builds `PreAttackContext(actingUnit, actionType)`, calls
`RuleEvaluator.GatherOffers` → affordable `AbilityOffer`s, asks the acting player which (if any) to use
(once-per-activation gated by the existing `AbilityUsed:` marker), then for each accepted ability resolves
its `TargetSelector` and calls `RuleEvaluator.ResolveAbility(offer, chosenTargets)` → ops (cost-consume +
effect), which the stage applies. **Open: exact insertion point** — confirm where `ChooseActionStage`
routes into shoot/melee and whether one PreAttackStage serves both (the hook is action-agnostic).

### TargetSelector resolution (new — never read today)
A `PreAttackTargeting.EligibleTargets(actingUnit, selector, tableState)` helper: filter by
`ETargetAffinity` (Self / Friend = same team / Foe = enemy team), by `RangeInches` (unit-to-unit
distance), and by `RequireLineOfSight`. Present the eligible set via `SelectionRequest<IUnit>` honouring
`MinCount`/`MaxCount`. (A "which pre-attack ability?" prompt rides `StringSelectionRequest` when a unit
offers more than one.)

### FirstTrigger consume-on-fire (Option A) mechanics
Thread the source token into the synthesized granted `ResolvedRule` in `CollectGrantedRules`. In
`CollectSurviving` (post-suppression, so only *surviving* effects count), for each granted rule that came
from a `FirstTrigger`/`NextTrigger` token and produced ≥1 surviving op, emit one `RemoveTokens` op for that
token. The stage applies it alongside the effect ops — so the buff dies the moment it helps, in the buffed
unit's own attack resolution. Known minor nuance: consumed on the first sub-roll it applies to (a two-melee-
weapon unit gets it on the first weapon only); A→A′ (whole-action scope) is an additive change if ever needed.

### Slice plan
- **2a** — ✅ **DONE (engine `0956527`).** `PreAttackStage` fires `Activation_OnPreAttack` and resolves
  SELF-targeted abilities, inserted on both attack edges of `ChooseActionStage` (Charge → melee, Shoot →
  shoot); layered (no HasMoved/HasAttacked). **Insertion point confirmed.** Surprise: two sibling instances
  of one stage type collided on the parent's transition key (`StageBase.Name => GetType().Name`), so `Name`
  is now `virtual` and `PreAttackStage` overrides it per action type. 3 tests; suite 718/0; headless exit 0.
- **2b** — ✅ **DONE (engine `ae7a3bb`).** `PreAttackTargeting.EligibleTargets` (the first code to read a
  `TargetSelector`: affinity/range/LoS/required-token, off-battlefield excluded) + cross-unit selection via
  `CancellableSelectionRequest<UnitData>` (MinCount..MaxCount, cancel-before-min aborts). An ability is
  offered only when it has enough valid targets. "Pick a friendly unit within 12, grant X" works.
- **2c** — ✅ **DONE (engine `b97fa7a`).** FirstTrigger consume-on-fire (Option A): the evaluator emits a
  `ConsumeRuleGrant` for a granted FirstTrigger rule whose effect survives, applied via the new
  `RemoveTokensWithPayload`. **Also fixed a latent slice-1 bug:** `TokenContainer.AddToken` merged by
  type+owner only, collapsing distinct `RuleGrant` payloads into one count (dropping all but the first
  grant); the merge is now payload-aware so multiple grants coexist.
- **2d** — ✅ **DONE (engine `47dcf17`).** `AiStringSelectionResolver` skips the pre-attack menu (picks
  Done) — a conservative, always-legal default (the AI doesn't yet reason about buffs), so it never fires
  abilities blindly or issues a pre-attack target request. A real "buff self / mark nearest" policy is a
  future refinement.
- **2e** — ✅ **DONE (engine `37a26c4`).** Authored **Furious Buff** (grant Furious to a friendly — read back
  + consumed on fire) and **Mend** (heal a friendly D3) as catalog rules, wiring the previously-dead
  `Effect.Heal` consumer into `OperationApplier` (clamped to wounds taken). **This closes the #4-Heal item
  deferred earlier.** A true Mark/Debuff (deferred-debuff #6) and offensive pre-attack (dice-pool #10) are
  their own primitives, not part of the #2 targeting stack. 2 integration tests.

**#2 COMPLETE (2a–2e).** The whole "before attacking, pick a friendly/enemy unit within N, grant/heal X"
family is now authorable and works end-to-end (offer → target → resolve → grant/heal → consume-on-fire).

### Risks / open
Insertion point + shoot-vs-melee sharing (2a); `Heal` consumer lands here (deferred from #4-Heal); spans
both repos (engine stage + app-side resolver tweaks for the ability prompt).

## Notes
- 2026-06-28: **⚠️ Two catalog primitives were built on the `034-spell-content` branch — adopt them, do NOT
  re-build (parallel-build collision).** #034's spell-primitive work implemented: **#9 (apply fatigue to a
  target)** as `Effect.ApplyFatigue` → `IOperationServices.ApplyFatigue` → `FatigueUtilities.ApplyFatigued`
  (engine `60d8ae9`); and **#18 (move-the-target)** as a caster-directed `Effect.TriggeredMove` — `MoveUnit`
  gained a controller param so the request routes to the rule's bearer, not the victim (engine `0e707b7`).
  Both were built on the same engine master this branch sits on, so the effect/op names should line up at
  merge — reconcile rather than duplicate. #034 also reuses #100 #1 (granted-rule read-back) to author plain
  "gains rule X" spell buffs. See `WorkItems/034-spell-content.md` (2026-06-28 notes).
- 2026-06-22: **RestrictActions / Immobile done** (engine `1912b5d`) — `ChooseActionStage` honours `RestrictActions` ops, graying out disallowed actions; Immobile authored. Picked as the cleanest *contained* remaining Part-1 item (the others — RangeModifier, Strider — thread the `canMoveThroughEnemies`-style flag through ~14 files incl. core movement validation across both repos, too invasive to take on unsupervised; left for their own scoped efforts). Suite 729/0, headless exit 0.
- 2026-06-22: **#2 COMPLETE** — slices 2d + 2e done (engine `47dcf17`, `37a26c4`). 2d: AI skips the pre-attack menu (conservative default). 2e: authored Furious Buff + Mend catalog rules; wired the deferred `Effect.Heal` consumer (closes #4-Heal). The full "before attacking, pick a friendly/enemy unit within N, grant/heal X" family works end-to-end. Suite 727/0, headless exit 0. A true Mark/Debuff (deferred-debuff #6) and offensive pre-attack (dice-pool #10) remain their own future primitives. **Net for the session: Part 1 #1/#3/#4-Shred/#4-Heal done; #2 fully done; #5 and the remaining #4/Part-2/Part-3 primitives catalogued for later.**
- 2026-06-22: **#2 slices 2b + 2c done** (engine `ae7a3bb`, `b97fa7a`). 2b: cross-unit targeting — `PreAttackTargeting.EligibleTargets` reads the `TargetSelector` (affinity/range/LoS/token), selection via `CancellableSelectionRequest<UnitData>`; "pick a friendly unit within 12, grant X" works end-to-end. 2c: FirstTrigger consume-on-fire (Option A) + a latent slice-1 token-merge bug fixed (distinct grant payloads were collapsing). Suite 724/0, headless exit 0. Remaining #2: **2d** (AI policy — note the existing AI resolvers already answer the StringSelection + CancellableSelection generically, so this is mostly "make the default sensible / skip"), **2e** (wire representative rules incl. Mend via the deferred `Heal` consumer + integration tests).
- 2026-06-22: **#2 slice 2a done** (engine `0956527`) — dedicated `PreAttackStage` live in the activation flow, insertion point confirmed (between `ChooseActionStage` and the shoot/melee stages, layered). Made `StageBase.Name` virtual so the two sibling instances (one per attack edge) don't collide on the transition key. Self-targeted abilities resolve; cross-unit targeting is 2b. Suite 718/0, headless exit 0. User greenlit #2 in full ("make it so").
- 2026-06-22: **#2 design written for red-line** (above): dedicated pre-attack stage, `SelectionRequest<IUnit>` targeting, Option-A buff consumption, simple AI; slice plan 2a–2e. Awaiting sign-off before building. All four forks settled with the user.
- 2026-06-22: **Slices 2 & 3 done** — Part 1 #3 (stub conditions, engine `e5d3c23`) and #4-Shred (engine `9ae84dd`). Suite 715/0; full build clean; headless smoke exit 0. **Explicitly deferred** the rest of Part 1 (recorded in the catalog above, not silently cut): #4 RangeModifier/Strider (invasive multi-site / movement-subsystem), #4 Heal/Mend (dead until #2's pre-attack targeting), #4 StatModifier/RestrictActions (pair with the marker/action families), and all of #5 (dormant hooks need paired consumer rules — land them with those rules). Net: the three clean, high-value seam-finishes shipped (grant read-back, conditions, Shred); the remainder are genuinely larger or #2-coupled. Paused here for the #2 design discussion as planned.
- 2026-06-22: **Slice 1 — granted-rule read-back done** (engine `4fb6159`, on branch `100-special-rule-primitives`). Closed the `RuleGrant` write→read loop in `RuleEvaluator` (`CollectGrantedRules`); shared resolver threaded through `FDGServer`/`GameContext`; new `GrantedRuleReadbackTests` (read-back fires, control, null-resolver + unknown-name safe, argless dedup, aura end-to-end). Suite 705/0, full build clean, headless smoke exit 0. Inert in live play until aura rules are authored (no `RuleGrant` tokens exist yet) — verified the headless game is unchanged. Confirmed `033-caster` (parallel) resolves spells through its own `CastSpellStage`, not granted-rule read-back, so no overlap. Branched off submodule `a0ab822` / superproject `b5e71aa` (latest master at start).
- 2026-06-22: Created from the first-five-armies primitive audit (see session "ExtractSpecialRulesAndSpells"). Numbered **100** at the user's request to stay clear of branches that may have claimed 095–099 elsewhere — confirm no collision at merge per the never-reuse rule.

## Decisions
- The B/C split is a judgment line; the durable artifact is the **primitive list**, not the percentages. Two facts anchor it: (1) `RuleGrant` tokens are written but never read (`RuleEvaluator.cs:201-214`) — so auras/buffs are non-functional despite the effects existing; (2) five `Condition` subtypes throw from their base `Evaluate`. Both are "finish the seam," not "design something new," which is why ~75% of the corpus is reachable without new architecture — the real cliff is the casting subsystem (#23) and the dice/marker/lifecycle primitives (#10–#18).

## Outcome
_(written when closed)_
