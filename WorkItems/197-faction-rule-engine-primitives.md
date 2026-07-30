# 197 — Faction rule coverage, part 2: engine primitives + the scope-mismatch bug

**Status**: in progress. Corpus dead references **2,342 -> 5** of 13,870 (0.04%), 2 names.
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

Ref counts are live from `--rule-coverage FdgRaylib/Assets/Books` (2026-07-30). **5 dead across 2 names,
all of them `no-definition` - the `scope-mismatch` category has been empty since Strafing (2026-07-28).**

| Refs | Slice | What it needs | Rules |
|-----:|-------|---------------|-------|
| 3 | **Vengeance** | "Place N markers on the unit that destroyed this one, N = models with this rule at game start; friendly units get +N to hit vs the marker count." P13's marker-scaled magnitude now exists and covers the read side; **still needs a magnitude source for "count of models with rule X in the bearer unit at game start"** — `ValueSource.RuleCarrierCount` (P23) counts LIVING carriers now, not at game start. | Vengeance (3) |
| 2 | **P12** attack-count producer — DEFERRED 2026-07-22 (owner ruling) | Regenerative Strength's marker GAIN is "one marker per ignored wound", but the Regeneration ignore roll is a histogram: under the probabilistic roller the ignored count is fractional, and token counts are integers — bridging them means int-locking a roll-derived value. Owner chose to keep the dice invariant pristine over a rounding approximation. The producer seam (a fold at `DetermineHitRollStage`'s attackCount, where a code comment marks the spot) was NOT built unused, per grow-on-demand; note the consumption side is ALREADY fraction-ready - `attackCount` is a `float`, so only the marker count is integral (`Token.Count` is an `int`), not the thing it would feed. **A second, unwired prerequisite, found 2026-07-30:** `EHookID.Lifecycle_OnWoundIgnored = 21` is declared and documented ("used by rules that count or react to ignored wounds, e.g. Regenerative Strength markers") but has **no context type and no firing site anywhere in the engine** - a rule authored there today would validate, lint clean and never fire (the Breath Attack shape). So even with the fractional question settled, the producer also needs a `WoundIgnoredContext` plus a fire site in `AssignWoundsStage`'s wound-ignore fold. If this reopens, the fractional options are: fractional token counts (a format change - tokens are int-counted everywhere), a separate decisive roll for the marker count (which then diverges from the wounds actually ignored), or rounding (what the owner rejected). **Read side is settled for when this reopens:** melee Yes/No per weapon volley ("add +X attacks to this weapon?"), once-gated per activation — the player picks the weapon by accepting on it (owner-ruled: prompted, not auto). | Regenerative Strength (2) |

## Deferred sub-arms of shipped rules (name resolves; mechanic partial)

These do **not** show in the dead count. Recorded here so they are not silently lost.

- ~~**Hazardous self-wound arm** (15 refs)~~ — **DONE 2026-07-29**, see Combat primitives. The balance
  flag is cleared: Hazardous is no longer upside-only.
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

- ~~**`RuleFireLint.Check` returns at the FIRST passive entry that produces operations**, so a rule's later
  entries are never lint-checked.~~ **WRONG — corrected 2026-07-29 while shipping Hazardous.** `Check`
  loops every passive entry (`for i in Passive.Count -> CheckPassiveEntry`); the `return` sits inside
  `CheckPassiveEntry` and only ends that entry's search through its own context variants, which is exactly
  what it claims to test. Hazardous's second entry is linted. Standing lesson 4 again, this time against a
  premise this ledger itself filed.
- **The CLI army-file prompt loops forever on EOF** when the file fails to load (a stale probe army
  produced a 5.8 GB log before timeout). It should abort at EOF like every other resolver.
