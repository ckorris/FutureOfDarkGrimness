# 191 — Tactician: challenge-level game-playing AI (umbrella)

**Goal:** an AI opponent that genuinely challenges human players with any army vs any army,
built as a ladder of shippable bots: evaluation-driven heuristics (A) -> MCTS over macro-actions
(B) -> learned value function (C) -> optional self-improvement loop (D).

**Design authority:** `docs/ai-agent-plan.md` — architecture, standing decisions (D1-D6),
invariants (G1-G12), stop-and-ask triggers, per-slice specs, gates, and the macro-action
vocabulary (Appendix A, awaiting Chris's edit). This file is the running ledger; the plan doc is
the spec. Keep both current (plan G10).

**Prerequisites:** #192 (structured game result), #193 (determinism/seeding), #194 (FdgLab
harness). Order: any, but all three before Phase A. Related: #066 (AI resolver legality tests),
#168 (rule-load diagnostics surfacing), #170 (solo-rules deploy packing — baseline hygiene).

**Standing authorizations (Chris, 2026-07-09):** engine submodule modification within
`Ai/Tactician/` + the named P/B seams; new project `FdgLab/` in this repo; Python+ONNX stack.
Solo-rules bot behavior is frozen (benchmark baseline) — refactors sharing its machinery need
pin tests. *(Amended 2026-08-15, owner's call: the freeze is lifted for transport behavior —
solo now embarks at deploy time and has a disembark trigger, see the A5-10b note. Benchmark
numbers recorded before that date were measured against the pre-A5-10b solo bot; future
campaigns re-base.)*

## Notes (newest first)

**2026-09-03 - B+C CAMPAIGN KICKOFF: branch `tactician-bc` (both repos), execution plan
`docs/tactician-bc-campaign.md`, plan-doc amendment (sec. 14).** Chris asked whether to skip
Phase B and train a value net directly to use a 4-day unattended window; after weighing it
(no true afterstate without B1, one-ply cannot value sacrificial/anticipatory plays,
search-free self-play loops collapse) he chose B then C, driven from his phone with
check-ins every few hours. Decisions D7-D11 recorded in the campaign doc: ladder order
stands; generalization across points {1k,2k,3k,4k} and shapes {1v1,2v2} is first-class
(new invariant G13 - fractions not absolutes, per-side feature aggregation, max^n backup,
branching-scaled budgets; 3v3/FFA not gated, one FFA no-fault smoke); gates gain
non-regression panels; held-out set for C is specific pairs at every point level + one 2v2 cell (first draft held out the whole 1k panel - Chris caught that it would leave 1k nearly untrained; corrected same day); the C1
exporter is pulled forward as idle-compute filler and its feature schema gets an Opus
review before the first long run (lock-in); pre-authorized seams: `DeterminePlayerTurnStage`
pause/step hook if B0 needs it, lab-side `SlotSpec.Team`. Model/effort policy per step with
a prompt-to-switch protocol (Sonnet default, Opus for lifecycle/UCT/C4/failure analysis,
Fable for B0 read-out + B1 design, B2 tree shape, C replan). Plan-limit facts verified
2026-09-03: shared weekly bucket, Fable <= 50% of it and ~2x Opus / ~5x Sonnet, box compute
is free, subagents count. Next: step 1 (harness: Team, pool manifest, panels, pause file),
step 2 (A generalization baseline, overnight), step 3 (B0 spike, soak overnight), step 4
(exporter) before Chris leaves 2026-09-04.

**2026-08-15 (cont.) - A5-10b: deploy-time embark extended to EVERY profile; solo gets a
get-out rule.** Chris sharpened the policy the same day: "Units should very rarely embark into
a transport AFTER deployment. During deployment, it's almost always best" - i.e. the deploy-vs-
midgame distinction, for all bots, not just the Tactician (and he chose to lift the solo
behavior freeze knowingly - AskUserQuestion, option "Extend it to solo too"). Changes:
`AiSelectionResolver<T>` now ACCEPTS the deploy-time embark prompt (first offered transport)
and, given the new optional `RuleEvaluator` (wired in `BuildSoloRules`), picks transports first
at the deploy-order prompt; `AiStringSelectionResolver` gains the solo-grade get-out rule
`ShouldDisembark` (disembark when any loaded friendly transport is within 12" - 6" placement +
one move - of an enemy model or a not-already-allied-held objective; the active unit is not
threaded through Choose Action, so it reads all loaded friendly transports - exact with one,
worst case a slightly early hop with several) plus the ranked Disembark branch above
Charge/Move/Shoot/Pass. Mid-game EMBARK stays filtered for everyone (the surviving half of
#335). Gunline inherits all of it via BuildSoloRules; the Tactician keeps its tightest-fit +
A5-5 edition, and its scaffold-mode fallthrough now accepts first-offer instead of declining.
`ChooseUnitToDeployStage.CHOOSE_UNIT_INSTRUCTIONS` promoted to a stage const (both AI layers
key on it; Tactician's `DeployOrderInstructions` aliases it). Tests: `AiSelectionResolverTests`
decline test FLIPPED to accept + new transports-first order test;
`AiStringSelectionResolverTests` +3 (near-objective disembarks, far keeps riding, near-enemy
disembarks); `TransportDeploymentChoiceTests` end-to-end AI test flipped to embark;
`TacticianDeployEmbarkTests` fallback test now pins first-offer accept. Verify: engine suite
2969/0 (+4 net), full build clean, headless smoke exit 0 (test army has no transports - branch
inert there).

**2026-08-15 - A5-10: deploy-time embark (owner's reversal of the #335 decline, Tactician
only).** Chris, reviewing a save where the Dark Elf Raiders bot walked its infantry past empty
transports: "you should pretty much always do that" - reversing his own 2026-08-04 #335 call
("very rarely the correct thing"), which predated the pieces that make riding pay (A5-5 arrival
timing, M12 DeliverCargo, #355 disembark-to-charge). Two additions to
`TacticianUnitSelectionResolver`, both keyed the same way the solo decline is: (1) the
deploy-time embark prompt (cancel label = `DEPLOY_NORMALLY_CHOICE`) is now ANSWERED with a
transport - tightest fit (least remaining capacity among the engine-validated offers, ties keep
list order) so small squads don't squat in big holds; (2) the A5-9 deploy-order pick deploys
transports before everything else (within groups the sensitivity order stands), since the
embark offer only exists for a hold already on the table. Requires the tableState+evaluator
ctor args; the scaffold shape (no table state) still falls through to the solo decline (G3).
Solo and Gunline keep #335 unchanged. Tests: `TacticianDeployEmbarkTests` (4: end-to-end embark
through the real `ChooseDeployActionStage`, tightest-fit pick, transport-first deploy order,
no-tableState fallback declines). Verify: engine suite 2965/0 (+4), full build clean, headless
smoke exit 0. Mid-game embark stays cut (Appendix A: MoveToEmbark) - deploy-time only.

**2026-07-27 — OVERNIGHT WIDE-MULTIPLIER CAMPAIGN: DEFAULTS STAND AGAIN (second null, now with
in-run confirms).** Chris asked for a second auto-tuning round (23:25 -> 07:00 window). The
engine had moved to `24d77f8` since yesterday's campaign (origin merge incl. #291's
base-off-table clamp), so every number re-based: fresh screen baseline **62.38** (8 cells x 50
games, seeds 3000+; was 60.38 pre-merge). Driver upgraded (committed with this entry):
**x0.5 / x2.0** multipliers over **12 knobs** - the 7 previously untuned movement/targeting
weights (MoveScreen, MoveObjective, MoveObjectiveApproach, MoveApproach, ShootThreatFactor,
MoraleBreakBonus, ShootingKillBonus) probed first, yesterday's 5 last - plus per-bench
timeouts, deadline awareness (--deadline-epoch with observed-rate projection), and an in-run
confirm stage: a screen hit (>= +3.0 at 50 g/cell) adopts only if it clears +2.0 at 150 g/cell
on a DIFFERENT seed base (5000). Campaign result: 25 evals, **no candidate reached even the
screen threshold** (best: MoveApproach x0.5 +2.00, MoveObjectiveApproach x2 +1.62,
MoveRetaliation x2 +1.50). Leftover budget went to follow-up probes: knock-outs (weight -> 0)
of the #191 slices all read neutral-to-negative at 50 g/cell (arriving-pressure -0.12,
risk-posture -0.12, share-floor -0.75 - each still earns its keep or breaks even), the top-3
singles combo read +1.88 (no synergy over MoveApproach alone), and ko-screening (MoveScreen=0)
screened +2.55. Confirms at 150 g/cell seed 5000 (defaults there: 63.91): **MoveScreen=0
+0.19** (the +2.55 was a mirage) and **MoveApproach=0.375 -2.88** (the campaign's best single
is actively WORSE on fresh seeds - winner's curse caught in-run, exactly what the confirm
stage was added for). Verdict: the hand-tuned defaults are now confirmed locally optimal to
x0.5/x2.0 across 12 knobs on the merged engine, and single-knob (or naive combo) weight
nudges are exhausted as an improvement lever - the next lever is structural (sum-vs-max
alternative-target aggregation, joint moves). Artifacts:
`FdgLab/reports/tune-2026-07-27-overnight/` (campaign.log, evals.jsonl, probes.log,
probes.jsonl; reports/ is gitignored - the numbers of record are here). Ops note: the
follow-up probe task was externally killed at ~04:50 (no OS/OOM evidence, cause unknown);
phase 2 was restarted standalone and completed 06:23.

