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

**2026-07-09 — filed** (Tactician prerequisite, plan sec. 7 P3). Benchmark army pool (~8 armies,
archetype spread) is Chris-curated — stop-and-ask before substituting placeholder armies for the
real pool.

## Outcome

(open)
