# 387 — Show modified weapon range in the shoot panel and movement overlay

**Status**: in-progress (filed 2026-08-23, user-requested during #377 hand-verify)
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
