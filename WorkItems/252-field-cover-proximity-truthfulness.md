# 252 — Anchored field texture reflects the #201 cover proximity rules

**Status**: todo (filed 2026-07-21 from the #201 assessment; approach recommended below)
**Related**: #201 (the rules + the assessment's origin), #162 (tactical overlay umbrella - this is a
field-pipeline slice), #253 (the sibling placement visual), #247 (overlay UI rethink - coordinate)

## Goal

With the #201 lobby toggle ON, the Self/Target-anchored opportunity field's COVER tint must stop
painting cover in spots where a proximity exception voids it. Today only the tint lies: the
authoritative instruments (pips, movement aim lines, option-card cover flag) already apply the rules
(#201 S4). "Done" means the target-anchored field's cover channel matches `CoverCheckStage`'s
verdict per texel (modulo quantization), the fidelity sampler's `field-cover` channel is updated to
the same truth, and the perf budget (`TacticalOverlayConfig.RebuildBudgetMs`) still holds in
per-frame ghost mode.

## Why it isn't a patch (assessment, 2026-07-21)

- The production picture - GPU (`GpuFieldRenderer.RebuildDiscs` fans) and CPU
  (`PolarSightMap.ClassifyInto`) - is built from `PolarSightMap`: ONE nearest-cover-entry distance
  per angle bucket per source. The per-texel `BestSight` calls in `TacticalOverlayController`
  (~line 597) are only the debug fidelity sampler, not production.
- The proximity rules ARE radially encodable, but they turn cover-along-a-ray from a single
  threshold into a union of per-piece intervals: per angle, rule 1 voids a piece for query
  distances d in a band just past its far edge (d - exit(theta) < 2" + base r); rule 2 voids the
  near ~6" when the source sits inside the piece. So the map must store per-piece cover INTERVALS
  (entry AND exit), `ClassifyInto` must composite per piece with each piece's own void band
  excluded (union across pieces - you cannot just subtract one piece's band from a merged map),
  and the GPU fan geometry gains the same per-piece structure.
- Semantics gap needing a design call: rule verdicts need a TARGET. Target-anchored mode has one;
  Self/ghost mode has no single target, so a rules-true self field needs per-enemy evaluation or a
  documented approximation.

## Recommended approach

Per-piece polar intervals (the encoding above) for **target-anchored mode only**; Self/ghost mode
stays raw with the divergence note kept (`PolarSightMap` header + `RulesProbe`). Alternative
considered and disliked: exact per-texel CPU correction over cover texels near pieces - cheap
predicate, but GPU-only frames build no CPU masks, so it forces a mask upload/composite into the
GPU path anyway. Estimate: a solid day including perf validation and fidelity-sampler updates.
Shooter base for texel-side verdicts: modal base radius as a circle (facing-independent), matching
the field's existing base approximation. The rules predicate to call is
`CoverProximityRules.VoidsCover` / the `EvaluateSightLine` context overload - never a reimplementation.

## Notes

- 2026-07-21: Filed from the assessment written during #201 (its detail file carries the same text
  under "Assessed 2026-07-21"). No code yet.

## Decisions

(none yet - the Self-mode semantics call is open; surface it before building)

## Outcome

(open)
