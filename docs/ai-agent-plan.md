# Tactician — game-playing AI agent: master plan

Authored 2026-07-09 (superproject `334b58c`, engine `38c5aa5`), signed off by Chris the same day.
Umbrella work item: **#191** (`WorkItems/191-tactician-agent.md` is the running ledger; this file is
the design authority). Prerequisites: **#192 / #193 / #194**.
**Execution plan for Phases B+C (2026-09-03 onward): `docs/tactician-bc-campaign.md`** - branch
`tactician-bc`, step order, gates with generalization panels, model/effort policy. See the
2026-09-03 amendment (section 14) for the design deltas it introduced.

**Goal:** an AI opponent that gives a real challenge to human players, with any army against any
army, running on Chris's desktop (Threadripper 1950X 16C/32T, RTX 4070 Ti SUPER 16GB, 32GB RAM).
Built as a ladder — every phase is a shippable, benchmarkable bot, and every phase's output is the
next phase's infrastructure.

---

## 0. How to use this document (instructions to the implementing agent)

1. Read `CLAUDE.md`, then this file through section 6, then only the section for the current
   phase. Before touching resolvers/movement/deployment code, read `docs/ResolverGuide.md`;
   `docs/EngineNotes.md` is the engine map.
2. Work **one slice at a time** (house rule): implement -> test -> verify gate -> commit ->
   dated ledger entry in `WorkItems/191-tactician-agent.md`. Never batch slices.
3. Precision decays by design: Phases P and A are implementable specs; B is implementable with
   one mandatory spike (B0); C is design-level; D is a sketch. **Mandatory replan checkpoints**
   are listed in section 13 — do not treat the C/D sections as finished specs.
4. File/line claims below were verified 2026-07-09. Re-verify before relying on them; when the
   plan and reality disagree, reality wins — record the divergence in the ledger and patch the
   affected section of this doc in the same commit (G10).
5. Anything matching a **stop-and-ask trigger** (section 5) requires Chris's sign-off first.

---

## 1. Grounding facts (from the 2026-07-09 exploration)

**The seam.** The engine requests every player decision as an `IStageTaskRequest` resolved by an
`IStageResolver<TRequest, TResult>` in a `StageResolverRegistry`. There are exactly **16 request
types** (`FutureOfDarkGrimness/StageResolution/Requests/`; inventory table in
`docs/ResolverGuide.md`). The existing bot ("solo rules") is a third resolver set —
`FutureOfDarkGrimness/Ai/AiResolverRegistryFactory.cs` + `Players/ComputerPlayerController.cs` —
registered exactly like the CLI/GUI sets. A new agent is the same shape: registry in, decisions out.

**Existing bot character** (baseline to beat): legality-first heuristics. Fixed action priority
(Charge > Move > Shoot > Pass), move-toward-nearest-enemy, shoot-most-shooters-not-in-cover,
melee-the-weakest, **first-option-in-list for unit activation**, always declines casting/abilities,
`AutoFill()` wound assignment, **zero objective awareness** (although objectives alone decide the
winner). Its genuinely good part: validate-and-backoff ladders that never submit an illegal result
(`Ai/Resolvers/AiDefineMovementResolver.cs` etc.) — reuse these (G11).

**Observability.** `ITableState` exposes everything: positions, wounds, weapons, rule tokens,
`Progress.Scores` (per-player objective counts), `UnactivatedUnits`, objective `OwnerID`s. No
hidden information (reserve units sit at (0,0,0) by convention until placed).

**Speed.** A full 4-round headless game runs in ~1.4s wall *including* .NET startup; the
in-process template is `CliApp.RunAsync` (`FdgRaylib/Cli/CliApp.cs:51-106`): `LocalMessageBus` +
`GameDataStore`, one `FDGGame_AsLocal` per player with `AssignInterfaces(...)`, `PlayerSlot[]` +
`FDGServer`, await `OnGameEnded`. No built-in delays. Estimated parallel throughput on this
machine: order of 100k games/day.

