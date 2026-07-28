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
| 12 | **Per-model rule attribution (Sergeant)** (from F16, 2026-07-22) | OPR common rule `8HWdOwMYcI0p`: "When this model attacks, unmodified results of 6 to hit deal 1 extra hit" — a one-model champion upgrade. `ListCompiler` attaches `RulesGained` to `unit.SpecialRules` and hit rolls fold over the whole unit's pool, so a data definition would over-grant (~10x on a 10-model unit: every model's 6s proc, not just the sergeant's die). Owner ruled 2026-07-22: must apply to the one model only — needs a per-model attachment + a hit-roll seam that scopes an extra-hit effect to the bearer model's own attacks. | Sergeant (12) |
| 11 | **Defense floor (Armor(X))** (from F16, 2026-07-22) | OPR common rule `74RjQ1k41DoO`: "Counts as having Defense X+" — a stat SET with a varying rating. No Defense-side analog of `qualityFloor` exists, and data effects carry fixed authored values (no arg-reading), so one definition cannot cover Armor(3)/Armor(4). Needs a defense-floor effect reading `Arg(0)` (or engine-side stat handling a la Tough). #196 shipped a zero-hook marker-with-arg definition (the Unique/Transport precedent, lint-allowlisted) so the name resolves and the description shows meanwhile. | Armor (11) |

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

### Deferred here, shipped as its own slice — `Versatile Defense Aura` (21 refs)

See **Slice: Versatile Defense** below (DONE 2026-07-23). The deferral was right: it needed a token-lifecycle
change, which is exactly what the P5a sign-off kept out of a hook/resolver slice.

## Capability seam — **DONE 2026-07-23** (no refs; an architecture fix Chris called)

Building Caster Group hit `SpellTargeting.IsCaster` comparing against `CoreRuleCatalog.Caster` — an
identity check standing in for a capability. Chris's ruling: represent "this unit can open the cast
prompt" as an effect a rule declares at a hook, not as a check for a named rule, and **do the same
everywhere else the engine does this**. Two things an identity check cannot do, both of which bit here:

- A SECOND rule conferring the same thing is invisible. `Caster Group` is not `Caster` and can never be
  granted as one (its X is a live model count; granted rules carry no arguments, so `GrantedRules`
  screens them out).
- A capability that depends on LIVE STATE cannot be expressed at all — which the remaining P23 rules
  need, since both read "friendly casters may only use this rule if this unit isn't Shaken".

**The seam.** One hook, `EHookID.Lifecycle_OnCapabilityQuery` + `CapabilityQueryContext`, read through
`CapabilityRuleQueries` (the `RangeRuleQueries`/`SightRuleQueries`/`MovementRuleQueries` pattern). A rule
answers by emitting an `Enable*` operation; nothing applies it, its presence in the queue IS the answer.
One hook rather than one per capability: the operations already discriminate, and being an ordinary hook
each answer respects the entry's `Condition` and rule suppression.

**Converted (full audit of the codebase):**

| Site | Was | Now |
|---|---|---|
| `SpellTargeting.IsCaster` | `Definition == CoreRuleCatalog.Caster` | `Effect.EnableCasting` (Caster, Caster Group) |
| `TransportUtilities.IsTransport` / `GetCapacity` | `Definition == Transport` + read `Arg(0)` | `Effect.EnableTransport(ValueSource)`, capacity riding the answer |
| `ReDeploymentStage.HasReDeployment` | `Definition == ReDeployment` | `Effect.EnableReDeployment` |
| `ChooseActionStage` Disembark/Embark/Teleport routing | `offer.RuleName == <name>` | `offer.Ability.Effect is Effect.X` |

The routing one was the most galling: all three already carried a marker `Effect` that exists for
exactly that purpose, and **the same method already routed `StormOfHits` by effect** — the three name
checks were the outliers, not the pattern.

`IsTransport`/`GetCapacity` now take a `RuleEvaluator`, threaded through the AI (`TacticalAnalysis`,
`MacroActionGenerator`, `TacticianActivationResolver`, `TacticianPlanner`, `TacticianRangedAttackResolver`),
the deploy/embark stages, `SpilloutExecutor`, and app-side `TableTooltipOverlay` (which builds its own
read-only evaluator, as it already built its own resolver).

**Deliberately NOT converted**, with reasons, so this isn't a silent cut:
- **`Hero`** (`HeroJoinResolver`, 3 sites). Army-BUILD-time structural marker, resolved before any rule
  dispatch exists, and deliberately hook-less per #006 slice F. One of the three sites isn't even a
  capability check — it's "skip the marker itself when relocating the hero's rules onto its model".
  Converting would mean threading a rule evaluator into army construction for a marker no second rule
  confers.
- **`Condition.UnitHasRule` / `TargetHasRule` / `TargetSelector.RequiredRule` / `Effect.IgnoreRule`.**
  These name a rule because the CORPUS TEXT does ("vs units with Tough", "ignores Regeneration").
  Authored data, not an engine assumption.
- **`ListValidator`, `DefaultBaseEstimator`, `ArmyForgeScreen.IsCaster`.** Book/roster data, where no
  rule graph exists yet.

Tests: `CapabilityRuleQueriesTests` (9) confer each capability from a rule that is NOT the core one —
exactly what an identity check cannot see, and what tests over the core rules can never catch — plus
live-state gating, capability isolation, largest-capacity-wins, and the by-effect menu routing under a
differently-named rule. Engine 1962/1962, app 491/491.

**Mutation testing found two holes in my own tests**, both the same shape: the query-level tests called
`CapabilityRuleQueries` directly, so reverting `IsCaster` (and later `HasReDeployment`) to their identity
checks left everything green while the STAGES silently lost the behaviour. Added
`TheStagesAskForTheCapability_NotForTheCasterRule` and
`ARuleOtherThanReDeployment_ThatConfersTheCapability_AlsoGrantsRedeploys`; both mutations now red, as do
reverting the menu routing and reverting `IsTransport`.

**Noted, not fixed:** the CLI army-file prompt loops forever on EOF when the file fails to load (a stale
probe army produced a 5.8 GB log before the timeout). It should abort at EOF like every other resolver.
Worth its own item.

## Slice: P23 Spell Conduit — **DONE 2026-07-23** (9 refs; P23 DONE 19/19)

> "Casters within 12" that are from other friendly units may cast spells as if they were in this model's
> position, and get +1 to casting rolls when doing so. Friendly casters may only use this rule if this
> unit isn't Shaken."