- **The ranged-attack chooser assumes weapon names are unique per unit** (`BuildWeaponOptions` keys its
  pool, LoS map, per-target stats and range cache by NAME; #209's determinism ordering leans on it) and
  FAULTS the state machine on a duplicate. Sergeant's slice sidesteps it by renaming marked copies, and
  the only other same-name-split producer (slice 0's partial targeted upgrades, 17 "Upgrade Master
  Marksman Carbine with: Precise" sites) is safe in practice - all are one-carbine heroes, whole-entry
  attach, no split. Latent until a book update ships a multi-copy partial weapon upgrade; the honest fix
  is profile-keying the chooser (and auditing its melee sibling).

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
6. **Never write code after a combat stage's `onFinished`.** That call is a tail call into the next stage;
   its continuation is never resumed, so anything below it is dead code in play — while running fine under
   a test layer whose `ExecuteTransition` returns immediately. Hazardous shipped its first cut this way:
   14 green tests, zero effect in a real game. Work that must happen "after the attack" belongs in a later
   stage, carried there on the results struct. Corollary: a test double that terminates the state machine
   proves nothing about ordering — only a probe (or a test driving the real next stage) does.

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

**Sergeant — DONE 2026-07-29** (12 refs: the champion option of every Wormhole Daemons troop squad;
engine `998bbd2` + `4ca9b6a`). OPR `8HWdOwMYcI0p` verbatim: "When this model attacks, unmodified results
of 6 to hit deal 1 extra hit (only the original hit counts as a 6 for special rules)." The mechanic is
Surge's body exactly; the slice built only the ONE-MODEL scoping (owner ruling 2026-07-22), and the
owner chose **weapon marking over true per-model rules** (2026-07-29, after the per-model alternative was
costed: army-file format has no per-model expression, and pooled volleys would need re-keying at three
sites; no remaining open item needs it). Sergeant is authored **Weapon-scoped**, and `ListCompiler` grew
a champion post-pass: a weapon-scoped rule gained from a TARGETS-LESS, non-affects-All section ("Upgrade
up to three models with one") attaches to one copy of each weapon profile per application - AFTER all
sections, so "Replace all Hand Weapons" can't eat the mark - and never reaches `unit.SpecialRules`
(army-load's spread is the ~10x over-grant). The marked copy is the aggregate format's "this model":
round-robin hands it to a model at load, `WeaponComparer` batches it as its own volley, and the
weapon-participant dispatch fires the extra-hit fold on those dice alone - the joined-hero mechanism,
reused. Corpus census pinned in tests: Sergeant's 12 sites are the ONLY occupants of the routing shape.
The no-cascade parenthetical was already engine law (6-triggered rules read unmodified rolls before
synthetic hits insert). **The play probe caught a crash the whole green suite missed**: the ranged
chooser keys its weapon pool by NAME ("An item with the same key has already been added: Rifle"), so
marked copies are RENAMED "Rifle (Sergeant)" - the invariant stays true and the row/log self-attributes
("Blood Squad's Sword (Sergeant)'s Sergeant added 0.167 extra hits", both combat kinds probed). Guards:
`SergeantRuleIntegrationTests` (6: mark shape + name uniqueness, replace ordering, multi-application,
affects-All and unit-scope controls, load + batch-owners), `SergeantShippedDataTests` (4: Weapon scope
load-bearing, census pins the section shape, embedded copies, real-book compile). **33 -> 21 dead,
7 names.**

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
`RepelAmbushers` / `AmbushBeacon` (P22a), `EnableBuffRelay` (Extended Buff Range, 2026-07-29).

## Activation & ability seams

**Instinctive — DONE 2026-07-31** (4 refs: all Goblin Reclaimers "Ramshackle Crew" affects-All upgrades;
engine `1399368` + `2b920e5` + `d6b96e3` + the correction, app `84ddfec` + the correction). OPR verbatim:
"When this model is activated, if it is able to shoot/charge an enemy unit, then it must immediately
attack the closest valid target and gets +1 to hit rolls for that attack."

**The 2026-07-23 deferral premise was stale as flagged** (P20's target-gating seam made both choosers a
normal slice), **and then the first cut over-read the rule.** Built 2026-07-30 against a design session
reading of "able to shoot/charge" as including able-via-a-move; the owner's rules research on 2026-07-31
found no such clause and cut it. What that removed: `CompelledAttackMovePlanner`, the auto-resolve menu
options, `DefineMovementPathRequest.MustEndAbleToAttackRule`, `CompelledMoveDestinationCheck` and its CLI
+ GUI enforcement, and the planned-move plumbing through UnitActionContext / MovementStage /
DefinePathStage. **Standing lesson 4 again, from the other side:** the filed premise was stale, but so was
the replacement - "surface the fork" is not the same as "confirm the rule text", and a design session can
invent scope as easily as a stale ledger can preserve it. Kept from that work: `ValidateAgainstBudgets`
(the shared move validation DefinePathStage now uses for its one caller) and the quiet capability queries.

**What the rule actually is, and the seam that makes it exact.** The condition is read ONCE, when the
unit activates. `ChooseActionStage` decides on its first visit of the activation (the only stages before
it are ActivationStart and the Surprise Attack burst) and stamps **`TokenType.CompelledToAttack`**
(cleared at end of activation). A unit that could NOT attack then is untouched for the whole activation:
it moves, and it attacks with a free target and no bonus. A unit that COULD gets the menu collapsed to
the attack actions - both kinds when both apply, since the rule compels the TARGET, not which attack -
with Move/Pass/Cast/abilities barred until it attacks.

The token, not the capability, is the live obligation: `CapabilityRuleQueries.MustAttackClosestSource`
answers "carries such a rule" (display name), `IsCompelledToAttackClosest` answers "is bound right now",
and both choosers plus the authored +1 read the second. That split is what makes "moved into range =
normal attack" true rather than aspirational, and a mutation that points the choosers at the capability
is caught. `Effect.CompelClosestTarget` remains the rule-agnostic vocabulary (capability answer, gated
`AllModelsHaveThisRule` per #267 - a joined hero without the rule frees the unit).

Target narrowing (unchanged from the first cut): `ApplyTargetGating` grew a closest-target gate run LAST,
so "closest VALID" means closest among what survived Limited/Deadly/QuickShot gating - the compulsion
never points at a target the unit may not shoot (the #200 livelock class); `ChooseMeleeDefenderStage`
narrows the same way; both narrow BEFORE the request is issued, so human and AI comply by construction
(the P20 pattern). Ties within 0.001in all stay selectable. The +1 rider is data: two
`rollModifier(Hit,+1)` entries, each gated `TokenPresent(CompelledToAttack)` AND the combat kind
(`Not(IsMelee)` for the shot, `And(IsMelee, IsCharging)` for the swing) - so a strike-back never gets it,
and neither does a unit that merely HAS the rule.

**Two gotchas the probes caught, both worth remembering.** (1) A probe army embeds its own
`ruleDefinitions` copy, so re-gating the supplement left `Scenarios/armies/InstinctiveMobs.fdgarmy` firing
the OLD unconditional rider - the engine was right and the probe lied. Re-embed probe armies, not just
books. (2) The move-into-range test was VACUOUS as first written: stacking the unit's models on one point
let the near enemy's base occlude the far one for every model, so the far row was unfireable on its own
merits and the target-gating assertion passed no matter what the gate did (standing lesson 3 - the
mutation that survived is what exposed it). Translating the models instead fixed it.

Also fixed alongside: single-participant `Evaluate` logged "X's <rule> applied an effect" for every
capability query, contradicting `CapabilityRuleQueries`'s documented non-logging contract - pre-existing
noise the per-activation compulsion query would have turned into a stream.

**Recorded, not fixed:** the AI does not reason about the compulsion, but its attacks always comply
(it answers the narrowed requests). Pre-existing and BISECTED to pre-slice engine `5c4dd2c` (not a
regression): the solo-AI Dummies probe army sometimes gets an empty fresh-activation menu when far from
everything - filed for the next AI pass.

Guards: engine `CompelClosestTargetRuleIntegrationTests` (16: capability + hero-join control, menu
collapse both kinds, unable-at-activation acts freely, **moved-into-range is NOT compelled** (menu, token
and target list), the obligation outlives the menu, closest-VALID-not-closest-on-table, melee narrowing,
rider combo incl. strike-back exclusion and the no-obligation-no-bonus control); 4 mutations of the
activation-time semantics each caught. App `InstinctiveShippedDataTests` (7: authored shape, riders
pinned to the obligation gate, 4-site census all-affects-All, embedded copy, real-book compile). Probe
`instinctive-compelled-shoot` shows both halves on ONE unit across two activations: the Freaks that move
into contact charge at 4+ with a free target list ("Tough Dummy - too far away"), and the same unit
activating already in contact is collapsed to Charge, narrowed ("Tough Dummy - Instinctive: must attack
the closest") and swings at 3+. **9 -> 5 dead, 2 names.**

**Surprise Attack — DONE 2026-07-30** (2 refs; engine `86af87d`). OPR verbatim: "Counts as having
Infiltrate. The first time this unit is activated, pick one enemy unit within 6in in line of sight, and
roll X dice. For each 2+ it takes one hit with AP(1)." Filed as blocked on P10; the filed premise that
`StormOfHits` was "very close" turned out to be **half right and half a trap** (standing lesson 4): the
looping-stage shape was reusable, but Storm's pool is rolled DECISIVELY - correct there, because its
successes are target PICKS - while these successes are a HIT COUNT and must stay fractional (standing
lesson 5). Reusing the effect would have int-locked the burst.

New vocabulary: **`Effect.DealPooledHits(ValueSource DiceCount, SuccessThreshold, ArmorPenetration)`** ->
`RuleOperation.InvokeDealPooledHits`, the hit-pool sibling of P10a's `DealAutoWounds` (those successes are
save-skipping wounds; these are ordinary hits through the full pipeline). Range and LoS ride the ability's
own `TargetSelector` rather than the effect, so `AbilityTargeting`'s existing sight leg is reused as-is -
Storm's duplicated `RangeInches` was not copied. `SyntheticHitResolution` gained **`ResolveRolled`**, the
same fold over ALREADY-rolled hits, so the fractional success histogram reaches the save/wound children
without a scalar round-trip. **`SurpriseAttackStage`** runs between `ActivationStartStage` and the action
menu; `ActivationStartStage` filters the effect out of its own offers (it is a LEAF stage and cannot run a
child chain) - the ChooseActionStage-routes-Storm shape, moved one stage earlier.

**Two owner rulings (2026-07-30), signed off before building.** (1) *Mandatory at activation start, not a
menu action*: the text says "the first time this unit is activated", so it fires by itself before any
move - the Storm shape was rejected because it would let the player defer or decline the burst. The only
decision is WHICH enemy, so the pick is a non-cancellable `SelectionRequest<UnitData>` and a single
eligible enemy resolves with no prompt at all (`ActivationStartStage`'s "nothing to choose" precedent).
(2) *The burst is LOST, not banked*: the `OncePerGame` marker is paid BEFORE the target search, so a unit
whose first activation has no enemy within 6in in sight never gets it. This is why the cost is spent
against the bearer when the target search comes back empty.

"Counts as having Infiltrate" is authored as a **copy of Infiltrate's own passive entry**, not a grant -
the deployment arm has to be live before deployment and nothing grants a rule that early; the app test
compares the whole entry, so retuning Infiltrate cannot silently desync the copy. It is load-bearing on
one of the two carriers: the Hive Burrower buys Surprise Attack(5) on an item that REPLACES the item
granting Ambush, so without the passive it has no route onto the table. Both carriers are single-model
units (census pinned in tests), so unit scope is exact - the Sergeant question does not arise.

**The AI's pick, added 2026-07-30 on request.** The burst first shipped with the AI taking the first
eligible enemy (the solo fallback). It now scores them: **`CombatMath.EstimatePooledHits`** prices the
pool through the same save/wound mirror `EstimateSpellDamage` uses, and `TacticianPlanner`'s
`TryChooseBurstTarget` ranks candidates by **fraction of the target's remaining wounds removed x what the
target is worth** - `SpellValuation.TargetValue`'s damage arm, reused rather than re-invented, so the
burst and a damage spell answer "which enemy?" the same way. Deliberately simple: no positional or
objective context, and no save-it-for-later (the burst is mandatory on this activation or never). The
discriminator is `SurpriseAttackStage.PICK_INSTRUCTION_PREFIX` - the prompt was reworded so the constant
LEADS ("Pick the enemy unit hit by <rule>"), matching how the spell and deploy-order branches key
themselves; the pool's dice/threshold/AP are read off the ACTIVE unit's own rule (a burst can only resolve
during its bearer's activation), and anything unresolvable falls back to the solo pick rather than
guessing. Guards: 3 tests in `TacticianTargetChoiceTests` (armor drives it, worth breaks a damage tie, and
a carrier-less unit falls back), each caught by mutation; probe `surprise-attack-ai-target.json` run on
both profiles - the solo AI takes the first-listed near-dead squad, the Tactician takes the Tough Dummy
behind it.

Guards: engine `SurpriseAttackRuleIntegrationTests` (10: the argument-driven pool, the once-per-game gate,
the burst with its AP folding into the save against an AP-less control, the mandatory pick, out of
range/out of sight, the second activation, the fractional pin, and the ActivationStartStage seam), app
`SurpriseAttackShippedDataTests` (8: authored shape, the Infiltrate-entry equality, the 2-ref census,
embedded copies, both carriers through the real compiler). Five mutations were each caught by exactly one
test. Probe `Scenarios/surprise-attack-burst.json`: 5 dice -> 4.17 hits at AP(1) into a Defense 5+ squad
(save threshold 6, 3.47 wounds, 3 models dead), the pick prompted because two enemies were in range, the
unit still moved and charged afterwards, no second burst across four rounds, and the second carrier alone
in a corner logged "no enemy within 6in in line of sight - the surprise is spent". **11 -> 9 dead,
3 names.**

**Extended Buff Range — DONE 2026-07-29** (9 refs: all HumanDefenseForce, all Field/Vehicle Radio items;
engine `a83c4a0`). The generalized Spell Conduit the audit called for, on the capability seam:
**`Effect.EnableBuffRelay(rangeInches)`** answers `Lifecycle_OnCapabilityQuery`, and
**`AbilityTargeting.EligibleTargets` grew the relay leg** — a candidate out of a FRIENDLY pick's own range
is still eligible when some other friendly bearer is within the relay's 12in of the user AND the candidate
is within the ability's own range of the bearer (12 + 12 = the audit's "across 24in"). The relay relaxes
RANGE only; affinity/token/rule filters unchanged, and two gates are pinned: Foe picks never relay (the
rule relays buffs, not target acquisition) and sight-requiring picks never relay (a relay lends position,
not eyes — no corpus Friend-pick needs LoS, so the combination is gated, not guessed: grow-on-demand).
All three `EligibleTargets` callers (menu gating, the stage, StormStage) inherit the leg for free. No
Shaken gate authored — Conduit's comes from its own wording; this rule's carries none. Engine
`ExtendedBuffRangeRuleIntegrationTests` (9: capability answer, both legs' boundaries, Foe/enemy-bearer/LoS
gates, no-relay control, end-to-end through BeforeAttackActionStage with a requester that only takes
OFFERED targets — the canned one force-picks and would mask a dead relay); app
`ExtendedBuffRangeShippedDataTests` (authored shape, 9-ref census, embedded copy, capability answer
through the real attach path). Probe: Commander's 12in Precision Shooter Buff offered and landed on a
squad 20in away through a radio at 10in — "Far Squad's Precision Shooter added +1 to Hit rolls" on its
next volley; control with the radio at 16in (first leg dead) dropped both the far squad AND the radio from
the pick list. **42 -> 33 dead, 8 names.**

**Inquisitorial Agent — DONE 2026-07-29** (20 refs, the item's largest single name; engine `0a14cd8`).
> "Once per game, if all models in this unit have this rule, it may be activated even if it had already
> activated this round (**stops being fatigued** when activated for the second time). Only up to **one
> third of the units in the army with this rule at the beginning of the game (rounding up)** may use it in
> a single round."

The reactivation itself was already Martial Prowess's (`Cost.OncePerGame` + `Effect.Reactivate` +
`AllModelsHaveThisRule`), exactly as filed. Two riders were new, and the ledger had recorded only one of
them — the fatigue clause is in the corpus text and was missing from the row.

**The "novel army-global state" turned out not to be needed.** The quota is derived entirely from
existing per-unit state:
- **Roster** (`N`, fixed at game start): counted straight off `ArmyData.UnitBindings`, which is
  **append-only** — a destroyed unit stays in the list, marked not-alive. So counting bindings whose rules
  carry the offered ability IS the game-start roster, casualties included, with no snapshot. Pinned by
  `DeadAgentsStillCountTowardTheRoster` (a live-only count would allow ceil(2/3)=1 instead of 2).
- **Uses this round**: one new `TokenType.ReactivatedThisRound`, round-end cleared, stamped on ACCEPTANCE
  (a declined offer costs the army nothing). The unit's own once-per-game marker stays separate and
  permanent.

Because it is all tokens, it saves, resumes and networks for free — no new serialized field, no new
round-end reset, no new sync. **The assumption has a guard**: a unit-CREATING rule in the same book
(Spawn/Split/Reinforcement) would inflate the roster mid-game, so an app-side test fails loudly if any book
ever pairs one with this rule. None does today.

**Where the gate lives:** `DeterminePlayerTurnStage`, checked *before* the player is asked, so a full quota
is silently unavailable rather than offered and then refused. It has to be there — neither the `Cost` seam
(`IsAffordable` sees one unit) nor a `Condition` (the hook context carries one unit) can see the army. The
declaration rides `Effect.Reactivate(ClearsFatigue, ArmyRoundQuotaDivisor)`; both default off, so Martial
Prowess is unchanged, and a control test pins that. Ability matching is by the ability itself, not a rule
name, so an army-flavored rename counts the same way.

**Verified in play**: four agents, `Agents 0`/`Agents 1` reactivate in round 1 (each logging "stops being
fatigued for its second activation"), then `Inquisitorial Agent: 2 of 4 units have already used a second
activation this round (limit 2) - not offered`; round 2 re-opens the cap for `Agents 2` while 0 and 1 never
reappear. **67 -> 47.**


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

## Unit creation & restoration (P17 DONE 2026-07-28, 24/24; Armor DONE 2026-07-29)

**Armor(X) defense set — DONE 2026-07-29** (11 refs across 7 books, #196 F16 handoff; engine `d8c052e`).

> "Counts as having Defense X+ **in place of** the model's own Defense stat." #196 shipped a zero-hook
> marker-with-arg so the name resolved and the description showed — the mechanic was absent, and every
> site is a PAID upgrade (Heavy Armor 5pts, mounts, chariots) whose other bundled rules (Tough/Fast/
> Strider/Impact) all worked. These 11 refs never showed in the dead count; the row lived here instead.

- **Owner ruling 2026-07-29: a literal SET, not a floor** — replaces the base even where the base is
  better (no corpus site worsens today; floor was offered first and declined as less literal). Pinned by
  `Armor_IsALiteralSet_NotAFloor`.
- **Vocabulary:** `Effect.SetDefense(ValueSource)` ("setDefense", reads `Arg(0)`),
  `RuleOperation.SetDefense`, `IDefenseSetSink`/`DefenseSetSink` (several sets -> lowest wins,
  mirroring MaxWoundsSink's best-of).
- **Seam: Tough's, not the save path's.** Fires at `Lifecycle_OnUnitCreated`; `UnitCreationRules.Apply`
  folds the sink and WRITES `UnitData.Defense`, so every reader — the save stage, impact, reflect,
  synthetic hits, `GetSaveDefense`, and the AI's CombatMath — sees it with zero per-path folding. The
  rejected alternative (fold at `Shooting_OnHitRollComplete`, carried on `RollToHitResults` like
  Shielded) would have missed impact/reflect and needed a CombatMath mirror. Resume-safe: `Defense` is
  a serialized property, so the skip-creation-rules-on-resume path reloads the written value.
- **Joined hero:** the hero's standalone unit never runs UnitCreationRules, so `HeroJoinResolver` bakes
  the set into `HeroAttachment.Defense` (`ResolveJoinedHeroDefense` — matched by EFFECT SHAPE, not rule
  name, so a book alias can't dodge it), mirroring how `heroWounds` bakes in Tough. While squadmates
  live the unit saves at the unit's stat (the hero's Armor stays out of it), per the last-model-Defense
  philosophy; the host's own Armor covers everyone via the unit stat.
- Tests: engine `ArmorRuleIntegrationTests` (6 — set-from-arg, SET-not-floor pin, best-of, no-op
  control, hero-join bake + host-no-leak, join-without-armor control); app `ArmorShippedDataTests`
  (4 — authored shape, 11-ref census across the 7 books, embedded copies carry the mechanic,
  shipped-data end-to-end to Defense 4). Both stat writes mutation-checked. Probe: D6+ dummies with
  Armor(4) log "Base roll to save is 4" in play; non-Armor units unchanged.
- `RuleSupplementLintTests` allowlist entry removed (its now-fires tripwire failed as designed).

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

**Hazardous self-wound arm — DONE 2026-07-29** (15 refs, all weapons in RatmenClans; engine `7934e88`).
> "Attacks with this weapon get AP(4), but **this weapon's unit takes one wound on unmodified rolls of 1
> to hit**." #196 shipped the AP half only, so for three passes Hazardous was **upside-only** — the one
> open item in this ledger that was a live balance bug rather than missing coverage. Dead count unchanged
> (the name already resolved); this closes the mechanic and the flag.

**The filed plan was wrong in the expensive direction** (standing lesson 4, again). It called for a
`RuleOperation.InvokeDealWoundsToUnit` + `IOperationServices.DealWoundsToUnit` + a new
`OperationExecutor.Execute` point in `RollToHitStage`. None of that was needed: `RollToHitStage` already
consumes `ReduceArmorPenetration` as a plain summed op, and the stage is async and holds the attacker. So
the whole arm is one effect, one op, and four lines in the stage — no interface member, no executable, no
executor point. `CapabilityEffect.Apply` is `sealed` and `ApplyCore` never sees the `RuleInvocation`, so
the executable route would have needed a base-class change too, for nothing.

New `Effect.SelfWoundOnUnmodifiedRoll(OnRollValue, Count)` : `CapabilityEffect<IHasUnmodifiedHitRolls>` —
`AddExtraHit`'s exact shape, reading the same histogram, producing `RuleOperation.InflictSelfWounds`. The
supplement's Hazardous grew a **second entry at the same hook** (AP + overheat), gated
`unmodifiedRollEquals 1`. Per grow-on-demand the op stays non-executable; the comment on it names the
condition (a second hook needing self-wounds) that would earn it an `IOperationServices` member.

**Two owner rulings, 2026-07-29:**
1. **The wound lands after the whole attack resolves**, not at the roll. The alternative interleaved the
   shooter's casualties into the middle of the target's saves and could tear the attacking unit down while
   later stages of its own attack were still running (destruction spills transports and clears marks). The
   player still learns at the roll: the to-hit beat carries a `2 self-wounds` proc chip.
   **Implemented as a carried total, not a local await** — see the tail-call trap below. `RollToHitStage`
   counts the wounds onto `RollToHitResults.SelfWounds`; **`ApplyWoundsStage`** applies them after the
   target's, which lands the behaviour for shooting, melee swings and Strafing at once (every chain with a
   hit roll ends in that stage) and skips the chains that have no hit roll to overheat on.
2. **Unignorable.** Applied straight through the casualty seam, so no save and no Regeneration read — the
   treatment dangerous terrain and No Retreat already get. The corpus text does not say "can't be ignored",
   so this is a consistency call, and `RegenerationDoesNotSaveTheShooterFromItsOwnGun` states it out loud.

**Reuse:** P7's wound-dealing body moved out of `MoraleUtilities` into
**`CasualtyPresentation.ApplyUnitWounds`** — the shared "the UNIT owes a pool of wounds" path (spread
living models front-to-back up to each one's capacity, casualty beats through the #232 cascade, killer-less
destruction seam). No Retreat and Hazardous now differ only in how they count their wounds.

**Dice invariant:** the 1-count is FRACTIONAL and stays that way. Flooring it is the pool-size precedent
(P17d/P7) and does not apply to a wound total — a 2-attack pistol owes 1/3 of a wound per volley and would
never self-wound under a floor. Two tests pin the fraction. *Mutation note:* the first draft used a 6-die
volley, whose 6 x 1/6 = exactly 1.0 survives a floor untouched — the test read like it pinned the invariant
and did not. Re-cut to 4 dice (2/3 of a wound); the floor mutation now reddens both.

**The tail-call trap — the reason to keep probing in play (new standing lesson 6).** The first cut placed
the wound application *after* `await onFinished(results)` in `RollToHitStage`, which reads as "after the
attack resolves" and passed all 14 unit tests. **In a real game it never executed once.** A combat stage's
`onFinished` is effectively a tail call into the next stage (`CombatStage.Execute` -> `AddResult` ->
`NextStage.Activate`), and the continuation below it is never resumed — no existing stage has a line after
that call, which is the convention that hid it. It *looks* live under `NoOpLayer`, whose
`ExecuteTransition` returns `Task.CompletedTask` immediately, so the harness dutifully ran the dead code
and the ordering test even passed for the wrong reason. Caught only by the headless probe: the rule's two
hook entries both fired and narrated, `selfWounds` computed 0.5 correctly, and no wound was ever dealt.
**Anything a combat stage must do after handing off belongs in a later stage, not below its `onFinished`.**

**Verified in play (after the fix):** a 3-attack Plas-Burst volley at Quality 2+, probabilistic ->
`Applying 2.0833333 wounds killed 2 models` (the target), immediately followed by
`Overheaters takes 0.5 wounds from its own Plas-Burst Rifle`. Correct value, correct order.

**10 mutation checks**, each reddening only the intended tests: wound the defender (9), apply at the roll
(1), floor the total (2), read the wrong face (6), drop the lint entry (app lint, 1), skip the destruction
seam (1), ignore per-model capacity (1), drop the proc chip (1), drop the `ApplyWoundsStage` call (8), never
set the carried total (9).

**Found in passing:** this ledger's own claim that `RuleFireLint.Check` skips a rule's later passive
entries is wrong — see Tooling / hygiene. Hazardous's second entry is linted, and the M5 mutation's failure
message names it as "passive entry 1".

**P15 Unpredictable Marks — DONE 2026-07-29** (5 refs: Fighter Mark 3 in AlienHives, Shooter Mark 2 in
GoblinReclaimers; engine `bad8725`). The residual: the two mark names were dead `no-definition`, and
authoring them alone would have shipped a no-op — a mark-granted Unpredictable only reaches the attacker
when `ClaimTargetMarks` converts it at the hit stage, AFTER the action-level branch roll, so both arms
gate out on a branch that never rolled. `UnpredictableBranchResolver.Resolve` now takes the DEFENDER and
treats a Mark token granting an applicable Unpredictable rule as a roll trigger (kind-aware: a Shooter
mark doesn't roll for a melee swing; keyed on the granted rule so ordinary marks consume no die — the
seeded stream stays untouched). Definitions authored on the uniform mark-family template (18in, LoS,
`markTarget` -> the CORE Fighter/Shooter rules, whose branch-gated arms already existed). Engine tests
extend `UnpredictableRuleIntegrationTests` (defender-mark roll, wrong-kind no-die, non-Unpredictable-mark
no-die, and a real-CombatActionContext end-to-end proving branch + claim + arm compose to quality-1);
app `UnpredictableMarkShippedDataTests` pins authored shape, census, embedded copies. Probe: mark placed
via the CLI ability, then the marked unit charged and the STRIKE-BACK rolled ApBonus — "Markers's
Unpredictable Fighter added -1 to Save rolls", threshold 6 -> 7 in play. **47 -> 42 dead, 9 names.**

**P15 randomized-branch (Unpredictable) — DONE 2026-07-11** (48 of 53 refs). "Roll one die: 1-3 AP(+1),
4-6 +1 to hit." Forks resolved with Chris: **decisive** selecting die (a branch selector cannot be
averaged into "half a modifier"), and **once per attack ACTION**, not per weapon. The two arms consume at
DIFFERENT hooks (`Shooting_OnHitRollModifier` vs `OnHitRollComplete`) and ops at one hook aren't visible
at the other, so the single roll is resolved ABOVE the hooks and threaded down: `EUnpredictableBranch` +
**`IHasUnpredictableBranch`** + `Condition.UnpredictableBranchIs`. `UnpredictableBranchResolver` rolls
only when the attacker carries an applicable rule (native, per-model, or aura-granted), so the seeded
stream (#193) is untouched for ordinary attacks; cached per action, reset on `SwapCombatRoles`.
**576 -> 528.** *Marks shipped 2026-07-29 — see the entry above.*

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

**P16 one-shot extra attack (Takedown Strike / Takedown Shot) — DONE 2026-07-30** (7 refs; engine
`8aab7c8`). OPR `eyMkgYDVrP7C` / `LPEKodkJ6xPS` verbatim: "Once per game, when it's this model's turn to
attack in melee, it may make one attack at Quality 2+ with AP(2), Deadly(3), and Takedown" / "... when this
model shoots, it may make one extra attack against the target at ...". **Two findings shrank the slice.**
(a) *Every rider is already a weapon rule*: "at Quality 2+" IS `Reliable` (`QualityFloor`, folded by
minimum, so it lifts a bad shooter and leaves a 2+ one alone), and Deadly(3) / Takedown are themselves - so
the only new vocabulary is "make one extra attack with an AUTHORED profile", and the riders fold through the
shared hit/save/wound stages exactly as a fired volley's do. (b) *All 7 carriers are single-model units*
(`minModels=maxModels=1`: Shadow Hunter, Master Jester, Elite/Clan Handler, Rebel Leader, Cult Hitman,
Surveillance Ministry Assassin), so the per-model scoping that cost Sergeant a whole slice is a non-issue -
unit scope is exact, and the once-per-game ability fires once even for a hero merged into a squad. Census
pinned in tests.

New vocabulary: `Effect.ExtraAttack(WeaponName, Attacks, ArmorPenetration, WithRules)` ->
`RuleOperation.InvokeExtraAttack`, plus **`EHookID.Combat_OnAttackWindow`** + `AttackWindowContext`
(attacker, defender, combat kind) - the first `Combat_` hook, fired in both kinds because the two rules
differ in nothing else and separate on `Condition.IsMelee`. `ResolveExtraAttackStage` builds the synthetic
weapon (the `BeforeAttackActionStage` build, resolved at dispatch time) and runs the shared attack chain as
real children, the StrafingStage pattern.

**Three owner rulings (2026-07-30), all signed off before building.** (1) *In-pipeline in all three chains*,
not a before-attack menu action: the stage is instantiated in ShootStage (after the weapon/target choice,
before FireStage), in MeleeStage (after in-range determination, before the swing loop) and in
`StrikeBackStage` - so "its turn to attack in melee" is true of a unit that was CHARGED, not only of the
charger. This is deliberately unlike the deferred Ravage strike-back arm, which rides charge-contact. The
target is inherited, never picked: it is already range/LoS/contact-validated, so a prompt could only offer a
way to break the rule. (2) *Melee Takedown turned ON* - `BuildTargetListStage`'s `if (!metaData.IsMelee)`
gate is gone, so a strike picks its victim out of the enemy unit (the assassin fantasy the rule exists for).
Safe for existing data because **no melee weapon in any of the 48 bundled books carries Takedown** - pinned
by `ExtraAttackShippedDataTests.NoMeleeWeaponInAnyBook_CarriesTakedown`, which fails loudly if a re-import
ever changes that. (3) *EXTRA, not instead-of*: the source's sibling says "one extra attack" and this one
omits the word, but a paid once-per-game upgrade adds rather than substitutes. Verified in play - the
normal volley/swing follows the injected attack.

**A C# init-order trap, worth remembering.** `ParentStage`'s constructor calls `PopulateTransitions`, so a
constructor-assigned `_isMelee` field is still `false` there - the melee instance silently got the
ranged-only `CoverCheckStage` and threw "Ran combat stage ... when a result was already present". The combat
kind therefore lives in the TYPE (`ResolveMeleeExtraAttackStage` / `ResolveRangedExtraAttackStage` over an
abstract base with `protected abstract bool IsMelee`), which an override answers correctly during base
construction. Any future stage whose child chain varies by construction parameter has the same problem.

**The lint earned its keep again**: `RuleSupplementLintTests` failed both rules with "never offered by
GatherOffers in any synthesized context" until `RuleFireLint.ContextVariants` learned to build an
`AttackWindowContext` - the missing capability-wiring step that produced the Breath Attack no-op. Also
extended: `AbilityOfferingHooks` and `IsOpHandledAtAbilityHook`.

Guards: engine `ExtraAttackRuleIntegrationTests` (15: the combat-kind split both ways, the emitted profile,
the once-per-game gate, each rider against a control that removes only it, the melee Takedown confinement,
decline-spends-nothing, the strike-back wiring driven through the real `StrikeBackStage`, and a regression
that an ordinary swing still spreads its wounds); app `ExtraAttackShippedDataTests` (10: authored shape,
profile, the single-model census, the no-melee-Takedown invariant, embedded copies, real-book compile).
Mutation-checked: restoring the melee gate, removing the strike-back window, and dropping `WithRules`
resolution each fail the right tests. Probe `Scenarios/p16-takedown-extra-attacks.json` (headless, exit 0):
both rules fire, all three riders self-attribute in the log ("Reliable set base Quality to 2+", "Deadly(3)
multiplied wounds by 3", "Takedown re-scoped the attack to a single target model" - the last one IN MELEE),
the normal attack follows, and across 4 rounds each unit keeps attacking but is never offered a second
strike. **21 -> 14 dead, 5 names.**

**P19 out-of-order activation (Coordinate) — DONE 2026-07-30** (3 refs; engine `6468b24`). OPR
`FPlO2MymiMc0` verbatim: "At the end of this unit's activation, another friendly unit within 12in that
hasn't activated yet may be activated immediately. May not be used if this unit was activated via
Coordinate." **The filed premise was wrong** (standing lesson 4): this row said "generalize the live
self-`reactivate` to a chosen friendly unit", and Coordinate is not a Reactivate variant at all.
`Reactivate` re-adds an ALREADY-ACTIVATED unit to the pool so it appears as a CHOICE later; Coordinate
takes a unit that has NOT activated and makes it the next activation, now. Reactivate grants an extra
activation, Coordinate only reorders ones already owed - the acting side gains **tempo, not activation
count**. So the work landed in the turn-order layer, the first rule to reach it.

**Built as a rule-agnostic primitive, at the owner's insistence** (2026-07-30): nothing in the engine
mentions Coordinate. `Effect.ActivateUnitNext` -> `RuleOperation.InvokeActivateUnitNext`, plus three
tokens named for the mechanism - `ActivatesNext` ("takes the next activation, ahead of the normal
alternation"), `ActivatedOutOfOrder` ("this activation was granted by another unit"), and
`ActivatedThisRound`. `TeamPlayerAlternationCursor.PointAt(PlayerID)` is the generic cursor move.
No new hook: `Activation_OnEndOfActivation` already offers abilities.

**Why a token and not a context field.** The producer (`ReconcileEndOfActivationStage`, on
`ISingleTurnContext`) and the consumers (`DeterminePlayerTurnStage` + `ChooseUnitToActivateStage`) sit in
different layers, so the grant has to be carried. A token carries it with no plumbing AND survives a
resume for free - decisive here, because #052's rolling save point is written at the TOP of
DeterminePlayerTurnStage, i.e. AFTER the grant, so stage-local state would be lost on a load. **The pool
stays authoritative**: a flag is honoured only for a unit genuinely still unactivated, alive and on the
table, so a marker outliving its target (killed before its turn) degrades to a normal advance instead of
pinning the round on a unit that can never be picked.

**`ActivatedThisRound` is new state and worth knowing about.** "Has this unit activated this round?" was
previously derivable ONLY from the round context's pool, unreachable from a rule, an ability's targeting,
or any stage below the round layer. It is now stamped/cleared in lockstep with the pool by the only two
methods that move it (`MarkUnitAsActivated` / `ReinstateUnitForActivation`, the latter mattering for
Martial Prowess). It is what lets the eligibility filter see an ALLY's units, which no per-player pool
reachable from a turn context covers.

**Three owner rulings (2026-07-30).** (1) *The generic-jump alternative was rejected on the merits*: a
"transfer control to stage X with a context object" hook would be aimed at the wrong joint - the stage
SEQUENCE is already correct here (DeterminePlayerTurn -> ChooseUnitToActivate -> activate); only the DATA
those stages compute is wrong. Its real costs are per-layer context ownership (`GetNewChildContext`),
`GetResumeEntry`/#052, and #203's tail-call stack discipline. Filed separately as its own idea, to be
scoped on reactions/interrupts rather than sized by a 3-reference faction rule. (2) *An ally's unit is a
legal target and ITS OWNER controls it* - which simplified the primitive rather than complicating it, since
"the next activation is the flagged unit, and the acting player is its owner" covers own-unit as the case
where the owner is you. Hence `PointAt` moving the team index too. A beat announces it (`Notice`), naming
the controlling player when it is not the granting one. (3) *The end-of-activation Yes/No default flipped
NO -> YES*, covering Ambush Re-Deployment as well: these are paid-for one-shots, and a default of NO meant
every AI and EOF resolver declined them every time - an army paying points for an ability only a human
could use, and a human never seeing the mechanic played against them.

Data: authored `Free` (the source states no per-game or per-round limit; its only brake is the anti-chain
clause, authored as `Not(TokenPresent(ActivatedOutOfOrder))` - data, not code), selector
`(12in, 1, 1, Friend, no LoS)`, embedded into HumanDefenseForce. All 3 carriers are single-model HDF heroes
(Tank Company / Storm / Company Leader), so unit scope is exact.

Guards: engine `ActivateUnitNextRuleIntegrationTests` (15: the grant, the anti-chain condition, the
pool/token lockstep, the bearer and already-activated exclusions, range, cancel-costs-nothing, the ally
target + beat, the cursor staying vs crossing to an ally, stale flags on activated AND dead units, the
menu-free activation, and a control that ordinary activations still get their menu); app
`CoordinateShippedDataTests` (6). **Mutation-checking caught a vacuous test**: the first
"pool authority" mutation PASSED because the test only exercised pool membership while the mutation cut
the liveness half - so a `Consumer_AFlagOnADeadUnit_IsIgnored` test was added and both halves re-checked
red. Probe `Scenarios/p19-coordinate.json` (headless, exit 0): every round runs General ->
**First Squad out of order** -> opponent, and First Squad carries Coordinate ITSELF and is never offered it
(4 offers across 4 rounds, not 8) - the anti-chain verified in play, not only in a unit test.
**14 -> 11 dead, 4 names.**

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

- ~~**Sergeant + Armor(X)** (23 refs, both #196 F16 handoffs) are the per-model / per-stat attribution
  family~~ — Armor shipped 2026-07-29 on Tough's creation seam (unit-wide stat write, no per-model
  attribution needed after all: every corpus site is a single-model hero or an affects-All squad).
  ~~Sergeant (12) remains, and remains genuinely per-model~~ — Sergeant shipped 2026-07-29 as weapon
  marking (the marked copy IS the model, owner-chosen over true per-model rules); the per-model framing
  turned out not to need per-model machinery.
- ~~**Extended Buff Range + P19 Coordinate** (12 refs) both reach *another friendly unit*; Conduit's relay
  machinery is the nearest precedent for the first~~ — Extended Buff Range shipped 2026-07-29 on the
  capability seam (`EnableBuffRelay` + AbilityTargeting's relay leg), and ~~Coordinate (3) remains~~
  Coordinate shipped 2026-07-30 as its own turn-order primitive - it needed no relay, since its 12in pick
  is measured from the bearer.

## Outcome

_(written when the item closes)_
