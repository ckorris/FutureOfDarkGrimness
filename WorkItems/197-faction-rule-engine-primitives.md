# 197 — Faction rule coverage, part 2: engine primitives + the scope-mismatch bug

**Status**: in progress (slice 0 DONE 2026-07-09; later slices still carry their own forks)
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
| 10 | **Distance at the save-roll hook** | `SaveRollCompleteContext` carries no `DistanceInches` (only `HitRollCompleteContext` does), but `AddExtraWound`'s effect must fire at `Shooting_OnSaveRollComplete` (it reads the save-roll histogram). F5's Boost variants need "extra wound on 1-2 instead of 1, when shooting/charging enemies over 9in away" and can't express the distance gate at the hook the effect requires. Thread distance into `SaveRollCompleteContext`, or find another way to carry it through to the save stage. | Warbound Boost (2), Warbound Boost Aura (5), Infected Boost Aura (3) |
| 6 | **Reroll threshold parameter** | `RerollCondition.OnUnmodifiedValue` carries no value — `RerollSink.cs` hardcodes it to the unmodified max face (6). F10's Boost variants need "re-roll unmodified 5s *or* 6s". `AddExtraHit`/`AddExtraWound` already parameterize their trigger value per entry (`OnRollValue`); `RerollCondition` needs the same, or a second variant with a threshold. | Mischievous Boost Aura (4), Scrapper Boost Aura (2) |
| 7 | **`moraleTestThen` outside spell casting** | `Effect.MoraleTestThen.Apply()` is an intentional no-op — `CastSpellStage` special-cases the effect before calling `Apply()` and runs the morale-test-then-branch itself. None of the five generic ability-offering stages (`ChooseActionStage`, `PreAttackStage`, `StrafingStage`, `DeterminePlayerTurnStage`, `DeployUnitStage`) do the same, so a plain `SpecialRuleDefinition` activated ability using it is a genuine no-op in play (confirmed by `RuleFireLint`, not assumed). Wire `MoraleTestThen` into the generic ability path, or add a non-spell "morale test, conditional consequence" primitive. Both corpus uses are ordinary unit-rule references (`unit.rules`), not spell-list entries, so modelling them as `SpellDefinition`s instead would not fix the corpus. | Mind Control (4), Fatigue Debuff (3) |
| 3 | **Vengeance** | "Place N markers on the unit that destroyed this one, N = models with this rule in this unit at game start; friendly units get +N to hit where N is the marker count on the target." Needs two things that don't exist: a magnitude source for "count of models with rule X in the bearer unit" (`ValueSource` only has `Literal`/`Arg`), and marker-scaled roll magnitude (this is P13, already tracked below — Vengeance can piggyback on P13 once it lands, but still needs the model-count source on top). | Vengeance (3) |

## Slices — by leverage

Reference counts are corpus-wide (44 books). Primitive numbers are #100's.

| Refs | Slice | Needs | Rules |
|-----:|-------|-------|-------|
| 175 | **P5a** activation-choice hook | Fire dormant `Activation_OnActivationStart` + a "pick one effect until end of activation" resolver (CLI + GUI + AI). **FORK:** new request type vs reuse `StringSelectionRequest`. | Versatile Attack (56), Versatile Reach Aura (56), Watchborn (42), Versatile Defense Aura (21) |
| 93 | **P22** new deploy timings | `deferDeployment` variants beyond Scout/Ambush; "deploy anywhere >3in from enemy"; deploy-round state for round-of-arrival bonuses. | Delayed Action (47), Teleport (15), Rapid Blink (8), Ambush Beacon (6), Rapid Ambush (4), Teleport Aura (4), Ambushing Piercing Shot (4), Surprise Attack (2), Rapid Blink Boost Aura (2), Infiltrate Aura (1) |
| 86 | **New** reposition-at-activation | Place a unit's models within a rolled D3/D3+1in of their current position at activation start. Needs the P5a hook + a placement resolver + the dice invariant. | Wolfborn (60), Bounding (22), Bounding Aura (4) |
| 66 | **P5b** round-start hook | Fire dormant `Round_OnRoundStart`; a "roll to clear Shaken" outcome effect. Cheapest of the dormant hooks. | Steadfast Aura (28), Battleborn (26), Honor Code (9), Steadfast (3) |
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
