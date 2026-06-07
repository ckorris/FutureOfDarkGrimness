# Work Items

Numbered, persistent backlog of engineering tasks. Each item is roughly "one Jira ticket" sized — some are umbrellas that will fragment when picked up.

See `WorkItems/README.md` for the per-item file template. Per-item working notes live in `WorkItems/NNN-slug.md`, created when work starts on that item.

Numbers are permanent and never reused. If an item is split, its line stays and points at the new numbers.

> **2026-06-03 — reconciliation.** This index had drifted out of sync with the `WorkItems/NNN-*.md` detail files and git history. Numbers **044/045/046** had each been reused across two parallel efforts (a terrain/deployment effort and a line-of-sight effort), violating the never-reuse rule. Resolved by treating the on-disk detail files + merged commits as authoritative: **044/045/046 now mean the line-of-sight cluster** (matching `WorkItems/044-046-*.md`). The two terrain tasks that had been squatting on 044 and 046 were reassigned fresh permanent numbers **049** and **050**. Work item **012** (merged: engine `a967fa1`, GUI `3a6f189`) and **044** (LoS ally-exclusion, merged `8701abf`) were complete but never checked off — fixed. Terrain rotation, formerly listed as its own #045, is folded into the #002 entry where it actually shipped. Items **041 / 045 / 046** are implemented and on master but parked in *Awaiting verification* until manually eyeballed in the running app.

---

## Setup & map

- [ ] 003 — Force organization validation (optional rule: hero/unit/copy/cost caps)

## Deployment

