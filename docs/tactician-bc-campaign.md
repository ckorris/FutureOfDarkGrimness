# Tactician B+C campaign - execution plan (search, then learned evaluation)

Authored 2026-09-03 (superproject `3dc41ee`, engine `f9356f8`), signed off by Chris the same day.
Design authority remains `docs/ai-agent-plan.md` (phases, invariants G1-G12, macro-action vocabulary);
this file is the **execution plan** for taking Phase B (MCTS) and Phase C (learned value function)
from "not started" to merged, mostly driven from Chris's phone over a multi-day unattended window
and then at a normal cadence. Running ledger: `WorkItems/191-tactician-agent.md` (newest on top).
When this file and the plan doc disagree, the plan doc wins on *design*, this file wins on *order,
branching, and model policy* - and the disagreement gets fixed in the same commit (G10).

---

## 0. Instructions to the implementing agent

1. Read `CLAUDE.md`, then `docs/ai-agent-plan.md` sections 0-6 and the section for the current
   phase, then this file. Then the ledger's top ~5 entries. Do not re-read the whole ledger.
2. **One step at a time**, in the order of section 4 unless a step's prerequisites say otherwise.
   Implement -> test -> verify -> commit (submodule first) -> dated ledger entry -> next step.
3. **Model/effort protocol (Chris's request).** Each step names a recommended model + effort. At
   the START of a step, compare with the session's current model. If they differ, say exactly one
   line before doing anything else, e.g.:
   `Step B1 recommends Opus 5 at high effort (currently Sonnet 5 / medium). Switch with /model, or say "continue" to proceed as-is.`
   Then wait. If Chris says continue, proceed and note the model actually used in the ledger
   entry. Never re-prompt inside the same step. Never block a compute-only step (benches, soaks,
   data generation) on a model switch - those run the same on any model; recommend switching
   DOWN for them.
4. **Unattended-window rule:** every work burst ends by leaving a compute job running or queued
   (data generation, soak, bench) so the machine is never idle if the session hits a usage limit.
   Report what is running and its expected finish time as the last line of every burst.
5. **Phone-check-in reply format:** <= 8 lines - what landed (commits), what is running, the one
   decision needed if any, and the next step. Detail goes in the ledger, not the chat.
6. Stop-and-ask triggers in the plan doc (section 5) still apply. Two were pre-authorized on
   2026-09-03 - see section 1. Ask in the phone format above.
7. Precision decays: steps 0-4 are implementable now; B1-B5 are implementable with B0's numbers in
   hand; C steps are re-detailed at the mandatory C replan (step 11). Do not build C4 before it.

---

## 1. Decisions made 2026-09-03 (Chris)

- **D7. Ladder order stands: B then C.** The "skip MCTS, go straight to a value net" option was
  weighed and rejected because of its downsides (no true afterstate without B1, one-ply cannot
  value sacrificial/anticipatory plays, search-free self-improvement loops collapse). C's
  infrastructure that does not depend on B (the C1 exporter) is pulled forward to fill idle
  compute during B's build - nothing else of C is built before the C replan.
- **D8. Generalization axes are first-class.** Every bot from B onward is built, benchmarked and
  (for C) trained across **point levels {1k, 2k, 3k, 4k}** and **player shapes {1v1, 2v2}**. 3v3,
  3v2 and free-for-all are explicitly NOT gated: side-aggregated features and a per-side reward
  make them the same input shape with more noise, and one player's share of the outcome is
  smaller. One 4-player FFA game per gate runs as a no-fault smoke only. Statistical power stays
  on the 2k 1v1 matrix; the other cells are non-regression **panels** (section 5).
- **D9. Base branch `tactician-bc`** in BOTH repos (superproject + engine submodule). Every step is
  committed to it (submodule-first cadence). Master receives merges only at the landmarks in
  section 2. Chris's other open branches are unaffected.
- **D10. Pre-authorized engine seams** (were stop-and-asks): (a) a minimal pause/step hook at
  `DeterminePlayerTurnStage` if B0 shows the resume path cannot run exactly one activation and
  stop cleanly - pin-tested, solo bot behavior untouched; (b) a `Team` field on FdgLab's `SlotSpec`
  and whatever `PlayerSlot` plumbing 2v2 lab games need (lab-side; the engine already supports
  teams via #257/#297).
- **D11. Model policy** (section 3): Sonnet 5 by default; Opus 5 where a design mistake costs a
  re-run or a re-slice; Fable 5.1 only for the handful of decisions that shape a whole phase.
  Token care is relative to impact: 50% better on something critical is worth Fable; 10% better
  on something small is not.

---

## 2. Branching and landmarks

```
master ----------------------------------------------L1------------------L2----
            \                                        /                   /
             tactician-bc ---B0--B1--...--B-gate----+---C1--...--C-gate--+
                 (engine submodule has the same branch; pointer advances on it)
```

- Work commits go to `tactician-bc` (engine first, then superproject pointer + app-side change).
- Merge `tactician-bc` -> `master` at each landmark; before merging, `git fetch origin`, inspect
  master's state, run the engine suite + full build + headless smoke on the merge result, and
  merge the engine branch to engine master FIRST (submodule-first applies to merges too).
- **L0 (optional, early, low-risk):** steps 0-2 - harness generalization + A's generalization
  baseline. Pure bench tooling; merging early keeps master's FdgLab current. Chris's call.
- **L1: B-gate passed** (step 10). The search bot ships behind `--ai-profile tactician-search`
  (name TBD at B5); A-greedy stays the default until L2 or Chris says otherwise.
- **L2: C-gate passed** (step 16). Learned evaluator promoted; both remain selectable (G4).
- Failed gate twice -> stop, analysis + options to Chris (plan sec. 13.4); no merge.

---

## 3. Model and effort policy

Verified 2026-09-03 for Chris's Max 5x plan: one shared weekly bucket; Fable is capped at 50% of it
and weighs ~2x Opus, ~5x Sonnet; a 5-hour rolling window sits under the weekly cap; only model
turns draw usage (benches and self-play on the box are free); subagents count. Practical rules:

| Use | Model / effort | Why |
|---|---|---|
| Slices with a clear spec, tests, ledger entries, Python scripts, bench runs | **Sonnet 5 / medium** | Most of the work; quality is set by the spec and the tests, not the model |
| Compute-only steps (benches, soaks, data generation), monitoring | **Sonnet 5 / low** | Nothing to reason about; recommend switching DOWN |
| Engine concurrency / lifecycle work (B1), UCT core (B4), C4 integration design, gate-failure analysis (G2 log reading with judgment) | **Opus 5 / high** | A wrong call here costs a re-run measured in box-days |
| B0 read-out + B1 design, B2 tree shape incl. multiplayer backup, the C replan, C-gate generalization failure analysis | **Fable 5.1 / high** | Four or five turns total in the campaign; each shapes a phase. Keep Fable well under its 50% weekly ceiling |
| Read-only exploration fan-outs | Sonnet subagents, sparingly | Subagents draw from the same bucket; prefer direct grep when the target is known |

Never use Haiku for anything that requires a judgment call about game behavior (G2).

---

## 4. Steps

Each step: goal, deliverables, verify, model/effort, box usage. "Ledger" = dated entry in #191.

### Step 0 - Branch + plan (DONE 2026-09-03, Sonnet)
`tactician-bc` in both repos; this file; plan-doc amendment; ledger note.

### Step 1 - Harness generalization (Sonnet / medium; ~1 burst)
- `SlotSpec.Team` (default: own team) threaded to `PlayerSlot(teamNumber)`; `GameSpec` helpers
  for 2v2. FFA N-slot already works.
- Pool manifest: `FdgLab/armies/pool.json` listing armies by point level, sourced from the
  existing `armies/` lists (1k/2k/3k/4k) copied into `FdgLab/armies/` with archetype labels.
  Record which lists are held out for C (section 5).
- `bench --panel <name>` running a named cell set (section 5) with the same report format;
  `bench` gains `--dop` and a `--pause-file` check between games (a data-gen driver and a bench
  must not fight for cores).
- Raise the watchdog default for search profiles later (B5); leave 120s now.
- Verify: 2v2 seeded game deterministic and hash-reproducible; a 4-slot FFA smoke exits with a
  `GameResult`; panel report renders. Ledger.

### Step 2 - A generalization baseline (Sonnet / low; box: a few hours)
Run every panel in section 5 with Tactician-vs-SoloRules and Tactician mirrors at 100 games/cell.
This is the number B must not regress and the first evidence of where A's 2k weights drift.
Read 2 logs per panel (G2). Ledger with the full table. **Do not tune weights on this** - that is
a separate decision for Chris.

### Step 3 - B0 spike (measurement: Sonnet / high; read-out + B1 design: Fable / high; ~1 burst + soak)
Per plan sec. 9 B0, on 2k AND 4k mid-game states (clone cost scales with unit count):
- `GameSaveSerializer.Save/Load` round-trip time and size.
- In-process resumed server from a snapshot (the `ScenarioLauncher.BuildResume` pattern) playing
  exactly one activation with scripted resolvers, capturing the next `DeterminePlayerTurnStage`
  snapshot. Answer (a) can we stop at the boundary, (b) can we abandon 10k simulation servers
  without cumulative leaks (memory trace over the soak).
- Fill the plan's decision table (< 30ms / 30-200ms / > 200ms per node expansion). If (a) fails,
  build the pre-authorized pause/step hook (D10a) as its own commit with pins.
- Fable turn: read the numbers, write B1's design into the ledger (what `SimulationService`
  owns, RNG per simulation, how a simulation is torn down, what B2's node cost budget is).

### Step 4 - C1-lite exporter + self-play driver (Sonnet / medium; schema review: Opus / high; ~1-2 bursts)
Built now because it is the only thing that uses the box during B1-B3, and its data is C's
bootstrap set. **The feature schema is a lock-in** (regenerating costs box-days), so it gets an
Opus review before the first long run.
- Engine `Ai/Tactician/Learning/PositionEncoder.cs`: at every activation boundary, for the acting
  player, (1) a global vector of **fractions and normalized quantities only** (wounds as fraction of
  side total, distances over table size and move budget, objectives as share of the count, expected
  firepower per point across defense bands from `CombatMath`, activation economy, casting
  resources, round/rounds-left), aggregated **per side** (own side, allied, enemy sum, enemy max) so
  1v1 and 2v2 share one shape; (2) a per-unit entity table (for a later v2 encoder) with the same
  normalization. Version the schema (`schema=1` in every file header).
- `fdglab selfplay --pool pool.json --mix mix.json --out FdgLab/data/<date>/ --dop 12 --pause-file`:
  samples (point level, shape, armies, profiles) from a mix (default: 70% Tactician mirror, 20%
  Tactician vs solo, 10% Tactician vs gunline; points and shapes weighted per section 5; held-out
  cells excluded), advancing seeds; one gzipped JSONL/CSV file per N games with provenance (both
  commits, profiles, seed range) in the header; labels joined at game end (win/tie/loss for the
  row's side, final objective differential, rounds). Crash-tolerant: restartable from the last
  complete file. No new C# dependencies; Python converts to parquet.
- Verify BEFORE any long run: a 10-minute sample loaded in pandas - row counts match decision
  counts, label balance sane, no feature outside its expected range, held-out cells absent,
  reproducible under a fixed seed. Ledger. Then launch and leave running.

### Step 5 - B1 SimulationService (Opus / high; ~2 bursts)
Engine `Ai/Tactician/Simulation/`: `Snapshot`, `Advance(snapshot, compositeDecision)`,
`Rollout(snapshot, policy, toEnd)`, per-simulation seeded RNG, probabilistic dice with sampled
decisive rolls. Built on B0's findings and the hook if it exists. Verify: unit tests on authored
states (2k and 4k), a 1k-simulation leak soak, determinism under seed. Ledger.

### Step 6 - B2 composite action space + multiplayer backup (design: Fable / high, one turn; build: Sonnet / medium)
- Node = activation boundary; edge = (unit, action, macro-action, primary target) from
  `MacroActionGenerator`; progressive widening by visit count.
- **Per-side reward vector; each node's acting player maximizes its OWN side's value (max^n);
  teammates share a reward.** 1v1 must reduce bit-identically to the two-player case (pin).
- Time budget scales with root branching (a 4k game gets more time, not a shallower tree), within
  a hard cap set by Chris for GUI play (plan: 5-10s vs humans, 1-2s in benches).
- Verify: generator-level tests for candidate counts at 1k/4k; multiplayer backup pins. Ledger.

### Step 7 - B3 rollouts (Sonnet / medium; ~1 burst)
A-greedy both sides to game end; reward win 1 / tie 0.5 / loss 0 + small objective-differential
shaping (lambda ~0.1, normalized by objective count so 1k and 4k boards shape alike); several
rollouts per leaf. Verify: rollout distributions on authored states; no reward-hacking signature
in 20 read logs (G2). Ledger.

### Step 8 - B4 UCT (Opus / high; ~1-2 bursts)
Time-budgeted UCT, root parallelism across cores, deterministic under a fixed seed, no
transposition table. Verify: seeded search reproducible; node-expansion cost matches B0's table;
500-game memory soak stable (box: hours). Ledger.

### Step 9 - B5 integration (Sonnet / medium; ~1 burst)
Search drives activation choice, action, movement, shooting target; minor requests stay
A-heuristic; feature flag / profile name so A-greedy and B-search are both benchmarkable (G4).
Watchdog raised for search profiles. G3 fallback to A-greedy on any search failure, logged and
counted. 100-game smoke cells at 2k 1v1 and 3k 2v2 (box: 1-2h each, data gen paused).
**R9 check:** first GUI game with search - the window stays live; add a "thinking..." line if
not present. Ledger.

### Step 10 - B-gate (runs: Sonnet / low; failure analysis: Opus / high; Chris games)
Section 5 gate. Iterate fix -> overnight bench. Chris plays >= 2 games ("does it anticipate?"),
verbatim in the ledger. Last-round-steal and charge-vs-shoot probes green (gating; the probe
harness from the 2026-07-11 handoff item 1 is built here if it still does not exist). **L1
merge.**

### Step 11 - C replan (Fable / high, one turn; mandatory)
Re-detail C1-C4 with: B's measured node costs, the exporter's data (how much, which cells),
A-baseline vs B-baseline panels, and the vocabulary review against observed B play (plan sec.
13.2). Output: updated plan-doc section 10 + this file's steps 12-16, in one commit.

### Step 12 - C1 finalize (Sonnet / medium)
Encoder v1 promoted from step 4 (or v2 entity encoder if the replan says so); extract the lobby's
random-army generator (`FdgRaylib/Rendering/LobbyScreen.cs`) into a callable the lab can use, so
the data mix can draw armies at ANY point level from the books instead of the fixed lists;
regenerate a B-play dataset at low volume alongside the A-play set.

### Step 13 - C2 training (Sonnet / medium; GPU: minutes-hours)
`FdgLab/python/` (uv-managed: torch, lightgbm, pandas, pyarrow, onnx, onnxruntime): LightGBM
baseline, then a small MLP. Hold out entire army pairs, one point level, and one shape (section
5). Loss curves are diagnostics only (G9).

### Step 14 - C3 ONNX evaluator (Sonnet / medium)
`IPositionEvaluator` seam engine-side (heuristic impl there); `OnnxPositionEvaluator` app/lab-side
via `TacticianOptions`; < 1ms CPU inference target; R7 verified on Linux.

### Step 15 - C4 integration (design: Opus / high; build: Sonnet / medium)
Leaf evaluation vs value-truncated rollouts, blend factor - decided on the benchmark. One data
regeneration from the best agent.

### Step 16 - C-gate (as step 10). **L2 merge.**

---

## 5. Gates and panels (supersedes the plan doc's single-matrix gates for B and C)

**Main matrix (statistical power):** the 8-army 2k 1v1 pool, ~12 pairs incl. mirrors, >= 100
seeds, side-swapped (plan 6.1). Score = wins + 0.5 ties.

**Panels (non-regression):** ~100 games/cell, side-swapped, paired seeds.
- `points-1k`: 4 cells from the 1k lists. `points-3k`: 4 cells. `points-4k`: 3 cells.
- `shape-2v2`: 4 cells at 2k per player (mixed archetypes per side), 2 cells at 3k per player.
- `ffa-smoke`: 1 four-player game per gate, must produce a `GameResult` with no fault.

**Held out for C (never in training data):** two 2k pairs (default: Dwarf-vs-HDF and DE-vs-HEF -
Chris may override), the whole `points-1k` panel's pairs, and one 2v2 cell. Recorded in
`pool.json`.

| Gate | Main matrix | Panels | Probes | Chris |
|---|---|---|---|---|
| B | >= 60% vs A, >= 85% vs solo | no panel cell below A's step-2 baseline minus 5 points; ffa-smoke clean; decision time within budget at 4k; memory stable over 500 games | last-round steal, charge-vs-shoot | >= 2 games, verbatim |
| C | >= 55% vs B | held-out pairs within ~5 points of trained pairs; held-out point level AND held-out shape each within ~5 points | lane-block, buff-anticipation | >= 2 games, verbatim |

Bench compute with search: a 2k 1v1 search game is ~150-300s, so the main matrix is ~12-24h at
DOP 16 and panels are ~2-3x that in total. Run the main matrix first, panels overnight after,
and never re-run the full set for a change that a 100-game cell can arbitrate.

---

## 6. Operating rules for the unattended window (2026-09-04 to 2026-09-07)

- Data generation runs at DOP 12 (leaves 4 threads for builds/tests); benches and soaks take the
  whole box - touch the pause file first, remove it after.
- Every burst ends with a job running or queued (section 0.4) and a one-line "running: X, ETA Y".
- Anything that would change the exporter's feature schema after the first long run is a
  stop-and-ask (it invalidates data).
- No GUI hand-verification is possible; anything needing it is listed in the ledger under
  "awaiting GUI hand-verify" for Chris's return, never silently skipped.
- If a usage limit hits mid-burst, the ledger entry is written BEFORE the last commit, not after -
  a half-landed slice with no note is the failure mode to avoid.

---

## 7. Rough calendar (from the 2026-09-03 estimate; re-estimate at each landmark)

| When | Steps |
|---|---|
| Sep 3 evening | 0, 1, 2 (panels run overnight), 3 (B0 soak runs overnight) |
| Sep 4 morning | 3 read-out + B1 design (Fable), 4 built and launched before Chris leaves |
| Sep 4-7 (phone) | 5, 6, 7, 8; 9 if time; first main-matrix bench queued for the night of Sep 7 |
| Week of Sep 8 | 10 (B-gate iterations, probes, Chris's games) -> L1; 11 (C replan) |
| Weeks of Sep 15-29 | 12-15 |
| ~early Oct | 16 -> L2 |

The phone cadence is what compresses B into the trip: each check-in is one 30-90 minute
autonomous burst on a well-specified step. If limits bite, the box keeps generating data and the
calendar slips by however many bursts were lost - nothing else breaks.
