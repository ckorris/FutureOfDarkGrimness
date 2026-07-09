# 197 — Faction rule coverage, part 2: engine primitives + the scope-mismatch bug

**Status**: in progress (slice 0, the ">9in shot or charged" gate, and P5a DONE 2026-07-09; later slices still carry their own forks)
**Related**: #196 (data-only half of the same audit), #100 (primitive catalog — this item is its corpus-wide successor), #102 (range/terrain threading; defensive Darkborn residual), #034 (spells), #042, #093, `SpecialRulesAudit.md`

## Goal

The other half of the faction-rule coverage audit: **97 dead rule names (942 references) that cannot be
authored as data** because the primitive, hook, or stage wiring does not exist — plus one confirmed
regression where *already-implemented* rules never attach (slice 0, 157 references).

Done = each slice below either ships its primitive with an integration test mirroring the nearest
existing `*RuleIntegrationTests`, or is explicitly re-deferred with a recorded reason. This is an
umbrella; expect it to fragment into per-primitive slices as it is picked up, exactly as #100 did.

## Why this half needs Opus (and #196 does not)

- **It edits the `FutureOfDarkGrimness` submodule**, which is read-only by default (CLAUDE.md). Every
  slice needs explicit authorization, submodule-first commit cadence, and cross-repo pointer bumps.
- **It invents vocabulary.** New effect kinds, new operations, new consumers, dormant hooks fired for the
  first time — each needs a `RuleOperation`, an applier that actually executes it, `RuleJson` derived-type
  registration, `HookContextCatalog` capability wiring, and `RuleFireLint` support. Getting any one of
  those wrong reproduces the Breath Attack failure (validates, registers, does nothing).
- **It contains genuine design forks** (marked FORK below) that must be surfaced with tradeoffs and
  signed off before building, per CLAUDE.md.
- **It has a live invariant to respect.** Dice: modifiers must be applied as a threshold shift on
  `RollDecisive`, never as a post-roll adjustment, or probabilistic (histogram) mode silently diverges
  from realistic mode. Slices P10 and P15 both introduce new dice-driven mechanics and are the two most
  likely places to break it.
- **Its rules interact.** Marker-scaled magnitude (P13), the round/destroy triggers (P5), and the
  spend-for-bonus markers (P14b) are one coupled family; building them independently produces three
  incompatible marker mechanics.

## Slice 0 — the scope-mismatch bug — **DONE 2026-07-09**

**157 references to rules that are fully implemented and never attached.** `ArmyListRuleResolution.ResolveForScope`
refuses a rule whose `SpecialRuleDefinition.Scope` differs from its attachment site, warns, and returns null.
The catalog and the imported corpus disagreed about scope for ten rules:

| Rule | Catalog scope | Corpus attaches at | Refs | Books | Outcome |
|------|---------------|--------------------|-----:|------:|---------|
| Precise | Unit | Weapon | 64 | 10 | re-scoped to Weapon |
| Thrust | Unit | Weapon | 62 | 19 | re-scoped to Weapon |
| Strafing | Unit | Weapon | 12 | 9 | **deferred — own slice, see below** |
| Bane in Melee | Weapon | Unit | 8 | 2 | routed onto the unit's weapons |
| Shred when Shooting | Weapon | Unit | 3 | 3 | routed onto the unit's weapons |
| Rending in Melee | Weapon | Unit | 3 | 2 | routed onto the unit's weapons |
| Unstoppable in Melee | Weapon | Unit | 2 | 2 | routed onto the unit's weapons |
| Reliable | Weapon | Unit | 1 | 1 | placed on its target weapon |
| Takedown | Weapon | Unit | 1 | 1 | placed on its target weapon |
| Shred in Melee | Weapon | Unit | 1 | 1 | routed onto the unit's weapons |

**145 of the 157 now attach.** A further **23 `Precise` references were resolving but landing on the wrong
weapons** — see the targeting fix below; those are a correctness win the reference count doesn't show.

### What shipped

- **`Precise`, `Thrust` re-scoped to `Weapon`** (`CoreRuleCatalog`). Both are pure `Shooting_OnHitRollModifier`
  / `OnHitRollComplete` roll modifiers, and weapon scope is where the corpus puts them (core `Rending` and
  `Indirect` are weapon-scoped on the same hooks).
