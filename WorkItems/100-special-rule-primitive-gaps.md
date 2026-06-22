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

3. **Stub `Condition` bodies.** `Or`, `UnitHasRule`, `TargetHasRule`, `StatGreaterOrEqualTo`, `TargetMajorityHasTough` are declared but their `Evaluate` throws (`Rules/Definitions/Condition.cs`). Implement them. Unlocks the "vs Tough(3)+ / vs Defense X" weapon family (Melee Slayer, Slayer, Shatter, Disintegrate, Purge, Demolish, Tear, Break) and every "**…Boost**: if this unit/model has Y" rule (Hive Bond Boost, Lustbound Boost, Scrapper Boost, Changebound Boost, …).

4. **Consumers for declared-but-dead effects.** These `Effect` subtypes exist (some lack an `Apply`) but no stage consumes the operation:
   - `AddExtraWound` → **Shred** ("+1 wound on unmodified 1 to block"). Appears in nearly every army; needs an `Apply` + a wound-injection sink at `Shooting_OnSaveRollComplete`.
   - `Heal` → **Mend** (remove D3 from a friendly Tough model). Op `InvokeHeal` has no consumer.
   - `RangeModifier` → **Increased Shooting Range / Ranged Shrouding / Darkborn** ("+6"/-X" range").
   - `StatModifier` → persistent stat change for "+1 Defense until end of game" growth payoffs (no `Apply` today).
   - `RestrictActions` → Hold-only (Immobile, Artillery's Hold facet).
   - `IgnoreTerrainEffects` → **Strider / Flying** terrain ignore.

5. **Fire the dormant hooks.** Each is defined in `EHookID` but never dispatched by a live stage; firing it unlocks a class:
   - `Activation_OnActivationStart` → activation-choice self-grants: **Versatile Attack / Reach / Defense, Watchborn**.
   - `Shooting_OnPostShoot` + `Melee_OnPostMelee` → "move up to 3" after shooting/melee": **Hit & Run, Harassing, Guerrilla, Hit & Run Fighter/Shooter** (the `TriggeredMove` effect is already live; only the trigger is missing).
   - `Shooting_OnUnitDestroyed` → "destroyed an enemy" markers: **Piercing/Precision Frenzy, Vengeance**.
   - `Lifecycle_OnWoundIgnored` → "marker on ignored wound": **Regenerative Strength**.
   - `Round_OnRoundStart` / `Round_OnRoundEnd` → start-of-round un-shake (**Battleborn, Steadfast, Honor Code**), per-round growth markers, and spell-token replenishment (feeds #033).

### Part 2 — New effect / operation primitives

6. **Deferred debuff token** — apply a one-shot roll penalty ("-1 to hit / defense / morale / casting") to a *target* unit's next relevant action. The debuff analog of #1. Unlocks **Precision Debuff, Morale Debuff, Defense Debuff, Speed Debuff, Casting Debuff** (roll-penalty part), and the matching "…Debuff" spells.

7. **Morale-outcome override** — convert a failed morale test into a pass and apply self-wounds. **No Retreat.** (Morale state machine exists post-#091/#021; this is a new outcome-rewrite effect.)

8. **Apply terrain state to a target** — force a target to take a Dangerous-terrain test, or count as standing in Dangerous/Difficult terrain. Builds on the existing terrain-effect stage (`ApplyNonMovementTerrainEffectsStage`). Unlocks **Dangerous Terrain Debuff, Difficult Terrain Debuff** and the Plague "Aura of Pestilence"-style spells.

9. **Apply fatigue to a target** — a rule/spell makes a chosen unit fatigued. Fatigue state exists (#020); applying it as an effect is new. Unlocks **Fatigue Debuff** and War Disciples' Terrifying Fury.

10. **Dice-pool → hits / auto-wounds** — "roll N dice, each ≥X scores a hit (or an auto-wound) on a target." Generalizes the live `DealHits` to dice-driven counts and to wounds-without-to-hit. Unlocks **Ravage, Breath Attack, Crossing Attack**, the Wormhole **Storm of Change/Lust/Plague/War** rules, and underpins #11.

11. **Reflect-damage** — when the bearer takes wounds in melee, or dies, deal hits back at the attacker. **Retaliate, Self-Destruct, Deathstrike.** New on-wound-taken / on-death hook + deal-hits-at-attacker.

12. **Attack-count modifier producer** — "+X attacks" (flat or marker-scaled) at the #015 `Shooting_OnPreHitRollCount` seam (the seam exists; no producer yet). Unlocks **Regenerative Strength** and clean handling of the "doesn't apply to newly generated attacks" clause on Predator-family rules.

13. **Marker-scaled magnitude** — a `ValueSource.TokenCount` and an effect whose magnitude scales with a marker count; combined with the round/destroy triggers (#5) this gives the **growth/frenzy** family (Piercing/Precision/Defensive Growth, Piercing/Precision Frenzy, Fortified Growth).

14. **Enemy marker-tag + spend-for-bonus** — tag a target with X markers, then let friendly attackers spend the target's markers for +AP / +hit. **Piercing Tag, Precision Tag, Piercing/Precision Target, Precision Spotter, Piercing Spotter.**

15. **Randomized-branch effect** — "roll 1 die: 1-3 → effect A, 4-6 → effect B," applied to all models with the rule for the attack. **Unpredictable / Unpredictable Fighter / Unpredictable Shooter** (and their Marks via #2/#6).

16. **One-shot special-attack injection** — once per game, inject a single extra attack with an authored weapon profile (e.g. Quality 2+, AP(2), Deadly(3), Takedown). **Takedown Strike / Takedown Shot.**

17. **Place / restore a unit mid-game** — create a new unit or restore destroyed models. **Spawn, Reinforcement, Reanimation, Split.** (Touches deployment + table-state lifecycle.)

18. **Move-the-target** — force a chosen enemy unit to move a set distance/direction. **Mind Control**; Soul-Snatcher Cults' Deep Hypnosis.

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

## Notes
- 2026-06-22: **Slice 1 — granted-rule read-back done** (engine `4fb6159`, on branch `100-special-rule-primitives`). Closed the `RuleGrant` write→read loop in `RuleEvaluator` (`CollectGrantedRules`); shared resolver threaded through `FDGServer`/`GameContext`; new `GrantedRuleReadbackTests` (read-back fires, control, null-resolver + unknown-name safe, argless dedup, aura end-to-end). Suite 705/0, full build clean, headless smoke exit 0. Inert in live play until aura rules are authored (no `RuleGrant` tokens exist yet) — verified the headless game is unchanged. Confirmed `033-caster` (parallel) resolves spells through its own `CastSpellStage`, not granted-rule read-back, so no overlap. Branched off submodule `a0ab822` / superproject `b5e71aa` (latest master at start).
- 2026-06-22: Created from the first-five-armies primitive audit (see session "ExtractSpecialRulesAndSpells"). Numbered **100** at the user's request to stay clear of branches that may have claimed 095–099 elsewhere — confirm no collision at merge per the never-reuse rule.

## Decisions
- The B/C split is a judgment line; the durable artifact is the **primitive list**, not the percentages. Two facts anchor it: (1) `RuleGrant` tokens are written but never read (`RuleEvaluator.cs:201-214`) — so auras/buffs are non-functional despite the effects existing; (2) five `Condition` subtypes throw from their base `Evaluate`. Both are "finish the seam," not "design something new," which is why ~75% of the corpus is reachable without new architecture — the real cliff is the casting subsystem (#23) and the dice/marker/lifecycle primitives (#10–#18).

## Outcome
_(written when closed)_
