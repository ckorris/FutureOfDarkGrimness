# 169 — Transport Rout occupant spill

**Status**: in-progress (design phase — fork write-up + sign-off required BEFORE building)
**Related**: #035 (Transport core — slice E built the spillout), #096 (transport visuals), #165 (dangerous-terrain deaths miss `UnitDestructionNotifier` — sibling destruction-path gap), #184 (counter strike sequencing). Audit: `Audit-2026-07-06-New-Subsystems.md` §8 item 18.

## Goal
A Transport that dies by Routing (melee-morale loss at half strength) spills its occupants exactly like one destroyed by shooting/melee swings: each occupant placed fully within 6" of the wreck (interactive placement), un-embarked, Shaken, dangerous-terrain tested. No destruction path may leave occupants permanently embarked off-table ("ghost" state). Preserve the deliberate design decisions from #035: no automatic `EmbarkedIn` token-sweep (spillout ordering stays deterministic), spillout is immediate/mid-resolution ("before it's removed"), and the placement flow stays interactive. "Done" = design signed off, implemented with a `TransportRuleIntegrationTests`-style case covering the Rout path, suite green, ledger updated.

## Notes
- 2026-07-08 (cont.): **Option B implemented.** Engine: new `StateMachine/SpilloutExecutor.cs` (the slice-E flow lifted verbatim from the stage — gate, 6" `CircularZone` placement, `ApplySpilloutEffects`, beat presentation — now `static`, taking `IGameContext` + `IUnit`); `UnitDestructionNotifier.NotifyUnitDestroyed` calls `SpilloutExecutor.SpillIfDestroyedTransport` first (before the token sweep and the killer-null early-return); `SpilloutOccupantsStage` deleted, its insertions removed from `FireStage`/`SwingMeleeWeaponStage` (beat ordering preserved — ApplyWounds presents deaths before it notifies). Tests: `TransportSpilloutTests` retargeted at the executor; new `RoutedTransport_SpillsOutOccupants` (real `RoutWithPresentation` -> notifier -> spill path) + `NotifyUnitDestroyed_WithoutKiller_StillSpills`. Verify: engine suite **1317/0**, app build clean, headless smoke exit 0 (3/3 clean ties on rerun). One smoke run hit the known #159-family "Breaks cohesion" DefinePathStage game-error (unrelated — no transports in the smoke army); noted in #159's ledger since it's awaiting verification with a 0/24 repro claim.
- 2026-07-08: **Engine code-path map complete. The gap is wider than the audit stated.** There are 8 distinct unit-destruction paths (all deal lethal wounds; no removal primitive exists). Spillout (`SpilloutOccupantsStage`, a `CombatStage` self-gated on `IsTransport(defender) && GetIsDead()`) is statically wired after `ApplyWoundsStage` in only 2 of them:
  1. Shooting (`FireStage.cs:44`) — spillout YES
  2. Melee swing (`SwingMeleeWeaponStage.cs:47`) — spillout YES
  3. **Melee Rout** (`AssignMeleeMoralePenaltyStage.cs:42` -> `MoraleUtilities.RoutWithPresentation`) — spillout NO (the #169 bug; calls `NotifyUnitDestroyed` with killer=null)
  4. Impact hits (`ResolveImpactHitsStage.cs`, ApplyWounds@111) — spillout NO (latent)
  5. Spell damage (`ResolveSpellDamageStage.cs`, ApplyWounds@68) — spillout NO (latent)
  6. Strafing (`StrafingStage.cs`, ApplyWounds@134) — spillout NO (latent)
  7. Pre-attack/overwatch (`PreAttackStage.cs`, ApplyWounds@190) — spillout NO (latent)
  8. Dangerous terrain (`MovementExecutor.cs:84,92`) — spillout NO, and never calls `NotifyUnitDestroyed` at all (= #165)

  Paths 1-7 all funnel through `UnitDestructionNotifier.NotifyUnitDestroyed` (`StateMachine/UnitDestructionNotifier.cs:25`) — paths 1,2,4-7 via `ApplyWoundsStage.cs:73` (killer=attacker), path 3 via `MoraleUtilities.cs:215` (killer=null, early-returns before the `Shooting_OnUnitDestroyed` hook). Its token sweep (`TokenClearService.ClearForDestroyedOwner`) deliberately skips `EmbarkedIn` (ManualOnly per `TokenType.cs:64-66` — the no-auto-sweep decision), which is exactly why routed transports strand occupants forever.

  The interactive flow (`SpilloutOccupantsStage.SpillOccupants`, lines 53-97) needs only `IGameContext` + the transport `UnitData` — placement runs via `GameContext.PlayerRequester.RequestDecision`, NOT stage machinery, so it is extractable to a helper callable from any async destruction site. The stage graph is static (`PopulateTransitions`/`TransitionSetBuilder`); there is no runtime stage-enqueue API.
- 2026-07-08: Item opened on branch `169-transport-rout-spillout`. Context gathered: audit §8.18 + §4, #035 Decisions/slice-E history. Design-fork write-up presented to Chris for sign-off before any implementation.

## Decisions
- **Option B — single choke point at `UnitDestructionNotifier` (signed off by Chris, 2026-07-08).** Extract `SpilloutOccupantsStage.SpillOccupants`/`PresentSpilloutRolls` into a helper needing only `IGameContext` + the transport; invoke it inside `NotifyUnitDestroyed` — before the token sweep and before the `killer == null` early-return — so all notifier-reached destruction paths (shooting, melee swing, Rout, impact hits, spells, strafing, overwatch) spill in one seam. The two hard-wired `SpilloutOccupantsStage` insertions (`FireStage`, `SwingMeleeWeaponStage`) become redundant and are removed. `NotifyUnitDestroyed` goes async (2 call sites, both already async contexts). **Rejected:** A (Rout-site patch — fixes 1 of 5 gaps, adds a third parallel insertion site); C (fold #165 in — bundles #165's unresolved kill-attribution ruling; #165 stays separate and becomes nearly free after B since its fix is "route MovementExecutor deaths through the notifier"). Preserved from #035: no `EmbarkedIn` auto-sweep, interactive placement, spillout-before-removal semantics. Sign-off covers the engine-submodule modification.

## Outcome
(open)
