# 253 — Movement visual: where ending your move earns the cover bonus

**Status**: todo (filed 2026-07-21 by owner request)
**Related**: #201 (the proximity rules + the `VoidsCover` predicate this samples), #252 (field-texture
truthfulness), #230 (placement range rings), #247 (overlay UI rethink - coordinate the presentation)

## Goal

A brand-new movement-time visual: a colored area on the table showing where a model would have to
stand to actually RECEIVE the +1 Defense cover bonus against a chosen enemy - i.e. defender-side
"move into cover and it counts", honoring the #201 proximity exceptions when the lobby toggle is on
(hugging *their* muzzle-adjacent wall still counts via the amendment; sharing their forest under 6"
does not). "Done" means: while moving with an enemy pinned/hovered, the area draws on canvas, agrees
with what `CoverCheckStage` would roll from those positions, and is clearly distinguishable from the
existing band/threat visuals.

Owner phrasing (2026-07-21): "a totally new visual for where you'd have to move into cover to get
that bonus." Read as the DEFENDER-side area above. The attacker-side sibling - "stand here and you
shoot over this wall" (rule-1 ignore, owner request recorded in #201's future-visual section) - is a
natural second facet of the same instrument; confirm with the owner whether it lands here too before
building.

## Design notes (from #201's compatibility work - the hard thinking is done)

- Sample the real rules, never a texture: `CoverProximityRules.VoidsCover(piece, ctx)` and the
  `EvaluateSightLine` context overload are public, pure, allocation-free (one `GetLastSegmentExit`
  + at most three `SurfaceDistanceToPoint2D` calls per piece) - built cheap precisely so a UI can
  sample per candidate position (#201 "future visual" section).
- The region is inherently PER TARGET (both rules depend on both endpoints), and strictly it is
  per shooter-model too; a practical visual evaluates against the pinned enemy unit's living models
  (any-shooter-grants-cover matches `CoverCheckStage`'s per-defender check). A target-independent
  band would be an approximation and must be labeled as such.
- "In cover" for the moving model = its own hypothetical position vs each enemy model's sight line;
  majority-rule (unit-level bonus) is a unit property, so decide whether the visual shows per-model
  truth ("this model counts as in cover") or unit-level bonus prediction ("if the unit ends here,
  the majority holds"). Per-model truth is cheaper and composes with any formation; recommend that,
  with the majority readout left to the info panel.
- Presentation fork to surface before building (with #247): its own mask/tint vs an outline region
  vs hatching; and where it lives (movement resolver overlay like the aim lines, or the tactical
  overlay's instrument layer). Sampling cost is texel-band-scale and CPU-friendly; no PolarSightMap
  involvement needed (this is an instrument, not the field).

## Notes

- 2026-07-21: Filed. No code. Scope question (attacker-side facet in or out) flagged above.

## Decisions

(none yet)

## Outcome

(open)
