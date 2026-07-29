# 197 — Faction rule coverage, part 2: engine primitives + the scope-mismatch bug

**Status**: in progress. Corpus dead references **2,342 -> 67** of 13,870 (0.5%), 12 names.
**Related**: #196 (data-only half, closed 2026-07-22), #100 (primitive catalog), #102, #034, #042, #093, #095, `SpecialRulesAudit.md`

> **Compacted 2026-07-28.** Shipped slices are summarized to the durable facts: the seam/vocabulary
> they created, owner rulings, and anything deferred. Test counts, suite tallies, mutation-check
> narratives and in-play transcripts were dropped — they live in git history and the commits named
> per slice. Nothing deferred was dropped; every open item is in "Open work" below.

## Goal

The half of the faction-rule audit that **cannot be authored as data** because the primitive, hook or
stage wiring does not exist — originally 97 dead names / 942 refs — plus one regression where
*already-implemented* rules never attached (slice 0, 157 refs).

Done = each slice ships its primitive with an integration test mirroring the nearest existing
`*RuleIntegrationTests`, or is explicitly re-deferred with a recorded reason.

## Why this half is heavier than #196

- **It edits the `FutureOfDarkGrimness` submodule.** Submodule-first cadence, cross-repo pointer bumps.
- **It invents vocabulary.** A new effect needs a `RuleOperation`, an applier that executes it, `RuleJson`
  derived-type registration, `HookContextCatalog` capability wiring, and `RuleFireLint` support. Miss one
  and you reproduce the Breath Attack failure: validates, registers, does nothing.
- **It contains design forks** that must be surfaced with tradeoffs and signed off before building.
- **The dice invariant is live.** Modifiers are a threshold shift on `RollDecisive`, never a post-roll
  adjustment, or probabilistic (histogram) mode diverges from realistic mode.

---

# Open work

Ref counts are live from `--rule-coverage FdgRaylib/Assets/Books` (2026-07-29). **67 dead across 12 names,
all of them `no-definition` - the `scope-mismatch` category is empty for the first time since slice 0.**

