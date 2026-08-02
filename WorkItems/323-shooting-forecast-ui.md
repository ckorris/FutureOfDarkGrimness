# 323 — Pre-roll shooting forecast (effective Hit/Save + modifiers in the target UI)

**Status**: in-progress
**Related**: #319 (ChooseRangedAttackRequest reply forms), #292 (RuleHoverText), #286 (canvas hover binding)

## Goal

When planning a shooting target, the player sees the numbers the dice will actually use — the
effective to-hit and effective save (Def + AP + cover + rule modifiers) with the modifier names
that produced them — instead of only raw weapon stats and datasheet Qua/Def. Three layers:
(1) engine-side read-only forecast attached to `WeaponTargetStats` on `ChooseRangedAttackRequest`
(weapon rules don't cross the wire, so precompute-on-request, same pattern as `CoverIgnoreRule`);
(2) target rows show `Hit X+ / Save Y+` plus target-specific modifier words (Cover, Stealth,
Shielded...); (3) Details pane shows the full tag ledger using the SAME strings as the post-roll
dice-overlay chips (shared `ComposeThresholdTags`), and a compact badge appears next to the
hovered target on canvas. Expected wounds deliberately NOT shown (owner call, 2026-08-02: most
players don't think probabilistically). Rides along: drop the misleading target-Quality line
from the shooting details section.

## Notes

- 2026-08-02: Slice 2 (rows) shipped. GUI: "Hit X+ / Sv Y+" right-aligned on each target row's name
  line (draw-list overlay; row's own text color so grayed rows gray with it; long-name collision
  guard skips the draw). CLI: "[hit X+, save Y+]" after the target name. Verified live in headless
  output - per-target differentiation visible (a 5+ row next to 4+ rows; Blast pricing cover away).
- 2026-08-02: Slice 1 (engine) shipped, submodule `3e503bd`, 2620/2620 green. `ShootingForecast`
  (ChooseRangedWeaponStage/) computes per fireable weapon-x-target row and `Attach` stamps it in
  `OfferWeapons` (NOT in BuildWeaponOptions - HasAnyFireableTarget reuses the builder every Choose
  Action open and only needs a bool). `AttackForecast` record nested in the request; chips come from
  calling the stages' own internal `ComposeThresholdTags`/`ComposeSaveModifierTags` directly (they
  are static+pure; no hoist needed - tests already call them as
  `DetermineHitRollStage<ICombatMetadata>.ComposeThresholdTags`). Save-side whole-attack fold
  evaluated with an EMPTY roll histogram so face-triggered ops price as zero. Better than planned:
  granted one-shot buffs (new read-only `GrantedRollModifiers.PeekNet`) and persistent Target-family
  markers ARE priced; only spendable Tag/Spotter markers + unclaimed Marks fall to Notes.
  `ICombatActionContext` now exposes `AttackerMoved` (Indirect's -1 after moving forecasts truly).
  Not priced, by design: Rending/Furious procs (natural 6s), Unpredictable branch die,
  RegenerativeStrength attack bump. 6 new tests in ChooseRangedAttackStageTests.
- 2026-08-02: Filed. Design agreed with owner: forecast rides the request (not the presentation
  beat stream — beats are one-way transient narration, fan out to spectators, and would race the
  request; the forecast is decision state for the acting player only). Math via the read-only
  `RuleEvaluator.EvaluateAllNamed` twin (no log spam, no one-shot grant spends) feeding the same
  tag composition as `DetermineHitRollStage` / `DetermineSaveRollsNeededStage`. Known honest gaps
  (one-shot granted tokens, TargetMarkerSpend prompts, RegenerativeStrength attack bump) are
  footnoted, not silently wrong — CombatMath's `Notes` precedent. Canvas badge is hover-only
  (rides #286 two-way binding); always-on per-target numbers are the documented escalation path
  if playtesting wants more.

## Decisions

- **Reuse the vocabulary, not the pipe.** The dice-roll presentation system was considered for
  transport and rejected: beat lifetime (~1.8s, timeline-queued) fights "hangs around while the
  player decides", and the request already delivers to exactly the right audience at exactly the
  right moment.
- **No expected-wounds number in the UI.** CombatMath computes it, but the owner prefers target
  numbers only; the closed-form math stays available engine-side (AI Tactician).

## Outcome

(open)
