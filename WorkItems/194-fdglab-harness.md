# 194 — FdgLab self-play harness (GameRunner, benchmark, probes)

**Goal:** new console project `FdgLab/FdgLab.csproj` in this repo (references the engine project;
plan decision D3). Deliverables:
- `GameRunner.RunGame(GameSpec) -> GameRecord`: fully in-process AI-vs-AI game per the
  `CliApp.RunAsync` template (`FdgRaylib/Cli/CliApp.cs:51-106`) minus stdin. GameSpec = armies,
  seed, per-slot AI profile, randomness type. GameRecord = `GameResult` (#192), timings, optional
  per-round score trace + state snapshots.
- Watchdog: overtime games cancelled and recorded as Fault — never wedge the fleet. Fault rates
  tracked from day one (doubles as an engine fuzz harness).
- Parallel runner (configurable DOP; requires #193).
- Benchmark command (plan sec. 6.1): seeded matchup matrix, side-swapped, score = win + 0.5*tie,
  markdown + CSV reports under `FdgLab/reports/` (gitignored).
- Strategy-probe command scaffold (plan sec. 6.2; probes authored from Phase A).

**Why:** Tactician prerequisite P3 (`docs/ai-agent-plan.md` sec. 7) — the verification instrument
every later gate depends on, and later the self-play data generator for Phase C.

**Verify:** 200-game solo-rules-vs-solo-rules matrix, zero hangs, plausibly symmetric results;
identical re-run reproduces identical aggregates; throughput + fault baseline recorded in #191.

## Notes (newest first)

**2026-07-09 — implemented; gate 2/3 passed, reproducibility facet blocked on #198.**

Shipped (`FdgLab/`, engine-only dependency, in the solution): `GameRunner` (fresh store/bus/server
per game, per the DeterminismTests assembly pattern; AI seeds keyed on slot ID via
`AiResolverRegistryFactory.BuildSoloRules(seed, slotID)`); watchdog -> `Fault`; `TimingRegistry`
(per-decision wall times -> `DecisionStats`); `LabPlayerController` (log capture — built mid-item to
diff divergent transcripts, kept as a permanent instrument); `LabMessageBus` (lab copy of the app's
LocalMessageBus); `bench` (matchup matrix / `--pool`, side-swapped seeds, score = win + 0.5*tie over
completed games, faults listed with messages, md + CSV to gitignored `FdgLab/reports/`, SHA-256
**outcome hash** over the deterministic tuple stream as the reproducibility instrument); `smoke`
(`--repeat`, `--dump-logs`); `probes` scaffold. Armies: `.fdgarmy` path, `builtin` (CLI EOF-fallback
copy), `builtin-basic` (minus the Ambush unit).

**Gate results:**
- 200-game builtin mirror: zero hangs, symmetric (37/37 wins, 125 ties, A score exactly 50.0%). PASS.
- Throughput/fault baseline (Debug, DOP 16, Threadripper 1950X): **5.25 games/s** (~19k/hour,
  ~450k/day); per-game mean 3.0s, p95 4.3s; ~141 decisions/game at ~5ms mean. Faults ~0.5-1% — all
  the known #159 cohesion crash (now with a seeded 8/10 repro, logged in #159). PASS.
- Identical re-run reproduces identical outcome hash: **FAIL — engine bug, not harness**. Filed
  **#198** (run-to-run nondeterminism beyond the seed on rich army paths; movement paths differ,
  ambush-arrival availability flips; suspects: async-void stage-transition races and/or
  identity-hash-ordered collection iteration). Verified not to be RNG (mutation-tested in #193), not
  cross-game state (flips at DOP 1 and on in-process single-spec repeats), not GUID ordering.
  The bench outcome hash + `smoke --repeat` are #198's acceptance tests. Until then, statistical
  comparisons (win rates over 100s of games) are valid — the noise is unbiased — but exact replay is
  not; the gate facet re-verifies when #198 closes.

**Deferred (recorded, not silently cut):** per-round score trace and state snapshots on `GameRecord`
(Phase C1's data exporter is their consumer; hooks exist); probes are scaffold-only until Phase A
authors them; Chris's curated ~8-army benchmark pool still pending (stop-and-ask) — `builtin`/
`builtin-basic` and repo `.fdgarmy` files serve until then.

**2026-07-09 — filed** (Tactician prerequisite, plan sec. 7 P3). Benchmark army pool (~8 armies,
archetype spread) is Chris-curated — stop-and-ask before substituting placeholder armies for the
real pool.

## Outcome

**Done 2026-07-09 — gate 3/3.** The deferred reproducibility facet re-verified the same day #198
landed: two identical 200-game `bench` invocations produce identical outcome hashes on both the
builtin (`B05AA1D810364C6B`) and builtin-basic (`F4318EF0D91161F5`) matrices at DOP 16. Zero hangs,
symmetric mirrors, zero faults post-#198 (fault-rate note in #159). Baseline: 5.25 games/s Debug
(~19k/hour). The harness earned its keep before closing: its outcome hash caught #198, its trace
instrument (`GameTracer`, added mid-investigation) localized it, and its seed scan updated #159.
Deferred to Phase C1 as designed: per-round score trace + state snapshots. Probes: scaffold only
until Phase A. Chris's curated benchmark army pool: still pending (stop-and-ask).