| Refs | Slice | What it needs | Rules |
|-----:|-------|---------------|-------|
| 20 | **Inquisitorial Agent** (re-filed from Misc) | Once-per-game self-`reactivate` (the effect exists) PLUS an army-wide "up to one third of units with this rule, rounding up, per round" quota — novel army-global state. | Inquisitorial Agent (20) |
| 12 | **Sergeant** — per-model rule attribution (#196 F16 handoff) | OPR `8HWdOwMYcI0p`: "when this MODEL attacks, unmodified 6s to hit deal 1 extra hit" — a one-model champion upgrade. `ListCompiler` attaches `RulesGained` to `unit.SpecialRules` and hit rolls fold over the whole unit's pool, so a data definition over-grants ~10x. Owner ruled 2026-07-22: must apply to the one model only. Needs a per-model attachment + a hit-roll seam scoping an extra-hit effect to the bearer model's own attacks. **P11's per-model start-wounds snapshot solved the analogous problem by before/after comparison; the hit-roll path has no such seam.** | Sergeant (12) |
| 11 | **Armor(X) defense floor** (#196 F16 handoff) | OPR `74RjQ1k41DoO`: "counts as having Defense X+" — a stat SET with a varying rating. No Defense-side analog of `qualityFloor`, and data effects carry fixed authored values. Needs a defense-floor effect reading `Arg(0)` (or engine-side stat handling a la Tough). #196 shipped a zero-hook marker-with-arg definition so the name resolves and the description shows — **so these 11 refs do NOT appear in the dead count, but the mechanic is absent.** | Armor (11) |
| 9 | **Extended Buff Range** (re-filed from Misc) | Relay non-spell Hero picks across 24in via another friendly unit with the rule — a relational aura-relay, i.e. generalized Spell Conduit for non-spell "pick friendly within 12in" rules. **Conduit's `CastSupport` neighbour scan and the `EnableSpellRelay` shape are the template.** | Extended Buff Range (9) |
| 7 | **P16** one-shot special-attack injection | Once per game, inject one extra attack with an authored weapon profile. | Takedown Strike (5), Takedown Shot (2) |
| 5 | **Unpredictable Marks** (P15 residual) | A mark grants Unpredictable at the hit-roll hook, AFTER `UnpredictableBranchResolver`'s action-level roll, so the mark-granted rule is invisible to it. Needs the resolver to also scan the DEFENDER for an Unpredictable-granting mark at action time. | Unpredictable Fighter Mark (3), Unpredictable Shooter Mark (2) |
| 4 | **Instinctive** — DEFERRED 2026-07-23 | "When activated, if able to shoot/charge, this model MUST attack the CLOSEST valid target, +1 to hit for that attack." The defining mechanic is **forced target selection**, which `RestrictActions` cannot express (it gates action TYPES, not targets) and which must override both the human Choose-Action/target flow AND the AI target resolver — feature-sized. Shipping the +1 rider alone would invert the rule's character (a compelled creature becomes a pure buff), so it was deliberately NOT shipped buff-only. | Instinctive (4) |
| 3 | **P19** reactivate another unit | Generalize the live self-`reactivate` to a chosen friendly unit. | Coordinate (3) |
| 3 | **Vengeance** | "Place N markers on the unit that destroyed this one, N = models with this rule at game start; friendly units get +N to hit vs the marker count." P13's marker-scaled magnitude now exists and covers the read side; **still needs a magnitude source for "count of models with rule X in the bearer unit at game start"** — `ValueSource.RuleCarrierCount` (P23) counts LIVING carriers now, not at game start. | Vengeance (3) |
| 2 | **Surprise Attack** — now UNBLOCKED | Infiltrate + "the first time this unit is activated, pick one enemy within 6in in LoS and roll X dice; each 2+ deals a hit with AP(1)". Was filed as blocked on P10; **P10 is DONE**, so `StormOfHits` (rolled pool -> hits, threshold + AP + range config) is very close to this shape — single-target rather than per-success picking. | Surprise Attack (2) |
| 2 | **P12** attack-count producer — DEFERRED 2026-07-22 (owner ruling) | Regenerative Strength's marker GAIN is "one marker per ignored wound", but the Regeneration ignore roll is a histogram: under the probabilistic roller the ignored count is fractional, and token counts are integers — bridging them means int-locking a roll-derived value. Owner chose to keep the dice invariant pristine over a rounding approximation. The producer seam (a fold at `DetermineHitRollStage`'s attackCount, where a code comment marks the spot) was NOT built unused, per grow-on-demand. **Read side is settled for when this reopens:** melee Yes/No per weapon volley ("add +X attacks to this weapon?"), once-gated per activation — the player picks the weapon by accepting on it (owner-ruled: prompted, not auto). | Regenerative Strength (2) |

## Deferred sub-arms of shipped rules (name resolves; mechanic partial)

These do **not** show in the dead count. Recorded here so they are not silently lost.

- **Hazardous self-wound arm** (15 refs) — "takes one wound on unmodified 1s to hit". Needs
  `Effect.SelfWoundOnUnmodifiedRoll(1)` reading the hit histogram at face 1, a
  `RuleOperation.InvokeDealWoundsToUnit`, an `IOperationServices.DealWoundsToUnit` (mirroring
  `ApplyWoundsStage` + the `UnitDestructionNotifier` choke), AND a new `OperationExecutor.Execute` point
  in `RollToHitStage` — the hit-roll stages fold only sink ops today, no executable runs there. A
  wound-subsystem hook, not a small primitive. **Balance flag: until it lands, Hazardous is upside-only**
  (AP(4) with no self-harm).
- **Mobile Artillery defensive arm** (2 refs) — "as long as this unit hasn't moved during the ROUND,
  enemies shooting it from over 9in get -2". Needs round-persistent per-unit moved-this-round state
  readable at the DEFENSIVE hit hook (fired during the enemy's activation). `UnitActionContext.HasMoved`
  is per-activation and about the ACTING unit; `AfterMoving` reads the attacker, not the bearer; and a
  token granted at `Movement_OnMoveActionDeclared` is never applied (`ExecuteMoveStage` only *consumes*
  one-shot grants there). Needs a stage-level "grant a MovedThisRound token on move" + `Not(TokenPresent)`.
- **Ravage strike-back** — a Ravage unit that is CHARGED does not roll on its strike-back; only the
  charger triggers the stage, mirroring Impact's charge-only scope.
- **Reinforcement via transport spillout** — `TransportUtilities.ApplySpilloutEffects` is deliberately
  Stages-free, so it does not fire the Shaken arm. A spilled-out Reinforcement unit is only offered when
  destroyed. Wire via `SpilloutExecutor` if play hits it.
- **Speed Feat Buff** (1) — the spell-buff variant of the shipped Speed Feat.

## Tooling / hygiene found here, not fixed

- **`RuleFireLint.Check` returns at the FIRST passive entry that produces operations**, so a rule's later
  entries are never lint-checked. Consistent with what it claims to test, but a dead second entry on a
  live rule is invisible. Worth its own item if a per-entry check is wanted.
- **The CLI army-file prompt loops forever on EOF** when the file fails to load (a stale probe army
  produced a 5.8 GB log before timeout). It should abort at EOF like every other resolver.

---

# Standing lessons (cross-slice, earned the hard way)

1. **Verify in play, not just by lint.** `--validate-rules` checks structure; `RuleFireLint` proves an
   entry *can* fire — neither proves its operations are *consumed*, nor what several rules *sum to*.
   Three shipped #196 rules passed both and were no-ops. Every slice since ends with a headless probe.
2. **A Boost is authored as the INCREMENT.** The corpus writes a Boost as the *boosted rule* ("extra hits
   on 5-6"), but the engine composes base + Boost **additively** (`HitInjectionSink`, `RollModifierSink`,
   `MovementModifierSink` all add; only `WoundIgnoreSink` takes the min). 45 corpus units carry both a
   base and its Boost. `FdgRaylib.Tests/BoostRuleCompositionTests` asserts the **net** effect.
3. **Mutation-test your own tests.** Repeatedly, query-level tests passed while the STAGES silently lost
   the behaviour (capability seam, Caster Group), and an AI test was vacuous because the fan-out lane
   landed far from the enemy (P22a). If reverting the change leaves green, the test is not pinning it.
4. **The filed premise is often wrong.** Darkborn (already built, only a naming bug), P5b (hook not
   dormant), P6 (4 of 5 rules rode built seams), P21/P22 (misfiled as one slice each), Versatile Defense
   (no new clear-trigger needed), Rapid Ambush (a field, not a new timing). **Check the source text and
   the engine before designing.**
5. **Decisive vs fractional.** A roll whose outcome selects a branch, a target count, or a binary
   token removal is `RollDecisiveFace` even in probabilistic mode (P15 branch, P10b pool, P5b recovery,
   Reanimation restore). A roll that produces hits/wounds stays fractional. Never int-lock the latter.

---

# Shipped — the seams that exist now

Read this section to find machinery to reuse. Each entry: what it created, owner rulings, dead-count delta.

## Attachment & scope

**Slice 0 — the scope-mismatch bug — DONE 2026-07-09** (145 of 157 refs; engine `8cdca83`)
`ArmyListRuleResolution.ResolveForScope` refused any rule whose `SpecialRuleDefinition.Scope` differed
from its attachment site. Catalog and corpus disagreed for ten rules. `Precise` (64) and `Thrust` (62)
re-scoped to `Weapon`; the rest routed onto the unit's weapons. **`ListCompiler` now honours
`UpgradeSection.Targets`** — a targeted upgrade's weapon-scoped rules attach to the named weapon only,
splitting the `WeaponFileEntry` when the upgrade buys fewer copies than the unit carries. This fixed 23
further `Precise` refs that were resolving but landing on the wrong weapons (a melee CCW getting a
shooting rule). No book data changed, so a re-import cannot reintroduce it. `ResolveAnyScope` splits
name+arity resolution from the scope gate. Guards: `WeaponScopedWargearRoutingTests` (engine, asserts
*which* weapon carries *which* rule), `BookRuleScopeTests` (app, walks all 47 books, allowlist fails when
a listed rule starts resolving cleanly). **Deferred: `Strafing`** — closed 2026-07-28, see Combat primitives.
The allowlist is empty now; it and its stale-entry guard are kept for the next re-import.

**Darkborn — DONE 2026-07-11** (59 refs). **Only a naming bug** — both mechanics were already built
(#029/#183's `EffectiveChargeDistanceAgainst` powers defensive Darkborn). OPR reuses the bare name for
two different rules; `OprBookImporter.DisambiguateAmbiguousRuleNames` routes by owning army (Dark
Brothers -> Defensive, Dark Prime Brothers -> Offensive) across every reference site, so a re-import
stays correct. The two bundled books were patched by targeted string replace, not regenerated (they carry
hand-authored #197 rules a re-import would clobber). **635 -> 576.**

## Distance & geometry

**The ">9in shot or charged" gate — DONE 2026-07-09** (engine `bf6353d`, `e677f1e`; app `27c55c4`)
Twelve rules read "shot **or charged** from over 9in away". `Condition.DistanceGreaterThan` reads LIVE
distance and melee resolves in base contact, so the charge arm of all twelve was dead. **Owner sign-off:**
the 9in measures the distance when the charge is *declared*, expressed as one condition.
New **`IHasAttackOriginDistance`** — live distance when shooting; distance to the defender at
**activation start** when charging (this engine models Charge as the melee attack and the approach as a
separate Move, so activation start is when the unit "sets off"; `UnitActionContext.Reset` snapshots min
distance to every enemy); 0 for a non-charging swing, so a strike-back never inherits the charger's
launch. Read by **`Condition.AttackedFromOverInches(X)`** on the hit-modifier, hit-complete and
save-complete contexts; the last also gained `DistanceInches`/`IHasDistance`.
Building it surfaced **three defect classes in #196's shipped data** — see Standing lesson 2, plus two
rules emitting `rollModifier(Hit)` at `Shooting_OnHitRollComplete` where only `Save` deltas fold.

**Grounded family — DONE 2026-07-23** (8 refs; engine `2d90fac`). One shared primitive:
**`Condition.MostModelsWithinInchesOfTerrain`**. Terrain reaches the condition as a new **`IHasTerrain`**
capability on the two hit contexts, populated from `GameContext.TableState.Terrain` (empty on
AI-valuation / synthetic-hit paths — conservative: the bonus is only ever omitted). Shared
`TerrainProximityQueries` measures from the base EDGE, strict living-model majority. `HookContextCatalog`
is reflection-based, so implementing `IHasTerrain` auto-registered it. **242 -> 234.**

**`Condition.WeaponHasRule` (Quick Readjustment) — DONE 2026-07-23** (2 refs; engine `e82ec55`). Reads the
FIRING weapon (`invocation.Weapon`, meaningful only for a weapon-scoped rule). QR is authored
weapon-scoped and fires +1 only on the weapon that also carries Indirect, netting its -1 to 0.
**229 -> 227.**

## The capability seam (the most reused thing in this item)

**DONE 2026-07-23** — no refs; an architecture fix Chris called mid-slice and extended codebase-wide.

Every in-play "does this unit have rule X?" became "what can this unit do?". One hook,
**`EHookID.Lifecycle_OnCapabilityQuery` + `CapabilityQueryContext`**, read through
**`CapabilityRuleQueries`** (the `RangeRuleQueries`/`SightRuleQueries`/`MovementRuleQueries` pattern). A
rule answers by emitting an `Enable*` operation; **nothing applies it — its presence in the queue IS the
answer.** One hook rather than one per capability: the operations discriminate, and being an ordinary
hook each answer respects the entry's `Condition` and rule suppression.

Two things an identity check cannot do, both of which bit: a SECOND rule conferring the same thing is
invisible, and a capability depending on LIVE state cannot be expressed at all.

| Site | Was | Now |
|---|---|---|
| `SpellTargeting.IsCaster` | `== CoreRuleCatalog.Caster` | `Effect.EnableCasting` |
| `TransportUtilities.IsTransport` / `GetCapacity` | `== Transport` + `Arg(0)` | `Effect.EnableTransport(ValueSource)`, capacity riding the answer |
| `ReDeploymentStage.HasReDeployment` | `== ReDeployment` | `Effect.EnableReDeployment` |
| `ChooseActionStage` Disembark/Embark/Teleport routing | `offer.RuleName == <name>` | `offer.Ability.Effect is Effect.X` |

The routing one was the most galling: all three already carried a marker `Effect` for exactly that
purpose, and the same method already routed `StormOfHits` by effect.

**Deliberately NOT converted, with reasons:** `Hero` (army-BUILD-time structural marker, resolved before
any rule dispatch exists, deliberately hook-less per #006 slice F); `Condition.UnitHasRule` /
`TargetHasRule` / `TargetSelector.RequiredRule` / `Effect.IgnoreRule` (these name a rule because the
CORPUS TEXT does — authored data, not an engine assumption); `ListValidator`, `DefaultBaseEstimator`,
`ArmyForgeScreen.IsCaster` (book/roster data, no rule graph exists yet).

Later payloads on the same seam: `EnableSpellLending` (Accumulator), `EnableSpellRelay` (Conduit),
`RepelAmbushers` / `AmbushBeacon` (P22a).

## Activation & ability seams

**P5a activation-choice hook — DONE 2026-07-09/23** (175/175; engine `df234bc`, `90ba258`; app `6dbd31c`)
**Owner sign-off:** label the abilities (not a new `Effect.ChooseOne`); give the choice its own request
type. New **`ActivationStartStage`** (`MainUnitActionStage`'s starting child, binding on to
`ChooseActionStage`; runs once per activation — loop-backs return to ChooseAction, not here).
**`ActivatedAbility.Label`** distinguishes sibling abilities at one hook; the once-per-X `Cost` is keyed
on the **rule** name, so taking one effect spends the gate for its siblings — exactly "pick one".
**`ChooseAbilityEffectRequest`** replies with the chosen option's index; its own request type because
`docs/ai-agent-plan.md` A4 replaces AI resolvers one request type at a time. Options are plain data
(requests cross the wire via Newtonsoft, which cannot round-trip a polymorphic `Effect` graph).
Chosen effects grant a helper rule with `addRule(ThisActivation)` — no new effect kind.
**Latent defect fixed:** `RuleEvaluator.GatherOffers` read only own rules, never resolving `RuleGrant`
tokens, so an aura conferring an **ability-only** rule did nothing — the Breath Attack failure one level
up. **1,018 -> 864.**

**Optional single-ability activation-start (Speed Feat) — DONE 2026-07-23** (engine `82cdea7`).
`ActivationStartStage` now offers a SINGLE-ability rule as an optional Yes/No (declining saves it),
mirroring `DeployUnitStage`; multi-ability rules stay a mandatory pick. Safe because all 4 pre-existing
activation-start rules are multi-ability.

**`AbilityEffectChoice`** (shared stage helper, from Versatile Defense) — the "group a hook's offers by
rule -> ask which effect -> resolve/apply/execute" block, so a rule resolves identically at every hook it
uses. `DeployUnitStage` routes MULTI-ability groups through it and keeps its Yes/No for single-ability
"you MAY" rules (Vanguard, Fanatic).

**Dash — DONE 2026-07-28** (6 refs; engine `c8b28ab`). Reposition-at-activation's twin at the far end of
the activation, riding P22d's seam. The effect is **Bounding's body verbatim** (`repositionAtActivation`,
D3, `plusInches: 1`), so this was prompting-and-plumbing work: `ReconcileEndOfActivationStage` gathered
offers but never folded `RuleOperation.RepositionModels`, the third and last trigger of the family
(`ActivationStartStage` and `DeployUnitStage` already did). Authored as an **activated** ability rather
than a passive entry like its three siblings, because only the ability path can express "once per round" —
which matters exactly when a unit is reactivated.

**Owner sign-off (2026-07-28): the placement is the only prompt.** The seam asks single-ability rules a
Yes/No defaulting to NO, which is right for Ambush Re-Deployment's self-removal and wrong for a free
repositioning buff — it would double-prompt a human and permanently decline the rule for every automated
resolver. New `RepositionPlacement.IsCancellablePlacement`: an ability whose effect already asks a
cancellable question skips the Yes/No. **Recorded, not silently unified:** `DeployUnitStage` keeps its
unconditional Yes/No for Vanguard and Fanatic — that prompting is shipped behaviour its tests pin, and
changing it was out of scope here. **Recorded consequence:** the cost is emitted when the ability
resolves, so cancelling the placement still spends the round's use (same as Vanguard/Fanatic at
deployment today); pinned by test so a change of heart fails loudly.

Data (app-side, supplement): `Dash` + `Dash Aura`; all 6 refs are Custodian Brothers' Envoy Banner option.
**127 dead.**

**End-of-activation ability seam — DONE 2026-07-28** (from P22d; **this is what Dash rides**).
`ReconcileEndOfActivationStage` gathers offers at a new **`Activation_OnEndOfActivation` +
`ActivationEndContext`**, BEFORE its token sweep, via `AbilityEffectChoice`. **One deliberate difference,
recorded in the stage doc: the Yes/No defaults to NO** — at activation start the lone optional ability is
a buff and an aggressive EOF/AI default suits it; here the only corpus ability is a once-per-game
self-removal, which an auto-accepting AI would fire on every unit's first activation. A unit wiped out
during its own activation is never offered.

**Versatile Defense — DONE 2026-07-23** (21/21, the P5a residual). New **`ELifetime.UntilNextActivation`**
(mapped onto the existing `CustomHook(Activation_OnActivationStart)` — no new `TokenClearTrigger`; the
filed premise was wrong) with one `ClearForHook` call at the top of `ActivationStartStage`, which must run
BEFORE offers are gathered or a re-picking unit holds both effects. New **`Cost.Free`** — the once-per-X
"used" marker is keyed on the RULE, so `OncePerActivation` paid at deployment leaves a marker that shuts
the unit's own first activation; the deployment hook fires exactly once per unit, so no gate is needed.
**Latent defect fixed:** `GatherOffersFromRules` built its `RuleInvocation` without a `Definition`, so the
self-referential `Condition.AllModelsHaveThisRule` took its "no identity to check" arm and returned
**true** — an ability's all-models gate was no gate at all. Both effects were Sturdy's and Changebound's
bodies verbatim, so this was lifetime-and-plumbing work, not rules work. **283 -> 262.**

**Reposition-at-activation — DONE 2026-07-09** (96 refs; engine `5f3c4df`). **Owner ruling: a PLACEMENT,
not a move** — nothing is asked of the path, only of the destination. `PlaceObjectsRequest` gained
**`MaxDistanceFromStartInches`**, a *per-model* radius (0 = unconstrained, so deployment is untouched),
honoured by all three resolvers. `Effect.RepositionAtActivation` rolls its die at Apply (Heal's shape) so
the op carries a concrete distance; several **sum**, which is how `Rapid Blink Boost` widens D3 to 2D3 as
an increment rather than a second prompt. Also exposed two gaps P5a had introduced: `ActivationStartStage`
never evaluated PASSIVE entries at its hook and never ran `OperationExecutor`. **797 -> 701.**

**Teleport — DONE 2026-07-11** (15 + Aura 4; engine `a84c56b`). Chris corrected two filed premises before
build: **flat 6in radius** (not action-conditional), and **a Choose Action MENU option, not a pre-attack
hook** (the pre-attack stage reports `Hold` for all shooting and never fires on a Rush). Routed by name to
a new `TeleportStage` (the Disembark/Embark pattern) which repositions 6in and LOOPS BACK so Charge/Shoot/
Pass re-evaluate from the new position. Fully layered — sets neither HasMoved nor HasAttacked. Cost paid
only on an ACCEPTED placement. Depended on **#206** (proximity Pass gate, engine `6053061`), built first.
**701 -> 682.**

**Delayed Action — DONE 2026-07-11** (47 refs; engine `597a43e`). An activation-ORDER rule. Forks settled
with Chris: "pass its turn" is DISTINCT from the Pass/Hold action — the unit does NOT activate, it stays
in the pool and the turn passes to the opponent; placement is at **unit-selection** (pick-then-confirm
Yes/No), not an action-menu button, because the condition is only knowable at turn level and "don't
activate at all" isn't representable inside a unit's action menu — this kept the shared unit-selection
resolvers untouched; budget is once per round per PLAYER (a `DelayedActionUsed` token scan).
`SingleTurnStage` snapshots `OpponentHasMoreUnitsToActivate`; `ReconcileChildContextBeforeLeaving` skips
`MarkUnitAsActivated` when delayed. Terminates because the per-round-per-player token is finite.
**682 -> 635.**

## Deployment

**Fanatic — DONE 2026-07-22** (19 refs; engine `599be98`). Rides the existing `Deployment_OnUnitDeployed`
hook (**Vanguard's seam**). New `Effect.RepositionOnDeploy(MaxInches)` -> the shared
`RuleOperation.RepositionModels`; flat, not a degenerate `DiceExpression` (every one rolls at least one
die). New shared **`RepositionPlacement`** helper, extracted verbatim from
`ActivationStartStage.OfferReposition`, so activation-start and deploy run ONE implementation.
**379 -> 360.**

**Re-Deployment — DONE 2026-07-22** (27 refs; engine `3c2d340`). New **`ReDeploymentStage`**, a child of
`DeployAllUnitsStage` inserted after the deploy loop and BEFORE `PlaceDeferredUnitsStage`, so set-aside
(Scout) units are still off-table and therefore ineligible — exactly "excluding units that were set
aside". **Owner ruling: 2 redeploys per Re-Deployment unit owned, stacking.** Players alternate one unit
at a time in activation order (`FirstDeploymentRollOrder`'s head both deploys and activates first);
round-robin, a pass ends participation, terminates because each cycle marks a player done or spends a
finite budget. Faithful for the 1v1 corpus; multi-player is an approximation over the flat order.
**360 -> 333.**

**P22 Ambush variants — DONE 2026-07-28** (42/42). Four sub-slices:

- **P22a Repel Ambushers + Ambush Beacon (30 refs).** **Owner sign-offs:** the Beacon waiver is judged PER
  arriving MODEL; it overrides BOTH restriction kinds (the flat over-9in rule and Repel's 12in); the
  arrival scans became side-aware. **The shape: constraint discs on the request + one legality authority.**
  `PlaceObjectsRequest` gained `EnemyKeepOutDiscs` / `EnemyDistanceWaiverDiscs` (new
  `PlacementDisc(Center, RadiusInches)`, snapshotted at request build — nothing moves during a placement).
  New **`PlacementDistanceRules`** is the single authority combining flat minimum + keep-outs + waivers
  (waiver wins; keep-outs exclusive at the boundary like the over-9in rule, waiver inclusive). All four
  placement resolvers judge enemy distance through it. Both rules are capability answers;
  **`AmbushArrivalRules`** turns them into discs — side-aware (`ITeamExtensions.AreAllied`; friendly/enemy
  is relative to the ARRIVING unit), living models only, reserve units project nothing, one disc per
  living model. **Found while scoping: the CLI resolver's enemy scan was player-based** (teammates counted
  as enemies), while GUI/AI/Tactician were already side-aware — fixed. GUI: the no-go blob paints each
  keep-out disc and ERASES the beacon bubbles (custom zero-src blend — the hole IS the semantics) with a
  green outline. Aircraft off-table redeploy is not "using Ambush": no discs, by construction.
  **199 -> 169.**
- **P22b Rapid Ambush (4 refs).** **A field, not a new `EDeferTiming` value** — the filed premise
  suggested a new timing; the only variable is the earliest arrival round. `DeferDeployment` gained
  **`MinArrivalRound` (default 2)**, so every `Timing == LaterRound` check and all pre-existing authorings
  stay untouched. `BringOnReserves` now runs every round and gates PER UNIT. **169 -> 165.**
- **P22c Ambushing Piercing Shot (4 refs).** **Pure data, no engine change** — `deferDeployment` +
  a Save -1 gated `and(not(isMelee), tokenPresent(ArrivedFromReserve))`. The arrival pass already stamps
  that token and the round-end sweep clears it. Recorded approximation: an Aircraft off-table return
  stamps the same token; no corpus APS unit is an Aircraft (checked). **165 -> 161.**
- **P22d Ambush Re-Deployment (4 refs).** **Owner sign-off: the return is MANDATORY** — the next round
  start PLACES the unit without asking; only the spot is the player's. `DeferDeployment` gained
  **`MandatoryArrival`**. **Two halves that meet on a token:** the removal is an end-of-activation ability
  (`Cost.OncePerGame`, `allModelsHaveThisRule`) whose `Effect.AmbushRedeploy` -> `InvokeAmbushRedeploy` ->
  **`IOperationServices.RedeployAsAmbush`** drops any objective the unit's SIDE holds within 1in, parks
  models at the unplaced sentinel, `PlaceInReserve`, and stamps `TokenType.PendingAmbushArrival`
  (ManualOnly — it must survive the round-end sweep). The return leg is the rule's own `deferDeployment`
  entry GATED on that token, so the ordinary arrival pass needs no special case. Created the
  end-of-activation ability seam (above). **161 -> 157. P22 closed 42/42.**

## Unit creation & restoration (P17, DONE 2026-07-28, 24/24)

**P17a Spawn + the unit-creation machinery (14 refs).** **Owner sign-off: a mid-round creation may
activate the SAME round** ("same round for all"), including Split's destruction-seam path.

*Why it was dead twice over:* OPR writes the X as a STRING rating ("Spawn(Spores [5])") and both importers
flattened non-numeric ratings to the bare rule name — the argument never reached the books. And nothing
could represent it: `RuleArgument` had only `Int`.

- **Vocabulary:** `RuleArgument.Str`; `SpecialRuleEntry_Text` ("coreText") emitted by both importers for
  non-numeric ratings; `SpecialRuleEntryParser` grows a text-parenthetical branch **GATED on
  no-space-before-paren** so rule NAMES like "Versatile Attack (Piercing)" keep resolving whole;
  `RuleAttachmentPersistence` extended compatibly (it used to HARD-FAIL a resume on any non-Int argument;
  new blobs write a kind-carrying `Arguments` list, old blobs still read via `IntArguments`).
- **Auxiliary unit specs:** `ArmyListFile.AuxiliaryUnits` (nullable, omitted when absent so existing files
  round-trip byte-identical) — full `UnitFileEntry`s keyed by the rule's exact argument text in `Id`
  (display `Name` stays clean). `ListCompiler.CompileAuxiliaryUnits` compiles each named book unit at the
  `[n]` size, zero points, RECURSIVELY with a seen-guard (built for Split's chain). The rule-name set
  {Spawn, Split} is a commented constant per grow-on-demand. `ArmyRuleDataPersistence` carries the aux
  list so specs survive mid-game and across a resume.
- **The creation service:** `Effect.SpawnUnit(radius)` -> `InvokeSpawnUnit` ->
  **`IOperationServices.SpawnUnit`**: find the army, read the persisted spec, build through **the same
  path a deploying unit takes** (UnitData ctor + the now-public `GameBootstrap.AttachRulesFromArmyList` +
  `UnitCreationRules.Apply`, so Tough's max wounds and auras land identically), store-Create (**which is
  what replicates to network clients** — the AddSingleDataMessage path), append `army.UnitBindings` and
  re-Set through the store (#190's update path), place in a 6in `CircularZone`, stamp
  `TokenType.JoinsRoundInProgress`.
- **Same-round activation without touching the creator:** the per-round pool snapshots at round start and
  a creation can fire from code that cannot see the round context. So the ROUND CONTEXT adopts —
  `SingleRoundContext.AdoptMidRoundUnits()` runs at its own query seams, folds marked units into their
  owner's pool and spends the marker; the round-start snapshot sweeps strays so nothing joins twice.
  **Works for any future mid-round creation for free.**
- Found here: `BookRuleSupplement`'s reference scanner didn't know the new entry kind, so the embed
  silently skipped Spawn. **157 -> 143.**

**P17b Split (3 refs).** Rides P17a's whole machinery. The only new engine work is the trigger seam: the
existing destroyed hook is the KILLER's (`Shooting_OnUnitDestroyed`, requires an attributable killer;
routs and dangerous-terrain deaths skip it). New **`EHookID.Lifecycle_OnSelfDestroyed` +
`SelfDestroyedContext`**, fired by `UnitDestructionNotifier` for EVERY alive-to-dead transition, BEFORE
the killer-attribution early-return — "before removing the last model" holds because the dead models'
positions are still live, which is what centres the 6in placement on the corpse. Because #299 routed every
batched wipe-out through this notifier, Split fires on all of them for free. **143 -> 140.**

**P17c Reinforcement (4 refs).** **Two trigger arms meeting on one token.** The destroyed arm rides P17b's
killer-less seam; the Shaken arm needed **`Morale_OnShakenApplied` to become an EVALUABLE moment** (new
`ShakenAppliedContext`, fired by `ApplyShakenWithPresentation` — the hook had only ever been a token-clear
target). Both gate on `not(tokenPresent(ReinforcementSpent))`, and the service stamps that token BEFORE
the Shaken arm's removal-as-destroyed reaches the destruction seam — **the ordering that stops the
destroyed arm re-prompting.** Declining does NOT stamp (a Shaken decline still offers at the later death).
The copy is built from the live original with the firing rule STRIPPED ("this rule doesn't apply to the
new copy"), held in reserve with `PendingReinforcementArrival` (the P22d shape, so save/load and
networking ride free). `StartOfRoundExtraActionStage.PlaceReinforcements` runs right after
`BringOnReserves` (the rule's "after Ambushers have been deployed") and places MANDATORILY; the
`ArrivedFromReserve` stamp IS the can't-seize clause. New **`TableEdgeBandZone`** — an `IBoundedZone`
whose true shape is four non-overlapping border rectangles delegated to an internal `CompositeZone`;
`SaveTypeRegistry` gained its stable id (its own guard test caught the omission). Centre-in-band is the
same approximation every non-rectangular placement zone accepts. **140 -> 136.**

**P17d Reanimation + Aura (3 refs).** **Owner sign-off: wounds-first, auto-place.**
`Effect.RestoreWounds(minRoll)` -> `InvokeRestoreWounds` -> `IOperationServices.RestoreWounds`: pool =
floor of the unit's total wounds dealt (the probabilistic roller's fractional tail earns no die —
conservative, no int-locking); the pool rolls DECISIVE faces into one `DiceRolledBeat`; each success tops
up the first wounded LIVING model, else revives the first dead one at one wound — so a just-revived Tough
model is the wounded model the next success tops up, and bodies return one at a time and fill before the
next. Revives auto-place beside the first living model (0.1in base-to-base, eight angles, widening rings,
overlap-checked against the whole table; anchor-stack as last resort). Rides the P5a activation-start
passive seam — no stage work at all. **136 -> 133. P17 closed 24/24.**

## Combat primitives

**P15 randomized-branch (Unpredictable) — DONE 2026-07-11** (48 of 53 refs). "Roll one die: 1-3 AP(+1),
4-6 +1 to hit." Forks resolved with Chris: **decisive** selecting die (a branch selector cannot be
averaged into "half a modifier"), and **once per attack ACTION**, not per weapon. The two arms consume at
DIFFERENT hooks (`Shooting_OnHitRollModifier` vs `OnHitRollComplete`) and ops at one hook aren't visible
at the other, so the single roll is resolved ABOVE the hooks and threaded down: `EUnpredictableBranch` +
**`IHasUnpredictableBranch`** + `Condition.UnpredictableBranchIs`. `UnpredictableBranchResolver` rolls
only when the attacker carries an applicable rule (native, per-model, or aura-granted), so the seeded
stream (#193) is untouched for ordinary attacks; cached per action, reset on `SwapCombatRoles`.
**576 -> 528.** *Marks deferred — see Open work.*

**P10a auto-wound dice pool (Ravage + Crossing Attack) — DONE 2026-07-22** (39 refs; engine `1340496`,
`3ee6896`). **The reading that reshaped the slice:** P10's names are two unrelated mechanics — Ravage/
Crossing are "roll X dice, each 6+ is a **wound**"; Storm is "roll 3 dice, each 2+ deals 3 **hits**".
**Owner sign-off: the wounds skip the armor save but stay regenerable.** New
`Effect.DealAutoWounds(DiceCountPerModel, SuccessThreshold=6)` -> `InvokeDealAutoWounds`;
**`SyntheticWoundResolution`** rolls the pool, keeps the success count as the sub-histogram's fractional
`TotalRolls` (never int-locked), and wraps it as a `RollToSaveResults` whose FAILURES are every wound —
letting them enter `AssignWoundsStage` directly, skipping the save stages (and the P14b marker-spend
prompt, correct since there is no save to block) while Regeneration/Tough run untouched.
`ResolveRavageWoundsStage` fires at `Melee_OnChargeContact`. **`CrossingAttackStage`** sits beside
`StrafingStage`; `StrafingStage` was filtered to `DealHits` abilities and Crossing to `DealAutoWounds` so
the two never double-offer at the shared `Movement_OnMoveThroughEnemy` hook (Strafing's filter is
`AttackWithThisWeapon` since its own slice; the split is unchanged). **Crossing Attack(X) is the
first activated ability whose effect reads `ValueSource.Arg`** — `AbilityOffer` now carries the bearing
rule's `Arguments` and `ResolveAbility` resolves against them (backward-compatible). **423 -> 384.**

**P10b Storm of X — DONE 2026-07-22** (5 refs; engine `dcace2d`). **Owner sign-off: per-success target
picking** (each 2+ independently picks an enemy, up to 3 different units), which forced two signed-off
consequences: **the pool roll is DECISIVE** (you cannot pick a fractional number of targets; only the
pick-COUNT is decisive, each target's 3 hits still flow fractionally), and **it needs a looping stage**.
`Effect.StormOfHits` (pool dice, threshold, hits-per-success, WithRules, AP, range) -> `InvokeStorm`;
**`StormStage`** routed from Choose Action **by effect type** since four rule names share it. Per-target
batches LOOP: `OnBatchDone` re-enters the stage, `OnAllDone` returns to the menu — the melee-swing loop
pattern, self-looping with the queue as a stage field. **384 -> 379.**

**P11 reflect damage — DONE 2026-07-22** (27 refs; engine `163a2f3`, `9a4dbeb`). **Owner ruling: per-model
attribution** (exact, not unit-level). The reading that shaped it: all three rules reflect AFTER the melee
resolves, so this is a post-melee **TALLY**, not a per-wound hook. Adding a **per-model start-wounds
snapshot** to `CombatActionContext` (keyed by model reference so a Counter swap needs no re-keying) made
attribution exact by before/after comparison — **no per-wound tracking, sidestepping the mechanism that
stalled Sergeant.** `ResolveMeleeReflectStage` (a `MeleeStage` child after consolidation) counts per
rule-bearing MODEL: Retaliate's wounds-taken x X, Deathstrike's kills x X, Self-Destruct's X per model
that ENTERED alive plus killing every survivor through the `UnitDestructionNotifier` choke. Hit counts are
built directly as synthetic histograms, never an int `Roll`. **333 -> 306.**

**Heavy Impact — DONE 2026-07-23** (3 refs; engine `f739d2c`). `Effect.ChargeImpactHits` gained
`ArmorPenetration` (default 0); `ImpactSink` folds it as a MAX across sources (the single impact pool
cannot separate per-source AP — no corpus unit hits this, since Heavy Impact replaces Impact).
**232 -> 229.**

**Reroll threshold (Mischievous / Scrapper Boost) — DONE 2026-07-28** (6 refs; engine `5de9c85`).
> "If this model has Mischievous/Scrapper, when it shoots or charges enemies over 9in away, enemies
> taking hits from it must re-roll successful unmodified defense results of **5-6**."

`RerollCondition.OnUnmodifiedValue` gained `MinValue` (default 6) — the change `RerollSink`'s own doc had
anticipated ("add a value field to the condition if a non-6 reroll rule ever appears"). **The default is
load-bearing for compatibility**: every pre-existing authoring, core Bane included, serializes as a bare
`{"kind":"onUnmodifiedValue"}` and must keep meaning "the unmodified maximum".

**Correction to this row's filed premise.** It said to author these as the INCREMENT (re-roll 5s only),
by analogy with #196's Boost double-counting lesson. That lesson is about the ADDITIVE sinks; a threshold
is not additive. `RerollSink` now folds save rerolls by **MINIMUM**, so a base (6) and its Boost (5-6) on
the same weapon net the wider band, and the Boost is authored as the full band the corpus states —
correct alone, correct with its base, and correct if the base were ever dropped.

**Latent engine bug found and fixed:** `DiceResults.TotalWithinRange` offset-corrected its lower bound by
`SideMin` but used its upper bound raw. Accidentally right for a full die (`SideMin` 1) and wrong for
every SUBSET, whose `SideMin` is its lowest kept face — so any range query over a subset over-counted or
threw `IndexOutOfRange`. Nothing had asked: the old reroll path used `At(SideMax)`, which indexes
directly. Asking `AtOrAbove(5)` of a `SubsetAtOrAbove` is what surfaced it.
`DiceResultsSubsetRangeTests` pins both the subset behaviour and the unchanged full-die behaviour.

**Recorded, not fixed:** `CombatMath` (the AI's analytic mirror) builds its `SaveRollCompleteContext`
without a distance, so the `attackedFromOverInches` gate reads 0 there and the AI values these Boosts —
and every other save-side range-gated rule — as if they never fire. Pre-existing, not introduced here.

Data (app-side, supplement): base Boost + Boost Aura per family, Weapon-scoped like their bases, on the
Warbound Boost template; embedded into GoblinReclaimers + Jackals. The base Boost ships even though only
the Aura registered as dead, because each book's spell grants it by name. **121 dead.**

**Strafing — DONE 2026-07-28** (12 refs, the last `scope-mismatch` in the corpus).
> "Once per activation, when this model moves through enemy units, pick one of them and attack it with
> this weapon as if it was shooting. This weapon may only be used in this way."

**Two-thirds of the filed premise was wrong** (standing lesson 4 again). It listed three blockers; only
one was real.

- *"Cannot be a scope flip - its fly-over passive rides a hook that never reads weapon rules."* The source
  rule **grants no fly-over at all** - "when this model moves through enemy units" presupposes a unit that
  already can. Every one of the 11 carrier units has `Aircraft` or `Flying`, both of which emit
  `IgnoreEnemyMovementBlock` at unit scope; even the one footslogger (Saurian's Gecko Champion) gets
  `Flying` from the same Pterodactyl item that grants the bomb. The catalog's passive was an engine
  invention duplicating Flying. **Owner-signed-off: dropped**, with `StrafingStage` warning once
  (`WarnOnce`) if a bearer ever turns up without the capability, since the weapon would then be unusable.
  `StrafingShippedDataTests.EveryStrafingCarrier_CanMoveThroughEnemies` walks all 47 books and pins it.
- *"Needs a once-per-activation weapon-use restriction."* `Cost.OncePerActivation` already existed and
  already reset (P22's `ReconcileEndOfActivationStage` token sweep closed the old Appendix C deferral).
  The real clause is "may only be used in this way", which is a **weapon-pool exclusion**, and it was
  live: `IWeapon.IsMelee()` IS "range 0", every corpus bomb has range 0, so a Bomber Plane dragged into
  melee was swinging its Blast(3) bombs as a close-combat weapon. New `StrafingRules.IsStrafeOnly`
  (structural - keys on the effect, not the name, so a renamed copy is restricted too) filters
  `GetMeleeWeapons`/`GetRangedWeapons`, `MeleeRangeUtilities.GetMeleeWeaponsFromModels` and
  `CombatMath.SurvivorWeaponBatches`.
- *"Needs a mid-move attack-with-this-weapon primitive."* Real, and the whole slice.

**No book data changed.** The books were right from the start - all 12 references already sit on bomb
weapons - and the catalog was the approximation. First slice in #197 that is engine-only.

New vocabulary: **`Effect.AttackWithThisWeapon`** (`attackWithThisWeapon`) -> **`RuleOperation.InvokeWeaponAttack`**,
a payload-free op: the weapon IS the payload. **`AbilityOffer` gained `Weapon`**, and `GatherOffers` now
scans the acting unit's weapons - deduped BY NAME, the identity the shooting/melee pools already use,
since weapons are per-model instances and a five-model unit would otherwise be asked five times. That is
the seam the passive side has had since #027 and the ability side never did; `ResolveAbility` threads the
weapon into `RuleInvocation.Weapon`, which already existed.

`StrafingStage` was rewritten rather than joined by a sibling: after the re-author nothing else emits
`DealHits` at the move-through hook, so keeping the old synthetic-hits arm would have been dead code
(grow-on-demand). Its child chain is now the **real shooting chain** - BuildTargetList -> CoverCheck ->
DetermineHitRoll -> RollToHit -> save -> wounds -> **`ResolveStrafeMoraleStage`** - minus RangeCheck and
OcclusionCheck, since the mover is directly overhead and the weapon's range is 0 precisely because it can
be used no other way. So the weapon's own Attacks, AP and rules apply; verified in play, where Blast(3)
multiplied the hits and AP(1) moved the save from 5+ to 6+.

**Owner ruling (morale):** "as if it was shooting" carries the shooting morale test, unlike Impact and
Crossing Attack, which deal mid-move wounds and never test - their text says nothing of the kind.
**Owner ruling (the pick):** one enemy crossed keeps the yes/no; several get a cancellable pick and no
yes/no on top (the Dash rule - an ability that lets you decline at the pick is not asked twice).

Hygiene fixed in passing: `RuleFireLint` still listed `InvokeDealHits` as handled at
`Movement_OnMoveThroughEnemy`, which after this slice nothing reads - exactly the silent no-op the lint
exists to catch. Removed.

**Recorded, not fixed:** the AI never *uses* Strafing. The offer surfaces as a `YesNoRequest` mid-move and
`ComputerPlayerController` answers it by default rather than by valuation, and `CombatMath` has no term
for a mid-move attack. Same state as Crossing Attack and every other move-through ability, so this is the
family's gap rather than this slice's. **114 -> 102.**

## Markers & tokens

**P13 marker-scaled magnitude — DONE 2026-07-22** (41 refs; engine `2efc06e`). Shipped **without touching
`ValueSource`** (its context-free `Resolve` stays pure): new effects `tokenScaledRollModifier` /
`tokenScaledReduceArmorPenetration` read the bearer's token count at Apply time (steps = count /
perMarkers, with Fortified's read-side `maxReduction` cap). `GrantToken` gained a grant-time `maxTotal`
clamp. `ReconcileObjectivesStage` now fires `Round_OnRoundEnd` rules for every living unit before the
token sweep (new `RoundEndContext`). Both Shaken-application sites clear
`CustomHook(Morale_OnShakenApplied)` tokens. "On the table" composes from existing conditions:
`not(InReserve) and not(EmbarkedIn) and not(OffTableFromForcedMove)`. Authored behind
`tokenPresent(marker, minCount: perMarkers)` so `RuleFireLint`'s token seeding proves each entry fires.

**P14b spend-for-bonus markers — DONE 2026-07-22** (28 refs; engine `d0985e2`). Two marker classes on the
ENEMY unit, bonus kind in the token type: persistent (`Persistent{Hit,Ap}BonusMarker` — counted every
attack, never removed) and spendable (`Spendable{Hit,Ap}BonusMarker`). **Owner ruling: the spend is
PROMPTED, not auto-spent** — `TargetMarkerSpend` asks the attacking player how many to remove (a
`StringSelectionRequest`, spend-all listed first so the CLI EOF default and the AI first-option fallback
both take the aggressive default; zero-marker attacks never prompt), folded into `DetermineHitRollStage`
(skipped while fatigued, like granted buffs) and `DetermineSaveRollsNeededStage`. Placement is data over
existing `TargetSelector`/`Cost` machinery; Spotter's "on a 4+ place a marker" is the new
`grantTokenOnRoll` effect (decisive die, `InvokeGrantTokenOnRoll`, ClearTokenOnRoll's mirror).

*P13 + P14b were built together per this file's own sequencing warning — one coherent marker mechanic,
not three incompatible ones. **492 -> 423** (-69 of the cluster's 71; P12's 2 deferred).*

**P5b round-start Shaken recovery — DONE 2026-07-09** (66 refs; engine `05eb91e`). **The premise was
wrong:** `Round_OnRoundStart` was not dormant — `StartOfRoundExtraActionStage.GrantSpellTokens` fires it
every round for every living unit. So this needed only the effect: `Effect.ClearTokenOnRoll` ->
`InvokeClearTokenOnRoll`, rolling with `RollDecisiveFace` (the outcome is binary; a histogram would want
to remove a *fraction* of a token). **864 -> 798.**

## Casting (P23, DONE 2026-07-23, 19/19)

**Caster Group (3 refs).** **Two of its three sentences needed no code** — spell tokens are held by the
UNIT, so "pick a model to be the caster" and "transfer its tokens when it dies" have no observable
content. This slice is what prompted the capability seam (above). Also new **`ValueSource.RuleCarrierCount`**
(living models of the bearer's unit carrying the firing rule); `ValueSource.Resolve` now takes the whole
`RuleInvocation` as the ONLY entry point — an arguments-only overload would be the easy thing to reach for
and would silently return a wrong answer for state-reading variants. `UnitHasGrantedRule` lifted into a
shared `RuleGrantQueries` so the gate and the count cannot disagree about what "has" means. **262 -> 259.**

**Spell Accumulator (7 refs).** **The first slice built entirely on the capability seam** — no new hook, no
new stage, the whole rule authored as two data entries: `grantToken(AccumulatorTokens, arg0, manualOnly,
maxTotal 6)` at round start, and `enableSpellLending(AccumulatorTokens, 12)` at the capability hook gated
`if !Shaken` (the payoff the seam predicted: a `Condition` re-asked on every ask, not a special case).
**Its own token type is load-bearing** — the corpus puts the upgrade on units that are themselves casters,
and the rule says *other* friendly units; one shared type would let the holder spend its own pool, and
would make a full pool look like a caster to the #103 assist scan. New **`SpellPurse`** ("everything this
caster may spend right now") — five sites that read `Tokens.GetTokenCount(SpellTokens)` now ask it, so the
menu, the picker and the AI planner cannot price a spell differently. **Own tokens are spent first, then
lenders in table order** (draining the restricted resource first leaves the team the most options); stated
as a decision, a one-line change if play argues otherwise. **259 -> 252.**

**Spell Conduit (9 refs).** The gating half is Accumulator's exactly; the payload changes a property of
the whole cast rather than lending a resource: `Effect.EnableSpellRelay(range, bonus)` read by a
**`SpellRelay`** helper, with the neighbour scan shared into **`CastSupport`**. **A relay moves the spell,
not the caster:** only the origin of range/LoS changes (including which models are discounted as
blockers); affinity is still judged against the caster, the #103 assist scan still measures 18in from the
casting unit, and the caster's own eligibility is unchanged. **Design fork (owner call): no origin prompt
— the origin is derived and made visible.** A relay origin is never worse than the caster's own, so
`CastSpellStage` offers the UNION of every origin's legal targets (`RelayedTargeting`), narrows viable
origins as targets are chosen, and casts from a relay if one still covers them all, relays preferred. The
owner's "it just happens" concern was answered by visibility rather than a prompt: a relay note in the
spell picker, the origin named on every target row ("Dummies (via Synaptic Relay, +1)"), the cast
announced, and "relay +1" listed first in the roll breakdown. With no conduit on the table every path
degrades to the exact prior behaviour. **252 -> 243.**

## Buffs & debuffs

**P7 morale-outcome override — DONE 2026-07-29** (9 refs; engine `3c47383`).
> No Retreat: "When a unit where **most models** have this rule fails a morale test that causes it to be
> Shaken or Routed, the test counts as passed instead. Then, roll as many dice as the number of wounds it
> would take to fully destroy it, and for each result of **1-3** the unit takes one wound, **which can't be
> ignored**." No Retreat Aura (5 refs) confers it unit-wide; No Retreat Buff (1 ref) grants it once.

The filed row said only "convert a failed morale test into a pass, then take unignorable self-wounds" —
right as far as it went, but it omitted the band (1-3, not a single face), the pool size (the unit's
REMAINING wounds), and the majority gate. All three are places a plausible authoring is silently wrong.

New `Effect.PassFailedMoraleTest(SelfWoundOnRollAtMost)` -> `RuleOperation.PassMoraleTest` at
`Morale_OnMoraleTestComplete`, read by `MoraleUtilities` rather than folded by a sink: **both halves have
to land at one point in the sequence**, and the op carries its own price so conversion and cost can never
drift apart. New **`Condition.MostModelsHaveThisRule`** — `AllModelsHaveThisRule`'s body at a strict
majority of LIVING models, same ownership semantics (per-model rules, a joined hero excluded from the
host's static rules, unit-held grants counting for everyone).

**Two owner rulings, 2026-07-29:**
1. **Only Shaken/Rout tests convert.** `TakeMoraleTest` has four callers and only two end that way; the
   others are Mind Control / Fatigue Debuff and a spell's own test, which the rule's wording does not
   cover. Threaded as `failureCausesShakenOrRout`, set at the two eligible call sites.
2. **The already-Shaken automatic failure converts too.** That path returned before the hook ever fired —
   and it is precisely the failure that ROUTS, which the rule's own text names. The conversion is now
   offered ahead of the GF v3.5.1 short-circuit. A unit without the rule still auto-fails; that is its own
   test, because this reached into load-bearing rules code.

**Ordering:** a Fearless re-roll is free, so it goes first; converting before it would charge a wound pool
the unit never owed. The already-evaluated op queue is reused rather than re-evaluated, or a one-shot
grant (the Buff's) would be double-spent.

**Found in passing:** the morale-complete evaluation passed no models, so **per-model rules were invisible
at that hook** — `MostModelsHaveThisRule`'s majority arithmetic would have been dead code, and a joined
hero's morale rule unreachable. Now carries `HeroStatRules.LivingModels`, the #183 shape already used by
`EffectiveChargeDistanceAgainst` and Counter. Fearless is unaffected (it gates on `AllModelsHaveThisRule`,
which a lone hero still fails).

**Dice invariant:** the pool is FLOORED from remaining wounds (the P17d Reanimation precedent — a
fractional tail cannot buy a whole die); the wounds it produces stay fractional. One batched roll, so the
probabilistic roller yields the expected number of low faces. Wounds are unignorable **by construction** —
applied straight through the casualty seam, never the save/wound-ignore pipeline, exactly as dangerous
terrain's are — and a self-kill still goes through the destruction seam, killer-less.

**Verified in play:** `Diehards: No Retreat rolled 1 dice, 0 at 3 or less` then `passed its morale test`,
where the ruleless control unit was Shaken.

**Recorded, not fixed:** this builds the "deal unignorable wounds to a unit" half that the deferred
**Hazardous self-wound arm** (15 refs) needs; that arm still needs its histogram-reading effect and an
`OperationExecutor` point in `RollToHitStage`. **76 -> 67.**

**P20 action-permission modifiers — DONE 2026-07-28** (12 refs; engine `e52924d`).
> Quick Shot: "This model may shoot after using Rush actions." Quick Shot Aura (5 refs): "This model and
> its unit get Quick Shot." Quick Shot Mark (4 refs): "Once per activation, before attacking, pick one
> enemy unit within 18in in line of sight, which friendly units gets Quick Shot AGAINST once."
> Unwieldy: "Strikes last when charging." Unwieldy Debuff (3 refs): "...pick one enemy unit within 18in in
> line of sight, which gets Unwieldy in melee once (next time the effect would apply)."

**Neither BASE rule is referenced by any unit in the corpus** — both exist only as the target of a grant
(aura, mark, one-shot debuff, spell). That is why `--rule-coverage` never counted "Quick Shot" or
"Unwieldy in melee": it walks unit/item/weapon/upgrade sites, so a rule that only ever arrives by grant is
invisible to it. Three books' `Combat Ecstasy` spell and High Elf Fleets' `Creator of Illusions` were
granting these names into thin air — dangling, silent, and NOT in the dead count. Both now resolve.
`QuickShotAndUnwieldyShippedDataTests` walks every book's spells for exactly this class of break.

**Quick Shot** is a permission, deliberately not a distance: new `Effect.ShootAfterRush` ->
`RuleOperation.AllowShootAfterRush` at `Activation_OnActionChoice` (the hook `ChooseActionStage` already
fires for `RestrictActions`), read by the advance-and-shoot cap in `GetCanShoot`. Authoring it as
`movementBonus(Advance, +6)` would have been close in effect and wrong in kind — it also changes what
counts as an Advance for every other rule that asks. `GetCanShoot` became **public static** like its
sibling `GetCanPass`: a rushed unit with nothing else to do never reaches the menu (zero valid options
auto-pass, and rushing also closes Pass), so the answer has to be readable without one. Aircraft keeps its
own hardcoded waiver — **recorded, not fixed:** it now expresses the same idea as this op and could ride it.

**Quick Shot Mark was the design fork** (owner-ruled 2026-07-28: full fidelity). Every other Mark is a
weapon/attack rule where "against" makes sense; Quick Shot is a movement permission, and the mark is
claimed in `DetermineHitRollStage` — *after* the shot is declared — so a plain `markTarget("Quick Shot")`
would have resolved the name and done nothing. Instead the READ moved earlier (`GetCanShoot` asks whether
any fireable enemy carries a shoot-after-rush mark) and the permission stays target-bound: a new
`ApplyQuickShotMarkGating` in `ChooseRangedAttackStage`'s shared gating pipeline marks every unmarked
target unselectable. Sharing that pipeline is what keeps the action gate and the shoot stage agreeing
(#200). The claim/consumption is untouched — because the target list is narrowed to marked units, the mark
is always actually spent. `ICombatActionContext.MarkedTargetsOnly` carries the decision from the gate
(the only place that knows how far the unit moved) into the stage. Detection is **structural** — by the
effect a granted rule produces, not by its name — so a Shred Mark cannot double as a Quick Shot.

**Unwieldy** is Counter's mirror: `Effect.StrikeLast` -> `RuleOperation.StrikeLast` at
`Melee_OnCounterTrigger`, and `DetermineStrikeOrderStage` now evaluates the **charger on the Actor seat**
as well (Counter's participants were all Subject-seated, so an attacker-side rule was invisible to it).
Both ops drive the SAME role swap: a charger that strikes last and a defender that strikes first describe
one outcome, so they compose instead of swapping twice and handing the first swing back. The banner names
whichever is responsible. The stage's evaluation is a live one, which is what spends the debuff's one-shot
grant.

**Verified in play** (`--scenario` probe): two identical rifle units rush 8in — the Quick Shot Aura one
keeps Shoot, the other has zero options and auto-passes; a rushed unit with a marked enemy gets
`[1] Rifle -> Dummies` and `[-] Rifle -> Tough Dummy [unavailable: Rushed - only a Quick Shot marked
target may be shot.]`; and an Unwieldy charger produces `Clumsy Chargers is unwieldy - Tough Dummy strikes
first!` with the melee resolving defender-first and the charger offered the strike-back.

**Recorded, not fixed:** the AI never uses Quick Shot Mark or Unwieldy Debuff (the whole
`Activation_OnBeforeAttackAction` family, see P8), and `CombatMath` has no term for either permission, so
the Tactician will not plan a rush-and-shoot. **88 -> 76.**

**P8 terrain debuffs — DONE 2026-07-28** (14 refs; engine `a0c5301`).
> Dangerous Terrain Debuff, wording A (9 refs — Change/Havoc/Plague Disciples, Goblin Reclaimers): "Once
> per activation, before attacking, pick one enemy unit within 18in **in line of sight**, which **counts as
> being in Dangerous Terrain once** (next time the effect would apply)."
> Dangerous Terrain Debuff, wording B (2 refs — Lust Disciples, War Disciples): "...pick one enemy unit
> within 18in [**no** line of sight], which **must immediately take a Dangerous Terrain test**."
> Difficult Terrain Debuff (3 refs — Wormhole Daemons of Plague): wording A, Difficult, no line of sight.

**The filed row was right about both halves and did not know they were the same NAME.** "Force a
Dangerous-terrain test / count as standing in terrain" is two rules OPR ships under one name — the
**Darkborn** situation, six armies wide. `OprBookImporter.AmbiguousRuleNames` routes Lust/War Disciples to
**"Dangerous Terrain Debuff (Immediate)"**; the other four keep the bare name. **Only the minority variant
is renamed** (Darkborn renamed both): one side has to diverge from its own printed page either way, and
this keeps the corpus wording for 12 of the 14 refs. The two bundled books were patched by targeted string
replace — labels deliberately untouched, since `DisambiguateAmbiguousRuleNames` rewrites rule ENTRIES only,
so the books stay byte-identical to what a re-import produces.

**Wording A is pure data** — #153's `Effect.CountAsInTerrain` + `AddRule(NextTrigger)` already do exactly
this, with `Speed Debuff`/`Piercing Debuff` as the literal template (18in / Foe / pre-attack /
`OncePerActivation`). Line of sight is per-variant, straight from the text, and is the one field a
copy-from-the-nearest-sibling edit would silently get wrong.

**Wording B needed the primitive.** New `Effect.DangerousTerrainTest` -> `RuleOperation
.InvokeDangerousTerrainTest` (an `ExecutableOperation`, because it deals wounds and can destroy the unit,
so it needs the async present/casualty/destruction seam) -> `IOperationServices.ForceDangerousTerrainTest`
-> new `MovementExecutor.RollForcedDangerousTerrain`. `RollDangerousTerrain`'s batched-roll body was
extracted into a shared `RollBatch(testers, unit)`; the move-driven path builds its testers from paths, the
forced path from LIVING models. `RuleFireLint` needed no change (its ability arm already returns true for
any `ExecutableOperation`). **Modelling B as A was the trap**: A only bites when the victim moves, so a
victim that simply holds still would shrug the whole debuff off, inverting the rule. The engine tests pin
the two arms against each other for exactly that reason.

**Owner ruling 2026-07-28: a Flying victim waives the forced test** (`IgnoresAllTerrain`), matching what it
already waives on a real crossing and on the counts-as grant — one rule for all three dangerous-terrain
paths. Strider is `DifficultOnly` and still takes it; both directions are pinned. The waiver skips the roll
rather than discarding it, so the seeded dice stream is untouched, and it logs, so a rule that visibly
fires and does nothing is never a mystery.

**Verified in play** (`--scenario` probe, three carriers vs Dummies): wording B wounds a standing victim at
the attacker's pre-attack action and then drops off the menu; wording A's grant lands on the enemy, fires
at the enemy's own move (`Dummies: 5 model(s) tested dangerous terrain - 1 wound(s) dealt`) and is spent
exactly once. **The 6in Difficult cap itself was NOT separately re-probed** — the probe geometry could not
discriminate a capped from an uncapped AI move. It is covered by `MovementRuleIntegrationTests`, which
already grants a counts-as-Difficult rule through the same `RuleGrant`/`NextTrigger` token and asserts
`MovementActionContext.MaxAdvanceDistance`; this slice changes who grants it, not what the cap does.

**Recorded, not fixed:** the AI never USES any of the three. `Ai/` has no reference to before-attack
abilities at all, so this is the whole `Activation_OnBeforeAttackAction` family's gap (~40 supplement
rules, Mend and Breath Attack included), not this slice's. **114 -> 88.**

**P6 deferred buff/debuff family — DONE 2026-07-23** (20/20 + 3 riders; engine `6121b13`, `c83b1fd`,
`4a8e767`). **The row's premise was mostly wrong** — it filed all five rules as needing a new one-shot
debuff primitive; four ride seams already built AND already consumed, and shipped as pure data:
Morale Debuff (`statModifier(Morale, -1, NextTrigger)` — `MoraleUtilities` already calls `ConsumeNet`),
Defense Debuff (`statModifier(Save, -1)`), Speed Debuff (`addRule("Slow", NextTrigger)` on the #153 seam),
Piercing Debuff (Fortified's `reduceArmorPenetration(1)` on the **Actor** seat, so it blunts the bearer's
own AP). **Only Casting Debuff had no carrier:** new **`ERollKind.Cast`** + `TokenType.CastRollModifier`,
folded into `CastSpellStage`'s existing `netModifier` so the delta shifts the **threshold** (never a
post-roll adjustment); consumed once per attempt after the cost is spent, so a browsed-and-cancelled spell
never burns the debuff. Riders authored free: Casting Buff (2), Speed Buff (1).
**`TargetSelector.RequiredRule` now scans the unit's MODELS too** — a joined hero keeps its rules on its
model (#006/#093), so the unit-only scan was blind to every hero Caster, which is every practical target
for "pick one enemy with Caster". Also fixed a **#167 tooling gap**: `ScenarioToken` had no way to express
a payload, so a placed `CastRollModifier` carried no delta and looked exactly like the modifier not
working; it now takes an optional `delta` for the four carrier types, and a `delta` on any other type is a
compile error rather than a silent drop. **306 -> 283.**

**`moraleTestThen` outside spell casting — DONE 2026-07-28** (7 refs; engine `548399a`).
> Mind Control: "Once per activation, before attacking, pick one enemy unit within 18in in line of sight,
> which must take a morale test. If failed you may move it by up to 6in in a straight line in any
> direction." Fatigue Debuff: same, "if failed, it becomes fatigued."

Both are the **P6 pre-attack shape**, so the hook, cost and target selector were all already proven — the
only gap was that `Effect.MoraleTestThen.Apply()` was a documented no-op, enacted solely by
`CastSpellStage`. **Owner sign-off (2026-07-28): make it an executable operation** rather than teaching
one more stage about it. New `RuleOperation.InvokeMoraleTestThen` -> `IOperationServices.MoraleTestThen`,
the P17/P22 shape. Every ability-offering stage already runs `OperationExecutor`, so the effect now works
at **all** of them instead of only where a stage was taught — which was the actual gap, not just these 7
refs. `RuleFireLint`'s ability arm already returns true for any `ExecutableOperation`, so no lint change.

**The operation carries BOTH units.** The on-failure effect resolves with the rule's OWNER as bearer and
the failing unit as target, because `Effect.TriggeredMove` reads `Bearer.PlayerID` for the controller —
carrying only the victim would hand the enemy control of its own forced move. Pinned and mutation-checked.

**`CastSpellStage` deliberately keeps its own path**, recorded rather than unified: it resolves the same
effect over SEVERAL targets and reports one aggregated banner (#293), which per-target executables would
fragment. It short-circuits before `Apply` is called, so nothing double-runs.

Data (app-side, supplement): both rules; embedded into Jackals, SoulSnatcherCults, WormholeDaemonsofLust
(Mind Control) and WormholeDaemonsofWar (Fatigue Debuff). **114 dead.**

**Screened / Screened Aura — DONE 2026-07-23** (1 ref). Pure data, byte-identical to shipped `Machine-Fog`
on the `AttackedFromOverInches` gate. **243 -> 242.**

**Mobile Artillery attacker arm — DONE 2026-07-23** (2 refs). Pure data:
`Not(IsMelee) + Not(AfterMoving) + AttackedFromOverInches(9)`, Actor seat, +1 Hit. *Defensive arm
deferred — see above.* **234 -> 232.**

**Hazardous AP arm — DONE 2026-07-23** (15 refs). The corpus weapons carry `armorPenetration: 0` in-profile
with a bare `Hazardous` rule, so the AP must come from the rule: weapon-scoped Actor-seat
`rollModifier(Save, -4)` at `Shooting_OnHitRollComplete` (Thrust's pattern). *Self-wound arm deferred —
see above.* **227 -> 212.**

**Speed Feat — DONE 2026-07-23** (4 refs; engine `82cdea7`). Fork surfaced: offered at activation start
(brace-your-moves) rather than a per-move prompt. Needed the optional-single-ability change above.
**212 -> 208.**

**Protection Feat — DONE 2026-07-23** (9 refs). **No engine change** — reuses Speed Feat's optional
machinery + Regeneration's `IgnoreWoundOnRoll`. Fork surfaced: a reactive wound-stage prompt would need
new interactive infra in the hot combat path (and `SaveRollCompleteContext` in `AssignWoundsStage` doesn't
apply token ops, so an auto-use token model also needs engine work AND wastes charges on all-saved
attacks). Modelled as a **proactive optional brace**: a once-per-game Yes/No at activation start granting
an `UntilNextActivation` per-wound 5+ ignore covering the opponent's turn. Effect faithful; only the
timing shifts reactive -> brace-in-advance. **208 -> 199.**

## Authoring safety

**`RuleFireLint` operation-consumption check — DONE 2026-07-09** (engine `a2304fb`). `IsOpConsumedAtPassiveHook`
— the passive twin of `IsOpHandledAtAbilityHook`, keyed on the operation's *payload* where that decides
consumption (`ApplyRollModifier(Save)` is read at the hit-complete hook; `(Hit)` is not). Unmapped pairs
report as unconsumed, so drift fails loudly; only flags an entry whose *entire* output is ignored. This is
the hole that let `Changebound`/`Machine-Fog` ship as no-ops. **Still not covered (#166):** whether the
consumed value is used *correctly*, and what several rules *sum to* — `BoostRuleCompositionTests` covers
the latter for the rules it names.

---

## Decisions

- **Split from #196 on "does it touch the submodule", not on rule count.** The data half has a closed
  vocabulary and a mechanical pass/fail gate; the engine half has design forks and cross-repo cadence.
- **Slice 0 lived here, not in #196**, despite being small: a catalog-scope semantics call on submodule
  code, with a real regression surface.
- **Supplement-rule grants die across a save/load resume** — found under P6, **resolved 2026-07-23 under
  #095**: `ArmyData` now persists the army file's embedded `RuleDefinitions` (and `Spells`, a second
  casualty found with it) into the save, and the resume path replays them into the shared resolver.
- **Per-model accommodations recorded, not built** (Accumulator, Caster Group, Ambush Beacon): rules
  saying "this *model's* X" are implemented at the UNIT, because there is no per-model token pool. No
  corpus entry makes the difference observable in any of the three cases.

## Sequencing

The original ordering is spent — slice 0, P5a/P5b, the marker cluster, P10, P11, P21, P22, P17, P23 and P6
are all done. Remaining work is a long tail of independent slices; take them by leverage from the Open
work table. Two clusters are worth grouping:

- **Sergeant + Armor(X)** (23 refs, both #196 F16 handoffs) are the per-model / per-stat attribution
  family — different mechanics, but both are about a rule that applies to less than the whole unit.
- **Extended Buff Range + P19 Coordinate** (12 refs) both reach *another friendly unit*; Conduit's relay
  machinery is the nearest precedent for the first.

## Outcome

_(written when the item closes)_