**Same capability shape as Accumulator, different payload.** The gating half is identical - a capability
entry at `Lifecycle_OnCapabilityQuery`, 12", `if !Shaken` - so it was settled before building. What is new
is that the offer changes a property of the whole cast (where range and line of sight are measured from,
plus the roll) rather than lending a resource. `Effect`/`RuleOperation.EnableSpellRelay(range, bonus)`,
read by a new `SpellRelay` helper. `SpellPurse`'s neighbour scan was lifted into a shared `CastSupport`
(both rules are worded "... other friendly units within 12" ...", so they share the eligibility test).

**A relay moves the spell, not the caster.** Only the origin point of range/LoS changes (including which
models are discounted as blockers - the relay's own, not the caster's). Affinity is still judged against
the caster (a Friend spell means the caster's side, however relayed), the #103 assist scan still measures
18" from the casting unit, and the caster's own eligibility (has it attacked, is it embarked) is
unchanged. The narrow reading, and the one the wording supports.

**Design fork (owner call): no origin prompt; the origin is derived, made visible.** A relay origin is
never worse than the caster's own - it only adds reach and adds +1 - so the only real question is which
origin reaches the targets the player picked. `CastSpellStage` therefore offers the UNION of every
origin's legal targets (`RelayedTargeting`), narrows the viable origins as targets are chosen, and casts
from a relay if one still covers them all, relays preferred. The owner's concern with "it just happens"
was answered by making the relay visible at every step rather than by adding a prompt:

- the spell picker carries a relay note ("Synaptic Relay relays: cast from its position for +1 ...") -
  `ChooseSpellRequest.RelaysInRange`, rendered by both resolvers;
- every target row names the origin it would use ("Dummies (via Synaptic Relay, +1)") - the honest place
  for it, since at spell-pick time the origin is not yet decided and this is exactly what a player needs
  to aim for the bonus;
- the cast is announced ("... casts Sky Blaze through Synaptic Relay (+1)") and the roll breakdown lists
  "relay +1" first, as the modifier the player did not ask for by name.

With no conduit on the table there is one origin (the caster's own) and every path degrades to the exact
prior single-list behaviour.

Data (app-side, supplement): `Spell Conduit`, no argument, embedded into AlienHives, BlessedSisters,
HighElfFleets, HumanInquisition, SaurianStarhost, SoulSnatcherCults. Corpus ledger 252 -> 243.

Tests: `SpellConduitRuleIntegrationTests` (15) - who relays (nearby friendly yes; the conduit itself no;
enemy no; past 12" no; Shaken no and recovering yes; destroyed no); reach extended to a target the caster
can't reach; affinity still judged from the caster; the +1 on a relayed cast and NONE on a self-reached
one (same board, same dice); relays-preferred when both origins reach; the no-conduit degrade; and the two
visibility strings (picker note + per-target origin). `SpellConduitShippedDataTests` (5) pin the shipped
JSON: one capability entry and nothing else, the 12"/+1 values, the Shaken condition, an end-to-end relay
offer, and that every book referencing the rule embeds it. Engine 1991/1991, app 504/504.

Mutation-checked, seven mutations each redding exactly its own test: dropping the relay bonus from the
roll, measuring targeting from the caster instead of the origin, the ChooseAction reach ignoring relays,
dropping the other-unit gate, flattening the target label, and taking the last viable origin instead of
the first (relays-preferred). That last mutation initially survived - my tests only covered
single-origin-reachable targets - so `WhenBothOriginsReachTheTarget_TheRelayIsPreferredForItsBonus` was
added to close it.

In play (headless scenario probe): an enemy 24" from the caster (outside its own 18" spell) but 14" from
the conduit made Cast available where it otherwise would not be, and the log carried every visibility
surface: the picker note, "Dummies (via Synaptic Relay, +1)", "casts Sky Blaze through Synaptic Relay
(+1)", and "rolled 2, needed 3+ (base 4+, relay +1)".

## Slice: P23 Spell Accumulator — **DONE 2026-07-23** (7 of P23's 19 live refs)

> "Gets X accumulator tokens at the start of each round, but can't hold more than 6 tokens at once.
> Casters from other friendly units within 12" may spend this model's accumulator tokens as if they were
> their own spell tokens. Friendly casters may only use this rule if this unit isn't Shaken."

**No new hook and no new stage** — this is the first slice to be built entirely on the capability seam
from Caster Group, which is what that seam was for. The two halves are plain data:

| Half | Authored as |
|---|---|
| the pool | `grantToken(AccumulatorTokens, arg0, manualOnly, maxTotal 6)` @ `Round_OnRoundStart` |
| who may draw on it | `enableSpellLending(AccumulatorTokens, 12)` @ `Lifecycle_OnCapabilityQuery`, `if !Shaken` |

The Shaken clause is the payoff predicted when the seam was built: a `Condition` on the entry, re-asked
on every ask, not a special case in the cast stage. The cap rides `Effect.GrantToken.MaxTotal`, which
already existed for the #100 #13 marker family — the rule states its own 6 rather than borrowing the
engine's `MAX_SPELL_TOKENS`, so the two can differ.

**Its own token type, and that is load-bearing.** `TokenType.AccumulatorTokens`, not `SpellTokens`. The
corpus puts the Change Boon upgrade on units that are themselves casters, and the rule says *other*
friendly units — one shared type would let the holder spend its own pool. It also keeps a full pool from
making its holder look like a caster to the #103 assist scan.

**New: `SpellPurse`** (beside `SpellTargeting`) — "everything this caster may spend as a spell token right
now". Five sites used to read `unit.Tokens.GetTokenCount(SpellTokens)` directly and now ask it, because
"as if they were their own spell tokens" means *every* use a spell token has: the spell's cost, the #244
self-boost, and a #103 assist on someone else's cast. `ChooseActionStage`'s Cast gate and both AI
affordability checks (`MacroActionGenerator`, `TacticianPlanner`) ask the same purse, so the menu, the
picker and the planner cannot price a spell differently.

**Own tokens are spent first, then lenders in table order.** A caster's own tokens are usable by nobody
else while a lender's pool is shared with every friendly caster in range, so draining the restricted
resource first leaves the team the most options — and a caster that can already afford its spell behaves
exactly as it did before accumulators existed. Stated as a decision, not an accident; a one-line change if
play argues otherwise.

**Deliberately simplified, recorded rather than built:** the rule says "this *model's* accumulator tokens"
and the pool is held by the UNIT — the same accommodation Caster Group needed, and for the same reason
(there is no per-model token pool). No corpus entry puts two Change Boons on one unit, so nothing
observable rides on it.

Data (app-side, supplement): `Spell Accumulator`, `engineArgumentCount: 1`, embedded into
HumanInquisition + WormholeDaemonsofChange. Corpus ledger 259 -> 252.

Tests: `SpellAccumulatorRuleIntegrationTests` (14) — the pool fills / caps at 6 / carries over / is not
spell tokens; who may draw on it (nearby friendly yes, the holder itself no, enemy no, past 12" no,
Shaken no and recovering yes, destroyed no); own-before-borrowed, several lenders, one caster's spend
leaving less for the next; plus two STAGE-level tests so reverting either stage to its own pool reds.
`SpellAccumulatorShippedDataTests` (6) pin the shipped JSON: token type, `MaxTotal`, `ManualOnly`, the
12" range, the Shaken condition, that both halves name the SAME pool (authored separately — funding one
pool and lending another would do nothing at all), and that every book referencing the rule embeds it.
Engine 1976/1976, app 498/498.

Mutation-checked, six mutations, each redding exactly its own test: reverting the `ChooseActionStage`
gate, reverting `CastSpellStage`'s price+spend, dropping the other-unit / friendly / range gates, and
spending borrowed tokens before own.

In play (headless scenario probe, `--trace-rules`): the capability fires as
`Spell Accumulator(3) at Lifecycle_OnCapabilityQuery/Actor: fired -> EnableSpellLending`, round start
fires `GrantTokenToUnit`, Cast goes from *"Not enough spell tokens (0)"* to offered once the pool exists,
the boost prompt correctly reads 1 affordable (4 in the purse minus a 3-token spell), and the cast logs
**"Psy-Seer draws on nearby accumulators to cast Sky Blaze (Change Boon Host lends 2)"** — 1 of its own
first, then 2 borrowed.

**Found while building: two stale lint allowlist entries.** `RuleFireLint`'s capability arm listed only
`EnableCasting`, so `EnableTransport` / `EnableReDeployment` would have reported as unread had they been
each rule's first producing entry; widening it revealed that `Transport` and `Re-Deployment` were still
allowlisted in `RuleCatalogLintTests` as "engine-marker: detected by name", which stopped being true when
they gained capability entries. Both entries removed — their absence is now the assertion.

**Noted, not fixed:** `RuleFireLint.Check` returns at the FIRST passive entry that produces operations,
so a rule's later entries are never lint-checked. That is consistent with what it claims to test ("does
this rule fire at all"), but it means a dead second entry on a live rule is invisible. Worth its own item
if the per-entry check is wanted.

## Slice: P23 Caster Group — **DONE 2026-07-23** (3 of P23's 19 live refs)

> "Pick one model with this rule in this unit to have Caster(X), where X is the total number of models
> with this rule in this unit. If the model is killed, pick another to be the new caster, and transfer all
> spell tokens to it. The caster loses all unspent spell tokens at the end of the round."

**Two of those three sentences were already true and needed no code.** Spell tokens are held by the
UNIT, not by a model, so "pick a model to be the caster" and "transfer its tokens when it dies" have no
observable content in this engine - there is no per-model pool to move, and the designation is invisible.
Recorded here rather than built, so a future reader doesn't go looking for the bookkeeping.

**The capability, not the rule.** Casting was detected by `SpellTargeting.IsCaster` comparing against
`CoreRuleCatalog.Caster` - an identity check standing in for a capability. Caster Group confers casting
and is not `Caster`, and can never be granted as one: its X is a live model count, and granted rules carry
no arguments (`GrantedRules` screens argumented rules out). Owner call (2026-07-23, mid-slice) was to fix
the shape rather than special-case it:

- New `EHookID.Casting_OnCastCapability` + `CastCapabilityContext` + `Effect.EnableCasting` ->
  `RuleOperation.EnableCasting`, read by `CastingRuleQueries.CanCast` - the
  `RangeRuleQueries`/`SightRuleQueries`/`MovementRuleQueries` pattern. A capability HOOK, not an event:
  nothing applies the operation, its presence in the queue IS the answer.
- Core `Caster` authors the entry like any other rule; `SpellTargeting.IsCaster` delegates. A second
  caster-conferring rule now needs no engine change at all.
- Two things fall out, both needed by the rest of P23: the answer is **live**, so an entry's `Condition`
  gates the capability (which is how Conduit's and Accumulator's "only if this unit isn't Shaken" clauses
  will be expressed), and rule **suppression** applies - an `IgnoreRule` cancelling casting now works.

**Also new: `ValueSource.RuleCarrierCount`** - living models of the bearer's unit carrying the firing rule.
`ValueSource.Resolve` now takes the whole `RuleInvocation` rather than just the arguments, deliberately as
the ONLY entry point (an arguments-only overload would be the easy thing to reach for and would silently
return a wrong answer for any state-reading variant). `UnitHasGrantedRule` was lifted out of
`Condition.AllModelsHaveThisRule` into a shared `RuleGrantQueries` so the gate and the count cannot
disagree about what "has" means.

Data (app-side, supplement): `Caster Group` = the capability entry + `grantToken(SpellTokens,
ruleCarrierCount, roundEnd)`. The `RoundEnd` clear is the "loses all unspent tokens" clause and rides the
TOKEN, so core Caster's pool still carries over.

Tests: `CasterGroupRuleIntegrationTests` (11) - the capability with and without the rule, carried by a
MODEL (the #093 joined-hero corner), gated live by a `Condition`, the stage wiring through
`SpellTargeting.IsCaster`, pool sized by carriers, shrinking with casualties, mixed units, round-end loss,
and core Caster's carry-over left intact. Engine 1952/1952, app 491/491.

Mutation-checked. The first pass found a **hole in my own tests**: reverting `IsCaster` to the identity
check left everything green, because the tests called `CastingRuleQueries.CanCast` directly and nothing
pinned the STAGES to it. Added `TheStagesAskForTheCapability_NotForTheCasterRule`; the mutation now reds.
Dropping the living-model filter reds the casualty test; dropping core `Caster`'s capability entry reds 7
tests across the cast stages.

Verified in play: a headless game with a Caster Group army traces
`Caster Group at Casting_OnCastCapability/Actor: fired -> EnableCasting`, offers **Cast** in the action
menu, and reports "No spell has a target in range" rather than "Not enough spell tokens" - i.e. the pool
was funded and the affordability gate passed. Corpus dead references **262 -> 259**.

## Slice: Versatile Defense — **DONE 2026-07-23** (21/21, the P5a residual)

> "When a unit where all models have this rule is deployed or activated, pick one effect: when shot or
> charged from over 9in away, the unit either gets +1 to defense rolls, or enemy units get -1 to hit rolls
> against it. This effect lasts until the units' next activation."
> — plus `Versatile Defense Aura`: "This model and its unit get Versatile Defense."

All 21 refs are the Aura form, reached through the **Icon of Havoc** wargear option in five books
(Havoc Brothers + the four Disciples).

**The filed premise was right, with one correction and one thing it missed.**

- **Right:** `ELifetime` had no "until my next activation" — `ThisActivation` dies before the opponent ever
  shoots, `ThisRound` dies at the wrong moment for a unit that activates late. New `ELifetime.UntilNextActivation`.
- **Correction — no new `TokenClearTrigger` variant.** `CustomHook(hook)` already means "clears when hook X
  fires" and `TokenClearService.ClearsAtHook` already sweeps it, so the lifetime maps straight onto
  `CustomHook(Activation_OnActivationStart)`. The only engine wiring is one `ClearForHook` call at the top of
  `ActivationStartStage` (mirroring `ReconcileEndOfActivationStage` at the other end), which must run BEFORE
  the offers are gathered or a unit that re-picks would hold both effects.
- **Missed — the deployment arm needs a cost that doesn't exist.** The once-per-X "used" marker is keyed on
  the RULE name, so it is shared by every ability of the rule at every hook. `OncePerActivation` paid at
  deployment leaves an `ActivationEnd` marker that only clears at the end of the unit's FIRST activation, so
  the unit would arrive at that activation's start with its own gate already shut. `OncePerRound` blocks
  round 1; `OncePerGame` blocks forever. New **`Cost.Free`** — no gate, because the deployment hook fires
  exactly once per unit and the stage resolves the rule's group exactly once.

**Latent defect found and fixed: an ability's `AllModelsHaveThisRule` gate was no gate at all.**
`RuleEvaluator.GatherOffersFromRules` built its `RuleInvocation` without a `Definition`, so the
self-referential `Condition.AllModelsHaveThisRule` took its "no rule identity to check" arm and returned
**true**. Versatile Defense is the first corpus rule that puts the all-models gate on the CHOICE rather than
on the effect — and it has to, because the effect's own gate is satisfied vacuously by the unit-held grant
that confers it. Fixed by passing `rule.Definition` through. Safe by survey: every `availableWhen` authored
across the catalog and the supplement today is `Always` or `TokenPresent`, neither of which reads it.

**Shipped:**
- Engine: `ELifetime.UntilNextActivation` + its `ClearTriggerFor` mapping; `Cost.Free`; the
  `ActivationStartStage` sweep; the `GatherOffers` `Definition` fix; a `TokenDisplay` phrase for the one
  named `CustomHook`.
- Engine: **`AbilityEffectChoice`** (next to `RepositionPlacement`, the shared-stage-helper precedent) —
  the "group a hook's offers by rule -> ask which effect -> resolve/apply/execute" block lifted out of
  `ActivationStartStage`. `DeployUnitStage` now routes MULTI-ability groups through it (mandatory, no
  Yes/No) and keeps its Yes/No prompt for the single-ability "you MAY" rules (Vanguard, Fanatic). Same
  resolution at both of the rule's hooks, by construction.
- Data (app-side): four definitions — `Versatile Defense (Guard)` (= Sturdy's body verbatim),
  `Versatile Defense (Evasion)` (= Changebound's), `Versatile Defense` (four labelled abilities: both
  effects at both hooks), `Versatile Defense Aura`. Embedded into the five books via `--apply-rules`.
  **The two effects were already shipped and covered under other names**, which is why this slice was
  lifetime-and-plumbing work, not rules work.

Tests: `VersatileDefenseRuleIntegrationTests` (15) — lifetime survives activation-end AND round-end but dies
at activation start; both effects net the right modifier and only beyond 9in; the all-models gate on the
choice (incl. the offer returning once the odd model out is dead); the deployment arm leaves the activation
pick open; and both stages driven for real (`DeployUnitStage` asks the choice not a Yes/No;
`ActivationStartStage` sweeps the previous pick; deploy-then-activate holds only the latest).
`VersatileDefenseShippedDataTests` (8, app-side) pin the same over the DATA as authored. `Cost.Free`
round-trip added to `CostSerializationTests`. Engine 1933/1933, app 490/490.

Mutation-checked all three engine changes independently: dropping the `Definition` pass-through reds exactly
the two gate tests; dropping the `ActivationStartStage` sweep reds the two "only the latest is held" tests;
mapping the lifetime to `ActivationEnd` reds the three lifetime tests.

**Verified in play, not just by lint** (the repeated #197 lesson): a headless game with a probe army
carrying `Versatile Defense Aura` prompts `pick one effect on deployment`, prompts again
`for this activation` each time the unit activates, and logs
`Versatile Defense (Guard) added +1 to Save rolls` on an incoming volley. A second run with the two
abilities swapped (so the EOF default lands on the other arm) logs
`Versatile Defense (Evasion) added -1 to Hit rolls`. Corpus dead references **283 -> 262**.

## Slice: Teleport — **DONE 2026-07-11** (15 + Teleport Aura 4)

Chris tried Teleport in a real game and it did nothing (it was a dead reference - no definition). Two
premises in the table row above turned out wrong, both corrected by Chris in-conversation before build:

- **Radius is a flat 6in**, not action-conditional 3in/6in. (Chris's game design overrides the OPR
  source text I read.)
- **It is a Choose Action MENU option, not a pre-attack hook.** The pre-attack stage was the wrong seam:
  it reports `Hold` for all shooting (so it can't tell an Advance from a stationary Hold) and never fires
  on a Rush. Teleport instead sits in the menu, repositions 6in, and LOOPS BACK so Charge/Shoot/Pass
  re-evaluate from the new position - which is exactly Chris's described flow ("move up -> Teleport or
  Charge, no Pass; teleport clear -> Pass returns").

Depended on **#206** (proximity Pass gate): "teleport clear of an enemy -> Pass returns" is the #206 gate
re-run on loop-back, not Teleport code. Built #206 first (engine `6053061`), then Teleport.

Shipped (engine `a84c56b`):
- `CoreRuleCatalog.Teleport` (in `All`, so book refs resolve - Teleport was a dead reference) + `Teleport
  Aura` via the `UnitAura` factory ("this model and its unit get Teleport").
- Offered at `Activation_OnActionChoice`, `Cost.OncePerActivation`, gated "before attacking"
  (not-yet-attacked, applied in `ChooseActionStage` like Embark's spatial gate). Routed by name to a new
  `TeleportStage` (the Disembark/Embark pattern), which runs a 6in per-model placement reusing
  `PlaceObjectsRequest.MaxDistanceFromStartInches` (from reposition-at-activation) and loops back. Fully
  layered - sets neither HasMoved nor HasAttacked, so move + teleport + shoot is legal; the teleport does
  not count toward the move-shoot cap.
- Cost paid only on an ACCEPTED placement, so backing out (Back) leaves Teleport available; the CLI/AI
  default is a stand-still Selected (not a cancel), so automation still pays and can't re-loop.
- `Effect.Teleport` is a no-op marker (the stage does the work), allowlisted in the catalog fire-lint like
  Disembark/Embark.

Tests: `TeleportRuleIntegrationTests` (8) - catalog resolution, offered/not-after-attacking,
ChooseActionStage routing + stash, TeleportStage pays-cost/places/loops-back/layered, 6in radius +
allowCancel, cancel-leaves-put-and-unspent. Verified end-to-end in a real headless scenario (a Blinkers
unit with Teleport): the action surfaces in the menu, offers the 6in placement, holds/loops correctly, and
is spent once used. Corpus dead references 701 -> 682.

## Slice: Delayed Action — **DONE 2026-07-11** (47 refs)

"Once per round, if your opponent has more units left to activate than you, this model's unit may pass
its turn instead of activating (may still be activated later)." An activation-ORDER rule (Titan Lords x5
sub-factions).

Design decided with Chris in-conversation (several forks):
- "Pass its turn" is DISTINCT from the existing Pass/Hold action (which activates the unit and consumes
  it). Delayed Action does NOT activate the unit - it stays in the pool for a later turn, and the turn
  passes to the opponent.
- Placement: NOT an action-menu button. The rule's condition ("who has more units left") is only knowable
  at the turn/round level, and "don't activate at all" isn't representable once you're in a unit's action
  menu. So it lives at unit-selection: after you pick the unit, a Yes/No "hold back?" appears (the
  disembark-style pick-then-confirm). This kept the shared unit-selection resolvers (and the click-on-map
  affordance) untouched - the alternative (an inline per-unit button) would have forced dedicated
  CLI/GUI/AI activation resolvers. Chris chose the lighter pick-then-confirm.
- Budget: once per round per PLAYER (a per-player `DelayedActionUsed` token scan over that player's living units), per
  Chris's read of "once per round".

Shipped (engine `597a43e`):
- `CoreRuleCatalog.DelayedAction` marker (in `All`; no hooks/abilities - detected by name), allowlisted in
  the fire-lint like Hero/Transport. `TokenType.DelayedActionUsed` (RoundEnd-cleared) is the per-player gate.
- `SingleTurnStage.GetNewChildContext` snapshots `OpponentHasMoreUnitsToActivate` (any opposing team has
  strictly more living-unactivated units than mine) onto the turn context. `ReconcileChildContextBeforeLeaving`
  skips `MarkUnitAsActivated` when the turn was delayed, so the held-back unit stays in the pool; the cursor
  then advances to the opponent on the next `DeterminePlayerTurnStage`.
- `ChooseUnitToActivateStage` offers the hold-back after selection when the unit has the rule, the gate
  holds, and the team hasn't already held back (token scan); accepting drops the token, marks the turn
  delayed, and routes to a new `ToDelayedTurnEnd` (straight to the ActivatedUnit-null-safe reconcile stage).
- Yes/No default is `false` (activate), so headless/AI never stall on the offer.

Termination: the once-per-round-per-player token guarantees a player can hold back at most once per round, so
every unit eventually activates and the round ends. Verified end-to-end in a headless scenario (1 Delayed
Action unit vs 2 enemy units): the offer appears only while outnumbered, accepting hands the turn to the
opponent, the unit activates on a later turn, and it isn't re-offered once the team has used it. 6 new
integration tests. Corpus dead references 682 -> 635.

## Slice: Darkborn — **DONE 2026-07-11** (59 refs)

**It was only the naming bug** - both mechanics were already fully built. The table row's "per-target
charge-distance debuff does not exist" was **stale**: `Movement_OnChargeDeclared` +
`MovementRuleQueries.EffectiveChargeDistanceAgainst` were built for #029/#183 (Melee Shrouding), and
`DarkbornDefensive` already rides them for its "-2in charge" facet (Melee Shrouding's own doc even says
"Same mechanism unblocks defensive Darkborn's -2 charge"). Both `DarkbornOffensive` (+3in range / +3in
charge, self-buff) and `DarkbornDefensive` (enemies -4in range floor 6 / -2in charge floor 6 vs this unit)
are in the catalog with passing mechanic tests (`RangeModifierRuleIntegrationTests`).

The only defect: **OPR reuses the bare name "Darkborn" for both rules across armies**, so all 59 references
(Dark Brothers 27, Dark Prime Brothers 32) matched neither disambiguated catalog name and were dead.
Confirmed against the source text which army means which: **Dark Brothers -> Defensive**, **Dark Prime
Brothers -> Offensive** (clean, unambiguous split).

Fork resolved with Chris (option B): disambiguate at the importer, not only in book data.
- **`OprBookImporter.DisambiguateAmbiguousRuleNames`** - a post-import pass keyed on the OPR army name
  (`{("Dark Brothers","Darkborn")->"Darkborn (Defensive)", ("Dark Prime Brothers","Darkborn")->"Darkborn
  (Offensive)"}`). The importer is the one place with the owning-army context needed to route the bare
  name, so a future re-import stays correct rather than reintroducing the dead bare name. The pass walks
  every rule-reference site (unit rules, weapon rules, items, and upgrade-option gains), not just
  unit-level, so it can't silently miss a placement.
- **The two bundled books** were patched to the variant names directly (a targeted string replace, not a
  re-import - the books have diverged from their imported state with the other hand-authored #197 rules,
  so a regenerate would clobber those).

Tests: `OprBookImporterTests.Import_DisambiguatesDarkborn_ByArmy` (both armies x unit/weapon/upgrade sites)
+ `Import_LeavesDarkbornUntouched_ForAnUnlistedArmy` (control); app-side
`BookRuleScopeTests.Darkborn_ResolvesToTheArmyVariant` (both shipped books resolve to the right variant,
the end-to-end guard against a re-import reintroducing the bare name). Verified via `--rule-coverage`:
corpus dead references **635 -> 576**, no Darkborn ref left dead.

## Slice: P15 randomized-branch effect (Unpredictable) — **DONE 2026-07-11** (48 of 53 refs)

The whole family is one shape: "when attacking, roll one die: on a 1-3 the models get AP(+1), on a 4-6
they get +1 to hit instead." Both modifiers already existed - the only new thing is a per-attack die that
SELECTS between them. Two forks were resolved with Chris before building:

1. **Decisive vs weighted branch** (the dice-invariant fork). The selecting die is DECISIVE
   (`RollDecisiveFace`): even under the probabilistic roller it commits to one concrete face, exactly as
   morale / dangerous-terrain / P5b round-start rolls do. A branch selector can't be meaningfully averaged
   into "half a modifier" on a threshold roll. Chris chose decisive (Option A) - faithful to a
   literally-"roll one die" rule and consistent with every other discrete roll in the engine. The AI's
   analytic `CombatMath` sees one branch per evaluation rather than the 50/50 mean; accepted as the same
   tradeoff the engine already takes everywhere it uses `RollDecisive`. Neither arm violates the invariant:
   +1-to-hit and AP(+1) are both threshold shifts folded before the analytic spread is counted.
2. **Roll granularity: once per attack ACTION** (not per weapon). Chris chose per-action, so a multi-weapon
   unit shares one branch across all its weapons ("roll one die ... apply to all models").

Why it needed real engine work (not just data): the two arms consume at DIFFERENT hooks - +1 to hit at
`Shooting_OnHitRollModifier` (72), AP(+1) as a -1 save modifier at `Shooting_OnHitRollComplete` (73, the
Thrust machinery) - and ops emitted at one hook aren't visible at the other. Two independent hook-rolls
would give both-or-neither instead of exactly one, so the single roll has to be taken before hook 72 and
carried to 73. Hook 72 is the first shared shoot/melee rule hook (hook 70 is shooting-only), and a same-pass
token grant isn't visible in that same pass, so the branch is resolved ABOVE the hooks and threaded down.

Shipped (engine):
- `EUnpredictableBranch` (None/HitBonus/ApBonus) + `IHasUnpredictableBranch` capability + a
  `Condition.UnpredictableBranchIs` that reads it.
- `UnpredictableBranchResolver` rolls the decisive die once, ONLY when the attacker carries an applicable
  rule (native, per-model, OR aura-granted via a RuleGrant token - so the Auras work), so the seeded stream
  (#193) is untouched for ordinary attacks. Called from `CombatActionContext.ConsumeAttackIntoContext`,
  cached per action (reset on `SwapCombatRoles` so a Counter attacker rolls fresh), threaded into
  `CombatMetadata.UnpredictableBranch` and onto the hit/save contexts.
- Catalog: `Unpredictable` (both kinds), `Unpredictable Fighter` (IsMelee), `Unpredictable Shooter`
  (Not(IsMelee)) - each two arms sharing the branch - plus `Unpredictable Fighter/Shooter Aura` via
  `UnitAura`. The fire-lint's hit/save context variants gained a branch dimension so the arms lint as
  firing (no allowlist). Single-emission verified: no corpus unit stacks base variants.

Tests: `UnpredictableRuleIntegrationTests` (15) - both arms through the REAL DetermineHitRollStage /
RollToHitStage (HitBonus -> hit threshold 4->3, ApBonus -> SaveModifier -1, exactly one fires),
melee/shooting gating, the resolver (roll bands, no-roll-when-absent, aura-granted detection), and the
once-per-action-shared-across-weapons behavior through a real `CombatActionContext` with a sequence roller.
Corpus dead references **576 -> 528**.

**Deferred: the two Mark variants** (`Unpredictable Fighter Mark` 3, `Unpredictable Shooter Mark` 2 = 5
refs). A mark grants Unpredictable at the hit-roll hook (72), AFTER the action-level roll has already
happened, so the mark-granted rule is invisible to the once-per-action resolver. Making them work needs the
resolver to also scan the DEFENDER for an Unpredictable-granting mark at action time - its own small slice.
Left dead (not silently), tracked in the by-leverage table below.

## Slice P10a: the auto-wound dice pool (Ravage + Crossing Attack) — **DONE 2026-07-22** (39 refs)

**The reading that reshaped the slice:** P10's dead names split into two unrelated mechanics once the
source text was checked (it is not stored in the repo - the book data carries only the name + numeric
arg). Ravage / Crossing Attack are "roll X dice; for each 6+ the target takes one **wound**" - an
AUTO-WOUND (no to-hit, no save). Storm of Change/Lust/Plague/War are "roll 3 dice; for each 2+ an enemy
takes 3 **hits** with [rule]" - a rolled HIT-count that rides the existing pre-attack + #164 fold. So
P10 is two primitives; this slice built the auto-wound one, proven on Ravage.

**Owner sign-off (2026-07-22):** the wounds skip the armor save but stay **regenerable** (Regeneration /
Tough still apply), matching the rulebook "takes a wound"; build the auto-wound primitive first (Ravage),
then Crossing Attack, then Storm.

Ravage is structurally **Impact with a threshold of 6 and the save skipped** - so it mirrors
`ResolveImpactHitsStage` almost exactly. Shipped (engine `1340496`):
- **`Effect.DealAutoWounds(ValueSource DiceCountPerModel, int SuccessThreshold=6)`** ->
  **`RuleOperation.InvokeDealAutoWounds`** (a plain op, enacted stage-side like `InvokeDealHits`). The
  effect resolves X x living carriers (weapon-aware, the `ReduceImpactDicePerModel` pattern) because the
  text is per model. There is **no output-attribution problem** (unlike Sergeant): the wounds land on the
  enemy, and the evaluator dedups the rule once per unit, so a single pool summed across carriers is exact.
- **`SyntheticWoundResolution`** (next to `SyntheticHitResolution`): rolls the pool, keeps the success
  count as the sub-histogram's fractional `TotalRolls` (never int-locked - the #100 invariant, same
  discipline Impact's `AtOrAbove` already uses), and wraps it as a `RollToSaveResults` whose FAILURES are
  every wound and successes are empty. That lets the wounds enter `AssignWoundsStage` directly - **skipping
  `DetermineSaveRollsNeededStage` and `RollToSaveStage`** (which also sidesteps the P14b marker-spend
  prompt, correct since there is no save to block) while Regeneration/Tough run untouched.
- **`ResolveRavageWoundsStage`** fires at `Melee_OnChargeContact` (melee is only ever entered via Charge),
  folds the `InvokeDealAutoWounds` dice, and runs an `AssignWounds -> ApplyWounds` child pipeline. Wired
  into `MeleeStage` right after Impact. `CoreRuleCatalog.Ravage` (in `All`); `RuleFireLint` consumption arm
  extended so the op lints as read at its hook.
- Tests: `RavageRuleIntegrationTests` (6) through the REAL stage - save-skipped (a rolled-6 defense-4 save
  would block all, yet the wounds land), below-threshold no-op, per-model carrier scaling (2x3=6),
  Regeneration still ignoring the wounds, and the probabilistic-mode fractional invariant (3 dice -> 0.5).
  Engine 1847/1847, app build clean, headless smoke exit 0. Corpus dead **423 -> 392**.

**DEFERRED (recorded, not silently cut):** a Ravage unit that is CHARGED does not roll on its strike-back
- only the charger triggers the stage, mirroring Impact's charge-only scope. Its own small follow-up if
the strike-back case matters in play.

**Crossing Attack (8 refs, engine `3ee6896`)** reuses the same primitive on the movement trigger - it is
Strafing but auto-wounds instead of hits. Shipped:
- **`CrossingAttackStage`** (next to `StrafingStage` in the movement flow, right after it): same
  move-through-enemy detection and YesNo offer, but it offers ONLY `DealAutoWounds` abilities and runs the
  save-skipping `AssignWounds -> ApplyWounds` pipeline. `StrafingStage` was filtered to offer only
  `DealHits` abilities, so the two never double-offer or double-charge a rule at the shared
  `Movement_OnMoveThroughEnemy` hook.
- **Arg threading:** Crossing Attack(X) is the FIRST activated ability whose effect reads
  `ValueSource.Arg`. `ResolveAbility` previously passed empty arguments (a `// thread ... when one does`
  TODO); `AbilityOffer` now carries the bearing rule's `Arguments` and `ResolveAbility` resolves against
  them, so `DealAutoWounds(Arg(0))` reads the real X. Backward-compatible (the param defaults to null).
- `CoreRuleCatalog.CrossingAttack` (fly-over passive + the ability, like Strafing); `RuleFireLint`
  ability-hook arm extended. Tests: `CrossingAttackRuleIntegrationTests` (7) - offer + arg threading
  (Crossing Attack(2) -> 2 dice), unsaveable wound through the real stage, Regeneration still applying,
  decline, and the Strafing/Crossing offer-isolation split. Corpus dead **392 -> 384**.

## Slice P10b: Storm of X (decisive rolled multi-target hit burst) — **DONE 2026-07-22** (5 refs)

The other half of P10: a rolled HIT-count, not auto-wounds. "Once per game, when activated before
attacking, roll 3 dice; for each 2+ pick an enemy unit within 12in that takes 3 hits with [rule]"
(Change=Shred, Lust=Surge, Plague=Bane, War=AP(1)).

**Owner sign-off (2026-07-22):** per-success target picking (each 2+ independently picks an enemy, up to
3 different units) - NOT one target taking the scaled hits. That choice forced two consequences, both
signed off:
- **The pool roll is DECISIVE.** You cannot pick a fractional number of targets, so the 3 dice commit to
  concrete faces (integer successes) even under the probabilistic roller - the same dice-invariant call as
  P15's branch die and P5b's recovery. Only the pick-COUNT is decisive; each target's 3 hits still flow
  through the fractional hit pipeline, so the invariant holds. (Had it been single-target, the pool could
  have stayed fully fractional.)
- **It needs a looping stage.** Up to 3 separate target+hit batches, each its own save/wound pipeline -
  which the one-shot pre-attack path (Breath Attack) cannot do.

Shipped (engine `dcace2d`):
- **`Effect.StormOfHits`** (config: pool dice, threshold, hits-per-success, WithRules, AP, range) ->
  **`RuleOperation.InvokeStorm`**. Config-only; the stage does the rolling and targeting.
- **`StormStage`** - offered in Choose Action and routed from `ChooseActionStage` (the Teleport pattern,
  detected by effect type since four rule names share it; `Cost.OncePerGame`; gated `!HasAttacked`; fully
  layered). On first entry it pays the cost, rolls the pool with `RollDecisiveFace` in a loop -> integer
  successes, and prompts one enemy-within-12in pick per success (a success with no enemy in range is lost).
  Each picked target's 3 hits run the real `SyntheticHitResolution` fold (so Shred/Surge/Bane/AP apply,
  #164) through the `DetermineSaveRolls -> RollToSave -> AssignWounds -> ApplyWounds` child pipeline. The
  per-target batches LOOP: `OnBatchDone` re-enters the stage (dequeue next), `OnAllDone` returns to the
  menu when the queue drains - the melee-swing loop pattern, but self-looping with the queue encapsulated
  as a stage field.
- **Arg-driven abilities** already worked (Crossing Attack's `AbilityOffer.Arguments`), though Storm takes
  no (X). `RuleFireLint` ability-hook arm extended for `InvokeStorm` at `Activation_OnActionChoice`.
- Tests: `StormRuleIntegrationTests` (5) through the real stage - 3 successes -> 3 independent target picks
  -> each takes its 3-hit batch (the loop), 0-successes-but-cost-spent, probabilistic-mode integer picks,
  Choose Action routing, and the once-per-game gate. Engine 1863/1863. Corpus dead **384 -> 379**.

## Slice P21 — re-scoped: it was never one slice (2026-07-22)

Like P22 before it, the P21 row bundled unrelated mechanics under a title ("setup-phase re-deploy")
that fits only two of its seven rules. Reading the source text (off-repo, from the army markdown), the
60 refs decompose as:

| Rule | Refs | Actual mechanic | Home |
|------|-----:|-----------------|------|
| Re-Deployment | 27 | Post-deployment sub-phase: remove up to 2 units, redeploy, players alternate | **Genuine new stage** - this slice |
| Fanatic | 19 | "After this model deploys, place it within 9in" | **Rides the existing `Deployment_OnUnitDeployed` hook** (Vanguard's seam) - DONE below |
| Dash + Dash Aura | 6 | "At END of activation, place models within D3+1in" | Reposition-at-activation's twin at a different trigger - re-filed (own row) |
| Ambush Re-Deployment | 4 | "At end of activation, remove and redeploy as Ambush next round" | Ambush variants (round-N arrival) - re-filed |
| Mobile Artillery | 2 | ">9in Hold+shoot +1 / hasnt-moved enemy -2 hit" | Pure data on the built `AttackedFromOverInches` gate - re-filed (Misc) |
| Quick Readjustment | 2 | "Ignore move-shoot penalty for Indirect weapons" | Small penalty-ignore - re-filed (Misc) |

Only **Re-Deployment (27) + Fanatic (19) = 46 refs** are genuine deploy-phase work. The other 14 were
re-filed to their natural rows with reasons (not silently dropped). Owner signed off the decomposition,
Fanatic-as-placement, and 2-per-Re-Deployment-unit budget on 2026-07-22.

### Fanatic (19 refs) - DONE 2026-07-22, engine `599be98`

Two discoveries shrank this to near-data. There is already a `Deployment_OnUnitDeployed` hook that fires
after a unit's placement and offers activated abilities (`DeployUnitStage.OfferPostDeploymentAbilities`) -
**Vanguard** already rides it ("once per game, after deploy, move up to 9in", `Effect.TriggeredMove`).
Fanatic is Vanguard-shaped but a PLACEMENT not a move (owner's reposition-is-a-placement ruling, and the
corpus word "placed"), so it reuses the reposition-placement machinery the DONE reposition-at-activation
slice built. Shipped:
- **`Effect.RepositionOnDeploy(float MaxInches)`** -> emits the shared `RuleOperation.RepositionModels`
  op. Flat (no dice) rather than reusing `RepositionAtActivation` with a degenerate die, since every
  `DiceExpression` rolls at least one die and Fanatic's range is fixed.
- **`RepositionPlacement`** (new shared helper) - the "you MAY place all models within Nin" fold, extracted
  verbatim from `ActivationStartStage.OfferReposition` so both the activation-start and the deploy path run
  ONE implementation. `DeployUnitStage.OfferPostDeploymentAbilities` now folds `RepositionModels` from the
  resolved ops into the placement after the executor runs (the executor ignores it - it is stage-folded,
  not an `ExecutableOperation`).
- **`CoreRuleCatalog.Fanatic`** (in `All`): a `Deployment_OnUnitDeployed` activated ability, `Cost.OncePerGame`
  (deployment happens once, so the gate is naturally spent - matching Vanguard, and it also stops a re-offer
  on a later Scout/Ambush placement), Self target, `RepositionOnDeploy(9)`. `RuleFireLint` ability arm
  extended (`RepositionModels` handled at the deploy hook).
- Tests: `FanaticRuleIntegrationTests` (6) - catalog shape, the flat op emission, the once-per-game gate,
  and through the REAL `DeployUnitStage`: accept -> repositions within 9in (radius + allowCancel reach the
  resolver), decline -> stays at its deploy position, non-Fanatic -> no prompt. Engine 1870/1870, app build
  clean, headless smoke exit 0. Corpus dead **379 -> 360**.

### Re-Deployment (27 refs) - DONE 2026-07-22, engine `3c2d340`

The genuine new deploy-phase work: "after all other units are deployed (excluding units that were set
aside), you may remove up to two friendly units from the table and deploy them again; players alternate in
placing, starting with the player that activates next." Shipped:
- **`ReDeploymentStage`** - a new child of `DeployAllUnitsStage`, inserted after the normal deploy loop
  (`OnFinishedDeployingAllUnits`) and BEFORE `PlaceDeferredUnitsStage`, so set-aside (Scout) units are still
  off-table and therefore ineligible - which is exactly "excluding units that were set aside" (eligibility is
  `GetIsOnBattlefield`).
- **Budget (owner ruling): 2 per Re-Deployment unit owned, stacking** (2 units -> 4 redeploys). Detected by
  name (`unit.RuleDefinitions.Any(r => r.Definition == ReDeployment)`, the Caster-detection pattern) over the
  player's living units.
- **Alternation:** players go one unit at a time in activation order. "The player that activates next" is the
  head of `FirstDeploymentRollOrder` - `MainPhaseContext` seeds its turn order from that same list, so the
  deployment-roll-order head both deploys and activates first. A round-robin over the player order spends one
  redeploy (or takes a pass) per player per cycle; a pass ends that player's participation; the sub-phase ends
  when everyone has passed or exhausted budget. Terminates because each cycle either marks a player done or
  spends one of a finite budget. (Faithful team-alternation for the 1v1 corpus; multi-player is an
  approximation over the flat activation order.)
- **The redeploy** picks a friendly on-table unit (a `CancellableSelectionRequest<UnitData>`, Cancelled =
  pass; already-redeployed units excluded so "two units" stays distinct) and re-places its models anywhere in
  the owner's deployment zone via the normal `PlacementRequesting.RequestMandatoryPlacement` flow -
  `DeployUnitStage`'s placement, reused. Re-Deployment is a marker rule (no hooks), allowlisted in the catalog
  fire-lint like Delayed Action.
- Tests: `ReDeploymentRuleIntegrationTests` (6) through the REAL stage - no-rule -> no prompt, budget of 2 per
  unit, budget stacks to 4, pass ends participation, set-aside units never offered, and two-player alternation
  starting with the roll-order head with each unit landing in its own zone. Engine 1877/1877, app build clean,
  headless smoke exit 0. Corpus dead **360 -> 333** (Ambush Re-Deployment's 4 correctly stay dead - re-filed).

## Slice P11: reflect damage (Retaliate + Deathstrike + Self-Destruct) — DONE 2026-07-22, engine `163a2f3` + `9a4dbeb`

"When this model takes a wound / is killed in melee, the attacker takes X hits." Three rules share a
"deal X hits back at the melee attacker" primitive; slice 1 built Retaliate + Deathstrike, slice 2
(engine `9a4dbeb`) added Self-Destruct.

**Owner rulings (2026-07-22):** per-model attribution (exact, not unit-level - the harder path that also
defers Sergeant); build Retaliate + Deathstrike first, then Self-Destruct.

The reading that shaped it: all three reflect AFTER the melee resolves (Self-Destruct's text says so
explicitly), so this is a post-melee TALLY, not a per-wound hook. The engine already snapshots per-unit
start-wounds on the combat context; adding a per-model snapshot made per-model attribution exact by simple
before/after comparison - no per-wound tracking, sidestepping the mechanism that stalled Sergeant. Shipped:
- **Per-model start-wounds snapshot** on `CombatActionContext` (`ModelRemainingWoundsAtStart`, keyed by
  model reference so a Counter swap needs no re-keying), captured before the first swing (attacker at
  construction, defender at `SetDefender`), melee-only.
- **`ResolveMeleeReflectStage`** - a new child of `MeleeStage` after consolidation (before the post-melee
  Harassing move). For each of the two combatants as bearer, it counts per rule-bearing MODEL: Retaliate's
  wounds-taken (`start - current`) x X, and Deathstrike's kills (start > 0, now dead) x X, then deals that
  many plain hits at the other unit through the real `DetermineSaveRolls -> RollToSave -> AssignWounds ->
  ApplyWounds` pipeline (so the target's armor / Regeneration apply). The per-bearer batches LOOP via
  `OnBatchDone` re-entering the stage, the StormStage pattern. A model has the rule if it or its unit carries
  it (Arg(0) = X), so a unit-wide rule counts every model and a champion-only rule counts just that model.
- **Fractional-safe:** the hit count is `X x woundsTaken` built directly as a synthetic histogram (not an
  int `Roll`), so probabilistic mode's fractional wounds flow through without int-locking - the #100 dice
  invariant, the same discipline Ravage used.
- `Retaliate` / `Deathstrike` are marker rules (no hooks; detected by name at the stage, like Delayed
  Action / Re-Deployment), allowlisted in the catalog fire-lint. Tests: `ReflectRuleIntegrationTests` (6)
  through the real stage - X-per-wound, X-per-kill, survive/no-wound/no-rule no-ops, and the per-model
  attribution pin (a champion's wound reflects, the grunt's does not). Engine 1886/1886, app build clean,
  headless smoke exit 0. Corpus dead **333 -> 309**.

**Self-Destruct (3 refs, engine `9a4dbeb`)** extends the same stage: X hits at the enemy per rule-bearing
model that ENTERED the melee alive (whether it died fighting or not - keyed on the start snapshot so the
count is stable), and every survivor "is immediately killed" now, routed through the same
`UnitDestructionNotifier` choke a melee kill uses (marks cleared, hook fired, enemy credited). The two hit
terms don't double: a model killed fighting counts once for its X and isn't re-killed. Tests: 2 more in
`ReflectRuleIntegrationTests` (8 total) - a survivor self-kills AND deals X, a killed-in-melee model deals
X once. Engine 1889/1889, app build clean, headless smoke exit 0. Corpus dead **309 -> 306**; P11 complete.

## Slice P6: the deferred buff/debuff family — DONE 2026-07-23, engine `6121b13` + `c83b1fd` (+ `4a8e767` tooling)

"Once per activation, before attacking, pick one enemy unit within 18in in line of sight, which gets X
once (next time the effect would apply)." Five debuffs; two friendly-facing riders came along free.

**The row's premise was mostly wrong.** It filed all five as needing "the debuff mirror of the built
`FirstTrigger` buff grant". Checked against the engine, four of the five ride seams that were already
built AND already consumed - the corpus just had nobody author them:

| Rule | Refs | What it actually needed |
|------|-----:|-------------------------|
| Morale Debuff | 4 | `statModifier(Morale, -1, NextTrigger)` - `MoraleUtilities` already calls `ConsumeNet` |
| Defense Debuff | 3 | `statModifier(Save, -1, NextTrigger)` - `DetermineSaveRollsNeededStage` already consumes it |
| Speed Debuff | 2 | `addRule("Slow", NextTrigger)` - core `Slow` IS exactly -2in/-4in, and `ExecuteMoveStage` already spends one-shot movement grants (the #153 seam) while the budget projection stays read-only |
| Piercing Debuff | 3 | Fortified's `reduceArmorPenetration(1)` on the **Actor** seat, so it blunts the bearer's own weapon AP instead of protecting it. Ungated by `isMelee` ("when attacking", not "when shooting") |
| Casting Debuff | 8 | **The only real gap** - no cast-roll modifier carrier existed |

Riders authored on the same shapes: **Casting Buff (2)** (the +1 mirror, unblocked by the same
primitive) and **Speed Buff (1)** (`addRule("Fast")`), both previously dead under Misc/P23.

### Shipped

- **`ERollKind.Cast`** (a fourth roll kind) + **`TokenType.CastRollModifier`** + the
  `RollModifierTokens` map entry. `CastSpellStage` folds `GrantedRollModifiers.ConsumeNet(caster, Cast)`
  into its existing `netModifier`, so the delta shifts the 4+ **threshold** exactly as boost and assists
  do - never a post-roll adjustment, so the dice invariant holds. Consumed once per attempt after the
  cost is spent (a browsed-and-cancelled spell never burns the debuff), success or failure alike. The
  roll breakdown gained a `granted +/-N` term; `TokenDefinitionCatalog`/`TokenDisplay`/`SpellText` name it.
- **`TargetSelector.RequiredRule` now scans the unit's MODELS too** (engine `c83b1fd`). A joined hero
  keeps its rules on its model (#006/#093), so the unit-only scan was blind to hero Casters - which is
  every practical target for "pick one enemy with Caster". Only `Artillery` used the filter before, so
  the widening is behaviour-preserving there.
- **Data:** 8 definitions into `GdfRuleSupplement.json`, re-applied to the 11 carrying books via
  `--apply-rules` (additive diffs only, no reformatting). `Piercing Debuff Effect` is the support rule
  `Piercing Debuff` grants.
- **Tests:** engine `CasterRuleIntegrationTests` +3 (debuff spoils / buff rescues through the real
  `CastSpellStage`, and per-kind carrier isolation), `PreAttackRuleIntegrationTests` +1 (joined-model
  RequiredRule); app `DeferredDebuffCompositionTests` (16) drives the **real shipped supplement** through
  the real evaluator and sinks - the seat pin (Actor blunts, Subject would buff), the melee arm, the
  granted-distance nets, and the offer shape. Engine 1895/1895, app 439/439, headless smoke exit 0.
- **Mutation-checked both halves:** dropping the `granted` fold reddens exactly the two new cast tests;
  flipping the authored seat to Subject / the granted rule to Very Fast / the cast roll kind to Hit
  reddens exactly 4 app tests.

Corpus dead references **306 -> 283**.

### Verified in play, not just by lint

A headless scenario (`--scenario`, seeded Realistic) showed the ability offered and gated correctly
(`--trace-rules`: `offered` once, then `not offered (cannot pay OncePerActivation)`), with the
`requiredRule: Caster` filter finding the enemy caster. Same seed, same roll of 3, three runs:

| Scenario | Log |
|----------|-----|
| control | `failed to cast Hex: rolled 3, needed 4+` |
| Casting Debuff | `failed to cast Hex: rolled 3, needed 5+ (base 4+, granted -1)` |
| Casting Buff | `cast Hex: rolled 3, needed 3+ (base 4+, granted +1)` |

Closing that loop needed a **#167 tooling gap** fixed first (engine `4a8e767`): `ScenarioToken` had no
way to express a payload, so a placed `CastRollModifier` token carried no delta and netted zero - it
looked exactly like the modifier not working. It now takes an optional `delta` for the four carrier
types, and a `delta` on any other type is a compile error rather than a silent drop. `Scenarios/README.md`
documents it; 2 tests.

### Found here, fixed under #095 later the same day: supplement-rule grants die across a save/load resume

**Resolved 2026-07-23** — `ArmyData` now persists the army file's embedded `RuleDefinitions` (and its
`Spells`, a second casualty found with it) into the save, and the resume path replays them into the shared
resolver. The `Precision Fighter Buff` control scenario below now runs clean. The data authored in this
slice needed no change. See #095's 2026-07-23 note. Original finding, for the record:


`addRule` grants of a **supplement** rule resolve in a normal game (`GameBootstrap.BuildRuleResolver`
registers every player's embedded definitions into one shared resolver, so cross-army grants are fine),
but a resumed game logs `Granted rule 'X' has no definition in the registry - the grant does nothing`.
`FDGServer.BuildRuleResolver`'s own doc names the cause: on resume the army files may be vestigial, so
only core-rule grants survive. Confirmed **pre-existing and class-wide**, not introduced here: a control
scenario carrying the already-shipped `Precision Fighter Buff` fails identically, while `Speed Debuff`'s
core `Slow` grant survives. Affects all 15 shipped `* Buff` rules plus `Piercing Debuff`. Left as
authored (consistent with its siblings and correct in normal play) rather than papering over one
instance; filed against **#095**.

## Slice P22a: Repel Ambushers + Ambush Beacon (30 refs) — DONE 2026-07-28

> Repel: "Enemy units using Ambush must be set up over 12\" away from this model's unit." (24 refs, 8 books)
> Beacon: "Friendly units using Ambush may ignore distance restrictions from enemies if they are deployed
> within 6\" of this model." (6 refs, 3 books)

**Owner sign-offs (2026-07-28):** the Beacon waiver is judged PER arriving MODEL; it overrides BOTH
restriction kinds (the flat over-9" rule and Repel's 12"); and the arrival distance scans became
side-aware in this slice.

**The shape: constraint discs on the request + one legality authority.** `PlaceObjectsRequest` gained
`EnemyKeepOutDiscs` / `EnemyDistanceWaiverDiscs` (new `PlacementDisc(Center, RadiusInches)`, snapshotted
at request build — nothing moves during a placement, so the snapshot is exact), and a new engine
`PlacementDistanceRules` is the single authority combining the flat minimum + keep-outs + waivers
(waiver wins over both; keep-outs exclusive at the boundary like the over-9" rule, waiver inclusive).
All four placement resolvers now judge enemy distance through it: CLI + GUI (their `TooCloseToEnemy`
kept only to word the failure message), solo-AI (`BlockPenalty`'s one enemy-distance read — the
Tactician inherits it, being a subclass), and the Tactician's strike aim (per-victim clearance asks
`CapabilityRuleQueries.AmbushRepelDistance` so "right behind it" aims past a repeller's 12").

**Both rules are capability answers** at `Lifecycle_OnCapabilityQuery` (`Effect./RuleOperation.
RepelAmbushers(dist)` + `AmbushBeacon(range)`), so a Condition gates them live and suppression applies;
`AmbushArrivalRules` (beside `ReserveRules`) turns the answers into discs — side-aware
(`ITeamExtensions.AreAllied`; "friendly"/"enemy" is relative to the ARRIVING unit), living models only,
reserve units project nothing, one disc per living model of the constraint unit. The per-model
radiation is Repel's wording exactly; for Beacon ("this model") it is the unit-holds-the-rule
accommodation Accumulator/Caster Group made — every corpus beacon is a single-model unit.

**Found while scoping: the CLI resolver's enemy scan was player-based** (teammates counted as enemies
for the 9" rule), while GUI/AI/Tactician were already side-aware — fixed here per the sign-off, one
line on the shared `AreAllied`.

**GUI**: `IEnemyExclusionProvider` reshaped to discs; the no-go blob paints each keep-out disc at its
own radius and ERASES the beacon bubbles (custom zero-src blend — the hole IS the semantics), with a
green outline so the player sees why the hole exists. Aircraft off-table redeploy is not "using
Ambush": no discs, by construction.

Data (app-side, supplement): both defs, capability entry each; embedded into the 8 Repel books
(EternalDynasty, Jackals, HumanInquisition, SoulSnatcherCults, RobotLegions, WormholeDaemonsofWar,
OrcMarauders, HumanDefenseForce) + 3 Beacon books (DAOUnion, SaurianStarhost, EternalDynasty).

Tests: engine `AmbushArrivalConstraintTests` (12 — disc building incl. dead/reserve/allied/live-Condition
gating, the combination authority incl. both waiver-overrides pins and the boundary conventions, and the
stage wiring end-to-end via the real `StartOfRoundExtraActionStage`); `AiPlaceObjectsResolverTests` +1
(the AI honors a keep-out disc bigger than the flat rule). App `AmbushConstraintShippedDataTests` (5 —
shipped-data pins + end-to-end discs from the real JSON + every-referencing-book-embeds). Engine
2259/2259, app 676/676, smoke exit 0.

Mutation-checked, four mutations each redding exactly its own tests: dropping the waiver short-circuit,
reverting the side check to player-based, dropping the stage's disc wiring, and deleting the AI's
enemy-distance read. That last one initially SURVIVED — both AI tests passed with the check deleted,
because the fan-out lane happens to land far from a mid-table enemy — so both were hardened to park the
enemy on the AI's natural landing spot (the pre-existing 9" test had been vacuous all along).

Corpus dead references **199 -> 169**.

## Slice P22b: Rapid Ambush (4 refs) — DONE 2026-07-28

> "Counts as having Ambush, but may be deployed at the start of any round, including the first."
> (Dark Prime Brothers, Dark Brothers)

**A field, not a new `EDeferTiming` value.** The filed premise suggested a new timing; the real
variable is only the EARLIEST arrival round, so `DeferDeployment` (effect + op + JSON) gained
`MinArrivalRound` (default 2) and every `Timing == LaterRound` check in the codebase stays untouched -
core Ambush and all pre-existing authorings keep their gate by defaulting. `BringOnReserves` now runs
every round and gates PER UNIT (`roundCount < defer.MinArrivalRound`); the Aircraft off-table return
keeps its flat round-2 gate. `ChooseUnitToActivateStage`'s unavailability tooltip words the unit's own
round ("Reserve - arrives round N").

Data (app-side, supplement): `deferDeployment(LaterRound, 9, minArrivalRound: 1)`; embedded into
DarkPrimeBrothers + DarkBrothers.

Tests: engine `RapidAmbushRuleIntegrationTests` (3 - round-1 arrival WITH the 9" constraint while a
core-Ambush unit in the same run stays held (the per-unit pin), declined-round-1 re-offered round 2,
and the default-2 pin so existing authorings are provably untouched). App `RapidAmbushShippedDataTests`
(2 - the JSON deserializes minArrivalRound 1 (a silent fallback to 2 would validate, lint and play
exactly like core Ambush), book embedding). Engine 2262/2262, app 674/674, smoke exit 0.

Mutation-checked: reverting the per-unit gate to the flat round-2 check reds exactly the round-1 test.

Corpus dead references **169 -> 165**.

## Slice P22c: Ambushing Piercing Shot (4 refs) — DONE 2026-07-28 (app-side only)

> "Counts as having Ambush, and its weapons get AP(+1) when shooting on the round in which it deploys
> via this rule." (Jackals, Robot Legions, Rebel Guerrillas)

**Pure data, no engine change** - two entries on shipped seams: `deferDeployment(LaterRound, 9)` (the
Ambush half) + a Piercing-Fighter-shaped Save -1 at `Shooting_OnHitRollComplete`, Actor seat, gated
`and(not(isMelee), tokenPresent(ArrivedFromReserve))`. The arrival pass already stamps that token and
the round-end sweep already clears it, so "on the round in which it deploys" rides the existing
lifecycle. Recorded approximation: an Aircraft off-table return stamps the same token; no corpus APS
unit is an Aircraft (checked), so nothing observable rides on the shared marker.

Tests: app `AmbushingPiercingShotShippedDataTests` (4 - the defer half is plain round-2 Ambush; token
present -> -1 Save shooting and 0 in melee; no token -> 0; book embedding). App 679/679.

Corpus dead references **165 -> 161**.

## Slice P22d: Ambush Re-Deployment (4 refs) — DONE 2026-07-28 (P22 closed, 42/42)

> "Once per game, when a unit where all models have this rule ends its activation, you may immediately
> remove it from the table (dropping any objectives it might hold within 1\"), and deploy it as if it
> had Ambush at the beginning of the next round." (Elven Jesters)

**Owner sign-off (2026-07-28): the return is MANDATORY** - the next round start PLACES the unit without
asking; only the spot is the player's. `DeferDeployment` gained `MandatoryArrival` (default false);
`BringOnReserves` skips its Yes/No when set.

**Two halves that meet on a token.** The removal is an end-of-activation activated ability
(`Cost.OncePerGame`, `availableWhen: allModelsHaveThisRule`) whose `Effect.AmbushRedeploy` resolves to
an executable `InvokeAmbushRedeploy` -> new `IOperationServices.RedeployAsAmbush`: drop any objective
the unit's SIDE holds within 1" (base-edge to marker centre, reconcile's own measure; #297
side-awareness), park the models at the unplaced sentinel, `PlaceInReserve`, stamp the new
`TokenType.PendingAmbushArrival` (ManualOnly - it must survive the round-end sweep). The return leg is
the rule's own `deferDeployment(LaterRound, 9, mandatoryArrival: true)` entry GATED on that token, so
the ordinary arrival pass finds it with no special case, and arriving spends the token.

**The end-of-activation ability seam is new** (and is what Dash rides later):
`ReconcileEndOfActivationStage` gathers offers at a new `ActivationEndContext`
(`Activation_OnEndOfActivation`) BEFORE its token sweep, mirroring `ActivationStartStage`'s
single-ability-Yes/No / multi-ability-pick shape via `AbilityEffectChoice`. One deliberate difference,
recorded in the stage doc: **the Yes/No defaults to NO** - at activation start the lone optional
ability is a buff and the aggressive EOF/AI default suits it; here the only corpus ability is a
once-per-game self-removal with a mandatory return, which an auto-accepting AI would fire on every
unit's first activation. A unit wiped out during its own activation (melee strike-back) is never
offered. `RuleFireLint` gained the hook in `AbilityOfferingHooks` + the context in `ContextVariants` -
the lint itself caught the missing context by failing the shipped data's fire check, which is exactly
the loud-drift direction it was built for.

Data (app-side, supplement): both halves as above; embedded into ElvenJesters.

Tests: engine `AmbushRedeployRuleIntegrationTests` (4 - the full accept loop through the REAL stages
(removal state, mandatory return that FAILS the test if any Yes/No is asked, arrival token, pending
token spent, once-per-game gate), decline leaves the gate unspent, the objective drop trio (ours-near
drops / enemy-near stays / ours-far stays), wiped-unit never offered). App
`AmbushRedeployShippedDataTests` (3 - the ability pins, the return-leg pins incl. BOTH HALVES NAMING
THE SAME TOKEN (gating on any other strands the unit off-table), book embedding). Engine 2266/2266,
app 683/683.

Mutation-checked: dropping the MandatoryArrival honor (always ask), disabling the objective drop, and
dropping the alive guard each red exactly their own test.

Verified in play (headless scenario probe, scratchpad): an on-table carrier surfaces "Use Ambush
Re-Deployment on Troupe?" at every end of activation (EOF declines, gate unspent - correct for "you
may"); a unit seeded InReserve+PendingAmbushArrival arrives at the round-2 start with ZERO "Deploy
from Ambush this round?" prompts (grep-counted), through the normal placement flow, then logs
"arrives from Ambush!". Exit 0.

Corpus dead references **161 -> 157**. P22 closes at 42/42 (Repel 24 + Beacon 6 + Rapid 4 + APS 4 +
Re-Deployment 4).

## Slice: Misc small primitives — IN PROGRESS (started 2026-07-23)

The 102-ref "Misc" row, triaged rule-by-rule (each is a one-off). Full wording pulled from the corpus
doc. Build order: pure-data wins first, then shared-condition clusters, then the genuine one-offs.
Three heavier rules RE-FILED out of misc into their own slices (surfaced, not silently cut) - see the
re-file note at the end of this section.

**Screened / Screened Aura (1 ref) - DONE 2026-07-23 (app-side `2279a95`).** "When units where all
models have this rule are shot or charged from over 9in away, enemy units get -1 to hit." Byte-identical
to the already-shipped `Machine-Fog` / `Changebound` on the DONE `AttackedFromOverInches` gate +
`Shooting_OnHitRollModifier`. **Pure data, no engine change.** Authored `Screened` (Machine-Fog verbatim)
+ `Screened Aura` (the only corpus form, in Wormhole Daemons of Plague) into the supplement, embedded via
`--apply-rules`. Tests: `ScreenedShippedDataTests` (4) - nets -1 only beyond 9in (hook-correctness pin),
fires for a long charge not just shooting, the aura confers the rule, and end-to-end aura -> conferred
rule -> live -1. App 510/510; headless smoke exit 0. Corpus dead **243 -> 242**.

**Grounded family - Reinforcement / Precision / Stealth (8 refs) - DONE 2026-07-23 (engine `2d90fac`,
super `94bdaa3`).** "If a unit where all models have this rule has most of them within 1in of terrain, ..."
- Reinforcement +1 defense, Precision +1 to hit attacking, Stealth enemies -1 to hit. **One shared new
primitive: `Condition.MostModelsWithinInchesOfTerrain`.** Terrain reaches the condition as a new
`IHasTerrain` capability on the two hit contexts, populated from `GameContext.TableState.Terrain` in
`DetermineHitRollStage` / `RollToHitStage` (empty on AI-valuation / synthetic-hit paths - a conservative
default: the bonus is only ever omitted). The condition reads `invocation.Bearer` (the fact is about the
firing unit; the same context serves both seats) against the terrain list; shared `TerrainProximityQueries`
measures from the base EDGE (bounding-radius inflation, as `SweptBaseGeometry` does), strict living-model
majority. `HookContextCatalog` is reflection-based, so implementing `IHasTerrain` auto-registered it - no
validator table to touch. Lint seeds one origin-terrain hit-context variant so the rules prove fireable.
Design note: terrain-as-capability (not cached unit state) keeps rules stateless and avoids staleness; the
alternative was strictly worse. Data: 3 base + 2 auras (Precision is Aura-only in corpus; Stealth base-only),
embedded into MachineCults + SoulSnatcherCults. Tests: engine `GroundedTerrainRuleIntegrationTests` (7 -
fires/no-terrain/far/strict-majority/living-only/base-edge-not-centre/empty), app `GroundedShippedDataTests`
(8 - hook+seat+gate+effect pins, aura links, net +1def/+1hit/-1enemy-hit only in terrain). Engine 2011/2011,
app 523/523, smoke exit 0. Corpus dead **242 -> 234**.

**Mobile Artillery (2 refs) - attacker arm DONE, defensive arm DEFERRED 2026-07-23 (app-side `79999ab`).**
"When this unit uses a Hold action and shoots at enemies over 9in away, +1 to hit. As long as this unit
hasn't moved during the round, when enemies shoot at it from over 9in away, they get -2 to hit."
- **Attacker arm: pure data** on shipped primitives - `Not(IsMelee)` + `Not(AfterMoving)` (= Hold, i.e.
did not move this activation) + `AttackedFromOverInches(9)`, Actor seat, +1 Hit. Embedded into RobotLegions
+ HumanDefenseForce. `MobileArtilleryShippedDataTests` (5). **Defensive arm DEFERRED, recorded not cut:**
"hasn't moved during the ROUND" needs round-persistent per-unit moved-this-round state readable at the
DEFENSIVE hit hook (fired during the enemy's activation). `UnitActionContext.HasMoved` is per-activation and
about the ACTING unit; `AfterMoving` reads the attacker, not the bearer; and a token granted at
`Movement_OnMoveActionDeclared` is not applied (`ExecuteMoveStage` only *consumes* one-shot grants there, it
doesn't apply new token ops). The arm needs a stage-level "grant a MovedThisRound token when a unit moves"
primitive + a `Not(TokenPresent)` defensive gate - a real change, filed for its own slice. Corpus dead
**234 -> 232** (name now resolves; defensive arm partial).

**Heavy Impact (3 refs) - DONE 2026-07-23 (engine `f739d2c`, super `27fcf08`).** "Counts as Impact(X) with
hits that have AP(1)." `Effect`/`RuleOperation.ChargeImpactHits` gained an `ArmorPenetration` (default 0);
`ImpactSink` folds it as a MAX across sources (the single impact pool can't separate per-source AP - an
edge case no corpus unit hits, since Heavy Impact replaces Impact), and `ResolveImpactHitsStage` builds the
synthetic impact weapon with that AP instead of the hardcoded 0. Data: `Heavy Impact` (engineArgumentCount 1)
into SaurianStarhost + RatmenClans. Tests: engine `ImpactRuleIntegrationTests` +2 (a save of 4 vs Def 4 HOLDS
at AP 0 but FAILS at AP 1 - the contrast pins the AP threading), app `HeavyImpactShippedDataTests` (1, AP=1
pinned). Corpus dead **232 -> 229**.

**Quick Readjustment (2 refs) - DONE 2026-07-23 (engine `e82ec55`, super `46eb0cd`).** "This model ignores
the shoot-after-move penalty when using Indirect weapons." Indirect's penalty is a weapon-scoped -1 gated on
`And(AfterMoving, Not(IsMelee))`. New `Condition.WeaponHasRule` reads the FIRING weapon
(`invocation.Weapon`, meaningful only for a weapon-scoped rule); Quick Readjustment is authored weapon-scoped
(routed onto every weapon by slice 0) and fires its +1 only on the weapon that also carries Indirect,
netting the -1 to 0. Lint's `SatisfyCondition` seeds the named companion rule onto the lint weapon. Data into
HumanInquisition + RatmenClans. Tests: engine `QuickReadjustmentRuleIntegrationTests` (4 - penalty stands
alone, cancelled with QR, QR does NOTHING on a non-Indirect weapon or when stationary), app
`QuickReadjustmentShippedDataTests` (1). Corpus dead **229 -> 227**.

**Hazardous (15 refs) - AP arm DONE, self-wound arm DEFERRED 2026-07-23 (app-side `29f854b`).** "Gets AP(4),
but this weapon's unit takes one wound on unmodified rolls of 1 to hit." The corpus weapons carry
`armorPenetration: 0` in-profile with a bare `Hazardous` rule, so the AP MUST come from the rule.
- **AP(4): pure data** - weapon-scoped Actor-seat `rollModifier(Save, -4)` at `Shooting_OnHitRollComplete`
(Thrust's AP pattern; verified sign against Thrust). Into RatmenClans. Tests `HazardousShippedDataTests` (2).
**Self-wound arm DEFERRED, recorded + flagged:** "takes one wound on unmodified 1s to hit" needs a
mid-attack wound application against the ATTACKER's own unit - a new `Effect.SelfWoundOnUnmodifiedRoll(1)`
reading the hit histogram at face 1, a `RuleOperation.InvokeDealWoundsToUnit` executable, a new
`IOperationServices.DealWoundsToUnit` (mirroring `ApplyWoundsStage`'s DealWounds + `UnitDestructionNotifier`
choke), AND a new `OperationExecutor.Execute` point in `RollToHitStage` (the hit-roll stages fold only sink
ops today; no executable runs there). That is a wound-subsystem hook, not a small primitive - filed for its
own slice. **Balance note: until it lands, Hazardous is upside-only** (AP with no self-harm). Corpus dead
**227 -> 212** (name resolves; self-wound arm partial).

**Speed Feat (4 refs) - DONE 2026-07-23 (engine `82cdea7`, super `a413385`).** "Once per game, when this
unit moves ... you may move +2in on Advance / +4in on Rush/Charge." **Engine change:** `ActivationStartStage`
now offers a SINGLE-ability activation-start rule as an optional Yes/No (declining saves it), mirroring
`DeployUnitStage`; multi-ability rules stay a mandatory pick. Safe because all 4 existing activation-start
rules are multi-ability. Data: `Speed Feat` (once-per-game ability granting `Speed Feat Boost` for the
activation), `Speed Feat Boost` (3 movementBonus entries), `Speed Feat Aura`, into OrcMarauders. Fork
(surfaced): offered at activation start (brace-your-moves) rather than a per-move prompt. Tests: engine
`SpeedFeatRuleIntegrationTests` (3), app `SpeedFeatShippedDataTests` (3). Dead **212 -> 208**. `Speed Feat
Buff` (1, a spell-buff variant) still open.

**Protection Feat (9 refs) - DONE 2026-07-23 (app-side `6dabbc7`, engine test `893e76b`/`daea39b`).** "Once
per game, when this unit takes wounds ... you may roll one die per wound, ignoring each on a 5+." **No engine
change** - reuses Speed Feat's optional-activation machinery + Regeneration's `IgnoreWoundOnRoll`. Fork
(surfaced): a reactive wound-stage prompt would need new interactive infra in the hot combat path (and
`SaveRollCompleteContext` in `AssignWoundsStage` doesn't apply token ops, so a token-consume auto-use model
also needs an engine change AND wastes charges on all-saved attacks). Modelled instead as a **proactive
optional brace**: a once-per-game Yes/No at activation start granting an `UntilNextActivation` roll-per-wound
5+ ignore that covers the opponent's turn. Effect faithful (once-per-game, per-wound 5+, all-models,
optional); only the timing shifts reactive -> brace-in-advance. Into SaurianStarhost + RobotLegions. Tests:
engine `ProtectionFeatRuleIntegrationTests` (3), app `ProtectionFeatShippedDataTests` (3). Dead **208 -> 199**.

**Instinctive (4 refs) - DEFERRED 2026-07-23 (recorded + flagged, not built).** "When activated, if able to
shoot/charge an enemy, this model MUST immediately attack the CLOSEST valid target and gets +1 to hit for
that attack." The defining mechanic is **forced target selection** (must attack the closest), which
`RestrictActions` cannot express (it gates action TYPES - Advance/Rush/Charge/Hold - not targets) and which
would need to override both the human Choose-Action/target flow AND the AI target resolver - genuinely
feature-sized. Shipping only the +1-to-hit rider would invert the rule's character (a mindless compelled
creature becomes a pure buff), so it was NOT shipped buff-only. Filed for its own slice.

### Re-filed OUT of Misc into their own slices (surfaced 2026-07-23, not built here)

Per the triage, three "misc" rules are not small primitives and are re-filed:
- **Repel Ambushers (24)** - "enemy Ambush must set up >12in from this unit." A cross-unit Ambush-arrival
  keep-away constraint, the sibling of `Ambush Beacon` in the open **P22 Ambush variants** slice (same seam:
  Ambush arrival consulting other units' rules). Build with P22.
- **Inquisitorial Agent (20)** - once-per-game self-`reactivate` (the effect exists) PLUS an army-wide "up to
  one third of units with this rule, rounding up, per round" quota - novel army-global state. Own slice.
- **Extended Buff Range (9)** - relay non-spell Hero picks across 24in via another friendly unit with the
  rule. A relational aura-relay (generalized Spell Conduit for non-spell "pick friendly within 12in" rules).
  Own slice.

### Misc slice outcome (2026-07-23)

Shipped or partially shipped 10 of the 13 triaged rules; corpus dead **243 -> 199** (-44). New primitives:
`MostModelsWithinInchesOfTerrain` + `IHasTerrain`, `WeaponHasRule`, `ChargeImpactHits` AP, optional
single-ability activation-start abilities. Deferred sub-arms / rules with recorded reasons + owner flags:
Mobile Artillery defensive arm (moved-this-round state), Hazardous self-wound (mid-attack wound subsystem;
**Hazardous is upside-only until then**), Instinctive (forced target selection). Re-filed: Repel Ambushers,
Inquisitorial Agent, Extended Buff Range. Still open in Misc: Speed Feat Buff (1), and the untouched-this-pass
tail (Grounded auras all done; Screened done). Engine 2023/2023, app 548/548, smokes exit 0.

## Slices — by leverage

Reference counts are corpus-wide (44 books). Primitive numbers are #100's.

| Refs | Slice | Needs | Rules |
|-----:|-------|-------|-------|
| 175 | ~~**P5a** activation-choice hook~~ **DONE 2026-07-23** (175/175) | Shipped: see the P5a write-up above. The deferred `Versatile Defense Aura` (21) closed 2026-07-23 — see the Versatile Defense write-up. | Versatile Attack (56), Versatile Reach Aura (56), Watchborn (42), Versatile Defense Aura (21) |
| 21 | ~~**Versatile Defense** (out of P5a)~~ **DONE 2026-07-23** (21/21) | The filed premise held, with one correction and one addition: no new `TokenClearTrigger` variant was needed (`CustomHook` already says "clears at hook X"), and the deployment arm needed a new `Cost.Free` because the once-per-X "used" marker is keyed on the RULE. Both effects were Sturdy's and Changebound's bodies verbatim. See the write-up above. | Versatile Defense Aura (21) |
| 47 | ~~**Delayed Action** (was P22)~~ **DONE 2026-07-11** | Shipped at unit-selection (pick-then-confirm hold-back), NOT the next-activator seam - see the Delayed Action write-up above. Fork resolved with Chris: holding back does NOT activate the unit (it stays in the pool) and the turn passes to the opponent; once per round per player. | Delayed Action (47) |
| 15 | ~~**Teleport** (was P22)~~ **DONE 2026-07-11** (15 + Teleport Aura 4) | Shipped as a flat-6in menu action - see the Teleport write-up below. The pre-attack-hook / 3in-vs-6in reading was wrong (see write-up); Chris corrected the design in-conversation. | Teleport (15), Teleport Aura (4) |
| 18 | **Ambush variants** (the real P22, + a P21 re-file) | The only genuine deploy-timing work. `Rapid Ambush` (deployable from round 1 — a new `EDeferTiming`), `Ambush Beacon` (relaxes the >9in enemy restriction for OTHER friendly Ambushers within 6in — a cross-unit deployment constraint), `Ambushing Piercing Shot` (Ambush + AP(+1) during the round it arrives — needs deploy-round state). **`Ambush Re-Deployment` (4, re-filed from P21):** "once per game, when this unit ends its activation, remove it and redeploy as if it had Ambush at the start of the next round" - not deploy-phase at all; needs the round-N Ambush arrival these rules build plus an end-of-activation trigger. | Rapid Ambush (4), Ambush Beacon (6), Ambushing Piercing Shot (4), Ambush Re-Deployment (4) |
| 2 | **Surprise Attack** (was P22) | Infiltrate + "the first time this unit is activated, pick one enemy within 6in in LoS and roll X dice; each 2+ deals a hit with AP(1)". Blocked on **P10**'s dice-pool primitive regardless. | Surprise Attack (2) |
| 96 | ~~**New** reposition-at-activation~~ **DONE 2026-07-09** (96/96) | Owner's ruling: a **placement**, not a move — nothing is asked of the path, only of the destination. `PlaceObjectsRequest` gained `MaxDistanceFromStartInches`, a *per-model* radius (0 = unconstrained, so deployment is untouched), honoured by all three resolvers. `Effect.RepositionAtActivation` rolls its die at Apply (Heal's shape) so the op carries a concrete distance; several **sum**, which is how `Rapid Blink Boost` widens D3 to 2D3 as an increment rather than a second prompt. The AI declines by standing still. Engine `5f3c4df`. | Wolfborn (60), Bounding (22), Rapid Blink (8), Bounding Aura (4), Rapid Blink Boost Aura (2) |
| 66 | ~~**P5b** round-start Shaken recovery~~ **DONE 2026-07-09** (66/66) | **The premise was wrong:** `Round_OnRoundStart` is not dormant — `StartOfRoundExtraActionStage.GrantSpellTokens` fires it every round for every living unit (Caster token grants), applying token ops and running executables. So this needed only the effect. New `Effect.ClearTokenOnRoll` -> `InvokeClearTokenOnRoll`, an executable resolved through `IOperationServices`. Rolls with `RollDecisiveFace`, never `Roll(1)` — the outcome is binary, so a histogram would want to remove a *fraction* of a token. Engine `05eb91e`. | Steadfast Aura (28), Battleborn (26), Honor Code (9), Steadfast (3) |
| 46 | ~~**P21** setup-phase re-deploy~~ **DONE 2026-07-22** (46/46; was 60, 14 refs re-filed) | **Misfiled like P22.** Only two of the seven rules were deploy-phase work, both now shipped (write-ups below): **Fanatic (19)** rides the existing `Deployment_OnUnitDeployed` hook (Vanguard's seam) as a placement; **Re-Deployment (27)** is a new post-deployment alternating sub-phase (`ReDeploymentStage`). The other 14 refs were re-filed with reasons (see the re-scope note). | Fanatic (19) + Re-Deployment (27) DONE |
| 59 | ~~**Darkborn** (#102 residual)~~ **DONE 2026-07-11** | It was **only the naming bug** - both mechanics were already built (the "per-target charge debuff doesn't exist" note was stale; #029/#183's `EffectiveChargeDistanceAgainst` powers it). The importer now disambiguates the bare `Darkborn` by army; books patched. See the Darkborn write-up above. | Darkborn (59) |
| 53 | ~~**P15** randomized-branch effect~~ **DONE 2026-07-11** (48/53) | Decisive per-attack-action die (Option A), once per action, threaded via a new `IHasUnpredictableBranch` capability. See the P15 write-up above. **The 2 Mark variants (5 refs) are deferred** - a mark grants after the action-level roll. | Unpredictable Fighter (26), Unpredictable Fighter Aura (11), Unpredictable (5), Unpredictable Shooter Aura (5), Unpredictable Shooter (1) done; Unpredictable Fighter Mark (3) + Unpredictable Shooter Mark (2) deferred |
| 44 | ~~**P10** dice-pool -> hits / auto-wounds~~ **DONE 2026-07-22** (44/44) | It was TWO primitives: an AUTO-WOUND pool (Ravage, Crossing Attack - roll X, each 6+ a direct unsaveable wound) and a rolled multi-target HIT burst (Storm of X - roll 3 decisively, each 2+ picks an enemy taking 3 hits with a rule). Both shipped (write-ups below). Also retired the #164 `dealHits.WithRules` seam's remaining generality (Storm rides the same fold). | Ravage (31) + Crossing Attack (8) + Storm of Change/Lust/Plague/War (5) all DONE |
| 41 | ~~**P13** marker-scaled magnitude~~ **DONE 2026-07-22** (41/41) | Shipped WITHOUT touching `ValueSource` (its context-free `Resolve` stays pure): new effects `tokenScaledRollModifier` / `tokenScaledReduceArmorPenetration` read the bearer's token count at Apply time (steps = count / perMarkers, Fortified's read-side `maxReduction` cap), `GrantToken` gained a grant-time `maxTotal` clamp (the "up to a max. of X markers" clause, spell-token-cap pattern), `ReconcileObjectivesStage` now fires `Round_OnRoundEnd` rules for every living unit before the token sweep (new `RoundEndContext`, reflection-registered), and both Shaken-application sites clear `CustomHook(Morale_OnShakenApplied)` tokens (Fortified's lose-all-on-Shaken, pure data). "On the table" composes from existing conditions: `not(InReserve) and not(EmbarkedIn) and not(OffTableFromForcedMove)`. Authored behind `tokenPresent(marker, minCount: perMarkers)` so RuleFireLint's existing token seeding proves each entry fires. 8 definitions (incl. support base `Defensive Growth`); engine 1820/1820, `TokenScaledMarkerTests` (9, incl. a real-stage round-end firing pin per the #196 consumption lesson). Engine `2efc06e`. | Piercing Frenzy (9), Defensive Frenzy (8), Piercing Growth (6), Precision Frenzy (6), Fortified Growth (6), Precision Growth (5), Defensive Growth Aura (1) |
| 28 | ~~**P14b** spend-for-bonus markers~~ **DONE 2026-07-22** (28/28) | Two marker classes on the ENEMY unit, bonus kind in the token type (mirroring the roll-modifier trio): persistent (`Persistent{Hit,Ap}BonusMarker` — the Target family, counted every attack, never removed) and spendable (`Spendable{Hit,Ap}BonusMarker` — Tag/Spotter). **Owner-ruled 2026-07-22: the spend is PROMPTED, not auto-spent** — `TargetMarkerSpend` asks the attacking player how many to remove (a `StringSelectionRequest`, spend-all listed first so the CLI EOF default and the AI first-option fallback both take the aggressive default; zero-marker attacks never prompt), folded into `DetermineHitRollStage` (skipped while fatigued, like granted buffs) and `DetermineSaveRollsNeededStage` (+net raises the defender's threshold). Placement is data: `Activation_OnPreAttack` abilities over the existing `TargetSelector`/`Cost` machinery; Spotter's "on a 4+ place a marker" is the new `grantTokenOnRoll` effect (decisive die, `InvokeGrantTokenOnRoll` executable, ClearTokenOnRoll's mirror). Engine 1831/1831, `TargetBonusMarkerTests` (11). Engine `d0985e2`. | Precision Target (7), Piercing Tag (6), Precision Spotter (4), Piercing Spotter (4), Precision Tag (4), Piercing Target (3) |
| 27 | ~~**P11** reflect damage~~ **DONE 2026-07-22** (27/27) | A post-melee reflect (write-up below): Retaliate (X hits per wound taken), Deathstrike (X hits per killed model), Self-Destruct (X per participating model + self-kill any survivor), all per-model attribution. | Retaliate (20) + Deathstrike (4) + Self-Destruct (3) DONE |
| 24 | **P17** place / restore a unit | Create a unit or restore destroyed models mid-game. Touches deployment + table-state lifecycle + networking sync. | Spawn (14), Reinforcement (4), Reanimation Aura (3), Split (3) |
| 21 | **P23** casting support — **DONE 2026-07-23 (19/19)** | Rides #034. Caster Group, Spell Accumulator and Spell Conduit all shipped on the capability seam (write-ups above); Casting Buff/Debuff landed under P6. Conduit relays the cast origin, no prompt - the origin is derived from the targets and made visible in the picker, the target rows, the banner and the roll breakdown. | Spell Conduit (9) + Spell Accumulator (7) + Caster Group (3) DONE; Casting Buff/Debuff (2+X) under P6 |
| 20 | ~~**P6** deferred debuff token~~ **DONE 2026-07-23** (20/20, +3 riders) | **The row's premise was mostly wrong** - only 8 of the 20 refs needed a primitive. Four of the five rules ride seams that were already built AND already consumed (Morale/Save granted modifiers, the #153 movement-grant seam, Fortified's AP reduction on the Actor seat) and shipped as pure data. Only `Casting Debuff` had no carrier: new `ERollKind.Cast` + `TokenType.CastRollModifier`, folded into `CastSpellStage`'s threshold. See the P6 write-up above. | Casting Debuff (8), Morale Debuff (4), Piercing Debuff (3), Defense Debuff (3), Speed Debuff (2) DONE + riders Casting Buff (2), Speed Buff (1) |
| 14 | **P8** apply terrain state to target | Force a Dangerous-terrain test / count as standing in terrain. Builds on `countAsInTerrain` + `ApplyNonMovementTerrainEffectsStage`. | Dangerous Terrain Debuff (11), Difficult Terrain Debuff (3) |
| 12 | **P20** action-permission modifiers | (a) allow shooting after Rush; (b) "strikes last", the inverse of live `strikeFirst`. | Quick Shot Aura (5), Quick Shot Mark (4), Unwieldy Debuff (3) |
| 9 | **P7** morale-outcome override | Convert a failed morale test into a pass, then take unignorable self-wounds. | No Retreat Aura (5), No Retreat (3), No Retreat Buff (1) |
| 7 | **P16** one-shot special-attack injection | Once per game, inject one extra attack with an authored weapon profile. | Takedown Strike (5), Takedown Shot (2) |
| 12 | **Strafing** (out of slice 0) | Make `Strafing` the weapon rule the source says it is: movement-hook access to the bearer's weapons, a mid-move "attack with *this* weapon" primitive replacing the fixed 3-hit `InvokeDealHits`, and a once-per-activation weapon-use restriction. Currently allowlisted in `BookRuleScopeTests`. | Strafing (12) |
| 3 | **P19** reactivate another unit | Generalize the live self-`reactivate` to a chosen friendly unit. | Coordinate (3) |
| 2 | **P12** attack-count producer — **DEFERRED 2026-07-22** (owner ruling) | Regenerative Strength's marker GAIN is "one marker per ignored wound", but the Regeneration ignore roll is a histogram: under the probabilistic roller the ignored count is fractional, and token counts are integers — bridging them means int-locking a roll-derived value. Owner chose to keep the dice invariant pristine over a round-per-attack approximation; the 2 refs stay dead until fractional token counts (or another exact mechanism) exist. The attack-count producer seam itself (a fold at `DetermineHitRollStage`'s attackCount, where the code comment already marks the spot) was NOT built unused, per grow-on-demand. The read side's design is settled when this reopens: melee Yes/No prompt per weapon volley ("add +X attacks to this weapon?"), once-gated per activation — the player picks the weapon by accepting on it (owner ruled 2026-07-22 the pick must be prompted, not auto). | Regenerative Strength (2) |
| 6 | **Dash** end-of-activation reposition (re-filed from P21) | Reposition-at-activation's twin at a different trigger: "at the END of this unit's activation, once per round, place all models with this rule within D3+1in of their position." The DONE reposition slice fires the SAME `RepositionAtActivation` placement at activation START (`ActivationStartStage`); Dash needs it at end-of-activation (`Activation_OnEndOfActivation`, a hook that already carries token lifecycle) with a once-per-round gate. Small delta on shipped machinery - not deploy-phase. | Dash (2), Dash Aura (4) |
| 102 | **Misc** small primitives | Each is a one-off; triage before building. Several may collapse into P5/P13. | Repel Ambushers (24, enemy Ambush placement constraint), Inquisitorial Agent (20, once-per-game reactivate), Hazardous (15, self-wound on unmodified 1), Extended Buff Range (9), Protection Feat (8) + Aura (1), Instinctive (4, forced action at activation), Speed Feat Aura (4) + Buff (1), Heavy Impact (3, Impact with AP), Grounded Reinforcement Aura (3), Grounded Precision Aura (3), Mobile Artillery (2, re-filed from P21 - >9in Hold+shoot / hasnt-moved defensive hit mods; likely pure DATA on the built `AttackedFromOverInches` + Hold-action + moved-this-round primitives - verify on build), Quick Readjustment (2, re-filed from P21 - ignore the shoot-after-move penalty for Indirect weapons; a small penalty-ignore), Grounded Stealth (2, "within 1in of terrain" condition), Screened Aura (1) |

## Suggested sequencing

1. **Slice 0** — 157 references of working code, unblocked by one decision. Do it before anything else.
2. **P5b** (round-start) then **P5a** (activation-choice) — P5a alone is 175 refs, the largest single
   engine win, and P5b is the cheapest dormant-hook exercise to prove the pattern.
3. **P13 + P14b + P12 together** — one coherent marker mechanic, or three incompatible ones.
4. ~~**P10**~~ **DONE 2026-07-22** (Ravage + Crossing Attack + Storm; write-ups above).
5. ~~**P21**~~ **DONE 2026-07-22** (Fanatic + Re-Deployment; write-ups above). P22 (Ambush variants) remains.
6. Then the long tail (P6, P8, P11, P15, P17, P20, P7, P16, P19), Darkborn, and the misc triage.

`#196` can run fully in parallel — it touches no engine file.

## Notes

- 2026-07-23: **P23 Spell Conduit DONE (9 refs) - P23 closed at 19/19.** The relay half is a new payload
  on the capability seam (`EnableSpellRelay`), changing where a cast is measured from rather than lending
  a resource; the gating half is Accumulator's exactly. Owner fork resolved by deriving the origin from
  the chosen targets (a relay is never worse than casting unaided) and making it visible at every step -
  picker note, per-target origin label, cast banner, roll breakdown - instead of adding a prompt. New
  shared `CastSupport` neighbour scan (Accumulator + Conduit share the "other friendly unit in range"
  test). Corpus dead count **252 -> 243**. See the write-up above.
- 2026-07-23: **P23 Spell Accumulator DONE (7 refs)** — the first slice built entirely on the capability
  seam: no new hook, no new stage, the whole rule authored as two data entries. New `TokenType`
  (`AccumulatorTokens`, separate for a load-bearing reason), `Effect`/`RuleOperation.EnableSpellLending`,
  and a shared `SpellPurse` that five sites now ask instead of reading a unit's own spell tokens. Also
  removed two stale `RuleCatalogLintTests` allowlist entries the widened lint arm exposed. Corpus dead
  count **259 -> 252**. See the write-up above.
- 2026-07-23: **The capability seam** (write-up above), Chris's call mid-slice and then extended by him
  to the whole codebase: every in-play "does this unit have rule X?" check became "what can this unit
  do?", asked at `Lifecycle_OnCapabilityQuery` and answered by an `Enable*` effect. Covers casting,
  transport, re-deployment, and ChooseActionStage's menu routing (which had marker effects available and
  matched rule NAMES anyway, in a method that already routed StormOfHits by effect). Hero stays a
  build-time structural marker, with the reason recorded rather than dropped.
- 2026-07-23: **P23 Caster Group DONE (3 refs)**. Two of its three sentences needed no code (spell tokens
  are unit-scoped, so the "designate a model / transfer on death" half has no observable content). Two of Caster Group's three sentences needed no code (spell tokens are unit-scoped, so the
  "designate a model / transfer on death" half has no observable content). Also new
  `ValueSource.RuleCarrierCount`. Corpus dead count **262 -> 259**. See the write-up above.
- 2026-07-23: **Versatile Defense DONE (21/21)**, which closes **P5a at 175/175** - the largest slice in
  this item, and the last of its deferred residuals. Two new vocabulary items
  (`ELifetime.UntilNextActivation`, `Cost.Free`), one shared stage helper (`AbilityEffectChoice`, so the
  rule resolves identically at both its hooks), and one latent defect fixed: an ability's
  `AllModelsHaveThisRule` availability gate was silently always-true because `GatherOffers` never handed
  the invocation its `Definition`. The row's premise about a new `TokenClearTrigger` was wrong -
  `CustomHook` already expressed it - and the row missed the cost collision that forced `Cost.Free`. Both
  effects turned out to be Sturdy's and Changebound's bodies verbatim, so no rules work at all. Corpus
  dead count **283 -> 262**. See the Versatile Defense write-up above.
- 2026-07-23: **P6 DONE (20/20, +3 riders)**, and like P21/P22 the row was misfiled - it claimed all five
  rules needed a new one-shot debuff primitive; only `Casting Debuff` (8) did. The other four ride seams
  already built and already consumed and shipped as pure data, which also pulled in `Casting Buff` (2)
  and `Speed Buff` (1). The one primitive is a fourth roll kind: `ERollKind.Cast` +
  `TokenType.CastRollModifier`, folded into `CastSpellStage`'s threshold (engine `6121b13`). Building it
  surfaced two further things: `TargetSelector.RequiredRule` never scanned MODELS, so "pick an enemy with
  Caster" was blind to every hero caster (fixed, engine `c83b1fd`); and scenario tokens could not carry a
  payload, so the live check of a granted modifier was impossible (fixed as #167 tooling, engine
  `4a8e767`). Verified in play at three seeds-identical runs: the same roll of 3 needs 4+ / 5+ / 3+.
  Recorded but NOT fixed: supplement-rule grants die across a save/load resume - pre-existing and
  class-wide (proved with a shipped-Buff control), filed against #095. Corpus dead **306 -> 283**.
  See the P6 write-up above.
- 2026-07-22: **P11 reflect damage DONE (27/27)** across two slices (engine `163a2f3` + `9a4dbeb`). A
  post-melee reflect: `ResolveMeleeReflectStage` (new `MeleeStage` child after consolidation) deals X hits
  back at the melee attacker through the real save/wound pipeline, batches looping like Storm - Retaliate
  per wound taken, Deathstrike per killed model, Self-Destruct per participating model (also self-killing any
  survivor via the destruction choke). Owner ruled PER-MODEL attribution, made exact by a new per-model
  start-wounds snapshot on `CombatActionContext`; hit count stays fractional (dice invariant). Marker rules,
  fire-lint allowlisted. Corpus dead **333 -> 306**. See the P11 write-up above.
- 2026-07-22: **P21 DONE (46/46), after re-scoping it (misfiled like P22).** The row's 60 refs were not
  one mechanic: only Re-Deployment (27) and Fanatic (19) were deploy-phase work; the other 14 were re-filed
  (Dash/Dash Aura 6 -> end-of-activation reposition; Ambush Re-Deployment 4 -> Ambush variants; Mobile
  Artillery 2 + Quick Readjustment 2 -> Misc/data). Both deploy-phase rules shipped (write-ups above):
  **Fanatic** (engine `599be98`) rides the existing `Deployment_OnUnitDeployed` hook (Vanguard's seam) as a
  placement - new `Effect.RepositionOnDeploy` -> the shared `RepositionModels` op, folded into a within-9in
  placement by `DeployUnitStage` via a new shared `RepositionPlacement` helper extracted from
  `ActivationStartStage`. **Re-Deployment** (engine `3c2d340`) is a new post-deployment alternating sub-phase
  (`ReDeploymentStage`): 2 redeploys per Re-Deployment unit (stacking), players alternate in roll order,
  set-aside units ineligible. Corpus dead **379 -> 333**. See the P21 re-scope note and both write-ups.
- 2026-07-22: **P10b Storm of X shipped** (5 refs; engine `dcace2d`), completing P10 (44/44). A rolled
  multi-target hit burst: a new `StormStage` (routed from Choose Action like Teleport) rolls a 3-dice pool
  DECISIVELY - integer successes, since you cannot pick a fractional target (the dice invariant, per P15) -
  then per success the player picks an enemy within 12in that takes 3 hits with the storm's rule through
  the #164 fold, the per-target batches looping via `OnBatchDone` re-entering the stage. Owner-ruled
  per-success distinct targeting (not one scaled target). New `Effect.StormOfHits` + `InvokeStorm`; catalog
  Storm of Change/Lust/Plague/War. Corpus dead **384 -> 379**. See the P10b write-up above.
- 2026-07-22: **P10a Crossing Attack shipped** (8 refs; engine `3ee6896`), completing the auto-wound half
  of P10 (Ravage + Crossing = 39/44). Reuses Ravage's `DealAutoWounds` primitive on the movement trigger:
  a new `CrossingAttackStage` beside `StrafingStage`, each filtering offers to its own effect type
  (`DealHits` vs `DealAutoWounds`) so they don't cross-consume. Also threaded activated-ability arguments
  through `ResolveAbility` (Crossing Attack(X) is the first arg-driven ability) - a small, backward-
  compatible `AbilityOffer.Arguments` addition. Only Storm of X (5, rolled hit-count) remains in P10.
  Corpus dead **392 -> 384**. See the P10a write-up above.
- 2026-07-22: **P10a Ravage shipped** (31 refs; engine `1340496`). Building the slice surfaced that P10
  is two primitives, not one - an auto-wound pool (Ravage, Crossing Attack) and a rolled hit-count (Storm
  of X) - because the source text (off-repo, not in the book data) reads "takes one wound" for the first
  and "takes 3 hits with [rule]" for the second. Built the auto-wound primitive on Ravage: it mirrors
  Impact (`ResolveImpactHitsStage`) but at threshold 6 and skipping the save, so the wounds are unsaveable
  yet still regenerable (owner ruling). New `DealAutoWounds` effect + `InvokeDealAutoWounds` op +
  `SyntheticWoundResolution` helper; per-model scaling via the carrier-count pattern (no Sergeant-style
  attribution problem - wounds hit the enemy). Fractional-count invariant pinned in probabilistic mode.
  Crossing Attack (8, same primitive, movement trigger) and Storm (5) remain. Corpus dead **423 -> 392**.
  See the P10a write-up above.
- 2026-07-22: **The marker cluster shipped as one coherent mechanic** (P13 + P14b together, per this
  file's own sequencing warning; P12 deferred): corpus dead count **492 -> 423** (-69 of the cluster's
  71; Regenerative Strength's 2 remain). Details in the three slice rows. Fork decisions made with
  Chris this session, all on the fidelity side:
  - **Tag/Spotter marker spend is PROMPTED**, not auto-spent (a `StringSelectionRequest` to the
    attacking player, spend-all first so automated resolvers default aggressively). No new request
    type or app-side resolver was needed — CLI/GUI/AI all already resolve string selections.
  - **Regenerative Strength's weapon pick must be prompted too**; design settled (per-volley Yes/No,
    once-gated) but the rule itself then hit the dice invariant — fractional ignored wounds cannot
    become integer markers without int-locking — and Chris chose deferral over a rounding
    approximation. See the P12 row.
  - Sequencing note for whoever picks up P12: token-scaled magnitudes (P13's effects), the
    attacker-bonus claim (`TargetMarkerSpend`), and `grantTokenOnRoll` now exist — Vengeance (the
    other P13 coupling) still additionally needs its model-count magnitude source.
- 2026-07-11: **P15 (Unpredictable) shipped** (48 of 53 refs; engine only). A per-attack-action decisive
  die (1-3 -> AP(+1), 4-6 -> +1 to hit), rolled once per action and threaded to both the hit hook (72) and
  the save hook (73) via a new `IHasUnpredictableBranch` capability so both arms read the SAME roll. Forks
  resolved with Chris: decisive (not weighted) roll, once per action (not per weapon). The 2 Mark variants
  (5 refs) are deferred - a mark grants after the action-level roll. Corpus dead count **576 -> 528**. See
  the P15 write-up above.
- 2026-07-11: **Darkborn shipped** (59 refs; engine importer + book data). It was only the naming bug -
  both mechanics were already built (#029/#183's charge-distance debuff powers defensive Darkborn; the old
  "doesn't exist" note was stale). OPR names both armies' rules bare "Darkborn"; the importer now
  disambiguates by army name (Dark Brothers -> Defensive, Dark Prime Brothers -> Offensive) and the two
  bundled books were patched. Corpus dead count **635 -> 576**. See the Darkborn write-up above.
- 2026-07-11: **Delayed Action shipped** (47 refs; engine `597a43e`). Hold-back at unit selection
  (pick-then-confirm), NOT an action-menu button - see the Delayed Action write-up above. Corpus dead count
  682 -> 635.
- 2026-07-11: **Teleport shipped** (15 + Teleport Aura 4; engine `a84c56b`), after **#206 proximity Pass
  gate** (engine `6053061`) which it depends on. Corpus dead count 701 -> 682. See the Teleport write-up
  above - flat 6in menu action, not the pre-attack 3in/6in the row first assumed (Chris corrected both).
- 2026-07-09: **Reposition-at-activation shipped** (96 refs; engine `5f3c4df`, app below). Corpus dead count
  797 -> 701. Owner ruled it a placement rather than a move, so it rides a new per-model radius on
  `PlaceObjectsRequest` instead of `InvokeTriggeredMove`. Verified in a real headless Wolf Brothers game, not
  just by the lint: the D3 is rolled per activation and the placement request reaches the resolvers.
  **It also exposed two gaps P5a introduced:** `ActivationStartStage` never evaluated PASSIVE entries at its
  hook, and never ran `OperationExecutor` — so an ability there with an imperative effect would have been
  silently dropped, and the new lint map's `Activation_OnActivationStart` arm was a false pass. Both fixed.
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
