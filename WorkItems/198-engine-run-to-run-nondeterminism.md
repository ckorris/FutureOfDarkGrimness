# 198 — Game outcomes vary run-to-run beyond the seed (rich army paths)

**Symptom.** With everything #193 seeded (dice, decisive rolls, roll-offs, placement RNGs, per-slot AI
streams), a fully seeded AI-vs-AI game still flips outcomes between runs — including **repeats of the
identical spec inside one process**. Flip rates are per-seed: some seeds are rock-stable, others sit on
a knife edge (~20-40%). Only rich armies show it; #193's simple 3-model rifle armies (and the whole
`DeterminismTests` fixture) never hit the racy paths, which is why the engine suite is green while this
is broken.

**Repro** (deterministic commands, nondeterministic outcomes — that is the bug):

```bash
dotnet run --project FdgLab/FdgLab.csproj -- smoke --seed 1007 --repeat 20
#  -> mix of "Tie scores=[1, 1]" and "Win scores=[1, 0]"  (observed 12/8)
dotnet run --project FdgLab/FdgLab.csproj -- smoke --a builtin-basic --b builtin-basic --seed 1032 --repeat 16
#  -> mix of Tie and Win even WITHOUT the Ambush unit      (observed 13/3)
dotnet run --project FdgLab/FdgLab.csproj -- bench --a builtin --b builtin --games 200
#  -> "Outcome hash" differs between two identical invocations (16/200 rows flipped in one comparison)
```

Transcript diffing (`smoke --dump-logs DIR`, then diff a Tie against a Win):

- **builtin-basic, seed 1032**: first divergence is a movement — the same move sends **3 vs 2 models**
  across dangerous terrain, i.e. the AI's produced paths differ between identical runs; everything
  downstream (saves, pile-in counts, melee result, morale, Rout) cascades from there.
- **builtin, seed 1007**: which side's Ambush unit can act after arrival flips — one run slot 1's
  Infiltrators arrive and shoot, the other run they log "No actions available"; the dangerous-terrain
  test line floats to different positions between transcripts, and "Entered Choose Action" appears
  doubled — stage sequences appear to interleave.

**Ruled out:** RNG seeding (mutation-verified in #193; also in-process repeats share every RNG stream
construction path); harness cross-game state (fresh store/bus/server per game; flips reproduce at
DOP 1 and in single-spec in-process repeats); PlayerID GUID ordering (12 identical in-process repeats
of a stable seed each mint fresh GUIDs).

**Suspects (not yet confirmed):**
1. **The fire-and-forget async-void stage transitions** (documented in `docs/EngineNotes.md`, flagged
   as risk R1 in `docs/ai-agent-plan.md`): with `RunContinuationsAsynchronously` on the request path
   (#084), every decision hops the thread pool; if any two stage side effects can interleave, order
   becomes timing-dependent. The doubled/floating log lines in the seed-1007 transcripts point here.
2. Iteration over reference-keyed `HashSet`/`Dictionary` on a decision path (identity hash codes vary
   per allocation, hence per run). One candidate audited and cleared
   (`AiPlaceObjectsResolver.GetTableOccupants` — Contains-only); the movement/formation path
   (`AiDefineMovementResolver`, `CohesiveFormation`, `MovementUtilities`) has not been swept.

**Interlock with #159:** the DefinePathStage cohesion crash reproduces at **8/10 with
`fdglab smoke --seed 1027 --repeat 10`** (builtin armies) — a far stronger repro than the ~1-in-10
unseeded rate, and this item explains why #159 flakes even at a fixed seed: the crash sits downstream
of the nondeterministic movement paths.

**Impact.** Blocks the reproducibility facet of #194's gate (benchmark outcome hashes differ between
identical invocations) and with it plan G4's benchmark discipline; search rollouts (Phase B) would be
non-replayable. Statistical comparisons (win rates over hundreds of games) remain valid — the noise is
unbiased — so Phase A tuning can proceed; exact-replay debugging cannot.

**Acceptance test (already built):** two identical `fdglab bench` invocations print the same outcome
hash, and `smoke --seed 1007 --repeat 20` / `--seed 1032 --repeat 16` (builtin-basic) produce 20/16
identical lines. Then extend the engine `DeterminismTests` fresh-game fixture to use the richer
builtin army so the suite holds the line.

## Notes (newest first)

**2026-07-09 — fixed. Root cause: ONE unseeded RNG, not a race.** Chris's theory ("related to random
terrain placement, or objective placement") was correct. Method: FdgLab gained a `GameTracer` — an
ordered trace of every model position write + terrain/objective creation interleaved with the game
log (`smoke --trace --dump-logs`). Diffing a Tie trace against a Win trace of the same seed put the
first divergence in **`PlaceTerrainStage.PlaceAutoLayout`**: `new System.Random()` drives the 40%
deployment-zone thinning of the built-in auto layout, so every run played on a different table and
everything cascaded. #193's audit missed it because the fully-qualified `new System.Random()` dodged
the `new Random()` grep; a corrected audit confirms it was the last one on the game path.

Why the transcripts lied: the skip log line does not name the piece, so two different tables produced
identical-looking logs for ~394 lines. The suspected async-void race and identity-hash iteration were
red herrings (and .NET Dictionary/HashSet enumerate in insertion order anyway, so the identity-hash
theory was doubly wrong). The filing's "movement paths differ / ambush arrival flips" localizations
were all downstream symptoms of different terrain.

**2026-07-09 — filed** from #194's gate verification (the benchmark harness's first catch). Engine
investigation, likely in the state-machine transition plumbing — read `docs/EngineNotes.md` threading
notes and audit stage-transition concurrency before touching anything. Relates #193 (seeding half of
the determinism story), #159 (downstream crash), #191 (Phase B blocked on this for replayable
rollouts), #194 (gate facet blocked).

## Outcome

**Done 2026-07-09.** Engine: one-line fix — `PlaceAutoLayout` draws from `IGameContext.Rng` (the #193
seam); unseeded games still vary (null seed -> unseeded Rng), seeded games now reproduce end-to-end.

Verified against this item's own acceptance tests: `smoke --seed 1007 --repeat 20` -> 20/20 identical
(was 12/8); `smoke --a builtin-basic --b builtin-basic --seed 1032 --repeat 16` -> 16/16 (was 13/3);
two 200-game `bench` runs -> **identical outcome hashes** on both builtin (`B05AA1D810364C6B`) and
builtin-basic (`F4318EF0D91161F5`) matrices at DOP 16, so parallel-safety holds at game level. Engine
suite gained a rich-army fresh-game determinism test (Ambush/Scout/Vanguard/Counter/Blast/etc. — the
paths the too-simple #193 fixtures never touched), **mutation-verified**: reverting the fix turns it
red. Suite 1380/1380; two seeded headless CLI runs byte-identical. A humility note for the ledger:
#193's own two-run CLI check had passed despite this bug — two runs can match by chance; the 200-game
outcome hash is the standard that caught it.

Bonus: zero #159 faults in 1,200 post-fix games (seeds 1000-1099, 2000-2199, both army sets) — the
old crash trajectories were fed by the random deployment-zone terrain. Not proof of fix; noted in #159.

Closes the #194 reproducibility gate facet. Phase B's replayable-rollout prerequisite is met.
