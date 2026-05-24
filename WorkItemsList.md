# Work Items

Numbered, persistent backlog of engineering tasks. Each item is roughly "one Jira ticket" sized — some are umbrellas that will fragment when picked up.

See `WorkItems/README.md` for the per-item file template. Per-item working notes live in `WorkItems/NNN-slug.md`, created when work starts on that item.

Numbers are permanent and never reused. If an item is split, its line stays and points at the new numbers.

---

## Setup & map

- [ ] 002 — Terrain placement workflow (`MapSetupStage` children currently empty) — in progress on `TerrainPlacement` ([WorkItems/002](WorkItems/002-terrain-placement.md))
- [ ] 003 — Force organization validation (optional rule: hero/unit/copy/cost caps)

## Deployment

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
- [ ] 012 — Decouple Advance distance from total move distance in `PathTemplate` (currently hardcoded half)

## Shooting

- [ ] 013 — Weapon-group target selection (up to 2 targets per unit's shoot action)
- [ ] 014 — Fix `RangedContext` `NotImplementedException` paths (`BeginNewAttack`, `SetAttackWeapon`, `ConsumeAttackIntoContext`)
- [ ] 015 — Attack-count modifiers in shooting flow (`RollToHitStage` TODO)
- [ ] 016 — Hit→wound effect propagation (`DetermineSaveRollsNeededStage` TODO)

## Melee

- [ ] 017 — In-range attacker/defender determination (2" horizontal, 4" vertical) — replace current "everyone fights" behavior
- [ ] 018 — Pile In move (currently no-op)
- [ ] 019 — Consolidation moves after melee resolution (3" if one destroyed, 1" back if neither)
- [ ] 020 — Fatigue: per-unit/per-round flag — hit on unmodified 6s after first melee attack this round. May not need to be a stage; current `ApplyFatigueStage` may be deletable.
- [ ] 021 — Morale roll modifiers + Fear/Fearless effects in `DetermineMeleeWinnerStage` and `RollForMoraleStage`
- [ ] 022 — Vertical melee range handling (`ChooseMeleeDefenderStage` TODO)

## Wound assignment

- [ ] 023 — Tough wound-priority (continue wounding same Tough model until killed; heroes last)
- [ ] 024 — Validate wound splits in `AssignWoundsResults` (currently allows illegal splits)
- [ ] 025 — Fix or remove `AssignWoundsResults.AutoFill` bug (`modelWoundsRemaining` always 0)

## Special rules — framework

- [ ] 026 — Unit special rules framework: `UnitData.SpecialRules`, `GetRealSpecialRulesFromArmyList`, `GetMobility` currently return defaults
- [ ] 027 — Weapon special rules framework (`IWeapon.cs` TODOs)
- [ ] 028 — Deadly weapon priority (resolve first, wounds don't carry across models)

## Special rules — implementations

These are umbrellas; will fragment per-rule when picked up.

- [ ] 029 — Movement-modifier rules: Fast, Slow, VeryFast, Immobile, Strider, Aircraft, Flying
- [ ] 030 — Combat-modifier rules: Furious, Impact, Counter, Thrust, Surge, Relentless
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
- [ ] 041 — Factor line of sight into movement resolver's ranged-targeting overlay (`GuiDefineMovementResolver.DrawRangedTargeting` currently checks range only)
- [ ] 044 — Multi-pool terrain selection: lobby picker for which `TerrainLayoutFile` feeds `AutoFromLayout` / `Alternating`. Spun off from #002 — that ships with one hardcoded built-in pool.
- [ ] 045 — Terrain rotation: angle field on `RectangularZone` (or new `RotatedRectZone` shape) + GUI resolver R-key rotate. Threads through movement / LoS / overlap / save-load. Spun off from #002.

## Movement

- [ ] 046 — Movement validation ignores model base radius for terrain footprints. `MovementUtilities.ValidateMovingThroughImpassibleTerrain` (and the difficult/dangerous variants) test a zero-width center-to-center line against terrain footprints, so a model can park with its center just outside an impassable shape while its base overlaps it. Fix: inflate the terrain footprint by the model's `BaseRadiusInches` (Minkowski expansion) or use swept-disc distance, in `MovementUtilities`. Resolver layer needs no changes. Pre-existing — surfaced more by #002's richer terrain.

---

## Done

- [x] 001 — D3+2 objective placement: interactive alternating-team placement w/ validator + AI strategy + debug auto-place toggle ([WorkItems/001](WorkItems/001-objective-placement.md))
- [x] 043 — Filter dead models out of `IUnit.AllWeapons` so dead models no longer contribute weapons to attack/strike-back/shoot lists or the tooltip readout