- **`ListCompiler` honours `UpgradeSection.Targets`.** A targeted `Upgrade`/`PickN` option whose gained wargear
  carries weapon-scoped rules attaches them to the named weapon and to nothing else, splitting the
  `WeaponFileEntry` when the upgrade buys fewer copies than the unit carries (`SameProfile` already keys on
  `SpecialRules`, so the split entries never re-merge). Rules placed this way are excluded from the
  item-rule fold. When the target matches no weapon the unit currently carries — a Scope bought without the
  carbine it upgrades — nothing is placed and the rule falls back to the unit path, preserving the old
  behaviour rather than silently dropping a rule the player paid for.
- **`GameBootstrap` routes what's left.** A unit-level name resolving to a `Weapon`-scoped definition attaches
  to every weapon the unit carries. This is the untargeted wargear case ("Toxic Cysts (Bane in Melee)"),
  where a unit-wide upgrade is exactly the intent, and the rules' own `isMelee` / `not(isMelee)` gates pick
  the right weapons at dispatch. A weapon rule on a weaponless unit warns instead of vanishing.
- **`ArmyListRuleResolution.ResolveAnyScope`** splits name + arity resolution from the scope gate. The gate
  still guards the weapon-level path, where a unit rule genuinely has nowhere to go.
- **Tests.** `WeaponScopedWargearRoutingTests` (engine) asserts *which* weapon carries *which* rule, not that
  a warning stopped — an implementation that sprayed every rule onto every weapon would satisfy the weaker
  assertion and reintroduce the exact bug the targeting fix prevents. Both routing paths were mutation-checked
  (disable the code, watch precisely the right tests go red). `BookRuleScopeTests` (app) walks all 47 shipped
  books and fails on any unit-scoped rule named on a weapon, with a reasoned allowlist that also fails when a
  listed rule starts resolving cleanly. It carries the end-to-end Sniper Drones assertion.

### Decisions

- **Honour `targets`, don't hand-edit `DAOUnion.fdgbook`.** The 2026-07-09 sign-off called for moving
  `Reliable`/`Takedown` onto the Pulse Rifle as book data. Building it surfaced that the book schema already
  records the target (`targets: ["Pulse Rifles"]`), and that once `Precise` became weapon-scoped **23 more
  references joined the same class**: `Scope (Precise)` on `targets: ["Master Marksman Carbine"]`, on units
  that also carry a `CCW` and a `Flamer Pistol`. Attach-to-all would have put `Precise` on the melee CCW —
  the owner's ruling ("a weapon rule applies only when *that* weapon fires") violated at 25 refs, not 2.
  Teaching the compiler to read `targets` fixes all 25, needs no book edits, and survives a re-import, so
  there is nothing to record against the OPR importer. `ListCompiler.TargetMatches` already handled the
  plural-label matching ("Pulse Rifles" -> "Pulse Rifle").
- **Per-weapon dispatch needed no refactor.** `RollToHitStage` passes the firing weapon into
  `RuleParticipant.Actor(attacker, metaData.WeaponType, ...)`, so a weapon-scoped rule is evaluated only for
  the weapon actually attacking. The 157 refs were lost at *attach* time, before dispatch ever saw them.
- **The `Precise` refs were never dropped, only mis-aimed.** Unit-scoped `Precise` buffed every weapon a unit
  fired, melee included (its hook is the shared hit-roll sink). That was pre-existing wrongness, not something
  slice 0 introduced — but it is fixed now rather than preserved.

### Deferred out of slice 0: `Strafing` (12 refs, 9 books)

**The signed-off premise was false.** The plan said the importer wrongly emitted `Strafing` on the weapon and
the book data should move it to the unit. Checked against the source: the rule is weapon-scoped — the mid-move
attack is made *with the weapon carrying the rule*, and that weapon may be used no other way. All 12 references
sit on bomb weapons (`Pulse Bombs`, `Void Bombs`, `Cluster Bombs`, ...). The books are faithful; the **catalog**
is the approximation — it deals a hardcoded 3 hits, at unit scope, and never models the weapon restriction.

