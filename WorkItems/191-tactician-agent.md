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
pin tests.

## Notes (newest first)

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
