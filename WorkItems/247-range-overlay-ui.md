# 247 — Range / threat overlay UI rethink

**Status:** Slice 1 built 2026-07-26 (direction B, context-driven anchor + one global toggle) —
awaiting GUI hand-verify. Q5 (threat frontiers) and the legend/labels half of Q1 still open.

## 2026-07-26 — slice 1: one toggle, one anchor per frame

Came out of #230's hand-verify: "we just need that tactical overlay easier to toggle... while moving, or
deploying, or even just inspecting the map while waiting for an opponent."

The finding that shaped it: those are **not the same problem**. Moving and deploying both already have an
anchor (their ghosts); *idle inspection had none* — `DrawField` returned early with no move job and no
placement, so there was nothing to toggle on. That half needed a new anchor, not a relocated checkbox.

Built (direction **B**, signed off in conversation):

- **`FieldAnchorPlan`** (`FdgRaylib/Rendering/TacticalOverlay/`) — the anchor decision, which had been
  spread across four places (`DrawField`'s move-request branch, the `GhostAnchoredField` mode flag,
  `ResolveFieldTarget`'s pins/hover, and #230's placement fallback), collapsed into one pure function with
  a priority order: `hover > move-ghosts | pinned-target > placement-ghosts`. Exactly one winner, which is
  what makes the pictures mutually exclusive — two team-coloured washes over the same ground are
  unreadable, so the contest is the feature, not tidiness. Testable without ImGui (`FieldAnchorPlanTests`,
  10 cases).
- **Hover anchors the field on any unit**, at its live positions — the idle-inspection case, and it works
  mid-decision too. Excludes the unit whose ghosts are live: its models still stand at their *original*
  positions, so anchoring there mid-aim would answer a question nobody asked.
- **`V` is the master toggle** (`ViewSettings.ShowReachOverlay`, default on), handled globally in
  `UpdateInput` rather than in any resolver, so it means the same thing while moving, placing and idle.
  Checkboxes in the placement panel and Esc -> Options drive the same flag. Resolvers now report what they
  *have* to anchor on; the controller decides what draws.
- **Caching**: `RebuildGhostField` is signature-gated on (anchor unit, source positions at 0.1", distinct
  ranges, every other unit's model positions — they are LoS blockers). Live ghosts move every frame so
  their signature changes every frame and they rebuild exactly as before; a hovered unit is stationary, so
  the expensive half (polar sight maps + GPU upload) is skipped after the first frame. The per-frame vs
  cached split is emergent, not a mode.
- **Hover dwell disabled** (`HoverPreviewDelaySeconds` 0.150 -> 0.0, note left in
  `TacticalOverlayConfig`). Chris: the delay doesn't read as deliberate restraint, it reads as the picture
  being slow to bake. The anti-flicker measure that survives is the hover-doesn't-steal-your-own-ghosts
  rule above.

**Behaviour change worth naming:** hovering an enemy during a move job used to show the *target-anchored*
field ("where can I stand to shoot it"); it now shows that enemy's *own* reach, per the new rule. The
target-anchored picture is still reachable by pinning (click) and then looking elsewhere — hover means
"what does that unit reach", pin means "where can I stand to shoot it". Coherent, but it is a change.

**Deliberately NOT done:** deleting `TacticalOverlayConfig.GhostAnchoredField`. It selects between two
genuinely different *move-job* pictures and is default-OFF pending a feel-check, so removing it would
silently change the default movement experience. The contest routes around it instead.

### Answers to the open questions below

- **Q2 (anchor concept)** — answered: context-driven. The manual toggle survives only for the move job's
  two pictures.
- **Q3 (field vs rings)** — answered: field. #230 first shipped plain geometric outlines and they were
  reverted; without LoS/cover they draw reach straight through a building, which is the one thing a
  distance tool must not do.
- **Q4 (when it shows)** — answered: sticky global toggle, context picks the anchor.
- **Q1 (discoverability)** — half. One key with one meaning, surfaced in two panels; an on-table legend
  for the band/threat vocabulary is still unbuilt.
- **Q5 (threat frontiers)** — untouched. F is still a separate layer with its own reference-player logic
  (`ResolveReference` keys off a move job or the activating unit), so enemy threat still does not
  auto-show while deploying. Folding threat into the same contest is the obvious slice 2.

### Hand-verify

1. `V` toggles the field off/on identically while moving, while deploying, and while idle.
2. Idle (or waiting on an opponent): hover any unit -> its reach appears immediately, no perceptible dwell.
3. Mid-placement: hover an *enemy* -> the field switches to that enemy; move off -> your ghosts' field
   returns. Hover your *own* placing unit -> the ghost field stays (must not switch).
4. Only ever one field on screen.
5. Hover a unit and hold still: no per-frame rebuild cost (the `[overlay] ... ms/frame` Debug warning must
   not fire; it should for live ghosts on the CPU path only).
6. Move a third unit between a hovered unit and open ground -> the shadow repaints (the blocker half of the
   signature).
7. Esc -> Options: the new "Weapon reach (V)" checkbox agrees with the hotkey both ways.

---

**Originally filed:** Exploratory, 2026-07-18. Design pass first — surface the fork and get Chris's
sign-off before building anything (per CLAUDE.md). Number 247 verified free against index + archive on
2026-07-18.

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