This cannot be a scope flip. `Strafing`'s fly-over permission is a `Movement_OnMoveThroughEnemy` passive read
by `MovementRuleQueries.CanMoveThroughEnemies`, and movement hooks never consult weapon rules. Making it a
weapon rule needs: (a) movement-hook access to the bearer's weapons, (b) a mid-move "attack with *this* weapon"
primitive replacing the fixed 3-hit `InvokeDealHits`, and (c) a once-per-activation weapon-use restriction.
Filed as its own slice; allowlisted in `BookRuleScopeTests` with that reason so the guard stays meaningful.

## Slices found while building #196 (2026-07-09)

Small, self-contained primitive gaps #196 hit while authoring data-only rules — each blocks a handful of
refs its family otherwise fully resolved. None found until the family was actually built; recorded here the
same day per the "don't silently cut scope" guardrail.

| Refs | Slice | Needs | Rules |
|-----:|-------|-------|-------|
| 10 | ~~**Distance at the save-roll hook**~~ **DONE 2026-07-09** | Turned out to be one facet of a bigger gap — the whole ">9in shot **or charged**" gate. See the slice write-up below. `SaveRollCompleteContext` now carries `DistanceInches` (+ `IHasDistance`) and the charge launch distance. | Warbound Boost (2), Warbound Boost Aura (5), Infected Boost Aura (3) — all authored |
| 6 | **Reroll threshold parameter** | `RerollCondition.OnUnmodifiedValue` carries no value — `RerollSink.cs` hardcodes it to the unmodified max face (6). F10's Boost variants need "re-roll unmodified 5s *or* 6s". `AddExtraHit`/`AddExtraWound` already parameterize their trigger value per entry (`OnRollValue`); `RerollCondition` needs the same, or a second variant with a threshold. **Note:** once built, these two must be authored as the *increment* (re-roll 5s only) — their base already re-rolls the 6s. See the Boost-composition rule below. They also need `attackedFromOverInches` (already built). | Mischievous Boost Aura (4), Scrapper Boost Aura (2) |
| — | ~~**`RuleFireLint` operation-consumption check**~~ **DONE 2026-07-09** | `IsOpConsumedAtPassiveHook` — the passive twin of `IsOpHandledAtAbilityHook`, keyed on the operation's *payload* where that decides consumption (`ApplyRollModifier(Save)` is read at the hit-complete hook; `(Hit)` is not). Unmapped pairs report as unconsumed, so drift fails loudly. Only flags an entry whose *entire* output is ignored. Whole core catalog (123) and supplement (148) pass unchanged; reverting `Changebound` to its shipped hook turns exactly that rule red. Engine `a2304fb`. Still not covered (WorkItems/166): whether the consumed value is used *correctly*, and what several rules *sum to* — `BoostRuleCompositionTests` covers the latter for the rules it names. | (no refs — an authoring-safety gap) |
| 7 | **`moraleTestThen` outside spell casting** | `Effect.MoraleTestThen.Apply()` is an intentional no-op — `CastSpellStage` special-cases the effect before calling `Apply()` and runs the morale-test-then-branch itself. None of the five generic ability-offering stages (`ChooseActionStage`, `PreAttackStage`, `StrafingStage`, `DeterminePlayerTurnStage`, `DeployUnitStage`) do the same, so a plain `SpecialRuleDefinition` activated ability using it is a genuine no-op in play (confirmed by `RuleFireLint`, not assumed). Wire `MoraleTestThen` into the generic ability path, or add a non-spell "morale test, conditional consequence" primitive. Both corpus uses are ordinary unit-rule references (`unit.rules`), not spell-list entries, so modelling them as `SpellDefinition`s instead would not fix the corpus. | Mind Control (4), Fatigue Debuff (3) |
| 3 | **Vengeance** | "Place N markers on the unit that destroyed this one, N = models with this rule in this unit at game start; friendly units get +N to hit where N is the marker count on the target." Needs two things that don't exist: a magnitude source for "count of models with rule X in the bearer unit" (`ValueSource` only has `Literal`/`Arg`), and marker-scaled roll magnitude (this is P13, already tracked below — Vengeance can piggyback on P13 once it lands, but still needs the model-count source on top). | Vengeance (3) |

## Slice: the ">9in shot or charged" gate — **DONE 2026-07-09**

**Owner sign-off (2026-07-09):** the 9" measures the *distance to the target when the charge is declared*
(not the path length travelled), expressed as **one condition** rather than a hand-composed `Or()`.

