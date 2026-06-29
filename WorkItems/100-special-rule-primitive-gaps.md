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

14. **Enemy marker-tag.** Two variants: (a) **rule-conferring mark** ("mark an enemy → the next friendly to attack it gets rule X, then spent") — ✅ **BUILT** (`Effect.MarkTarget`, engine `5ef5443`; the "X against once" spell family); (b) **spend-for-bonus markers** — tag a target with X markers, then let friendly attackers spend them for +AP/+hit (**Precision Spotter, Piercing Spotter, Piercing/Precision Tag/Target**) — still open (needs a count-and-spend mechanic, not the one-shot rule transfer).

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
- 2026-06-28: **Aura wave 3 — defensive wound-ignore + AP rules + auras (8 rules).** Surveyed the
  remaining un-cataloged aura bases by frequency and cataloged the ones that map to existing primitives:
  **Resistance** & **Protected** (each "wound ignored on 6+" = `IgnoreWoundOnRoll(6)`, Subject — Regeneration
  at a higher threshold; mechanically identical to each other), **Piercing Assault** ("AP(+1) when charging"
  = Thrust's AP facet alone, `RollModifier(Save,-1)` gated `And(IsMelee,IsCharging)`), **Piercing Hunter**
  ("AP(+1) shooting >9"" = same AP shape gated `DistanceGreaterThan(9)`, like Artillery). + their 4 auras
  via `UnitAura`. `All` 88 → 96. Tests: Resistance threshold (ignores 6s only → 2.5/3), Piercing
  Assault/Hunter save-modifier gates (charge / distance), aura-integrity auto-covers the 4 auras. Suite
  894/0, app build clean, headless exit 0.
  **Deliberately deferred** (need machinery, not data — recorded so they're not silently dropped):
  - **Shielded** (+1 defense) / **Fortified** (incoming AP-1, min 0): the hit-complete save-modifier eval
    passes ONLY the attacker (Actor) participant, so a *defender* save bonus needs the defender threaded as
    a Subject participant there — a seam (like #093's threading). Fortified additionally needs an
    AP-reduction-with-floor primitive (not a flat save bonus). Own slice.
  - **Melee/Ranged Shrouding, Increased Shooting Range**: movement/charge/range *validation* threading → #102.
  - **Versatile Reach / Versatile Defense**: pick-one-of-two-effects-per-activation → an activated-ability
    choice primitive. **Unpredictable Fighter/Shooter**: roll-a-die-then-branch primitive. **Steadfast /
    No Retreat / Reanimation**: round-start / morale-recovery machinery. **Quick Shot / Swift**:
    action-permission (changes action legality, not a modifier). **Precision Fighter/Shooter, Piercing
    Fighter, Rending when Shooting, Thrust in Melee**: corpus gives no standalone base definition (aura/grant
    only) — would be guessing semantics, so left until grounded.
- 2026-06-28: **Next aura wave — Courage + the "in melee" mirrors + their auras (10 rules).** Unlocked the
  high-frequency auras whose base wasn't cataloged. Base rules: **Courage** (+1 to morale tests — the rule
  the Fearless doc anticipated, `RollModifier(Morale,+1)` at `Morale_OnPreMoraleTest`, folds easier); and
  the **"in melee" combat-kind mirrors** **Bane / Rending / Shred / Unstoppable in melee** — the exact flip
  of the when-shooting trio (same effect at the shared save/hit hooks, `Condition.IsMelee()` instead of
  `Not(IsMelee)`; Rending mirrors base Rending — AP-on-6 + Regen-ignore — with an added melee gate). Then
  the 5 auras (Courage Aura, Bane/Rending/Shred/Unstoppable in Melee Aura) via the `UnitAura` factory. The
  corpus only *names* these via the aura/buff (e.g. Courage Aura inlines "+1 morale"; "Bane in Melee Aura"
  grants "Bane in melee"), so cataloging the base is what makes the grant resolve. `All` 78 → 88. Tests:
  `Courage_LowersTheMoraleThreshold` (real catalog rule), `BaneInMeleeAttacker_Melee_RerollsSavedSixes` +
  `…_Shooting_DoesNotReroll` (representative in-melee gate; the other 3 mirrors share the tested IsMelee
  pattern + their shooting twins), and the aura-integrity test auto-covers the 5 new auras. Suite 889/0,
  app build clean, headless exit 0.
- 2026-06-28: **Case-sensitive resolution — FIXED (resolver now case-insensitive).** Resolved the latent
  finding below the user's way (case-insensitivity, not renaming): `RuleResolver._rules` is now an
  `OrdinalIgnoreCase` dictionary, so "Bane when Shooting" (corpus) resolves to the registered "Bane when
  shooting" and vice versa — and registration rejects case-only duplicates, keeping register/resolve
  semantics aligned. Also made the two name-matching `Condition`s (`UnitHasRule` / `TargetHasRule`)
  case-insensitive for consistency, so "has Bane when Shooting" matches the lowercase-registered rule.
  Test `Resolver_IsCaseInsensitive_CorpusCasingResolvesToCatalogRule`. The aura grant strings were left as
  the exact registered names (still correct under the new comparer). Suite 886/0, app build clean, headless
  exit 0. This is the higher-leverage fix — every #034 corpus reference that differs only by case now
  resolves, not just the three "when shooting" rules.
- 2026-06-28: **Aura cluster — CATALOGED (17 new auras, pure data).** Generalized `Effect.Aura` (proven by
  the post-combat-move auras) across every base rule already in `All`. Surveyed the corpus (uniform wording
  "this model and its unit get X", NO ranged auras → all map to `Effect.Aura`); intersected the ~89 distinct
  corpus aura names with the catalog. Added a private **`UnitAura(name, grantedRule)` factory** (Effect.Aura
  at `Lifecycle_OnUnitCreated`, `UntilEndOfGame`) and folded the 4 existing post-combat auras into it for
  one uniform aura section. New auras: Regeneration, Furious, Stealth, Scout, Relentless, Ambush,
  Counter-Attack, Evasive, Fast, Fearless, Melee Evasion, Rapid Advance, Rapid Charge, Rapid Rush, +
  Bane/Shred/Unstoppable-when-Shooting. `All` 61 → 78; all pickable in the Army Creator. One
  **catalog-integrity test** (`EveryCatalogAura_GrantsAResolvableRule`) iterates `All` and asserts every
  `Effect.Aura` grants a resolver-known name — covers all 21 auras + any future one, and caught nothing
  (casing correct). Suite 885/0, app build clean, headless exit 0. **Deliberately deferred** (base rule not
  yet cataloged, so the aura would grant an unresolvable name): Courage, Melee Shrouding, Versatile Reach,
  Resistance, the "in melee" combat-kind mirrors (Bane/Rending/Shred/Unstoppable in Melee), Bounding,
  Teleport, etc. — each needs its base rule first.
- 2026-06-28: **Latent finding — case-sensitive rule resolution vs corpus casing.** `RuleResolver` is an
  ordinal (case-sensitive) dictionary, but three #093 rules are registered lowercase — "Bane when shooting",
  "Shred when shooting", "Unstoppable when shooting" — while the corpus writes them "Bane when **S**hooting"
  etc. So a corpus army list / spell that references the rule by its book name resolves to **nothing**
  (skip-and-warn), silently dropping the rule. Not introduced here and the new auras dodge it (they grant
  the exact lowercase registered name, verified by the integrity test). Recommended fix (its own small,
  deliberate slice): rename those 3 base rules to the corpus casing ("…when Shooting") + update the 3 aura
  grant strings to match + grep for any other by-name resolution of the lowercase form. Flagged for sign-off
  rather than folded in, to keep the aura cluster scope clean.
- 2026-06-28: **Post-combat-move Boost + Aura variants — BUILT (6 rules, mostly data + one gate tweak).**
  Cataloged the corpus's variant rules on the existing family:
  - **Boosts** (Harassing Boost, Guerrilla Boost): "if the unit has the base rule, move 6\" instead of 3\"."
    Each is a post-combat-move rule emitting `TriggeredMove(6")` at both hooks, gated
    `Condition.UnitHasRule("Harassing"/"Guerrilla")`. The "instead of 3\"" is realized by a small
    **`PostCombatMoveGate` change: coalesce all the hook's `InvokeTriggeredMove` ops into ONE move at the
    MAX budget** — so base-3" + boost-6" → a single 6" move. (Bonus: this also fixes a latent double-move
    if a unit ever stacks two family rules — previously each emitted its own move request.)
  - **Auras** (Hit & Run Shooter Aura, Hit & Run Fighter Aura, Harassing Boost Aura, Guerrilla Boost Aura):
    "this model and its unit get X." Authored as `Effect.Aura("<base>")` at `Lifecycle_OnUnitCreated`
    (first production uses of `Effect.Aura`); the grant projects unit-wide via the #100 #1 read-back
    (`CollectGrantedRules`), so the granted family rule fires at the post-combat hooks for the whole unit.
    Each granted rule is already cataloged, so the resolver resolves it by name.
  All 6 registered in `All` (now pickable in the Army Creator). 3 tests: Boost upgrades to 6" (coalesced,
  single move), Boost inert without the base rule (UnitHasRule gate fails), Aura grants unit-wide + fires
  at the right hook only. Suite 884/0, app build clean, headless exit 0. **Two judgment calls (noted):**
  (1) coalesce-to-MAX for "instead of" rather than a suppression/replacement primitive — cleaner and fixes
  the latent double-move; (2) `UnitHasRule` (unit-level) for the corpus's "most MODELS have X" — the
  architecture's designated gate; true per-model majority is a #093 nuance, consistent with treating
  "all-models-have-X" rules as unit-level elsewhere. The post-combat-move family (base + once-per-round +
  Boost + Aura) is now complete.
- 2026-06-28: **Post-combat-move "once per round" gate — BUILT (closes the family's deferred facet).**
  New `TokenType.PostCombatMoveUsed` (RoundEnd clear, swept by the round-end pass like Fatigue) + a shared
  `PostCombatMoveGate.OfferIfAvailable(ctx, unit, ops)` helper that both `PostShootStage` and
  `PostMeleeStage` now route through instead of running the move ops directly. The gate: no-op if the unit
  has no post-combat-move rule (empty ops); skips if `PostCombatMoveUsed` is already present; otherwise
  enacts the move and — **only if the unit actually repositioned** (model positions compared before/after,
  so a declined zero-distance move keeps the budget) — sets the marker. **One shared budget across
  shooting and melee** (matches "once per round after shooting OR melee"). Tests:
  `PostCombatMove_OncePerRound_SharedAcrossShootAndMelee` (move after shooting spends it; a later
  post-melee trigger is gated) + `PostCombatMove_DeclinedMove_KeepsBudget`. Suite 881/0, app build clean,
  headless exit 0. **Minor known edge (noted, not a headline gap):** a unit carrying TWO separate family
  rules (e.g. Hit & Run Shooter + Hit & Run Fighter) shares the one budget rather than getting one each —
  faithful for the common single-rule case; revisit only if a unit in the corpus stacks them. The
  post-combat-move family is now faithfully once-per-round; Appendix C row removed.
- 2026-06-28: **Post-combat-move family — CATALOGED (pure data on the live seam).** Added four rules to
  `CoreRuleCatalog.All`, all the same `TriggeredMove(3", optional)` shape as Harassing, differing only in
  which post-combat hook(s) they carry: **Hit & Run Shooter** (`Shooting_OnPostShoot`), **Hit & Run
  Fighter** (`Melee_OnPostMelee`), **Hit & Run** (both), **Guerrilla** (both). Grounded against the
  off-repo corpus (`Special Rules and Spells by Army.md`) — Harassing / Hit & Run / Guerrilla are
  mechanically identical, faction-renamed. Now pickable in the Army Creator (picker derives from `All`).
  4 hook-correctness tests (each fires at its hook, not the other) in `TriggeredMoveRuleIntegrationTests`.
  Suite 879/0, app build clean, headless exit 0. **DEFERRED FACET — "once per round" gate (whole family,
  incl. shipped Harassing):** the corpus wording is "*once per round*, units … may move after shooting or
  being in melee." None of these rules gate that — they fire once per shoot ACTION and once per resolved
  melee. Shooting is fine in practice (a unit shoots once/round), but **a unit charged by several enemies
  in a round can move after each melee**, and a both-hooks rule can move after shooting *and* after melee
  the same round. Faithful fix needs a per-round "already used the post-combat move" marker
  (`RoundEnd`-clear token set on fire + checked before offering) — a small NEW primitive (a once-per-round
  passive gate), not pure data, so it's its own slice. **Also still separate (need more than data):** the
  *Boost* variants ("if most models have the base rule, move 6\" instead of 3\"" — a conditional
  replacement/upgrade) and the *Aura* variants ("this model and its unit get X" — an `Effect.Aura` grant,
  authorable on #100 #1's read-back but not yet cataloged).
- 2026-06-28: **Hit & Run / post-melee move (#5) — BUILT (melee seam; completes the post-combat seam).**
  Lit the dormant `Melee_OnPostMelee` hook, the twin of the shooting seam below. New per-action context
  `PostMeleeActionContext(IUnit Unit)` (Hook ⇒ `Melee_OnPostMelee`) + new `PostMeleeStage`, inserted into
  `MeleeStage` after `ConsolidateStage`: `consolidate.OnConsolidated` → `postMelee` → `meleeFinishedEvent`.
  The `BackToChooseAction` exit (no melee occurred) still routes straight to `meleeFinishedEvent`, bypassing
  the move — correct. Extended **Harassing** with a second `HookEntry(Melee_OnPostMelee, Always,
  TriggeredMove(3", optional))`; it now fires on both shooting AND melee. Same `EvaluateAll((unit, Actor))`
  → `OperationExecutor` chain. Test `Harassing_AtPostMeleeHook_RepositionsTheUnit` added. Suite 875/0, app
  build clean, headless exit 0. **Seat decision (settled, revisitable):** only the **charged/attacked**
  unit is offered the move — matches the `Melee_OnPostMelee` doc ("Harassing's move *from being
  attacked*"), not the charger. `PostMeleeStage.ResolveAttackedUnit` picks the participant that is NOT
  `ChargingUnit` (immutable through a Counter `SwapCombatRoles`, so swap-robust), guarded on `GetIsAlive()`
  (the charged unit can be wiped out by the melee → nothing to move). If a future "Hit & Run after charging"
  flavour is wanted, fire for both participants instead. **Now resolved (was deferred):** Harassing's melee
  half. **Still data-once-cataloged:** Hit & Run Fighter (melee-only), Hit & Run (both), Guerrilla — same
  `TriggeredMove` shape, just need catalog entries. The post-combat-move seam (#5 dormant hooks) is now
  **fully live on both shooting and melee.**
- 2026-06-28: **Hit & Run / post-shoot move (#5) — BUILT (shooting seam).** Lit the dormant
  `Shooting_OnPostShoot` hook. New per-action context `PostShootActionContext(IUnit Unit)` (Hook ⇒
  `Shooting_OnPostShoot`), distinct from the dormant per-shot `PostShootContext` — fired ONCE per shoot
  action by a new `PostShootStage`. `PostShootStage` calls `EvaluateAll((unit, ERuleSeat.Actor))` →
  `OperationApplier.ApplyTokenOperations` + `OperationExecutor.Execute(…, new GameOperationServices(…))`
  (the `DeployUnitStage` chain). Wired into `ShootStage` as the **shared convergence point**: BOTH shoot
  exits — `DetermineCanKeepShootingStage.ToFinishShooting` ("fired all weapons") and
  `ChooseRangedAttackStage.OnNoValidShots` ("no further valid shots") — now route through `PostShootStage`
  before `OnFinishedShooting`, so the move is offered exactly once per action regardless of exit. First rule:
  **Harassing** = passive `HookEntry(Shooting_OnPostShoot, Always, TriggeredMove(3", IsOptional:true))`,
  registered in `All`. Reuses the live `Effect.TriggeredMove` → `MoveUnit` self-move (player declines via a
  zero move). Tests: `Harassing_AtPostShootHook_RepositionsTheUnit` + `NoPostShootRule…ProducesNoOperation`
  added to `TriggeredMoveRuleIntegrationTests`. Suite 874/0, app build clean, headless exit 0.
  **Forks settled:** (1) per-action context — built `PostShootActionContext`, NOT reused per-shot; (2) melee
  seam — DEFERRED (see below); (3) move lifetime — `ThisAttack`/optional, no once-per-activation gate.
  **Deferred (explicit, not silently cut):**
  - **Melee half** — Harassing's "after being attacked in melee" move (`Melee_OnPostMelee`) is NOT wired;
    the rule fires on the shooting seam only. Needs a melee post-combat seam (the dormant `Melee_OnPostMelee`
    = 103, fired after strike + strike-back + morale + consolidate). Follow-up slice.
  - **Other family members** — Hit & Run Fighter (melee-only), Hit & Run (both), Guerrilla: blocked on the
    melee seam / are data once it lands.
  - **Per-shot Limited fire** — still dormant; left untouched. If ever wired it must use a per-shot/per-weapon
    hook, NOT `Shooting_OnPostShoot` (else a post-shoot HookEntry like Harassing would fire per shot).
  - **Known minor over-trigger:** a Harassing unit that takes the Shoot action but has zero valid shots
    reaches `OnNoValidShots` → still offered the optional 3" move. Harmless (optional; only affects units
    carrying a post-shoot rule) and arguably unreachable if Shoot is gated on a valid target; chosen over
    under-firing the legit "fired some weapons, then ran out of targets" mid-sequence case.
- 2026-06-28: **Hit & Run / post-combat move (#5) — DESIGN NOTES (superseded by the BUILT entry above for the
  shooting seam; melee seam still pending).**
  Picked it up as a "fire a dormant hook + run the live `TriggeredMove`" reuse; it needs real design. Findings:
  - **Hooks exist but are DORMANT.** `EHookID.Shooting_OnPostShoot` (=80) and `Melee_OnPostMelee` (=103) are
    defined but never fired in production (referenced only in tests + a `TokenClearTrigger` that clears at
    `OnPostShoot`). "The trigger is missing" — must be fired.
  - **`PostShootContext` exists but is the WRONG shape.** It's `(IUnit Attacker, IUnit Target)` — per-SHOT,
    built for Limited (mark the weapon used vs a target); used only at `Tests/SpecialRuleTests.cs:960`. Hit &
    Run needs a per-ACTION fire (once after the unit's whole shooting action). Firing the move off a per-shot
    hook would **double-fire** (once per target). → Build a separate minimal **per-action** context, e.g.
    `PostShootActionContext(IUnit Unit)` mirroring `PreMoraleTestContext`, distinct from the per-shot one.
  - **Shooting seam.** `ShootStage.cs`: `DetermineCanKeepShootingStage.ToFinishShooting` binds to
    `onFinishedShootingEvent`. Insert a new child `PostShootStage` between them. It fires the per-action hook,
    runs `GameContext.RuleEvaluator.EvaluateAll(ctx, (unit, ERuleSeat.Actor, null))` → ops, then
    `await OperationExecutor.Execute(ops, new GameOperationServices(GameContext))` (the `DeployUnitStage`
    pattern). Acting unit = `ICombatActionContext.AttackingUnit`. Hit & Run is PASSIVE (a `HookEntry`, not an
    `ActivatedAbility`) → use `EvaluateAll`, not `GatherOffers`.
  - **Melee seam.** `Melee_OnPostMelee` after the melee fully resolves (strike + strike-back); find the melee
    completion point. Deferrable — do shooting first, note melee as a follow-up (don't silently cut it).
  - **Reused primitives (all live).** `Effect.TriggeredMove(MaxInches, IsOptional)` → `InvokeTriggeredMove` →
    `GameOperationServices.MoveUnit` (raises `DefineMovementPathRequest` to the unit's owner — correct for a
    self-move). Use `IsOptional: true` (player may decline the ~3" move). Test pattern:
    `TriggeredMoveRuleIntegrationTests.Vanguard_ThroughSeam` + a `CannedMovePathRequester`.
  - **Rules to author** (in `CoreRuleCatalog.All`): Hit & Run Shooter = `HookEntry(Shooting_OnPostShoot,
    Always, TriggeredMove(3, IsOptional:true), …)`; Hit & Run Fighter = melee; Hit & Run = both; Harassing /
    Guerrilla = same move-after-attack family.
  - **Watch:** `TokenClearTrigger` clears at `Shooting_OnPostShoot` (`Rules/Foundation/TokenClearTrigger.cs:62`,
    Limited's "used this shoot" marker). The per-action Hit & Run fire must stay DISTINCT from any future
    per-shot Limited fire so they don't cross-trigger.
  - **Forks to settle first:** per-action context (recommended) vs reuse per-shot; melee seam; the move's
    lifetime/once-per-activation; whether to wire the per-shot Limited fire too (separate effort — leave dormant).
- 2026-06-28: **#14 enemy mark/tag primitive — slice 5** (engine `5ef5443`). Implements the "pick an enemy,
  the next friendly to attack it gets rule X (then it's used up)" spell family. `Effect.MarkTarget(rule)`
  drops a `TokenType.Mark` (rule-name payload) on the picked enemy via the normal cast path. The FIRST
  attack into a marked enemy CLAIMS it at `DetermineHitRollStage` (the shared first rule-eval point of every
  shoot/melee attack): the marked rule transfers to the attacker as a one-attack grant the read-back applies
  across the attack's hooks, and the mark is removed — **spent by the attack itself, dice-independent**
  (the corrected semantics, after the user flagged that consume-on-fire was wrong: a no-6 Furious attack
  must still spend the mark). Closed a **latent gap** found en route — `AttackEnd`-lifetime tokens were
  never swept (`TokenClearService.ClearsAtHook` had no case); added `ClearAttackEndTokens`, retired lazily
  at the attacker's next claim. `TestGameContext` gained an optional resolver; end-to-end claim+consume
  test (mark→Precise→threshold 3, second attack→4). Suite 872/0, build clean, headless exit 0. Deferred:
  Indirect-as-a-mark (its LoS-ignore is needed at the occlusion check, *before* the claim seam) — one niche
  spell, stays skipped. Unlocks ~20 mark spells (Unstoppable-when-shooting ×12, Relentless ×3, etc.) once
  authored. **The marked rule must exist in the catalog** — most do now (slices 1–4); the rest are data.
- 2026-06-28: **Combat-kind-scoped grants — slice 4: "when shooting" save-side rules** (engine `7b12055`).
  Extended #093's combat-kind condition to the **save context**: `SaveRollCompleteContext` now carries
  `IsMelee` (implements `IHasCombatKind`), threaded from `AssignWoundsStage`'s metadata — the mirror of the
  hit-side threading — so `Not(IsMelee)` gates at the save hook. On that, authored **Unstoppable when
  shooting**, **Shred when shooting**, **Bane when shooting** (each = the base effect + `Not(IsMelee)`).
  Registered in `All`; 6 gate tests (shooting fires / melee doesn't) across the wound-ignore, Shred, and Bane
  fixtures. Suite 870/0, build clean, headless exit 0. Unlocks the biggest #034 skip bucket (Unstoppable ×12
  / Shred ×6 / Bane ×3 = ~21 grant spells). The "in melee" mirror is the same with `Condition.IsMelee()`;
  hit-side combat-kind variants (Rending/Crack in melee) already work via the hit context. See #093.
- 2026-06-28: **Pure-data conferred rules — slice 3: combat reuse-rules (pure-data set COMPLETE)** (engine
  `adfb8c1`). Three rules reusing existing machinery: **Lacerate** (Bane's Defense-6 save-reroll, minus the
  Regeneration-ignore), **Crack** (Rending's AP-on-unmodified-6 at AP(+2) = -2 to save), **Counter-Attack**
  (Counter's strikes-first-when-charged facet alone, unit-scoped). Tests added to the Bane/Rending/Counter
  fixtures. Suite 864/0, build clean, headless exit 0. **The "expressible today" pure-data list (line 24) is
  now authored** (12 rules across slices 1–3). What remains in Part 2 needs new bits, not pure data:
  Guarded/Reinforced (charge-start distance), Fortified/Reinforced (AP-reduction-with-floor), Resistance
  (spell-source wound condition), the faction "Boost" rules (author each effect on #100 #3's conditions),
  and the combat-kind-scoped grants (#093).
- 2026-06-28: **Pure-data conferred rules — slice 2: movement self-modifiers** (engine `7593de4`). Five
  `MovementBonus` rules mirroring Fast/Slow: **Agile** (+1 Advance, +2 Rush/Charge), **Quick** (+2 all),
  **Rapid Advance** (+4 Advance), **Rapid Rush** (+6 Rush), **Rapid Charge** (+4 Charge). Registered in
  `All`; 5 `MovementRuleIntegrationTests`. Suite 861/0, build clean, headless exit 0. Remaining clean
  pure-data: Lacerate (reroll Defense 6s — mirror Bane), Crack (AP(+2) on unmodified 6 to hit — mirror
  Rending), Counter-Attack (Strikes-first-when-charged — mirror Counter's StrikeFirst facet).
- 2026-06-28: **Pure-data conferred rules — slice 1: hit-roll modifiers** (engine `643633a`, branch
  `100-conferred-rules-data`, fresh off master since `100-special-rule-primitives` is stale/already-merged).
  Authored four Tier-A rules from the "expressible today" list (line 24) against existing primitives, no
  engine work: **Evasive** (-1 to hit vs the bearer, all attacks, Subject seat), **Melee Evasion** (same,
  `IsMelee`-gated), **Precise** (+1 to hit, Actor), **Good Shot** (+1 to hit shooting-only, `Not(IsMelee)`).
  All ride the shared `Shooting_OnHitRollModifier` sink (which `DetermineHitRollStage` fires for melee +
  shooting alike). Registered in `CoreRuleCatalog.All`; 7 new `HitRollRuleIntegrationTests`. Suite 856/0,
  build clean, headless exit 0. Unlocks the units carrying these + the spells that grant them (Evasive ×12 /
  Melee Evasion ×6 in #034's skip list). **Next pure-data slices:** Lacerate/Crack (reroll/AP-on-6, mirror
  Bane/Rending), the movement family (Agile/Quick/Rapid Advance/Rush/Charge — `MovementBonus`), Precise/Good
  Shot done. Deferred (not pure data — need new bits): Guarded/Reinforced (">9\" when charged" = charge-start
  distance, not current), Fortified/Reinforced (AP-reduction-with-floor), Resistance (ignore-wound with a
  spell-source distinction).
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