- [ ] 048 — Block deployment of models into impassible terrain (auto-placement and GUI both need intersection check; observed: AI placed model inside building flush against deployment zone edge) ([WorkItems/048](WorkItems/048-deployment-into-impassible.md))
- [ ] 004 — Ambush deployment between rounds (set-aside + alternating placement at start of rounds 2+)
- [ ] 005 — Scout deployment after main deployment (alternating, within 12" of zone)
- [ ] 006 — Hero joins unit + takes morale on behalf of unit
- [ ] 007 — Resolve `DeployAllUnitsStage.Enter` `NotImplementedException` and "actually move the models" TODO

## Activation flow

- [ ] 008 — Shaken unit activation behavior (idle, can't seize/contest, clears at end of activation)
- [ ] 009 — General end-of-activation morale test (half-size trigger outside melee)
- [ ] 010 — Custom actions branch in `ChooseActionStage` (currently hardcoded `false`)

## Movement

- [ ] 011 — `MovementUtilities.ValidateMovingThroughEnemyUnits` (currently empty)
- [ ] 050 — Movement validation ignores model base radius for terrain footprints. `MovementUtilities.ValidateMovingThroughImpassibleTerrain` (and the difficult/dangerous variants) test a zero-width center-to-center line against terrain footprints, so a model can park with its center just outside an impassable shape while its base overlaps it. Fix: inflate the terrain footprint by the model's `BaseRadiusInches` (Minkowski expansion) or use swept-disc distance, in `MovementUtilities`. Resolver layer needs no changes. Pre-existing — surfaced more by #002's richer terrain. (Reassigned from 046, whose number was reused for the line-of-sight cluster.)

## Shooting

- [ ] 015 — Attack-count modifiers in shooting flow (`RollToHitStage` TODO)
- [ ] 016 — Hit→wound effect propagation (`DetermineSaveRollsNeededStage` TODO)

## Melee

- [ ] 017 — In-range attacker/defender determination (2" horizontal, 4" vertical) — replace current "everyone fights" behavior
- [ ] 020 — Fatigue: per-unit/per-round flag — hit on unmodified 6s after first melee attack this round. May not need to be a stage; current `ApplyFatigueStage` may be deletable.
- [ ] 021 — Morale roll modifiers + Fear/Fearless effects in `DetermineMeleeWinnerStage` and `RollForMoraleStage`
- [ ] 022 — Vertical melee range handling (`ChooseMeleeDefenderStage` TODO)

## Wound assignment

- [ ] 023 — Tough wound-priority (continue wounding same Tough model until killed; heroes last)
- [ ] 024 — Validate wound splits in `AssignWoundsResults` (currently allows illegal splits)
- [ ] 025 — Fix or remove `AssignWoundsResults.AutoFill` bug (`modelWoundsRemaining` always 0)

## Special rules — framework

- [~] 042 — Special rules architecture (Plan B: data-driven `Condition × Effect` records over a named hook surface, with a unit/model token system as the state primitive). Prerequisite for 026–034. **Phase 7 passive dispatch complete** (polymorphic `RuleEvaluator` + `RuleValidator`; every passively-dispatchable rule green, suite 215/8 → 231/8 post-merge). Remaining: 7 activated-ability tests (Phase 7c), 1 token-clear, behavior-level execution (Phase 8), JSON loader. See `WorkItems/042-special-rules-architecture.md`.
- [~] 026 — Unit special rules framework wiring (`UnitData.SpecialRules`, `GetRealSpecialRulesFromArmyList`, `GetMobility`) — depends on 042. **Army-list → #042 rule-registry resolution done** (2026-06-07): `Rules/Dispatch/CoreRuleCatalog.cs` (9 live core rules incl. Very Fast) + `RuleResolver.TryResolve`; `FDGServer` resolves each `UnitFileEntry.SpecialRules` name and `AttachRuleDefinition`s it (skip+log for unimplemented), replacing the old hardcoded-Stealth `//TEST` hack — declared rules now fire in live headless games. Remaining: the legacy `GetRealSpecialRulesFromArmyList`/`GetMobility` paths (separate `SpecialRule` type) and numeric-arg core rules (not yet in catalog).
- [ ] 027 — Weapon special rules framework (`IWeapon.cs` TODOs) — depends on 042
- [ ] 028 — Deadly weapon priority (resolve first, wounds don't carry across models)

## Special rules — implementations

These are umbrellas; will fragment per-rule when picked up.

- [ ] 029 — Movement-modifier rules: Fast, Slow, VeryFast, Immobile, Strider, Aircraft, Flying
- [ ] 030 — Combat-modifier rules: Furious, Impact, Counter, Thrust, Surge, Relentless
- [ ] 051 — Furious charge gate: extra-hits-on-6 should fire only when the bearer is in melee **AND charging**. The combat-kind (melee) gate shipped with the #042 extra-hit slice; the "charging" condition was deferred — it needs charge/action state threaded into the hit-roll-complete context (same shape as the `AttackerMoved` threading). Until then Furious over-applies to *any* melee attack, not just charges. Hold until the melee subsystem (#017/#020) is fleshed out — charge-precision on a stubby melee engine is premature. (Spun off from the #042 extra-hit slice, 2026-06-07.)
- [ ] 031 — Defense/unit rules: Tough, Regeneration, Stealth, Fear, Fearless, Hero, Scout, Ambush
- [ ] 032 — Weapon rules: AP, Rending, Blast, Reliable, Indirect, Takedown, Limited, Unstoppable, Bane

## Casting

- [ ] 033 — Caster(X) subsystem: spell tokens per round, casting attempts (4+), friendly Caster ±1 assist within 18"
- [ ] 034 — Spell content (initial set per faction)

## Transport

- [ ] 035 — Transport(X) system: embark/disembark via move actions, deploy with units inside, dangerous terrain test on destruction

## Networking & infrastructure

- [ ] 036 — Server readiness handshake (`FDGServer.cs:148` TODO — wait for all clients ready)
- [ ] 037 — Replace non-concurrent collections in `FDGHost` (`FDGHost.cs:75, :130` TODOs)
- [ ] 038 — Resolve `LobbyViewModel_Host` `NotImplementedException` paths (`:288, :400`)
- [ ] 039 — Resolve `GameDataStore.CreateFromTypeMap` `NotImplementedException` / introduce builder

## Client / renderer

- [ ] 040 — Post-game navigation back to main menu in GUI mode (currently window just stays open)
- [ ] 049 — Multi-pool terrain selection: lobby picker for which `TerrainLayoutFile` feeds `AutoFromLayout` / `Alternating`. Spun off from #002 — that ships with one hardcoded built-in pool. (Reassigned from 044, whose number was reused for the line-of-sight cluster.)

---

## Awaiting verification

Implemented and merged to master; engine test suite green. Held open only until the behavior is confirmed by hand in the running app — tick and move to `## Done` once verified.

- [ ] 041 — Factor line of sight into movement resolver's ranged-targeting overlay: both the per-enemy-unit weapon list and the per-model fire lines now require LoS (terrain + model-base blockers), with a red block-stub when no model in the unit is visible. ([WorkItems/041](WorkItems/041-movement-resolver-ranged-los.md)) — commit `ec2f552`
- [ ] 045 — Cover indication in targeting overlay and shot UI: fire lines through cover render dashed yellow; shot picker spells out "Cover (+1 Def)". Presentation-only, no engine change. ([WorkItems/045](WorkItems/045-cover-indication.md)) — commit `cc341b0`
- [ ] 046 — `GetFirstBlockingHit` engine API: returns the closest `Blocking` terrain entry point along an (attacker, target) segment so overlays can draw a stub + marker; `IZone.GetFirstSegmentEntry` on circle/rect. 6 new `LineOfSightTests` cases, suite 135/135. ([WorkItems/046](WorkItems/046-los-first-blocking-hit.md)) — commit `d9e60fb`

---

## Done

- [x] 001 — D3+2 objective placement: interactive alternating-team placement w/ validator + AI strategy + debug auto-place toggle ([WorkItems/001](WorkItems/001-objective-placement.md))
- [x] 012 — Decouple Advance / Rush / Charge distances: engine splits `MaxRushDistance` from `MaxChargeDistance`, `PathTemplate` carries all three explicitly (no more hardcoded half), `ValidatePaths` gains a "beyond Rush ⇒ a model must end in melee" check + Pass-gating; GUI movement resolver gains a three-band (Advance/Rush/Charge) ring preview + Done gating. Tests added (`ChargeReachValidationTests`, `ChooseActionPassDisableTests`). Engine `a967fa1`, GUI `3a6f189`.
- [x] 013 — Weapon-group target selection (up to 2 targets per shoot action): already implemented via `GameWideConstants.MAX_TARGETED_UNITS_PER_SHOOT_ACTION` + `attackedDefenderRefs` tracking in `ChooseRangedAttackStage`; item was stale
- [x] 014 — `RangedContext` NIE paths: file was dead code (entire body in a `/* ... */` block); actual ranged flow uses `CombatActionContext`. File deleted.
- [x] 018 — Pile In move: defender models not already in BTB step up to 3" toward nearest charging model, with impassible-terrain and strict coherency fallbacks ([WorkItems/018](WorkItems/018-pile-in.md))
- [x] 043 — Filter dead models out of `IUnit.AllWeapons` so dead models no longer contribute weapons to attack/strike-back/shoot lists or the tooltip readout
- [x] 019 — Consolidation moves after melee resolution: 3" Wipeout / 1" Disengage with per-model GUI path-builder, AI resolver, table-bounds clamp, and validation against terrain + cohesion + cap ([WorkItems/019](WorkItems/019-consolidation-moves.md))
- [x] 044 — Allied/same-team models don't block line of sight: `LineOfSightUtilities.BuildModelBlockers` now excludes every model on the attacker's team (via `tableState.Teams`) plus the defender unit's models, falling back to attacker-player-only when no team is registered. New `ModelBlockerTests` cases (ally exclusion, third-party enemy still blocks). ([WorkItems/044](WorkItems/044-los-ally-exclusion.md)) — commit `8701abf`
- [x] 002 — Terrain placement workflow: three-mode lobby setting (AutoFromLayout / Alternating / LoadFromFile), AI + human + CLI resolvers, `CompositeZone` for L-shapes, `RotatedZoneWrapper` + SAT for 45° rotation, GUI thumbnails + R-key rotate. (Includes terrain rotation, formerly tracked separately as #045.) ([WorkItems/002](WorkItems/002-terrain-placement.md))
- [x] 047 — Deployment zone selection: draw labelled zones on the canvas, allow clicking zones directly, synchronise hover between dialog and table, and renumber in reading order ([WorkItems/047](WorkItems/047-deployment-zone-labels.md))