Six Boost rules and six defensive rules share the wording "*when it shoots **or charges** enemies over 9\"
away*" / "*when units ... are shot **or charged** from over 9\" away*". `Condition.DistanceGreaterThan`
reads the **live** attacker-to-target distance, and a melee attack resolves in base contact
(`MELEE_RANGE_INCHES_HORIZONTAL == 2`), so a 9" live-distance gate **can never pass in melee**. The charge
arm of all twelve was dead. Max charge is 12", so charges from 9–12" are a real, reachable case — the arm
was unimplementable, not vacuous.

- **`IHasAttackOriginDistance`** (new capability): the distance an attack was *launched* from. Live distance
  when shooting; distance to the defender at **activation start** when charging; 0 for a non-charging melee
  swing, so a strike-back never inherits the charger's launch.
- **Why activation start.** This engine models `Charge` as the *melee attack* and the approach as a separate
  `Move` action (see `ChooseActionStage.GetCanCharge`: Charge is offered when an enemy is already within
  melee range). So the moment the unit "declares a charge and sets off" is its activation start.
  `UnitActionContext.Reset` snapshots the min distance to every enemy unit, because by the time a charge's
  defender is picked the unit has closed and the pre-move geometry is gone.
- **`Condition.AttackedFromOverInches(X)`** reads it. Implemented on `HitRollModifierContext`,
  `HitRollCompleteContext` and `SaveRollCompleteContext` (the last also gained `DistanceInches`/`IHasDistance`,
  which is what the old "distance at the save-roll hook" slice was really asking for).
- Engine `bf6353d` + `e677f1e`; `AttackOriginDistanceTests` (10 cases). Mutation-checked: dropping the charge
  origin, treating *any* charge as qualifying, and letting a strike-back inherit the origin each turn exactly
  one test red.

### Three defects in #196's shipped data, found and fixed here (app-side `27c55c4`)

All three passed `--validate-rules` **and** `RuleFireLint`. The first checks structure; the second proves an
entry *can* fire, not that its operations are *consumed*, nor what several rules *sum to*.

