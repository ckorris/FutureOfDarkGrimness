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

**2026-07-09 — implemented.** Engine `c9a2466`. Scope was **larger than filed**: six unseeded RNG
sites on the game path, not two.

- `GameRandom` (new, `Utilities/Probability/`): per-consumer seeded `Random` via salts, plus
  `DeriveForSlot(seed, slotID)` for AI players. Keyed on **slot ID, not PlayerID** — player GUIDs are
  minted fresh each run of a non-resumed game, so seeding off them would silently defeat the seed.
- `ProbabilisticDiceRoller`: `static readonly Random` -> per-instance, seeded from `DiceSeed`. This was
  the parallel-games cross-talk hazard.
- `DiceUtilities.RollOff_SingleWinner/_Ordered`: both private `new Random()`s deleted; roll-offs now go
  through `IDiceRoller.RollDecisiveFace()` (new extension). This resolves the stale comment that they
  couldn't use the context roller "because that may not be random" — `RollDecisive` (added later, for
  morale) yields a concrete face even in probabilistic mode. Four call sites threaded.
- `PlaceOneObjectiveStage`: shuffle draws from the new `IGameContext.Rng` (the game's single seeded
  source for non-dice randomness).
- `AiPlaceObjectiveResolver` / `AiPlaceOneTerrainResolver`: optional injected `Random`, supplied by
  `AiResolverRegistryFactory.BuildSoloRules(..., seed, slotID)`. Solo-rules *distribution* unchanged
  (D1); only reproducibility added. Threaded through both lobby sites, `CliApp`, `ScenarioLauncher`.
- CLI `--seed N` on headless, `--scenario` (overrides the saved seed), and the GUI scenario path.
- Left alone deliberately: `LobbyViewModel_Host._tributeRng` (lobby chrome, not the game path).

**Test-quality note.** The whole-game determinism tests were initially near-vacuous. (1) They compared
`GameResult`s, and two identically-FAULTED games compare equal — added an explicit "did it really
play" guard, which promptly caught #195. (2) The fresh-game fingerprint recorded only model positions,
and the solo-rules bot ignores objectives, so an unseeded objective placer moved no models: a mutation
test (un-seed the placer) left the suite green. Fixed by fingerprinting objective positions/owners too
and adding direct resolver-level tests; the same mutation now kills 3 tests.

**2026-07-09 — filed** (Tactician prerequisite, plan sec. 7 P2).

## Outcome

**Done 2026-07-09.** Suite **1367/1367** (18 new `DeterminismTests`), full build clean.

End-to-end via the real app: two `--headless --seed 42` runs produce byte-identical logs (after
normalizing the per-run PlayerID GUIDs); `--seed 99` diverges; two unseeded runs still differ, so the
default stayed unpredictable. `Game result:` lines match across seeded runs.

Mutation-verified: re-introducing an unseeded RNG in `AiPlaceObjectiveResolver` turns 3 determinism
tests red, so the suite genuinely detects the class of bug it exists to prevent.

**Found and filed, not fixed: [#195](195-resume-plays-extra-rounds.md)** — resumed games play four
more rounds instead of finishing the four-round game (`ReconcileObjectivesStage._timesEntered` is a
per-instance counter that ignores the resumed round). The resume determinism fixture therefore asserts
only "did not fault" on round count, with an explicit pointer to #195 so the bug is never pinned as
expected behavior.
