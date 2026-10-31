# 360 — Move-penalty shooters: price the Mobile Artillery token, overshoot forced approaches

**Status**: filed 2026-08-05 (not started; was #357 pre-reconciliation-55)
**Related**: #191 (Tactician umbrella), #359 (lane clearing - a SideStep shuffle by an
Indirect unit is priced by the same seam), #197 (rule corpus, Mobile Artillery facets)

## Goal

The Tactician IS structurally conscious of shoot-after-move penalties: the damage term
estimates every candidate with `AttackerMoved: candidate.Intent != Hold`
(`TacticianPlanner.Score`), that flag reaches the hit-modifier hooks via
`CombatMath.EstimateShooting`, and Indirect's "-1 to hit when the unit moved this
activation" lives in `CoreRuleCatalog` on exactly that gate. Artillery-proper is engine-
gated Hold-only and cannot move at all. Two real gaps remain:

### Facet 1 - Mobile Artillery's defensive token is never priced (found 2026-08-05)

"Enemies get -1 to hit this unit while it has not moved this round" rides a token stamped
at move time. The retaliation term estimates incoming fire at the CANDIDATE endpoint from
the unit's CURRENT tokens - a move candidate's estimate still carries the unmoved-this-round
defense, so moving silently forfeits the -1 for free. A Mobile Artillery unit will shuffle
for a marginal gain and give up its defensive buff unpriced.

Fix sketch: in the retaliation estimate, when the active unit carries a
defensive-while-unmoved facet and the candidate is not Hold, evaluate the incoming
`HitRollModifierContext` as if the token were already forfeited (a defender-side analog of
the `AttackerMoved` flag - likely a `DefenderMoved`/context bit the hook can read, or
evaluate with the token masked). Pin with the existing
`Scenarios/mobile-artillery-defensive.json` / `mobile-artillery-moved.json` pair.

### Facet 2 - overshoot a forced approach (Chris's question, 2026-08-05)

Chris: artillery that must move to get in range should often move MORE than minimally -
so it is (a) likely still in range next turn when targets shift, and (b) in range of the
objectives enemies must approach.

Opinion recorded at filing: yes, and the reason is the penalty's SHAPE - Indirect's -1
(and the Mobile Artillery forfeit) is flat per activation, not per inch. Once a move is
forced, marginal inches are free on the damage axis THIS turn, while inches deferred to
next turn cost a fresh -1 THEN. A minimal-approach endpoint is therefore usually
dominated: it buys this turn's shot at -1 AND makes next turn's -1 likely too (targets
redeploy ~6"/round). The right aim is RANGE SLACK: among candidates already paying the
move penalty, prefer endpoints where weapon reach covers the expected target mass (buffer
~ one enemy move, ~6") and the markers enemies must contest - "in range of the objective"
is the artillery station, not "on it". The counterweights are already priced (retaliation,
projected threat, charge reach), so a modest term self-limits: it will not creep a gunner
into charge range for slack it does not need.

Fix sketch (both halves needed - candidates AND a term, since in-range damage is flat in
distance so nothing today prefers the deeper endpoint):
- Generator: for units with a move-tied shooting penalty that are out of range, add a
  deeper M4 band at `reach - slackBuffer` (and/or aim the band at the nearest contested
  marker rather than the current target).
- Scorer: a small `TacticianWeights` term, non-Hold candidates of penalty-carrying units
  only: + weight x min(1, slack / 6") against the best target/marker cluster. Zero for
  units without such penalties (do not distort everyone else's spacing).

## Notes

- 2026-08-05: filed from the session's analysis of Chris's "is the TacticalBot conscious
  of Indirect/Artillery move penalties?" question. Not started - #359 (lane clearing) is
  the active slice; this is next in the agreed order, facet 1 first (well-scoped, and the
  forfeit gap actively misprices today's games).
