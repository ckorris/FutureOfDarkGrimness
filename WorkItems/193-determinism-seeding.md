# 193 — Determinism & seeding pass

**Goal:** same seed + same build => identical game, including in parallel. (a)
`ProbabilisticDiceRoller.RollDecisive`'s static unseeded `Random` becomes per-instance, seeded
from `GameSettings.DiceSeed` (mirror `RealisticDiceRoller`, #167 plumbing). (b) Audit + seed all
other game-path RNGs — known offenders: solo-rules `AiPlaceObjectiveResolver` (random X),
`AiPlaceOneTerrainResolver` (random template/rotation/position); route through
`IGameContext.DiceRoller` or a `GameSettings`-owned seeded `Random`. Solo-rules distributional
behavior must not change (pin with existing AI tests). (c) CLI `--seed N` on headless + scenario
paths. (d) No static mutable RNG remains (parallel-game safety).

**Why:** Tactician prerequisite P2 (`docs/ai-agent-plan.md` sec. 7) — reproducible benchmarks,
debuggable self-play, and cross-talk-free parallel games.

**Verify:** same-seed transcript equality (timestamps filtered); same-seed equality run solo vs
amid 16 concurrent games. Both added to the suite (plan sec. 6.4).

## Notes (newest first)

**2026-07-09 — filed** (Tactician prerequisite, plan sec. 7 P2).

## Outcome

(open)
