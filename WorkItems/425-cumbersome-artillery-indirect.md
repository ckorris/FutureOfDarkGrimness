# 425 — Cumbersome (new core); Artillery and Indirect 3.6; Quick Throw

**Status**: done 2026-10-30 (owner review asked on D1, D3, D4, D5; GUI hand-verify pending)
**Related**: #415, #360 (Indirect/Mobile Artillery move penalty in AI), #290 (advance-and-shoot gate), #436 (Mobile Artillery / Re-Position Artillery dropped)
**Branch**: `feature/opr-3.6`. Model/effort: Opus 5.5 / medium.

## Goal

Core Rules v3.6.0:
- Cumbersome (new): "May only use Hold or Advance actions, but can't shoot when moving." Carried by artillery
  weapons/units (18 GDF + 19 AoF books).
- Indirect: "When using Hold actions and shooting, may target enemies that are not in line of sight as if in
  line of sight, and ignores Cover from Sight Blockers." (3.5: -1 to hit after moving, LoS/cover bypass
  unconditional.)
- Artillery: "When this model uses Hold actions and shoots at enemies over 12" away, it gets +1 to hit rolls.
  If most models in this unit have this rule, as long as they haven't moved during this round, enemy units
  shooting at them from over 12" away get -1 to hit rolls."
- Book rule Quick Throw: "This model may always target enemies that are not in line of sight when shooting with
  Indirect weapons (instead of only when using Hold actions)."
- Dropped in 3.6: Mobile Artillery, Re-Position Artillery (#436 removes them).

"Done" = a Cumbersome unit's action menu offers Hold and Advance only, and Advance is move-only (no shoot
after move; "can't shoot when moving" - a Cumbersome unit that Advances 0" is still a Hold, record the
ruling); Indirect's LoS bypass and Sight-Blocker-cover bypass apply only on Hold (move-then-shoot needs LoS
and gets cover normally), the -1 after moving is gone; Artillery's defensive -1 is gated on most models +
unmoved this round; Quick Throw lifts Indirect's Hold condition.

## What changes

1. Engine: new `CoreRuleCatalog.Cumbersome` (action gating via the capability query the Immobile rule uses;
   shoot-after-move denied); Indirect re-authored with an `IsHoldAction` condition on both facets;
   Artillery's defensive facet re-gated (most models via #428's condition + unmoved token); `Quick Throw`
   supplement definition as an Indirect-condition override (simplest: a weapon/unit marker read by the
   Indirect condition).
2. Resolvers: Choose Action menu greys Rush/Charge for Cumbersome with the reason text; the shooting target
   list hides out-of-LoS targets for a moved Indirect unit; CLI equivalents.
3. AI: the Indirect/Mobile Artillery pricing in #360 becomes Hold-vs-Advance pricing for Indirect and
   Cumbersome.
4. Tests: Cumbersome action set; Cumbersome Advance cannot shoot; Indirect on Hold targets out of LoS; after
   Advance it cannot; Artillery -1 only unmoved and most-models; Quick Throw lifts the Hold gate.

## Acceptance

Tests green; smoke exit 0; hand-verify (Chris): an artillery unit's action menu and a moved Indirect unit's
target list.

## Code anchors (survey 2026-10-29)

- `CoreRuleCatalog.Artillery` / `.Indirect` / `.Immobile`, `ChooseActionStage` action gating, `ShootAfterRushRules`,
  `SightRuleQueries`, LoS/cover evaluation in the shooting stage. See the appended survey rows below.

### Survey rows (code survey 2026-10-29)

Findings that shape the item: Indirect today ignores ALL cover (3.6: only cover from Sight Blockers) and
has no Hold condition; Immobile's Hold-only gating is the precedent for Cumbersome; Quick Readjustment
(dropped by 3.6) has tests that must go with #436.

- Indirect `CoreRuleCatalog.cs:140-159` (:143-146 -1 when `AfterMoving && !IsMelee`; :147-150 IgnoreLineOfSight
  Always; :151-154 IgnoreCover Always). `Condition.AfterMoving` `Condition.cs:405-408`. Queries
  `Rules/Dispatch/SightRuleQueries.cs` (IgnoresCover :35, LineOfSightIgnoreSource :45, IgnoresTerrain :60).
  Sight-blocker flag `TableState/Zones/Terrain/ETerrainType.cs:12` (`Blocking`). AI `CombatMath.cs:21/:119`,
  `TacticianWeights.cs:220`.
