# 371 — Shooting mode: Declare First / One At A Time

**Status**: implemented, awaiting GUI hand-verify
**Related**: #319 (hold fire / the two exits), #340 (one firing per weapon choice), #028/#314 (resolve-first
weapons), #276 (eligible-copy trim), #157 (the superseded burst split)

## Goal
A host lobby setting for WHEN a shooting unit commits to its targets. **One At A Time** (default, and
what the game has always done): fire a weapon, see what it killed, aim the next one. **Declare First**:
aim every weapon before any dice are rolled. Done = the setting rides the lobby, the wire, the config
and the save, and the shoot loop honours it in both front ends and for both AI profiles.

## Notes

- 2026-08-11: Implemented. Signed off with Chris on both forks before building (below).
  - `GameSettings.ShootingMode` + `EShootingMode`, `OneAtATime` declared first so `default(GameSettings)`
    and every pre-#371 save resolve to the old behaviour. NOT resume-overridable - it is a rules setting.
  - `ICombatActionContext`: the pending-attack queue entries now carry the unit the attack was DECLARED
    against (`SetAttackWeaponAtTarget`), plus `TryPeekPendingAttack` / `DropNextPendingAttack`.
    `ConsumeAttackIntoContext` re-points `DefendingUnit` at the dequeued entry's target.
  - `ChooseRangedAttackStage`: `OfferWeapons` returns `EOfferOutcome` (Routed / OfferAgain) instead of a
    hold-fire bool; in Declare First a CHOICE also returns OfferAgain. Re-entry with a queue drains it
    via `ResolveNextDeclaration` without issuing a request.
  - `DetermineCanKeepShootingStage`: ends the action on an empty pool **and** an empty queue.
  - Lobby row + `ShootingModeLabel`, `ILobbyViewModel`/host/client plumbing, `HostGameSettings`.
- 2026-08-11: Tests - 6 new in `ChooseRangedAttackStageTests` (declaration count, per-declaration
  targets, lost shots, surviving target, the keep-shooting gate, and a One At A Time control). Engine
  2954 green, app 1171 green.
- 2026-08-11: **Verified end to end headlessly** by temporarily defaulting to DeclareFirst and running a
  full 4-round game with two real multi-weapon armies (Battle Brothers vs Orks). A Battle Brothers unit
  declared Fusion-Mod, Heavy Fusion Rifle, 4x Heavy Rifle and Master Plasma Pistol with no dice between
  them, then fired all four in declaration order; the "declared target is gone - those shots are lost"
  branch fired once in that game. Exit 0. The temporary default was reverted.
- 2026-08-11: **Five existing tests were corrected, not worked around.** They re-entered the stage twice
  to mean "a weapon has fired" without modelling FireStage's consume, so a declaration was still queued.
  They now consume between entries, which the #340 Takedown tests in the same file already did.

## Decisions

- **Fork 1 (signed off): queue behind the existing panel, not a new combined dialog.** Declare First
  reuses `ChooseRangedAttackRequest` verbatim and simply asks again, so both GUI and CLI resolvers and
  both AI profiles work unchanged - no new request type, no new wire format, no new AI resolvers. The
  cost is that the player sees the same panel once per weapon rather than one declaration screen.
- **Fork 2 (signed off): shots at a destroyed target are LOST, not re-aimed.** Committing before you
  know the outcome is the entire point of the mode; re-prompting would hand back exactly the casualty
  information it exists to withhold. `ResolveNextDeclaration` discards such a declaration with a log
  line, and the weapon stays spent (a Limited one is already burnt - it was committed at declaration).
- **The target rides the queue entry, not `DefendingUnit`.** That single field cannot describe several
  declarations aimed at different units. `ConsumeAttackIntoContext` re-points it as it dequeues, which
  is also what keeps `DefenderRemainingWoundsAtStart` a per-attack snapshot in BOTH modes. Melee queues
  no target and is untouched.
- **Resolve-first weapons batch the declaration, and that is correct.** Deadly/Takedown gating reads the
  AVAILABLE pool, so under Declare First "must fire Deadly weapons first" becomes "must DECLARE them
  first" - and since the queue fires in declaration order they still resolve before the ordinary
  weapons, which is what the rule is for. Where a Deadly weapon leaves nothing else declarable, the
  action naturally splits into two declare-then-resolve batches.
- **Back stays forbidden once anything is declared.** A declaration marks the weapon used and spends a
  Limited weapon, so it is no more un-doable than a shot. "Done" therefore means "stop declaring and
  roll what I have" - checked before the fired-something test, which cannot tell the two apart.
- **The drain is gated on the QUEUE, not on the mode flag.** One At A Time never leaves a queue behind
  (FireStage consumes it), so the check is equivalent - but reading the queue means a stray pending
  attack can never be double-offered, and it is what caught the five stale tests.

## Deferred / not covered

- No automated test drives the whole `ShootStage` graph through a Declare First re-entry loop; the two
  stages are tested individually and the loop was verified by the headless run above. There is no
  ShootStage-level harness in the suite to hang one on.
- **Declare First has no headless or scenario switch.** `ScenarioSettings` carries Randomness / DiceSeed
  / Background only, so the mode can be reached from the lobby but not from `--scenario`. Deliberately
  out of scope (the ask was a lobby option); worth adding when #167's ledger work next touches the
  scenario schema.
- AI behaviour under Declare First is structurally sound (both resolvers answer per request and never
  see the mode) but is not covered by an AI-specific test.

## Outcome
_Open until GUI hand-verify._
