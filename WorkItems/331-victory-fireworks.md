# 331 — Victory fireworks in the winning side's colours

**Status**: in-progress (implemented + tested; awaiting GUI hand-verify)
**Related**: #332 (the early match end that made the game-over card worth looking at), #257 (team-pooled
scoring), #221 (player colour palette), #246 (Options panel)

## Goal
Firework bursts behind the game-over card, coloured by the winning side's player colours. Chrome, not
rules: cheap enough that a machine that runs the game today still runs it, off-switchable, and correct on
a networked client as well as the host.

## Notes

- 2026-08-04: **GUI hand-verified by the owner** ("it looks great") on the #332 decided-match scenario -
  the 30 FPS line-segment sparks read as intended, so the tuning below stands. The three defaulted taste
  calls were not challenged.
- 2026-08-04: Implemented in two slices. Engine: `TeamScoreTally` (new) holds the #257 pooled tally and
  `VictoryCalculationStage` now calls it; 9 new `TeamScoreTallyTests`. App: `VictoryFireworks` (new,
  ~200 lines) plus the hook in `RaylibRenderer`, a `ViewSettings.ShowVictoryFireworks` toggle and its
  Options row; 9 new `VictoryFireworksTests`. Engine suite 2782/2782, app suite 1020/1020, full build
  clean, headless smoke exits 0. Engine `8e339eb`.

## Decisions

**The client cannot be told who won, so it works it out.** Same wall as #332: `ShowGameOver` receives only
the prose message, and the structured `GameResult.WinnerPlayers` is host-side only and never crosses the
wire. Rather than widen the wire (rejected in #332), the renderer derives the winner from state that DOES
replicate - objectives and player slots - via `TeamScoreTally`. That tally was extracted out of
`VictoryCalculationStage` rather than reimplemented in the renderer, so host and client run the same code
over the same state and cannot disagree about who is being celebrated. Wrong-coloured fireworks would be a
silly bug to have shipped for want of sharing 40 lines.

**Tuned for the 30 FPS cap, which is the real design constraint.** `Raylib.SetTargetFPS(30)` means a 33ms
budget, so cost was never the question - the existing `DrawDeathBursts` already draws per-particle circles
every frame. What 30 FPS does hurt is *legibility*: fast pinpoint sparks travel too far between frames and
strobe. So every particle draws as a LINE from its previous position to its current one - that segment IS
the spark, it restores the continuity the frame rate removes, and it is cheaper than the circle it
replaces. Speeds stay moderate and lifetimes long for the same reason; the slow drifting tail is the part
that reads well at 30 FPS.

**Draw order came free.** All raw Raylib drawing happens before `rlImGui.Begin()`, so the particles land
over the board and banners but under every ImGui window - the game-over card floats on top of them with no
layering work at all.

**Taste calls, defaulted rather than asked (owner said "keep it simple"):** fireworks show for winner and
loser alike (it is the match result, not a taunt); a tie fires every tied side's colours, which reads as
shared rather than as a failure; a multi-player team alternates its members' colours shell by shell rather
than picking the top scorer's.

**Bounded by construction.** The particle pool is a preallocated 900-entry struct array that never grows;
a full pool drops new sparks rather than allocating. Pinned by a test that runs two simulated minutes at
30 FPS and asserts the peak stays under 600, so the tuning keeps real headroom rather than riding the
ceiling.

## Outcome