- Artillery `CoreRuleCatalog.cs:~115-125` (Subject part gated all-models at :122). Immobile :2280.
  Action gating sites in `ChooseActionStage.cs` (:656 charge, :708 Pass, :825 shoot-before-move, :849
  move-and-shoot cap - the Aircraft pattern to mirror for Cumbersome).
- Tests: `IndirectLineOfSightRuleIntegrationTests.cs`, `HitRollRuleIntegrationTests.cs`
  (IndirectAttacker_AfterMoving_RaisesHitThreshold), `SpecialRuleTests.cs:598`,
  `QuickReadjustmentRuleIntegrationTests.cs`, `WeaponScopedCounterAndSightTests.cs`.

## Notes

- 2026-10-30: done. Engine commits on `feature/opr-3.6`: `87e03f4e` (Cumbersome), `e4616efa` (Indirect 3.6),
  `fa46b156` (Artillery 3.6), `04e01d1b` (Quick Throw), `b4630f8d` (AI Hold-vs-Advance pricing pins); one
  superproject commit bumps the pointer with the app side (CLI/GUI movement panels, GUI movement overlay, the
  supplement entries, shipped-data tests, the `Scenarios/425-artillery-hold.json` hand-verify probe, docs, ledger).
  Engine suite 3771 -> 3805 passed (1 skipped); app suite 3082 -> 3086; `--validate-rules` 293 -> 294 definitions
  OK; `--rule-coverage` unchanged (Dead 36, the two Counter-Attack names); headless smoke exit 0. Every new test was
  broken on purpose and seen failing (sabotage of each gate, cap and condition), then restored.
- 2026-10-30: census (3.6.0 snapshots): Cumbersome is a BOOK `specialRules` entry in 37 books (18 GDF + 19 AoF),
  never in the common-rules JSON; 37 of 39 Artillery units carry it (HDF Support Vehicle and Robot Legions Support
  Platform do not); 6 of 58 Indirect-weapon units are Cumbersome. Quick Throw: Human Inquisition, Ratmen Clans (GDF),
  Dragon Empire, Ratmen, Duchies of Vinci, Dwarves (AoF; the last two grant it to one model by upgrade), identical
  text everywhere. "Indirect when Shooting Aura" and "Indirect Mark" are still 3.6.0 book rules.
- 2026-10-29: filed with the plan.

## Decisions

- **D1 (OWNER REVIEW) - an Advance of 0" is a move.** The menu has no Hold entry: Hold is "Shoot (or Pass)
  without moving", and a Move whose path is 0" ("Skip all", Done with no waypoint) still spends the move action
  (`HasMoved`, #333). Ruling: the Advance action IS "moving", so a Cumbersome unit that took it may not shoot; a
  player who wants to shoot picks Shoot (Hold). The same fact drives the other Hold gates - Indirect's two facets and
  Artillery's +1 read `Not(AfterMoving)` - so a 0" Advance forfeits those too. (The filing text above said the
  opposite - "a Cumbersome unit that Advances 0" is still a Hold"; the session brief recommended this reading and it
  is the one implemented.) Artillery's DEFENSIVE clause reads physical movement instead (D4), which a 0" move does not
  stamp. Alternative: treat a 0" Move as a Hold everywhere (needs a "moved 0" exception in `GetCanShoot` and in the
  `AttackerMoved` the shoot stage threads).
