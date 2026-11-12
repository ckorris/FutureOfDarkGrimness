# 381 — AoF Retreating Strike: post-melee move-end strike

**Status**: implemented + tested 2026-08-31 (ruling landed same day, see Decisions) - awaiting GUI hand-verify.
**Related**: #376 (hands this off as its last unresolved name; all recon dates 2026-08-22), #377
(spells may grant it), mirrors the #197 primitive discipline. Engine submodule work expected.

## Goal

The last dead AoF rule name: **Retreating Strike, 14 refs** (the only remaining dead census
entry after #376's five slices). Mechanic, paraphrased: once per round, when the unit ends a
move within 3" of enemy units after having been in melee, its owner picks one of those enemies
and rolls X dice per carrying model; each 6+ deals one wound (unsaveable-style, like Ravage).
Done means: the trigger is owner-ruled, the primitive built with an integration test mirroring
the nearest existing (`CrossingAttackStage` is the near-exact template - move-triggered,
once-per-X, pick-an-enemy, roll-at-6+ wounds via `SyntheticWoundResolution` + the save-skipping
assign/apply child chain), the def authored in `AofRuleSupplement.json`, and the AoF census
reports 0 dead refs.

## The owner ruling needed (deferred 2026-08-22: "I need to look into the rule more")

Engine facts that make this a genuine fork (#376 recon):
- The charger's mandatory 1" post-melee move-back (`ConsolidateStage`) writes positions
  directly - no hook, no MovementExecutor, no MovedThisRound stamp.
- Only the CHARGED unit gets the post-melee triggered move (`PostCombatMoveGate`, the
  Harassing family funnel - which already snapshots before/after positions and owns the
  once-per-round marker discipline).
- `Movement_OnMoveResolved` is a DEAD hook: declared and documented, never fired, no context
  type, no lint entry. Lighting it up generally is a materially bigger slice.

Options (recommendation was A):
- **A. Both combatants, move required**: new stage after PostMeleeStage; ConsolidateStage and
  PostCombatMoveGate record that a unit actually moved post-melee; fires for either combatant
  that carries the rule, moved, and ended within 3" of an enemy. Covers the charger's move-back
  (the rule's likeliest primary use) and Harassing-style moves.
- **B. Charged unit only**: ride PostCombatMoveGate alone. Least wiring; a unit that CHARGES
  with the rule never triggers it - a large silent scope cut vs the text.
- **C. Proximity only**: fire at melee end for any carrier within 3", no move requirement.
  Simplest; over-grants for a stationary defender that never moved.

## Notes

- 2026-08-31 implemented, one slice. Engine: `Movement_OnMoveResolved` LIT (narrowly - chosen moves
  only) via `MoveResolvedContext`; shared `RetreatingStrikeResolution` (offer at the hook filtered to
  DealAutoWounds, `AbilityTargeting.EligibleTargets`, bare `CancellableSelectionRequest<UnitData>`
  where cancel = decline, `SyntheticWoundResolution` pool, CrossingAttack-style save-skipping
  assign->apply child chain); two thin trigger stages - `RetreatingStrikeMoveStage` (MovementStage,
  after ExecuteMoveStage, dark below the #333 zero-move floor) and `RetreatingStrikePostCombatStage`
  (after PostMeleeStage AND PostShootStage; keys off new `ICombatActionContext.PostCombatMover`,
  which the Post*Stages set only when `PostCombatMoveGate` - now returning bool - really repositioned
  the unit). New round-scoped `TokenType.WasInMeleeThisRound` stamped on BOTH combatants in
  `ApplyFatigueStage` (Fatigued is not a complete was-in-melee signal: the passive defender). Fire-lint
  taught the new hook (AbilityOfferingHooks, IsOpHandledAtAbilityHook, ContextVariants). Def authored
  in `AofRuleSupplement.json` as pure data: activated DealAutoWounds at the hook, Cost.OncePerRound
  (RoundEnd marker - the ActivationEnd trap avoided), TargetSelector(3,1,1,Foe,noLoS),
  diceCountPerModel=Arg(0) (book carries X=1 on multi-model units, X=3 on the single-model beasts -
  carriers x X lands both shapes), availableWhen = And(TokenPresent(WasInMeleeThisRound),
  Not(TokenPresent(Shaken))). Dark Elves book rebaked (only book changed, 39 byte-identical); census
  allowlist entry REMOVED - both corpora at 0 dead refs. 13 tests in
  `Tests/RetreatingStrikeRuleIntegrationTests.cs`; engine 3083 green, app 1557 green, headless smoke
  exit 0.
- 2026-08-31 recorded deferrals / adjacent findings (none block the rule as printed):
  - Wipe-out consolidation (the survivor's chosen 3" move) does NOT trigger the strike; nor do
    teleport/reposition placement or disembark. Extend `MoveResolvedContext`'s seams if ever ruled in.
  - ~~The engine's Harassing offers the post-melee move to the CHARGED unit only~~ FIXED same day by
    #391: both combatants now get the post-melee move (charger first), so a charging carrier reaches
    its strike through Harassing exactly as the ruling described. The strike arm picked it up
    automatically (same gate) - the hand-off became a list (`PostCombatMovers`) for the
    both-combatants-Harass case, drained one strike per pass.
  - ~~Harassing itself is NOT Shaken-gated in the engine~~ FIXED same day by #391 at the
    PostCombatMoveGate chokepoint.
  - Solo AI answers the target pick with fewest-living-models and never declines (the bare-request
    resolver); acceptable for a free once-per-round strike, revisit if Tactician needs rule-aware
    targeting.
  - Joined-hero carrier counting: a unit-scope DealAutoWounds counts ALL alive models (a joined hero
    without the rule still adds a die) - the #303 family, same behavior as Ravage Aura.

## GUI hand-verify checklist

- Forge or import a Dark Elves list (any unit - all 14 carry the rule + Harassing). Get charged,
  survive, take the Harassing move, stay within 3" of an enemy: the Retreating Strike target pick
  appears (cancel declines), dice beat rolls at 6+, wounds land with no save.
- Same unit, its own later activation: move to within 3" of an enemy - the pick appears at move end.
- Charge WITH the unit (post-#391): after the melee it is offered its Harassing move too - DECLINE
  it, and after the 1" move-back there is NO strike prompt (the excluded arm); take it, and the
  strike prompt follows the move.
- Use it once, trigger again same round: no second prompt; next round it is back.

- 2026-08-23 (#378): the 14 dead references now ship in a BUNDLED book (`Assets/Books/
  AoF-DarkElves.fdgbook`, unit-attached) and are pinned by `FdgRaylib.Tests/BookRuleCensusTests`'
  allowlist entry ("Retreating Strike" -> this item). When this item lands: author the def, rebake
  the Dark Elves book (`scripts/bake-aof-books.sh`), and REMOVE the allowlist entry - its stale
  guard fails loudly until you do.

- 2026-08-22 (filed from #376's close; number 381 taken after `git fetch origin`, see
  Reconciliations). Implementation notes banked from the #376 recon:
  - Once-per-round must be a RoundEnd-cleared marker, NEVER ActivationEnd: the trigger fires
    during the ENEMY's activation and the end-of-activation sweep only visits the activated
    unit (the RegenerativeStrengthSpent trap, documented on TokenType).
  - `Cost.OncePerRound` machinery exists but has NO shipped production user yet - if used,
    it needs an integration test.
  - Target pick: `CancellableSelectionRequest<UnitData>` has resolvers on every profile, but
    the solo AI answers EVERY such request with AiChooseMeleeDefenderResolver
    (fewest-living-models) - acceptable for a wound pick, not rule-aware.
  - Proximity: `AbilityTargeting.EligibleTargets` with `TargetSelector(3f, 1, 1, Foe, false)`
    is the canonical "enemies within N inches, on the battlefield" query (the off-battlefield
    guard matters - reserves park at the origin).
  - "Roll X per model with this rule": Effect.DealAutoWounds' carrier counting is exactly this
    (Ravage precedent); X is the rule argument.
  - Dice invariant applies (histogram pools, fractional wound totals, decisive picks).

## Decisions

- 2026-08-31 **owner ruling (unblocks the item)** — informed by the official Discord thread
  "Harassing + Retreating Strike on Opponent's Activation" (2026-04-28/29) and the fact that
  ALL 14 Dark Elves carriers also have Harassing (the rules ship as a designed pair):
  - Trigger = **end of any CHOSEN move by the unit while it carries a round-scoped
    "was in melee this round" state**. Two qualifying move kinds: (1) post-melee
    Harassing-family moves (the `PostCombatMoveGate` funnel — "Harassing fires first"),
    (2) the unit's own activation move later in the round.
  - The charger's compulsory 1" post-melee move-back does NOT count ("its move" = a move
    the unit chooses; counting it would make the strike free on every charge, and the
    Discord describes the charger's path to the strike as going through Harassing).
  - Optional to use (Active Special Rule) => **Shaken blocks it**, same as Harassing.
  - Once per round (RoundEnd-cleared marker, per the banked note - never ActivationEnd).
  - Pick ONE enemy unit within 3" (may differ from the melee opponent); roll one die per
    carrying model (single-model units: X dice); each 6+ deals one straight wound
    (dealAutoWounds path, no save); no melee-range restriction on who rolls.
  - This supersedes the original A/B/C fork: it is A minus the move-back arm, plus the
    own-activation-move arm.

## Outcome