**Dice.** `IDiceRoller.Roll` returns `IDiceResults` — a per-face **float histogram**.
`ERandomnessType.Probabilistic` resolves combat as exact expected values (fractional wounds flow
end-to-end — never int-lock a roll-derived value). `RealisticDiceRoller` is seedable via
`GameSettings.DiceSeed` (#167). Gaps (fixed in Phase P): `ProbabilisticDiceRoller.RollDecisive`
uses a **static unseeded** `Random` (morale tests, D3+2 objective count); no CLI `--seed`; several
AI resolvers own private unseeded `Random`s.

**Cloning.** `GameSaveSerializer.Save(GameDataStore) -> string` / `Load(string)` is an in-memory
deep clone of the whole game world (no file I/O; `StoreReplay.Rebuild` rewires everything).
`ScenarioCompiler` authors arbitrary mid-game states. Resume re-enters the state machine via the
`FDGServer` resume ctor (`GameModel/FDGServer.cs:74-100`) at **activation-boundary granularity**
(`GameProgressData`, written at `DeterminePlayerTurnStage`); `ScenarioLauncher.BuildResume`
(`FdgRaylib/Cli/ScenarioLauncher.cs:34-72`) is the ~40-line assembly template.

**Rules.** The decision-type surface is closed and small: rules almost always change *resolution*
(automatic sink math) or add entries to existing menus, not new decision kinds. 112 catalog
definitions + supplements; ~78% of book rule instances implemented; the rest are **silently
dropped at army load** (#168). The engine is the only ground truth — plan against what it does,
not what books say.

**Termination.** Fixed 4 rounds (`GameWideConstants.NUMBER_OF_ROUNDS`). `ReconcileObjectivesStage`
seizes/contests objectives each round end (3" radius); `VictoryCalculationStage` tallies owners.
The end signal is currently a **string only** (`OnGameEnded : Action<string>`) — fixed by #192.

---

## 2. Standing decisions (signed off by Chris, 2026-07-09)

D1. **Tactician is a new AI option, not a replacement.** The solo-rules bot is never modified
    behaviorally — it stays selectable, and it is the permanent benchmark baseline. (Refactors
    that share its machinery are allowed only with pin tests proving identical behavior.)
D2. **The bot ships in the engine submodule** (`FutureOfDarkGrimness/Ai/Tactician/`, peer of the
    existing `Ai/`). Engine modification is authorized for this effort **within** `Ai/Tactician/`
    plus the specific seams named in Phase P and B; anything else is a stop-and-ask.
    Submodule-first commit cadence applies throughout.
D3. **Training/benchmark infrastructure is a new console project `FdgLab/` in this superproject
    repo** — not a separate repo, not a new submodule. Rationale: the lab, the app, and the engine
    share one submodule pointer (no version skew); testing with the app is trivial because the bot
    lives in the engine; nothing lab-side ships with the engine because the engine repo is
    separate. Extract to its own repo later only if a real need appears.
D4. **ML stack: Python + PyTorch for training, ONNX for deployment.** Self-play data is exported
    from C# to files; training runs in Python (`FdgLab/python/`); trained nets export to ONNX and
    run in C# via ONNX Runtime. The engine takes **no ML dependency**: it defines
    `IPositionEvaluator`; the heuristic implementation is engine-side; `OnnxPositionEvaluator`
    lives app-side and is injected via `TacticianOptions`.
D5. **Search over macro-actions; geometry, never sampling.** Continuous decisions (movement,
    deployment) are converted to a small set of goal-directed candidates produced by pathfinding
    plus the engine's own legality machinery. Random position sampling is forbidden (it fails the
    narrow-hallway case by construction).
D6. **ML enters as evaluation, not as a raw action policy.** The learned component is a position
    value function (later plus a policy *prior* that ranks search candidates). Decisions are
    always made by search over the real engine. This is what makes "any army vs any army" work:
    the value function generalizes a scalar judgment; the engine handles the rules exactly.

---

## 3. Architecture

| Component | Where | Role |
|---|---|---|
| `TacticianResolverRegistryFactory` + `TacticianOptions` | engine `Ai/Tactician/` | Builds the Tactician's resolver registry; options carry evaluator, time budget, feature flags |
| `EAiProfile { SoloRules, Tactician }` | engine | AI selection, threaded through CLI/scenario flags (lobby UI in A6) |
| `CombatMath` | engine `Ai/Tactician/` | Closed-form expected-outcome estimates, pin-tested against engine resolution |
| `TacticalAnalysis` | engine `Ai/Tactician/` | Threat ranges, objective-control math, unit value model — shared queries |
| `MovementPlanner` + `MacroActionGenerator` | engine `Ai/Tactician/` | Pathfinding + goal enumeration -> validated candidate decisions (Appendix A vocabulary) |
| `IPositionEvaluator` (+ `HeuristicEvaluator`) | engine `Ai/Tactician/` | Position -> score seam; hand-crafted impl engine-side |
| `SimulationService` | engine `Ai/Tactician/Simulation/` | Snapshot / advance-one-activation / rollout, built on `GameSaveSerializer` + resume ctor |
| MCTS searcher | engine `Ai/Tactician/Search/` | UCT over composite activation decisions (Phase B) |
| `FdgLab` console app | superproject `FdgLab/` | `GameRunner` (in-process parallel games), benchmark matrix, strategy probes, self-play data exporter, `OnnxPositionEvaluator` |
| Training scripts | `FdgLab/python/` | PyTorch/LightGBM training, ONNX export |

Data flow (C onward): FdgLab self-play -> feature/outcome files -> Python training -> ONNX model
-> injected evaluator -> stronger self-play -> repeat.

---

## 4. Invariants and working guidelines

G1. **The engine is ground truth.** Any closed-form math (CombatMath) must have a pin test
    comparing it to actual engine probabilistic resolution; on mismatch, the engine wins.
    Never reimplement a rule's effect from the rulebook — mirror what the engine does.
G2. **Never trust a benchmark number without reading games.** Every gate run includes skimming
    at least 3 full game logs per direction and checking fault/timeout counts. A win caused by
    the opponent crashing is not a win; log-visible degenerate behavior (units oscillating,
    never casting, suicide charges) invalidates the number even if it is high.
G3. **Never fault a stage.** The Tactician must always submit a legal result. Every emitted
    result passes the engine validators first (reuse the validate-and-backoff ladders). If no
    macro-action candidate is feasible, fall back to delegating that single request to the
    solo-rules resolver and log the fallback with reason. Fallback rate is a tracked metric;
    an invalid submission is a P0 bug.
G4. **Benchmark discipline.** Fixed, seeded matchup matrix; side-swapped; N >= 200 games per
    comparison; score = wins + 0.5 * ties. Every gate records in the #191 ledger: superproject +
    engine commit hashes, seeds, per-matchup and aggregate rates, fault counts, decision-time
    stats. Comparisons always against *all* previous rungs (solo-rules, A, B, ...), which stay
    selectable behind flags forever.
G5. **Determinism.** Same seed + same build => identical game. All new randomness draws from the
    game's seeded source (`IGameContext.DiceRoller` or a `GameSettings`-owned seeded RNG). No
    `Random.Shared`, no time-based seeds, no static mutable RNG, no decision derived from
    unordered collection iteration.
G6. **Measure before optimizing.** Clone cost, node-expansion cost, inference latency: measured
    and recorded before any optimization work is scheduled.
G7. **Slices stay small.** If a slice exceeds ~2 working sessions or sprouts unplanned scope,
    stop, split it, record the split in the ledger. Never silently cut scope — deferred facets
    are written down at the moment of deferral.
G8. **House invariants** (repeated because they bite here): dice-derived values stay `float`
    (probabilistic mode); objectives decide winners, never unit counts; user-facing text is
    ASCII-only; engine tests green + full build before any commit; submodule-first cadence.
G9. **Training hygiene (C/D):** held-out army pairs never appear in training data; a trained
    evaluator is promoted only by beating the incumbent on the benchmark (G4), never on loss
    curves alone; training data provenance (generating agent + commit) is recorded per file.
G10. **Plan maintenance:** when reality diverges from this plan, update the affected section in
    the same commit as the divergence, with a dated note. Future sessions must be able to trust
    this document.
G11. **Prefer existing machinery** — `MovementUtilities.ValidatePaths`, `CohesiveFormation`,
    `ScenarioCompiler`, `GameSaveSerializer`, the dice rollers — over parallel reimplementations.
G12. **Explainability aid:** every Tactician decision logs a one-line ASCII rationale on the
    Debug channel (`intent=ChargeToContact target=U7 score=3.42 alternatives=5`). This is the
    primary debugging instrument for "why did it do that" and costs little.

---

## 5. Stop-and-ask-Chris triggers

- Engine changes outside `Ai/Tactician/` and the named P/B seams (#192 result object, #193
  seeding, B0's pause/step hook if needed).
- Any behavioral change to the solo-rules bot, the GUI, save format, or wire format.
- Changes to the macro-action vocabulary (Appendix A) — additions and removals both.
- New dependencies beyond the agreed set (C#: ONNX Runtime; Python: PyTorch, numpy, pandas,
  pyarrow, lightgbm/xgboost, onnx).
- Wanting to lower a gate threshold, or a gate failed after two distinct fix attempts.
- Any training run projected over ~12 hours.
- Curating or changing the benchmark army pool.
- **Starting slice A3c (goal enumeration):** SATISFIED 2026-07-09 — Chris confirmed Appendix A
  v2 with one edit (mid-game MoveToEmbark cut from M12; see the appendix entry). Recorded here,
  in the appendix header, and in the #191 ledger.

---

## 6. Verification instruments (built once in Phase P, used at every gate)

**6.1 Benchmark matrix.** An army pool of ~8 armies curated by Chris (stop-and-ask trigger),
spanning archetypes: horde melee, elite shooting, mixed arms, caster-heavy, tough/vehicle-heavy,
ambush/scout-heavy (+ suggested: a transport list, and a second-faction repeat of one archetype).
*Point level (decided with Chris 2026-07-09): 2,000 points, uniform across the pool* — matches
real play (usually 2k+), expresses full archetypes, and big games degrade through small-force
regimes anyway (the reverse is false). Throughput cost accepted; measure real game wall-time on
the first 2k army (G6) and record it. C-gate rider: hold out one army PAIR at a different point
level (e.g. 1k) to probe generalization across game SIZE. A fixed matchup list (~12 pairs
including mirrors) x >= 100 seeds x side swap.
Output: per-matchup and aggregate score, fault/timeout counts, decision-time distribution;
markdown + CSV under `FdgLab/reports/` (gitignored; gate summaries copied into the #191 ledger).

**6.2 Strategy probes.** Hand-authored scenarios (via `ScenarioCompiler`) with one known-best
decision, scored automatically ("did the agent choose it"). Initial probe set:
- *hallway*: unit at the mouth of a narrow terrain corridor; correct move traverses it.
- *lane-block*: an anti-tank unit can occupy the only approach a vehicle has to an objective
  (Chris's canonical example).
- *last-round steal*: a rush move at round 4 flips an objective; nothing else matters.
- *focus-fire*: concentrating shooting kills one unit; spreading kills none.
- *charge-vs-shoot*: expected melee outcome clearly better/worse than shooting, alternated.
- *buff-anticipation*: casting a defensive buff this turn is correct because of next turn's threat.
Probes are informational dashboards from Phase A onward; specific probes become gating at the
phase where their capability is claimed (marked per gate below).

**6.3 Math pin tests.** CombatMath vs actual engine probabilistic resolution over a generated
matrix of attacker/defender pairs drawn from the army books (including rule-carrying units).
Tolerance: |delta expected wounds| <= max(0.05, 2%). Unsupported rules are listed, not silently
wrong (the test asserts the supported set and reports coverage).

**6.4 Determinism tests.** Same-seed transcript equality (filter timestamps); same-seed equality
when run solo vs amid 16 concurrent games (cross-talk detector).

---

## 7. Phase P — prerequisites (work items #192, #193, #194)

Estimated effort: 3-6 sessions. All three are mechanical; suitable for any model.

**P1 = #192 — Structured game result.** Engine.
- `GameResult` record: `EGameOutcome { Win, Tie, Fault }`, `PlayerID? Winner`,
  per-player final scores, rounds played. Built by `VictoryCalculationStage`; surfaced as
  `FDGServer.OnGameCompleted : Action<GameResult>` alongside the existing string event (which
  stays for compatibility). Headless CLI prints one structured summary line.
- *Verify:* integration test (mirror the nearest `*RuleIntegrationTests`) asserting winner matches
  the objective tally, including tie and zero-objective cases; headless smoke exits 0 with the
  structured line.

**P2 = #193 — Determinism and seeding pass.** Engine + app.
- (a) `ProbabilisticDiceRoller`: replace the static `Random` behind `RollDecisive` with a
  per-instance seeded RNG driven by `GameSettings.DiceSeed`, mirroring `RealisticDiceRoller`
  (#167 plumbing). (b) Audit and seed every other RNG on the game path — known: the solo-rules
  `AiPlaceObjectiveResolver` (random X) and `AiPlaceOneTerrainResolver` (random template/rotation/
  position); route through `IGameContext.DiceRoller` or a `GameSettings`-owned seeded `Random`
  (behavior under a given seed may change; solo-rules *distributional* behavior must not — pin
  with existing AI tests). (c) CLI `--seed N` for headless and scenario paths. (d) No static
  mutable RNG remains anywhere in the engine (parallel-game safety).
- *Verify:* the 6.4 determinism tests, added to the suite.

**P3 = #194 — FdgLab project: GameRunner, benchmark, probes.** New app-side project.
- `FdgLab/FdgLab.csproj` console app referencing the engine project.
- `GameRunner.RunGame(GameSpec) -> GameRecord`: fully in-process game, both sides AI, per the
  `CliApp.RunAsync` template minus stdin. `GameSpec` = armies, seed, per-slot `EAiProfile`,
  randomness type, round count. `GameRecord` = `GameResult`, wall time, decision count/timing,
  optional per-round score trace, optional state snapshots (for C's data pipeline later).
- Watchdog: a game exceeding a time limit is cancelled and recorded as `Fault` — a hung resolver
  must never wedge the fleet. (Side benefit: the benchmark doubles as an engine fuzz harness;
  fault rates get tracked from day one.)
- Parallel runner with configurable degree of parallelism; benchmark command
  (`fdglab bench --pool <dir> --games N --out report.md`) implementing 6.1; probe command
  scaffold implementing 6.2 (probes themselves authored in Phase A+).
- *Verify:* 200-game solo-rules-vs-solo-rules matrix completes with zero hangs and plausibly
  symmetric results; identical re-run reproduces identical aggregates (G5); throughput and fault
  baseline recorded in the ledger.

**P-gate:** all three verified; the solo-rules-vs-solo-rules baseline report archived in the
ledger. Replan checkpoint: confirm measured throughput supports B/C assumptions (section 13).

---

## 8. Phase A — competent evaluation-driven agent (no search)

Estimated effort: 8-14 sessions. Target character: "tactically sharp, strategically naive" — it
does the math and plays the objectives, but doesn't yet anticipate.

**A0 — Tactician scaffold.** `Ai/Tactician/` + `TacticianResolverRegistryFactory.CreateController
(tableState, playerId, TacticianOptions)`, initially delegating *every* request to solo-rules
resolver instances. `EAiProfile` enum; `--ai-profile tactician` on headless + scenario paths
(lobby UI deferred to A6). *Verify:* a seeded headless Tactician-vs-Tactician game is
transcript-identical to solo-rules-vs-solo-rules under the same seed.

**A1 — CombatMath.** Expected wounds/kills for (attacking unit, weapon set) vs (target unit,
range/cover context): hit probability from Quality + modifiers, save from Defense + AP, plus the
~15 highest-frequency combat rules by book instance count (candidates: Rending, Blast(X),
Deadly(X), Furious, Reliable, Poison, Stealth, cover, Regeneration, Tough(X) spillover, Fear's
morale contribution; finalize the list from actual counts and record it). Melee variant includes
return strikes and fatigue. Everything unsupported is *listed* (6.3 reports coverage), not
approximated silently. *Verify:* 6.3 pin tests green; coverage + discrepancy table in the ledger.
*As built (2026-07-09, G10 note):* implemented definition-driven rather than name-keyed — the
core catalog and the data-authored supplement share one Condition x Effect vocabulary, so
CombatMath mirrors only the stages' arithmetic and delegates every rule effect to the engine's
own read-only evaluation (`RuleEvaluator.EvaluateAllNamed` + the stages' sinks). All named
candidates above are covered AND their clones price themselves for free; "Poison" does not exist
in the engine (no such catalog rule). Coverage/gaps recorded per the A1 ledger entry in #191.

**A2 — TacticalAnalysis.** Per-unit threat range (mobility + weapon range) and expected-damage-
at-range queries; objective-control math (who holds/contests within 3", projected at round end);
a unit value model (point cost when present, else f(wounds, quality, weapon output) — calibrate
roughly against book points). *Verify:* unit tests on authored scenario states.

**A3 — MovementPlanner + MacroActionGenerator.** The load-bearing slice; expect 2-3 sub-slices.
- A3a: extract the solo-rules movement machinery (gap targeting, `CohesiveFormation.PackGrid`
  formation packing, validate-with-`MovementUtilities.ValidatePaths`-and-backoff ladder) into
  shared `MovementPlanner` primitives. Solo-rules resolvers call the shared code; behavior pinned
  identical by the existing AI tests plus a seeded-transcript comparison (D1).
- A3b: pathfinding — coarse grid over the table (0.5-1" cells), cells blocked/costed by
  impassible/difficult terrain inflated by base radius; A*; path -> "advance along path up to
  budget" -> formation packing at the endpoint -> validation ladder.
- A3c: goal enumeration per **Appendix A** vocabulary; emits `MacroAction { intent, unit,
  target?, resolved request payloads }`; cap ~12 candidates per activation; Hold always included.
- *Verify:* per-goal unit tests; the **hallway probe passes at the generator level** (a
  corridor-traversing candidate is emitted); feasibility metric: on positions sampled from
  benchmark games, >= 95% of activations yield at least one valid non-Hold candidate.

**A4 — Greedy decision policy.** Replaces the delegated resolvers one request type at a time:
- Activation order: score urgency (threatened units, objective flips available, kill
  opportunities) instead of first-in-list.
- Action + movement choice: enumerate (action x macro-action) pairs; score =
  w1 * expected damage dealt (CombatMath, value-weighted) - w2 * expected retaliation next
  activation + w3 * objective-control delta (now and projected round end) + small terms
  (cover, range-band discipline). Weights are named constants in one file; tuning is benchmark-
  driven and recorded.
- Shooting target choice, melee defender choice: CombatMath value-weighted.
- Wound assignment: preserve output (choose casualties to keep the best weapons/models legal
  within engine constraints) instead of `AutoFill`.
- Deployment (A4b): use the generator inside the zone — deploy toward objectives/range bands,
  cover-aware; objective placement favoring own mobility/range profile.
- *Verify:* benchmark vs solo-rules after each replaced resolver; no regression in fault rate.

**A5 — Casting, abilities, reserves.** Enumerate spell/ability candidates (spell x valid target,
plus assist decisions): `dealHits` spells scored via CombatMath; buff/`addRule` spells get crude
static values (documented placeholder — honest note: *anticipatory* buff value arrives in Phase
C); cast-assist contributes when the expected value crosses a token-value heuristic; ambush/
reserve timing gets simple round/threat heuristics instead of "always deploy normally".
*Verify:* caster-heavy matchups improve or hold; logs show the agent actually casting (G2).

**A6 — Selection UX.** Lobby `AddAiPlayer` profile choice + flag polish. Small, last.

**A-gate:** aggregate >= 70% vs solo-rules across the matrix, no matchup below 50%, fault rate <=
baseline, hallway probe green (gating), other probes recorded. Chris plays >= 2 games and his
impressions go in the ledger verbatim.

---

## 9. Phase B — search: MCTS over composite activations

Estimated effort: 8-15 sessions. Opponent anticipation arrives here.

**B0 — Spike (mandatory, timeboxed ~1 session, pure measurement).**
- Measure `GameSaveSerializer.Save/Load` round-trip time and string size on mid-game states.
- Assemble a headless resumed server from a snapshot in-process (engine-side
  `SimulationService` built on the resume ctor, following the `ScenarioLauncher.BuildResume`
  pattern), auto-play one activation, capture the next activation-boundary snapshot; measure.
- **Top engineering risk lives here:** stopping/abandoning a simulated game cleanly. Engine stage
  transitions are fire-and-forget async void; there is no server stop API. The spike must answer:
  (a) can we run exactly one activation and capture state at the next `DeterminePlayerTurnStage`
  save-point; (b) can we abandon simulation servers without cumulative leaks (run 10k simulations,
  watch memory). If either fails: design a minimal pause/step hook at `DeterminePlayerTurnStage`
  (engine seam — stop-and-ask with the measurements in hand).
- Decision table from measured node-expansion cost: < 30ms -> full MCTS (hundreds of nodes per
  decision at a 5-10s budget); 30-200ms -> MCTS with small node counts leaning on the evaluator;
  > 200ms -> optimize the snapshot path before proceeding (binary serializer, partial clone —
  measure first, G6). Record everything in the ledger.

**B1 — SimulationService.** Engine, `Ai/Tactician/Simulation/`: `Snapshot(state)`,
`Advance(snapshot, compositeDecision) -> snapshot'` (scripted-resolver harness plays the
prescribed decision for the acting unit, then stops at the next boundary), and
`Rollout(snapshot, policy, toGameEnd) -> GameResult`. Per-simulation seeded RNG; probabilistic
dice for combat, sampled decisive rolls (chance nodes) resampled across rollouts.

**B2 — Composite action space.** Tree node = state at an activation boundary; edge = (unit to
activate, action, macro-action, primary target where applicable) built from MacroActionGenerator.
Branching estimate: ~5 units x ~8 actions ~ 40; progressive widening by visit count if larger.

**B3 — Rollout evaluation.** Playouts: both sides play the Phase-A greedy policy to game end.
Reward: win 1 / tie 0.5 / loss 0, plus a *small* objective-differential shaping term (lambda ~0.1,
tunable — beware reward hacking, G2). Average multiple rollouts per leaf for morale variance.

**B4 — UCT search.** Time-budgeted (config: 1-2s in benchmarks, 5-10s vs humans); root
parallelism across cores (simplest sound option); deterministic under a fixed seed for tests; no
transposition table (states too large — revisit only with evidence).

**B5 — Integration.** Search drives the major decisions (activation choice, action, movement,
shooting target); minor requests stay Phase-A heuristic. Feature flag selects A-greedy vs
B-search so both remain benchmarkable (G4).

**B-gate:** >= 60% vs Phase A; >= 85% vs solo-rules; decision time within budget; memory stable
over a 500-game run; *last-round steal* and *charge-vs-shoot* probes green (gating). Chris plays:
"does it feel like it anticipates?" — ledger verbatim. **Then the mandatory C replan (sec. 13).**

---

## 10. Phase C — learned value function (design level; re-detail at the B replan)

Estimated effort: 10-20 sessions including training iteration. First phase with genuine research
risk; the fallback (Phase B with the hand-crafted evaluator) remains a shipped, strong bot.

**C1 — PositionEncoder + data pipeline.** Army-agnostic features at activation boundaries.
- v1: global summary vector (~200 floats): per player — objective control counts/margins,
  min/mean distance to each objective, remaining-wounds fraction, expected firepower vs defense
  bands 2+..6+ (from CombatMath — this is what makes features army-agnostic), mobility
  aggregates, activation economy, casting resources; plus round number and acting-player context.
- v2 (only if v1 plateaus): per-unit entity vectors + attention/DeepSets pooling in PyTorch.
- FdgLab exporter: self-play games emit (features, final result) pairs to parquet; provenance
  (generating agent, commits, seed) in the file metadata (G9).

**C2 — Training (Python).** Baseline: LightGBM/XGBoost regression on final objective
differential + a win-probability classification head; then a small MLP (2-4 layers, 64-256
units — tiny on purpose; CPU-inferable). Split discipline: hold out entire army *pairs*, plus
seed-splits within pairs. Loss curves are diagnostics; the benchmark is the only promotion
criterion (G9).

**C3 — ONNX export + `OnnxPositionEvaluator`.** Implements the `IPositionEvaluator` seam;
lives in FdgLab first, ships as an FdgRaylib asset when promoted. Inference target < 1ms CPU;
batch leaf evaluations if profiling says so (G6).

**C4 — Integration.** Value-truncated rollouts (roll k activations with the greedy policy, then
evaluate) or pure leaf evaluation — decide empirically; blend factor between rollout result and
net evaluation tuned on the benchmark. Regenerate training data from the current best agent once
(a single improvement iteration; the full loop is Phase D).

**C-gate:** >= 55% vs Phase B; held-out army-pair performance within ~5 points of trained pairs
(the generalization requirement — this gate is the "any army" promise); *lane-block* and
*buff-anticipation* probes green (gating). Chris feel test, ledger verbatim.

---

## 11. Phase D — self-improvement loop (sketch only; full replan required first)

AlphaZero-shaped generation loop: self-play with search + root exploration noise -> record
(state, search visit distribution over macro-actions, outcome) -> retrain value net + a **policy
prior** over macro-action candidates (the prior guides UCT selection and prunes candidate
generation) -> promotion gate: new generation plays incumbent, promoted at >= 55% -> repeat.
League play (incumbent + past generations + solo-rules + Phase-A) resists self-play collapse.
Compute envelope: ~100k games/day => a 20-50k-game generation is ~0.5-2 days; plan for tens of
generations, not thousands. **Do not pre-build D infrastructure during earlier phases** (house
design principle: grow vocabulary on demand).

---

## 12. Known risks (with mitigations)

- **R1 — Simulation stop/abandon** (B0): the top engineering unknown; mitigated by the timeboxed
  spike and, if needed, a minimal engine pause hook (stop-and-ask).
- **R2 — Clone throughput**: measured at B0 before any B commitment; optimization only on
  evidence.
- **R3 — CombatMath drift** as engine rules evolve: pin tests (6.3) run in the suite forever.
- **R4 — Unimplemented book rules** (~22% of instances): the agent plays the real engine, so it
  is *consistent*; but armies leaning on unimplemented rules are mispriced relative to their book
  intent. #168 (surface load diagnostics) helps humans notice; not this effort's problem to fix.
- **R5 — Reward hacking / self-play collapse** (C/D): small shaping terms, G2 replay reading,
  league play, promotion gates.
- **R6 — Benchmark overfitting** to the army pool: held-out pairs (C-gate) + occasional pool
  refresh (stop-and-ask).
- **R7 — ONNX Runtime on Linux**: expected trivial at these net sizes (CPU inference); verify in
  C3 before depending on it.
- **R8 — Unknown shared statics** beyond RNG breaking parallel games: P3's cross-talk determinism
  test; any hit is a P0.
- **R9 — GUI/timing coupling**: the Tactician thinks for seconds in GUI games; resolver `Resolve()`
  runs on the engine thread (not the render thread) so the window stays live, but verify the
  first time search runs under the GUI, and surface a "thinking..." indication (small app-side
  task when it comes up).

---

## 13. Replan checkpoints (mandatory)

1. **End of P:** confirm measured throughput and fault baseline support the B/C assumptions
   written here; adjust estimates.
2. **End of B:** re-detail Phase C with measured node costs and real benchmark states; review the
   macro-action vocabulary against observed play (with Chris).
3. **Start of D:** full replan from C's results; explicitly decide whether D is worth it versus
   polishing C. D as written is a direction, not a commitment.
4. **Any gate failed twice:** stop; present analysis + options + recommendation to Chris.

---

## 14. Amendment 2026-09-03 (Chris, signed off) - generalization axes, B then C, execution plan

Recorded per G10 at the start of the B+C campaign (`docs/tactician-bc-campaign.md` is the
execution plan; this section is the design delta).

- **Ladder order reaffirmed: B then C.** A "skip B, train a value net on A's greedy planner"
  option was evaluated and rejected (no true afterstate without B1; one-ply search cannot value
  sacrificial/anticipatory plays; search-free self-improvement loops are collapse-prone). The
  only C work pulled forward is the C1 exporter, to use idle compute during B's build.
- **G13 (new invariant). Scale and shape generalization.** From Phase B on, bots are built,
  benchmarked and trained across point levels {1k, 2k, 3k, 4k} and player shapes {1v1, 2v2}.
  Concretely: (a) no scoring or feature term carries an absolute (inches, points, wound counts)
  where a fraction or normalized quantity exists; (b) C1 features aggregate per SIDE (own side,
  allied, enemy sum, enemy max) so every shape has one input width; (c) B2's search uses a
  per-side reward vector with max^n backup (each acting player maximizes its own side), reducing
  bit-identically to two-player in 1v1; (d) search time budgets scale with root branching under a
  hard cap. 3v3 / 3v2 / FFA are NOT gated - one 4-player FFA no-fault smoke per gate only.
- **Gates gain panels.** B-gate and C-gate keep the 2k 1v1 main matrix for statistical power and
  add non-regression panels (1k, 3k, 4k, 2v2) at ~100 games/cell against A's measured baseline;
  C's held-out set covers army pairs, one point level AND one shape (extends the 6.1 rider). The
  panel definitions live in the campaign doc, section 5, to keep this doc's gate text stable.
- **A-gate status.** Automated criteria passed (2026-07-26, 83.9 matrix / no cell < 50 / 0
  faults). Left formally open and carried, not dropped: hallway probe (built at the B-gate with
  the probe harness), A6 lobby profile picker, generalization panels for A (measured at campaign
  step 2, not gated retroactively).
- **Pre-authorized stop-and-asks:** a minimal pause/step hook at `DeterminePlayerTurnStage` if
  B0 needs it (pin-tested, solo untouched); lab-side team plumbing (`SlotSpec.Team`).
- **Branching:** all B+C work on `tactician-bc` (both repos); master merges only at L1 (B-gate)
  and L2 (C-gate), optionally L0 for the harness tooling.

---

## Appendix A — Macro-action vocabulary v2 (CONFIRMED by Chris 2026-07-09, with one edit: mid-game MoveToEmbark cut from M12 — see that entry. A3c is cleared to build this vocabulary.)

v1 was authored by the planning session; v2 folds in Chris's review (escort, kite, mass, fatigue
bait, block, move-to-cast, transport delivery). Two of those became generator-wide rules rather
than intents — see "Generator rules" below.

**Generator rules (apply to every intent):**

- **Diversity-preserving pruning.** The per-activation candidate cap ranks within and across
  intent families but must keep at least one feasible candidate per family, and must NEVER prune
  purely by immediate expected value. Sacrificial plays — a cheap unit charging a strong melee
  unit to eat its fatigue (protecting a more valuable later attacker), a throwaway line that just
  buys time — look terrible to one-step math and are exactly what search exists to discover. If
  a candidate is generated, search decides its worth; the generator only decides feasibility.
  (Origin: Chris's "fatigue bait" — not a movement intent, but a hard rule that the candidate
  survives to be searched. Requires fatigue state in CombatMath (A1) and in C1 features.)
- **Feasibility is explicit.** Every candidate states reachable / blocked / budget-clipped, so
  search never wastes rollouts on infeasible branches and G3 fallbacks are attributable.

**Movement intents** (per activation; Hold always present):

- **M1 Hold** — stay, optionally reform/re-face toward the dominant threat.
- **M2 AdvanceOnObjective(o)** — path toward objective *o* at advance budget (can still shoot).
- **M3 RushObjective(o)** — path toward *o* at rush budget (no shooting).
- **M4 EngageAtRange(e, band)** — position relative to enemy *e* at a chosen range band:
  *MaxRange* (own best weapon's reach), *HalfRange* (when the weapon profile rewards it), or
  *SafeShooting* (kite): inside own range but OUTSIDE e's threat envelope (e's move + weapon
  reach), so we shoot and e cannot answer even after moving. The kite band may move the unit
  *away* from e; that is the point.
- **M5 ChargeToContact(e)** — existing charge machinery, gap-targeted. Generated even when the
  immediate exchange is unfavorable (see diversity rule — fatigue bait, tarpits).
- **M6 FallBack** — away from the highest-threat enemies, maximizing survival while staying
  within objective-relevant distance if currently holding one.
- **M7 SeekCoverFrom(e)** — nearest position with cover against *e* within budget, biased toward
  the current strategic goal.
- **M8 Block(e, asset)** — form a barrier across enemy *e*'s shortest path toward *asset* (an
  objective or a friendly unit): enemies cannot move or shoot through our units, so a line stops
  an advance outright. Subsumes v1's speculative ScreenLane. Whether the blocker is a durable
  wall or a cheap speed bump is search's call, not the generator's. *Implementation note:*
  blocking wants a stretched line formation, not `PackGrid`'s tight block — the formation packer
  needs a line mode (new work, flag in A3a).
- **M9 Escort(ally)** — keep pace with a key friendly unit, interposing against its dominant
  threats (bodyguard for a caster, a transport, an objective-holder).
- **M10 Concentrate** — move toward the army's main friendly cluster / focal sector. This is the
  per-unit enabler of a "death ball": massing sacrifices map coverage to overwhelm one area, and
  is sometimes right, sometimes wrong — the generator only offers the option; search and (in C)
  the learned evaluator decide when concentration beats spreading. C1 features must include
  local force-concentration measures so this is learnable.
- **M11 MoveToCast(spell, target)** — position the caster within cast range (and, where required,
  line of sight) of an intended target — friendly buff recipients (e.g. Furious onto a
  high-attack melee unit, a defensive buff onto something vulnerable) or enemy targets for
  damage/debuffs. Also generated for cast-*assist* positioning (the 18" assist radius). The
  value is often anticipatory (cast lands this turn or next); search/eval judges it. *Verify
  during A5:* whether the Cast action permits movement in the same activation — if not, this
  intent is inherently a set-up move and only search can value it.
- **M12 DeliverCargo(transport)** — a transport routes to where its *cargo* wants to be (the
  cargo's own projected best goals: objectives, charge targets, range bands), including
  disembark timing. The transport's own position is subordinate to the cargo's plan.
  *Confirmed edit (Chris, 2026-07-09):* the inverse intent — mid-game **MoveToEmbark** — is CUT
  from the generator: post-deployment embarking is almost never useful in real play (seen once,
  and only because the transport flew). Deploy-time embark stays (deployment intents). Revival
  condition if it ever returns: gate it on the transport's mobility meaningfully exceeding the
  cargo's (e.g. Flying), never as a universal candidate.

**Deployment intents:** zone-constrained analogues of M2/M4/M7/M10, plus reserve declarations
(Ambush/Scout timing: simple round/threat heuristics in A5).

**Casting intents:** (spell x valid target) enumeration; assist contribute/decline by expected
value; M11 supplies the positioning half.

With twelve intent families the raw candidate count can exceed the old ~12 cap; the cap becomes
a ranking budget governed by the diversity rule above (at least one candidate per feasible
family survives). Tune the budget empirically at A3c/B2 and record the choice.

---

## Appendix B — vocabulary

- **Composite activation decision**: the tuple (unit to activate, action, macro-action, primary
  target) treated as one search edge, because engine state is only resumable at activation
  boundaries.
- **Gate**: the benchmark + probe + human-play criteria a phase must pass before the next starts.
- **Pin test**: a test asserting a refactor/approximation matches existing behavior exactly (or
  within stated tolerance).
- **Probe**: a hand-authored scenario with a known-best decision, scored automatically (6.2).
- **Solo rules**: the existing heuristic bot (`Ai/AiResolverRegistryFactory`), permanent baseline.