- **D2 - how "Hold or Advance only" lands in a Move/Charge/Shoot/Pass menu.** Charge is greyed with
  "Cumbersome - may only use Hold or Advance actions."; Move stays and carries the subtext "Advance only
  (Cumbersome) - no Rush, and can't shoot after moving."; Rush is enforced as a budget, not a menu entry: an
  allowed-action set without Rush caps the Rush (and, without Charge, the charge-length approach) at the Advance in
  `MovementActionContext`, its per-model budgets and `MovementRuleQueries` (`ActionRestrictionQueries.CapBudget`), so
  the request, the authoritative re-validation and the AI agree. Reasons now name the restricting rule, so Immobile's
  greyed reason changed from "Immobile." to "Immobile - may only use Hold actions." (two #427 pins updated).
- **D3 (OWNER REVIEW) - cover from Sight Blockers.** 3.6 cover: "most models fully inside Cover terrain or behind
  Sight Blockers". The engine's base cover model predates that: on an ordinary shot a `Blocking` piece blocks the line
  (the model is out of sight) and never grants cover. So "Indirect ignores Cover from Sight Blockers" needed the
  distinction only where a hidden model can be shot at all - a shot that ignores line of sight - and that is where it
  was added (`ESightBlockerCover`, `CoverMajority`): Indirect on a Hold sees through blockers and counts only Cover
  terrain (including a wood behind the wall, which an ordinary sight test would have masked); Quick Throw after moving
  counts the blocker as cover; Indirect no longer drops Cover-terrain cover (3.5 dropped all). NOT done (pre-existing
  base-rule gap, recorded in `docs/EngineNotes.md`): cover for models behind Sight Blockers on an ordinary in-LoS shot.
  Options: (a) leave it (current); (b) count a defender model hidden from EVERY living shooter as in cover; (c) count
  any blocked line as cover, matching the generous any-shooter fold Cover terrain already uses. Recommendation: (b), as
  its own work item (needs a fresh number from `origin/master`) - it changes cover in ordinary play, the AI's cover
  habit and the targeting previews, so it is not a #425 side effect.
- **D4 (OWNER REVIEW) - "haven't moved during this round".** Read as "no `MovedThisRound` token" - the #197 stamp
  `MovementStage` writes when a move of more than 0" resolves, cleared at round end. Charge moves, pile-in,
  consolidation, Teleport, disembarking and rule-triggered moves (e.g. a spell moving the unit) do not stamp it, so
  they do not switch the -1 off (pre-existing). Impact is small: 37 of 39 Artillery units are Cumbersome and cannot
  charge. Alternative: stamp on every position change of the unit's own models.
- **D5 (OWNER REVIEW) - Quick Readjustment ships inert.** Its 3.5 +1 (on an Indirect weapon after moving) cancelled
  Indirect's -1; with the -1 gone it would be a free +1. The supplement entry is now `passive: []` (allowlisted in
  `RuleSupplementLintTests`); `QuickReadjustmentRuleIntegrationTests` pins why. Legacy: the four bundled 3.5 books
  that name it embed the OLD +1 until #438 re-bakes them from 3.6 books (none of which names it), and saved armies
  keep theirs. #436 deletes the entry (hand-off written there).
- **D6 - Quick Throw's shape.** Weapon-scoped, one passive entry at `Shooting_OnSaveRollModifier`:
  `weaponHasRule "Indirect"` -> `ignoreLineOfSight`, no move condition - the text lifts the Hold gate for targeting
  only, so Indirect's Sight-Blocker-cover bypass stays Hold-only. Defined once, in `GdfRuleSupplement.json`: AoF books
  bake against GDF + AoF merged (`RuleSupplementSet`) and the text is identical, so no `AofRuleSupplement.json` entry
  (pinned by `QuickThrowShippedDataTests.QuickThrow_ResolvesForAoFBakes_FromTheMergedSupplements`).
- **D7 - the supplement's "Indirect when shooting"** (granted by "Indirect when Shooting Aura", still a 3.6.0 book
  rule) is re-authored to the 3.6 Indirect shape so the #438 re-bake embeds 3.6 behaviour. The bundled 3.5 books
  embed the old copy until then.
- **D8 - Artillery's distance and Hold.** Both facets use `DistanceGreaterThan(12)` on the hit context - the same
  closest-models shooting distance #432's `attackedFromOver` reads there, and melee (within 3") can never pass it. The
  engine's offensive facet was ungated beyond 9"; it now needs the Hold (`Not(AfterMoving)`).
