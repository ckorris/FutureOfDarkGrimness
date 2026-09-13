# 345 — Show how much of the volley actually fires (7/10)

**Status**: in-progress (implemented + tested; awaiting GUI hand-verify)
**Related**: #276 (the trim this reports), #325 (the forecast it rides on), #340 (one-at-a-time weapons)

## Goal
When choosing a shooting target, show how many attacks will actually go off against how many the unit
could throw — e.g. "7/10" — so a player can see before committing that blocking terrain (or range) has
taken part of the volley out. Today the only way to learn it is to count the dice after the fact.

## Notes

- 2026-08-05: Implemented. `ChooseRangedAttackStage.AttackCounts(option, stats)` mirrors the #276 trim
  (including its "eligible == 0 leaves the count alone" guard) and multiplies by the weapon's Attacks;
  `ShootingForecast.Attach` stamps the pair onto `AttackForecast.AttacksFiring` /
  `AttacksPotential`. Shown in three places — the GUI details pane ("Attacks: 7 of 10 (3 held back -
  no line of sight or range)"), the target row's sub-line and the canvas hover badge (both "7/10
  attacks", short form), plus the CLI target list. Tests in `ChooseRangedAttackStageTests` (engine)
  and `GuiChooseRangedAttackResolverTests` (short-form suppression).

## Decisions

- **Engine-side, like the rest of #325.** A resolver cannot compute this: `IWeapon.RuleDefinitions` is
  `[JsonIgnore]`, so a remote player's request carries no weapon rules, and the eligible-carrier set is
  the stage's own.

- **One implementation of "eligible copies".** `CountEligibleCopies` (which drives the actual trim) and
  the forecast both call the new `EligibleCopies`. If they were separate the preview could promise dice
  the roll would not throw, which is worse than showing nothing.

- **Silent when nothing is held back.** The short form on rows and the canvas badge returns "" when
  firing == potential — those surfaces are scanned to *compare* targets, and a ratio on every row is
  noise. The details pane still shows the plain count ("Attacks: 10"), because that pane is read
  deliberately.

- **A one-at-a-time weapon (#340 Takedown / Sniper) reports 1 of 1.** Its other copies are not blocked,
  they are aimed separately on later passes, so "1 of 3" would blame terrain for the rule's behaviour.

- **The dice beat was left alone.** The to-hit beat's context line already carries the attack count it
  rolled ("Warriors -> Gunners | 7 attacks"); adding the potential there needs a pre-trim count on
  `ICombatMetadata` and the pending-attack plumbing. Judged out of scope for the ask, which is about
  the decision point. Noted here rather than dropped.

## Outcome
_(pending)_