**2026-07-26 — AUTOMATED TUNING CAMPAIGN RAN TO COMPLETION: DEFAULTS STAND (a null result at
full evidence).** Coordinate descent on the merged engine (submodule `d8d8446`): 5 knobs x
{x0.7, x1.3}, the 8-cell eval set, 50 games/cell paired seeds, adopt at >= +3.0 mean points.
11 evals, NOTHING adopted - best singles were the caution-direction bumps MoveRetaliation x1.3
(+1.7) and MoveProjectedThreat x1.3 (+1.4). Their combination probed +2.62 at 50 games/cell,
just under the bar and selected-winner-biased, so BOTH arms re-ran at 200 games/cell (G4): the
combo reads **-1.12** (5/8 cells negative, BB-vs-Hives -5.5) - winner's curse confirmed, the
+2.62 was noise. Verdict: the hand-tuned defaults are locally optimal to +-30% per knob and
against the best-looking combo; no default changes, so the full-gate arbiter was never needed.
Ops: one DOP-16 bench SEGFAULTED mid-campaign (rc -11, transient, plausibly #210's race under
load) - the driver now retries crashed benches and resumes completed evals from evals.jsonl.
Post-merge 200-game baseline on the 8 cells (the next campaign's reference): RL-Orks 53.2,
RL-Hives 60.0, RL-HEF 69.3, HDF-Hives 59.5, DE-Hives 64.5, DE-Orks 60.8, BB-Hives 70.5,
Dwarf-Orks 66.1 (mean 62.99). Next levers when this reopens: wider multipliers, joint moves,
and the STRUCTURAL candidates coordinate descent cannot reach (sum-vs-max alternative-target
aggregation; MoveScreen/MoveApproach were deliberately out of scope this round). Artifacts:
FdgLab/reports/tune-2026-07-26/ on disk (campaign.log, evals.jsonl, result.json; the reports
dir is gitignored like every bench report - numbers of record live in this ledger).

**2026-07-26 — TUNING INFRA (Chris: "do the automated weight tuning"): weights
runtime-overridable, FdgLab --weights, campaign driver. Engine `7f30a82`.** TacticianWeights
float consts -> public static floats + TrySet(name, value) (reflection, set before games only);
the committed defaults remain the shipped policy and still change only with a benchmark
attached. FdgLab bench/smoke take --weights "Name=V;..." (invariant culture; unknown name or
bad value is a hard usage error - a silently-skipped override would corrupt a campaign;
recorded in the report header so a tuned run can never pass as default). Verified: defaults at
dop 1 reproduce the cache-slice hash 6267BEA2307042D2 exactly (const->static is value-neutral);
--weights MoveRetaliation=99 flips the 4-game hash (the override reaches the planner); unknown
name exits 2. Driver: FdgLab/tools/tune_weights.py - coordinate descent over {MoveRetaliation,
RetaliationShareFloor, MoveProjectedThreat, PostureRetaliationRelief, PostureObjectiveBoost},
x0.7/x1.3 candidates per round, 8-cell eval set (the three RL decision cells + the trio gate's
sub-70 cells: HDF-Hives, DE-Hives, DE-Orks, BB-Hives, Dwarf-Orks), 50 games/cell paired seeds,
adopt only at >= +3.0 eval-mean points (~1.2 sigma incl. #210 schedule noise), 2 rounds with
early stop, every eval appended to evals.jsonl; the script never edits source - the full
ordered-pool gate arbitrates before any default changes.

**2026-07-26 — PERF: TerrainGrid per-game cache - the bot's move pause halved (2.2x decision
mean, 2.6x p95). Engine `5fcecb4`.** Chris: "noticeable pause before it moves". dotnet-trace on
a Hives-vs-Orks tactician smoke (seed 3000): ~HALF the game's busy CPU was TerrainGrid.Build -
rebuilt at least twice per activation (planner route grid + generator shared grid, plus deploy
lanes) though the grid depends only on terrain + base radius + Strider flag; the #268 dense
palettes made the old "built per query; measured cheap" note stale (its own comment asked for
profiler evidence before revisiting - this is it). New TerrainGridCache: ConditionalWeakTable
per table state (concurrent games never share), keyed (radius, flag, terrain count). Cold
single-game decision mean 45.7 -> 20.9ms, p95 315.7 -> 122.9ms, wall 18.8 -> 9.4s. Neutrality
PROVEN at dop 1: 3 matchups x 10 games (horde / caster / transport+ambush), old-vs-new
hash-equal (6267BEA2307042D2 / 16C0181B0279BAFB / 1EEF569455930F1D) + a bit-identical
GUID-normalized seed-3000 game log. DOP-16 hash comparison is NOT usable for this - same-code
DOP-16 runs flip 17/20 outcomes (filed under #210 with the dop-1-only verification practice;
also there: the first stash-verification attempt silently compared cache to cache after a
failed rebuild - caught, redone from a verified-old build). Suite 2168/2168 incl. 4 new
TerrainGridCacheTests.

**2026-07-26 — 200-GAME CONFIRMATION CELLS: THE FLOOR-CLEARING STORY DOES NOT SURVIVE G4
RESOLUTION.** All six cells completed, 0 faults, seeds 3000+, 200 games/cell, paired seeds
(sigma ~3.5/cell unpaired, less paired). Trio vs neutralized (`3c4924f~1`): RL-vs-Hives
60.8 vs 57.3 (+3.5), RL-vs-Orks 53.2 vs 60.5 (-7.3), RL-vs-HEF 69.3 vs 74.5 (-5.2). Two
findings. (1) The 50-game floor cells were NOISE: the neutralized engine's 49/49 on
RL-vs-Hives/RL-vs-Orks reads 57.3/60.5 at 200 games - both comfortably above the A-gate
line - so "the trio is what clears the floor" (previous entry) is RETRACTED; the trio's
case now rests on full-matrix parity (83.9 vs 84.3 at 3200), the behavioral pins, and
fault-freeness. (2) The trio reads net -9 across the three RL decision cells,
concentrated in RL-vs-Orks (-7.3, ~2 sigma) - a real watch item, not noise-shrugged.
The already-recorded candidate knobs (MoveRetaliation retune, sum-vs-max alternative
aggregation) plus the new posture/projection weights go to the automated tuning campaign
(Chris, 2026-07-26), whose cell set must include RL-vs-Orks and RL-vs-HEF. Process note:
a mid-run status check misread the still-running script as crashed and briefly restored
the submodule to master while its last two neutralized cells ran; both cells' outcomes
differ from the trio run's same-seed cells, which (determinism, G5) proves they ran
baseline code - the numbers stand.

**2026-07-26 — TRIO GATE (one-ply reply + arriving pressure + risk posture): MATRIX 83.9 /
MIRRORS 82.5, ZERO CELLS BELOW 50, ZERO FAULTS IN 3200 - AND THE ATTRIBUTION RUN SHOWS THE
TRIO IS WHAT CLEARS THE FLOOR.** Full ordered gate (trio-gate, hash `E5B567EFFDAF2A6F`,
seeds 3000, DOP 16): matrix 83.9, mirrors 82.5, worst cell RL-vs-Hives 51, faults 0/3200,
timeouts 0. Row avgs: HEF 92.4, Hives 90.5, Orks 90.5, BB 82.9, Dwarf 82.9, DE 81.0, HDF
79.0, RL 71.9. Because the old 83.9/84.4 reference predates the #256/#264 engine drift, a
NEUTRALIZED full gate was run on the same engine + seeds with the trio's three commits
checked out (trio-gate-neutralized, hash `D63814604A328DE4`): matrix 84.3, mirrors 82.5,
but TWO below-50 cells (RL-vs-Hives 49, RL-vs-Orks 49) and 1 fault (DE-vs-HEF seed-3010
watchdog timeout). Attribution verdict: the trio costs -0.45 matrix (noise), holds mirrors
exactly, LIFTS both floor cells over the 50 line (49/49 -> 51/54), and the run is fault-free
where the neutralized engine was not. RL-row watch item RESOLVED: 71.4 neutralized -> 71.9
trio (+0.5) - the drop from the old 77.6 reference is engine drift, not the trio;
RL-vs-HEF's -8 (68->60) is offset by +5/+2 in the same row and its G2 read (flipped seed
3016 decision replay) shows healthy marker play, no timidity signature. A-gate automated
criteria on the CURRENT engine: aggregate >= 70 PASS (83.9), no cell < 50 PASS (the
pre-trio engine FAILS this today), faults <= baseline PASS (0). Reports:
FdgLab/reports/trio-gate, trio-gate-neutralized.

**2026-07-26 — RISK POSTURE (idea 3, closing the approved trio; strategic-allocation (c)
from game 3) shipped. Engine `738a855`.** Posture = round-scaled projected-objective deficit
(best-placed opponent minus us, half a tilt per marker, clamped [-1,1]; early deficit is
deployment noise, late is the game), cached per activation. Behind: retaliation AND arriving
pressure discount by PostureRetaliationRelief (0.35 at full deficit) and the objective
delta + gradient boost by PostureObjectiveBoost (0.3, behind-only - being ahead is no reason
to stop playing markers). Ahead: retaliation prices UP the same slope - protect the lead,
run out the clock. 1-vs-3 late no longer scores like 3-vs-1. Pin
BehindOnObjectivesLate_ARiskyGrabPricesBetterThanWhenLevel (same guarded grab, two-down vs
level boards) verified failing pre-fix. Suite 2164/2164. **50-game probes (seed 3000, 0
faults), same 5 cells (slice-2 -> this, pre-trio baseline in parens): RL-vs-Hives 51->51
(50), RL-vs-Orks 49->54 (49), RL-vs-HEF 59->60 (68), Hives-vs-HEF 89->84 (86), BB-vs-Orks
80->73 (72) - noise-level shuffling, trio reads parity on these cells (sum 325->322). Full
ordered gate next; its row-level read arbitrates the trio and the RL-vs-HEF watch item.**

**2026-07-26 — ARRIVING PRESSURE (idea 2 of the approved trio) shipped. Engine `ec65f9a`.**
New MoveProjectedThreat (0.15) term: enemies the current retaliation term ignores entirely
(outside every this-round envelope) are projected one rush-budget step toward their nearest
attractive goal (a marker their side does not own, or one of our units - deterministic,
cached per activation) and the endpoint pays a low-weight forecast of their threat from
there. Only zero-current-threat enemies are priced (no double count), a cached max-range
precheck keeps the CombatMath cost off distant enemies, and projected MELEE pressure is
EXEMPT when our melee margin against the arriver is positive - a staged charge must not be
penalized for standing its ground (the A5-6 charging-beats-being-charged interaction).
2 pins - ArrivingPressure_PricesAnEnemyTwoMovesOut (verified failing pre-fix) and
ArrivingMeleePressure_IsAnOpportunityForAWillingBrawler (verified failing with the exemption
disabled; first fixture draft was too weak to discriminate and was strengthened). Suite
2163/2163. **50-game probes (seed 3000, 0 faults), same 5 cells (slice-1 -> this, with the
pre-trio baseline in parens): RL-vs-Hives 47->51 (50), RL-vs-Orks 54->49 (49), RL-vs-HEF
63->59 (68), Hives-vs-HEF 82->89 (86), BB-vs-Orks 73->80 (72) - net +2.2/cell over slice 1;
the two target cases (elites camping in a horde's arrival path, melee flood vs gunline)
respond exactly as designed. WATCH: RL-vs-HEF has drifted 68->63->59 across the trio's two
slices (~1.3 sigma cumulative); G2 read of flipped seed 3016 shows NO degenerate behavior
(forward marker play, Warriors advance + shoot, no SeekCover spiral, loss is an objective
race 1-2) - full-gate row read decides whether it is real.**

**2026-07-26 — ONE-PLY OPPONENT REPLY shipped (Chris approved ideas 1-3 of the smartness
brainstorm; this is idea 1). Engine `3c4924f`.** Retaliation now prices each enemy's best
single reply instead of a headcount discount: the per-sharer dilution divisor
(1 + 0.5 x sharers) is replaced by an adversarial share - incoming x ours/(ours +
best-alternative-target-value), floored at RetaliationShareFloor (0.25). The alternative-
target value mirrors the incoming computation exactly (shooting at post-advance reach, melee
margin at half weight inside charge threat) over OTHER friendlies at their current positions,
cached per enemy per activation. Consequences: a juicy unit can no longer hide behind chaff
(same headcount, thin alternative -> near-full price), chaff pays little when a fatter target
shares the envelope, and the ledgered "dilution counts units, not their remaining volley
value" simplification is resolved. Pin Retaliation_PricesTheEnemysBestReply_NotAHeadcount-
Discount (same geometry + sharer count, fat vs worthless alternative must discriminate)
verified FAILING pre-fix; the old Retaliation_Dilutes pin stays green. Suite 2161/2161.
**50-game probes (seed 3000, 0 faults everywhere), against fix-NEUTRALIZED baselines rerun
on the CURRENT engine (the old row numbers predate the #256/#264 drift): RL-vs-Hives 50->47,
RL-vs-Orks 49->54, RL-vs-HEF 68->63, Hives-vs-HEF 86->82, BB-vs-Orks 72->73 - net -1.2/cell,
parity within noise (sigma of the 5-cell mean ~3). Behavioral instruments all hold: seed-7001
timidity replay stays fixed (Hive Warriors RushObjective x3 + Block, no sideways slide, Win),
Hives-vs-Gunline 100.0, RL-vs-Gunline 93.0.** Shipped on behavior + principle with the gate
after the other two approved slices as arbiter. WATCH ITEM: the softness concentrates where
the Tactician's own units are valuable vs shooty opponents (RL/Hives elite rows) - under the
reply model a valuable unit pays near-FULL price (old dilution gave it 0.67-0.4 by headcount),
so if the full gate shows elite-army softness the single-knob response is a MoveRetaliation
retune, or aggregating alternatives by SUM instead of MAX (proportional-pick model).

**2026-07-23 — D1 BASELINE RE-PINNED after #264 issue 6 (the solo skirt capped at +/-60 degrees,
was +/-100: past perpendicular a "skirt" is a retreat, and it was taken at the FULL rush budget).**
New 200-game outcome hashes, DOP 16, reproducible across duplicate runs, zero faults, zero
timeouts: builtin mirror `F82D5A91B0119955` (27/27 wins, 146 ties; previous `3674C906996F34CC` was
29/29/142), builtin vs builtin-basic `A7EEB33FD9CEFC6A` (36/25/139; previous `CE3DC8150005FF2C` was
40/25/135). The mirror staying perfectly symmetric is the sanity check on the change. Every hash
reference below this note refers to the OLD baseline. #264 also landed five other Tactician fixes
(route-distance objective gradient, gated reachable bonus, blocked-goal pathfinding, per-model route
joins + snake side selection, per-model move budgets with a resolver repair pass, route-aware
deployment lanes) - see [WorkItems/264](264-tactician-walled-unit-lateral-retreat.md).

**2026-07-22 — D1 BASELINE RE-PINNED after #256 (S1 measure-and-correct budgets, S2 friendly
re-aim, S4 corridor snake deliberately moved solo-bot movement).** New 200-game outcome hashes,
DOP 16, reproducible across duplicate runs, zero faults: builtin mirror `3674C906996F34CC`
(29/29 wins, 142 ties; previous `B05AA1D810364C6B` was 37/37/125), builtin vs builtin-basic
`CE3DC8150005FF2C` (40/25/135; previous `F4318EF0D91161F5`). The rerun also caught and fixed a
latent G3 gap (the solo resolver's stand-still early-outs bypassed validation - see #256's
2026-07-22 evening note; engine `f7b6d78`). Every hash reference below this note refers to the
OLD baseline.