- **D9 - AI scope (parity only).** The Tactician never generates a charge or charge-approach macro for a unit that
  may not Charge, never credits a volley to an Advance plan of a unit that cannot shoot after moving, prices only the
  Hold volley in its urgency term, and exempts an Indirect weapon from a cut lane only on a Hold (`CombatMath` reads
  the candidate's `AttackerMoved`). Enemy-side modelling and the Artillery forfeit price go to #442 (hand-off there).

## Ledger (facet -> code anchor -> test)

| Facet | Code anchor | Test |
|---|---|---|
| Cumbersome core definition, 3.6 text | `CoreRuleCatalog.Cumbersome` | `CumbersomeRuleIntegrationTests.Cumbersome_CarriesTheCore360Wording`, `..._RestrictsToHoldOrAdvance_AndForbidsShootingAfterMoving` |
| Charge greyed with the reason; Move kept with subtext | `ChooseActionStage.Enter` (`ActionRestrictionQueries.AllowedActions`, `RestrictedActionReason`) | `ChooseAction_CumbersomeUnit_KeepsMoveAndShoot_GreysChargeWithTheReason`, `ChooseAction_Control_WithoutCumbersome_OffersCharge` |
| No Rush / no charge approach (budget cap) | `ActionRestrictionQueries.CapBudget`; `MovementActionContext`; `MovementRuleQueries.EffectiveMaxRushDistance/PerModelMoveBudgets` | `MoveBudget_IsAdvanceOnly_InTheContextAndTheSharedQueries`, `MoveLongerThanTheAdvance_IsRejectedByTheAuthoritativeCheck` |
| Move request names the rule (CLI/GUI panels) | `DefinePathStage`; `DefineMovementPathRequest.AdvanceOnlyRule/NoShootAfterMoveRule`; `DefineMovementPathResolver`; `GuiDefineMovementResolver` | `MoveRequest_CarriesTheAdvanceCap_AndNamesTheRule` |
| Can't shoot when moving (0" too, D1); Hold can | `ChooseActionStage.GetCanShoot`; `ActionRestrictionQueries.ShootingAfterMovingForbiddenBy` | `AfterAnAdvance_CannotShoot`, `AfterAZeroInchAdvance_StillCannotShoot`, `Holding_CanShoot`, `Control_WithoutCumbersome_ShootsAfterAnAdvance` |
| Tactician legality (no charge plans, no Advance volley) | `MacroActionGenerator` (`hasMelee`), `TacticianPlanner.CanShootAfter/ActiveMayCharge` | `Tactician_GeneratesNoChargePlans_AndNoMoveBeyondTheAdvance`, `Tactician_CreditsNoShotToAnAdvance_ButKeepsTheHoldVolley` |
| Indirect 3.6 text; LoS bypass Hold-only | `CoreRuleCatalog.Indirect`; `CoverIgnoreContext(AttackerMoved)`; `SightRuleQueries` (`attackerMoved`); `OcclusionCheckStage` | `IndirectLineOfSightRuleIntegrationTests.Indirect_OnAHold_BehindWall_ShotNotOccluded`, `Indirect_AfterAnAdvance_BehindWall_ShotIsOccluded`, `SightRuleQueries_Indirect_AreHoldGated` |
| Target list / Shoot gate follow the Hold gate | `ChooseRangedAttackStage.BuildWeaponOptions/HasAnyFireableTarget(attackerMoved)`; `GetCanShoot` | `TargetList_OnAHold_OffersTheTargetBehindTheWall_AfterAnAdvance_DoesNot` |
| Ignores Cover from Sight Blockers only (not Cover terrain) | `Effect/RuleOperation.IgnoreSightBlockerCover`; `ESightBlockerCover`; `CoverMajority.Evaluate`; `CoverCheckStage`; per-weapon `HasCover` in `ChooseRangedAttackStage` | `Hold_TargetInTheOpenBehindTheWall_GetsNoCover`, `Hold_TargetInCoverTerrainBehindTheWall_KeepsThatCover`, `Hold_TargetInCoverTerrainInSight_KeepsThatCover`, `AfterAnAdvance_TargetInCoverTerrain_GetsCoverNormally` |
| No -1 after moving (replaces the old penalty test) | `CoreRuleCatalog.Indirect` | `HitRollRuleIntegrationTests.IndirectAttacker_AfterMoving_NoHitPenalty` (replaces `IndirectAttacker_AfterMoving_RaisesHitThreshold`) |
| Movement preview = post-move shot | `WeaponSightProfileBuilder` (`attackerMoved: true`, `IgnoresSightBlockerCover`); GUI overlay | (GUI hand-verify) |
| Artillery +1: Hold and over 12" | `CoreRuleCatalog.Artillery` | `ArtilleryRuleIntegrationTests.Attacker_OnAHold_Over12_GetsPlusOne`, `Attacker_AfterMoving_Over12_GetsNothing`, `Attacker_OnAHold_Within12_GetsNothing` |
| Artillery -1: unmoved, most models, over 12" | `CoreRuleCatalog.Artillery` (Subject seat, `MovedThisRound`) | `Defender_Unmoved_Over12_GivesTheShooterMinusOne`, `Defender_MovedThisRound_GivesNothing`, `Defender_Within12_GivesNothing`, `Defender_MostModelsGate`, `BothSeats_Stack`, `TheDefensiveFacet_ReadsTheMovedThisRoundToken` |
| Quick Throw lifts the Hold gate for LoS, not cover | `GdfRuleSupplement.json` "Quick Throw" | `QuickThrowRuleIntegrationTests.*` (4); `QuickThrowShippedDataTests.QuickThrow_LiftsOnlyTheLineOfSightGate_OnAnIndirectWeapon`, `..._ResolvesForAoFBakes_FromTheMergedSupplements` |
| Supplement "Indirect when shooting" = 3.6 Indirect | `GdfRuleSupplement.json` | `QuickThrowShippedDataTests.IndirectWhenShooting_IsThe36Indirect` |
| Quick Readjustment inert (D5) | `GdfRuleSupplement.json`; `RuleSupplementLintTests` allowlist | `QuickReadjustmentRuleIntegrationTests.*`, `QuickReadjustmentShippedDataTests.QuickReadjustment_ShipsInert_UntilItIsDeleted` |
| AI pricing parity | `CombatMath.EstimateShooting` (LoS exemption on a Hold); `TacticianActivationResolver.Urgency` | `TacticianLineOfSightTests.EstimateShooting_BlockedSight_IndirectAfterMoving_NeedsTheLane`, `TacticianPhantomVolleyTests.Urgency_CumbersomeShooter_PricesOnlyTheHoldVolley` |

## GUI hand-verify (Chris)

Launch `dotnet run --project FdgRaylib/FdgRaylib.csproj -- --scenario Scenarios/425-artillery-hold.json` (no bundled
book carries Cumbersome until #438 re-bakes the 3.6 books, so the probe armies do).

1. **Artillery unit's action menu** (Field Gun, Artillery + Cumbersome): Charge is greyed "Cumbersome - may only use
   Hold or Advance actions." although Adjacent Grunts stand 2.5" away; Move shows the subtext "Advance only
   (Cumbersome) - no Rush, and can't shoot after moving."; Shoot is offered. Shooting the Enemy Battery (Artillery,
   ~24" away, unmoved): the forecast nets the Field Gun's +1 (Hold over 12") against the target's -1 (4+).
2. **Its move panel**: the summary reads "advance up to 6" - no Rush (Cumbersome), no shooting after moving"; no
   post-move fire lines; a moved model's roster row / status reads "advance - cannot shoot (Cumbersome)"; Done with a
   path over 6" is refused. After the move, Shoot is greyed "Cumbersome - can't shoot when moving." (0" move too, D1).
3. **A moved Indirect unit's target list** (Mortar Team): on a Hold the list offers Hidden Grunts (behind the wall, in
   the open) "ignores line of sight (Indirect)" with no cover, and Wooded Grunts (behind the wall, in the wood) WITH
   cover; the Mortar Team shooting the Enemy Battery shows 5+ (Artillery's -1). Re-load, Advance the Mortar Team a
   couple of inches without clearing the wall: neither grunt unit is offered any more.

## Outcome

Done 2026-10-30. Core Rules v3.6.0 Cumbersome, Indirect and Artillery, and the Quick Throw book rule, are
implemented end to end (engine, CLI and GUI panels, Tactician legality and pricing parity) with integration tests
for every facet. Deferred, each recorded: base-rule cover behind Sight Blockers on ordinary shots (D3, owner
review); "moved this round" for non-Move movement (D4); enemy-side AI modelling of Cumbersome and the Artillery
forfeit price (#442); deleting Mobile Artillery / Re-Position Artillery / Quick Readjustment and their tests (#436).
Awaiting Chris's GUI hand-verify (above) and his answers on D1, D3, D4, D5.

## Rules for the agent (plan section 1, restated)

- Branch `feature/opr-3.6` in BOTH repos; check `git branch --show-current` in each before committing.
  Never commit, merge or push `master`. Submodule-first commit cadence; subjects start with `NNN:`.
- 3.6 rule text: `~/Projects/GDF Armies/Rules 3.6.0/` (core PDFs' text extracts, common-rules JSON) and the
  3.6.0 book snapshots. Never commit anything from that folder. No 3.5 fallbacks or version flags.
- Vertical slice: engine change + CLI resolver + GUI resolver/overlay + AI resolver legality + an
  integration test mirroring the nearest `*RuleIntegrationTests` + the `docs/` line if a number changed.
- Verify per CLAUDE.md (engine suite green, full build, headless smoke exit 0) and
  `FdgRaylib --rule-coverage FdgRaylib/Assets/Books` not regressing. Never commit red.
- Keep this file's Notes/Decisions current and flip the row in `docs/opr-3.6-upgrade-plan.md` section 5.
  A design fork stops for Chris's sign-off with options + a recommendation written here.
