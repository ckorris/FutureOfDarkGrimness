# 247 — Range / threat overlay UI rethink

**Status:** Exploratory, filed 2026-07-18. Design pass first — surface the fork and get Chris's
sign-off before building anything (per CLAUDE.md). Number 247 verified free against index + archive on
2026-07-18. No code yet.

## Trigger

Chris, right after the #246 escape-menu work: "I feel like I need a better UI for the range overlay
thing." The escape-menu Options panel now surfaces the two controls for it — **Threat frontiers (F)**
and **Anchor field on my position (off = the target's weapon ranges)** — and those labels read as
cryptic, which is a symptom: the range/threat visualization system grew feature-by-feature and its
controls + on-table picture never got a coherent UI pass.

## What "the range overlay thing" is today

Several overlapping visualizations, all from the #162 tactical overlay (`docs/tactical-overlay-plan.md`,
`FdgRaylib/Rendering/TacticalOverlay/`):

- **Opportunity field** — the team-colored band field drawn while planning a move. Two modes via the
  *anchor* toggle: *Target* = "where can I stand to shoot the pinned target" (classic), *Self* = "what
  can I hit from my pending ghost position", rebuilt per frame (`TacticalOverlayConfig.GhostAnchoredField`).
- **Threat frontiers** — contour lines toggled by F / the Options checkbox
  (`TacticalOverlayController.ThreatToggledOn`). No longer auto-shown during a move; this is the only
  way to raise them now.
- **Per-model instruments** — pips/counts/distance annotations that call real rules.
- **Passive range rings (disabled)** — the old cyan shoot / amber charge per-model rings
  (`TableTooltipOverlay.DrawRangeRings`, commented out) that the field *replaced*. Still in the source
  as a re-enable-able path.

Related but out of this item's core: #230 (range rings during placement), #214 (teleport reach circle),
#231 (remove the confusing LoS blocking line), #224 (persistent unit-inspector panel with threat radius).

## The problem to pin down (open questions for Chris)

The design pass should start by nailing *which* of these is bothering him, rather than guessing:

1. **Discoverability / labels** — is it mainly that the controls (Threat, Anchor) and their meaning are
   opaque, and the fix is clearer labels + maybe an in-context legend? Or is the on-table picture
   itself the problem?
2. **The anchor concept** — is "Target vs Self" the right mental model at all, or should the overlay
   just *know* what to show from context (planning a move -> my reach; inspecting an enemy -> its
   threat) with no manual toggle?
3. **Field vs rings** — the band field replaced the old range rings deliberately. Does the field read
   well, or would simpler per-model range/charge circles (the disabled `DrawRangeRings`) communicate
   "who can reach what" more legibly — or some blend (rings on hover, field on plan)?
4. **When it shows** — passive-on-hover, only-while-planning, or a sticky toggle? Today it's a mix.
5. **Threat frontiers** — are they earning their keep as a separate F-toggled layer, or should threat
   fold into the same picture as reach?

## Candidate directions (to flesh out after Q&A, not decided)

- **A — Just clean the controls.** Rename/annotate the Options toggles, add an on-table legend for the
  field bands + threat contours, leave the visuals as-is. Cheapest; fixes it if the problem is purely
  legibility.
- **B — Context-driven overlay.** Drop the manual anchor toggle; the overlay infers reach-vs-threat
  from what the player is doing (planning move, hovering enemy, idle). Fewer knobs, more "just works".
- **C — Rings-first legibility.** Bring back per-model range/charge rings as the primary "can I reach"
  cue (hover + selected), keep the field for the richer "where to stand" plan view, and make the two
  visually distinct so they don't compete (the original reason the field replaced the rings).
- **D — Fold into a unit inspector.** Merge with #224: a selected-unit panel that owns the range/threat
  readout and drives what's drawn, so the overlay has one clear owner + explanation surface.

These aren't exclusive (A is likely a prerequisite for any of them).

## Deliverable of this item

A short design note (options + tradeoffs + a recommendation) presented to Chris for sign-off, then a
split into concrete build items. Not a code change yet.

## Relations

- Under / feeds #162 (tactical overlay umbrella) — likely lands as new phases there rather than a
  wholly separate track; decide during the design pass.
- Overlaps #224 (unit inspector), #230 (placement rings), #214 (teleport circle), #231 (LoS line).
- The #246 Options panel is where any new/renamed controls will live.