**2026-07-11 — GARRISON RELEASE + FOCUS-FIRE DILUTION shipped (Chris: "I agree. Let's do
that." on the game-3 fork; the dilution fix was the standing recommendation from games 1-2).**
Two `TacticianPlanner.Score` changes, both in Ai/Tactician:
- *Garrison release:* the ObjectiveDelta -1 walk-away penalty now applies only while some
  living enemy can still reach the marker before game end (rounds left x max(rush, charge
  budget) + seizure radius, base-edge; aircraft excluded - they can never seize). Any living
  enemy OFF the battlefield (Ambush reserve, embarked cargo) conservatively keeps every
  marker contestable. Cached per activation (`MarkerContestable`).
- *Focus-fire dilution:* each enemy's priced retaliation divides by 1 +
  `RetaliationDilutionPerSharer` (0.5) x (OTHER friendlies inside its threat envelope,
  `ThreatRangeAgainst`-based, cached per activation). Half-weight, not uniform 1/N: the enemy
  picks its target adversarially. Applies to the melee-threat term too (a charger also picks
  one victim).
4 pin tests (SafeGarrison_Releases / GuardedGarrison_Holds / EnemyInReserve_KeepsGuarded /
Retaliation_Dilutes), each verified to FAIL with its fix reverted. Suite 1620/1620.
Behavioral verification, all three instruments:
- Game-3 save replay (analyze): Jetbikes' stay-on-marker (+0.05) falls 1st -> 13th; new top
  is EngageAtRange +0.72 toward the Elemental Strikers. Board verdicts stay sane.
- Seed-7001 decision replay (game-2 timidity repro): Hive Warriors' activations go
  RushObjective/SeekCoverFrom/Charge -> Charge/RushObjective/Charge/Block - the sideways
  slide is gone; still Win 3-0.
- Gunline probes: Hives 100.0 (=baseline), RL 97.0 (98.0 baseline; one win -> tie), 0 faults.
- Mirrors (8 x 50, Tactician vs SoloRules): avg 84.1 vs 84.4 at A5-9, no cell < 74, 0 faults.
  Per-cell: HEF 73->89 (the A5-9 dip resolves UP), Orks 70->74, HDF 72->75, BB 91->95,
  Hives 89->90, DG 92->90, DE 99->82, RL 89->78. The two drops were attributed: with both
  fixes NEUTRALIZED on the current engine DE=80/RL=76, so the fixes are +2 on both cells and
  the drops are engine drift landed since A5-9 (#204/#205/#206/#208 family) - exactly what
  the handoff's clean full re-gate rebaselines. Reports: FdgLab/reports/garrison-dilution-*,
  attribution-neutralized/.
Deliberate simplifications (recorded, not hidden): contest reach ignores terrain/pathing
(straight-line, over-estimates threat = conservative); dilution counts units, not their
remaining volley value; no losing-position urgency yet (strategic-allocation (c), still open).

**2026-07-11 — OPUS HANDOFF: remaining Phase A work, specced for execution (Chris is out of
Fable hours after today).** Ordered by value; (1) is the only A-gate blocker.
1. *Probe harness + hallway probe (A-GATE BLOCKER, plan 6.2 + A-gate line 345).* `FdgLab
   probes` is a scaffold that counts JSONs in `FdgLab/probes/` - neither harness nor scenarios
   exist. Build: each probe = a ScenarioCompiler JSON (see `Scenarios/README.md` +
   `example-shootout.json`) plus an expectation block (which unit activates, what the correct
   choice looks like - action name and/or endpoint predicate). Harness: load via
   ScenarioCompiler like `--make-scenario` does, build a Tactician registry
   (`AiProfileFactory.BuildRegistry`), run ONE decision through the planner
   (BeginActivation + ChooseAction + TakePlannedMove - the `FdgLab/Analyze.cs` code path is
   the template), score pass/fail, print a table. Hallway scenario: narrow impassible-terrain
   corridor, unit at the mouth, marker on the far side; PASS = the planned move enters/
   traverses the corridor. Note the A3 gate already proved a corridor-traversing CANDIDATE is
   emitted (generator-level test green) - the probe asserts the planner PICKS it.
2. *Remaining 5 probes (informational at A):* lane-block, last-round steal, focus-fire,
   charge-vs-shoot, buff-anticipation - specs in plan 6.2. Same harness; author JSONs.
3. *Post-#208 clean full gate:* rerun the A5-9 matrix + mirrors on the current engine (the
   #208 decline-invalid-triggered-moves fix killed the benchmark fault family) - baseline the
   garrison-release + dilution changes AND settle whether the HEF-row dip (89 -> 84.5, mirror
   73) was fault noise. Compare vs matrix 83.9 / mirrors 84.4 / no cell < 55.
4. *Nearest-fight fallback:* units with nothing scoring positive should drift toward the
   nearest live engagement instead of holding (observed as end-game passivity); small
   MacroAction/score facet, needs a pin test + mirror bench.
5. *Gunline polish (apparatus, not ladder):* spread claims across several safe objectives
   (today: all claimers converge on one), optional casting. Only worth it if Gunline probes
   become a standing gate.
NOT handed off (design-judgment or replan): focus-fire dilution tuning beyond the shipped
half-weight; Phase B kickoff/replan.
Also recorded (Chris, game-3 follow-up): movement scoring is COVER-BLIND - the offense term
prices shooting from the endpoint by distance only (`TacticianPlanner.Score` ->
`AttackContext` with no DefenderInCover from geometry), so a unit never shifts sideways for a
clear firing lane and never discounts shooting into cover; cover enters only at target-pick
time (RangedAttackResolver) and the defensive M7 SeekCover candidate. "Shift for a clear
lane" = new facet (needs LoS/cover ray checks per candidate endpoint, geometry exists in
`MacroActionGenerator.TryFindCoverGoal`); deferred, ranked below the shipped fixes.

**2026-07-11 — GAME 3 (Chris HEF vs Tactician HEF, mirror): impressions + save analysis
(HEFMirror_ShootersGuardedObjectiveTooMuch.fdgsave, late game).** Chris verbatim: "I won
handedly. Some bugs got in my way, but I focused on 3 of the objectives and purposefully
abandoned the most isolated one at the start of the game. Tactician put half its forces toward
that one, and left two of them guarding it. Smartly, it used shooters to do so, but even after
the objective was 100% safe, they still stayed there. I saw the deploy pattern early on and
knew I would almost definitely win." ... "I didn't see any particularly dumb moves, though,
other than over-committing, which I can imagine humans doing."
Save-dump diagnosis (fdglab analyze, first real use): late game, Chris owns 3 objectives to
the bot's 1; bot has 2 units left - Jetbike Protectors (3 models) parked ON its owned
objective, Retributors (10) nearby. The Jetbikes' table is the GARRISON LOCK in one screen:
stay-on-owned-objective +0.05, and every leave option -0.34 to -0.93. Two stacked causes:
(1) the leave-penalty (ObjectiveDelta -1 for stepping off an owned marker that only we hold)
applies even when NO enemy could reach the marker before game end - "100% safe" changes
nothing in the score; (2) once freed, forward moves are still negative because a lone unit
prices the FULL enemy volley at the end position (focus-fire dilution gap again) and there is
no losing-position urgency (1-vs-3 objectives scores identically to 3-vs-1). Deployment
over-commit (half the army toward the isolated objective Chris conceded) is the same family:
allocation is not proportional to expected contest. Strategic-allocation family recorded:
(a) deployment allocation, (b) garrison release when un-contestable, (c) score-aware urgency
when behind on objectives. (b) is cheap and targeted; (c) is Phase B/C anticipation territory
per the plan's "tactically sharp, strategically naive" A-phase character - over-committing is
exactly the naivete the phase boundary predicts, per Chris "I can imagine humans doing" it.

**2026-07-11 — ANALYSIS KIT (Chris: "make a tool to be better able to have headless games be
helpful for your analysis"; approved all three pieces).** Engine e7274d2, superproject b0952fb.
- `FdgLab analyze <save> [--unit substr] [--no-board]` - per-unit candidate-score table +
  ChooseAction verdict + a text board snapshot (objectives w/ projected owner, unit positions).
  Replaces the throwaway-NUnit-test workflow from the game-1/game-2 investigations.
- Decision-log sink: TacticianOptions.DecisionLog -> the planner narrates every Choose Action
  (winner + full scored candidate table, same format as analyze); GunlinePlanner narrates too.
  `smoke --log-decisions` (requires --dump-logs) interleaves "[ai N]" lines into the game log -
  a decision replay, not just an outcome log.
- Gunline profile (EAiProfile.Gunline, Ai/Gunline/): scripted human stand-in - hold the line
  and shoot, claim only objectives with no enemy within 18in, never charge or approach. Reuses
  Tactician deployment/target/wound micro; new IMovePlanSource seam shares the move executor.
  Known simplifications (fine for apparatus): no casting, no spreading across safe objectives,
  first-in-list activation order. 4 pin tests.
- Rebase note: engine master had grown 3 commits from a parallel session (#206 forced-charge
  Pass gate, #208 decline invalid optional triggered moves - the benchmark fault family! -
  #197 Teleport); rebased the kit on top, merged suite 1591/1591 green.
- Probes (50 games each, seeds 3000, 0 faults): Hives-vs-HEF(Gunline) 100.0, RL-vs-HEF(Gunline)
  98.0 (2 ties). A static line loses on objectives - the kit's value is BEHAVIORAL: the seed-7001
  decision replay reproduces the game-2 timidity signature headless (round-1 chaff SeekCoverFrom/
  FallBack against the held line; rounds 3-4 left-flank grunts still churning SeekCoverFrom at
  ~25in) - the focus-fire dilution fix now has an automated repro to iterate against. (One
  glitch: the first RL bench run exited 0 without writing its report; unreproduced, rerun clean.)

**2026-07-11 — GAME 2 (Chris HEF vs Tactician Hives, rematch): impressions + save analysis
(HEFvsAliensPart2.fdgsave, parked round 3).** Chris verbatim, at round 2: "only one unit on
the Alien side did the sideways move - the Hive Warriors. (The grunts in the bottom left
didn't move because they're shaken.) So it seems better but not fixed." Later: "I just saw
the Hive Guardians move right up to my Retributors, totally within charging range, and then
they didn't charge. That's also the second time this happened, I think, I wanna say the
Assault Grunts did this to my Elemental Strikers... in both cases, they are likely to lose
the fight... But it's okay to be sacrificial sometimes." Also: "in both cases, they're on
the objective."
Save-dump findings (temp score-dump test, same technique as game 1):
- NOT a charge-scoring bug: from the save state every adjacent unit picks Charge next
  activation, decisively (Hive Guardians 1.186 charge vs 0.805 hold; Assault Grunts x2 pick
  Charge at 0.662 and 0.816; ChooseAction returns "Charge" end-to-end). What Chris saw is the
  CHARGE-APPROACH LAG: charge and rush share the same budget, so a unit that ends its move
  "just within charging range" was by construction OUT of charge reach when it activated -
  the BudgetClipped M5 approach rushes to a ~1" gap and the contact charge comes next
  activation, after eating one point-blank volley. Inherent to the one-action ruleset,
  arguably correct play (staying at 13" never converts); the tarpit term then makes the
  follow-up charge a deliberate sacrifice, as designed.
- Hive Warriors (pure melee - 3x Razor Whip, Tough(3), no guns, parked in the corner):
  their round-2 lateral slide is the ledgered FOCUS-FIRE DILUTION gap in its purest form -
  a unit whose forward move buys zero offense this activation still gets charged the FULL
  expected enemy volley at the end position, so distance-keeping wins early. From the round-3
  save they now choose RushObjective toward (43,24) - urgency growing + geometry, so "better
  but not fixed" is exactly right. Queued fixes (Chris not yet asked): (1) dilution - scale
  priced retaliation by friendlies sharing the threat envelope; (2) nearest-fight fallback
  for melee units with no offense in reach. Do (1) first; it is the disease, (2) is a patch.

**2026-07-10 — A5-9: MATCHUP-AWARE DEPLOYMENT (Chris picked option 2; "no need to make it
mega perfect").** Two halves, new shared DeploymentMatchup helper (CombatMath at a nominal 12"
engagement range, ValueFraction units): (1) LANE CHOICE - deployment aims still use the
objective anchors + depth-by-range, but each lane is scored by the VISIBLE enemies roughly
opposite it (favorability = our value-out minus theirs, faded over 18" lateral); the override
fires only when a lane clearly beats the round-robin spread (edge > 0.05), so blind early
placements keep today's fan-out. (2) DEPLOY ORDER - "Choose Unit to Deploy" picks the LEAST
matchup-sensitive unit first (sensitivity = spread of OUTPUT-ONLY value across the enemy's
whole list - lists are open info; full favorability was wrong here, it marked fragile
generalists sensitive just because different enemies kill them differently), so counters
place late with more of the enemy layout visible. Pin tests: melter platform deploys into the
tank's lane not the horde's; blade chaff deploys before the melters. Suite 1571/1571.
Interactions noted: always-Ambush shrinks what deployment must solve (ambushers place round 2
at chosen spots); Scout/Infiltrate placements also route through the same deployment-shaped
aim and inherit lane scoring for free. **50-game probes (seed 3000, 0 faults): RL-vs-Hives
52 -> 67, RL-vs-Orks 50 -> 64 - the biggest single-slice lift since A5-3, exactly in the
Slow-army cells Chris's reasoning predicted ("they have to be intentional with their movements
from the start"); BB-vs-Orks 58 -> 61, HDF-vs-Hives 57 (noise). **Full gate
(a5-9-gate-ordered): matrix 83.9 / mirrors 84.4, best yet; NO CELL BELOW 55 (worst HDF-vs-
Hives 57, RL-vs-HEF 59); RL row 69.6 -> 77.6, HDF 74.0 -> 80.9; HEF row dipped 89.0 -> 84.5
(mirror 81 -> 73 - watch next gate, could be deployment-order interaction with caster armies);
faults 2/3200 (#208 signature). Session arc: matrix 79.2 -> 83.9, mirrors 77.4 -> 84.4, RL row
59.9 -> 77.6, worst cell 35 -> 57.**

**2026-07-10 — GAME-1 SAVE ANALYSIS (HEFDestroyingAliens_MeleeStayingBack.fdgsave, round 3)
+ RETUNE: MoveRetaliation 0.6 -> 0.45.** Loaded Chris's save and dumped every candidate score
for the stuck units - the numbers convict the retaliation term: Winged Grunts (fast, 10
models, objective 23" out) best moves were FallBack 0.059 / Hold 0.050 with RushObjective at
0.039; Hive Guardians topped on SeekCoverFrom 0.292; Hive Swarms all-negative except a 0.042
objective rush. Meanwhile engaged units were correct (Assault Grunts charge 0.547, Hive Lord
objective rush 0.682) - the pathology is specifically CROSSING INTO a gunline that holds its
line, which the solo benchmark opponent never does (it advances; Hives-vs-HEF benches 86 while
looking timid vs Chris). Retune 0.45: on the same save the three stuck units flip to forward
moves (Winged Grunts rush the marker 0.128, Swarms 0.100, Guardians approach the Combat Walker
fight 0.398). Suite 1570/1570. STILL OPEN (next slice candidates, do not lose): (a) the A5-8
deadline fade can zero the gradient for slow backfield units with nothing else pulling - should
degrade to nearest-fight approach, not freeze; (b) no focus-fire dilution - retaliation prices
every unit as if it alone eats the full volley, so hordes cannot price flooding; (c) A5-6
staging can stand off INSIDE enemy gun range vs sword-carrying shooters. Probes attached to
the retune commit. **Full gate (retal-045-gate-ordered): matrix 83.0 (best yet; was 81.8),
mirrors 83.2, no cell below 50 (worst: RL-vs-Orks exactly 50), faults 3/3200 all the #208
signature. The human-play-inspired retune also lifted the automated grid nearly everywhere
(Hives row 93.4, HEF 89.0, DE 87.8, Dwarf 85.1, HDF 74.0) - the timidity was costing games
against the solo bot too, just not enough to see without the save dump.**

**2026-07-10 — CHRIS'S HAND-PLAYED GAME 1 (HEF vs Tactician-as-Hives), live impressions
(verbatim):** "Into the second round, several of the alien hives' melee units haven't moved
much from the deployment zone. They deployed at the bottom. Oddly, the first turn, the assault
grunts, which had deployed further to the right, just moved straight laterally, not getting
close to anything worthwhile. It might be noted that I have a very shooty army, so maybe
they're scared, but that's not helpful." Screenshot: round 2/4, Hives backfield cluster
(Assault Grunts / Winged Grunts / Hive Guardians / Hive Swarms) still at the bottom edge.
Diagnosis hypotheses (in suspected order): (1) the A5-8 deadline fade turned into a GIVE-UP
mechanism - a slow backfield unit whose slack drops below -1 for every not-ours objective gets
ZERO gradient, and vs a gunline the melee-approach term is its only other pull; (2) approach
vs retaliation imbalance against a HUMAN gunline that holds its line - the solo benchmark
opponent ADVANCES into the horde, which masks the crossing problem (bench Hives-vs-HEF is 86);
one-step greedy pays margin x fraction-closed per step but charges 0.6 x the retaliation
increase, so hiding/lateral SeekCover/screen moves outscore crossing; (3) the A5-6 staging
line vs sword-carrying shooters (HEF Retributors have Energy Swords) can create a standoff
dead zone INSIDE the enemy's gun range: stage at their MeleeThreatReach + 1.5 while own charge
reach is symmetric -> hover at ~15.5" getting shot at 18-24". Fix candidates AFTER his games:
deadline fade should fall back to nearest-fight approach, not zero; a horde-crossing term
(retaliation is per-unit but alternating activations dilute focus fire across a flood);
staging slack rethink vs mixed gun+sword enemies. DO NOT tune mid-game - collect both games'
impressions first.

**2026-07-10 — A5-8b GATE: FIRST CLEAN ORDERED GRID - MATRIX 81.8, MIRRORS 82.8, NO CELL
BELOW 50.** a5-8b-gate-ordered (3200 games, seed 3000): matrix 79.2 -> 80.4 -> **81.8** across
the day's three gates; mirrors 77.4 -> **82.8** (best ever). **Zero cells below 50 for the
first time on the honest ordered grid** - worst cell RL-vs-Hives 51. Row averages: Hives 92.6,
HEF 86.8, DE 86.0, Orks 84.1, Dwarf 82.6, BB 79.8 (mirror 72 -> 96!), HDF 72.0, RL 70.2 (was
59.9 this morning). Faults 4/3200, all four the exact #208 triggered-move cohesion signature
(Nightmares/Warriors Combined) - rate 0.125% vs baseline 0.056%, same family, small-sample.
A-gate automated criteria: aggregate >= 70 PASS (81.8); no matchup < 50 PASS (first time);
faults-vs-baseline marginal (same family, rate wobble - flag for Chris). Remaining for the
A-gate: hallway probe (not built), deployment matchup awareness (design sketch for sign-off),
Chris plays >= 2 games (lobby button now exists). RL-row investigation (task #16) CLOSED -
root causes were the phantom shoot credit (A5-7) plus the A5-8/8b positional levers, not
UnitValue rule-blindness (that gap remains recorded but was not the collapse mechanism).

**2026-07-10 — A5-8 (Chris's third review pass, from the RL-row post-mortem): TARPIT CHARGES,
ALWAYS-AMBUSH, DEADLINE-AWARE OBJECTIVE GRADIENT, THREATENED-VALUE WARD PICK.** Four facets:
(1) Tarpit (Chris): a landed charge degrades the target's next volley (his correction: it does
NOT deny the activation - the target still shoots, with fewer guns and chargers in the way), so
charges earn ChargeTarpitPerWound (0.04) per expected wound of the target's ranged output (new
TacticalAnalysis.RangedOutputWounds). Makes Bot-Swarm-style chaff charge gunlines instead of
fleeing them; pin test verified failing at weight 0. (2) Always-Ambush (Chris): AmbushPolicy
now holds EVERYTHING with Ambush - the old melee-only + half-army cap left the Forge Spider
(24" gun) walking on at round 1 in all 20 dumped games; Ambush is free positioning, especially
for Slow armies. Arrival stays the engine default (round-2 YesNo, defaults to deploy).
(3) Deadline gradient (Chris: "RL must move toward objectives most of the game"): the
objective-approach gradient is now deadline-scaled PER OBJECTIVE - full 1.3 urgency when
rounds-to-reach (gap / rush speed) equals rounds remaining, decaying to the round baseline
with slack (fast units keep shooting and pop on late - his over-rush worry), zero when
unreachable even rushing every round (no futile marches; a marker 71" out is worth nothing).
The flip term keeps round-based urgency. (4) Ward re-key (Chris: "the Monolith needs
protection the LEAST"): ScreenLane picks the ward by threatened value (A5-4b exchange margin
vs the melee threat nearest each friendly, cargo-scaled) instead of raw UnitValue - the
Monolith topping the old pick with margin ~0 nulled the lane so nobody screened anyone;
M8/M9 emit lanes for the top-2 assets so the paying lane always has candidates. NOT in this
pass: deployment matchup awareness (design fork - options to be sketched for sign-off);
Deadly-vs-Tough recalibration (verified CombatMath already mirrors Deadly clump confinement -
overkill into chaff is lost in the estimate; no change needed); Flesh-Eaters Infiltrate aim
verified sane from traces (lands 1-3" from a marker). Suite 1566/1566. **50-game probes (seed
3000, 0 faults): RL-vs-Hives 42 -> 48, RL-vs-Orks 42 -> 53 (clears the 50 line), RL-vs-HEF 53
(held). Session total for the row: 36/36/35 -> 48/53/53 over solo-vs-solo baselines of
30/29/42.** Full ordered gate: a5-8-gate-ordered (numbers in a later entry).

**2026-07-10 — A5-8b: AMBUSH STRIKE AIM (Chris follow-up) + A6 LOBBY BOT SELECTION.**
(1) Ambush arrivals now aim BEHIND the best strike victim, not at a marker (Chris: "in real
games they'll always pop up right behind a unit that they'll do lots of damage to" - the
objective-first aim surprised him). TacticianPlaceObjectsResolver: per enemy unit, a landing
spot just over the rule clearance on the side away from their army mass; scored by best of
shoot-from-spot / charge-if-in-reach via CombatMath, minus the planner-style retaliation price;
strike taken when gross damage >= AmbushStrikeMinDamageValue (0.25) and net > 0, else the old
most-winnable-objective aim. Arrivals can't score the landing round, so the strike costs no
tempo. Pin test verified discriminating (bar at 99 -> falls back to marker). This un-defers the
A5-2 "dropping beside enemies is a search-level judgment" deferral. (2) A6 lobby: "Add AI
Player" is now two buttons - "Add Tactician Bot" / "Add DerpBot" (Chris's name for the legacy
solo bot); resume re-crew rows get Tactician/DerpBot buttons too. Plumbing: EAiProfile on
LobbyPlayerInfoFull + AddAiPlayer(profile) + SetSavedSlotPlayerType(..., profile) through
ILobbyViewModel/host/client, both launch sites dispatch through AiProfileFactory (the seam
built for exactly this); bots are listed as "Tactician Bot N" / "DerpBot N". Engine touch
outside Ai/Tactician (lobby layer) covered by Chris's explicit request. Suite 1567/1567.
**50-game probes (seed 3000, 0 faults): RL-vs-Hives 48 -> 50, RL-vs-Orks 53 -> 54, RL-vs-HEF
53 -> 58, Dwarf mirror 91 (44W-3L-3T; the strike aim is the ambush army's payoff). Session
total for the RL row: 36/36/35 -> 50/54/58.** The stale mid-A5-8 gate run was killed; the
definitive gate is a5-8b-gate-ordered.

**2026-07-10 — RL-ROW ROOT CAUSE: PHANTOM SHOOT CREDIT ON RUSH INTENTS (CanShootAfter keyed on
intent, executor on ActionType).** G2 log-read of the three sub-50 cells (10-game probes, seed
3000+, logs + #198 position traces): RL units walked INTO 24" gun range from round 2 on and
then never fired - Warriors (Combined), the 10-gauss firebase, shot 0-1 times per GAME; whole-
army shooting was 3-9 activations of ~30 (wounds dealt 6-18 vs opponents' 32-53). Instrumented
the planner (temporary intent logging, removed): Warriors picked **SeekCoverFrom three rounds
running**, Spider picked Escort/SeekCoverFrom - both intents are planned as EActionType.RUSH
(shot forfeited at the engine's advance-and-shoot gate) but `CanShootAfter` said they keep the
volley, so Score paid full shooting offense on top of the retaliation-dodging/screen credit.
Dodge-and-still-shoot priced as a free lunch = a gunline that seeks cover forever. Why RL is
hit worst: every unit is a shooter (phantom credit army-wide), it owns the pool's biggest
Escort magnet (760-pt Monolith), and the three killer opponents are pressure armies whose
charge threat makes retaliation-dodging moves score highest. Same defect family plausibly
behind the other two soft rows (BB 70.1, HDF 72.6 - the shooting armies). Fix: `CanShootAfter`
now keys on the ActionType the executor declares (Hold/Advance only). Pin test
ShooterWithATargetInRange_NeverPicksAMoveThatForfeitsItsShot (horde in range + in charge-
threat, cover in rush reach behind: buggy code rushes 8.5" and forfeits the volley) - verified
FAILING against the pre-fix code, green after. Suite 1563/1563. Behavior after fix (seed-3001
smoke): Warriors Hold+Shoot r3/r4, SeekCoverFrom gone from the picks. Casting untouched;
MoveToCast (Advance) keeps its credit. **50-game probes (seed 3000): vs Hives 36->42, vs Orks
36->42, vs HEF 35->53, all 0 faults. Context - solo-vs-solo baselines for the same cells: 30 /
29 / 42, so these matchups are intrinsically ~30% for RL and pre-fix the Tactician was BELOW
the dumb bot in the HEF cell; post-fix it lifts every cell +11..13 over solo. Post-fix log
read: shooting 9-12 activations/game (was 3-9), wounds dealt 29-43 (was 9-35); remaining
losses/ties are objective endgames (hordes camp/contest markers a Slow army cannot clear -
10-15 ties per 50 even solo-vs-solo, army character). The "no cell <50" criterion still fails
on Hives/Orks (~42) unless another lever lands or the criterion is judged against the
one-sidedness baseline - Chris's call.** Full ordered gate (a5-7-gate-ordered): **matrix 80.4
(was 79.2), mirrors 77.5, RL row 59.9 -> 63.6, below-50 cells down to two (RL-vs-Hives 44,
RL-vs-Orks 42; the HEF cell cleared at 53). Row deltas: Dwarf 79.5->83.8, Orks 83.6->85.1,
Hives 92.5->93.0, HEF 89.1->91.2; BB 70.1->69.2 and HDF 72.6->71.9 (noise-level). Faults
4/3200, ALL the #208 cohesion signature ("further than 1 inch from the closest model" at
DefinePathStage, mid-game), vs 1/3200 last run and baseline 1/1800 - same family, small-sample
Poisson wobble; none reproduce serially (consistent with #210 DOP sensitivity).**

**2026-07-10 — BENCH SHAPE FIXED (Chris caught it) + FIRST ORDERED-PAIRS GATE: 79.2% MATRIX,
BUT THE TRIANGLE WAS HIDING AN RL-ROW COLLAPSE.** Superproject `9ed0d1b`: pool benches now run
every ORDERED pair (64 matchups, 3200 games) - the old unordered triangle made profile A play
alphabetically-early armies far more often (Hives as the Tactician's side in 8 matchups, Robot
Legions in 1), skewing the aggregate toward its best armies; --triangle keeps the old shape for
historical comparison. **Ordered gate (a5-6-gate-ordered): matrix 79.2 (triangle said 81.1),
mirrors 77.4, faults 1/3200 (#208 family - better than baseline rate). Row averages: Hives
92.5, HEF 89.1, DE 86.6, Orks 83.6, Dwarf 79.5, HDF 72.6, BB 70.1, RL 59.9. THREE below-50
cells the triangle could never see, all Tactician-as-RL: vs Hives 36, vs HEF 35, vs Orks 36 -
RL playing into pressure armies collapses.** So the honest "no matchup < 50" criterion FAILS
again; the ordered grid is the reference going forward. Next session: G2 log-read the RL row
(hypothesis: same family as the soft HDF row - UnitValue is blind to special rules, and RL's
durability lives in rules like Regeneration/self-repair; also RL is slow, and the round-urgency
+ staging changes may interact badly with a slow army under pressure). Then hallway probe + A6
+ Chris's hand-played games.

**2026-07-10 — A5-6 SHIPPED (Chris's second review pass); GATE 77.2% MIRRORS / 81.1% MATRIX,
NO CELL BELOW 50, ZERO FAULTS - BEST MATRIX YET.** Engine `b626bea`. Six facets: (1)
charge-band staging - approach credit stops at the enemy's TRUE threat line (charge budget +
the 2" melee cylinder Chris flagged + 1.5" centroid slack; new TacticalAnalysis.MeleeThreatReach
used by approach, retaliation, and transport-danger checks alike) - charging beats being
charged; (2) boat-then-payload activation order (loaded transport +0.5 urgency, embarked cargo
-0.5); (3) emergency disembark when one enemy activation could take half the boat's remaining
wounds; (4) TacticianModelSelectionResolver - Takedown/single-model-spell picks snipe the
output model / rules-carrying (hero) model instead of solo's "Model 1"; (5) cargo-aware value
(TacticalAnalysis.UnitValueWithCargo) in ward selection and shooting targets; (6)
ShootThreatFactor 1.25x for targets that can charge us next activation. 6 pins; suite
1562/1562. **Gate (a5-6-gate): matrix 80.6 -> 81.1, mirrors 78.1 -> 77.2, no cell below 50
(floor: BB-vs-Orks 56), faults 0/1800. DE-vs-Orks 46 -> 70 across the A5-5/A5-6 passes.
Weakest remaining: HDF row (63-68) and RL mirror (69) - all comfortably clear.** Speed-
differential kiting was consciously NOT implemented: under alternating activations a "we are
faster" discount is unsound (they activate next); the charge-band staging is the sound version.
Report: FdgLab/reports/a5-6-gate. Remaining for the A-gate: hallway probe, A6 selection UX,
Chris's two hand-played games.

**2026-07-10 — A5-4b + A5-5 SHIPPED; GATE 78.1% MIRRORS / 80.6% MATRIX, NO CELL BELOW 50,
FAULTS = BASELINE - ALL AUTOMATED A-GATE CRITERIA PASS.** Engine `bcedbe4`. A5-4b (Chris's
review): ward threat = EXCHANGE MARGIN (a counter-blade powerhouse needs no screen; pinned) +
one-screen-per-lane (no dogpiles); his cases (a) weak-melee-threat and (d) late-objective-vs-
screen were already self-limiting (documented in code). A5-5: THE DE FIX - zero voluntary
disembarks existed in any DE log (cargo rode until the boat died and spilled out Shaken; the
fallback chain ended in Pass for embarked units). WantsDisembark: get out when a not-ours
marker or a winnable melee is within post-drop reach (6" placement + move/charge), keep riding
otherwise. Pinned both ways. 50-game probe DE-vs-Orks 46 -> 61.2 BEFORE the gate. **Gate
(a5-5-gate): matrix 77.4 -> 80.6, mirrors 79.4 -> 78.1, below-50 cells 1 -> 0 (DE-vs-Orks
cleared), faults 1/1800 = baseline v4 (#208 family). A-gate automated criteria: aggregate >= 70
PASS, no cell < 50 PASS, faults <= baseline PASS. Remaining: hallway probe, A6, Chris's two
hand-played games.** Report: FdgLab/reports/a5-5-gate. Next: A5-6 already code-complete
(Chris's second review pass - charge-band staging outside charge+2"-melee-cylinder threat
reach, boat-then-payload activation order, emergency disembark from doomed transports,
Takedown/single-model-spell sniping resolver, cargo-aware target/ward value,
shoot-what-threatens-you), gate to follow.

**2026-07-10 — A5-4 ANTI-HORDE PLAY SHIPPED (Chris-designed); GATE 79.4% MIRRORS / 77.4% MATRIX,
ZERO FAULTS - ONE CELL LEFT BELOW 50.** Engine `f-see-log` (A5-4 commit). Chris's design: screen
with expendable bodies (spent transports, the BB tank), shoot the horde before racing markers,
break mobs with concentrated fire. Implementation: (1) MoveScreen credits endpoints on the lane
between the biggest melee threat and our most valuable OTHER unit x the ward's threatened value
- the M8 Block / M9 Escort candidates existed all along, nothing paid them; deliberately NO
who-may-screen gate (retaliation prices each unit's own cost of absorbing the charge, so Tough
tanks and empty transports screen and casters do not). (2) MoraleBreakBonus 1.3x for volleys
expected to push a unit below HALF strength (the engine's own rout mechanic - break, don't
shave); needs CombatMath.ExpectedKillsFrom (public wrapper on the allocation mirror). (3)
ObjectiveUrgency scales the objective terms ~0.66 (round 1) -> 1.3 (final round). 3+1 pins;
suite 1554/1554. 50-game probes first: BB-vs-Orks 49 -> 61, DE-vs-Orks flat 48. **Gate
(a5-4-gate): mirror avg 79.1 -> 79.4, matrix 79.2 -> 77.4 (parity within #210 noise +
redistribution), faults 0/1800. Below-50 cells 2 -> 1: BB-vs-Orks 49 -> 58, BB-vs-HEF 37 -> 51;
remaining straggler DE-vs-Orks 46. Watch: BB-vs-RL 82 -> 63 (more conservative BB; still
comfortable).** Queued next (Chris review 2026-07-10): A5-4b screen tweaks - ward threat as
EXCHANGE MARGIN (a counter-blade powerhouse ward needs no screen) + one-screen-per-lane
(no dogpiled screens); DE disembark timing investigation (does cargo ever leave the boats
proactively, or only on spillout?); cargo-aware transport value; speed-differential kiting;
shoot-what-threatens-you. Chris's read on DE-vs-Orks: possibly a genuinely one-sided matchup,
but it should still beat the dumb bot (>50).

**2026-07-10 — A5-4 ANTI-HORDE SCORING PROBED AND REVERTED (negative result).** The 49%-cell
loss reading (BB/DE vs Orks): elite units take an early marker, hold a firing position, get
CAUGHT by the horde's melee elements in rounds 2-3 (BB s3005: APC eaten r2, a BB squad r3,
Battle Tank routed), and the horde's surplus bodies take every marker in round 4. Kiting
endpoints DO exist (EngageAtRange far-band aims can back away) - they lose the argmax. Two
scoring hypotheses probed on the cells' own 20 seeds: (a) soft-OR retaliation aggregation
(1 - prod(1-x)) + melee-threat factor 0.5 -> 0.75: BB 50, DE 45 (tie-heavy - near a horde every
endpoint saturates to "dangerous", differences flatten, the army turns passive); (b) max
aggregation + 0.75 factor alone: BB 47.5, DE 47.5 (DE's fast transports WANT to operate close -
pricier melee threat makes them shy). Neither beats shipped A5-3 (BB 50, DE 49-60 on the same
seeds); both reverted, no engine change. Takeaway: the anti-horde lever is BEHAVIORAL
(screening, focus-fire to break mobs, or true kite-cycles), not a constant nudge, and 20-game
probes are too noisy for weight deltas this small - use 50-100 games for any future retune.
The two cells sit at parity (49) and do not block practical play; candidates for the next
session alongside the hallway probe and A6.

**2026-07-10 — A5-3 OBJECTIVE GRADIENT SHIPPED; GATE 79.1% MIRRORS / 79.2% MATRIX - THROUGH THE
70% A-GATE AGGREGATE.** Engine `26eb326`. Mechanism (from G2 log-reading the a5-2-gate DE-vs-Orks
losses): ObjectiveDelta pays only ON the marker, so a unit two moves out had no reason to close -
shooter armies froze against hordes (offense 0 out of range, retaliation punishes proximity =>
Hold/Pass; DE units PASSED their round-4 activations while Orks walked onto the markers). The
melee-approach bug's exact twin, on the other win condition. Fix: ObjectiveApproach pays
MoveObjectiveApproach (0.4) x the fraction of the gap closed toward the nearest not-ours
objective; below MoveObjective (0.75) so arriving still dominates. 1 pin (shooter far from an
uncontested marker with a looming out-of-range horde must walk, not pass); suite 1551/1551.
20-game probe on the worst cell first: DE-vs-Orks 23 -> 60. **Gate (a5-3-gate, hash
63AC904B902B3D1D): mirror avg 61.9 -> 79.1, matrix 63.5 -> 79.2. Every mirror >= 64 (Hives 85,
BB 78, DE 90, Dwarf 81, HEF 90, HDF 64, Orks 78, RL 67). A-gate criteria: aggregate >= 70
PASSED; "no matchup < 50" NOT YET - BB-vs-Orks 49.0 and DE-vs-Orks 49.0 (one game each);
faults 2/1800 vs baseline 1, but ZERO Tactician-attributable: one is #208 (triggered-move
cohesion, baseline family), one is NEW #211 - the SOLO mover pathing through impassible terrain
during its own activation (repro'd; solo-side, #159's family).** Remaining for the A-gate: the
two 49% cells (both "shooters/transport vs Ork horde" - the next lever is likely kiting /
focus-fire, not objectives), the hallway probe, A6 selection UX, and Chris's >= 2 hand-played
games. Report: FdgLab/reports/a5-3-gate.

**2026-07-10 — A5-2 AMBUSH/RESERVES SHIPPED; GATE 61.9% MIRRORS / 63.5% MATRIX, ZERO FAULTS -
DWARF MIRROR 66->84.** Engine `6e6f523`. Neither bot ever used Ambush (solo always answers
"Deploy normally"). Now: AmbushPolicy holds melee/short-range Ambushers (max weapon range <
18" - they skip the approach march; long-range units keep their round-1 shooting), capped at
half the army's living units so the table is never conceded; the hold prompt is answered
explicitly both ways (never "Back to unit list" - the deploy-picker loop). Arrivals aim at the
most WINNABLE objective (not-ours -> fewest enemies within 9" -> nearest table centre; the
engine's spiral search enforces the clearance); Scout placement ("Place Scout Unit") reuses the
objective-aware deployment aim. Arrival TIMING stays the engine default (first opportunity,
round 2) - deferring arrival is search-level judgment (Phase B); dropping beside enemies to set
up charges is a recorded deferral. 4 pins; suite 1550/1550. G2: Dwarfs hold Jetpack
Warriors/Miners, arrive round 2, seed 3050 flips to a win. **Gate (a5-2-gate, hash
BED656997B7235ED): mirror avg 56.3 -> 61.9, matrix 58.8 -> 63.5, faults 0/1800 (baseline v4:
1). Dwarf row transformed: mirror 66->84, vs Orks 29->63, vs HDF 54->64, vs HEF 53->65, vs RL
48->69. Hives row also up broadly (65-94); Orks mirror 44->51, RL mirror 36->49.** Solo pool
baseline v4 frozen: hash `64A59D65881C48A6`, 1 fault/1800 (#208 family; note #210 - DOP-16
hashes only approximately reproducible). Remaining weak cluster is now sharply defined:
Tactician-as-shooters/transports vs Ork horde (DE-vs-Orks 23, BB-vs-Orks 30, HDF-vs-Orks 33)
plus the HDF rows generally (mirror 45, vs RL 40) - anti-horde defense
(screening/kiting/focus-fire) and Tough/vehicle handling, not casting or reserves. A-gate
check: aggregate 63.5 vs the 70 target, 9 cells below 50. Next: G2 log-read the weak cluster
before choosing the next slice.

**2026-07-10 — A5-1 CASTING SHIPPED (engine `0b0c0f7`) + #209 DETERMINISM FIX (engine `52d1968`,
Chris-authorized); GATE 56.3% MIRRORS / 58.8% MATRIX, ZERO FAULTS - HEF MIRROR 66->77.** A5-1:
Cast is LAYERED (loops back to Choose Action without ending the activation), so the planner takes
any positive-EV cast FIRST - checked before the post-move branch too, which is what pays off M11
MoveToCast set-up moves. SpellValuation prices damage spells through a new
CombatMath.EstimateSpellDamage (fixed hits through the save/wound mirror; the stage's
hit-complete fold on spell hits - Blast multiply - is a recorded gap); non-damage effects get the
flat CastEffectStaticFraction placeholder (plan A5; real buff value arrives in C). Net EV = 0.5 x
target sum - tokens x CastTokenValue. Pickers are livelock-safe BY CONSTRUCTION: spell pick =
argmax over the ENGINE's offered labels, never Cancel (a cancelled pick re-enters Choose Action
unspent); target pick never cancels before MinCount (same loop), stops adding targets when value
runs out after it. TacticianCastAssistResolver spends tokens when a 1/6 threshold shift beats
CastTokenValue, friend-boost and enemy-deny alike (solo always declines). G2 verified in logs:
spell picks, casts, and a +2 assist turning a 4+ into a 2+. 6+2 pins; suite 1546/1546. Deferred
(recorded, not silent): ability-effect choice + pre-attack ability menus (solo first-option),
single-model spell target pick (solo), granted-token buff read-back (existing evaluator gap).
**#209 (found during G2 verification): weapon-choice options were built by enumerating a
Weapon-keyed dictionary in identity-hash order - multi-weapon units swung/fired in RANDOM order,
so same-seed games did not replay (predates A5; hit the solo baseline too - two identical
10-game benches gave different hashes). Fixed at both stages (deterministic option order),
pinned by WeaponOrderDeterminismTests; serial runs now reproduce hashes exactly across
processes. Residual DOP>1 flips = #210 (contention race, trace-diff tooling added to bench).
Consequence: pre-fix gate hashes are historical one-shots; this gate is only loosely comparable
to A4b-2's because #209 changed both bots' weapon order in every multi-weapon game.**
**Gate (a5-1-gate, hash 53E1E8837F86AC8E): mirror avg 56.3 (was 57.1), matrix 58.8 (was 58.7),
faults 0/1800. A5 verify criterion (caster matchups improve or hold) PASSED: HEF mirror 66->77,
HEF-vs-HDF 73->79, HEF-vs-RL 82->86. Scattered moves elsewhere (DE-vs-Orks 33->23, RL mirror
45->36, Hives-vs-HDF 66->72) are consistent with the #209 baseline shift.** Solo pool baseline
v4 re-freeze pending (v3 hash is pre-#209). Report: FdgLab/reports/a5-1-gate. Next: A5-2
ambush/reserves - neither bot uses Ambush at all today (solo always answers "Deploy normally"),
so this is the Dwarf list's whole signature mechanic.

**2026-07-10 — A4b-2 OBJECTIVE PLACEMENT SHIPPED; GATE 57.1% MIRRORS / 58.7% MATRIX.** Engine
`dd0b1f1`. TacticianPlaceObjectiveResolver: zones are chosen AFTER objectives, so the
side-agnostic lever is cluster-vs-spread along X - an army whose model-count majority carries
>=18" guns clusters the markers around centre at MinSeparation steps (one firebase covers
them all); everyone else races them wide (+/-0.7 x half-width, first marker central). Z
reflects the existing-marker centroid through the band centre (solo's balancing idea,
deterministic - no RNG). Legality via public ObjectivePlacementValidator.Check on a 1" grid
sorted nearest-to-target, same as solo. 3 pins in TacticianObjectivePlacementTests; suite
1538/1538. **Gate: mirror avg 54.4 -> 57.1, matrix 54.4 -> 58.7, faults 1/1800 (= baseline,
#208 cohesion family). BB mirror recovered 42->50 (the A4b watch item), RL mirror 45 (still
soft). Six of eight mirrors >= 50; Hives rows dominant (60-94).** Weakest cells now
Tactician-as-shooters vs Orks horde: BB-vs-Orks 22, HDF-vs-Orks 36, Dwarf-vs-Orks 36 -
anti-horde defense (screening/focus-fire vs bodies), not obviously an A5 casting/reserve
gap; watch after A5, may need a weight pass. Dwarf rows + HEF-as-opponent rows remain A5
scope (ambush timing, casting). Report: FdgLab/reports/a4b2-gate (hash 05AE804C8A32F2EB).
Next: A5 casting/abilities/reserves.

**2026-07-10 — A4b DEPLOYMENT SHIPPED; GATE 54.4% MIRRORS / 54.4% MATRIX - FIRST GATE ABOVE
PARITY.** Engine `bb971b1`. Mechanism: the solo placement resolver's only strategy knob (the
preferred block centre) became a protected virtual seam - solo's fan-out is the unchanged base
implementation (pinned bit-identical by TacticianDeploymentTests' disembark comparison + the
in-suite determinism hashes); TacticianPlaceObjectsResolver overrides it for DEPLOYMENT
requests only (TaskName discriminator "Place Unit Models"): units spread across objectives
nearest-to-zone-first, melee crowds the forward edge, shooters stand 6" back (12"-range units
3"). Non-deployment placements (disembark/spillout/ambush/reposition) ARE the solo resolver.
4 pins; suite 1535/1535. **Gate: mirror avg 49.0 -> 54.4, matrix 47.4 -> 54.4. HEF mirror
45->68, DE 61->77, Dwarf 44->58, Hives 43->54; regressions BB mirror 55->42 and RL 60->46
(static gunlines may dislike clustered deploys - watch after A4b-2/A5, retune depth if it
persists). Faults 2/1800, both #208's triggered-move cohesion family (baseline has 1) - no new
fault modes.** Scope note (not silent): cover-aware centre choice deferred to a later A4b
sub-slice; deployment ORDER (which unit next) and zone choice stay solo. Report:
FdgLab/reports/a4b-gate. Next: A4b-2 objective placement (side-agnostic: zones are chosen
AFTER objectives, so the profile lever is cluster-vs-spread, not own-side).

**2026-07-10 — #207 MOVE-THROUGH FLAVOR FIXED (Chris-authorized engine core) + A4-4 SHIPPED;
GATE 49.0% MIRRORS, ZERO FAULTS.** Engine fix (`ebd2c8f`): GetEnemyModelFootprints and
GetEnemyUnitsMovedThrough skip off-battlefield units - embarked models parked at (0,0) no
longer form an invisible wall at the table corner. Pinned by EnemyFootprintTests (embarked
cargo leaves no footprint; deployed enemies still obstacles). Verified: seed-3000 Hives-DE
fault repro now plays out (Hives win 2-0); 100-game Hives-DE matchup 0 faults (was 12/50),
Tactician 81%. **Solo pool baseline re-frozen: v3 hash `0888D6E37A1F11E8`** (v2
CC04AE4A5C713492 stale - the fix changes transport-game outcomes); 1 fault/1800 remains,
triggered-move cohesion = #208 family, NOT #207. A4-4 (`580e194`):
TacticianAssignWoundsResolver - the engine machinery already enforces every ordering rule and
TryAddWounds pours full capacity per pick, so the decision is fill ORDER; greedy min
output-lost-per-wound-absorbed (static weapon score attacks x AP factor; special rules not
weighed - recorded gap). Mixed units lose cheap bodies first; Tough models soak partial
volleys; AutoFill fallback so it can never fault (G3). 3 pins in
TacticianWoundAssignmentTests. Suite 1531/1531. **A4-4 gate (1800 games, seeds 3000+):
mirror avg 49.0% (was 47.1%), matrix 47.4% (was 45.9%), faults 0/1800 vs baseline 1/1800 -
fault criterion passed clean. HDF mirror 34->39, Dwarfs 37->44 (wound assignment helping the
Tough-heavy lists).** Weakest rows now: BB-vs-Orks 24, DE rows vs melee ~24-29, Dwarf rows
27-36 (ambush timing = A5 scope). Reports: FdgLab/reports/{207-fix-hives-de,
pool-baseline-v3, a4-4-gate}. Next: A4b deployment + objective placement.

**2026-07-10 — OPTION (a) SHIPPED: MELEE APPROACH TERM; THIRD GATE 47.1% MIRRORS (from 25.4%)
- collapse fixed, fault regression root-caused to engine core (awaiting Chris).** Chris picked
option (a). Three-part fix (engine `5dc976d`, all inside Ai/Tactician): (1) generator - an
out-of-charge-reach M5 candidate now emits a RUSH-budget approach move toward a 1.1"-standoff
point on the lane to the nearest enemy model (before: an unplayable charge-budget move that
ActionNameFor discarded, so melee units outside 12" had literally no candidate that closed
distance); (2) planner dispatch keys on ActionType - Charge-typed candidates map to Charge,
the Rush-typed approach plays as a plain Move; (3) Score adds `MoveApproach=0.75 x exchange
margin-if-reached x fraction-of-charge-gap-closed` (cached per enemy per activation), zeroed
once in reach so real charges still dominate; the reachable-charge offense branch now also
requires ActionType==Charge so a reached standoff point is not scored as a fight. Pinned by
MeleeUnitOutOfChargeReach_ApproachesInsteadOfStanding (brawlers 24" out must close >= 6").
Suite 1526/1526. **Gate (a4-approach-gate, 1800 games, seeds 3000+): mirror avg 47.1%, matrix
45.9%. Melee mirrors: Hives 7->47, Orks 5->38; shooters held (DE 62, BB 51, RL 60). Six of
eight mirrors within noise of parity or above.** Remaining below: HDF 34 (Tough/vehicles -
wound-assignment and target-saturation, A4-4 territory), Dwarfs 37 (ambush/scout timing = A5).
**Faults 17/1800 vs 9 baseline - REGRESSION, but root-caused to an ENGINE-CORE bug the
approach behavior merely tickles more often** (embarked models parked at (0,0) count as
movement obstacles at the table-origin corner; full writeup + candidate one-line fix in #207;
faulting moves are legal per the real rules). Engine fix is outside the authorized seam -
stopped and asked Chris. Reports in FdgLab/reports/a4-approach-gate.

**2026-07-10 (overnight) — SECOND A4 GATE FAILED (25.4%); STOPPED per plan sec. 13. Analysis for
Chris below; no further weight iterations without his direction.** Cumulative A4-2(retuned)+A4-3
gate, mirrors: Hives 7, Orks 5, Dwarfs 12, HEF 15, HDF 24, RL 26 - but **Dark Elf 62 and Battle
Brothers 52: the two SHOOTING armies WIN their mirrors.** That split is the mechanism, confirmed
by reading a Hives game (G2): an all-melee mirror produced only ~8 melee engagements in 4 rounds -
Tactician brawlers barely fight. Why: the greedy one-step score gives a melee unit outside charge
reach NO reason to approach (offense=0 beyond 12", every position near the enemy scores
-retaliation), so melee armies dither/kite while solo's Charge>Move priority marches in, wins the
attrition war, then takes the objectives. Shooting armies don't have this hole - their one-step
damage calculus is correct at range - and they beat solo. **This is the anticipation gap the plan
assigns to Phase B search (D6); greedy was always going to be weakest here.**
Options for Chris (recommendation first):
(a) RECOMMENDED - add an approach term for melee units: progress toward the best charge target
    scaled by the expected margin-if-reached (a one-line proxy for next-turn value; plan A4's
    'small terms' clause covers it). One more gate run decides it.
(b) Hybrid interim: Tactician planner defers to solo behavior for melee-only units, keeps its
    (winning) policy for shooters - ships a strictly-better-than-solo bot today, ugly but honest.
(c) Accept A4 as scaffolding and pull Phase B (search) forward - the failure is exactly what
    search fixes, but it leaves the A-gate unpassed.
Faults 9/1800 (Dark-Elf #207-family; profile attribution still TODO). Suite 1525/1525 throughout;
all code pushed (engine `8c17102`). Gates archived in FdgLab/reports/a4-2-gate + a4-3-gate.

**2026-07-10 (overnight) — A4-2 + A4-3 SHIPPED; A4-2 GATE FAILED (23.75%) -> weights retuned;
cumulative re-gate running.** A4-2: TacticianPlanner scores (action x macro-action) pairs at
Choose Action (value-weighted damage - retaliation + objective delta), caches the winner, plays
it out at the movement request with request-budget re-validation and solo fallback (G3). Perf
war: 508ms -> 68ms per decision (one lazy shared TerrainGrid per enumeration; straight-clear
paths skip the grid). A4-3: value-weighted shooting target choice (CombatMath EstimateVolley per
selectable weapon x target, kill bonus) + melee defender by exchange margin; ChooseMeleeDefenderRequest
split from the generic cancellable selection (A4-1 pattern; adapters keep CLI/GUI dialogs and
solo behavior identical - solo hashes stable). Suite 1525/1525.
**THE GATE LESSON (G2/G4 doing their job): A4-2's first gate scored 23.75% mirror average** -
Hives 4%, Dwarfs 7%, Orks 7% - a collapse, not a tuning miss. Root cause read from the numbers:
objective terms were FLAT bonuses (2.5 move / 2.0 activation) while damage/retaliation terms are
value-fractions (~0.0-0.5), so every unit rushed objectives (Rush = no shooting), never fought,
and solo's brawlers cleared them then took the table. Retune: objective terms onto the same scale
(0.75) - a flip outranks a good exchange, not ten. Cumulative A4-2+A4-3 re-gate running (seeds
3000+, timeout 240). Per plan sec. 13: if this second attempt also fails the gate, STOP and
present analysis to Chris (one weight iteration is spent). Also noted: 7 Dark-Elf-game faults in
the failed gate (#207-family signatures, 5x "moves through an enemy unit" new flavor) - needs
profile attribution (TODO in #207); Tactician games ~12.6s wall (thinking is real; G6 later).


**2026-07-10 — A4-1 GATE + post-#199 baseline recorded.** Baseline v2 (solo-vs-solo, fixed engine,
36x100, seeds 1000+): hash `CC04AE4A5C713492` - THE frozen solo reference now (v1's
`3AC9C6FA0B50D590` was pre-#199). A4-1 gate (tactician-vs-solo, 36x50, seeds 3000+): hash
`94AA56B0A094DAD0`. **Mirror average 52.75% for the Tactician** (Robot Legions 64, Hives 59,
Dwarfs 56, HDF 52, BB 51, HEF 50, Orks 48, Dark Elf 42; N=50 each, so single-mirror noise ~7pp,
average ~2.5pp) - the small positive nudge expected from activation order alone; movement is
still solo. Faults 4/1800: three #207-class (consolidation standoff/move-through, all Dark Elf
transport games) + one 120s watchdog on a Hives-DarkElf game (baseline showed legit 2k games
reach 103s - pool runs should use --timeout 240; noted). Cross-matchup rows mix army strength
with profile and are not read as profile signal. NEXT: A4-2 (action+movement onto the
MacroActionGenerator) - Chris authorized continuing overnight; then A4-3 (shooting/melee targets).


**2026-07-09 — A4-1 SHIPPED (activation order + request split); #199 FIXED; first pool baseline.**
A4-1: `ChooseUnitToActivateRequest` split out per Chris's call (type dispatch - which immediately
caught the string version matching Instructions vs the auto-generated TaskName: it would have
silently no-opped); `DerivedRequestAdapter` forwards to existing base-type resolvers in all three
sets, GUI canvas dialog unchanged (shared instance - Chris to eyeball next GUI session);
`TacticianRegistry` = own resolvers over a solo fallback; urgency scoring (value-weighted kill +
flip + threat, weights in TacticianWeights). A0 identity pin retired per its own instructions;
3 behavioral tests replace it. Solo hashes identical (split is behavior-neutral).
**#199 fixed** (Chris-authorized): the float-identity trio in AssignWoundsResults - guard compared
RemainingWoundsBinding against its own double-rounded round-trip; exact-equality finish check;
ULP residues as "room". WoundEpsilon (1e-4) + one capacity formula; four-seed graveyard pinned +
mutation-verified. Suite 1518/1518.
**First 2k pool baseline (solo-vs-solo, 36 matchups x 100, realistic dice, PRE-#199 build):**
hash `3AC9C6FA0B50D590`, 2.79 games/s (10k/hour), mean 5.7s/game, mirrors ~48-52%, real archetype
signal (Hives 80% over elite shooting, HEF casters 64% over Hives). 7 faults / 3600 (~0.2%) - NONE
were #199 (realistic dice): two NEW classes filed as **#207** (AI standoff-violating moves, Dark
Elf transport list, rect-base geometry suspected - kin of #206) and **#208** (#197's triggered
moves lack the G3 validate-or-decline ladder). Baseline + A4-1 gate re-running on the fixed build;
gate numbers land in the next entry.

**2026-07-09 — #200 + #203 FIXED (Chris-authorized engine-core changes); POOL 8/8 GREEN.**
#203 first (its verification needed #200's livelock alive): Task.Yield at the activation boundary
+ Choose Action entry - the livelock then idled to a clean watchdog Fault at DEFAULT stacks
instead of killing the process; bench hashes unchanged on both matrices (outcome-neutral). Then
#200: instrumentation of the bounce branch revealed the real state - the Orc Bikers' Rocket-Mod
is Limited+Deadly and SPENT, and Deadly-first gating ran before Limited-spent gating, so the
empty rocket locked out every other weapon while the Shoot gate (no Deadly gating) said
"fireable". Fix: gating order swapped AND gate/stage now share one pipeline (ApplyTargetGating)
so they can never disagree again; 2 regression pins. Orks mirror now plays 4 full rounds (3.5s).
**All 8 pool mirrors complete**; suite 1511/1511; builtin hashes stable (pool-army trajectories
legitimately shifted - wrongly-locked-out units now shoot). Both items archived. The pool is
ready for A4's first benchmark baseline. Also filed at Chris's request: #204 (save-roll beats
for Rending vs non-Rending groups pace too close together - presentation only).

**2026-07-09 — BENCHMARK POOL DELIVERED by Chris; 7/8 validated; #200/#203 filed off the 8th.**
Eight 2k armies now in `FdgLab/armies/` (moved out of the engine submodule per D3): Alien Hives
horde melee, Battle Brothers elite shooting, Dark Elf Raiders transport, Dwarf Guilds
ambush/scout, High Elf Fleets caster, HDF tough/vehicle, Orks horde mixed, Robot Legions mixed.
**Throughput (G6, measured):** 2k mirrors run 1.4-2.5s wall each, 200-420 decisions - barely
above the tiny test armies; the 5-15x slowdown fear was wrong; Phase C/D volumes are unthreatened.
7/8 play clean full-length mirrors with real objective scores. The Orks mirror exposed two
engine bugs, filed: **#200** (Choose Action offers Shoot with zero fireable targets ->
deterministic AI livelocks; GetCanShoot lacks the target gate GetCanCast already has) and
**#203** (stage transitions chain synchronously; stack depth grows with game length; the loop -
and eventually any long game - kills the process with an uncatchable StackOverflow;
DOTNET_DefaultStackSize=0x4000000 is the lab's interim shield). Both fixes are engine-core
(outside Ai/Tactician) -> awaiting Chris's go per D2. Pool baseline matrix + A4 start once #200
is resolved (or run 7-army in the interim).

**2026-07-09 — A3c-2 DONE; A3 COMPLETE (all of A3a/b/c verified).** M11 MoveToCast (spell-token
holders + army spells via TableState.Armies; goal just inside the best affordable spell's range
of its affinity target; Self-affinity skipped; LoS not modeled - recorded; one candidate per
activation) and M12 DeliverCargo (loaded transports - IsTransport + GetOccupants - route toward
the nearest unowned objective as the cargo-plan proxy) complete the confirmed vocabulary. Float-
margin bug fixed on the way: movers take the epsilon, validators keep the full budget (the
ResolverGuide gotcha, caught by the DeliverCargo test at exactly one ladder halving). **The A3
feasibility gate metric PASSES:** new FdgLab instrument (`probes --feasibility`) shadow-runs the
generator at every real movement decision of benchmark games (JSON-path interception; decision-
neutral - the solo bot still plays): builtin mirror 597/597 activations with a valid non-Hold
candidate, builtin-vs-builtin-basic 464/464 - **100% vs the >= 95% gate**, zero generator
faults. Suite 1509/1509 (4 M11/M12 tests added). Engine `6ad58b5`; lab instrument in the
superproject commit. **Next: A4 (greedy decision policy)** - replace delegated resolvers one
request type at a time, benchmark after each; needs Chris's 2k army pool for meaningful scores.

**2026-07-09 — A3c-1 (MacroActionGenerator, M1-M10) DONE.** `Ai/Tactician/MacroAction.cs` +
`MacroActionGenerator.cs`: goal enumeration per confirmed Appendix A - Hold (always), objective
advance/rush (both budgets), EngageAtRange with the three bands (SafeShooting/kite exists only
when own reach exceeds the enemy's threat envelope; endpoint may open the distance - verified),
ChargeToContact (solo-style explicit-end-gap construction when the lane is clear, path-planner
route otherwise; feasibility graded by ACHIEVED gap), FallBack, SeekCoverFrom (far side of the
nearest Cover piece), Block (LINE spread perpendicular to the LANE via the new lineAxis
parameter - the first draft spread across the approach and the test caught it), Escort
(interpose toward the ward's nearest threat), Concentrate. Every move is ladder-built (G3);
every candidate carries feasibility (Reachable/BudgetClipped/Blocked) + a G12 rationale string.
Diversity-preserving pruning: rank-by-feasibility within family, round-robin across families,
round 0 completes even past the budget (>=1 per family guaranteed - tested at budget 6). Two
planner fixes shaken out by the tests: ClampRepackStep pre-clamp in BuildPathCandidate (first
candidates were over-budget and the ladder halved real moves - Concentrate under-moved), and the
charge construction above. **Verified:** 10 tests incl. the GATING generator-level hallway probe
(objective beyond a 4" corridor -> traversing candidate emitted, >6" progress) and
every-emitted-move-passes-ValidatePaths. Suite 1505/1505; bench hash unchanged (B05AA1D810364C6B,
solo-rules untouched). **Sub-slice split (G7), recorded:** A3c-2 = M11 MoveToCast + M12
DeliverCargo (need casting/transport queries) + the benchmark-sampled >=95% feasibility metric
(shadow-generator instrument in FdgLab). Next after that: A4 greedy policy.

**2026-07-09 — APPENDIX A v2 CONFIRMED by Chris (the A3c gate, plan sec. 5). A3c is go.**
One edit folded in at his direction: mid-game MoveToEmbark cut from M12 (post-deployment
embarking almost never useful - seen once, transport had Flying; revival condition recorded in
the appendix: gate on transport mobility >> cargo mobility). Deploy-time embark stays.
Also decided with him: **benchmark pool = ~8 armies at 2,000 points, uniform** (his argument
carried: real games are 2k+, strategy differs with scale - objective spread vs concentration -
big games pass through small-force regimes as attrition bites, and low-point lists under-sample
novel units). Chris is building the armies now; suggested archetypes given (the sec. 6.1 six +
a transport list + a second-faction repeat). Throughput cost to be measured on the first real
2k army (G6). C-gate rider recorded: one held-out pair at a different point level probes
generalization across game size. Plan doc updated in the same commit (appendix header, M12
entry, sec. 5 trigger marked satisfied, sec. 6.1 pool spec). Bycatch this exchange: Army Forge
gained an editable points limit (was hard-coded 1000; superproject `00132d3`).

**2026-07-09 — A3b (grid pathfinding) DONE.** `Ai/Tactician/GridPathfinder.cs`: `TerrainGrid`
(1" cells over the table, blocked/difficult by degenerate swept-disc tests, inflated by base
radius - the validator's own Minkowski semantics), A* (8-connected, no corner cutting, octile
heuristic, difficult cells x2 as a route PREFERENCE - the rules-true 6" whole-move cap is applied
by the caller), string-pulled polylines, `AdvanceAlongPath` (arc-length walk reporting passed
waypoints + difficult crossings). `MovementPlanner.BuildPathCandidate` (all models share the
path's interior waypoints - the unit funnels through corridors - and fan into Grid/Line formation
at the endpoint; arc length is the ladder's backoff knob) and `PlanMoveToward` (grid -> path ->
difficult cap -> G3 ladder; straight-line fallback when unreachable or flying). Multi-leg
`ModelMoveEntry.Positions` carries the corridor legs, so the engine validates the true route.
**Verified:** 7 authored-terrain tests - straight-when-clear, routes-around-wall (no leg clips
impassible), THREADS THE 4" CORRIDOR (plan D5's canonical failure of angular skirting), sealed
goal -> null (infeasible, not wrong), mid-leg budget stop, difficult-route 6" cap end-to-end,
corridor composition passes MovementUtilities.ValidatePaths and gains >4" toward the goal. Suite
1479/1479; bench hash unchanged (B05AA1D810364C6B - solo-rules untouched, as intended: nothing
calls PlanMoveToward until A3c/A4). Perf note (G6): grid built per query, a few thousand point
tests - optimize only on profiler evidence. NEXT IS THE HARD GATE: Appendix A v2 confirmation
with Chris before A3c (plan sec. 5) - A3c must not start without it.

**2026-07-09 — A3a (MovementPlanner extraction) DONE.** `Ai/Tactician/MovementPlanner.cs`: the
solo-rules move-construction mechanics moved verbatim behind shared primitives - `BuildCandidate`
(single-step vs formation re-pack, with the step<=0 -> StayInPlace degenerate preserved exactly,
dead models' zero-length paths included), `RefineStepTowardGap` (measure-and-correct, 3
iterations), `ValidateWithBackoff` (the G3 ladder: halve to min step -> reform-in-place -> hold
exact), `StayInPlace`/`HoldExactPositions`/`LiveEnemyFootprints`/`MinEnemyGap`, tuning constants.
`AiDefineMovementResolver` keeps only policy (archetype, nearest-enemy targeting, terrain
skirting, difficult-terrain clamp) and delegates the mechanics. NEW: `PackLine` + the
`EFormation {Grid, Line}` flag (Appendix A M8's barrier shape; perpendicular-to-move by default),
with rank-wrap so a long line never breaks the 9" coherency rule.
**Pinned (D1):** the 8 AiDefineMovementResolver tests + 7 CohesiveFormation tests green unchanged;
suite 1472/1472 (+3 PackLine tests); and the decisive instrument - 200-game benchmark outcome
hashes on both matrices, captured fresh immediately before the refactor and re-run after:
builtin `B05AA1D810364C6B`, builtin-basic `F4318EF0D91161F5`, BOTH IDENTICAL pre/post (they also
still match the #198-era values, so #196/#197's parallel landings didn't shift these
trajectories either). Deferred, recorded: `AiConsolidationMoveResolver` still owns its own
consolidation logic - migrate onto the planner only if A4 needs consolidation policy (avoid
speculative churn). Next: A3b (grid pathfinding), then the HARD GATE - Appendix A confirmation
with Chris before A3c.

**2026-07-09 — A2 (TacticalAnalysis) DONE.** `Ai/Tactician/TacticalAnalysis.cs`: mobility queries
(Advance/Rush reuse `MovementRuleQueries`; `ChargeDistanceAgainst` composes the unit's charge
budget + the target-conditioned query exactly as DefinePathStage does - first draft wrongly fed
the BASE charge into the per-target query and the Fast test caught it); `ThreatRangeAgainst`
(max of advance+longest-effective-weapon-range and charge reach - the M4 kite band's input);
`ExpectedShootingAt` (CombatMath at a hypothetical distance/cover); objective projection
(`ProjectObjectives`/`ProjectedScore` mirroring ReconcileObjectivesStage: base-edge distance
within 3", sticky owner, contest-to-neutral, Shaken/reserve-arrival/Aircraft exclusions - the
radius + rules are a MIRROR of that stage's privates, noted in both files); `UnitValue` (runtime
units carry no point cost - UnitFileEntry.PointCost never reaches UnitData - so it is the plan's
f(wounds, quality, weapon output): sqrt(durability x (1+output)) vs a Q4/D4 reference).
**Verified:** 10 tests on authored states - base/Fast move+charge distances, threat ranges,
seize/contest/sticky/edge-distance/exclusion projection cases, value ordering on real HDF stat
lines (Infantry>Recruits, Storm Troopers>Veterans, Tank>all), value falls with casualties. Suite
1462/1462. **Honest calibration note:** the book prices Recruits (10 @ 75) BELOW GRUNT Robots
(5 @ 80) where the formula ranks them the other way - quality is weighted harder by the book than
by this v1; revisit only if A4's value-weighted targeting misreads benchmarks (G2). Special rules
deliberately don't contribute to UnitValue yet (recorded gap).

**2026-07-09 — A1 (CombatMath) DONE.** `Ai/Tactician/CombatMath.cs`: `EstimateShooting` (all
in-range weapon batches), `EstimateMelee` (impact hits, Counter strike-first swap - which also
strips the charger's IsCharging, exactly as the engine's role swap does - swings per weapon batch,
return strikes from survivors only, fatigue, Fear-adjusted resolution margin), `EstimateVolley`
(the pinned core). **Design refinement over the plan (G10 note added to sec. 8):** the "~15 named
rules" became *definition-driven* math - CombatMath mirrors the stages' arithmetic skeleton and
delegates all rule effects to the engine's own `RuleEvaluator.EvaluateAllNamed` (read-only: no
log spam, no one-shot-grant spending) with the same contexts/participants/sinks the stages use.
So the plan's candidates AND their ~hundreds of data-authored clone instances (Lacerate, Crack,
Shred variants, gated "when shooting/in melee" families...) all price themselves identically to
the engine, by construction. ("Poison" from the candidate list does not exist in the engine.)
**Verified:** 60 pin tests (`CombatMathPinTests`) drive the REAL stage chain per case and assert
|delta| <= max(0.05, 2%) - in practice exact: Q2-6 x D2-6 sweep, AP sweep, cover, Reliable,
Stealth both sides of 9", Shielded, Fortified (AP2+AP0), Rending+Regen, Crack, Regeneration,
Unstoppable, Bane, Lacerate, Shred, Surge, Relentless both ranges, Blast cap (big+small unit),
Deadly vs 1W and Tough(3), melee swing, Furious charge-gated both ways, Thrust, Fatigued-token
6s-only, plus composition tests (impact math, Counter flag + charge strip, Fear margin, survivor
return strikes, out-of-range = 0). Mutation-verified: naive Deadly multiply and skipped Bane
reroll each turn their pins red. Suite 1442/1442.
**Coverage table (what prices itself vs what does not):**
- Modeled (sink-folded at the 7 combat hooks): rollModifier, qualityFloor, addExtraHit,
  multiplyHits, perHitSaveModifier, reduceArmorPenetration, reroll(save), addExtraWound,
  multiplyWounds (clump-confined), ignoreWoundOnRoll, ignoreRule, ignoreCover, chargeImpactHits,
  reduceImpactDicePerModel, strikeFirst, extraMeleeWoundCount, setMaxWounds (via stats), fatigue.
- Modeled at runtime IF the caller passes the game's evaluator (token read-back needs its rule
  resolver): aura/addRule-granted rules. A bare evaluator prices static rules only.
- NOT priced (surfaced per-call via `AttackEstimate.Notes` where detectable): granted one-shot
  roll-modifier tokens (engine's only accessor consumes them - a Peek API is an engine-seam ask),
  target Mark claiming (mutates tokens), Takedown priced best-case vs healthiest model,
  per-volley casualty carry-over inside one attack, melee in-range subset (assumes all living
  carriers reach post pile-in), morale/movement/deployment/casting hooks (other slices' scope).
**Deferred, recorded:** book-wide generated attacker/defender matrix sweep (6.3's full form) -
the hand-built matrix covers every core combat rule; the sweep needs app-side book loading and
lands with the FdgLab probe tooling (A2+). Full-MeleeStage composition pin (PileIn geometry etc.)
- component math is stage-pinned; composition is covered by analytic tests.

**2026-07-09 — A0 (Tactician scaffold) DONE.** Phase A begun. Engine: `Ai/Tactician/`
(`TacticianOptions`, `TacticianResolverRegistryFactory` — A0 delegates every request wholesale to
the unmodified solo-rules resolvers), `EAiProfile { SoloRules, Tactician }` + `AiProfileFactory`
(the single profile->AI dispatch; moved the enum from FdgLab into the engine, per plan sec. 3).
App: `--ai-profile <solorules|tactician>` on the headless + `--scenario` paths (lobby selection
stays deferred to A6); FdgLab `smoke --profile-a/--profile-b`; `bench` per-side profile flags
deliberately deferred to A4 (first benchmark that needs them). Verified per plan A0: new
`TacticianScaffoldTests` (rich armies, seed 24601: Tactician game == solo-rules game, fingerprint
equality; plus self-reproducibility) — suite 1382/1382; seeded headless CLI transcripts
solo-vs-tactician byte-identical modulo per-run PlayerID GUIDs on BOTH a completing seed (42,
4 rounds) and a faulting one (5150); lab smoke tactician-vs-tactician matches solorules exactly.
Test-fixture refactor: shared `Tests/Doubles/TestArmies.cs` + `GameFingerprints.cs` extracted from
DeterminismTests (pure move). Bycatch: **#199 filed** (AutoFill faults on a ~0.0555 fractional
wound, deterministic at seed 31415, profile-independent) and a **deterministic #159 repro** (piped
headless seed 5150, noted in #159 — points at the CLI AutoAdvance as a submitter). Next: A1
CombatMath.

**2026-07-09 — #198 fixed same day; P3 (#194) gate now 3/3, all prerequisites COMPLETE.** Root cause
was a single unseeded `new System.Random()` in `PlaceTerrainStage`'s auto-layout thinning (Chris
called the terrain theory; the async-race suspicion was a red herring). Found via FdgLab's new
`GameTracer` (position-write trace interleaved with the log). Every determinism instrument now
agrees: 200-game bench hashes identical across runs on both army sets, rich-army engine test
(mutation-verified) pins it, seeded CLI runs byte-identical. **Phase B's replayable-rollout
prerequisite is met early** - the B0 spike no longer carries #198 risk. Also: zero #159 faults in
1,200 deterministic games (see #159 - old crash trajectories were fed by random zone terrain).
The ladder is clear: next is Phase A (A0 Tactician scaffold), per plan sec. 8.

**2026-07-09 — P3 (#194) shipped; gate 2/3.** FdgLab exists and works: 200-game seeded matrix in 38s
Debug (**5.25 games/s, ~450k games/day** at DOP 16 — comfortably above the plan's Phase C/D
assumptions), zero hangs, exactly symmetric mirror results, faults ~0.5-1% (all = #159, for which the
harness found an 8/10 seeded repro: `fdglab smoke --seed 1027 --repeat 10`). The harness's first real
catch is **#198**: seeded games are NOT run-to-run deterministic on rich army paths (movement paths
differ; ambush arrival flips) — #193 covered RNGs, but something timing- or identity-hash-ordered
remains. Consequences for the ladder: **Phase A can proceed** (win-rate statistics are unbiased noise;
the bench outcome hash simply won't match between runs yet), but **#198 must close before Phase B**
(search rollouts must replay exactly) — slot it with or before the B0 spike, which was already going
to stare at the same async-void plumbing. Baseline solo-rules-vs-solo-rules report archived in
`FdgLab/reports/` conventions; builtin mirror A-score 50.0% exact.

**2026-07-09 — P2 (#193) done, archived.** Determinism is now a tested engine invariant: same seed +
same build => identical game, and that holds with 16 games running concurrently in one process (the
cross-talk detector plan sec. 6.4 asked for). #194's benchmark can therefore trust its aggregates on
day one, which is why the order was swapped. Three things worth carrying into #194:
(1) **AI seeds key on slot ID, not PlayerID** — GUIDs are per-run; `GameRunner`'s `GameSpec` must pass
`(seed, slotID)` the same way, or seeded benchmarks silently drift.
(2) **Benchmark fingerprints must include objectives**, not just models. The solo-rules bot ignores
objectives, so a model-only comparison is blind to objective-placement nondeterminism (a mutation test
proved it). Same trap will apply to any FdgLab state hashing.
(3) **#195 filed and now fixed** (engine `a19e6ab`): resumed games played four MORE rounds instead of
finishing the four-round game. Resume is now round-count-correct, so Phase B's `SimulationService` and
the scenario probes can rely on it. Remaining prereq: #194.

**2026-07-09 — P1 (#192) done, archived.** Engine `9b1c0ba`. `GameResult` + `FDGServer.OnGameCompleted`
land the reward/benchmark signal the whole ladder depends on. Two findings worth carrying forward:
(1) the default headless game ends `Tie` with `scores=[0, 0]` because **all four objectives stay
neutral all game** — neither the CLI-EOF player nor the solo-rules bot ever moves within 3". That is
the baseline #191 exists to beat, and it means early benchmarks will be tie-heavy until Phase A's
objective awareness lands; the `score = wins + 0.5 * ties` metric (plan G4) already handles this, but
expect low signal from A0/A1 comparisons. (2) `EGameOutcome.Fault` is now emitted by the disconnect
and engine-fault paths, so #194's watchdog can distinguish a real tie from a broken game for free.
Remaining prereqs: #193, #194.

**2026-07-09 — Appendix A v2.** Chris reviewed the vocabulary and contributed seven plays:
bodyguard/escort, kite, mass (death ball), fatigue bait, block, move-to-cast, transport delivery.
Integrated as: new intents M9 Escort / M10 Concentrate / M11 MoveToCast / M12 DeliverCargo+
MoveToEmbark; kite folded into M4 as the SafeShooting band; ScreenLane generalized into M8
Block(e, asset); fatigue bait became the generator-wide *diversity-preserving pruning* rule
(sacrificial candidates must survive to be searched) rather than an intent. New implementation
flags: line-formation mode for the formation packer (A3a), fatigue in CombatMath features (A1) +
concentration features (C1), verify whether Cast permits same-activation movement (A5). v2
awaits Chris's confirmation of the refined form before A3c (see plan sec. 5).

**2026-07-09 — filed.** Plan authored during the Fable window from a three-agent codebase
exploration (existing AI map, engine interface assessment, special-rules variance) + hardware
check. Signed off: new-option-not-replacement, engine-side bot, in-repo FdgLab, Python+ONNX,
search-over-macro-actions with ML as evaluation. Next actions: (1) Chris edits Appendix A
vocabulary; (2) fresh-session dry-run review of the plan doc for ambiguity; (3) Chris curates
the benchmark army pool (~8 armies, archetype spread); (4) start #192/#193/#194.

## Outcome

(open)
