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

- [~] 042 — Special rules architecture (Plan B: data-driven `Condition × Effect` records over a named hook surface, with a unit/model token system as the state primitive). Prerequisite for 026–034. **Phase 7 complete (suite 241/0)**: polymorphic `RuleEvaluator` + `RuleValidator`, all passive rules + activated abilities (7c) + cross-unit token cleanup (7g); suppression first-pass (`IgnoreRule`/`SuppressRule`) done. **Phase 8 integration underway (suite 281/0): 15 rules live across 8 sinks via the `SinkOperation<TSink>` pattern** — Stealth/Artillery/Indirect/Reliable (hit), Fast/Very Fast/Slow (movement), Surge/Relentless/Furious (extra-hit), Deadly (wound-mult), Regeneration (wound-ignore), Tough (max-wounds at army-load), Rending (save-mod, hit→save carry), Bane (save-reroll), + Unstoppable (suppressor). **Engine-primitive refactor (Phase 7h) begun — two subsystems now invocable: MOVEMENT (Vanguard, via `MovementExecutor` + the `ExecutableOperation`/`OperationExecutor` imperative-op seam, the mirror of `SinkOperation`) and DEPLOY (Scout + Ambush, via the defer/reserve primitive).** Remaining: attack/reactivate primitives (Reactivate/Strafing/Impact), then morale/casting; JSON loader (**no longer gated — #027 landed 2026-06-11: rules attach at unit or weapon scope, schema is weapon-attachment-capable**). See `WorkItems/042-special-rules-architecture.md` and `042-implementation-checklist.txt` (INTEGRATION PROGRESS cont. 1–30).
- [~] 026 — Unit special rules framework wiring (`UnitData.SpecialRules`, `GetRealSpecialRulesFromArmyList`, `GetMobility`) — depends on 042. **Army-list → #042 rule-registry resolution done** (2026-06-07): `Rules/Dispatch/CoreRuleCatalog.cs` (19 rule defs incl. Vanguard/Scout/Ambush + numeric `Deadly(X)`/`Tough(X)` loaded with args via `DescribeRuleEntry`) + `RuleResolver.TryResolve`; `FDGServer` resolves each `UnitFileEntry.SpecialRules` name and `AttachRuleDefinition`s it (skip+log for unimplemented), replacing the old hardcoded-Stealth `//TEST` hack — declared rules now fire in live headless games. Remaining: the legacy `GetRealSpecialRulesFromArmyList`/`GetMobility` paths (separate `SpecialRule` type).
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
- [ ] 039 — Resolve `GameDataStore.CreateFromTypeMap` `NotImplementedException` / introduce builder — folded into #052 (durable save format needs it)
- [ ] 052 — Save / Load a game in progress: snapshot the `GameDataStore` to a `.fdgsave` file + a new `GameProgressData` component (round/turn/activation state promoted into the store); load drops into a host lobby where saved slots are re-crewed (PlayerID remap), then `FDGServer` resumes mid-round via a new resume path. Save "any time" = rolling snapshot at each activation boundary; restore re-plays the current activation. Mostly submodule work (branch + bump). ([WorkItems/052](WorkItems/052-save-load.md))
- [ ] 053 — (low priority) Refactor state-machine contexts into the directly-serializable source of truth: make `MainPhaseContext` / `SingleRoundContext` (and `TeamPlayerAlternationCursor`) store-backed and JSON-serializable in place — teams keyed by `TeamNumber`, the `IGameContext` service refs `[JsonIgnore]`'d and re-injected on load — so the store *is* the save and the separate `GameProgressData` mirror added in #052 can be deleted. Same end result as the mirror but more invasive to the live engine (changes the context types every stage already uses), so deferred for risk. Follow-up to #052.
- [ ] 054 — (low priority) Client-initiated save: let a non-host player trigger a save. The host owns the authoritative `GameDataStore`, so the client would request it and the host produces the `.fdgsave` and sends it back over the network (or the host saves on the client's behalf). Today only the host can save (`CanSaveGame` is false on `LobbyViewModel_Client`). Follow-up to #052.

## Client / renderer

- [ ] 040 — Post-game navigation back to main menu in GUI mode (currently window just stays open)
- [ ] 053 — Sound cues on the presentation beat stream: BUILT (app-side, no engine change) — reusable `AudioManager` (device + cache + headless no-op, repurposable for UI sounds), `PresentationSoundCues.CueFor` beat→cue mapping, `PresentationPlayer.BeatStarted` hook fires cues in lockstep with visuals. Placeholder tone covers every cue until real `.wav` files land in `FdgRaylib/Assets/Sounds/` (drop-in by filename). Held open only until heard by hand. See `WorkItems/053-sound.md`.
- [ ] 052 — Presentation beat stream: engine-owned, paced, semantic event stream (`context.Present(beat)`) so play feels lifelike — gliding movement, projectile→save/hurt→death beats, tumbling dice, stage-change flashes. Free-running (engine self-paces on a wall clock it owns; no renderer ack), inline emission from stages, host-authoritative & replicated, headless degrades to instant + text. App owns the visual model/tweens; engine owns the beats and pacing. Spans the engine submodule + client. See `WorkItems/052-presentation-beat-stream.md`.
- [ ] 049 — Multi-pool terrain selection: lobby picker for which `TerrainLayoutFile` feeds `AutoFromLayout` / `Alternating`. Spun off from #002 — that ships with one hardcoded built-in pool. (Reassigned from 044, whose number was reused for the line-of-sight cluster.)
- [~] 055 — Special-rule attribution in the resolvers (originally tracked as **#052** in the 2026-06-11 commits + code comments, before the merge revealed origin/master had already reused #052 for save/load and the presentation beat stream; renumbered to 055 per the never-reuse rule — the `#052` strings in `SightRuleLabel`/resolver comments and commit messages `b76ec49`/`5768bab` predate the renumber). Make it visible *why* a shot/move behaves differently. **(a) + (b) DONE 2026-06-11.** (a) Movement targeting overlay — names the rule causing each cover/LoS-ignore inline on the per-weapon fire-line labels (e.g. `Huge Gun (Indirect ignores line of sight)`), with overflow handling (anchor-flip + clamp to screen on both axes). (b) Shooting resolver (CLI + GUI) — surfaces the same per-weapon rule info in the weapon list/details panels. Engine addition shipped: `RuleEvaluator.EvaluateAllNamed` (non-logging, pairs each op with its alias-aware origin name) + `SightRuleQueries.CoverIgnoreSource`/`LineOfSightIgnoreSource`; the names ride `WeaponOption` + `WeaponSightProfile`; shared `FdgRaylib.SightRuleLabel` composes the wording (one rule that ignores both → named once). Side-effect: the per-build sight-query log spam is gone (queries are now non-logging). **(c) LATER** — markers on the *units* that carry a relevant rule (e.g. a "Stealth" tag over enemy units with Stealth) so defensive/relational rules are visible at the source. Per-weapon accuracy landed with #027 (2026-06-11): `SightRuleQueries` evaluates the queried weapon's own rules, so the labels are weapon-accurate. Builds on #041/#045.

---

## Awaiting verification

Implemented and merged to master; engine test suite green. Held open only until the behavior is confirmed by hand in the running app — tick and move to `## Done` once verified.

- [ ] 041 — Factor line of sight into movement resolver's ranged-targeting overlay: both the per-enemy-unit weapon list and the per-model fire lines now require LoS (terrain + model-base blockers), with a red block-stub when no model in the unit is visible. ([WorkItems/041](WorkItems/041-movement-resolver-ranged-los.md)) — commit `ec2f552`
- [ ] 045 — Cover indication in targeting overlay and shot UI: fire lines through cover render dashed yellow; shot picker spells out "Cover (+1 Def)". Presentation-only, no engine change. ([WorkItems/045](WorkItems/045-cover-indication.md)) — commit `cc341b0`
- [ ] 046 — `GetFirstBlockingHit` engine API: returns the closest `Blocking` terrain entry point along an (attacker, target) segment so overlays can draw a stub + marker; `IZone.GetFirstSegmentEntry` on circle/rect. 6 new `LineOfSightTests` cases, suite 135/135. ([WorkItems/046](WorkItems/046-los-first-blocking-hit.md)) — commit `d9e60fb`
- [ ] 027 — Weapon-scoped special rules: engine-complete 2026-06-11 (branch `027-weapon-special-rules`, both repos; suite 396/0, headless-verified). Weapons carry #042 `ResolvedRule`s resolved from `WeaponFileEntry.SpecialRules` at army load with `SpecialRuleDefinition.Scope` enforcement (misattached rules warn + skip); dispatch is per-weapon through the fire pipeline + defender melee weapons (Counter) + `SightRuleQueries`; legacy `ISpecialRule_Weapon` deleted. **JSON loader / army creator no longer gated.** Verify in GUI: per-weapon rule labels in the shot picker / movement targeting overlay (the test army's Heavy Rifle carries Surge + Blast(3), Fists carry Counter, Infiltrators' Rifle carries Takedown — labels should show on those weapons only), and a melee charge into Heavy Gunners should show Counter striking first. ([WorkItems/027](WorkItems/027-weapon-special-rules.md))
- [ ] 005 — Scout deployment: set aside during normal deployment, then placed after all others within 12" of the zone (forward deploy). Built on the #042 deploy defer/reserve primitive (`PlaceDeferredUnitsStage` + forward-expanded zone). Headless-verified, unit-tested; try in GUI via a `.fdgarmy` with the Scout rule. (#042 INTEGRATION PROGRESS cont. 13.)
- [ ] 004 — Ambush deployment: kept in reserve, brought on at the owner's choice from round 2+ and placed anywhere >9" from enemies. Built on the same primitive (`StartOfRoundExtraActionStage` arrival + `PlaceObjectsRequest.MinDistanceFromEnemiesInches` honored by CLI/GUI/AI place resolvers; reserves excluded from activation/targeting via `IUnit.GetIsOnBattlefield`). Headless-verified, unit-tested. DEFERRED: "can't seize the round it arrives" objective nuance. (#042 INTEGRATION PROGRESS cont. 14.)

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