1. **Boost rules double-counted with their base.** The corpus writes a Boost as the *boosted rule*
   ("extra hits on 5-6, instead of only on 6"), but the engine composes base + Boost **additively**
   (`HitInjectionSink`, `RollModifierSink`, `MovementModifierSink` all add; only `WoundIgnoreSink` takes the
   min, which is why F2's Boosts were accidentally right). `Devout` + `Devout Boost` gave **two** extra hits
   on a natural 6. `Lustbound Boost` gave +3"/+9" instead of +2"/+6". Every gate-removal Boost gave −2 to hit
   beyond 9" instead of −1. **45 corpus units carry both a base and its Boost.**
   **Rule going forward: a Boost is authored as the INCREMENT** — only the face, magnitude, or range band its
   base does not already cover. The `Highborn Boost` template I generalized from is correct *only* because its
   increment happens to equal its base.
2. **`Changebound` and `Machine-Fog` were outright no-ops.** A `rollModifier(Hit)` emitted at
   `Shooting_OnHitRollComplete` is never read — the dice are already rolled, and only `Save` deltas fold from
   that hook. Core `Stealth`, the identical defensive −1-to-hit shape, correctly sits at
   `Shooting_OnHitRollModifier`. Both moved.
3. **The charge arm** (above) for `Changebound`, `Primeborn`, `Sturdy`, `Guardian`, `Machine-Fog`, `Guarded`.

`FdgRaylib.Tests/BoostRuleCompositionTests.cs` is the guard: it drives the real shipped supplement through the
real evaluator and the real sinks and asserts the **net** effect a player sees. Against the pre-fix data 9 of
its 11 cases go red, at least one per defect class.

## Slice P5a — activation-choice hook — **DONE 2026-07-09** (154 of 175 refs)

**Owner sign-off (2026-07-09):** label the abilities (not a new `Effect.ChooseOne`); give the choice **its own
request type**; defer `Versatile Defense Aura`.

Four rules read "when this unit is activated, pick one effect: until the end of the activation ...". The hook
they need (`Activation_OnActivationStart`) existed and nothing ever fired it.

- **`ActivationStartStage`** — `MainUnitActionStage`'s new starting child, binding on to `ChooseActionStage`.
  Runs once per activation: every loop-back from Movement/Melee/Shoot returns to ChooseAction, not here.
- **`ActivatedAbility.Label`** — a rule carries one ability per effect, all at the same hook;
  `AbilityOffer.RuleName` cannot tell them apart. The once-per-X `Cost` is keyed on the **rule** name, so
  taking one effect spends the gate for its siblings — exactly "pick one".
- **`ChooseAbilityEffectRequest`**, replying with the chosen option's **index**. Its own request type because
  `docs/ai-agent-plan.md` A4 replaces AI resolvers *one request type at a time*: riding `StringSelectionRequest`
  would force a future agent to take over Choose Action **and** the pre-attack menu at once, and tell them apart
  by sniffing prompt text — which `AiStringSelectionResolver` already does today
  (`if (request.Instructions == "Choose Action")`). Options are plain data: requests cross the wire via
  Newtonsoft, which cannot round-trip an ability's polymorphic `Effect` graph. An in-process agent recovers the
  abilities from the catalog via `RuleName`.
- Chosen effects grant a helper rule with `addRule(ThisActivation)` — the use `Effect.AddRule`'s own doc names.
  **No new effect kind or operation.**
- Engine `df234bc`, `90ba258`; app `6dbd31c`. Verified in play, not just by lint: a headless Dark Prime
  Brothers game logs `Versatile Attack - chose AP(+1)` once per activation.

### Latent defect found and fixed: granted rules' abilities were never offered

`RuleEvaluator.GatherOffers` read only the unit's and its models' **own** rules, while the passive path has
always resolved `RuleGrant` tokens back to definitions via `CollectGrantedRules`. So an aura conferring an
**ability-only** rule granted a token nothing ever read — the Breath Attack failure mode, one level up.
Latent until now (no shipped aura granted an ability-bearing rule); `Versatile Reach Aura` (56 refs) is the
first. Fixed by mirroring the passive path's screening exactly, and mutation-checked.

### Deferred: `Versatile Defense Aura` (21 refs)

Triggers on **deployment or activation** and lasts **until the unit's next activation** — a lifetime
`ELifetime` doesn't have (`ThisRound` is wrong; it must span the opponent's turns). Needs a new lifetime plus a
`TokenClearTrigger` that fires at activation **start** rather than end, and a second trigger hook at deployment.
Its own slice; mixing a token-lifecycle change into a hook/resolver slice was the thing the sign-off avoided.

## Slices — by leverage

Reference counts are corpus-wide (44 books). Primitive numbers are #100's.

