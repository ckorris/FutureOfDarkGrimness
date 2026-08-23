# 387 — Show modified weapon range in the shoot panel and movement overlay

**Status**: implemented 2026-08-23 (same session as filed); awaiting GUI hand-verify
**Related**: #377 (the range-affecting spells that made the gap visible), #102 (RangeModifier
family), #325 (request-carried display facts doctrine), #230/#247 (tactical overlay bands).

## Goal

When a weapon's effective range against a target differs from its printed range (Battle Rune's
+6" grant, an Eternal Guidance "+6\" range" mark on the target, Ranged Shrouding's -6", Increased
Shooting Range...), the player should SEE that the number is modified and by how much - today the
shoot panel says only "in range"/"Out of range" against the base range on the weapon line, and the
movement overlay's band labels print the effective range with nothing marking it as buffed/debuffed.
User's ask (2026-08-23, verbatim intent): "some kind of visual indicator, like +6 or something to
the range, in the shooting step? And the movement step?"

Surfaces:
- Shoot panel (GuiChooseRangedAttackResolver): target rows + the target detail pane.
- CLI shoot resolver: same fact on the option line (front-end parity).
- Movement tactical overlay: band labels (pinned-target mode and the ghost-anchored field with a
  pinned range target - the two places per-target effective ranges already flow).

## Design

- Engine carries the fact (#325 doctrine - resolvers cannot compute it: rules do not cross the
  wire): `WeaponTargetStats.EffectiveRangeInches`, stamped by ChooseRangedAttackStage from the
  `RangeRuleQueries.EffectiveRange` it already computes per weapon-target. 0 = unstamped (no
  living carrier looped) - treat as base.
- One app-side composer (`RangeDeltaText`) formats the suffix so all surfaces agree:
  `30" (24"+6")` style for full text, `(+6")` for compact suffixes. Movement band labels append
  the per-weapon suffix, since a band groups weapons by effective range but bases differ per name.

## Notes

- 2026-08-23: Filed mid-session after the #377 verification game: the user could target the
  marked Distant Lurkers at 27" but nothing said why the 24" rifle reached.

## Decisions

- Delta only, no per-rule attribution chips in this pass: the forecast Notes already name an
  unclaimed mark, and a HitTags-style chip list for range sources is a follow-up if wanted.

## Outcome

Implemented same-session (engine `c38a973`, superproject `32b7915`-era commit). Engine stamps
`EffectiveRangeInches` on `WeaponTargetStats` from the eligibility check's own number (+2 stage
tests: buffed 12->18, unmodified =base); app `RangeDeltaText` composes all surfaces (4 tests):
GUI target rows ("range 30" (+6")", out-of-range rows explain a shortening), target detail pane
("Range 30" (base 24", +6")"), CLI rows, and the tactical overlay's pinned-target band labels
(per-weapon "(+6")"). Engine 3033 green, app 1372 green, headless smoke exit 0.

**Hand-verify**: in the 377-spell-verify scenario, cast Battle Rune on the Rifles and Eternal
Guidance on the Distant Lurkers, then open Shoot with the Scouts - the Lurkers row should read
"range 30" (+6")" and its detail pane "Range 30" (base 24", +6")". While MOVING the Scouts with
the Lurkers pinned, the band label should read "30" 3x Rifle (+6")".
