# 359 — Crowded-zone lane clearing: measure the frontline bias, pay units to vacate rear lanes

**Status**: done (2026-08-05; was #356 pre-reconciliation-55, same-day slice; option 3 deliberately not built - see Outcome)
**Related**: #296 (crowded-game drift — this builds its two deferred residuals' cheapest halves),
#264 (walled-unit umbrella), #216 (stacked-plan repair), #205 (friendly-stacking rule)

## Goal

Chris's re-report (2026-08-05, same behavior as the #296 filing): in big games with packed
deployment zones, rear units activate and net ~2" because friendlies wall them in. Humans
(a) activate the front rank first and (b) make a front unit that has no reason to advance -
a long-ranged gun, say - step ASIDE so the units behind can pass. #296 landed the ordering
half as `ActivationFrontlineBias` (0.1, only decisive when kill/flip/threat are flat) and
deferred the lane-clearing half. This item:

1. **Measure** how often the frontline bias actually decides the activation pick in the
   crowded shape (it may be swamped exactly when needed - `UnderThreat` differs by more
   than 0.1 across a deployed line). Diagnostic rides the existing `--log-decisions` sink.
2. **Build the lane-clearing term**: a candidate endpoint that sits on the advance lane of a
   friendly that has NOT yet activated this round (`TokenType.ActivatedThisRound` absent)
   pays a value-weighted penalty; a new M13 SideStep candidate family (perpendicular to the
   advance axis, Advance-budget so the unit can still shoot) gives the argmax something to
   pick. The scorer already prices what stepping aside costs the stepper (Indirect's -1,
   marker deltas, screen credit), so the tradeoff comes out in one currency.

Explicitly NOT this item: friendly-aware routing / goal retargeting (stamping unactivated
friendly bases into the pathfinding grid) - that is the structural fix, held as option 3
if measurement + lane term do not move the observed behavior. Cross-team activation
ordering in 2v2 stays impossible at this layer (#296's recorded limitation).

## Mechanism (what exists today)

- The routing grid contains TERRAIN only - routes run straight through the friendly mass;
  #205 legality then forces endpoints short of the wall and the ladder halves to ~2".
- `TacticianActivationResolver`: `score = Urgency + ActivationFrontlineBias * forwardPercentile`.
- `MacroActionGenerator` has NO lateral family: no candidate expresses "stay near here but
  get out of the way", so no score term could reward it even if one existed.

## Notes

- 2026-08-05 (Chris's mid-review correction, applied): "stepping aside might actually make the
  problem worse if it prevents the unit from walking forward, which also gives the room behind
  it room to walk. If left and right weren't crowded, the unit behind it probably would've been
  deployed behind it anyway." Both halves adopted:
  - **BlockValue now scales by (1 - t) along the lane** (and ignores points behind the friendly
    or past its whole reach): blocking right in front of the friendly costs full weight,
    blocking at the tip of its reach costs ~nothing - so an ADVANCE that ends deep downrange is
    nearly free (the friendly walks into the vacated ground) and only the near-corridor wall
    prices. SideStep thereby demotes to the fallback for units with a real reason not to
    advance; a paying advance beats it on its own terms.
  - **Calibration fallout**: mid-lane standing now prices at ~half the constant, so
    `MoveLaneBlock` 0.1 -> 0.2 (keeps the mid-lane stand at the ~0.1 the term was sized for,
    above the 0.05 reachable tie-break); SideStep goalRadius 1 -> 2 (a formation that repacks
    within a base-width of its arbitrary clear-point has arrived - grading it BudgetClipped
    cost it the same tie-break and handed the pick back to Hold).
  - Pin rework: the packed-scene pin now asserts CLEARING (forward or aside both legal);
    a new walled-ahead pin (`FrontUnit_WalledAhead_StepsAsideRatherThanHoldingTheLane`) pins
    the lateral step for exactly Chris's "has reasons not to advance" case.
  - His deployment point stands as a scope note: in a rationally-packed round-1 zone, side
    room is rare by construction - the aside mechanism earns its keep mid-game and at terrain
    funnels; the packed round 1 itself is option 3's territory (friendly-aware routing).

- 2026-08-05 (#358 found while gating this slice's bench): the first pool run's 2 "watchdog"
  faults were NOT CPU-contention artifacts like every earlier one this week - seed 1010
  standalone burned 600s and ~1.5M decisions. A wedged SOLO unit's main-move decline reopens
  the action menu and the deterministic solo policy re-picks Move forever. Filed + fixed as
  [[358-solo-move-decline-livelock]]; both fault seeds complete on the fixed build. The #359
  bench below runs on top of that fix.

- 2026-08-05 (measurement + implementation, same session as filing):
  - **Bias measurement** (new `activate` narration in `TacticianActivationResolver`, rides the
    `--log-decisions` sink; pinned by `TacticianLaneClearTests.ActivationPick_FlagsBiasDecisive...`):
    on `Scenarios/crowded-2v2-3k.json` (seed 42, 4 rounds, all-AI Tactician), the frontline bias
    DECIDED 6 of 39 multi-option picks in round 1 (~15%) and 18 of 121 across the game. It binds
    far more than "only when everything is flat" suggested - no retune attempted; the 0.1 scale
    looks healthy.
  - **Lane term + M13**: landed as designed (LaneGeometry, MoveLaneBlock 0.1, SideStep family,
    gate on standing-on-a-lane). Clean-scene pins green + red-proved (weight 0 + gate off ->
    the two behavioral pins fail).
  - **Honest observation on the packed seed**: SideStep candidates appear and score within a
    hair of Hold (0.6527 vs 0.6605) but get BudgetClipped to ~0.3-1" lateral - THE FLANKS ARE AS
    CONGESTED AS THE LANES, and the straight-line ladder cannot weave around friendlies it
    cannot route past. Zero SideStep wins in the seed-42 game; round-1 sub-2" movers unchanged
    on the shared pre-divergence prefix (post-divergence counts are unpaired noise). The wedged
    rear unit's table (every candidate Blocked at its own centroid) is the clearest exhibit:
    no scoring term can help a unit the ladder cannot move. The term still shapes endpoint
    choice across all intents wherever room exists (the pins show the mechanism firing), but
    the packed-zone weave itself needs option 3 - friendly-aware routing / staging - exactly
    as scoped out of this slice.
  - **Verification (final build: t-scaled term + calibration + #358 fix)**: engine suite
    2876/2876 (5 lane pins + 2 latch pins); full build + headless smoke exit 0; solo D1
    bit-identical on both matrices (mirror `4B73F1B9DBBC8102`, basic `E86503B238B27EA1`) -
    even with the #358 latch, no D1 game reaches a wedge state. **Pool A/B, 8-army pool,
    Tactician vs SoloRules, 64 matchups x 50 games = 3200, DOP 12, seeds 1000+, unloaded
    machine:** control (= #281-gradient run, hash `CA79CA44195000DF`) **84.84% -> 84.39%**
    (hash `9A920BBBDFC03482`; -0.45pp vs ~0.65pp aggregate sigma - flat), **faults 0 -> 0**
    (the interim pre-correction run had faulted 2 games at 84.46% - both the #358 livelock,
    not the lane term), max game 29s (control 31s), decision cost 27.6 -> 28.4ms mean /
    477 -> 498ms worst p95 (flat). Worst cells +/-11pp ~ 1.6 sigma on 50-game cells (normal
    for a 64-cell scan); both MIRROR cells improved (Hives 73 -> 82, Robot Legions 76 -> 87).
    Final-build crowded-scenario observation: bias-decisive 6/41 R1 (21/114 game), SideStep
    still never wins the packed seed (congestion clips it; post-correction, forward advances
    absorb the lane-clearing role for free), the two `stuck` lines are the same wedged mob -
    the option-3 exhibit.

- 2026-08-05: filed and started (Chris: "do your recommendations exactly - 1 and 2 together,
  and file the remaining artillery issue [#360, filed as #357 pre-reconciliation-55]. Then 3 if we need").

## Decisions

- Lane = segment from the unactivated friendly's centroid toward the enemy mass, length =
  that friendly's Rush distance; endpoint blocking fades with lateral distance (full <=
  1.5", zero at 4") AND with progress along the lane (full at the friendly, zero at its
  reach tip - Chris's forward-clears-too correction; behind the friendly is free) -
  centroid-based, same style as the A5-4 screen geometry. Known v1 coarseness: wide
  formations can straddle a lane their centroid clears.
- Penalty weight is a `TacticianWeights` constant (`MoveLaneBlock` = 0.2; mid-lane stand
  ~0.1 after the (1-t) scale), value-weighted by the blocked friendly - pool-bench gated
  like every policy constant.
- SideStep candidates are gated on the unit currently standing on some rear lane, so the
  candidate budget and CPU are untouched in uncrowded games. Their goalRadius is 2" - the
  goal is an arbitrary clear-point, and a BudgetClipped grade would cost the reachable
  tie-break that lets a completed side-step beat standing still.

## Outcome

Both agreed halves landed in one slice: the frontline bias is MEASURED (it decides ~15% of
multi-option picks in the crowded shape - healthy, no retune; the `activate` narration now rides
`--log-decisions` permanently), and the lane-clearing mechanism exists end to end - LaneGeometry
lanes over unactivated friendlies, the (1-t)-scaled MoveLaneBlock penalty (Chris's
forward-clears-too correction), and the gated M13 SideStep family - pinned by five tests
including the walled-ahead step-aside. Pool-flat, zero faults, solo-D1-identical, decision-cost
flat. Found and fixed #358 (solo move-decline livelock) while gating the bench. The honest
limit, recorded up front and confirmed by observation: in a rationally-packed round-1 zone the
FLANKS are as congested as the lanes, so the packed-seed creep itself is unchanged - that is
option 3's territory (friendly-aware routing/staging), to be filed fresh if Chris's big games
still show it after this term has had its say mid-game.