| Refs | Slice | Needs | Rules |
|-----:|-------|-------|-------|
| 175 | ~~**P5a** activation-choice hook~~ **DONE 2026-07-09** (154/175) | Shipped: see the P5a write-up above. `Versatile Defense Aura` (21) deferred — needs an until-next-activation lifetime. | Versatile Attack (56), Versatile Reach Aura (56), Watchborn (42) done; Versatile Defense Aura (21) deferred |
| 21 | **Versatile Defense** (out of P5a) | A new `ELifetime.UntilNextActivation` + a `TokenClearTrigger` firing at activation **start**, and a second trigger at `Deployment_OnUnitDeployed`. Everything else (labelled abilities, the choice request) already exists. | Versatile Defense Aura (21) |
| 47 | **Delayed Action** (was P22) | **Not a deploy timing.** "Once per round, if your opponent has more units left to activate than you, this unit may pass its turn instead of activating (may still be activated later)." An activation-ORDER mechanic: a decline-to-activate option at the next-activator seam (`Activation_OnNextActivatorRequested`, which already offers abilities), gated on a live unmoved-unit count comparison, once per round. **FORK:** does declining consume the player's turn-slot (opponent activates twice) or is it free? | Delayed Action (47) |
| 15 | **Teleport** (was P22) | **Not a deploy timing.** A PRE-ATTACK reposition: place this model anywhere fully within 3in of its position on Advance/Charge, or 6in on Rush. Rides `Activation_OnPreAttack` (already an offer site) + a placement resolver; unlike `triggeredMove` it ignores terrain and intervening enemies. `Teleport Aura` (4) is then data. | Teleport (15), Teleport Aura (4) |
| 14 | **Ambush variants** (the real P22) | The only genuine deploy-timing work. `Rapid Ambush` (deployable from round 1 — a new `EDeferTiming`), `Ambush Beacon` (relaxes the >9in enemy restriction for OTHER friendly Ambushers within 6in — a cross-unit deployment constraint), `Ambushing Piercing Shot` (Ambush + AP(+1) during the round it arrives — needs deploy-round state). | Rapid Ambush (4), Ambush Beacon (6), Ambushing Piercing Shot (4) |
| 2 | **Surprise Attack** (was P22) | Infiltrate + "the first time this unit is activated, pick one enemy within 6in in LoS and roll X dice; each 2+ deals a hit with AP(1)". Blocked on **P10**'s dice-pool primitive regardless. | Surprise Attack (2) |
| 96 | **New** reposition-at-activation | Place a unit's models anywhere fully within a rolled D3in (D3+1in for Bounding, 2D3in for Rapid Blink Boost) of their position, when it activates. **The P5a hook now exists**, so this needs a placement resolver + the D3 roll (dice invariant: roll it once, decisively). `Wolfborn` and `Rapid Blink` are **word-for-word identical** — `Rapid Blink` was mis-filed under P22. | Wolfborn (60), Bounding (22), Rapid Blink (8), Bounding Aura (4), Rapid Blink Boost Aura (2) |
| 66 | ~~**P5b** round-start Shaken recovery~~ **DONE 2026-07-09** (66/66) | **The premise was wrong:** `Round_OnRoundStart` is not dormant — `StartOfRoundExtraActionStage.GrantSpellTokens` fires it every round for every living unit (Caster token grants), applying token ops and running executables. So this needed only the effect. New `Effect.ClearTokenOnRoll` -> `InvokeClearTokenOnRoll`, an executable resolved through `IOperationServices`. Rolls with `RollDecisiveFace`, never `Roll(1)` — the outcome is binary, so a histogram would want to remove a *fraction* of a token. Engine `05eb91e`. | Steadfast Aura (28), Battleborn (26), Honor Code (9), Steadfast (3) |
| 60 | **P21** setup-phase re-deploy | Remove + re-place a unit during/after deployment. | Re-Deployment (27), Fanatic (19), Dash Aura (4), Ambush Re-Deployment (4), Dash (2), Mobile Artillery (2), Quick Readjustment (2) |
| 59 | **Darkborn** (#102 residual) | Defensive Darkborn: enemies get reduced range **and** reduced movement/charge vs this unit. Per-target charge-distance debuff does not exist. **Also a naming bug:** the catalog registers `Darkborn (Offensive)` / `Darkborn (Defensive)`; the books reference plain `Darkborn`, which resolves to nothing. | Darkborn (59) |
| 53 | **P15** randomized-branch effect | "Roll one die: 1-3 -> effect A, 4-6 -> effect B", applied per attack. **Must respect the RollDecisive threshold-shift invariant.** | Unpredictable Fighter (26), Unpredictable Fighter Aura (11), Unpredictable (5), Unpredictable Shooter Aura (5), Unpredictable Fighter Mark (3), Unpredictable Shooter Mark (2), Unpredictable Shooter (1) |
| 44 | **P10** dice-pool -> hits / auto-wounds | Generalize `dealHits` to a rolled count and to wounds-without-to-hit. Unblocks the `dealHits.WithRules` resolver seam (#164) too. | Ravage (31), Crossing Attack (8), Storm of Lust (2), Storm of Change (1), Storm of Plague (1), Storm of War (1) |
| 41 | **P13** marker-scaled magnitude | `ValueSource.TokenCount` + effects whose magnitude scales with a marker count. Couple with P5b (round-start markers) and P5c. | Piercing Frenzy (9), Defensive Frenzy (8), Piercing Growth (6), Precision Frenzy (6), Fortified Growth (6), Precision Growth (5), Defensive Growth Aura (1) |
| 28 | **P14b** spend-for-bonus markers | Tag a target with N markers; friendly attackers remove them for +AP/+hit. Distinct from the built `markTarget` (one-shot rule transfer). | Precision Target (7), Piercing Tag (6), Precision Spotter (4), Piercing Spotter (4), Precision Tag (4), Piercing Target (3) |
| 27 | **P11** reflect damage | On-wound-taken / on-death hook + deal-hits-at-attacker. | Retaliate (20), Deathstrike (4), Self-Destruct (3) |
| 24 | **P17** place / restore a unit | Create a unit or restore destroyed models mid-game. Touches deployment + table-state lifecycle + networking sync. | Spawn (14), Reinforcement (4), Reanimation Aura (3), Split (3) |
| 21 | **P23** casting support | Rides #034. Caster-pool sharing, cast-roll modifiers, transfer-on-death. | Spell Conduit (9), Spell Accumulator (7), Caster Group (3), Casting Buff (2) |
| 20 | **P6** deferred debuff token | The debuff mirror of the built `FirstTrigger` buff grant: a one-shot roll penalty on a chosen enemy's next relevant action. | Casting Debuff (8), Morale Debuff (4), Piercing Debuff (3), Defense Debuff (3), Speed Debuff (2) |
| 14 | **P8** apply terrain state to target | Force a Dangerous-terrain test / count as standing in terrain. Builds on `countAsInTerrain` + `ApplyNonMovementTerrainEffectsStage`. | Dangerous Terrain Debuff (11), Difficult Terrain Debuff (3) |
| 12 | **P20** action-permission modifiers | (a) allow shooting after Rush; (b) "strikes last", the inverse of live `strikeFirst`. | Quick Shot Aura (5), Quick Shot Mark (4), Unwieldy Debuff (3) |
| 9 | **P7** morale-outcome override | Convert a failed morale test into a pass, then take unignorable self-wounds. | No Retreat Aura (5), No Retreat (3), No Retreat Buff (1) |
| 7 | **P16** one-shot special-attack injection | Once per game, inject one extra attack with an authored weapon profile. | Takedown Strike (5), Takedown Shot (2) |
| 12 | **Strafing** (out of slice 0) | Make `Strafing` the weapon rule the source says it is: movement-hook access to the bearer's weapons, a mid-move "attack with *this* weapon" primitive replacing the fixed 3-hit `InvokeDealHits`, and a once-per-activation weapon-use restriction. Currently allowlisted in `BookRuleScopeTests`. | Strafing (12) |
| 3 | **P19** reactivate another unit | Generalize the live self-`reactivate` to a chosen friendly unit. | Coordinate (3) |
| 2 | **P12** attack-count producer | A producer at the existing `Shooting_OnPreHitRollCount` seam (seam exists, no producer). Pairs with P13. | Regenerative Strength (2) |
| 98 | **Misc** small primitives | Each is a one-off; triage before building. Several may collapse into P5/P13. | Repel Ambushers (24, enemy Ambush placement constraint), Inquisitorial Agent (20, once-per-game reactivate), Hazardous (15, self-wound on unmodified 1), Extended Buff Range (9), Protection Feat (8) + Aura (1), Instinctive (4, forced action at activation), Speed Feat Aura (4) + Buff (1), Heavy Impact (3, Impact with AP), Grounded Reinforcement Aura (3), Grounded Precision Aura (3), Grounded Stealth (2, "within 1in of terrain" condition), Screened Aura (1) |

## Suggested sequencing

1. **Slice 0** — 157 references of working code, unblocked by one decision. Do it before anything else.
2. **P5b** (round-start) then **P5a** (activation-choice) — P5a alone is 175 refs, the largest single
   engine win, and P5b is the cheapest dormant-hook exercise to prove the pattern.
3. **P13 + P14b + P12 together** — one coherent marker mechanic, or three incompatible ones.
4. **P10** — also retires the `dealHits.WithRules` seam (#164).
5. **P22 / P21** — deployment cluster; share a placement resolver.
6. Then the long tail (P6, P8, P11, P15, P17, P20, P7, P16, P19), Darkborn, and the misc triage.

`#196` can run fully in parallel — it touches no engine file.

## Notes

- 2026-07-09: **P22 re-scoped — it was never one slice.** Reading the source text, its 93 refs are five
  unrelated mechanics, and the largest is not a deploy timing at all. `Delayed Action` (47, half the slice) is
  an activation-ORDER rule; `Teleport` (15+4) is a pre-attack reposition; `Rapid Blink` (8+2) is
  **word-for-word identical to `Wolfborn`** and belongs to the reposition-at-activation slice (now 96 refs);
  `Surprise Attack` (2) is blocked on P10 regardless. Only `Rapid Ambush` / `Ambush Beacon` /
  `Ambushing Piercing Shot` (14) are genuine deploy-timing work. Split accordingly in the table above.
  `Infiltrate Aura` (1) needed nothing — `Infiltrate` was already in the supplement — and shipped as data.
- 2026-07-09: **`RuleFireLint` consumption check + P5b shipped.** The lint now verifies that some stage at a
  passive entry's hook actually *reads* what it emits (`IsOpConsumedAtPassiveHook`, engine `a2304fb`) — the
  hole that let `Changebound`/`Machine-Fog` ship as no-ops. Whole catalog + supplement pass unchanged;
  reverting `Changebound` to its shipped hook turns exactly that rule red. P5b (66 refs, engine `05eb91e`)
  turned out to need no hook work at all: `Round_OnRoundStart` was never dormant. Corpus dead count 864 -> 798.
- 2026-07-09: **P5a shipped** (engine `df234bc`, `90ba258`; app `6dbd31c`). 154 of its 175 refs; corpus dead
  count 1,018 -> 864. Signed off first: labelled abilities, a dedicated request type, defer Versatile Defense.
  Building it exposed a latent engine defect — `GatherOffers` never read granted-rule tokens, so an aura
  conferring an ability-only rule did nothing. Fixed and mutation-checked. Verified in a real headless game,
  not just by the fire-lint.
- 2026-07-09: **The ">9in shot or charged" gate shipped** (engine `bf6353d`, `e677f1e`; app `27c55c4`).
  Signed off first: declared-distance semantics, one condition. Building it surfaced **three defect classes in
  #196's already-pushed data** — Boost rules double-counting with their base (45 units affected), two rules
  that were outright no-ops on the wrong hook, and the dead charge arm itself. All fixed; see the slice
  write-up above. Also closed the old "distance at the save-roll hook" slice (it was one facet of this) and
  authored the four rules it blocked. Filed a new slice: `RuleFireLint` does not check operation consumption,
  which is the hole that let the no-op rules ship.
- 2026-07-09: **Slice 0 shipped.** 145 of 157 refs attach; 23 mis-aimed `Precise` refs corrected. Engine
  commit `8cdca83`. Two deviations from the sign-off, both surfaced before building:
  (a) `Strafing` is a genuine primitive gap, not a data bug — the signed-off "fix the book data" premise
  was false, so it moved to its own slice rather than being quietly re-scoped;
  (b) the `DAOUnion.fdgbook` hand-edit was replaced by teaching `ListCompiler` to honour a section's
  `targets`, because re-scoping `Precise` pulled 23 more refs into the same "wrong weapon" class that the
  hand-edit would have left broken. No book data changed, so a re-import cannot reintroduce either.
- 2026-07-09: Slice 0 signed off. Submodule edits authorized for this item's scope. `Precise`/`Thrust`
  -> `Weapon`; `Strafing` stays `Unit` (book data fixed); weapon-scoped wargear rules attach to the unit's
  weapons, with DAO Union's `Reliable`/`Takedown` moved onto the Pulse Rifle as data. Confirmed while
  scoping: per-weapon dispatch already works, so no hit-roll refactor is needed.
  *(Superseded by the entry above — kept for the record of what was agreed vs what the data supported.)*
- 2026-07-09: Filed alongside #196 from a full-corpus resolution run (the engine's own
  `ResolveForScope` over all 44 books). Corpus totals: 13,870 rule references; 2,342 dead (16.9%).
  Of 216 distinct non-catalog rule names, 204 never resolve. Split: 107 names / 1,243 refs are
  data-only (#196); 97 names / 942 refs need engine work (this item); 10 names / 157 refs are the
  slice-0 scope bug.
- 2026-07-09: Also found — 13 catalog rules are referenced by no book at all. Mostly harmless, but
  `Darkborn (Offensive)` / `Darkborn (Defensive)` are the pair the books try to reach as `Darkborn`.

## Decisions

- **Split from #196 on "does it touch the submodule", not on rule count.** The data half has a closed
  vocabulary and a mechanical pass/fail gate (`--validate-rules` + `RuleFireLint`); the engine half has
  design forks and cross-repo commit cadence. Mixing them would have made the whole thing gated on
  engine authorization.
- **Slice 0 lives here, not in #196**, despite being small: it is a catalog-scope semantics call on
  read-only submodule code, and re-scoping `Reliable`/`Takedown` has a real regression surface.

## Outcome

_(written when the item closes)_
