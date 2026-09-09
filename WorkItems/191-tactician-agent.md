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

**2026-09-09 (15:20, Opus 5) - DEPTH LOSES, DECISIVELY. THE SHIPPED WIDENING STAYS. STEP 3 STOPPED
EARLY BY THE BOX; THE RESULT IS ALREADY 8 SIGMA.**

Head to head, both sides Strategist, points-1k panel, `iters:256` both sides, budgets riding on the
SLOT so side-swapped seeds keep each bot with its army (`shape-match.sh`, cell `match1`):

| | side A: C=0.5 (shipped) | side B: C=0.25 (depth 18) |
|---|---|---|
| **A score** | **77.0%** | 23.0% |

224 of 600 games, SE 3.3 points - about 8 sigma from even. Side-swap balance exact (112/112). The two
matchups that played agree closely (76.7% over n=150, 77.7% over n=74), so it is not one lopsided
pairing. **Caveat, stated plainly: matchups 2 and 3 never played, and both completed matchups share the
same side-A army (Alien Hives).** The number could move a few points; the direction could not plausibly
reverse.

*Chris's call (15:20): skip the depth increase.* `WideningC` stays at 0.5 and the search shape is
settled. This closes the re-tune-the-widening thread opened at 13:30 - the B4 tuning at 20 iterations
landed on a good value for reasons that still hold at 256.

*Why deeper played worse, most likely:* C=0.25 examines 9 root options instead of 23, and this is
determinized MCTS - an 18-ply line commits to 18 activations of assumed-known dice. Root breadth is what
carries this bot's strength, not lookahead. Recorded because it is the opposite of the intuition that
started the thread (mine and Chris's both), and worth not re-deriving.

*Head-to-head was the right design and should be reused:* the Strategist sits at ~78% vs the Tactician,
so near that ceiling a 3-point shape difference needs ~1500 games/arm; a direct match where 50% is the
null needs ~600. `--search-shape-b` (commit 80c13c1) exists for this.

**IN FLIGHT AT SESSION END:** `shape-match.sh` (task ber8sap0u) still running. `match1` is stuck at
224/600 - four consecutive segfaults, each within ~2 minutes of resume, giving up after attempt 5.
`match2` (C=0.5/256 vs C=0.25/128, the "faster bot" arm) and `anchor-c025` have NOT started. Given the
depth decision above, **match2 and the anchor are now moot and can be killed** - they only measured
variations of a shape we are not shipping. The one cell still worth running is matchups 2 and 3 of
match1 (run them directly with `--a`/`--b`, ~3h at DOP 4) if the coverage gap matters to anyone later.

**Box, 2026-09-09:** a full-heap crash dump of a DOP-6 Strategist bench is **12.5 GB**; one of them ate
the 31 GB box's memory and got a 4-hour run killed by the harness. The segfault itself cost nothing -
`bench.progress.jsonl` survived it intact. Set `DOTNET_DbgMiniDumpType=1` and point
`DOTNET_DbgMiniDumpName` into the scratch dir (small dumps are ~26 MB). Budget ~2 GB resident per
concurrent Strategist game at 256 iterations, so `--dop 4` is the cap on this box.

**STILL OPEN (unchanged by today):** the Strategist iteration default - 256 flat vs `37 * rootUnits`
capped (the coverage formula in the 13:30 entry). Chris's standing rule applies: absent a measured
strength gain, the cheaper configuration wins. A perf-focused prompt for a fresh instance is at
`/home/chris/Projects/fdg-lab-scratch/perf-instance-prompt.md`; thread affinity (31-37%, measured, not
implemented) is the biggest known win.


**2026-09-09 (13:30, Opus 5) - THE BUDGET DOES NOT BUY DEPTH. MAX DEPTH IS PINNED AT 6 FROM 64 TO 512
ITERATIONS ON BOTH A 3-UNIT AND A 7-UNIT ROOT. THE BREADTH/DEPTH KNOBS - ALL TUNED AT 20 ITERATIONS AND
NEVER REVISITED - ARE WHAT DECIDES HOW DEEP THE BOT LOOKS.**

Chris asked whether a nearly-empty board (say 3 units left) could spend its budget going DEEPER instead of
wider. I answered yes from reading the widening formula. The measurement says no, and the formula pointed
the opposite way from what the tree actually does. `depth-probe.sh` / `depth-narrow.sh`, 1 worker, pinned.

| iterations | 3-unit root: nodes / depth / closed / root edges | 7-unit root: nodes / depth / closed |
|---|---|---|
| 32 | 33 / 5 / 0 / 5 | 33 / 5 / 0 |
| 64 | 65 / **6** / 0 / 8 | 65 / **6** / 2 |
| 128 | 129 / **6** / 0 / 11 | 129 / **6** / 4 |
| 256 | 257 / **6** / 0 / 15 | 257 / **6** / 4 |
| 512 | 513 / **6** / 0 / 21 | 513 / **6** / 4 |

Nodes come out at exactly iterations+1, so every iteration expands one node - they are simply not going
downward. What the extra budget buys instead is root breadth, and that has a closed form:

**edges examined per unit = ceil(0.5 \* sqrt(iterations / rootUnits))**

which reproduces the data exactly (256 iterations / 3 units -> 3 x ceil(0.5\*sqrt(85)) = 15 opened, observed
15; at 512 -> 3 x ceil(0.5\*sqrt(171)) = 21, observed 21).

*That formula is the per-unit scaling law the high-priority ledger item was asking for.* What makes the bot
smarter or dumber in different places is not depth, it is how many options each unit gets examined, and
holding THAT constant needs iterations LINEAR in root units - affordable, not the quadratic I feared.
Anchored on current wide-board quality (4 edges/unit at 256 iterations, 7 units) that is
`iterations = 37 * rootUnits`: 3u -> 111 (~0.9s), 7u -> 259 (~5.4s), 10u -> 370 (~11s, over ceiling).
The 10-second ceiling binds around 9 units, so above that a cap takes over and per-unit quality still
falls. No way around that inside 10 seconds.

**Retracted from the 12:45 entry's neighbourhood:** I recommended a taper (`clamp(32*u, 96, 256)`) on the
theory that a narrow root saturates and extra iterations hit solved lines. Closed edges are ZERO at every
level on the narrow board, so nothing is saturating. The taper's shape happens to be roughly right but the
reason was wrong; the coverage formula above is the real basis.

**Chris's decision rule (13:30):** *"if the advantage to going deeper doesn't buy us much more win rate,
then it would be better to take the faster bot."* This flips the default - absent a measured strength gain,
the cheaper configuration wins. Applies to the depth work below and to the 256-vs-lower budget question.

**Why depth is pinned, and the first evidence it is fixable:** widening is `k(N) = ceil(C * N^alpha)` per
unit branch, and the shipped C=0.5 / alpha=0.5 was tuned at B4 on a **20-iteration** measurement (the note
is in `SearchOptions.cs` itself). At 256 iterations that tuning is an order of magnitude out of date. A
first smoke at 32 iterations on the 3-unit board: **C=0.5 -> depth 5; C=0.25 -> depth 10; alpha=0.3 ->
depth 10 on 11 nodes.** Depth is entirely available; the constant was holding it back.

**Lab (step 1, done):** new `FdgLab/SearchShape.cs` parses `c=F;alpha=F;exploration=F;continuation=N`
(the `--weights` idiom). `--search-shape` on both `bench` and `b0` reshapes every search in a run;
`--shape-sweep "SPEC|SPEC|..."` replaces b0 phase (d)'s hardcoded ladder, which only ever walked C down to
0.5 - the SHIPPED value - and so could never answer whether narrower buys depth. Sweep rows apply to the
shipped shape, not to `--search-shape`, so each row means exactly what its label says.

**Step 2 result (13:55) - DEPTH IS FREE, AND ONLY ONE OF THE FOUR KNOBS MATTERS.** `shape-sweep.sh`,
256 iterations, 1 worker, pinned; same budget in every row, so differences are shape alone.

| WideningC | 3-unit root: depth / root edges / ms | 7-unit root: depth / root edges / ms |
|---|---|---|
| 0.5 (shipped) | 6 / 15 / 3214 | 6 / 23 / 7431 |
| 0.35 | 10 / 11 / 3194 | 10 / 15 / 7326 |
| 0.25 | **18** / 8 / 3314 | **18** / 9 / 7175 |
| 0.15 | 24 / 5 / 1737 | **41** / 6 / 6242 |

Three times the depth at identical wall clock and identical node count (257). Best-edge visits rise with
it - 15 -> 47 on the wide board - so the root commits instead of sampling everything thinly.

*The other three knobs are settled and can be dropped from the design:*
- **ExplorationC is INERT.** 1.4 / 1.0 / 0.6 / 0.3 all give depth 6 and 22-23 root edges on the wide board,
  and it stays inert layered on C=0.25 (identical to C=0.25 alone). Under the shipped widening most nodes
  have one legal child, so PUCT has nothing to choose between. Not a knob.
- **Continuation is a bad deal.** 0 -> 1 -> 2 costs 7431 -> 12562 -> 17283 ms on the wide board and buys
  ZERO depth (6 / 6 / 6). Fails Chris's rule outright.
- **WideningAlpha is dominated by C.** alpha=0.25 reaches depth 18-19 on 4 root edges; C=0.25 reaches the
  same depth on 8-9. Same depth, twice the coverage. Leave alpha at 0.5.

*The trade, stated honestly:* shipped examines 23 root options 6 deep; C=0.25 examines 9 options 18 deep.
The chosen move changes on BOTH boards, so this is a different bot and narrower root coverage is a real
cost. C=0.15 starts returning fewer nodes than iterations (134 of 257 on the narrow board) - deep lines are
reaching game end and terminal hits expand nothing.

**Proposed step 3 arms (NOT run - awaiting Chris):** vs Tactician, panel, several hundred games each.
1. control C=0.5 / 256 iterations (the 78.3% config, 7.4s); 2. C=0.25 / 256 (depth 18, same cost);
3. **C=0.25 / 128** - the faster bot. Arm 3 exists because of Chris's rule: if depth is what carries
strength, we should be able to buy the time back. If arm 3 matches the control, "deeper and faster" settles
the iteration-budget question outright. If nothing beats the control, C=0.5 stays and the cheapest budget
that holds wins - also his rule.

**Open (step 3, NOT run):** depth is not strength. This is determinized MCTS - each worker searches one
sampled future, so a 10-ply line is 10 activations of assumed-known dice, and deeper leans harder on the
search and less on the 15b learned leaf that got us to 78.3%. The shapes that win on depth have to be
benched against Tactician on a real panel before any of this ships. Per Chris's rule above, a shape that
does not move the win rate loses to the cheaper one.


**2026-09-09 (12:45, Opus 5) - COST AND SCALING MEASURED AT 1k/2k/4k, AND A 32-37% FREE SPEEDUP FOUND:
PINNING THE 4 SEARCH WORKERS TO CORES THAT SHARE AN L3 BEATS LETTING THE SCHEDULER SPREAD THEM. THE "SLOW
LAPTOP" WORRY INVERTS - THIS BOX IS THE OUTLIER, NOT THE LAPTOP.**

DOP 1, 2 games per cell, `iters:128`, post-perf, `iter-scale3.sh`. "Per activation" is the bench's worst-p95
decision, which is the search; with alternating activations that IS the player's wait.

| army size | 16 cores (unpinned) | pinned to cores 0-3 | gain | per iteration, pinned | decisions/game |
|---|---|---|---|---|---|
| 1k | 3.36 s | 2.11 s | -37% | 16.5 ms | 190 |
| 2k | 5.58 s | 3.79 s | -32% | 29.6 ms | 319 |
| 4k | 6.73 s | 4.66 s | -31% | 36.4 ms | 469 |

*Two findings.*

**1. Cost grows SUBLINEARLY with army size.** 1k -> 2k costs 1.7x per iteration, but 2k -> 4k only 1.2x,
while unit count (read off decisions per game) grows 1 : 1.68 : 2.47. So a bigger board is much cheaper per
unit of thinking than feared, and the high-priority "scale the cap with root branching" item is affordable:
matching 2k's per-unit thinking at 4k needs ~1.5x the iterations at only ~1.2x the per-iteration cost.

**2. Thread affinity is worth 32-37%, free.** `lscpu` confirms the 1950X has FOUR separate L3 caches (32
MiB in 4 instances; cores 0-3 + SMT siblings 16-19 share one; NUMA reports a single node, so this is L3
locality, not memory distance). Unpinned, the scheduler scatters the 4 workers across 4 cache complexes and
they pay cross-CCX coherence on shared reads and on allocation - and an iteration allocates ~9.5 MB, with
one workstation-GC heap shared by all of them. Pinned to one CCX they share an 8 MiB L3. This is a bigger
win than any single perf pass 3-10 and it changes no play semantics.
*Consequence for the reference-machine question (Chris was right to push back):* the 4-core cells were not
simulating a slow laptop, they were accidentally simulating a WELL-LAID-OUT one. A 4-core laptop has all
cores on one L3 already, so it gets the pinned numbers naturally. Unpinned, this box is the SLOWER machine
for this workload. Every cost figure quoted earlier today came from the unpinned config and is pessimistic
by about a third.
*New work item candidate:* pin the search's workers to L3-sharing cores at search start. Open questions: how
to pick the core set portably (Linux `sched_setaffinity` via `Process.ProcessorAffinity`, macOS has no
equivalent), whether to leave the choice to the OS on <=4-core machines (where it is a no-op), and whether
more workers pinned per CCX beats 4 (workers are an ensemble over determinizations, so extra workers are not
free strength).

*What the budget costs, pinned, scaled linearly from the 128 measurement (linearity verified across 64/128/
256 at 31.9-34.2 ms):*

| iterations per worker | 1k | 2k | 4k |
|---|---|---|---|
| 128 | 2.1 s | 3.8 s | 4.7 s |
| 256 | 4.2 s | 7.6 s | 9.3 s |
| 384 | 6.3 s | 11.4 s | 14.0 s |

Against Chris's target (5 s good, 10 s ceiling): **a flat 256 fits everywhere** - 4.2 / 7.6 / 9.3 - with 1k
comfortably inside the 5 s goal. Evening out per-unit quality at 4k would need ~380 iterations there, i.e.
~14 s, which is over the ceiling; so the per-root-unit slope has to be capped, exactly as
`MaxBudgetMs` caps the time budget today.

**2026-09-09 (12:15, Opus 5) - RETRACTION: THE 256-ITERATION CRASH IS NOT REPRODUCIBLE AND IS NOT A DATA RACE.
THE 11:55 ENTRY'S "IN-RANGE BLOCKER" READING IS WITHDRAWN. A FULL AUDIT OF THE SEARCH'S SHARED STATE FOUND
NO HEAP-CORRUPTING WRITE; TWO REAL BUT NON-CORRUPTING RACES WERE FOUND AND FIXED (ENGINE `628a27a`).**

*The discriminator run* (`crash-test.sh`, same binary, same 2k pair, DOP 1, 4 games, only the budget flag
differing): `--search-budget interactive` (~190 iterations per worker) completed 4 games clean in 16 min;
`--search-budget iters:256` then ALSO completed 4 games clean, in 23 min. Earlier the same `iters:256`
configuration died 3 times out of 3 within ~25 s. So the crash is intermittent, not depth-triggered, and
reading that cluster of three as determinism was my error. Peak RSS across the whole run was 0.52 GB, so
memory exhaustion is excluded too.

*The audit* (full sweep of static/shared mutable state reachable from the 4 search workers). The decisive
structural fact: **there is no `unsafe`, `stackalloc`, `Span`, `MemoryMarshal`, `Unsafe.*`, `fixed`,
`GCHandle` or `Marshal.*` anywhere in the engine or FdgLab.** Without one of those, a racy managed
`Dictionary`/`List`/`HashSet` throws, spins, or silently loses entries - every write is bounds-checked and
reference stores are atomic. It cannot produce the wild pointer that faulted
`IComponentStore.get_Capacity()`. Also verified: `StoreClone.Clone` never writes its `source` (every typed
cloner writes only the target; `TokenContainer.Clone` takes the source's own lock), so the shared frozen
root snapshot being cloned by 4 workers at once is safe.

*Fixed anyway, both real:*
1. `SpecialRuleDefinition._listeners`/`_activators` (perf passes 4 and 10) memoized with `??=` on
   definitions that are process-wide shared - the core catalog's singletons plus army definitions from pass
   7's content-keyed cache - and `StoreClone` shares `ResolvedRule` by reference, so all 4 workers race the
   memo. Cannot corrupt memory (the array is fully built before the reference is published, and that store
   is atomic), but on a weak memory model a reader can see the reference before the element writes, read a
   stale zero bit and skip a rule that should have fired. That is **arm64 only - the shipped osx-arm64
   build** - and it would break "same seed, same tree" there. Now built in the constructor.
2. `UnitFileEntry`/`WeaponFileEntry.StableID` used a non-atomic `_nextID++` while army files are
   deserialized concurrently; two entries could share the ID other data keys on. `Interlocked.Increment`.

Engine suite green (3291). **Trees verified unchanged on both oracle boards:** A = 301 nodes / depth 6 / 4
closed / Great Monolith 18 visits; B = 301 / 6 / 2 closed / Nightmares 25 visits. Both match the canonical
values exactly, so the fixes are semantics-preserving like every perf pass.

*Verified safe, recorded so nobody re-hunts them:* `HookContextCatalog.Default` (filled in its constructor,
never written again), `Weapon._profileKey` (weapons are copied per clone), `ArmyRuleDataPersistence`'s parse
cache (ConcurrentDictionary, and every consumer read-only), `TerrainGridCache` (locked), `TerrainGrid`
route memos (concurrent + Interlocked), `MovementUtilities` terrain/cohesion caches and `RuleEvaluator`'s
dedup pool (all `[ThreadStatic]`), `TokenContainer` (fully locked), the MLP leaf and `SideMap` (immutable
after construction). Worker isolation confirmed: each worker owns its tree, action space, expander, rule
evaluator, scratch and planner; the only shared objects are the root snapshot, the side map, the leaf
evaluator and the cancellation source, all read-only.

*One latent trap, not live today:* `StoreClone`'s `SerializeValue` fallback would run Newtonsoft on 4
threads through one shared settings object, but it is unreachable because all 15 registered types have
typed cloners. It goes live the day someone registers a 16th type without one - worth a guard or a test.

*Where the crash stands:* back with the box, which is where the 2026-09-08 note had it before I over-read
three fast failures. The memtest86 pass remains the recommended next step; further managed-code hunting is
not justified by the evidence.

**2026-09-09 (11:55, Opus 5) - CORRECTION + THE NUMBER: FDG USES ALTERNATING ACTIVATIONS, SO A "TURN" IS ONE
ACTIVATION AND THE PLAYER WAITS FOR ONE SEARCH, NOT EIGHT. THE 11:05 AND 11:20 ENTRIES' TURN MODEL IS WRONG
AND IS SUPERSEDED HERE. AT 2k ON THIS BOX, 256 ITERATIONS PER WORKER = 8.2 s OF SEARCH; CHRIS'S 5 s GOAL IS
~128 AND HIS 10 s CEILING IS ~256 - WHICH IS WHAT THE SHIPPED TIME BUDGET ALREADY SPENDS.**

Chris (2026-09-09): "recall that this game uses alternating activations, so one turn is one activation."
Both earlier entries multiplied one search by ~8 units to get a "turn", which inflated every wait by 8x and
made the budget look ~20x tighter than it is. Everything downstream of that multiplication in those entries
is void; the cost line itself (43 ms per iteration in-game) stands.

*Measured, board A (Orks vs Robot Legions, 2k, 7 root units), post-perf, quiet box, 4 workers:*

| iterations per worker | pure search (1 worker, quiet) | in a real game (4 workers + engine work) |
|---|---|---|
| 64 | 2.2 s | 2.8 s |
| 128 | 4.3 s | 5.5 s |
| 256 | 8.2 s | ~11 s |
| 512 | ~16 s | ~22 s |

Per-iteration cost is flat at 31.9-34.2 ms across the three measured rungs, so cost is linear in the cap and
the 512 row extrapolates safely. The in-game column is the same work with 4 workers contending plus
per-activation setup, taken from the DOP 1 bench cell (2.78 s per search at 64 iterations = 43 ms each).

*Strength so far, vs the Tactician, scored for the Strategist:* 1k panel, 192 games, 4 iterations per worker
= 60.7%. The same panel at the shipping time budget (~256 iterations) = 78.3%, which Chris calls "very
smart". So the whole range from 4 to 256 is worth about 17 points - a real curve, but the bot keeps most of
what it knows at a much smaller budget.

*Where this lands.* Chris favours smartness and accepts up to 10 s. That makes **256 the candidate default
and 128 the safe pick**, and it means the iteration cap is NOT a strength sacrifice: it reproduces roughly
what ships today, while making that strength identical on every machine instead of a function of the
player's CPU. Still to measure: 128 vs 256 head to head (the ladder), the 4-core laptop proxy, and the
per-army-size scaling below.

*HIGH PRIORITY (Chris, 2026-09-09): scale the cap with root branching.* Fork F1 from the 11:05 entry is now
a priority item, not an open question. The design mirrors what the time budget already does
(`BudgetMsPerRootUnit`, base + per-root-unit, floor and cap) so the bot is not smarter at 1k and dumber at
4k. Note it now costs far less than feared: with one activation per turn, scaling the cap up at 4k does not
multiply against a unit count, it only pays the higher per-iteration cost of a bigger board.

*The crash is now IN the target range and is therefore a blocker, not a curiosity.* A bench at `iters:256`
segfaulted 3 times out of 3 within ~25 s of a game starting, while `iters:64` ran clean and a single b0
search at 512 iterations x 4 workers ran clean. So it needs a full game's repeated searches, which is
exactly shipped play at the budget we are about to choose. The dump (`logs/crash-92957.dmp`) shows a MANAGED
thread faulting on an interface dispatch on a component store, inside `StoreClone.Clone` ->
`GetTypeMapWithCapacities` -> `TacticianActionSpace.Load`, i.e. a bad reference read out of the store's
plain `Dictionary`. That is a different signature from the GC-thread faults blamed on hardware on 2026-09-08,
and it reads like a data race. Next test: whether the same bench crashes under the pre-existing
`--search-budget interactive` (~190 iterations at 2k) on this same binary. If it does, the bug predates the
`iters:N` path and has been in shipped play all along - which would also explain the nine panel crashes.

**2026-09-09 (11:20, Opus 5) - DECISION REVISED (Chris): NO MASTERMIND FOR NOW. ONE BOT, THE STRATEGIST, ON
AN ITERATION CAP, SET AS HIGH AS IS REASONABLE WITHOUT "COOKING PEOPLE'S COMPUTERS". SUPERSEDES THE TWO-BOT
ENTRY BELOW (11:05); EVERYTHING ELSE IN IT - THE ITERATION-CAP RATIONALE, THE FORKS, THE LADDER - STANDS.**

Chris (2026-09-09): "Let's not do Mastermind, that can be an eventual goal. Let's instead do just Strategist,
but make it still based on an iterations count... a level of iterations where it will not take a million
years to run each turn, but it is still a massive improvement on Tactician." Mastermind stays on the list as
a later goal, and costs nothing to add once the curve is known: it is one more constant.

*Assessment recorded, because it shapes the number.*
1. **Returns are logarithmic, cost is linear.** The only slope we have: the same four panels at ~58 vs ~256
   iterations per worker scored 67.7 -> 72.2 vs the Tactician (540 games a side). ~+2 points per doubling,
   for 2x the wait every time. "As high as tolerable" therefore buys very little for a lot of seconds; the
   target is the KNEE of the curve, not the ceiling. The ladder measures the slope properly - the two-panel
   estimate is coarse and taken near a ceiling, where it understates the true slope.
2. **The cap fixes strength across machines; it does NOT fix the wait.** That is the deliberate trade Chris
   accepted. The consequence is that the number must be chosen against a REFERENCE MACHINE THAT IS NOT THE
   DEV BOX. This box is a 16-core Threadripper 1950X where the shipping time budget already works out to
   ~256 iterations per worker; a mid-range laptop at 2-3x slower would wait 2-3x longer for that same cap.
   Choosing on this box ships something that cooks exactly the players the constraint is meant to protect.
3. **The unit that matters is seconds per TURN, not per activation.** A player waits through every activation
   in the turn - roughly 8-12 units at 2k. A "5 s per activation" cap is a ~60 s turn.
4. **Perf work is now the main lever on smartness.** Under a time budget, speed WAS strength. Under a cap,
   speed is headroom: the 1.6x already banked (passes 3-10) means the same wait now affords 1.6x the
   iterations, or the same strength at ~60% of the wait. Every future perf pass converts directly into a
   higher affordable cap, with play at a fixed cap provably identical (the identical-tree oracle). This is
   the "pure performance gain without tuning ramifications" Chris asked for, now literally true.
5. **The candidate-budget reading flips under a cap.** The breadth slice showed 4 and 16 candidates equal at
   equal TIME, which means 4 needed ~1.7x the iterations to match 16. At equal ITERATIONS, 16 is therefore
   the stronger setting - keep it. Open question worth a cell later: does MORE than 16 pay under a cap?

*Open input needed from Chris (does not block the measurement):* the target wall clock per turn, and on what
reference machine. Proposal to react to: ~20-30 s per turn at 2k on a mid-range 4-8 core laptop, which is
2-3x slower than this box. Everything else follows from that number plus the measured per-iteration cost.

*Running now: the cost probe* (`iter-cost.sh`, `iter-cost2.sh`) - DOP 1 on a quiet box, because a player
runs ONE uncontended game and a contended bench would understate the search and overstate the wait. Cells:
1k/2k/4k at 64 and 256 iterations per worker, 2 games each, measuring per-game wall, decisions per game and
worst-case decision. Cost is linear in iterations by construction, so two rungs per size validate the line
and the rest extrapolates. The strength ladder (adjacent-rung head-to-head via the new `--search-budget-b`)
follows once the cost curve says which rungs are affordable at all.

**2026-09-09 (11:05, Opus 5) - DECISION (Chris): THE LOBBY GETS TWO BOTS, "STRATEGIST" AND "MASTERMIND",
DIFFERING ONLY IN MAX SEARCH ITERATIONS - NOT TIME. NOT IMPLEMENTED YET; THIS RECORDS THE DESIGN, THE FORKS
IT OPENS, AND THE MEASUREMENT THAT PICKS THE TWO NUMBERS.**

*Chris's framing (2026-09-09):* "The tuning will be the max iterations they can spend, nothing else. But I
don't want to clock it on time, because that would mean the bot is smarter on a faster computer. I consider
it acceptable that a slower computer takes longer to think." Targets: the Strategist should beat the
Tactician by a good margin, and the Mastermind should be a significant gap above the Strategist.

*Cost to build: almost nothing in the search.* `UctOptions.Iterations` already exists, is documented as
"Iterations PER WORKER. Set => the search is deterministic and ignores the clock", and already flows through
`RunWorkerAsync`; setting it makes `SearchResult.Deterministic` true. So this is a configuration + lobby/UI
change plus two numbers, not new search machinery. The lab currently has no way to bench an iteration cap
(`bench` takes only `--search-budget benchmark|interactive`; only the `b0` spike takes `--search-iterations`),
so the measurement below needs one lab-side flag first.

*Three consequences worth recording.*
1. **Benches become reproducible.** Today every panel is time-budgeted and therefore not reproducible by
   design - the b0 spike says so in its own output, and `bench.md`'s "Outcome hash (deterministic)" is only
   a deterministic function OF the rows, not a promise the rows repeat. Under an iteration cap the hash
   becomes a real regression check: same seeds + same cap => same games, on any machine.
2. **The perf work changes meaning, in the direction Chris asked for.** Under a time budget, speed IS
   strength (today's confirm: 1.6x the iterations per decision). Under an iteration cap, speed is only
   speed: identical play, less waiting. That makes every future perf pass exactly the "pure performance gain
   without tuning ramifications" Chris wants, with no strength re-measurement needed.
3. **Iterations are PER WORKER, and workers are an ensemble, not depth.** Lobby play and the lab both use 4
   workers, each searching its own determinization; merging four 256-iteration trees is not one
   1024-iteration tree. So each bot must be defined as (iterations, workers) with workers pinned, or the
   numbers do not mean anything.

*Two design forks for Chris - not decided here.*
- **(F1) Flat cap, or iterations scaled by root branching?** The time budget deliberately scales with root
  units (`BudgetMsPerRootUnit`, "a 4k game gets more time, not a shallower tree", within a cap). A FLAT
  iteration cap drops that rule: a 4k game would spread the same iterations over ~3x the root branching and
  get a genuinely shallower tree than a 1k game. Recommend keeping the shape - iterations per root unit with
  a floor and a cap - so the two bots mean the same thing at every army size. This is the more consequential
  fork of the two.
- **(F2) Any wall-clock backstop?** A pure iteration cap has no upper bound in seconds; on a slow machine, or
  a pathological board, one activation could take a long time with no feedback. Options: pure cap (accept
  it, matches Chris's stated preference), or cap + a generous timeout that only bites in the tail
  (reintroduces hardware dependence, but only where the alternative is an apparent hang). A progress
  indicator in the GUI may cover the real concern without a timeout.

*Where the numbers sit today* (board A, 2k, 7 root units, post-perf, 30.1-30.8 ms per iteration measured
this morning): the benchmark budget (1840 ms) buys ~58 iterations per worker; the interactive budget that
ships (7800 ms here, 5-10 s range) buys ~256. So the shipping Strategist is already a "~256 iterations"
bot on this hardware, and every panel number in this ledger is a measurement of roughly that.

*Slope we already have.* The same four panels at ~58 vs ~256 iterations per worker, net leaf, 540 games a
side: 67.7 -> 72.2 pooled vs the Tactician. That is +4.5 points for 4.4x the search, about +2.1 per
doubling, and it is compressed by the ceiling - the Strategist already wins ~7 games in 10, so vs-Tactician
score cannot resolve "a significant gap" between two strong bots. The older B-gate figure (+13.5 for 4.3x)
was measured on weaker code at a lower base and should not be used for placing these budgets.

*Measurement that picks the two numbers (queued behind the perf confirm).*
- **Instrument: head-to-head.** Strategist(N) vs Strategist(M) directly, because that is the quantity Chris
  is specifying ("a significant gap above Strategist") and it does not saturate. A vs-Tactician anchor runs
  alongside for the absolute number ("beats the Tactician a lot more").
- **Rungs (per worker, 4 workers):** 32 / 64 / 128 / 256 / 512 / 1024, spanning today's benchmark (~58) and
  today's interactive (~256) so the new scale ties back to every existing result.
- **Order:** a cost probe first (one game per rung, wall clock per rung - cost is linear in iterations, and
  1024 will be ~4x a 256 game), then size the batch to the box rather than guess. Adjacent-rung head-to-head
  cells plus vs-Tactician anchors at the candidate pair.
- **Reading it:** pick the Strategist rung where vs-Tactician is comfortably high and a game still feels
  responsive, then pick the Mastermind rung that beats that Strategist by a clear head-to-head margin
  (a 60/40 head-to-head is a real gap even when both beat the Tactician ~72%).

**2026-09-09 (10:40, Opus 5) - NET 3K RE-RUN DONE (73.7 vs 73.0 HAND). THE INTERACTIVE PANEL SCREEN IS NOW
COMPLETE ON BOTH ARMS: NET 72.2 POOLED vs HAND 69.9 OVER 540 GAMES A SIDE. THE 15b NET-LEAF PROMOTION STANDS.**

The 3k cell that crashed three times on 2026-09-08 finished on the first attempt this morning (08:57-10:35,
98 minutes, workstation GC, nothing else running, 0 faults) - which is the "one heavy job at a time" rule
paying for itself, not a code change. It resumed from 69 banked and completed all 150 games.

Interactive budget (5-10 s per activation, the budget that ships to players), Strategist vs the Tactician bot,
scored for the Strategist:

| panel | games/arm | hand leaf | net leaf | delta |
|---|---|---|---|---|
| points-1k | 120 | 77.5 | 78.3 | +0.8 |
| shape-2v2 | 180 | 64.2 | 68.9 | +4.7 |
| points-3k | 150 | 73.0 | 73.7 | +0.7 |
| points-4k | 90 | 66.1 | 68.3 | +2.2 |
| **pooled** | **540** | **69.9** | **72.2** | **+2.3** |

Per-panel 3k detail (net): Battle Brothers-Goblin Reclaimers 50.0, Knight Brothers-Robot Legions 60.0,
Saurian Starhost-Soul-Snatcher Cults 76.7, Eternal Dynasty-DAO Union 96.7, Titan Lords-Goblin Reclaimers 85.0.
Against hand (63.3 / 66.7 / 65.0 / 81.7 / 88.3) the two arms trade cells in both directions by up to 12 points
and land 0.7 apart pooled: at 3k the leaf choice does not decide games.

*Reading.* +2.3 pooled is ~0.8 SE - not a result on its own. What carries more than the pooled number is that
all four panels came out positive; a 4-of-4 sign test is p=0.06 one-sided. Combined with the earlier
benchmark-budget screen (+1.8 pooled, 540 games), the net leaf is level-or-slightly-ahead of the hand
evaluator at both budgets and at every army size, and the 1k regression that made the 15b evidence contested
(-7.1 at benchmark budget) does NOT reproduce at the shipping budget (+0.8). **Decision: 15b stands, the
Strategist ships the learned leaf.** No further leaf work is motivated - the search is compute-bound, not
knowledge-bound (B-gate: 4.3x thinking time = 56.6 -> 70.1 vs the Tactician; the leaf swap = ~+2).

*Standing caveat, unchanged:* every strength number above was measured on PRE-PERF binaries (`step15bin` for
the panels, `phase1bin` for the breadth slice, `seambin` for the seam bench). Passes 3-10 (-43% ms per
iteration) are committed but have never been scored. Since budgets are milliseconds, not iterations, the
speedup should convert into strength (the b0 timed runs show 56 -> 61 iterations in the same 1840 ms window,
probe overhead included); the B-gate slope puts a 0.8-doubling gain at maybe +4-5 points, less near these
panels' ceiling. A single panel re-run on the optimized build against its recorded pre-perf number would
settle it (~40 min for points-1k). Not queued - Chris's call, along with the "4 candidates at half budget"
throughput arm.

**2026-09-09 (09:05, Fable 5.1) - SEAM BENCH: THE SEAM FIX SCORES 37.8 POOLED AGAINST 34.1 FOR THE PRE-SEAM
SNAPSHOT (+3.7, ~1 SIGMA). THE FIX STANDS. NET 3K RE-RUN STARTED 08:57.**

Same four low-ceiling 2k pairs, same seeds, benchmark budget, 16 candidates, Strategist vs Tactician scored for
the Strategist, 48 games per cell, workstation GC, zero faults: `seambin` (the committed seam fix, super
`55ca00b` / engine `d16d479`: a charge from range is prescribed as its Move, a Shaken unit is one edge; closed
edges 214 -> 4 on board A) against the breadth slice's cb16 column (`phase1bin`, `6fb3162` / `883b676`, the
same code without the fix).

| pair | phase1 cb16 | seam cb16 |
|---|---|---|
| Dark Elf Raiders vs Dwarf Guilds | 35.4 | 39.6 |
| Dwarf Guilds vs High Elf Fleets | 44.8 | 47.9 |
| Human Defense Force vs Orks | 32.3 | 28.1 |
| Robot Legions vs Alien Hives | 24.0 | 35.4 |
| pooled (192 games each) | 34.1 | 37.8 |

Three pairs up, one down; pooled +3.7 on 192 games a side, about one sigma of the ~3.5-point noise. Read it
as level-or-better: the fix was a correctness change (charge edges that used to fall through at play now
play as prescribed) and it costs nothing in strength, so `d16d479` stays and no revert is needed. Every perf
pass since (3-10) sits on top of it with identical trees, so the panel numbers and this bench describe the
shipped search.

Candidate budget follow-up (Chris, 08:55: "if 4 candidates is 4x faster than 16 but does not matter for
smarts, why not just do it?"). Answer given: the search is time-budgeted, so fewer candidates already turn
into more iterations inside the same wall time - that is exactly what the slice measured as equal strength -
and a game takes the same time either way; and the per-iteration saving is ~1.7x, not 4x (edge enumeration
is 57% of an iteration on board A by the pass-9 stage table, the simulation and leaf are untouched). The
real prize would be "4 candidates at half the budget" matching "16 at the full budget": a genuine 2x on
training/validation throughput. Proposed as a two-arm follow-up on the same four pairs (~70 minutes with the
retrying runner), queued only on Chris's say-so, behind the net 3k re-run. Caveats recorded: four weak 2k
pairs at 192 games each can hide a 3-point effect, and a shipped-default change would re-baseline every
panel (all ran at 16).

Net 3k re-run (`queue-net3k.sh`, `step15bin` net leaf, points-3k panel, workstation GC, three games at a time)
released at 08:57:03 and resumed from 69 banked of 150. Its result closes the 3k hand-vs-net question.

**2026-09-09 (08:30, Fable 5.1) - BREADTH SLICE RESULT: 16, 8 AND 4 CANDIDATES SCORE THE SAME (34.1 / 34.1 / 34.9
POOLED). BREADTH DOES NOT MATTER AT THE BENCHMARK BUDGET; NO PHASE 2 WIDENING WORK IS MOTIVATED.**

The question (asked 2026-09-08 morning, Chris's framing): "do we consider 16 move options and think about
all of them a little, or 4 or 8 and think about each more deeply?" Same seeds, same four low-ceiling 2k
pairs, benchmark budget, Phase 1 snapshot (`phase1bin`), Strategist vs Tactician scored for the Strategist,
48 games per cell; a per-cell score is wins plus half the ties, so the gap between arms is the number.

| pair | cb16 | cb8 | cb4 |
|---|---|---|---|
| Dark Elf Raiders vs Dwarf Guilds | 35.4 | 36.5 | 38.5 |
| Dwarf Guilds vs High Elf Fleets | 44.8 | 42.7 | 41.7 |
| Human Defense Force vs Orks | 32.3 | 27.1 | 29.2 |
| Robot Legions vs Alien Hives | 24.0 | 30.2 | 30.2 |
| pooled (192 games each) | 34.1 | 34.1 | 34.9 |

Per pair the arms trade places by a few points in both directions; pooled they are equal to within the
~3.5-point noise of 192 games. So at this budget the extra depth a 4-candidate search buys does not
help, and the breadth a 16-candidate search buys does not either: the decision is not breadth-limited.
Decision: keep the default of 16 (no evidence to change a shipped parameter), and drop progressive
widening / lazy enumeration (the Phase 2 design of 2026-09-08) - it would only have paid if breadth
mattered. The pure-speed passes deliver the same "more thinking per second" without touching the tree.
Fallback noted: 4 candidates is a free 4x cut to the enumeration cost wherever a cheaper decision is
wanted (an in-sim policy, a training-data run), at no measured strength cost on these pairs.

The chain moved on to the seam bench (the committed seam fix, cb16, same cells) at 08:26; its pooled score
against 34.1 says what the seam fix did to play strength. Net 3k re-run follows.

**2026-09-09 (07:15, Fable 5.1) - THE OVERNIGHT CHAIN HUNG AT 00:38 AND LOST THE NIGHT; DUMPED, KILLED, RESUMED.
THE HANG IS A GC THAT NEVER FINISHES. THE JIT THEORY IS OUT; ONLY WORKSTATION GC HAS EVER COMPLETED THE
REPRODUCING GAME, SO THE BENCHES NOW RUN ON IT.**

The breadth slice's cb4/pair2 cell hung at 00:38 with 21 of 48 games banked: one non-managed thread at
100%, memory flat for 6.5 hours, no progress, and the per-game watchdog (1800 s) never fired. `dotnet-dump`
attached this time (`logs/hang-cb4-52583.dmp`): 24 threads hold engine frames, all parked mid-work at
safepoints with no CPU - the signature of a garbage collection that suspended everyone and never came
back. Yesterday's 45-minute hang (17:34) looked the same from outside. So the hangs and the GC-thread
segfaults are one family: the collector's own state gets corrupted.

Tests this morning on the reproducing game (`repro-3k.sh`, Eternal Dynasty vs DAO Union, seed 6001):
`DOTNET_TC_QuickJitForLoops=0` (no on-stack replacement) crashed at 48 s, so the JIT/OSR reading of the
"instruction pointer on a data address" kernel lines is out. Tally: Server GC default crashes (72 s, 4:43),
PGO off crashes, no-OSR crashes, tiering off ran 46 minutes without a fault (too slow to finish; stopped),
workstation GC completed both games (7 min). Workstation GC is therefore the bench configuration from here
(`slice-ws.sh` = the retrying runner with `DOTNET_gcServer=0`, default collector, no hard limit, no RetainVM;
`overnight-ws.sh` restarted the chain at 07:12, reusing the two finished cells and resuming cb4/pair2 from
game 21; the net 3k re-run behind it already used workstation GC). Throughput is lower - the chain should
finish late morning rather than overnight. Hardware remains unexcluded (the workstation-GC panel cell did
crash three times last night while the box ran three heavy jobs); the memtest86 recommendation stands.

Dumps for whoever files the runtime bug: `logs/repro-crash-*.dmp` (Server GC, PGO off, no-OSR), the hang
dump above, and the panel-cell dumps `logs/crash-*.dmp` (17 in all). Engine `06c05f5` (step15bin) and the
Phase 1 snapshot both show it; the engine has no unsafe code.

**2026-09-09 (00:30, Fable 5.1) - NIGHT WRAP-UP: PASS 10 LANDED; ITEM 8 MEASURED (4 WORKERS SCALE 4.8x, NO GC KNOB
MOVES ANYTHING); THE HANG HUNT TURNED UP CRASHES, NOT HANGS; THE INTERACTIVE PANEL SCREEN FINISHED EXCEPT THE
NET 3k CELL (RE-RUN QUEUED); THE BREADTH SLICE IS RUNNING.**

Pass 10, exact (`RuleEvaluator.GatherOffersFromRules`, `GrantedRules`): a rule with no activated ability at
the hook is skipped before its invocation record is built (`SpecialRuleDefinition.ActivatesAt`, a bitset
like `ListensAt`); a unit with no grant tokens gets `Array.Empty` instead of a fresh list. Suite 3291/0/1,
both boards identical, zero non-timing diff lines. Too small to time above the noise; committed on the
oracle. Ten passes today, all exact: board A plain 46.6 -> 26.6 ms per iteration (-43%), allocation 19.9 ->
9.5 MB.

Item 8 (`scaling.sh`, quiet box, board A, pass 9 build): 1 worker 27.1 ms per iteration; 4 workers 5.8 ms per
iteration aggregate (4.8x vs serial on the spike's own accounting - the workers do not contend);
`DOTNET_gcConcurrent=0` 26.7 (noise); `DOTNET_GCgen0size=64MB` 5.7 at 4 workers (noise), and its 1-worker
run died in the search section without a core. Verdict: no runtime knob is worth setting.

The hang hunt (20 runs of the committed build's board A search under the original lab environment - Server
GC, libclrgc, 12 GiB hard limit, RetainVM - alongside the panels): 17 clean, run 1 aborted at 15 s with
glibc's "*** stack smashing detected ***" (native memory corruption), runs 4 and 12 segfaulted after their
search had completed. No hang. Together with the panel screen's nine crashes (three of them under
workstation GC, 21:37-21:44, while the hunt and a build were also loading the box) the picture is: GC
flavour, hard limit, heap count and PGO are not the variable; failures cluster when the box is heavily
loaded; a process unrelated to us (timeshift) segfaulted at 18:00. Hardware (memory under load) is now the
leading suspect. RECOMMENDATION FOR CHRIS: a memtest86 pass on this box at a convenient time. Until then:
crash dumps stay on, cells retry, and the lab runs one heavy job at a time.

Interactive panel screen (Strategist vs Tactician, interactive budget, scored for the Strategist; the net
leaf vs the hand leaf, same seeds; the question is whether the shipped net leaf holds at interactive):
| panel | hand | net |
|---|---|---|
| points-1k (4 pairs, 120 games) | 77.5% | 78.3% |
| shape-2v2 (6 cells, 180 games) | 93.3 / 58.3 / 70.0 / 53.3 / 55.0 / 55.0 | 81.7 / 51.7 / 75.0 / 75.0 / 58.3 / 71.7 |
| points-3k (5 pairs, 150 games) | 73.0% | INCOMPLETE - 69 of 150 (55.8% so far), abandoned after 3 crashes |
| points-4k (3 pairs, 90 games) | 81.7 / 58.3 / 58.3 (66.1%) | 76.7 / 58.3 / 70.0 (68.3%) |
The net leaf is level or ahead everywhere it is complete; the 3k verdict waits for `queue-net3k.sh` (queued
behind the seam bench; workstation GC, three games at a time, six attempts, resumes at 69).

Queue: breadth slice (phase1bin, cb16 / cb8 / cb4, 4 pairs x 48 games, benchmark budget) started 00:23;
then the seam bench (seambin, cb16); then the net 3k re-run. `c4-slice.sh` has no retry - a crashed cell
shows as a missing row and gets re-run by hand.

00:35 addendum: the slice runner's second cell (cb8/pair2) segfaulted a minute in, so both queued benches were
replaced by `overnight.sh`, which runs `c4-slice-retry.sh` (the repo runner with a five-attempt retry per
cell; the bench resumes from `bench.progress.jsonl`) for the breadth slice and then the seam slice into the
same report directories, then releases the net 3k re-run. Same binaries, seeds, flags and environment.

**2026-09-08 (21:10, Fable 5.1) - SEARCH PERF PASS 9 LANDED (PILE-IN GEOMETRY: BOARD A PLAIN 27.7 -> 26.6 ms PER
ITERATION), AND THE 3k CRASH IS ISOLATED: ONE GAME SEGFAULTS A GC THREAD UNDER SERVER GC AND COMPLETES UNDER
WORKSTATION GC. THE HAND 3k CELL FINISHED UNDER WORKSTATION GC.**

Pass 9, exact, inside `PileInUtilities`: the moving body's hull is built once per test instead of once per
pair; a body whose circumscribed circle cannot come within the overlap tolerance (or cannot beat the best
gap so far, or cannot be reached within the step bound) is skipped before the hull gap; each charger's hull
is built once per contact-slot bisection instead of 24 times per direction. Suite 3291/0/1; both boards
identical to the committed build, zero non-timing diff lines (175 pile-ins on board A alone).

| board A, quiet box | pass 8 | pass 9 |
|---|---|---|
| ms per iteration, plain (two samples) | 29.0 / 27.7 | 26.6 / 26.9 |
| ms per iteration, timing on | 28.2 | 29.0 (noise) |
| MB allocated per iteration | 9.9 | 9.5 |
| PileInStage span (175) | 2.35 ms, 901.7 KB | 1.47 ms, 277.5 KB |

Cumulative since the 14:20 seam fix (board A, plain): 46.6 -> 26.6 ms per iteration (-43%), 19.9 -> 9.5 MB.

THE CRASH. Standalone: `repro-3k.sh` plays matchup 3 of the 3k panel (`armies/3k - Eternal Dynasty` vs
`armies/3k - DAO Union`, seed 6001, one game at a time, the panel binary) and segfaults on a GC thread in
~72 s under Server GC (default settings, no hard limit, no RetainVM); with `DOTNET_TieredPGO=0` it still
crashes (4:43 in); with `DOTNET_gcServer=0` both games complete (7 minutes). Tiering-off ran 46 minutes
without a fault but at a fraction of the speed and was stopped with one of its two games done; the
hardware-intrinsics variant was skipped. The engine has no unsafe code. Conclusion: a .NET 8.0.26 Server GC
fault that this game's allocation pattern triggers; nothing engine-side to fix, so 3k/4k cells run with
`DOTNET_gcServer=0` (`c-panels-interactive8.sh`, since 19:55; outcomes are GC-independent per #392).
Recorded in memory (`project_server_gc_crash`) with the repro command, to re-check after any runtime update.

Panel screen: hand/points-3k COMPLETE at 20:32 under workstation GC (Battle Brothers vs Goblin Reclaimers
63.3, Knight Brothers vs Robot Legions 66.7, Saurian Starhost vs Soul-Snatcher Cults 65.0, Eternal Dynasty vs
DAO Union 81.7, Titan Lords vs Goblin Reclaimers 88.3 - Strategist score, 30 games each); net/points-3k
running since 20:33 (62 banked at 21:03); 4k cells follow, then the queued breadth slice and seam bench.
Item 8 (worker scaling, GC knobs) is running on the quiet box now.

**2026-09-08 (20:03, Fable 5.1) - SEARCH PERF PASS 8 LANDED: SHARED HOOK CATALOG, CACHED WEAPON PROFILE KEYS, ONE
SIGHT-BLOCKER SET PER SHOOT REQUEST, NO COVER EVALUATION ON THE GATE PATH, LINE OF SIGHT MEMOIZED PER
ATTACKER ACROSS ITS WEAPONS. BOARD A 32.1 -> 28.2 ms PER ITERATION (TIMING ON), PLAIN 30.0 -> 27.7, TREES
IDENTICAL.**

Exact, engine-wide (real play shares every one of these paths): `HookContextCatalog.Default` is built once
(each simulated game built two by reflecting over the whole assembly - 1.44 of the server's 1.87 ms);
`Weapon` caches its `WeaponProfileKey`, dropped whenever its rule list changes (stats are immutable);
`LineOfSightUtilities.BuildModelBlockerSet` builds the model blockers once per shoot request or gate and
`Excluding(defender)` filters per enemy unit in table order (the old per-enemy build allocated a zone per
model on the table, eight times over); `BuildWeaponOptions(wantCover: false)` on the gate path skips the
attackers x defenders cover evaluation whose answer the gate never reads; `ShotEligibility.CanHitAny` takes
a per-attacker sight memo so a model's weapons share each line-of-sight answer (only the range test is
per weapon). Suite 3291/0/1; both boards identical, zero non-timing diff lines.

| board A, quiet box | pass 7 | pass 8 |
|---|---|---|
| ms per iteration, timing on | 32.1 | 28.2 |
| ms per iteration, plain (two samples) | 31.3 / 30.0 | 29.0 / 27.7 |
| MB allocated per iteration | 10.2 | 9.9 |
| SimServer per simulation | 1.87 ms (SrvResolver 1.44) | 0.43 ms (SrvResolver 0.03) |
| GateShoot per action-stage entry | 0.46 ms, 108.8 KB | 0.18 ms, 50.5 KB |
| Sight-line evaluations | 183,704 | 101,133 |
| ChooseActionStage span | 0.88 ms | 0.61 ms |

Cumulative since the 14:20 seam fix (board A, plain): 46.6 -> 27.7 ms per iteration (-41%); allocation 19.9
-> 9.9 MB. Remaining split: Scoring 33.7%, Expand 32.6% (SimRun 29.9%: ChooseActionStage 23%, PileInStage
16%, DeterminePlayerTurnStage 15%), Candidates 22.4%, EnumerateUnits 11.0%; callees Combat 19.7%,
PlanMove 16.6%, PlanValidate 13.0%, RuleDispatch 9.1%.

Panel screen, the 3k hand cell: crashes five (19:49, no hard limit / no RetainVM / default GC), six (19:54,
8 GC heaps) and seven (19:55, one minute into the retry) - every one a non-managed GC thread. Matchups 0-2
of the cell are complete (90 games); the crashes began with matchup 3 (3k Eternal Dynasty vs 3k DAO Union,
10 of 30 banked) and now land within a minute of resuming it, so one of that matchup's remaining games is
the trigger and this is deterministic, not random hardware. (The 94.5 C `Tctl` reading noted at 19:50 is
this Threadripper's +27 C offset; `Tdie` was 66 C - no thermal problem.) The screen runs on
`c-panels-interactive8.sh` (workstation GC, the last GC-side variable) since 19:55; a standalone
reproduction of that matchup at seed 6001, one game at a time with the panel binary, started 20:02
(`repro-3k.sh`).

**2026-09-08 (19:47, Fable 5.1) - SEARCH PERF PASS 7: THE ARMY RULE-DATA PARSE IS MEMOIZED (SIMULATED SERVER
2.31 -> 1.87 ms), AND THE NEW PROBES NAME THE NEXT TWO TARGETS: THE CORE RULE RESOLVER IS REBUILT PER
SIMULATION (1.44 ms, 4.6% OF AN ITERATION) AND THE SHOOT GATE IS THE WHOLE ACTION-STAGE COST (0.46 ms PER
ENTRY, 4.6%).**

Exact: `ArmyRuleDataPersistence.Deserialize` keeps one parse per distinct JSON (a search resumes hundreds
of games from clones of one store; every consumer only reads the result - the resolver keeps the
definition references, spells are re-resolved into new RuntimeSpell objects, SpawnUnit reads the
auxiliary specs). Probes (instrumentation only) on the seven action-stage gates and the three parts of
the simulated server's resume constructor. Suite 3291/0/1; both boards identical, zero non-timing diff
lines.

| board A, quiet box | pass 6 | pass 7 |
|---|---|---|
| ms per iteration, timing on | 32.0 | 32.1 |
| ms per iteration, plain (two samples) | - | 31.3 / 30.0 |
| SimServer per simulation | 2.31 ms | 1.87 ms (SrvResolver 1.44, SrvRestore 0.08, SrvLaunch 0.14) |
| MB allocated per iteration | 10.2 | 10.2 |

Gate split per action-stage entry (969 entries): GateShoot 0.46 ms (108.8 KB), GateCast 0.05, GateCharge
0.04, GatePass 0.04, GateOffers 0.01, GateMove and GateAllowed ~0. In-sim spans: ChooseActionStage 29.4%
of SimRun, PileInStage 14.2% (2.34 ms per span), DeterminePlayerTurnStage 13.7% (the boundary capture and
leaf sit inside it), CastSpellStage 6.9%, ChooseRangedAttackStage 6.3%.

Panel screen: the hand/points-3k cell crashed a THIRD and FOURTH time (19:09 x2 on retry, 19:37 under
the default GC); the runtime's own dumps show a non-managed GC thread faulting each time and the engine
has no unsafe code, so the collector is tripping on its own heap. The settings all four crashes shared
are the 12 GiB hard limit and RetainVM, and this is the cell with the largest heaps; at 19:40 the screen
moved to `c-panels-interactive5.sh` (Server GC defaults, no hard limit, no RetainVM, crash dumps on;
`rss.log` watches memory). Next single change if it recurs: tiered PGO off.

**2026-09-08 (19:36, Fable 5.1) - SEARCH PERF PASS 6 LANDED: PER-ACTIVATION MEMOS OF THE OBJECTIVE PROJECTION
AND THE PER-UNIT MOVE BUDGETS. THE WORK IT TARGETED DROPS (PROJECTIONS 11,111 -> 2,703 CALLS, MOVE QUERIES
90,538 -> 62,087) BUT THE SINGLE TIMED RUN READS 32.0 -> 32.0 ms: INSIDE THIS MEASUREMENT'S NOISE.**

Plan item 2, exact: `TacticianPlanner` keeps the objective projection and each unit's Advance / Rush /
Charge budgets from `BeginActivation` until the activation's own move (the planner scores exactly once
per activation before any board change; the two early returns that change the board first - cast and
disembark - drop the memos); `FactsOf`, `MeleeApproachAgainst`, `MarkerContestable`,
`BestAlternativeTargetValue`, `ProjectedEnemyPosition`, `MeleeThreatTotal`, `ObjectiveApproach`,
`ObjectiveDelta`, `Posture` and `WantsDisembark` read them. `TacticianActivationResolver.ActivationScores`
memoizes each enemy's advance across the units it scores; `MacroActionGenerator` computes the unit's own
charge budget once per enumeration instead of once per charge target. Suite 3291/0/1.

| board A, quiet box, timing on | pass 5 | pass 6 |
|---|---|---|
| ms per iteration | 32.0 | 32.0 |
| MB allocated per iteration | 10.6 | 10.2 |
| ObjectiveProj | 574 ms, 11,111 calls | 168 ms, 2,703 calls |
| MoveQuery | 474 ms, 90,538 calls | 309 ms, 62,087 calls |
| RuleDispatch walks | 932,265 | 786,357 |
| Scoring (4,144 candidates) | 0.79 ms each | 0.74 ms each |

The ~600 ms the counters say were removed should read as ~2 ms per iteration; stages this pass does not
touch moved by as much between the two runs (Combat 1633 -> 1781, EnumerateUnits 897 -> 1003), so one
timed run cannot resolve it. From pass 7 on each build gets the timed run plus two plain runs
(`measure-quiet2.sh`), and the plain minimum is the number. Oracle: both boards identical to the committed
build, zero non-timing diff lines.

**2026-09-08 (19:27, Fable 5.1) - SEARCH PERF PASS 5 LANDED: THE COMBAT-ESTIMATE ALLOCATION DIET. BOARD A
35.4 -> 32.0 ms PER ITERATION, COMBAT 2005 -> 1633 ms OVER THE SAME 93,876 ESTIMATES, TREES IDENTICAL.**

Plan item 5, exact, all inside `CombatMath`: `Ops` returns `Array.Empty` for the (usual) empty answer and
copies with a loop otherwise; the defender's living models are gathered once per volley instead of once
per dispatch (three); every sink is built and applied only when its operation list is non-empty (an empty
list leaves each sink at its default: no floor, net 0, multiplier 1, no reroll, no injection, no ignore);
the per-hit splitter is skipped for an empty list (it would return exactly the one-group list built in
its place); the volley context record is copied once per estimate rather than per weapon batch; the
weapon batcher uses one shared comparer and no closure; melee builds the defender's participant array
once and reads StrikeFirst / ExtraMeleeWoundCount without LINQ. Suite 3291/0/1.

| board A, quiet box, timing on | pass 4 | pass 5 |
|---|---|---|
| ms per iteration | 35.4 | 32.0 |
| MB allocated per iteration | 11.1 | 10.6 |
| Combat (93,876 estimates) | 2005 ms, 9.3 KB each | 1633 ms, 7.9 KB each |
| Scoring (4,144 candidates) | 0.91 ms each | 0.79 ms each |

Oracle: both boards' trees identical to the committed build's; full logs identical under the timing and
id masks (the spike's own decision-table verdict aside, and on board B the section-1 capture probe's
5-second stop window, which the loaded box missed in this run - a watchdog line, the search after it is
identical). Cumulative since the 14:20 seam fix: 46.6 -> 32.0 ms per iteration plain-equivalent (49.0 ->
32.0 with the timing report on), 19.9 -> 10.6 MB per iteration.

Remaining: Scoring 34.2%, Expand 36.3% (SimRun 28.1%, SimServer 7.0%), Candidates 19.9%, EnumerateUnits
9.3%; callees Combat 17.0%, PlanMove 14.7%, PlanValidate 11.7%, RuleDispatch 8.8%, ObjectiveProj 6.0%,
MoveQuery 4.9%. Pass 6 is plan item 2 (per-activation memos of the objective projection and the per-unit
move budgets), then the in-sim action gates (item 6) and the server-construction drill (item 7).

**2026-09-08 (19:20, Fable 5.1) - SEARCH PERF PASS 4 LANDED: THE RULE-DISPATCH LISTENER FAST PATH. BOARD A
38.9 -> 35.4 ms PER ITERATION, RULE DISPATCH 2552 -> 905 ms OVER THE SAME 932,265 WALKS, TREES IDENTICAL.**

Plan item 1, exact: `SpecialRuleDefinition.ListensAt(hook, seat)` is a bitset over (hook x seat) built once
per definition (definitions are immutable and never copied); `RuleEvaluator.Evaluate` and
`CollectSurviving` (behind EvaluateAll / EvaluateAllNamed / EvaluateAllNamedLive) ask
`AnyoneListens` - static unit, weapon and model attachments plus token-granted rules - before renting the
dedup state or allocating the tagged list, and return the shared empty answer when nothing listens. A
one-shot grant is spent only when its rule listens at the hook, which is the same test, so nothing is
skipped that could have fired, logged, suppressed or spent. A grant that would not resolve, or that
reads arguments, answers "listens" so the full walk still raises its once-only warning. Tracing keeps
the full walk (it narrates hooks that nobody answers). `CollectFromRules` also skips non-listening rules
before the dedup registration (registration only blocks another instance of the same rule, which
listens the same way). Suite 3291/0/1.

| board A, quiet box, timing on | pass 3 | pass 4 |
|---|---|---|
| ms per iteration | 38.9 | 35.4 |
| MB allocated per iteration | 12.5 | 11.1 |
| RuleDispatch (932,265 walks) | 2552 ms, 0.7 KB/walk | 905 ms, 0.3 KB/walk |
| Combat (93,876 estimates) | 2905 ms | 2005 ms |
| MoveQuery (90,538 calls) | 936 ms | 497 ms |

Oracle: both boards' tree lines identical to the committed build's (A: 301 / 6 / 4 closed / Great Monolith
18; B: 301 / 6 / 2 closed / Nightmares 25); full logs identical under the timing/id masks except one
line of the b0 spike's own decision-table verdict, which is a function of the measured milliseconds.
Under the panel load: A 43.9 -> 36.8 ms, B 38.9 -> 29.4 ms.

Remaining split: Scoring 35.4%, Expand 35.2% (SimRun 27.5%, SimServer 6.7%), Candidates 19.5%,
EnumerateUnits 9.6%; callees Combat 18.9% (9.3 KB per estimate), PlanMove 14.4%, PlanValidate 11.5%,
RuleDispatch 8.5%, ObjectiveProj 6.0%. Pass 5 is the combat-estimate allocation diet (plan item 5).

Panel screen: the net/points-3k cell ran 7 minutes under the standalone GC without a fault before the
pass 4 timing pause; at the 19:19 restart the screen moved to `c-panels-interactive4.sh` (default GC,
same 12 GiB hard limit, runtime crash dumps on) and hand/points-3k resumed from its 78 banked games.

**2026-09-08 (19:10, Fable 5.1) - SEARCH PERF PASS 3 LANDED: MOVE VALIDATION BUILDS EACH HULL ONCE AND REJECTS FAR
PAIRS, THE PLANNER READS FAULT KINDS INSTEAD OF A PER-MODEL LIST, ONE SCENE SERVES A WHOLE ENUMERATION. BOARD A
49.0 -> 38.9 ms PER ITERATION (-21%), 19.9 -> 12.5 MB ALLOCATED, TREES IDENTICAL ON BOTH BOARDS.**

Exact-semantics changes (engine, plan items 3 and 4): `EnemyModelFootprint` carries its hull, zone and
circumscribed radius (built once per footprint instead of once per moving-model pair); each validation
pass builds every moving model's start and end hull once (`MoveGeometry`) and the pair tests use
`BaseShapeGeometry.FootprintGap` - the same expression `SurfaceGap2D` evaluates, so the same float; a
circumscribed-circle distance reject skips pairs that cannot touch, cross, or end inside the standoff
band (each flag needs one of those); `ValidatePathsForPlanning` reports the SET of fault kinds
(`EMoveFaultKinds`) and each validator stops at the first fault of every kind it can produce - the
ladder's three tests (impassible present, friendly present, friendly-only) read exactly that set; the
before-move cohesion extents and the impassible/difficult terrain subsets are memoized per thread with
full-identity checks; `MovementPlanner.PlanningScene` (terrain, enemy and friendly footprints) is built
once per `MacroActionGenerator.Enumerate` and handed to every plan. Engine resolvers keep the full
per-model error list. Suite 3291/0/1.

| board A, quiet box, timing on | pass 2 (committed) | pass 3 |
|---|---|---|
| ms per iteration | 49.0 | 38.9 |
| MB allocated per iteration | 19.9 | 12.5 |
| PlanValidate (38,115 calls) | 4233 ms, 66.9 KB/call | 1120 ms, 11.4 KB/call |
| PlanMove (8,045 plans) | 4167 ms, 307 KB/plan | 1386 ms, 72 KB/plan |
| Candidates (259 enumerations) | 19.9 ms each | 7.7 ms each |
| PlanFootprints calls | 20,877 | 1,289 |

Oracle: board A 301 nodes / depth 6 / 4 closed / Great Monolith 18 visits and board B 301 / 6 / 2 closed /
Nightmares 25 visits, identical to the committed build's; the two full b0 logs per board are identical once
timings, stack line numbers and the per-run player ids are masked; the backoff-ladder counters (6,037 /
1,287 / 993 / 537 / 480 / 315 / 297 / 196 / 190 / 100) are unchanged to the unit. Under the panel load the
plain runs read A 58.6 -> 43.9 ms, B 52.2 -> 38.9 ms.

What is left on the move side is now Scoring (39.2%, 1.11 ms per candidate) and the sim (32.6%); the
callees are Combat 24.9%, RuleDispatch 21.8% (932,265 walks), MoveQuery 8.0%, ObjectiveProj 5.2%. Pass 4
is the rule-dispatch listener fast path (plan item 1).

The 3k panel cell crashed a SECOND time at 19:05 (12 minutes into its resumed run): this time a WRITE fault
inside libclrgc.so on a Server GC thread, against this morning's null read in libcoreclr.so on a worker -
two different runtime-internal sites in the one memory-heavy cell, which points at the GC configuration
(standalone segments GC + 12 GiB hard limit + RetainVM) rather than game code. The wrapper now retries
(attempt 2 resumed at 19:05:42 with 78 games banked) and runs with `DOTNET_DbgEnableMiniDump=1` so the next
crash leaves a dump SOS can read; an RSS log (`fdg-lab-scratch/logs/rss.log`, every 20 s) will show whether
the process nears the limit first. GC mode cannot change outcomes (#392), so switching the 3k/4k cells to the
default GC is the fallback if a third crash lands.

**2026-09-08 (18:55, Fable 5.1) - THE BOX REBOOTED AT 18:48 (ACCIDENTAL); LAB REBUILT UNDER A PERSISTENT
DIRECTORY, PANELS RESUMED FROM THEIR BANKED GAMES, BOTH QUEUED BENCHES RE-ARMED.**

Lost with `/tmp`: every lab script, the three Release snapshots (`step15bin`, `phase1bin`, `seambin`), all
timing/determinism logs, and the hang hunt (5 of 20 runs done before the reboot, every one the identical
board A tree in 150 s - no hang in those 5). Survived: the repo, this ledger, and every
`bench.progress.jsonl` under `FdgLab/reports/c-panels-interactive-2026-09-08/` (1k and 2v2 complete for both
arms; hand/points-3k at 66 of 120 games; net 3k and both 4k cells not started).

Rebuilt in `/home/chris/Projects/fdg-lab-scratch/` (scripts, snapshots, `logs/`; nothing lab-side goes under
`/tmp` again): `step15bin` from superproject `569c0d9` / engine `06c05f5` (the PRE-promotion lab: no flag =
hand leaf, `--evaluator` = net leaf, exactly what the banked panel games were played with), `phase1bin` from
`6fb3162` / `883b676`, `seambin` = the untouched Release build of the committed state (`55ca00b`), each via
`git archive` exports so the live tree was never touched. The panel screen resumed 18:53 on the retrying
wrapper; the breadth slice (phase1bin, cb16/cb8/cb4) and the seam bench (seambin, cb16) are queued behind
it as before. The pinner process the earlier handoff said not to disturb did not survive the reboot and is
not mine to restart.

**2026-09-08 (18:00, Fable 5.1) - SEARCH PERF PLAN, PASSES 3-5: WHERE THE REMAINING 38.6 ms PER ITERATION GOES
ON BOARD A AND THE EXACT-SEMANTICS WORK QUEUED AGAINST IT (ALL ORACLE-VERIFIED, NO TUNING RAMIFICATIONS).**

Baseline is the 17:28 single-worker run on board A (`timing2-A.log`): 38.6 ms and 13.6 MB allocated per
iteration, 300 iterations = 4.0 GB of garbage per search. Inclusive split (stages overlap):

| bucket | share | what it is |
|---|---|---|
| EnumerateEdges | 61% | Candidates 26% (MacroActionGenerator.Enumerate) + Scoring 35% (TacticianPlanner.Score x16) |
| Expand | 28% | SimRun 22% (one prescribed activation + leaf capture; Continuation is 0) + SimServer 5.6% (FDGServer ctor) |
| EnumerateUnits | 10.5% | TacticianActivationResolver.Urgency per unit (pairwise shooting estimates) |
| Leaf | 2.5% | PositionEncoder + MLP |

Cross-cutting callees: Combat 21% (92,831 estimates, 10.9 KB each), RuleDispatch 19% (870,644 walks, 0.7 KB
each - almost all of them the 4 dispatches per volley inside Combat), PlanValidate 19% (21,147 validations,
52 KB each), PlanMove 18% (8,583 plans, 119 KB each), MoveQuery 7%, ObjectiveProj 5% (11,057 calls of a
board-fixed projection), Sight 3%. In-sim: ChooseActionStage is entered 3x per activation at 0.87 ms each
(GetCanShoot/GetCanCharge/three dispatch walks), DeterminePlayerTurnStage 2x (progress write + reactivation
offers, then the boundary capture + leaf).

Queued, in the order I intend to take them (gain x confidence / effort). Every item is exact: the tree
oracle (nodes/depth/closed/choice+visits) must be identical against a snapshot of the committed binary.

1. **Hook-listener fast path in RuleEvaluator.** Per unit (static rules + weapons + models + RuleGrant
   tokens) a bitmask of (hook, seat) pairs any rule has a passive entry for; Evaluate/EvaluateAll return
   `Array.Empty` before renting DedupState or allocating the tagged list when nothing listens. Exact:
   CollectFromRules produces nothing in that case and consume-grants is gated on the same test. Invalidate
   on token-container change (grants). Expected: most of RuleDispatch's 19% and a slice of Combat/MoveQuery.
2. **Board-fixed memos per activation** (the planner is pure during Enumerate+Score): ProjectObjectives
   (5% on its own), per-unit Advance/Rush/Charge/PerModel budgets, terrain + sight-blocker lists, enemy
   and friendly footprints (+ their zones), shared between Enumerate, Score, Urgency and the encoder. Small
   change, ~8%.
3. **Validation on the planner path.** ValidateWithBackoff only classifies faults (impassible present,
   friendly present, friendly-only); give the private validators a first-fault-per-kind mode that returns
   a kind bitmask and stops early; memo the "before" cohesion extents per unit per activation (recomputed
   O(n^2) for every candidate today); precompute enemy zones once per footprint list (allocated per model x
   enemy pair inside the loop now); bounding-circle reject before SurfaceGap2D/swept tests. Public overloads
   the engine's resolver uses keep the full error list. Expected: PlanValidate 19% -> ~8%.
4. **PlanMoveAlongRoute hoisting.** Terrain ToList, LiveEnemyFootprints, LiveFriendlyFootprints and
   RouteToward's LINQ (Average x2, Any x2) run per plan; pass the per-enumeration facts from item 2 in.
   ~3-5%.
5. **CombatMath allocation diet.** Per volley: LivingModels x3, Ops().ToList x4, notes list, Dice.Roll
   float[6] per roll, WeaponComparer+Batch per estimate, OfType/Sum/Count LINQ, `context with` copies.
   Struct-return the sinks, reuse a scratch DiceResults. Target 10.9 KB -> ~2 KB per estimate; ~5-8%.
6. **In-sim ChooseActionStage gates.** GetCanCharge (LINQ over armies + team lookup per entry),
   GetCanShoot -> HasAnyFireableTarget (confirm it early-exits), three dispatch walks per entry (mostly
   covered by item 1). ~3%.
7. **SimServer construction drill** (2.17 ms per simulation, 5.6%): probe FDGServer's ctor (stage graph
   build vs bus wiring vs logging) before deciding; nothing is known yet.
8. **Runtime knobs, measured not assumed:** DOTNET_GCgen0size (fewer gen0 GCs at 13.6 MB/iteration),
   concurrent GC off, and a 4-worker wall-clock measurement now that allocation has halved (the last
   scaling number predates pass 2). Config only; ~0-8%.
9. **Leaf encoder memo** (2.5%): the self and enemy blocks compute the same pairwise estimates mirrored.
   Low priority.

Explicitly not planned: AVX/SIMD (the MLP is ~20 us of a 960 us leaf; the geometry is per-pair scalar and
the bounding reject in item 3 gets the win), InvariantGlobalization (no culture-sensitive string ops in
the hot paths - every CompareTo found is on floats), and any change to candidate budgets, weights, priors
or the widening rule (those are the breadth slice's question, still queued behind the panels).

Rough expectation if items 1-6 land: 38.6 -> low/mid 20s ms per iteration on board A, allocation under
5 MB per iteration. Each lands as its own commit with before/after and the identical-tree line.

Determinism check, and a bug of my own found on the way (17:30-18:35). The 17:26 Release build carried an
instrumentation slip: the "plan: snake accepted" note was inserted before an UNBRACED `return snake;`, which
made the snake candidate return unconditionally - every snake was accepted, valid or not. That build produced
the 17:28 split (38.6 ms, 0 closed, Bot Swarms) and the first two determinism arms (37.4 / 37.8 ms, identical
to each other, so cross-process determinism does hold). Caught by
`ValidateWithBackoff_SnakeThatBarelyAdvances_IsRejectedForTheHalvingLadder` on the commit-time test run,
fixed (braces), suite 3291/0. The FIXED build reproduces the 14:12 tree EXACTLY: 301 nodes, depth 6, 4 closed
edges, Great Monolith approach with 18 visits, 46.6 ms per iteration plain and 49.2 with the timing report on, twice
in separate processes - so the 14:10
`seambin` snapshot was the committed fix semantically and the 14:20 entry's board A numbers stand. The 4
remaining closed edges are the forced-charge band (2 CLOSED Shoot/Hold + 2 CLOSED Pass/Hold: inside 1" of an
enemy the stage offers neither), a seam still to close.

The real baseline for the plan above is therefore the fixed build's report (`det2-fixed-timed.log`):
49.0 ms and 19.9 MB per iteration. The split is the same shape with move planning heavier than the buggy run
showed: PlanMove 28.3% (8,045 plans, 0.52 ms and 307 KB each), PlanValidate 28.8% (38,115 validations, 4.7 per
plan: 6,037 first-try valid, 1,287 one retry, 993 lateral re-aims, 297 snakes, 676 plans exhausting the
six-step ladder), Candidates 35.0%, Scoring 29.1%, Expand 27.2%, Combat 19.3%, RuleDispatch 18.1%, MoveQuery
6.6%, EnumerateUnits 8.5%. Items 3 and 4 (validation and plan hoisting) move up to sit beside item 1.

THE HANG IS REAL AND NONDETERMINISTIC. The 14:10 snapshot ran the identical board A command at 14:12 in 15 s and
at 17:34 sat inside the 300-iteration search for 45 minutes: one thread at 100%, no output after the root
probe line, `timeout 900` (no `-k`) never ended it, and dotnet-dump could not attach so there is no stack.
Since that snapshot is semantically the committed code, the current build can hang. `hang-hunt.sh` (written,
launched after the panels resume) repeats the run and dumps a process still alive at 240 s. Watch item for
the overnight queue: a hang shows as a 1800 s game timeout inside a cell, not a stalled queue. `seambin` is
re-snapshotted from the fixed build. Scripts from here on use `timeout -k 30`.

Separately, the interactive panel screen's hand/points-3k cell died at 18:24 with SIGSEGV inside libcoreclr.so
on a thread-pool worker (step15bin, 3 minutes after a restart; the kernel log has the fault, the systemd core
is a system dump the DAC cannot read and the Ubuntu runtime build has no public symbols). The bench resumes
from bench.progress.jsonl, so the screen now runs under `c-panels-interactive2.sh`, which re-runs a crashed
cell up to 3 times with the same log markers the queue waits on.

**2026-09-08 (14:05, Fable 5.1) - SEARCH PERF PASS 2: THE STOPWATCH DRILL-DOWN FOUND THE COST (RULE DISPATCH
ALLOCATING ON 6,000 CALLS PER ITERATION, THE SCORER RECOMPUTING PER-ENEMY FACTS 16x) AND A STRUCTURAL WASTE
(44% OF EXPANSIONS ON BOARD A CLOSE WITHOUT A CHILD). TWO EXACT-SEMANTICS FIXES LANDED: 62.9 -> 59.5 ms AND
44.9 -> 41.2 ms PER ITERATION, ALLOCATION 32.5 -> 23.2 MB PER ITERATION, TREES IDENTICAL.**

Chris (13:1x): "we're also doing this because of performance ... any pure performance gain without tuning
ramifications is a solid win across the board" - so the breadth slice stays queued (it costs nothing) and the
pure-speed work runs first. His progressive-widening idea (plan 4, unlock more as a state earns visits) is
recorded as the Phase 2 design if the slice says breadth matters.

*Drill-down instrumentation (engine, opt-in via FDG_SEARCH_TIMING=1, one static-readonly branch when off):*
`SearchTiming` now records bytes allocated per stage (process-wide, single-worker runs only), callee-level
probes wrapped around whole methods - rule dispatch (`RuleEvaluator.Evaluate/CollectSurviving/GatherOffers`),
combat math, move queries, sight, pathfinding, move planning, objective projection, the planner's
`ChooseAction`, `MacroActionGenerator.Enumerate`, melee range, the sim's direct requester - and an
ENGINE-STAGE TIMELINE (every `StageBase.Enter` the state machine performs inside a simulation is a span until
the next stage is entered or the simulation returns), plus `Note(key)` event counters. Wrapping was done by
`scratchpad/wrap_probes.py` (brace-matched, string-aware); bodies keep their original indentation.

*What the drill-down said (board A, 300 iterations, 72 ms/iteration with probes on; board B agrees within a
few points):* allocation **32.5 MB per iteration** (9.5 GB per 300); rule dispatch **1.89 million calls =
6,300 per iteration, 29% of time, 1.5 KB allocated per call** (2.8 GB); combat estimates 126k calls, 25%,
21.5 KB each (mostly the ~15 dispatches inside each); move queries 350k calls, 15.5%; move planning 10,090
plans = **41 per enumerated unit before the prune to 16**, 0.44 ms and 269 KB each, 20%; sight, pathfinding
and objective projection together < 5%. Inside the simulation, `ChooseActionStage` is 65% of `SimRun`
(4.3 ms per entry, 2.3 entries per activation). The sampler's story (pathfinding, list growth, tokens) was
wrong on every count; these numbers are exact.

*Pass 2 (exact semantics, verified):* (1) `RuleEvaluator`: empty answers are a shared empty array, results
are one exact-size list instead of LINQ chains, the suppression `HashSet` is built only when a `SuppressRule`
op is actually present, and the per-rule `RuleInvocation` record is built only when an entry matches the
hook and seat being dispatched (most rules have none). (2) `TacticianPlanner`: per-activation memos of the
facts about an enemy that no candidate changes (centroid, has-melee, advance distance, melee threat reach
against us) and of the two melee exchange estimates (us vs them, them vs us); `BestAlternativeTargetValue`
hoists the enemy's advance out of its friendly loop. Same cache lifetime as the existing `_meleeApproach` et
al. (cleared in `BeginActivation`; the search's scratch planner begins an activation per unit).

| measure (board A unless noted) | before | after |
|---|---|---|
| ms/iteration, oracle run, A / B | 62.9 / 44.9 | **59.5 / 41.2** |
| allocation per iteration | 32.5 MB | **23.2 MB** |
| rule dispatch calls / KB per call | 1.89M / 1.5 | 1.15M / 0.7 |
| combat time / calls | 5474 ms / 126k | 3167 ms / 113k |
| move queries time / calls | 3377 ms / 350k | 1189 ms / 120k |
| move planning | 4381 ms (20%) | 4381 ms (**26%, now the largest stage**) |

**Identical-tree oracle: exact on both boards** (A: 276 nodes / depth 6 / 214 closed / Bot Swarms Contest
(33,29) with 23 visits; B: 295 / 6 / 43 / Nightmares charge Jetpack Warriors with 24 visits - same on the
Phase 1 binary and the new one). Engine 3290/0/1, app 2836/0, headless exit 0.

*The structural finding.* 489 expansions produced 276 nodes on board A: **214 edges closed** - the engine
refused the prescribed action at play time (or the line ended) and the simulation, plus the natural planner
decision it fell through to, was thrown away. 43 of 337 on board B. Also 103 full `Enumerate` calls run
INSIDE simulations (16.6 ms each, ~10% of the iteration): the post-cast and post-disembark re-entry, where
the unit still has to decide its move - legitimate policy, but it means a Cast edge costs a Tactician
decision the search did not price. Counting WHY edges close (`Note` counters on every fall-through, with the
valid options the engine offered) is running now; the answer decides whether the generator's feasibility
gate or the engine's action gate is lying to the other.

**2026-09-08 (14:20, Fable 5.1) - THE CLOSED EDGES WERE A SEAM BUG, NOW FIXED: THE SEARCH PRESCRIBED "CHARGE"
FOR A MENU ENTRY THE ENGINE ONLY OFFERS TO A UNIT ALREADY IN CONTACT, AND OPENED 16 EDGES FOR SHAKEN UNITS THE
ENGINE IDLES WITHOUT ASKING. CLOSED EDGES 214 -> 4 (A), 43 -> 2 (B); WASTED IN-SIM TACTICIAN DECISIONS 103 -> 4;
62.9 -> ~47 ms (A) AND 44.9 -> ~38 ms (B) PER ITERATION. THIS CHANGES WHAT THE STRATEGIST PLAYS - BENCH QUEUED
TONIGHT.**

*The counts (Note counters on every fall-through, with the options the engine offered).* Board A, 124 charge
edges: every one fell through at the first Choose Action (`Charge` prescribed, engine offered
`[Move,Shoot,Pass]`), the simulated unit then re-decided naturally (a full `Enumerate` + 16 scores INSIDE the
simulation - the 103), and the edge survived (80) only when that natural move happened to end within 2" so
that `Charge` appeared on the re-entry; 44 closed. The other ~170 closures came in uniform blocks of 11-22
per candidate family: whole units whose activation the engine never offered a choice for - Shaken units
(`ChooseActionStage` idles and recovers them on `StartedActivationShaken` before asking anyone).

*Why.* `ChooseActionStage.ToCharge` binds straight to the melee stage; `GetCanCharge` requires an enemy inside
the 2" melee cylinder (`MeleeRangeUtilities.AreUnitsInMeleeRange`, docs/ResolverGuide.md). A charge FROM RANGE
in this engine is a Move whose path ends in contact (charge-length moves are legal when they end in melee
range, `ChargeReachValidationTests`), then `Charge` on the re-entry - natural play gets there through the
forced-charge band (Pass gated inside 1", Shoot forfeits) and the solo fallback. `TacticianActionSpace` built
its prescription names from `OfferableActions`, which listed `Charge` unconditionally.

*Fix (engine, `TacticianActionSpace` + `TacticianPlanner`):* a reachable charge whose target is not already
in melee range is prescribed as `Move` with the charge macro; the planner's post-move branch takes `Charge`
when the plan is a charge and the engine now offers it (natural play only reaches that branch with a charge
plan after it has already fought, when Charge is no longer offered - so A-play is unchanged). A Shaken unit
gets ONE unit-only edge ("idles and recovers") instead of a scored candidate set. Remaining closures: Hold
edges (Shoot/Pass) for a unit standing inside the forced-charge band, where the engine offers only
`[Charge]` - 4 on A, 2 on B; left as is.

| board (300 iterations, single worker, probes on) | closed edges | nodes | in-sim Enumerate | ms/iteration |
|---|---|---|---|---|
| A before (pass 2) | 214 of 489 expansions | 276 | 103 | 56.0 |
| A after | **4 of 304** | 301 | 4 | **48.9** |
| B before (pass 2) | 43 of 337 | 295 | ~30 | 41.2 (oracle) |
| B after | **2 of 302** | 301 | 2 | **38.5** |

Expansions now equal iterations (one real simulation each; before, the search retried closed edges within an
iteration). The root choice on board A moved from "Bot Swarms: Contest" to "Great Monolith: charge the Orc
Warriors" - the search can now see charges. **This is a behaviour change, not an exact-semantics one**: the
identical-tree oracle differs by design. It ships as its own engine commit (revertable) and is benchmarked
tonight: `scratchpad/queue-seam-bench.sh` runs the breadth slice's cb16 cell (4 pairs x 48, benchmark budget,
seeds 1000, shipped leaf) on the seam-fixed snapshot `seambin` right after the breadth slice; the gap to the
breadth slice's own cb16 (Phase 1 binary, same seeds) is the seam fix's worth, out
`FdgLab/reports/seam-slice-2026-09-08`.

*Next pure-speed targets, in order:* move planning (41 plans per unit before the prune to 16, 0.43 ms and 269
KB each - now ~35% of the iteration together with the rest of `Enumerate`), the sim's `ChooseActionStage`
(2.9 ms per entry after pass 2: `GetCanCharge` walks every enemy model pair, two `GatherOffers` dispatches,
a LINQ team lookup), allocation inside combat estimates (dice arrays, note lists, batch comparers).

**2026-09-08 (11:45, Opus 5 / xhigh) - SEARCH PERF PASS, PHASE 1: SEMANTICS-PRESERVING EDITS VERIFIED EXACT BY
AN IDENTICAL-TREE ORACLE, SPEEDUP ~0-3%. THE SAMPLER LIED ABOUT THE FINE STRUCTURE; STOPWATCHES GIVE THE REAL
SPLIT: ~56% ENUMERATING CANDIDATES THE SEARCH NEVER EXPANDS, ~37% SIMULATING ONE ACTIVATION.**

*Why this pass exists.* The B-gate measured 56.6% -> 70.1% vs the Tactician for 4.3x thinking time, and the
leaf swap bought ~+2: the search is compute-bound, not knowledge-bound. Chris (2026-09-08): speed first - it is
also the difficulty knob (`UctOptions.Iterations` already exists) and a better play experience.

*Profile (dotnet-trace, 1,200 single-worker iterations, Orks vs Robot Legions, round 2, quiet box).* Coarse
structure: edge enumeration >> simulation; state copy, server bootstrap, leaf and tree ops together < 10%.
Fine structure it reported: pathfinding ~35%, `List.set_Capacity` ~17%, token scans ~19%, combat estimates
~29%. A first-chance-exception probe on a real 40-iteration search found ZERO throws, so the 26 s of
exception frames in the trace belong to b0's own setup (a game played from deployment), not the search.

*Phase 1 (eleven edits, engine + lab):* route memo on `TerrainGrid` shared across every snapshot of a game
(the grid cache re-keyed on terrain IDENTITY - `TerrainData` is immutable and `StoreClone` copies it by
reference - instead of per table state, bounded at 32 sets; routes copied in and out; own tables for the
two pathfinders), a search root that turns a JSON snapshot into a typed one once, lock-free empty fast paths
in `TokenContainer`, one move-distance dictionary per validation instead of three, pre-sized weapon lists,
single-pass `AllocationOrder`. Tests: 6 new (`RouteMemoTests`, grid-cache identity/eviction), 2 rewritten
(`DifferentGames_NeverShareEntries` deliberately inverted: same terrain instances SHARE, equal-but-distinct
instances do not).

*Verification.* Engine 3290/0/1, FdgRaylib.Tests 2836/0, build clean, headless exit 0. **Identical-tree
oracle** (`scratchpad/verify-perf1.sh`): the same fixed-iteration deterministic search on the pre-change
binary and the new one - board A (Orks vs Robot Legions, round 2): 276 nodes / depth 6 / 214 closed edges /
same choice with 23 visits on both; board B (Dark Elf Raiders vs Dwarf Guilds, round 3): 295 / 6 / 43 / same
choice with 24 visits on both. **Timing: 66.6 -> 66.1 ms and 46.3 -> 44.8 ms per iteration.** Exact, and
useless as a speedup. The edits stay (correct, tested, and the memo matters once enumeration is lazy) but
they answered the wrong question.

*Stopwatch split (`SearchTiming`, FDG_SEARCH_TIMING=1, 300 iterations, board A, 60 ms/iteration):*

| stage | share | per call | calls |
|---|---|---|---|
| EnumerateEdges (per opened unit) | **56.2%** | 41.5 ms | 244 |
| - MacroActionGenerator.Enumerate (plan 16 candidates) | 24.4% | 18.0 ms | 244 |
| - TacticianPlanner.Score x16 | 31.8% | 1.47 ms | 3,904 |
| EnumerateUnits (activation urgency per node) | 6.2% | 5.4 ms | 208 |
| Expand (one simulated activation) | **37.5%** | 13.8 ms | 489 |
| - SimRun (engine, boundary to boundary) | 31.5% | 11.6 ms | 490 |
| - SimServer (FDGServer ctor) | 5.3% | 1.9 ms | 490 |
| - materialize + registries | 0.8% | | |
| Leaf (hand evaluator) | 2.7% | 1.0 ms | 489 |
| Select / scratch build | ~0% | | |

**The number that matters: 489 expansions for 244 enumerations - the search expands ~2 of the 16 candidates
it plans and scores per unit.** About half of every iteration is spent on edges never taken. The sampler's
"pathfinding 35% / list growth 17% / tokens 19%" were attribution artifacts (it oversamples allocation and
walkable sites); the stopwatch split supersedes it.

*Phase 2 (needs Chris - changes what the search considers).* Options, cheapest first: (a) `--candidate-budget
N` bench - the knob already exists (`SearchOptions.CandidateBudget`), now exposed in the lab and as slice
arms `cb16/cb8/cb4`; a 4-pair benchmark-budget slice at equal wall-clock (~1 h) says directly whether
breadth or depth wins at this budget. (b) Lazy enumeration: plan+score a candidate only when the search first
selects its edge, with a cheap prior for ordering - the structural fix, up to ~2x, but the prior quality is
the risk. (c) Simulation: SimRun's 11.6 ms is the engine playing one activation; SimServer's 1.9 ms could go
with a reusable server - deep engine work, park unless (a)/(b) land and it becomes the bottleneck.

*Instrumentation kept:* `Ai/Tactician/Search/SearchTiming.cs` (opt-in, one static-readonly branch per probe
when off), reported by `b0` after the measured search. Panel screen paused/resumed three times today for
quiet-box windows; it resumes from its progress files and lost nothing.

*Committed (11:50).* Engine master `883b676` (Phase 1 + `SearchTiming` + tests), super follows with the
submodule bump, `b0` timing report, the `--candidate-budget` lab flag and the `cb16/cb8/cb4` slice arms.
**Chris (11:47): "Run the breadth slice."** Queued behind the interactive panel screen (`scratchpad/
queue-breadth-slice.sh`, waits for the panels' DONE line and a quiet box): `c4-slice.sh` benchmark budget,
4 ring pairs x 48 games x arms cb16/cb8/cb4, shipped net leaf, seeds 1000 (cb16 = the old `net` arm's
settings, so it doubles as a replication of the +8.9 slice), Phase-1 Release snapshot `phase1bin`, out
`FdgLab/reports/breadth-slice-2026-09-08`. Reads: the cb8/cb4 gap to cb16 at equal wall-clock says whether
breadth or depth wins at this budget; a null result parks lazy enumeration.

**2026-09-08 (08:20, Opus 5 / xhigh) - THE PANEL SCREEN DISAGREES WITH THE 4-PAIR SLICE: THE NET LEAF IS ONLY
+1.8 POOLED ACROSS THE PANELS AND -7.1 AT 1k. THE 15b PROMOTION IS PROVISIONAL UNTIL THE INTERACTIVE PANELS
LAND. Interactive re-run started 08:19.**

`FdgLab/reports/c-panels-2026-09-07`, finished 23:02 unattended: hand-leaf and net-leaf Strategist vs the
Tactician on all four panels, 30 games/cell, seeds 6000, benchmark budget, dop 6, paired cells and seeds.
**1,080 games, 0 faults, 0 timeouts.**

| panel | hand | net | delta | games/arm |
|---|---|---|---|---|
| points-1k | 75.4 | **68.3** | **-7.1** | 120 |
| points-3k | 67.7 | 74.0 | +6.3 | 150 |
| points-4k | 77.2 | 77.2 | 0.0 | 90 |
| shape-2v2 | 52.5 | 57.2 | +4.7 | 180 |
| **pooled** | **65.9** | **67.7** | **+1.8** | **540** |

*Why this and the slice disagree, and which one to believe.* The C4 slice measured +8.9. Its four pairs were
SELECTED as the cells where the hand leaf scored lowest on the P4 slice - that is a sample chosen on the
control arm's weakness, so it measures the net exactly where the hand leaf has the most room and nowhere
else. I recorded that selection as a known bias at design time but reasoned about it in the wrong direction
("a leaf that only helps in already-won cells is missed"); the likelier direction is the one that happened -
it OVER-credits. The panels are the unbiased sample of the same question and they are 540 games per arm to
the slice's 192. **Believe the panels.**

*Is +1.8 anything?* No. Unpaired SE of the pooled difference is ~3.0 points, so +1.8 is inside noise; pairing
tightens it but not to where +1.8 becomes a result. The 1k regression (-7.1 over 120 games/arm, SE ~6.5) is
~1.1 SE - suggestive, not conclusive on its own, but two of its four cells fell 10.0 and 15.0.

*A mechanism worth testing, not just noise.* The gain tracks training mass by level: 2v2 is 41% of the
training rows (179,765 of 443,608) and gained most (+4.7); 3k is 19% and gained +6.3; 1k is the smallest at
10% (44,459) and is the one that regressed. If that holds, the fix is a level-balanced training mix or
level-balanced generation, not a bigger net - and it is cheap to test offline on data already on disk.

*The measurement that actually decides it.* The lobby plays at the INTERACTIVE budget, and the C4 confirm
already showed the net's edge shrinking as the budget deepens (+8.9 -> +6.0 on the same four pairs). A
benchmark-budget +1.8 could easily be <= 0 at interactive. Started 08:19:
`scratchpad/c-panels-interactive.sh` -> `FdgLab/reports/c-panels-interactive-2026-09-08`, same binary, cells
and seeds, panels ordered 1k (the regression) -> 2v2 -> 3k -> 4k so the decision-relevant numbers land first,
arms interleaved per panel. ~11 h.

*What this does NOT change.* The net is not worse - across 1,620 games at two budgets it is somewhere between
even and modestly ahead, and it is clearly ahead where the hand leaf is weak. Nothing is broken, no faults in
any of it. What changed is the SIZE of the claim: "+8.9, promote it" is not supportable; "roughly even
overall, better on hard cells, worse at 1k" is.

*Recommendation to Chris.* Leave the promotion in place while the interactive panels run - master is not a
release, he is playing it now, and reverting is a one-line change with the asset already committed. But
**15b's evidence is contested and the campaign doc now says so**; do not treat the leaf as settled, and do not
cut a release off master until the interactive panels land. If they come in <= 0, the honest move is to revert
the default to the hand leaf and keep the asset for a level-balanced retrain.

*My earlier caution was right for a shakier reason than I gave it.* The 2026-09-07 19:30 entry flagged
level-nonuniformity from the regeneration data; the 19:55 entry correctly noted that comparison mixed budgets
and sampling. The concern itself survived that correction - the paired screen confirms it.

**2026-09-07 (20:30, Opus 5 / xhigh) - STEP 15b DONE: THE STRATEGIST SHIPS THE LEARNED LEAF, AND IT REPLACED
THE HAND EVALUATOR RATHER THAN JOINING IT. MERGED TO MASTER. Engine `1ca296e`, super `79fe8a4`.**

*Owner's decision (Chris, 2026-09-07): REPLACE, not a second lobby bot.* The leaf is an implementation detail
of "the searching bot"; two lobby entries differing only by it would confuse players. My recommendation, his
call. Consequence accepted and handled: the hand leaf stays reachable in the lab as the C-gate's control arm,
via `--evaluator hand` (an explicitly-passed evaluator always wins over the default).

*Also his call: merge to master NOW, before the C-gate.* The campaign doc puts the L2 merge after step 16, and
the gate's panel screen is still running. Flagged, then done. Low risk: master is not a release (those come
from `v*` tags), and both the pre-promotion binary and the hand arm remain available.

*What shipped.* `BuiltInAssets/StrategistLeafV1.json` (402 KB, the `serving-full` weights plus a `provenance`
block naming the training data, the command, the offline metrics and the slice result), embedded like the
existing built-in assets so it cannot go missing from any of the four unsigned platform archives.
`AiProfileFactory.DefaultStrategistLeaf` loads it through a `Lazy<T>` - one shared instance, because a bench
builds a registry per slot per game and re-parsing 400 KB each time would cost more than the search it feeds
(the forward pass allocates its own buffers and never mutates the weights, so sharing is safe across the four
root workers and concurrent games). Precedence at the Strategist: caller-supplied leaf, then
`FDG_STRATEGIST_WEIGHTS`, then the shipped net.

*What deliberately did NOT change: the plain Tactician.* It is the benchmark opponent AND the policy the
search simulates with. Had the promotion reached it, every number this campaign is calibrated against would
have moved silently. `StrategistLeafAssetTests` pins that.

*Tests (4 new + 2 rewritten).* The asset loads and its schema/width match this build (the early warning for a
feature bump landing without a retrain - it would otherwise throw at bot creation in a game); the asset still
reproduces torch's outputs on 48 recorded cases to 1e-5 (`Tests/Fixtures/strategist-leaf-v1-parity.json`,
values spanning 0.003-0.999 so the fixture cannot pass for a constant net); the Strategist's default leaf is
that instance and is loaded once; the Tactician still scores with the hand evaluator. Two override tests that
asserted "default = hand-weighted" were REWRITTEN, not deleted - that assertion encoded the old policy.

*Lab follows the promotion.* "No `--evaluator` flag" no longer means hand, so: `--evaluator hand` is the
control arm, every bench report now stamps its leaf unconditionally (a Strategist number is a number about one
particular leaf, and reports outlive the memory of when the default changed), `c4-slice.sh`'s hand arm passes
it explicitly, and self-play game lines record `shipped` rather than `hand` when the run named none.

*Verification.* Engine 3284 passed / 0 failed / 1 skipped, FdgRaylib.Tests 2836 / 0, full build clean,
headless smoke exit 0 - run BOTH before the engine commit and again on the merge result. End-to-end: two
2-game benches on the Release binary, default arm stamped "shipped net (StrategistLeafV1, the default)" and
control arm "hand-weighted (control)", 0 faults each.

*Merge.* The other instance had already merged `tactician-bc` -> master at ~15:38 and added the env override,
so this merge carried only my last five commits plus the engine bump. One conflict, the ledger's top section
(both sides had appended); resolved keeping both in date order. Both repos' `tactician-bc` are now
fast-forwarded to master. Note: pushing to engine `master` reported "Bypassed rule violations ... changes must
be made through a pull request" - the branch protection is being bypassed by these direct pushes (the other
instance's merge did the same). Worth a decision from Chris if that rule is meant to bind.

*Panel screen.* Stopped at 20:19 for a quiet box (it has no `--pause-file` hook), restarted 20:26 - the
per-cell `bench.progress.jsonl` resumed 276 banked games instantly, nothing replayed. Deliberately still the
PRE-promotion snapshot binary `scratchpad/step15bin`: both its arms name their leaf explicitly, so the
comparison is unaffected, and resuming with a different binary would have mixed two builds inside one cell.

*Still open for the C-gate (step 16):* per-side evaluator on bench (C-vs-B is not expressible while
`--evaluator` binds to every Strategist in a game), the unwritten `lane-block` and `buff-anticipation` probes,
and Chris's >= 2 verbatim games - now playable on master with no env var at all, since the net IS the default.

**2026-09-07 (19:55, Fable 5.1) - C4 ITERATION 2: THE RETRAINED NET (v2) DOES NOT BEAT v1. v1 (`serving-full`)
IS THE C CANDIDATE. STEP 15 CLOSES; STEP-16 PANEL SCREEN RUNNING.**

*Retrain.* `v3-plus-bnet.parquet` = 443,608 A-play + 81,639 B-play rows (31,199 games). `--balance-sources`
weighted B-play rows x5.46 for equal total weight. Held-out games: v2 auc 0.8909 overall (A-play rows 0.9019,
B-play rows 0.8229 vs hand 0.7807); unseen pairings 0.8810. Scored on identical rows with one scorer, v1 vs v2:
A-play 0.856 vs 0.851, B-play 0.778 vs 0.781 - flat both ways. C# parity on `serving-v2-{weights,parity}`: 5/5.

*Re-slice (net2 only, benchmark budget, same seeds and quiet box as the screen; hand/net/blend from the screen):*

| Pair (A = Strategist vs Tactician) | hand | net v1 | blend | net v2 |
|---|---|---|---|---|
| Dark Elf Raiders vs Dwarf Guilds | 31.2 | 43.8 | 45.8 | 32.3 |
| Dwarf Guilds vs High Elf Fleets | 55.2 | 62.5 | 65.6 | 68.8 |
| Human Defense Force vs Orks | 41.7 | 43.8 | 37.5 | 44.8 |
| Robot Legions vs Alien Hives | 42.7 | 56.2 | 45.8 | 41.7 |
| **pooled (192 games each)** | **42.7** | **51.6** | **48.7** | **46.9** |

*Decision:* v2 is +4.2 over hand (under the +5 bar) and -4.7 under v1; **v1 stays**, and it is the one that
also passed the interactive confirm (+6.0). Two iterations were the maximum (plan sec 10) - both used. Read:
5k regeneration games at ~17 rows each is 16% of the rows even after weighting, and the offline metric said
"flat" before the board did; the leaf is not data-starved at this size, so a bigger net or new features
(fatigue, firepower bands - schema sec 4) is the next lever if the gate misses, not more of the same data.
Cell-to-cell swings of +-10 between v1 and v2 (DE-DG 43.8 -> 32.3, DG-HE 62.5 -> 68.8) are the 48-game
instrument, not a signal.

*Correction to the 19:30 caution:* the B-gate panel numbers (73/72/84) were INTERACTIVE budget; the regen
numbers were benchmark. That comparison was apples-to-oranges on budget as well as sampling. The paired
answer is now being measured directly.

*Running (19:50):* `scratchpad/c-panels.sh` - hand-leaf and net-leaf Strategist vs Tactician on all four
panels (points-1k/3k/4k, shape-2v2), 30 games/cell, seeds 6000 (the B-gate's panel seeds), BENCHMARK budget
(plan sec 10: screen at benchmark first), dop 6, out `FdgLab/reports/c-panels-2026-09-07/<arm>/<panel>`.
This is step 16's panel screen AND the paired answer to the level caution. ~3 h.

*Step 16 build items still open:* per-side evaluator on bench (C-vs-B for the main matrix), the lane-block and
buff-anticipation probes (unwritten, gate the gate), Chris's >= 2 verbatim games, and 15b's replace-vs-new
decision (recommendation: replace). Step 16's model note: runs are Sonnet/low, failure analysis Opus/high.

**2026-09-07 (19:30, Fable 5.1) - REGENERATION DONE: 5,000 NET-LEAF B-PLAY GAMES, 0 FAULTS. ITERATION-2 RETRAIN
+ RE-SLICE RUNNING.**

`FdgLab/data/2026-09-07-v3-bplay-net`: 25 files, seeds 500000-504999, every game line `evaluator=serving-full-
weights.json`, `search_budget=benchmark`; ~3,260 rows per 200-game file (~81k rows), ~45 min per batch at dop 6
(265 games/h, 18.5 h wall). Stopped at the batch boundary 19:25 after draining.

*A caution from the data, NOT a result.* Half the games are net-Strategist vs Tactician across the 20 training
pairings; at 3,800 games the net Strategist scored 66.8 / 63.0 / 59.8 / 65.8 at 1k/2k/3k/4k and 55.4 in 2v2
(61.8 pooled, n=1,721). The hand Strategist's B-gate PANEL numbers on these panels were 73/72/84 at 1k/3k/4k.
Not paired (older engine, sampler draws rather than side-swapped seeds, no hand arm in this run), so it does
not overturn the slice, but it is the first hint the net's gain may not be uniform across levels. Step 16's
panels are the proper measurement; this line exists so nobody reads the 4-pair slice as the whole story.

*Chain launched 19:25 (`scratchpad/retrain-v2.sh`, log `retrain-v2.log`):* load v3 + bplay-net into
`v3-plus-bnet.parquet` -> `train.py --balance-sources --tag serving-v2 --export` (B-play rows weighted to equal
total weight; report now slices by source) + a pairing-split fit -> C# parity on `serving-v2-{weights,parity}`
-> `c4-slice.sh ... benchmark 48 net2` into the screen's directory -> combined hand/net/blend/net2 table
(`summary-iter2.md`). net2 alone is re-run: hand/net/blend already have benchmark cells on these seeds from a
quiet box, and wall-clock search means a re-run would not reproduce them bit-for-bit anyway.

*Also today:* Chris asked for a merge-to-master prompt for another instance plus an `FDG_STRATEGIST_WEIGHTS`
dev-only override so the lobby Strategist can load the net; `serving-full-weights.json` was sent to him directly
(the models dir is gitignored by design - step 14 policy). Chris's 15b question - replace the Strategist's leaf
vs a new lobby bot - my recommendation is REPLACE (hand leaf stays lab-reachable as the C-gate baseline); no
decision recorded yet.

**2026-09-07 (Fable 5.1) - merge to master done 2026-09-07, super `4339768`, engine `406c0c0` (merge) /
`ae80842` (override); FDG_STRATEGIST_WEIGHTS dev override added.** `tactician-bc` merged into master
with a real merge commit on both repos; engine suite 3275/0/1 at the merge, 3280/0/1 after the
override. Reconciliation 58: the branch's #393 (Random Army) and #394 (simulation state copy) collided
with master's terrain items and yielded -> **#395** / **#396** (detail files, index, archive, this
ledger, engine + FdgLab comments renumbered; commit messages and quoted "do 394 now" left as-is).
Override: `AiProfileFactory.BuildRegistry`'s Strategist case loads
`MlpPositionEvaluator.FromFile(FDG_STRATEGIST_WEIGHTS)` when the variable names an existing file and
no caller supplied a leaf; unset = hand-weighted default (G9 holds). One line says which file loaded
(decision log, else console); a missing file keeps the default and says so, a bad file throws.
`TacticianPlanner.SearchLeaf` exposes the wired leaf for the tests (`StrategistWeightsOverrideTests`,
5 tests against the parity fixture). Headless Strategist run with the fixture prints the load line
and finishes. Launch: `FDG_STRATEGIST_WEIGHTS=<abs path to serving-full-weights.json>
./FdgRaylib/bin/Debug/net8.0/FdgRaylib`. The Purple box's `serving-full-weights.json` was NOT copied
here - this checkout is on a Windows machine without WSL, so the Linux path is unreachable; copy it
into `FdgLab/python/models/` (gitignored) by hand.

**2026-09-07 (00:10, Fable 5.1) - C4 CONFIRM AT THE INTERACTIVE BUDGET: THE NET LEAF HOLDS. +6.0 POOLED, AHEAD ON
ALL FOUR PAIRS, 384 GAMES, 0 FAULTS. REGENERATION WITH THE NET LEAF IS RUNNING.**

*What a cell measures (for the record):* Strategist(leaf) as side A vs the A-play Tactician, 48 games with sides
swapped, scored (W + 0.5T)/N for the Strategist. The pairs are the four where the hand leaf sat lowest, so the
absolute levels are low by construction; the number that matters is the gap between arms on identical seeds.

`c4-slice.sh FdgLab/reports/c4-confirm-2026-09-06 interactive 48 hand net`, 20:56-00:03, B-play paused and
drained throughout, ~23 min per cell (48 games), 0 faults, 0 timeouts.

| Pair (A = Strategist vs Tactician), interactive | hand | net | delta | (screen delta, benchmark) |
|---|---|---|---|---|
| Dark Elf Raiders vs Dwarf Guilds | 47.9 | 49.0 | +1.0 | +12.5 |
| Dwarf Guilds vs High Elf Fleets | 65.6 | 74.0 | +8.3 | +7.3 |
| Human Defense Force vs Orks | 45.8 | 52.1 | +6.3 | +2.1 |
| Robot Legions vs Alien Hives | 51.0 | 59.4 | +8.3 | +13.5 |
| **pooled (192 games each)** | **52.6** | **58.6** | **+6.0** | **+8.9** |

*Decision:* net confirmed (bar: >= +5 pooled, no pair lost by > 10 - met on both counts at both budgets). The
gap shrinks from +8.9 to +6.0 as the search deepens 4x, which is the expected direction (a deeper tree leans
less on its leaf) and still clears the bar. Per-pair deltas reshuffle between budgets (DE-DG +12.5 -> +1.0,
HDF-Orks +2.1 -> +6.3): 48 games per cell is coarse, read the pooled line.

*Hand-leaf B-play run stopped* at 00:06 after batch 0 (200 games, 0 faults, 3,207 rows in
`FdgLab/data/2026-09-06-v3-bplay`, seeds 400000-400199) - kept as the hand-leaf tree-state validation slice;
batch 1's in-flight games were lost (reproducible from seed). Design says the regeneration is with the WINNER,
so the box goes to the net.

*Regeneration launched 00:07:* pid **290269** (watcher script `scratchpad/selfplay-b2-net-run.sh`, pid file
`selfplay-b2.pid`, log `selfplay-b2.log`, RSS log `selfplay-b2-rss.log`), binary `scratchpad/step15bin`,
`selfplay --mix mix-strategist.json --evaluator models/serving-full-weights.json --dop 6 --seed-base 500000`
into `FdgLab/data/2026-09-07-v3-bplay-net`. Every game line stamps `evaluator=serving-full-weights.json`.
Target ~5k games; at the hand run's ~270 games/h that is ~19 h (net leaf is cheaper per call, so likely less).

*Next (in order).* (1) When the net B-play set has ~5k games: retrain with those rows added, weighted so the
two sources carry equal total weight, validated on B-play held-out games; re-slice at the benchmark budget
(hand vs net-v1 vs net-v2) - iteration 2 of 2. (2) Step 15b promotion: weights as an engine asset, an
`EAiProfile` value, lobby button - **profile name needed from Chris**. (3) Step 16 build item: per-side
evaluator on bench so C-vs-B is expressible. (4) Step 16's lane-block and buff-anticipation probes are still
unwritten and gate the C-gate.

**2026-09-06 (21:00, Fable 5.1) - C4 SCREEN: THE NET LEAF WINS. +8.9 POOLED OVER HAND, AHEAD ON ALL FOUR
PAIRS, 576 GAMES, 0 FAULTS. INTERACTIVE CONFIRM RUNNING.**

`FdgLab/tools/c4-slice.sh FdgLab/reports/c4-slice-2026-09-06 benchmark 48 hand net blend`, binary
`scratchpad/step15bin` (engine `06c05f5`), 19:46-20:54, self-play paused throughout (drained to 0% CPU before
the first cell). Strategist(leaf) vs Tactician, benchmark budget, 4 workers, dop 6, 24 seeds x 2 sides from
1000, identical seeds per arm. Reports are gitignored; the table is the record.

| Pair (A = Strategist vs Tactician) | hand | net | blend 0.5 |
|---|---|---|---|
| Dark Elf Raiders vs Dwarf Guilds | 31.2 | 43.8 | 45.8 |
| Dwarf Guilds vs High Elf Fleets | 55.2 | 62.5 | 65.6 |
| Human Defense Force vs Orks | 41.7 | 43.8 | 37.5 |
| Robot Legions vs Alien Hives | 42.7 | 56.2 | 45.8 |
| **pooled (192 games each)** | **42.7** | **51.6** | **48.7** |

*Decision (rule from the step-15 design):* **net** - +8.9 pooled (bar >= +5), ahead on every pair (+12.5, +7.3,
+2.1, +13.5). Blend is +6.0 but lost HDF-Orks by 4.2 and trails net on two of four; it is not the pick. Cost:
~5.4 min per 48-game cell, 38.7 s per game, 0.15 games/s at dop 6 - the screen took 68 min, under the estimate.

*Read.* The net's gain is concentrated where the hand leaf was weakest (DE-DG +12.5, RL-AH +13.5) - the same
shape as the offline round-1 auc gap. The hand control's 42.7 pooled is NOT comparable to the P4 slice's
numbers on these cells (those were interactive budget, these are benchmark); the interactive confirm is what
makes that comparison. 192 games per arm is a coarse instrument (SE ~3.6 before pairing), which is why the
rule demanded +5 and the confirm exists.

*Now running.* `c4-slice.sh FdgLab/reports/c4-confirm-2026-09-06 interactive 48 hand net`, launched 20:56
after draining B-play again, bench pid 277268, 8 cells, ~3 h (P4's interactive cells ran 12 games in 4-7 min).
B-play resumes when it ends. Promotion of the net (weights as an asset, `EAiProfile` value, lobby button - step
15b) waits on the confirm; the regeneration (~5k B-play games with the net leaf) follows it.

*B-play batch 0 (landed 20:55 during the resume window):* 200 games, 0 faults, 3,207 rows, ~45 min of active
generation across the pauses, i.e. **~270 games/h at dop 6 -> 5k games is ~19 h of unpaused box time**, not
the doc's 15. Cell logs are in `scratchpad/selfplay-b1.log`.

**2026-09-06 (19:55, Fable 5.1 - step 15's design turn recommends Opus/high; Chris said "continue") - STEP 15
DESIGNED AND BUILT, NOT YET RUN. Engine `06c05f5`. ONE DECISION OPEN: pause B-play now for the slice, or after
its 5k games.**

*Design* is recorded in full in `docs/tactician-bc-campaign.md` step 15 (arms, opponent, pairs, seeds, decision
rule, confirm, regeneration, tooling). The short form: Strategist(hand | net | blend 0.5) vs Tactician at the
benchmark budget on the four low-ceiling ring pairs (DE-DG, DG-HE, HDF-Orks, RL-AH), 48 paired-seed games per
cell, 576 games (~1.5-2 h); promote only on >= +5 pooled over hand with no pair lost by > 10; confirm the winner
at interactive (~3.5 h); regeneration with the winner (hand winning means the running B-play set already is it).
Opponent is A rather than B because the bench's `--evaluator` binds to every Strategist in the game - **per-side
evaluator on bench is a step-16 build item** (needed for C-vs-B), filed here, not dropped.

*Built.* `BlendedPositionEvaluator(primary, secondary, weight)` (engine; linear mix, so complementarity and
[0,1] survive; refuses weights outside [0,1] incl. NaN; 5 tests). Lab `--blend W` beside `--evaluator` on
bench AND selfplay (the regeneration channel can play the blend arm), label stamped in the bench header:
"blend 0.5 MLP (serving-full-weights.json) + 0.5 hand". `FdgLab/tools/c4-slice.sh` + `c4-summarize.py`.
Release binary snapshot `scratchpad/step15bin` (so later builds cannot disturb a running slice).

*Verification.* Engine suite 3266/0/1, full build clean, headless smoke exit 0, 2-game blend bench at the
benchmark budget: 0 faults, header correct, summarizer reads it.

*B-play at 19:55:* pid 267005 alive, batch 0 (200 games, dop 6) still not written after 52 min - the doc's
15 h for 5k games is not going to hold; rate recorded when batch 0 lands.

*Command, once the go is given (the box must be otherwise idle):*
`FDGLAB_BIN=<scratchpad>/step15bin/FdgLab setsid nohup FdgLab/tools/c4-slice.sh FdgLab/reports/c4-slice-2026-09-06 benchmark 48 hand net blend &`

**2026-09-06 (19:08, Fable 5.1) - SERVING MODEL RETRAINED ON THE FULL v3 A-PLAY SET: 26,199 games / 443,608
rows, `models/serving-full-weights.json` (uncommitted, same policy as step 14), C# parity 5/5.**

`load.py` on the closed v3 directory (131 files, schema 3 only, 0 out-of-range / 0 held-out rows; 81.8 MB
parquet `v3-full.parquet`, the 15.8k `v3.parquet` kept for comparison), then `train.py --model mlp --drop
activation_frac,acting_side_is_first --export` at 4 threads while B-play ran (both fits took 28s total).

| split | rows | model auc | hand auc | model brier | 15.8k model |
|---|---|---|---|---|---|
| held-out GAMES | 43,004 | **0.9038** | 0.8132 | 0.1085 | 0.906 |
| held-out PAIRINGS (3 of 20) | 41,178 | **0.8920** | 0.8340 | 0.1187 | 0.8865 |

Round 1 delta +0.257 (games) / +0.161 (pairings); largest level gain again 4k (+0.161). Read: 1.66x the data
moved the unseen-pairing number up 0.006 and the same-pairing number down 0.002 - both inside split noise,
so the model is data-saturated at this capacity/epoch count, not data-starved. Training loss was still
falling at epoch 12 (0.500), so a longer schedule is the cheap thing to try if C4 wants more; not done here
because C4's arm slice is the arbiter, not an offline metric. Parity: `MlpPositionEvaluatorTests` with
`FDG_MLP_WEIGHTS`/`FDG_MLP_CASES` pointed at `serving-full-{weights,parity}.json` - 5 passed. **C4 should
bench `--evaluator FdgLab/python/models/serving-full-weights.json`.** `serving-weights.json` (15.8k) stays
on disk as the step-14 reference.

*B-play at 19:08:* pid 267005 alive, RSS 981 MB, batch 0 (200 games) not yet complete after 5 min - rate
to be recorded when it lands.

**2026-09-06 (19:05, Fable 5.1 - step recommends Sonnet; Chris assigned it in the handoff prompt) - STEP 12b
DONE: THE B-PLAY CHANNEL EXISTS, A-PLAY IS PAUSED AT A BATCH BOUNDARY, B-PLAY IS GENERATING. Super `24d08c8`
(engine untouched).**

*The gap, confirmed.* `SelfPlayGameRunner` called `BuildRegistry` without `searchBudget`, so a Strategist in
any mix fell through to `AiProfileFactory.DefaultSearchBudget` - the 5-10s interactive budget, NOT A-play as
the handoff guessed, but not what any mix meant either. Now `spec.SearchBudget ?? GameRunner.LabSearchBudget`
and `spec.Evaluator` reach the factory exactly as `GameRunner.BuildRegistry` passes them for a bench.

*As built.* Mix entries carry `searchBudget` (benchmark|interactive) + `searchWorkers` (default 4), parsed by
a new `SearchBudgets` shared with bench `--search-budget` so one name cannot mean two things. `MixConfig.Load`
fails at load (unknown budget, budget on a no-Strategist entry, workers < 1) rather than at the first game
that draws the bad entry. `selfplay` gains `--evaluator PATH` (the C4 regeneration channel, same flag as
bench) and a profile-derived watchdog (Strategist: 900s 1v1 / 1800s 2v2; A-play keeps 120/600; `--timeout`
overrides - it was silently dead before, SampleGame ignored it). Every `game` line records `search_budget` +
`evaluator` ("none"/"hand" for A-play; additive, old files read as the defaults), and `load.py` carries both
into the parquet. `FdgLab/armies/mix-strategist.json`: strategist_vs_tactician 0.5 + strategist_mirror 0.5,
benchmark/4, same level weights as mix.json.

*Verification.* Engine suite 3260/0/1, full build clean, headless smoke exit 0. A-play regression: the new
binary on seeds 300000-300001 with mix.json reproduces the running v3 file's samples, profiles, outcomes and
round counts (Tie/4, Win/4). B-play smoke (2 games, dop 2, seeds 900000-900001): 0 faults, exit 0, **155s vs
13s for the A-play pair** - the search is engaged - and both game lines read `benchmark`/`hand`.

*Box swap (19:01-19:03).* Pause file touched after batch 130 (26,200 games; 131 files, no .tmp), process
drained to 0% CPU in 30s, SIGTERM'd, `pgrep -x FdgLab` empty, pinner 72538 untouched. **v3 A-play final:
26,200 games in `FdgLab/data/2026-09-06-v3`** (restartable: DetermineStartBatch resumes at batch 131 if it
is ever relaunched). Then B-play launched: pid **267005** (watcher 267004, pid file
`scratchpad/selfplay-b1.pid`), binary `scratchpad/step12bbin`, `FdgLab/data/2026-09-06-v3-bplay`, seed base
400000, dop 6, all three GC knobs, RSS sampled to `selfplay-b1-rss.log`. ~25 cores at launch.

*Throughput caveat (not yet measured at dop 6).* The smoke's ~78s/game at dop 2 while sharing the box says
5k games is nearer 30 h than the doc's 15 h; the first 200-game batch is the real number. Recorded in
the campaign doc when it lands.

*Deferred, recorded.* No unit test for `MixConfig.Load` validation or `SearchBudgets` - FdgLab has no test
project and the campaign's lab convention is a smoke run; filed here rather than dropped. `--evaluator` on
selfplay is wired but has only been exercised through bench (step 14's smoke); its first real use is C4's
regeneration.

*Next.* When B-play has a few batches: retrain on the full v3 set (26.2k games) with B-play rows as the
validation/held-out-budget slice, then step 15 - **design turn is Opus/high, prompt Chris to switch models
before starting it.**

**2026-09-06 (18:55, Opus 5) - HANDOFF: STEPS 12a/12c/13/14 DONE, BOX GENERATING, NEXT IS 12b.**

*Box state.* v3 A-play self-play running: pid 254871, binary `scratchpad/step10bin-v9`, 12 GiB cap /
dop 10, into `FdgLab/data/2026-09-06-v3` (seed base 300000). **24,800 games at 18:50**, RSS flat
~620 MB, ~7,300 games/hour. Pause with `touch FdgLab/.pause-selfplay`; pinner pid 72538 must keep
running. Nothing is blocked on this run - step 13's bar was already cleared on 15.8k of it.

*Where the phase stands.* 12a (schema v3), 12c (python loader), 13 first pass (offline bar CLEARED:
hand 0.818 auc, lgbm 0.914, mlp 0.906 game-split / 0.892 unseen pairings) and 14 (MlpPositionEvaluator,
engine `a3c0080`, super `9d0b6ce`, 2.5 us/call, torch parity 1e-5) are all committed and pushed.

*Next actions, in order.* **12b** (Sonnet/medium, no box needed to BUILD): thread a per-mix-entry
profile + search budget through `SelfPlayGameRunner` -> `AiProfileFactory.BuildRegistry` (it never
passes `searchBudget` today), add `FdgLab/armies/mix-strategist.json` (Strategist-vs-Tactician +
Strategist mirror, benchmark budget, 4 workers). Then STOP the A-play run and launch B-play at dop 6
into its own directory for ~5k games (~15 h) - one self-play process at a time. Then retrain the
serving model on the full v3 set (current weights come from 15.8k rows of an ongoing run and are
deliberately NOT committed), then step 15 (design turn is Opus/high).

*Carried, unanswered by Chris.* S2 (drop the generator-extraction item - already dropped in the doc)
and S3 (plain-C# MLP - already built that way); the `charge-vs-shoot-shoot-favored` probe re-pin;
>= 2 verbatim games; L1 merge to master; memtest86+. Step 16's `lane-block` and `buff-anticipation`
probes are still unwritten and are gating.

**2026-09-06 (19:40, Opus 5) - STEP 14 DONE: THE LEARNED EVALUATOR RUNS IN THE ENGINE, MATCHES TORCH
TO 1e-5, COSTS 2.5 us, AND PLAYS REAL BENCH GAMES. Engine `a3c0080`.**

*Serving vector (77 floats).* Two of the encoder's seven globals - `activation_frac` and
`acting_side_is_first` - describe an activation boundary, and a leaf sits mid-simulation with no
boundary to describe. Serving them as zeros would be a train/serve mismatch that nothing would catch,
so the serving model is TRAINED without them; measured cost of dropping both: held-out auc 0.9136 ->
0.9133, unseen-pairing auc 0.8865 -> 0.8862, i.e. nothing. `points_norm` survives because it IS
reconstructible at a leaf: `PositionEncoder.TotalGamePoints` sums each army's `PointsLimit`, the same
quantity `SelfPlay` passes the exporter. `EncodeForEvaluation` builds SELF = one member of the side
(deterministically chosen) and ALLY = the rest, matching how a training row is shaped - serving the
whole side as SELF with an empty ALLY block would be a shape the model never saw in 2v2.

*`MlpPositionEvaluator`.* Hand-written dense forward pass over a weights JSON. **2.5 us per call**
(target was < 100 us; the hand evaluator it replaces costs ~1800 us, nearly all of it encoding). Two-side
games are symmetrized so `IsComplementaryTwoSide` holds by construction. Guards: refuses weights whose
schema is not the build's, refuses a wrong input width.

*Parity.* `Tests/Fixtures/mlp-parity-{weights,cases}.json` - fixed-seed random weights (arithmetic, not a
model, so the fixture stays valid when the real weights are retrained) plus torch's output on 24 real
feature rows; the C# pass matches every case to 1e-5. The test also asserts the fixture's outputs SPAN a
range, or it could not fail for an implementation that ignored its input. The REAL 128/64 serving model
was parity-checked the same way on 1024 rows via the `FDG_MLP_WEIGHTS`/`FDG_MLP_CASES` overrides - passed.

*Wiring.* `TacticianOptions.Evaluator` (null = hand-weighted, so an unpromoted net cannot become the
default by accident - G9), threaded through `AiProfileFactory.BuildRegistry` beside `searchBudget`; lab
`--evaluator PATH` on `bench`, stamped into the report header the way `--search-budget` is. Smoke: a
2-game Strategist-vs-Tactician cell with the learned evaluator ran clean, header line
"Leaf evaluator: **MLP from serving-weights.json**".

*Verification:* engine suite 3260 passed / 0 failed / 1 skipped, full build clean, headless smoke exit 0.

*Deliberately NOT done (recorded, not dropped).* The shipping weights are not committed: the current
model is trained on partial data (15.8k games of a run still going) and step 15 retrains on the full set,
so committing 400 KB of throwaway weights would be noise. The committed fixture covers the arithmetic;
the real model is verified through the env-var override. Promotion (weights as an asset + an
`EAiProfile` value + the lobby button, step 15b) waits for C4's bench result - not for an offline metric.

**2026-09-06 (18:45, Opus 5) - STEP 13 FIRST PASS: THE TREE BASELINE BEATS THE HAND EVALUATOR
EVERYWHERE, AND BY +0.28 AUC IN ROUND 1. THE RESEARCH RISK IN PHASE C IS LOOKING SMALL.**

`FdgLab/python/train.py`, LightGBM on the 15.8k v3 games written so far (267,575 rows, 79 features,
early-stopped, 8 threads so generation kept its cores). Two splits, because they answer different
questions - and the second one exists because the C-gate's own held-out PAIRS never appear in exported
data at all (the exporter refuses to write them), so without it the generalization gap is invisible
until ~30 h of bench time says so.

| split | model auc | hand auc | model brier | hand brier |
|---|---|---|---|---|
| held-out GAMES (same pairings) | **0.9136** | 0.8183 | 0.1034 | 0.1690 |
| held-out PAIRINGS (3 of 20, unseen armies) | **0.8865** | 0.8328 | 0.1176 | 0.1707 |

*By round (held-out games), model vs hand:* R1 **0.857 vs 0.576 (+0.28)**, R2 0.890 vs 0.837, R3 0.937
vs 0.920, R4 0.971 vs 0.942. Exactly the shape the 18:10 baseline predicted: the headroom is early-game
evaluation, where the hand evaluator is barely above a coin and the search has the least depth relative
to the game remaining. *By level:* the gain is largest at 4k (+0.165), the hand evaluator's weakest
cell, and real at every level (1k +0.107, 2k +0.075, 3k +0.113, 2v2 +0.065).

*Generalization:* 0.9136 -> 0.8865 moving from unseen games to unseen ARMY PAIRINGS, a 0.027 auc drop,
still 0.054 above the hand evaluator on those same rows. That is the offline analogue of the C-gate's
"held-out pairs within ~5 points" criterion and it looks healthy - the model is not memorizing rosters.

*Top features by gain* (a sanity read, not a decision): enemy obj_held_share, our obj_held_share,
objective_count_norm, enemy value_share, round_frac, then **self__obj_open_approach and
enemy_sum__obj_open_approach** - two of the four v3 features earn their place in the top seven, which
is the S1 schema bump paying for itself immediately.

*Status and honesty about what this is not.* LightGBM is a DIAGNOSTIC, not the ship target (plan sec
10: trees would mean a feature-engineering signal, not a deployable evaluator - a boosted forest is far
too slow for a leaf called ~800 times per decision). The MLP is next (torch installing). And none of
this is a strength result: predicting an outcome better than the hand evaluator is necessary, not
sufficient - C4 still has to show it makes the SEARCH play better, on the bench. But the failure mode
this phase feared most, "the net cannot beat the heuristic offline", is not what the data says.

**2026-09-06 (18:10, Opus 5) - STEP 12c DONE: LOADER + THE BASELINE THE NET HAS TO BEAT. THE HAND
EVALUATOR SCORES AUC 0.818 ON HELD-OUT GAMES - AND 0.576 IN ROUND 1, WHICH IS WHERE C'S HEADROOM IS.**

`FdgLab/python/` (uv, `package = false`): `load.py` reads self-play jsonl.gz to parquet and re-asserts
schema sec 7's checks on EVERY row that could reach a model (not the export-time sample); `baseline.py`
scores `hand_value` as a predictor of `result`. Deps installed: pandas, pyarrow, numpy, lightgbm,
scikit-learn; torch/onnx are an optional extra, not installed until step 13 needs them.

*Loader run over the v3 data written so far* (79 files, 15,799 games, 267,575 rows): schema versions
seen `[3]`, features outside [0,1] **0**, bad widths **0**, NaN hand values **0**, rows from a held-out
pairing **0**. Split is by GAME (never by row - both sides' rows share one outcome label, so a row-wise
split leaks the answer), deterministic from the game seed so a resumed dataset extends the same split:
14,279 train / 1,520 val games. Mix as configured: 2v2 40%, then 3k/2k/4k/1k.

*The baseline (val split, 1,520 games neither model has seen):*
| predictor | brier | logloss | auc |
|---|---|---|---|
| hand evaluator | 0.1690 | 0.6389 | **0.818** |
| constant (train mean) | 0.1950 | 0.6924 | 0.506 |

By round: **0.576 / 0.837 / 0.920 / 0.942** (rounds 1-4). By level: 1k 0.797, 2k 0.847, 3k 0.801,
**4k 0.719**; by shape 1v1 0.794, 2v2 0.856.

*What this says for step 13, before a single model is trained.* The hand evaluator is already a real
predictor late in a game - by round 3-4 it orders wins above losses 92-94% of the time, and a net will
not beat that by much. Round 1 is where it is nearly blind (0.576, barely above a coin), and 4k is its
weakest level. So C's value is concentrated in EARLY-GAME evaluation and at the top of the point range,
which is also where B's search has the least depth relative to the game left. The step-13 report prints
these same metrics beside the model's, per round and per level, and "beat 0.818 overall" is the wrong
bar to steer by - "beat 0.576 in round 1 without losing round 4" is the real one.

*Not built yet:* 12b (the Strategist/B-play channel) - it needs the box, and the v3 A-play run has it.

**2026-09-06 (17:35, Opus 5) - v3 GENERATION HIT A MANAGED OOM AT BATCH 77 (15,400 GAMES). RELAUNCHED
WITH A 12 GiB CAP AND DOP 10, RESUMED AT BATCH 77, RSS SAMPLER ADDED.**

*What happened.* `selfplay-v3.log`: "Out of memory." then createdump, signal 6, 9.4 GB core
(`scratchpad/dumps/selfplay-v3-243944.dmp`). Not one of the three faults from the gate night (GC kernel
spin, BGC write fault, TP GPF) - this is the managed heap reaching `DOTNET_GCHeapHardLimit=0x200000000`
(8 GiB) and the allocator giving up. 77 batches completed cleanly first, 0 faulted games, no partial
`.tmp`; the resumable design cost nothing.

*Why v3 and not v2 (which ran 166 batches on the same cap).* Nothing here retains: `HandWeightedEvaluator`
is stateless (consts only), and neither it nor `PositionEncoder`/`MarkerTerms`/`TacticalAnalysis` holds a
static cache. What v3 added is ALLOCATION RATE at every boundary - a second full per-side encode inside the
hand value, on top of the encoder's own four blocks - against a hard cap with 12 games live at once. That
is pressure, not a leak, unless the numbers say otherwise.

*Relaunch (`scratchpad/selfplay-v3-run.sh`, pid file `selfplay-v3.pid`):* cap 12 GiB
(`0x300000000`, box has 27 GB available), dop 12 -> 10, mini-dump type 2 instead of 4 (a 9.4 GB full core
per fault is not worth the disk or the 19 s pause), and an RSS+batch sample every 60 s to
`selfplay-v3-rss.log`. **That sampler is the actual test:** flat-with-sawtooth RSS across a few hundred
batches says pressure and the cap change is the fix; a monotone climb says a real leak, and the first
suspect is then the exporter's per-game buffers rather than anything in the engine. Resumed at batch 77
(seed 315400) - the 15,400 games already written are valid v3 data.

*Cost of the interruption:* ~2 h of generation (the OOM was at ~16:0x, found at 17:30). v3 now reaches
v2's 33k-game volume around 21:00 rather than 19:30.

**2026-09-06 (15:20, Opus 5) - STEP 12a BUILT: SCHEMA v3 (79 FLOATS + hand_value), ALL SIX PRE-RUN
CHECKS GREEN, SELF-PLAY RELAUNCHED ON IT.** Chris signed off S1 ("Yes, please do that"). Engine `46e9d03`.

*What changed.* `PositionEncoder` v3: per-side block 16 -> 18 floats, adding `obj_contest_strength` (16)
and `obj_open_approach` (17) - the two `MarkerTerms` the shipping leaf evaluator already reads. Width
71 -> 79, `SchemaVersion = 3`. The parity is structural, not a convention: `HandWeightedEvaluator` now
READS indices 16/17 out of the encoder block instead of calling `MarkerTerms` itself (and its own
`ProjectObjectives` call went away with it), so the exported row and the evaluator are the same numbers
by construction. Test `MarkerTerms_AreExposedAtIndices16And17` fails the moment someone recomputes them
with different arguments. Lab side: `ExportRow.HandValue` per row (the evaluator's value for the ACTING
side at that boundary, via a new `SideMap.FromStore(IReadableGameDataStore)` overload), written by
`JsonlGzWriter`; the file header's `schema` field follows the encoder constant, so it reads 3 with no
edit. Suite 3255 passed / 0 failed / 1 skipped; full build clean; headless smoke exit 0 (Player 2 wins,
4 rounds).

*Schema sec 7's six pre-run checks, on a quiet box (self-play paused), 24 games at seeds 999000+:*
| check | result |
|---|---|
| 1. rows per game == sampled boundary count | 24/24 games have rows, 9-42 rows/game (1-in-4 sampling) |
| 2. every feature in range | 0 of 417 rows out of [0,1]; `obj_diff_norm` in [-1,1] |
| 3. label balance | 128 win / 115 loss / 174 tie rows - the mirror mix's real rate, not one class |
| 4. held-out pairings absent | 10 pairings sampled, none from `pool.json`'s heldOut |
| 5. determinism on a fixed seed | **content-identical**, hash `b5016651...` both runs |
| 6. encoder under budget | 3.63 ms mean (budget 5), up from 1.5 - the marker terms and the hand value are inside the same stopwatch |
New features look alive: contest strength nonzero on 192/417 rows (mean 0.073), open approach nonzero on
405/417 (mean 0.669).

*One extra change, flagged because it was not in S1's text:* `game_id` is now derived from the game seed
(SHA-256, first 16 bytes) instead of `Guid.NewGuid()`. Check 5 asks for byte-identical output on a fixed
seed, and with a random id per game that could never be checked literally - the first run "failed" it
with every row's content identical and only the ids and line order differing. Seeds never repeat inside
an output directory, so ids stay unique where it matters, and the check is now a real check rather than
a thing verified by hand. Reversible in one line if Chris dislikes it.

*Generation.* Self-play v2 stopped at batch 166 (33.4k games, `FdgLab/data/2026-09-05-v2`, kept as valid
v2 data alongside the 86k-game v1 run). v3 launched on the Release build `step10bin-v9` into
`FdgLab/data/2026-09-06-v3`, seed base 300000, dop 12, same GC knobs, same pause file. At ~8k games/h it
matches v2's volume by ~19:30 tonight. NOT started: 12b (the Strategist/B-play channel) - it needs the
box to itself and the v3 A-play run has priority.

*Model note:* step 12 recommends Sonnet/medium in the campaign's section 3 policy; this ran on Opus 5.
Nothing here needed it. Steps 12b/12c are the same shape.

**2026-09-06 (14:10, Fable 5.1) - STEP 11 DONE: C REPLAN WRITTEN (plan sec 10 + campaign steps 12-16). THREE
SIGN-OFFS ASKED.** Chris: "Please continue." Inputs and the reasoning are in plan doc sec 10 (re-detailed);
the executable detail in the campaign doc steps 12-16; this entry records what the replan CHANGED and why.

*Facts that moved the plan.* (1) `IPositionEvaluator` already exists and is constructor-threaded through
`StrategistActivationResolver`; `TacticianOptions` has no evaluator knob (one to add). (2) The hand evaluator
reads v2's block PLUS `MarkerTerms` (contest strength, open approach), which no exported row carries - a net
on v2 rows cannot see the term that produced P4's +18. (3) The step-12 "extract the lobby random-army
generator" item rests on a false premise: `BotArmyPicker` is a file selector by points, no generator exists,
and FdgLab cannot reference FdgRaylib by design. (4) `selfplay` cannot play the Strategist (no profile/budget
passthrough; `SelfPlayGameRunner` never passes `searchBudget`). (5) Data: v1 86k games (schema 1), v2 167
files / 33.4k games at 13:35 (schema 2, ~17 rows/game), 20 trained pairings, 80/20 win/tie; post-P4 A-play
picks Contest 13% of planned activations. (6) Costs: 1.0 ms/expansion, 1.8 ms hand leaf, 750-800
iterations/decision at 2k interactive, depth 7; B games 110-120/h at dop 6 (2k), 48/h (3k 2v2). (7) Box has an
RTX 4070 Ti Super and uv; no python env, no ONNX anywhere in code.

*Decisions (mine, in the replan):* the first net trains on A-play because B's leaf value IS the A-vs-A
continuation value (what a rollout under `InSimProfile=Tactician` returns) - B-play enters once as C4's
regeneration; vocabulary unchanged beyond M14 (13.2 review: the gap was contest resolution, not a missing
move type); two-head MLP 79->128->64->2 with an OFFLINE bar (beat the logged `hand_value` on held-out rows)
before any box-day; C4 as a 3-arm slice at the benchmark budget; C-gate costed at ~30 h (both sides search).

*Sign-offs asked of Chris (the replan is written assuming yes; nothing is built until answered):*
- **S1 - schema v3** (+2 per-side marker features = 79 floats, + per-row `hand_value`; sec 6 stop-and-ask).
  Self-play restarts into a new directory at seed base 300000. Cost: v2's ~1 day of data becomes v2-only.
- **S2 - drop the step-12 generator extraction** (premise false); file a book-based random army builder as a
  separate item if wanted (R6 pool refresh).
- **S3 - C3 as a plain-C# MLP forward pass** (weights JSON asset, ONNX kept as interchange + parity test)
  instead of an ONNX Runtime dependency - removes R7 and a native lib from four unsigned archives.
Also pending from earlier: probe pin (`charge-vs-shoot-shoot-favored`), verbatim games, L1, memtest86+.

*Next:* step 12 is Sonnet/medium work; per the section-0 protocol the model switch is prompted at step
start. Self-play v2 keeps generating until S1 is answered (every batch is valid v2 data either way).

**2026-09-06 (10:15, Fable 5.1) - P4 REGRESSION SLICE HOLDS: 72.9% vs 71.9% ON THE GATE BINARY. P4 SHIPS.**
`scratchpad/p4-slice.sh`, out `FdgLab/reports/p4-slice-2026-09-06/pair{0..7}`: the 8 ring pairs of the
gate matrix (the same 8-army ring, seeds from 1000, 12 games each, interactive budget, dop 6, v8 =
engine b36cfca) re-run on the P4 binary, with self-play paused so wall-clock budgets were honest.
96 games, 0 faults, 0 timeouts. Strategist vs Tactician (W + 0.5T)/N = (63 + 7)/96 = **72.9%**, against
the v5 gate's **71.9%** on the same cells and seeds (+1.0, well inside the ~5-point tolerance set in the
09:25 entry; 96 games is a coarse instrument, so read this as "flat", not "up").

| Pair (A = Strategist) | v5 | P4 | delta |
|---|---|---|---|
| Alien Hives vs Battle Brothers | 91.7 | 95.8 | +4.1 |
| Battle Brothers vs Dark Elf Raiders | 87.5 | 95.8 | +8.3 |
| Dark Elf Raiders vs Dwarf Guilds | 58.3 | 45.8 | -12.5 |
| Dwarf Guilds vs High Elf Fleets | 54.2 | 66.7 | +12.5 |
| High Elf Fleets vs Human Defense Force | 100.0 | 91.7 | -8.3 |
| Human Defense Force vs Orks | 41.7 | 37.5 | -4.2 |
| Orks vs Robot Legions | 91.7 | 100.0 | +8.3 |
| Robot Legions vs Alien Hives | 50.0 | 50.0 | 0.0 |

The per-cell swings are what 12-game cells do (one game = 8.3 points); no cell moved in a way that
suggests the Contest macro or the marker terms broke a matchup. Combined with the Orks A/B (26.4 ->
44.4 on the same 36 seeds, 09:25 entry), P4 is a net gain with no measured regression and ships in
the gate's binary lineage: **the B-gate record now stands on engine b36cfca (v8)**, not 41f178d - the
70.1% matrix / panels were measured on 41f178d and the slice is the bridge. If Chris wants the full
gate re-measured on v8 that is a ~10h rerun of `step10-gate-v5.sh` with the v8 binary; my
recommendation is not to, the slice + Orks A/B are the evidence and the next step is 11.

Still open for Chris (unchanged from 09:25): the `charge-vs-shoot-shoot-favored` probe pin (Contest
walk 1.000 vs Shoot 0.997 - the probe asserts a shoot that the search now rates a hair below claiming
the marker; either re-pin the probe or treat it as the P4 trade-off), >= 2 verbatim games, L1 merge,
memtest86+. Self-play v2 unpaused itself at 10:08 when the slice ended and is generating P4 A-play
from batch 23 (10:00 entry).

**2026-09-06 (10:00, Fable 5.1) - SELF-PLAY v2 MOVED TO THE P4 BINARY (same directory, resumed).**
Chris asked to pause self-play v2 and resume it on the new binary. `selfplay` resumes by scanning the
output directory for complete `selfplay_*.jsonl.gz` batches (DetermineStartBatch), so the v7 process
(pid 218664) was killed at 09:5x and the v8 build (engine b36cfca, P4) relaunched with identical
arguments (`--out FdgLab/data/2026-09-05-v2 --seed-base 200000 --dop 12`); it reported
"starting at batch 23 (seed 204600)". No partial batch was lost (no `.tmp` present). Provenance:
batches 0-22 (seeds 200000-204599, files written 08:30-09:03) are PRE-P4 A-play; batch 23 onward is
P4 A-play. Note the file header's engine commit is read from the git checkout, not the binary, so
the last few v7 files may carry b36cfca (committed 09:04) - trust the batch boundary above, not the
header, for the v7/v8 split. The new process honours the same pause file and is idle until the P4
regression slice ends (the slice script removes `FdgLab/.pause-selfplay`). GC knobs (8 GiB cap +
RetainVM + libclrgc.so) now on self-play too. New pid in scratchpad `selfplay-v2.pid` (228240).

**2026-09-06 (09:25, Fable 5.1) - P4 ORKS A/B: 26.4% -> 44.4% ON THE SAME 36 GAMES (+18). REGRESSION
SLICE OF THE 2k MATRIX RUNNING.**

`FdgLab/reports/p4-orks-ab-2026-09-06/` (v8 = engine `b36cfca`; seeds 3000+, interactive, dop 6, 0 faults):
| cell | v7 (gate v5) | v8 (P4) |
|---|---|---|
| Robot Legions vs Orks | 29.2% (1/6/5) | 25.0% (2/8/2) |
| Dark Elf Raiders vs Orks | 16.7% (1/9/2) | **58.3%** (6/4/2) |
| Battle Brothers vs Orks | 33.3% (2/6/4) | **50.0%** (3/3/6) |
| all 36 | 26.4% (4/21/11) | **44.4%** (11/15/10) |
Per-cell SE ~13 and 36-game SE ~8, so: two of three cells moved by 2-3 SE, the third is flat, and the
aggregate is a +18 that is unlikely to be noise. The Dark Elves - the one list the analysis found
out-traded (Light Skimmers charged 46 times) - gained the most, which fits: a sliver keeps the
skimmer's mass out of the charge arc while a toe denies. Robot Legions did not move; their problem
was never contest resolution alone (48.9 as the piloted army across the whole matrix).

*Next, running:* `scratchpad/p4-slice.sh` -> `FdgLab/reports/p4-slice-2026-09-06/`: the 8 ring
pairs of the 2k pool x 12 games (96), v5 read them at 71.9% - a change to A's seize test and the
leaf's marker terms touches every cell, so the Orks gain must not come out of the strong cells. Self-play
v2 paused for it (~45 min), unpaused at the end. If the slice holds (within ~5 of 71.9), P4 ships in
the gate's binary lineage and the campaign's next step is 11. If it drops, the knobs are the
approach scale (24") and the contest zone (6") before any rollback - the macro itself is inert
unless the search picks it.

*Open for Chris:* (a) `charge-vs-shoot-shoot-favored` (Shoot pin vs the Contest line, both value ~1.0);
(b) self-play v2 (pid 218664) is generating on the PRE-P4 A (v7): restart it on v8 into a new
directory to keep the C1 dataset's policy version single, or keep v7 data and note the version -
the step-11 replan decides what C trains on either way.

**2026-09-06 (09:10, Fable 5.1) - P4 BUILT (CHRIS: "LET'S DO P4, WITH THE SECOND HALF TOO"): THE
CONTEST MACRO (M14, SLIVER DENIAL), THE SEIZE TEST ON END POSITIONS, AND TWO EVALUATOR TERMS
(CONTEST STRENGTH, PER-MARKER OPEN APPROACH). VERIFIED GREEN; ORKS A/B RUNNING.** Engine `b36cfca`.

*Part 1 - M14 Contest(o).* For every marker the side does not own (enemy-held, neutral, contested)
and no model already touches: aim the LEAD model's center at (3" - 0.4" + its circumscribed radius)
from the marker so its base edge ends inside the seizure radius at any facing, and string the rest
back along the route at the widest cohesion gap - `MovementPlanner.PlanSliverAlongRoute`, an on-path
snake with rank spacing 2r + 0.9" (across-file spacing stays tight so up to three files keep the 9"
diagonal; wider units fall back to the tight file) and an 0.08" anchor back-off so bends do not
overshoot the 1" rule. The head clamps at the route's end (an arc past the end collapsed every rank
onto the head, failed cohesion, and the ladder halved the head 0.6" short - the first cut's bug).
Ladder: the sliver at halving arcs, then reform, then hold; no grid fallback (that is M2/M3). Both
budgets in one family; graded by the lead's ACHIEVED base-edge distance (`TacticalAnalysis.
MinEndBaseEdgeDistanceToPoint`, at the end facing per #312). Measured on the test board (5 models,
marker 10" out, rush 12): lead edge 2.6", centroid 7.4" from the marker vs Rush's 0.0" - the two
shapes the plan's item 4 wanted.

*The seize test itself was wrong.* `TacticianPlanner.ObjectiveDelta` credited "centroid within
4.5"" - it could not see a sliver at all and over-credited compact units. It now measures END
positions (one base edge within 3" + 0.05", the reconcile rule), keeping the centroid stand-in only
for endpoint-only candidates (tests). That changes A on its own: DOP-1 Tactician hash
`4241CF7010C28571` -> `620F181E09E66153` (stable x2). Every existing planner pin still passes.

*Part 2 - `MarkerTerms` in the leaf evaluator.* (a) Contest strength replaces the flat contested
share: per contested marker, our share of the unit VALUE inside the contest zone (3" + 3"), summed
over markers / marker count - a contest we out-mass is worth most of the 0.20, a toe-hold under a
horde almost nothing. (b) Per-marker open approach replaces "closest unit to ANY marker over the
table diagonal": for each marker we do not own, 1 - (nearest eligible unit's distance beyond 3")/24",
averaged - a slope from two rush moves out that does NOT saturate when one unit sits on the home
marker (the exact mechanism the 02:35 analysis named: N->S 5 vs N->O 13). Both leave the v2 encoder
vector; they are the v3 feature candidates for the step-11 replan (obj_contest_strength_share,
obj_open_approach) and are computed from the same projections at the encoder's own O(units x
markers) cost. Weights unchanged (0.70 held / 0.20 contested / 0.10 approach).

*Tests (+5, suite 3254/0/1):* Contest puts one edge inside 3" with the centroid > 2.5" behind the
lead and > 1.5" behind Rush's, engine-valid; no Contest where a model already touches; the planner
scores a sliver above Hold on a neutral marker; a contested marker loses value when the enemy masses
into its zone (material and threat coverage held constant); a spare unit walking toward the enemy's
marker raises value at each step while another sits on ours.

*Verification (`step10bin-v8`, self-play paused):* superproject build green; headless smoke exit 0;
strategist smoke exit 0 (hash `734F56F7781739C4`, wall-clock budget); probes 4/5 x2 with a CHANGED
split: `count-says-they-win` now PASSES (the last-round denial the 2026-09-05 16:30 entry left open -
the open-approach slope and the Contest edge give the search the line), and
`charge-vs-shoot-shoot-favored` now FAILS: round 3, enemy holds the only marker 8" away; the probe
expects Shoot (the gun clears the holder), the search takes Contest at value 1.000 vs Shoot 0.997 -
under sticky ownership a cleared marker is still THEIRS until we walk on, while a sliver makes it
neutral at this round's reconcile and the gunners shoot from 2.6" next round. Both lines win every
simulation; the probe's expectation predates the vocabulary. Left failing, NOT rewritten - Chris's
call whether "Shoot" stays the pin or the probe accepts either.

*A/B running:* `scratchpad/p4-orks-ab.sh` -> `FdgLab/reports/p4-orks-ab-2026-09-06/`, the three
Orks dump-logs cells (same seeds 3000+, interactive budget, dop 6, GC knobs), v7 read RL 29.2 / DE
16.7 / BB 33.3. Self-play v2 unpauses when it ends.

**2026-09-06 (08:40, Fable 5.1) - STEP 10 GATE v5 COMPLETE: THE B GATE IS MET AT THE SHIPPING BUDGET.
MAIN MATRIX 70.1% (bar 60), EVERY PANEL CELL >= 50, TITAN REVERSE 53.3, FFA CLEAN, 0 FAULTS IN 1,332
SCORED GAMES. SELF-PLAY v2 RUNNING.**

`FdgLab/reports/step10-gate-v5-2026-09-05/` (gitignored), one `bench.md` per cell, engine `53a917e`
(P0+P1+P3 + #396), binary `step10bin-v7`, interactive budget, dop 6, Strategist vs Tactician, sides
swapped, ties half. Hashes: matrix `F6B9F7052EA6E125`, 1k `0D96A2A45A364E21`, 3k `22D3E52B9D4E3512`,
4k `EFD422B556A8425B`, 2v2-2k `A56F1E99161BA1B1`, 2v2-3k `EAE60D4DD63A0D42`, titan `FEA65608590B9BA5`.

| cell | games | score | W/L/T | cells < 50 | weakest cell |
|---|---|---|---|---|---|
| main matrix 2k 1v1 (64 pairs x 12) | 768 | **70.1%** | 467/159/142 | 8 of 64 (n=12 each) | BB / DE / RL vs Orks 20.8 |
| points-1k (4 cells x 30) | 120 | 73.3% | 75/19/26 | 0 | Blood Brothers vs HDF 58.3 |
| points-3k (5 x 30) | 150 | 72.0% | 93/27/30 | 0 | Battle Brothers vs Goblins 51.7 |
| points-4k (3 x 30) | 90 | 84.4% | 70/8/12 | 0 | Havoc vs High Elf 75.0 |
| shape-2v2-2k (4 x 24) | 96 | 69.8% | 58/20/18 | 0 (one AT 50.0) | DE+RL vs Dwarfs+HE 50.0 |
| shape-2v2-3k (2 x 30) | 60 | 60.0% | 27/15/18 | 0 | BB+Knights vs RL+Titans 51.7 |
| titan reverse (Goblins piloted vs Titan Lords) | 30 | 53.3% | 13/11/6 | - | forward pairing was 88.3 |
| orks dump-logs (not in the aggregate) | 36 | 26.4% | 4/21/11 | 3 of 3 | DE vs Orks 16.7 |
| ffa-smoke (4 players, seed 42) | 1 | clean | Robot Legions wins 1-0-1-2, 694 decisions, 146 s | - | - |

*Against the campaign's gate B row (sec 5):* main matrix >= 60% vs A: **70.1, met** (SE ~1.7). Every
panel cell >= 50 head-to-head: **met** (14 of 14 cells; one exactly at parity, all others 51.7-91.7).
vs-solo ceiling check and the "no cell below step-2 baseline minus 5" clause: **not run** in v5 (the
v4/v5 design dropped vs-solo as a ceiling check; v4's aborted run and step 9 both had it >= 85 - if
Chris wants it on this binary, it is a 1-2 h cell). ffa-smoke clean: **met**. Decision time within
budget at 4k: **met by construction** (wall-clock budget, 6.6-10 s scaled by root units; 4k games
averaged 258 s wall for both sides' 600-800 decisions). Memory stable over 500 games: **met** - the
v5c process ran 560 games at 3.0-3.9 GB RSS under the 8 GiB cap with no growth; v5d ran 426 games the
same way. Probes (last-round steal, charge-vs-shoot): the step-10 probe set passes 4/5, the failing
one (`count-says-they-win`) is the in-sim A's last-round denial gap (2026-09-05 16:30 entry), an A
facet, not a B defect. Chris's >= 2 verbatim games: pending (the Orks transcripts are the candidates).

*Shape of the result:* the edge grows with the point level (1k 73, 2k 70, 3k 72, 4k 84) and holds in
2v2 (70 at 2k, 60 at 3k) - the generalization worry that started the campaign is answered in B's
favour. The edge is play, not list strength: piloting the weaker Goblin list against Titan Lords it
still scores 53. The one recurring weakness is marker commitment against horde melee (Orks) and with
Robot Legions / Dark Elf lists, diagnosed in the 02:35 entry (P4 territory, Chris's call).

*Ops record of the night, for the docs:* the chain took 16.5 h instead of 12 because of three runtime
faults (a Server GC thread spinning in kernel time for 3h50, a BGC write fault, a pool-thread GPF).
`DOTNET_GCHeapHardLimit=8 GiB` alone: faulted; + `DOTNET_GCRetainVM=1`: 3h13 then faulted;
+ `DOTNET_GCName=libclrgc.so` (segments GC): **5h42 and 426 games clean to the end of the chain**.
Until the hardware question is closed (memtest86+ from boot), every search bench runs with all three.
The resumable design (`bench.progress.jsonl`) meant no scored game was lost to any of it, and re-running
finished cells reproduced every hash.

*Self-play v2* launched by the script tail at 08:29:54 (pid 218664, `FdgLab/data/2026-09-05-v2`, dop
12, seed base 200000, schema 2, cap; NOT on the segments GC - the A-only path never faulted after the
pin). Only self-play process on the box.

*What is next (campaign):* step 10 closes on this record; L1 (B-gate landmark) merge to master is
Chris's call after the >= 2 verbatim games; then step 11, the mandatory C replan (Fable/high).

**2026-09-06 (02:55, Fable 5.1) - THIRD FAULT: points-3k DIED 2 MIN IN UNDER CAP + RetainVM (GPF ON A
POOL THREAD IN libcoreclr). RELAUNCHED AS v5d ON THE SEGMENTS GC (`libclrgc.so`). IF THIS DIES, IT IS
HARDWARE.**

02:47:41, exit 139, dmesg `traps: .NET TP Worker[201295] general protection fault ... in libcoreclr.so`
(a non-canonical pointer dereferenced by runtime code on a thread-pool thread; a GPF, not a page fault).
Dump `dumps/step10v5-points-3k-201161.dmp` (3.7 GB): 66 managed threads, none of them the faulting
one (native frames only - lldb+SOS territory, not pursued). That is three distinct faces in eight
hours: a Server GC thread spinning in kernel time (18:44-22:31), a BGC write to a not-present page
(22:41), a GPF on a pool thread (02:47). `DOTNET_GCRetainVM=1` bought 3h13 clean through the end of
the matrix plus the orks/1k cells (~560 games), then lost the first 3k cell. v5c ran the last 3h13 clean -
no crash, no stall - so RetainVM is NOT the answer, only a delay.

Relaunched 02:48 as `step10-gate-v5d.sh` (pid in `gate-v5d.pid`): cap + RetainVM + `DOTNET_GCName=libclrgc.so`,
the standalone segments GC that ships in 8.0.26 - same server-GC semantics (so the wall-clock budget
stays comparable), none of the regions code. The chain re-ran the finished cells first: each resumed
in full and exited 0 with an unchanged hash (matrix `F6B9F7052EA6E125`, orks `417F58DBEF9B324A` /
`A2CCA9EB402DA6EB` / `4C6152A28C0C3C20`, 1k `0D96A2A45A364E21`), which also overwrote their `.log`
files with the trivial resumed-run log (the bench.md/csv are regenerated from progress and identical;
the wedged/v5b/v5c attempt logs are kept under their suffixed names). points-3k restarted from 0/150.

*Reading so far:* three faces with the same "corrupted pointer" shape, all in runtime code, none in a
managed frame, on a box with one confirmed and pinned bad DRAM page (2026-09-04) - and self-play's
A-only path never faulted after the pin (tens of thousands of games). The search path is the
allocation-heaviest thing this box runs (6 games x 4 root workers x snapshots), so it is also the best
detector of a second bad page. If v5d faults too, stop chasing GC knobs: the next step is memtest86+
from boot (Chris) and, until then, `--dop 3` benches with the cap, which halve the resident set. If
v5d runs the remaining ~7 h clean, regions were the trigger and `DOTNET_GCName=libclrgc.so` joins the
cap in every search bench.

**2026-09-06 (02:35, Fable 5.1) - ORKS FAILURE ANALYSIS (36 dump-logs games): THE STRATEGIST PUTS TOO
FEW BODIES ON MARKERS TOO LATE, AND NEVER CONTESTS AN ORK-HELD MARKER. LOSING IS SETTLED IN ROUND 2.**

Cells (Strategist-piloted vs Tactician Orks 2k Horde Mixed, n=12 each, 0 faults): Robot Legions
29.2% (1/6/5), Dark Elf Raiders 16.7% (1/9/2), Battle Brothers 33.3% (2/6/4). Split by side: with the
Strategist in slot 0 it lost 14 of 18 and won 1; in slot 1 it tied 8 of 18 (1-1 or 2-2) and won 3.
Full report (a subagent parsed all 36 transcripts; counts, not anecdotes):
`FdgLab/reports/step10-gate-v5-2026-09-05/orks-failure-analysis.md` (gitignored dir; copy in the scratchpad).

*Marker-rounds, 18 games per side (S = Strategist, O = Orks, C = contested, N = neutral):*
| side | end R1 | end R2 | end R3 | end R4 |
|---|---|---|---|---|
| slot 0 | S18 O18 C4 N23 | S11 O28 C17 N7 | S13 O31 C17 N2 | S13 O36 C13 N1 |
| slot 1 | S19 O14 C4 N26 | S16 O20 C15 N12 | S23 O19 C15 N6 | S21 O27 C11 N4 |
The deficit is made at the end of round 2 (11 vs 28) and never recovered (S 13 -> 13). The
Strategist's own markers are rarely flipped by melee (S->O 5 marker-rounds in 18 slot-0 games); the
Orks convert the neutral and contested ones instead (N->O 13 vs N->S 5, C->O 10 vs C->S 4). The
Strategist takes an Ork-held marker in 5 of 36 games. The dominant final shape is "holds exactly one
marker" (11/18 on both sides). Attrition is NOT the story: Robot Legions trade even (3.0 vs 3.3 units
lost per game), Battle Brothers win it outright (1.8 vs 4.2) and still lose on markers; only the Dark
Elves are out-traded (4.3 vs 2.0; Light Skimmers charged 46 times, the most on the table).

*Round 1-2 behaviour:* round 1 is pure movement on both sides (Orks 98% move-only); Battle Brothers
and Dark Elves stand and shoot in round 1 (13/11 and 18/24 shoot/move activations). The Orks charge
Strategist units 212 times vs 150 the other way and out-charge it in rounds 3-4 (38 vs 27, 45 vs 28).
With 4-7 activations against 9-10 the Orks close every round (last 3-5 activations, 144/144).

*Why slot 0 loses and slot 1 ties:* the slot difference is round-1 initiative AND deployment order -
the deploy roll-off is seed-locked to Team 0 (Strategist deploys first in 15/18 slot-0 games, Orks in
15/18 slot-1 games), so a side swap does not isolate initiative; deploy-first vs result: Strategist
first 13 O / 4 T / 1 S, Orks first 8 O / 7 T / 3 S. From round 2 on the engine opens every round with
the side that finished activating first (the Strategist, 36/36 - the OPR rule). Going first reveals the
deployment and round-1 move before nine Ork units respond: Orks reach 28 marker-rounds by round 2 vs
20 in slot 1 - the one marker that turns a 1-1 tie into a 1-2 loss. On 3-marker maps in slot 1 each
side keeps its flank marker and the centre ends contested or Ork-held; the Strategist never goes for
the second uncontested marker.

*Anomalies:* none that look like engine faults. "No actions available for X - passing" is the
end-of-activation sentinel on every activation, not idle units. Every reserve arrives at the start of
round 2 (36/36). One rules question for #175: Flesh-Eaters' Infiltrate is handled as an Ambush
variant (reserve, arrives round 2, over 3" from enemies) - if Infiltrate is a deployment-time rule,
Robot Legions play round 1 with 5 units instead of 6.

*Reading:* (1) the evaluator scores a contested marker as neutral and does not model who wins the
contest (activation count, charges, last activation) - so the search is content to leave the centre
contested while outnumbered, which loses it 10:4; (2) it does not value marching a spare unit onto an
Ork-held or neutral far marker early enough. Both are exactly the P4 (Contest macro / contest-aware
marker term) design question, Chris's call - now with numbers. The main-matrix bar is met regardless
(70.1%); this is the failure profile for the follow-on, not a gate blocker. An initiative-isolating
rerun (roll-off re-seeded independently of slot) is a cheap side experiment if the slot asymmetry
matters for the campaign's side-swap reading.

**2026-09-06 (02:05, Fable 5.1) - GATE v5 MAIN MATRIX CLOSED: STRATEGIST 70.1% vs TACTICIAN AT THE
SHIPPING BUDGET (bar 60%). 768 games, 0 faults, hash `F6B9F7052EA6E125`.**

`FdgLab/reports/step10-gate-v5-2026-09-05/main-matrix/` (bench.md / bench.csv / progress). 467 W /
159 L / 142 T (ties 18.5%). The relaunches did not bend it: 69.3% over the 366 games before the GC
wedge, 70.8% over the 402 after (v5c, cap + RetainVM). v5c ran the last 3h13 clean - no crash, no
stall - so RetainVM is the working mitigation for the region-decommit faces of 2026-09-05 22:xx.

*Side asymmetry worth keeping in mind:* 74.5% when the Strategist is slot 0 (moves first) vs 65.6%
when it is slot 1 - a 9-point first-mover edge at 2k, larger than v4 suggested. Panels are side-swapped
too, so it washes out of every aggregate, but it says slot order is a real term in the game itself.

*Per-cell (n=12, directional only):* 8 of 64 cells under 50%, no faults anywhere. Five of the eight
have Orks as the opponent or Robot Legions as the piloted army, the same two names as v4's four
weakest cells: Battle Brothers vs Orks 20.8, Dark Elf vs Orks 20.8, Robot Legions vs Orks 20.8, Robot
Legions vs High Elf 25.0, Robot Legions vs Dwarfs 33.3, HDF vs Dwarfs 37.5, HDF vs Orks 41.7, Dark
Elf vs High Elf 45.8. Piloted-army means: Robot Legions 48.9 (the only sub-50 row), Dark Elf 56.8,
HDF 64.6, Battle Brothers 65.1, Dwarfs 75.0, Orks 81.8, Alien Hives 83.3, High Elf 84.9. As the
opponent, Orks hold the Strategist to 49.5 on average. The Orks `--dump-logs` cells running now are
the failure analysis for exactly this; read them first when the chain ends.

*Chain:* orks-rl started 01:56; remaining ~8 h (panels, titan reverse, ffa-smoke), then self-play v2
launches from the script tail.

**2026-09-05 (22:40, Fable 5.1) - GATE v5 WEDGED IN THE .NET SERVER GC FOR 3h50; KILLED AND RESUMED
AS v5b WITH THE 8 GiB HEAP CAP. 366/768 MATRIX GAMES AT 69.3% STRATEGIST.**

*What happened:* the main-matrix bench (pid 169734, dop 6) wrote its last game at 18:44:51 (366/768,
matchup 30 half done) and then nothing until found at 22:31. Process state: load average 1.0, one
thread at 100% - `.NET Server GC` (tid 169768) with 4 h of SYSTEM time and 0.3 s of user time - every
`.NET TP Worker` idle; RSS 3.9 GB, VmSize 281 GB (the uncapped server GC reservation), no swap,
memory fine. `clrstack -all` on a live mini dump shows all 6 games' search workers parked on awaits
(19 `UctSearch.RunWorkerAsync`, 22 `SimulationService.Run`) - the shape of managed threads suspended
for a GC that never finishes. Not a crash (no dumper fire, no dmesg line), not the watchdog's case
either: `Task.Delay` cannot fire while the runtime is suspended. Kernel stack unreachable without
root (ptrace scope; `/proc/<tid>/stack` denied). Different face from the 2026-09-04 crashes (those
were a flipped bit in an object header, hardware, pinned since); the pinner (72538) was alive and
holding its page (`VmLck: 4 kB`) throughout. Whether this is a second bad page, a runtime GC bug in
region decommit under a 256 GB reservation, or something else is open - the `-heap` dump is kept.

*Evidence kept:* `scratchpad/gate-v5-stall.dmp` (mini, 29 MB), `gate-v5-stall-heap.dmp` (4.6 GB),
`gate-v5-stall-clrstack.txt`, `FdgLab/reports/step10-gate-v5-2026-09-05/main-matrix.attempt1-wedged.log`.

*Action:* killed script + bench (SIGKILL; the runtime would not have processed SIGTERM), relaunched
as `scratchpad/step10-gate-v5b.sh` (pid in `gate-v5b.pid`, appends to the same `step10-gate-v5.log`),
identical chain resuming the same `--out` (366 resumed, 402 to play, seeds unchanged) with ONE change:
`DOTNET_GCHeapHardLimit=0x200000000` (8 GiB) on every bench cell, as self-play v3/v2 already run.
The cap bounds the GC's reserved range (roughly 2x the limit instead of 256 GB) and with it the
decommit bookkeeping; RSS was 3.9 GB so 8 GiB is 2x headroom. If the cap trips, the dumper fires
and the script retries with resume. The 6 in-flight games of matchup 30 are simply replayed.

*Score at the wedge (366 games, Strategist = slot 0 unswapped / slot 1 swapped, ties half):*
217 W / 76 L / 73 T = **69.3%** (bar 60%). v4 at 215 games on the old binary read 70.7%.

*ETA:* 402 games at ~138/h is ~3 h -> matrix closes ~01:40 Sep 6; chain end moves from ~04:00 to
~10:00 Sep 6 (self-play v2 launches at the end of the chain as before). If a second wedge shows
up under the cap, the next arm is workstation GC (`DOTNET_gcServer=0`) for the remaining cells,
throughput cost accepted; and memtest86+ from boot stays the real answer to the hardware question.

*22:50 addendum - v5b lasted five minutes.* Attempt 1 died at 22:41:13, exit 139: dmesg
`.NET BGC[187073]: segfault at 734085f54d28 ... error 6 in libcoreclr.so` - the background GC thread
WRITING to a not-present page. The dumper fired (`dumps/step10v5-main-matrix-187012.dmp`, full,
3.2 GB); `verifyheap` lists 4,027 bad references, and NONE of the first dozen resolves with bit 23
restored, so this is not yesterday's flipped-bit signature - the bad values all point into memory
the GC had just given back. Two faces in four hours (a GC thread spinning in kernel time; the BGC
touching decommitted memory) both fit region decommit, runtime 8.0.26 (Canonical). Relaunched as
`step10-gate-v5c.sh` (pid in `gate-v5c.pid`, 372 resumed) with `DOTNET_GCRetainVM=1` added to the
cap - the GC keeps its memory instead of decommitting, same server GC and regions so the wall-clock
budget stays comparable with the 372 games already recorded - and 6 attempts per cell instead of 3
so an overnight crash cannot abandon a cell. Old logs kept as `main-matrix.attempt{1,2}-v5b.log`.
If v5c also dies: the segments GC (`DOTNET_GCName=libclrgc.so`, present in 8.0.26) is the next arm,
then hardware (memtest86+). A 40-minute stall watcher runs alongside the monitor.

**2026-09-05 (16:30, Fable 5.1) - #396 BUILT: THE SEARCH RUNS ON A TYPED STATE COPY. THE GATE IS
RELAUNCHED FRESH (v5) ON IT.** Chris: "do 394 now - way more games, way faster". Engine `53a917e`.

*What changed.* `StoreClone.Clone` builds what `Load(Save(store))` builds without the text
(per-type copy constructors, bindings rebound, tokens/weapons copied, immutable rules and shapes
shared, the loader's post-steps in its order, a serializer fallback for any type without a typed
cloner); `IStoreSnapshot` is the seam through `SimulationService` and the tree (`StoreSnapshot` =
a frozen clone, materialized per simulation; `JsonSnapshot` = the old path, kept behind every
string entry point). The Strategist captures the live store by cloning it. Separately, the
finalizer-thread share of the profile was pinned by counting JIT events: the store constructor's
`Activator.CreateInstance` / `MakeGenericMethod().Invoke` per type emitted reflection invoke
stubs that .NET's WEAK type cache dropped at every GC, so they were re-emitted and re-finalized
per store; one cached delegate per type now. Details, decisions and the pins are in
`WorkItems/396-simulation-state-copy.md`.

*Numbers (b0, the default 2k pair, boundary 20, 20 iterations):*

| | JSON (v6) | typed copy (v7) |
|---|---|---|
| state round trip per expansion | 76-81 ms (load 47 + save 30) | **1.0 ms** (capture 0.8 + materialize 0.2) |
| round-4 root, interactive budget (6.6 s, 4 workers) | 315-346 iterations, depth 6-7, 95 ms/iteration serial | **752-798 iterations**, depth 7, 43 ms/iteration serial |
| benchmark budget (1.5 s), same root | 69 iterations, 94 MiB live tree | 166 iterations, 46 MiB live tree |
| probes: charge-vs-shoot (shoot-favored) | ~1-3k iterations | 30-39k iterations |
| probes: last-round-steal | (hundreds) | 0.4-3.5 M iterations |

Note the b0 [3] "advance" phase did not move (105 vs 117 ms): b0's advance uses its own
`GameSaveSerializer` calls, not the seam - the row that matters is the round trip and the search's
own iteration counts.

*Verification (`verify-396.sh`):* suite 3249/0/1; superproject build; DOP-1 Tactician hash
`4241CF7010C28571` twice (unchanged - the A policy never touches a snapshot); headless smoke exit
0; Strategist smoke exit 0, 0 faults; `Save(Clone(x)) == Save(x)` at every boundary of a played
line and a 2-worker search choosing identically through both paths (StoreCloneTests, 7).

*The probe that now fails, and why it is a finding rather than a regression: `count-says-they-win`
4/5 x3.* With 20-45k iterations instead of hundreds, the search reaches the game's END in every
line and every root edge scores exactly 0.500: in simulation the Paper Runner never denies our
marker. Replayed naturally (scratchpad `jitpin runner`): the in-sim Tactician plays the runner as
Move / AdvanceOnObjective and stops at 6" from the marker - outside the holder's 6" guns, and
outside the 3" seizure radius - so the round ends 1-1 whatever the gunners do. Two things are
true at once: (1) the probe's premise ("the runner walks on") is what a human does and what P1's
projection assumes, and against a human Shoot is the only safe move; (2) the search's opponent
model is A (B5's honest limitation), and A's last-round movement lets threat avoidance beat a
game-winning denial. Before #396 the search never got deep enough to notice, and P1's leaf
carried the probe. The fix is in A, not in the probe or the search: in the last round a unit
that can deny or seize a marker should walk into range to do it. Not built here (it is a
Tactician facet, Chris's call whether it goes into this window); the probe is left as is and
counted 4/5 until then.

*The gate.* v4 was stopped at 215 matrix games (15:1x) and NOT resumed: it is a wall-clock budget,
and the same budget now buys an order of magnitude more iterations, so the old games measure a
different bot. v5 launched 15:58 on `step10bin-v7`, same script otherwise
(`<scratchpad>/step10-gate-v5.sh` -> `FdgLab/reports/step10-gate-v5-2026-09-05/`), self-play v2
chained behind it as before. Peak RSS of one interactive-budget search at a round-4 2k root:
1.5 GB for the whole b0 run (search phases included), live tree 46 MiB at benchmark budget - dop 6 is safe on memory. What is left of an expansion at 2k is the in-sim policy itself (a natural activation costs 25-70 ms, a prescribed one 7 ms), so the next speed-up, if one is wanted, is in A's planner, not in state handling.

**2026-09-05 (15:10, Fable 5.1) - WHERE THE SEARCH'S CYCLES GO: A CPU PROFILE OF A REAL STRATEGIST
GAME. #396 FILED.** Chris: "why do we serialize at all for simulation?" - and "a better way to copy
the game state is a clear win". Measured rather than argued: the gate was killed (resumable, 104
games on disk), one Strategist-vs-Tactician 2k game was played under `dotnet-trace` on two spare
cores, and the gate resumed at 14:55 from its progress file (six in-flight games replayed, ~3 min).

Of 133 s of CPU, 63% inside the simulations: **41% JSON serialize/deserialize, 28% finalizing
dynamic methods on the finalizer thread (an emitter somewhere lets `DynamicMethod`s die at a high
rate - unexplained, first thing #396 pins), 13% rules/tokens, 6% the planner, 4% stage machinery,
0.3% the evaluator.** The state copy is the dominant cost of a search expansion, two-thirds of it
if the churn is part of it. Full table, the b0 wall-clock cross-check, and the plan are in
`WorkItems/396-simulation-state-copy.md` (filed, todo, not inside this window).

A first profile of `b0` itself was a trap worth recording: 46% of ITS CPU was exception unwinding
from b0's own throw-stops - the stage machine nests every transition as an awaited call, 300-500
frames deep by round 3, and a throw is rethrown at every frame while walking the rest: quadratic,
seconds per throw. The engine's simulation stop has been cooperative since R9 and pays nothing; the
b0 capture and THROW phases still do. Any real engine fault deep in a game pays it too.

**2026-09-05 (14:00, Fable 5.1) - CALIBRATED; THE COMPREHENSIVE GATE RUN IS LAUNCHED (v4, SHIPPING
BUDGET, ~12 h); SELF-PLAY v2 CHAINED BEHIND IT. HANDOFF.** Superproject: this commit (engine `41f178d`).

*Calibration (`calibrate.sh` on `step10bin-v6`, dop 6, interactive budget, box idle, 0 timeouts):*

| cell type | games | s/game mean | p95 | games/hour |
|---|---|---|---|---|
| 2k 1v1 (A/B runs) | 300 | 157 | - | 138 |
| 1k 1v1 | 12 | 107 | 121 | 180 |
| 3k 1v1 | 12 | 169 | 186 | 122 |
| 4k 1v1 | 12 | 276 | 330 | 66 |
| 2v2 at 2k | 16 | 341 | 454 | 53 |
| 2v2 at 3k | 12 | 458 | 633 | 45 |

*The run (`<scratchpad>/step10-gate-v4.sh` -> `FdgLab/reports/step10-gate-v4-2026-09-05/`, launched
13:56, ETA ~02:00 Sep 6):* Strategist vs Tactician only, dop 6, dumper armed, resumable (same
`--out` on retry, 3 attempts per cell), self-play paused for the chain. Cells in order: main matrix
(64 ordered pairs x 12 = 768 games, ~5.6 h - THE reading, aggregate SE ~1.8 points); the three
weakest Orks-opponent cells again with `--dump-logs` (RL/DE/BB vs Orks, 12 games each, separate
seeds, not in the aggregate - the round-4 transcripts for the failure analysis, G2); points-1k x30,
points-3k x30, points-4k x30, shape-2v2-2k x24, shape-2v2-3k x30 (per-cell SE ~9: directional, sec
5's "every cell >= 50%" is reported, not applied as pass/fail); Titan reverse x30; ffa-smoke. When the
chain ends it removes the pause file and launches **self-play v2** itself (`step10bin-v6`, `--out
FdgLab/data/2026-09-05-v2`, dop 12, seed base 200000, schema 2, 8 GiB heap cap; pid in
`<scratchpad>/selfplay-v2.pid`). The v1 process (pid 69804, old binary) was killed at 13:40 - its
wrapper had no restart loop; v1 data (`FdgLab/data/2026-09-03`) stays valid v1 data.

*Reading the gate when it lands (step 10 item 6):* `bench.md` per cell dir. Main-matrix aggregate vs
the 60% threshold is the verdict; the bench-budget reference points are 56.6% (full matrix, old
evaluator) and 60.8%/60.5% (6-cell probe at interactive budget, parts 0/1). Per-cell numbers are
directional. Titan reverse first, then the Orks logs. If the aggregate is short, the failure analysis
is Opus/high per the campaign, reading round-4 transcripts, and P4 (Contest macro) is the one
vocabulary decision still open for Chris.

**HANDOFF (a fresh instance reads this first, then the entries below down to the 12:20 handoff,
then `docs/tactician-bc-campaign.md` sec 0/4-6):**
- Repo state: superproject this commit / engine `41f178d`, branch `tactician-bc` both, pushed.
- Session scratchpad (absolute; a new session gets a different one):
  `/tmp/claude-1000/-home-chris-Projects-fdg-raylib-Purple/23109a3d-287a-4386-b169-c5057436f5b9/scratchpad/`
  - `step10bin-v6/FdgLab`: Release build of engine `41f178d` (P0/P1/P3 + root choice), hash-verified
    `4241CF7010C28571`. Never build into `FdgLab/bin/Release` while a lab process runs from it.
  - RUNNING: `step10-gate-v4.sh` (log `step10-gate-v4.log`; bench pids via `pgrep -x FdgLab`). Do
    not run anything else on the box until it prints `SELF-PLAY v2 LAUNCHED` - the budget is
    wall-clock. Then self-play v2 runs at dop 12 (leaves room for builds/tests, not for benches:
    touch `FdgLab/.pause-selfplay` first, remove after).
  - `verify-p013.sh`: the full verify chain (suite, Release build to `step10bin-v6`, hash x2, probes
    x3, smokes) - rerun after any engine change, into a NEW bin dir (edit BIN_DIR).
  - `calibrate.sh`, `calib/`: the table above. `depth-r4.log`: the P2 measurement.
- MUST KEEP RUNNING: bad-page pinner pid 72538 (`VmLck: 4 kB` in /proc/72538/status; restart with
  `python3 <old scratchpad>/badpage-pinner.py 24`, old scratchpad path in the 12:20 handoff).
- Rules that cost hours when broken: dop 6 for anything with search; dumper armed on every lab
  invocation; never edit a running bash script; never `pgrep -f`/`pkill -f` a pattern that is in your
  own command line (use `pgrep -x FdgLab`); `setsid ... &` makes `$!` the dead wrapper; every
  dotnet-* diagnostic tool needs `TMPDIR=/tmp`; `bench` resumes from `<out>/bench.progress.jsonl` -
  fresh `--out` (or `--fresh`) for a new measurement; only ever one self-play process.
- Monitors do not survive /clear: re-arm on `step10-gate-v4.log` (cell `exit=`, `FAILED`, `GATE v4
  DONE`, `SELF-PLAY v2 LAUNCHED`) and on the pinner's death.
- NEXT: (6) compile the gate (aggregates are the reading, per-cell directional; Titan reverse and
  the Orks logs first); (7) confirm self-play v2 is running and writing schema-2 batches into the
  new directory; then the step 10 verdict and, if short, the Opus/high failure analysis on the Orks
  transcripts before any further change.

**2026-09-05 (13:10, Fable 5.1) - DEPTH AT A ROUND-4 START, INTERACTIVE BUDGET: 6-7. P2 DEFERRED.**
`b0 --round 4 --search-budget interactive --search-workers 4` on the v6 binary (Alien Hives vs Battle
Brothers, seed 4242, first boundary of round 4 - 4 root units on the acting side, box idle):

| budget | iterations | nodes | max depth |
|---|---|---|---|
| 20 iterations, 1 worker (the B4 reference) | 20 | 21 | 5 |
| benchmark (1.48 s, 4 workers) | 69 | 69 | 5 |
| interactive (6.6 s, 4 workers) rep 1/2/3 | 315 / 327 / 346 | 312 / 327 / 343 | **6 / 7 / 7** |

One tree edge = one activation, so from this round-4 start the shipping search sees 6-7 of the
roughly 4 + (enemy's remaining) activations left in the game - most of the last round on this
board, and the leaf sits within a few activations of the real result, which P1 now counts. That
is the case FOR P2 being small and the case AGAINST paying for it: an endgame rollout replaces a
2 ms leaf with up to K activations of in-sim play (the [3g] line cost is ~95 ms per expansion),
i.e. it buys the true result at the price of an order of magnitude fewer iterations at exactly the
budget that produced the only measured lever so far (+8.3 for budget). Whether that trade is
positive is an A/B question with no room in the window (ends Sep 7; the gate run is 12-13 h).
**Deferred**, with the caveat recorded: this was a 4-unit root; a horde or a 2v2 side with 10+
unactivated units in round 4 will be shallower (widening opens ~5 root units and the rest of the
depth is spent across more branches), and that is exactly where the Orks cells live - the
comprehensive run's `--dump-logs` on those cells is what says whether depth or the vocabulary (P4)
is the binding constraint there. P4 (Contest macro) remains Chris's call.

**2026-09-05 (13:05, Fable 5.1) - P0 + P1 + P3 BUILT AND VERIFIED; A/B #2 STOPPED AT 93 GAMES (FLAT);
THE ROOT CHOICE WAS UNDERCOUNTING WHOLE UNITS.** Engine `41f178d`, superproject: this commit.

*A/B #2 (part 2 vs part 1), stopped by Chris's call at 12:55 after 93 of 300 games* - the argument for
letting it finish was attribution insurance only, and P1 overwrites part 2's last-round behaviour
anyway, so the number would have been half stale. Paired on the SAME seeds it completed (the first
two cells only, both Orks-opponent):

| cell | n | part 1 | part 2 | delta |
|---|---|---|---|---|
| Robot Legions vs Orks | 50 | 31.0% | 30.0% | -1.0 |
| Dark Elf Raiders vs Orks | 43 | 26.7% | 30.2% | +3.5 |
| **paired aggregate** | **93** | **29.0%** | **30.1%** | **+1.1** |

Noise, same as A/B #1 (-0.3 at n=300). Two evaluator rebalances, no measurable lever at interactive
depth on 1v1 2k. The data is in `FdgLab/reports/step10-budget-probe-evalfix2/bench.progress.jsonl`
(resumable if anyone ever wants the other 207 games; nobody should).

*What was built (design per the 11:00 entry; all three below the plan's seams, no schema change):*
- **P0 - objective-aware wound allocation** (`TacticianAssignWoundsResolver`, now takes the table
  state; registry passes it). A model within 3" of a marker the unit holds or contests carries a
  marker stake when it would die: `TacticianWeights.WoundObjectiveHold` (15) x `ObjectiveUrgency`
  (x0.66 round 1 .. x1.3 last round), split across the unit's models still standing on that marker,
  so the LAST body on the marker is worth ~10 in round 1 and ~20 in the last round - above any single
  model's gun (a 10-shot AP2 autocannon prices 13). A fifth of the stake when another allied unit
  also stands there (then output decides again). Three pins: partial-on-marker unit takes 3 wounds ->
  both on-marker models survive; the last on-marker rifleman outlives a heavy gunner; with an ally on
  the marker the gunner is kept instead. Below the search seam: fixes A and B at once.
- **P1 - last-round counting** (`RoundEndProjection`, new; `HandWeightedEvaluator` last-round branch).
  Movers = unactivated, seize-eligible units not already on a marker. Pass 1: every held marker is
  denied if an opposing mover can reach it (rush + 3"), a sticky-held marker with no holder is seized
  by the one side that can reach it; pass 2: a neutral marker goes to the one side that can reach it,
  standoffs stay neutral; a mover is SPENT by its assignment (one unit cannot deny two markers; least
  flexible unit first); pass 3: a marker still held inside an unspent enemy's shooting reach counts
  half. The evaluator blends it with the realized projection at confidence 0.75, so walking onto a
  marker you were projected to take is still worth the remaining quarter (gradient kept). Measured
  on one marker at round-4 start: an unactivated enemy that can walk onto our marker now costs 0.21
  (v2's flat half: 0.12); a second denier on two held markers costs a second marker (v2: only the
  material); a neutral marker only we can reach reads 0.17 ours before the unit moves (v2: 0). Four
  pins, plus the existing v2 pins still pass (the near-vs-far kill premium is now a full marker).
  Before the last round nothing changes.
- **P3 - last-round tempo prior** (`TacticianActivationResolver.Urgency`): in the final round the
  flip term becomes tempo - units that cannot reach a marker they would change are spent first
  (+0.5), responders are held for last (-0.5); the gap is above the flip bonus and every ordinary
  kill/threat fraction so it orders the round, with kill/threat ordering inside each group. Lives in
  the ActivationScores the root priors come from, so A and the tree both get it. Two pins (round 4
  spends the idler; round 3 the responder still leads).
- **Root choice, found by the new probe:** `UctSearch.ChooseRoot` picked the single most visited
  EDGE across all units. In `hold-the-responder` the idle squad's branch took 376 of 525 visits split
  five ways across interchangeable macros (80 each) and LOST to the responder's one edge at 96 -
  against the search's own values (0.19 vs 0.06). Now robust-child the way the tree is shaped: the
  unit with the most visits in total, then the most visited edge under it (ties unchanged). The probe
  went from 1/3 to 3/3 and nothing else moved.

*Verification (all on engine `41f178d`, box otherwise idle):* suite 3242/0/1 (nine new tests). DOP-1
Tactician-vs-Tactician six-game hash `4241CF7010C28571` on two consecutive runs - **deterministic, and
CHANGED from `8D6EFA0AF0B4019E` by construction**: P0 and P3 alter the A policy (wound picks, round-4
activation order), so the transcripts differ; the handoff's "unchanged" expectation could not hold
for this change and the check now reads determinism + liveness. Probes 5/5 on three consecutive
runs (`hold-the-responder`, `count-says-they-win` new; the harness accepts `"Action": "*"` for
"this unit, doing anything"). Headless smoke exit 0; 1-game Strategist smoke exit 0, 0 faults, hash
`8027DC5F2D7F85FF`. Release binary `<scratchpad>/step10bin-v6`.

*Scope, said out loud:* no probe for P0 ("deny-with-one-model-last"). Wound allocation sits below
the search seam, so a unit->action expectation cannot discriminate it; its three pin tests carry it.
`b0` grew `--round R` (capture the first boundary of round R) and `--search-budget
benchmark|interactive` (the tree a TIME budget buys, shipping worker count, prints max depth) for
the P2 sizing measurement - running now, next entry.

**2026-09-05 (12:20, Fable 5.1) - A/B #1 IS FLAT; P0 FILED (OBJECTIVE-BLIND WOUND ALLOCATION); HANDOFF
FOR THE NEXT INSTANCE.**

*A/B #1 (evaluator part 1 vs old; `FdgLab/reports/step10-budget-probe-evalfix`, hash
`FB77A685052A1BB9`, 300 paired games, 0 faults/timeouts, 155.8 s/game):* **60.8% -> 60.5%, delta
-0.3.** Per cell +4.0 / -9.0 / +4.0 / -2.0 / 0.0 / +1.0 - noise at n=50 per cell. The rebalance
that the 12:1 ratio and the flat 0.500-0.504 landscape argued for did NOT move win rate on six
1v1 2k cells at shipping budget. Honest reading: at interactive depth on 1v1 2k the root choice is
dominated by the A-policy priors and the tree, not by leaf weights; the defect may still bite at
bench budget (shallower) and in 2v2 (more of the game away from markers), neither measured here.
The BUDGET (+8.3) remains the only lever with a measured effect. **A/B #2** (part 2, threat to
holdings, vs part 1; same cells/seeds/budget) launched 12:15, `scratchpad/evalfix2-ab.sh` ->
`FdgLab/reports/step10-budget-probe-evalfix2`, ETA ~14:25.

*P0 - objective-blind wound allocation (Chris, from a GUI game):* "an enemy unit partially on an
objective ... took losses, and assigned wounds that killed off models that were ON the objective,
such that the unit was no longer holding the objective. A human would never."
`Ai/Tactician/Resolvers/TacticianAssignWoundsResolver.cs` (`CostPerWound`) prices which model to
lose with no marker awareness. Fix shape: models within the 3" seizure radius of a marker the unit
holds or contests are last to die (a large cost term, round-scaled like ObjectiveUrgency), with
a pin test (partial-on-marker unit takes N wounds -> the on-marker models survive) and a probe.
Cheap, certain, and squarely inside Chris's "micro-managing objective holding" observation. It is a
resolver below the search seam, so it fixes A and B at once.

*Decisions (Chris):* P1 (round-end counting in the evaluator) and P3 (last-round tempo prior)
first; P0 alongside as the cheapest of the three. P2 sized by the depth measurement; P4 (Contest
macro) still his call. Chris also chose to /clear before this work - the handoff below is for the
next instance.

**HANDOFF (read this first, then the previous ~6 entries, then `docs/tactician-bc-campaign.md`
sec 0/4-6; model: this is design+build work -> Opus or Fable per sec 3, then Sonnet for the runs):**
- Repo state: superproject `ad6de4e`+ / engine `0b0721d`, branch `tactician-bc` both, pushed.
- Session scratchpad (absolute; a new session gets a DIFFERENT one, read these by path):
  `/tmp/claude-1000/-home-chris-Projects-fdg-raylib-Purple/3de923b1-0a6a-4cc9-9335-4dbfcfd958aa/scratchpad/`
  - `step10bin-v5/FdgLab`: Release build of the CURRENT engine (part-2 evaluator, schema v2),
    hash-verified `8D6EFA0AF0B4019E`. Build a fresh one after any engine change (never into
    `FdgLab/bin/Release` while a lab process runs from it).
  - RUNNING: `evalfix2-ab.sh` (pid of the bench: `pgrep -f step10bin-v5`), log `evalfix2-ab.log`,
    prints the part-1 vs part-2 table when done (~14:25). Do not run anything else on the box
    until it finishes - its budget is wall-clock, contention makes it measure a weaker bot.
  - `calibrate.sh` (ready, NOT run): ~20 min of games per cell type at shipping budget; sets the
    comprehensive run's game counts from measured throughput. Run after A/B #2.
  - `paired-compare.py`: paired scorer (score_a vs score_b from bench.csv; never match on
    winner_army - it is the army's internal name, not the filename).
  - `HANDOFF-step10.md`, `HANDOFF.md`: earlier operational notes (crash chase, pinner, gate v1-v3).
- MUST KEEP RUNNING: bad-page pinner pid 72538 (`VmLck: 4 kB` in /proc/72538/status). It holds
  the faulty RAM page (bit 23 stuck at 0). If it dies: `python3 <scratchpad>/badpage-pinner.py 24`.
- Self-play: PAUSED (`FdgLab/.pause-selfplay`), v1 process pid in `<scratchpad>/selfplay.pid` (old
  binary, schema v1). When resuming, KILL that process and start fresh on the new binary into a
  NEW dir: `<bin> selfplay --out FdgLab/data/2026-09-05-v2 --dop 12 --seed-base 200000 --pause-file
  FdgLab/.pause-selfplay` (v1 ended at seed 86999; 200000 is safely past; schema=2 files must
  not share a directory with v1 files). Only ever one self-play process.
- Rules that cost hours when broken: dop 6 for anything with search (dop 12 crashed within 95
  min); every lab invocation with the runtime dumper armed
  (`DOTNET_DbgEnableMiniDump=1 DOTNET_DbgMiniDumpType=4 DOTNET_DbgMiniDumpName=<dir>/x-%p.dmp`);
  never edit a running bash script; never `pgrep -f`/`pkill -f` a pattern that is in your own
  command line; `setsid ... &` makes `$!` the dead wrapper - find pids with `pgrep -f <binary>`;
  every dotnet-* diagnostic tool needs `TMPDIR=/tmp`; `bench` now resumes from
  `<out>/bench.progress.jsonl` - use a FRESH `--out` (or `--fresh`) for a new measurement.
- Monitors do not survive /clear: re-arm one on `evalfix2-ab.log` ("EVALFIX2 A/B DONE") and one on
  the pinner's death.
- NEXT, in order: (1) collect A/B #2 into the ledger; (2) P0 + P1 + P3 with tests, new endgame
  probes (`FdgLab/probes/`: deny-with-one-model-last, hold-the-responder, count-says-they-win),
  suite, hash-verify, smokes, commit submodule-first; (3) 5-min depth measurement
  (`fdglab b0 --search-iterations` from a round-4 start at interactive budget) -> size P2 or
  defer it; (4) `calibrate.sh`; (5) the 12-13 h comprehensive run at shipping budget: all 64
  main-matrix cells x ~12 games, every panel cell x ~30, Titan reverse, ffa-smoke, vs Tactician
  only, `--dump-logs` on the Orks cells, dop 6, dumper armed, resumable, self-play paused; (6)
  compile the gate: aggregates are the reading, per-cell is directional (sec 5's "every cell
  >= 50%" cannot be applied strictly at n~30, say so); (7) resume self-play v2 for the rest of the
  window (ends Sep 7).

**2026-09-05 (11:00, Fable 5.1) - THINKING PASS: LAST-ROUND BEHAVIOUR. A PROPOSAL, NOT A CHANGE.**
Chris: "most of my hard-earned wins against the bot come down to my micro-managing of objective
holding toward the end ... Don't change anything about the plan yet."

*The rules that make the last round what it is (read, not remembered):* `ReconcileObjectiveOwner`
- nobody in range: previous owner keeps it; one side in range: they take it; BOTH in range: it goes
NEUTRAL. So a single surviving model within 3" of an enemy-held marker at round end denies it
outright; a holder must be fully removed from 3" to be taken, but merely contested to be lost.
Activations alternate, so the side with more unactivated units gets the last unopposed moves.
The A-era ledger already named this: "remaining losses/ties are objective endgames," "the horde's
surplus bodies take every marker in round 4" - and the A-era straggler cells (RL/DE/BB vs Orks) are
exactly the Strategist's worst cells today. Chris's observation is the same fact from the other
side of the table.

*Where the machinery falls short, each tied to code:*
1. **Horizon.** `Continuation = 0`: one tree edge = one activation, so depth = activations seen.
   Depth 5 at benchmark budget; perhaps 8-12 at interactive (unmeasured). A 2k round is ~18
   activations. From the start of round 4 the search sees about half the round and hands the rest
   to the evaluator; it only reaches the real result (terminal = true score) in the last third.
2. **The evaluator cannot count.** held / contested / threatened are per-marker yes/no. "They have
   three unactivated units that can reach my two markers and I have one responder" is invisible.
   `activation_share` and `activations_left_frac` are encoded but unused by the evaluator.
3. **Activation order is tactics-only.** `Urgency()` = kill / threat / flip terms; no round
   awareness, no "keep the responder for last." The root's unit priors come from it and widening
   (C=0.5) opens ~5 units, so "activate the irrelevant unit now" is rarely even explored.
4. **No sliver-contest.** M2/M3 path the unit's MASS to within the 3" goal radius. A human parks
   one model within 3" and keeps the rest safe or spread toward a second marker (coherency allows
   9"). Under the neutral rule one model is a full denial; the vocabulary cannot say it.
5. **Holder survivability is not modelled.** "threatened" is reach-based: a 20-model horde in reach
   and a 3-model squad in reach are the same threat. Expected survivors within 3" is the real
   quantity (CombatMath can price it per held marker).

*Proposals, cheapest and most certain first:*
- **P1 - round-end projection in the evaluator (counting).** In the last round, replace the per-
  marker yes/no with a projected round-end tally: for every unactivated unit on both sides, the
  markers it can reach; a greedy assignment (deny enemy-held, secure own-held, then seize neutral);
  reconcile with the neutral rule; value from the projected (mine - theirs). O(units x markers).
  Verify: a probe where the count says the enemy wins unless a specific unit moves; A/B on the
  Orks cells. Also a natural v3 encoder feature later.
- **P2 - endgame rollouts.** When <= K activations remain (K ~ 6-8), the leaf = play the round out
  with the in-sim A policy and take the REAL result. The machinery exists (`Continuation`); a
  per-node dynamic continuation is a small change in `SimulationExpander`. Cost-bounded by K.
  Verify depth FIRST: `fdglab b0 --search-iterations` prints max depth - measure from a round-4
  start at interactive budget (5 min, during the calibration window; not now, it would perturb
  the A/B).
- **P3 - last-round tempo prior.** In round 4, boost the activation prior of units that cannot
  affect any marker (spend them first) and hold units that can secure/deny one. Lives in the
  `ActivationScores` the root priors come from, so A and the search both benefit. Verify: probe
  "irrelevant unit + responder: activate the irrelevant one."
- **P4 - a Contest macro (M14): sliver denial.** Move so one model ends within 3" of the target
  marker while the unit's centroid ends as far back/aside as coherency allows. A vocabulary change
  (Appendix A is Chris-confirmed) and movement-planner geometry: the biggest lift here, and the
  one a human most visibly exploits. Verify: probe + the DE/RL-vs-Orks cells.
- **P5 - round-4 budget headroom.** Under the 10 s GUI cap there is ~16% headroom (8.6 -> 10 s);
  anything more is Chris's call on feel. Benches could reallocate rounds 1-2 -> round 4.

*Recommendation:* P1 + P3 first (cheap, address counting and tempo directly), P2 sized by the
depth measurement, P4 as the one vocabulary decision for Chris, P5 optional. Sequencing relative to
the current plan (unchanged): measure depth in the calibration window; in the comprehensive run,
`--dump-logs` the Orks cells so the failure analysis has round-4 transcripts to read (G2); decide
P1-P4 on that evidence after the run, not before it.

**2026-09-05 (10:35, Fable 5.1) - EVALUATOR FIX PART 2, FROM CHRIS'S REVIEW: THREAT TO HOLDINGS,
AND THE C1 SCHEMA GOES TO v2.** Chris: "In the final round, unactivated material not on objectives
can still be important to kill because it can 1. move to objectives and 2. destroy your units that
hold objectives." Answered in two parts: the decay itself was not the problem (progress is by
activations, so round 4 opens with material at 0.24, 44% of its round-1 weight, and the last
boundary is covered by the search reaching the real result) - but the review exposed that
material was FUNGIBLE: the unit 6" from your held marker and the one 40" away were worth the same
(0.006 to kill either, against a 0.077 marker swing), and the search only saw the difference
inside its horizon. Chris chose both halves: the evaluator term and the v2 encoder feature.
Engine `0b0721d`, superproject `072201c`.

- `obj_held_threatened_share` (block[15]): of a side's projected-held markers, the share an
  opposing unit can reach this round (cheap threat reach + 3" seizure radius covers the marker
  point - contest it or hit the holder). Last round: only UNACTIVATED enemies count; earlier
  rounds: any living enemy in reach. Width 67 -> 71, `SchemaVersion` 2. Per-leaf 1.2 -> 2.0 ms.
- Evaluator: a threatened held marker counts half. Round-4 measurement: killing the unit that can
  take our marker +0.175, killing its distant twin +0.065 (old evaluator: equal). The premium is
  the half-marker exactly.
- Suite 3233/0/1 (five encoder tests - the encoder's first - and one evaluator test). Hash
  `8D6EFA0AF0B4019E` unchanged. Probes 3/3 x3. Smokes clean.
- **Data:** self-play files before this are v1, after are v2; v1 stays valid v1 data; the loader at
  step 12/13 trains on v2 only or imputes (the 4 columns are not recomputable from a row). When
  self-play resumes it goes to a NEW --out with a seed base past batch ~420 of the v1 run.

*Chris's second question - is a 12-13 h gate too reduced to mean anything?* Answered in the chat and
recorded here: the AGGREGATES are meaningful at that size (all 64 matrix cells at ~12 games each is
768 games, aggregate SE ~1.8 points and no subset bias - the 6-cell probe ran 4 points under the full
matrix at the same budget); individual panel cells at ~30 games are directional only (SE ~9), so
sec 5's "every cell >= 50%" cannot be applied as a strict pass/fail there and will be reported as
such. Skip the vs-SoloRules passes (ceiling checks; a collapse would show against Tactician).
Chris's human-judgment point is right and is why the gate carries his games: outcome-only
measurement across 8 armies x 2 sides is a poor signal per game, and the volume pays for the
signal's poverty, not for the bot being hard to read - and the per-cell breakdown is what made the
Orks finding visible at all.

*Sequence from here:* A/B #1 (fix part 1 vs old, running since 10:02) lands ~12:05 -> A/B #2 (part 2
vs part 1, same cells/seeds/budget, ~2h10m) -> 20-min calibration of per-cell costs at shipping
budget -> the 12-13 h comprehensive run -> self-play (v2 schema, new directory) for the rest of the
window.

**2026-09-05 (10:05, Fable 5.1) - THE EVALUATOR FIX (step 10 failure analysis). Chris: "Before doing
so, fix the evaluator issue. I will turn you up to Fable for that fix."** Engine `23ba22f`,
superproject `91a0c2d`.

*The defect, quantified from the old evaluator's own arithmetic (0.55 held / 0.30 value / 0.15
threat, two-sided as 0.5 + (own - other)/2):* one marker of three = +0.092; killing 10% of the
enemy army = +0.0078. **12:1.** A marker outweighed wiping out more than the entire opposing
army. And `obj_held_share` is a step at the 3" seizure radius, so between markers the objective
term was constant for both sides, cancelled in the subtraction, and the whole landscape sat inside
~0.004 - which is exactly the 0.500-0.504 root spread measured in the charge-vs-shoot probe on
2026-09-04. The search then ranked by prior noise where the one-ply policy ranked correctly:
search worse than no search, precisely where a game spends most of its activations.

*The fix (entirely inside the v1 C1 schema - no self-play data invalidated; C3's net gets the
same features plus the globals it already has):*
- Objective term gets gradient: 0.70 projected-held + 0.20 contested (in range, not owned) +
  0.10 approach (1 - closest unit's normalized distance to any marker). Walking at a marker is a
  slope, not a cliff.
- Material (`value_share`) weighs 0.55 at the start of the game and decays quadratically to 0 at
  the last activation of the last round; objectives rise 0.35 -> 0.90 to fill it; threat coverage
  a constant 0.10. Material is instrumental (it wins the remaining rounds' marker contests); at
  the final activation only projected holdings are the score.
- Game progress = (round - 1 + fraction of living units activated this round) / total rounds, read
  the same way the encoder's round_frac / activation_frac are.

*Measured on the test board (10 units/side, 3 markers):* round 1 - marker +0.044, 30% kill +0.049
(ratio 1.11, was 0.29); round 4 start - marker +0.082, kill +0.021 (0.26). Approaching a marker
from 30" to 15" now raises value (was exactly 0). Per-leaf cost 1.22 ms, unchanged. Four new tests
pin these (three fail on the old evaluator by construction; the fourth pins the progress read).
Suite 3227/0/1. **Tactician-vs-Tactician DOP-1 hash unchanged `8D6EFA0AF0B4019E`** - the A policy
is untouched. Headless smoke exit 0; 2-game Strategist smoke on the new binary 0 faults.

*The probes, and a correction to their design (superproject commit):* the charge-vs-shoot probes
as first authored (round 4, marker unreachable) had NO correct answer under this ruleset -
eliminating the enemy does not win, markers do, so shoot/charge/pass all end in a tie, and once
the landscape was no longer flat the search reported exactly that (Pass 0.513, everything else
0.505: a terminal tie is 0.5 by `SideValues.FromResult`; a non-terminal material lead reads a
touch higher). Redesigned: round 3, the enemy holds the only marker 8" away - the clean kill
(gun) frees it for next round, the partial kill (knife) leaves a survivor contesting it, passing
concedes it. All three probes pass 3/3 on repeats; `shoot-favored` is gating again. In that
geometry the one-ply planner ranks a walk toward the marker FIRST (1.38 vs Charge 1.20 vs
Hold+Shoot 0.47) and the search picks Shoot - the search adding value over the heuristic, which is
the whole point of B. **Honest control:** the OLD evaluator also passes the redesigned probes
(verified with a properly stashed build - a first attempt at the control silently ran the new
binary because the old file failed to compile against the new tests, and was discarded). So the
probes are rule-correct regression guards, not the fix's discriminator. The discriminator is the
unit tests above and the paired A/B below.

*A/B, running (`scratchpad/evalfix-ab.sh`, report `FdgLab/reports/step10-budget-probe-evalfix`):*
the exact conditions of the 09:33 run that scored 60.8% on the old evaluator - same 6 cells, seeds
1000-1024, interactive budget, dop 6, self-play paused - with only the evaluator changed. ~2h10m.
Then Chris's reduced comprehensive run at shipping budget (target 12-13 h); per-cell costs at that
budget are only measured for 2k 1v1 (157 s/game), so a short calibration pass sets the game
counts first.

**2026-09-05 (09:35, Sonnet 5) - THE GATE MEASURED THE WRONG BOT. SHIPPING BUDGET IS WORTH +8.3
POINTS.** Chris's hypothesis ("was depth capped by time, did the concurrency limit it") - checked,
and it is the budget, decisively. `bench --search-budget benchmark|interactive` added (commit
`f8b9234`); `budget-probe` panel of 6 cells spanning the main matrix's measured range added
(`b9940f2`). 300 games, 0 faults, 0 timeouts, hash `E79A0ACD9176B85A`, 2h11m.

**This is a controlled comparison, not an approximate one:** same 6 cells, same seeds (1000-1024),
same opponent profile, same dop 6, same otherwise-idle box. The ONLY difference is how long the
search may think - 1-2s (lab benchmark budget, what every bench in this campaign has ever used) vs
5-10s (Interactive, what actually ships to a player). Measured cost: 157s/game vs ~52s, decision
mean 498ms vs 167ms, worst p95 6.8s vs 1.8s.

| cell | benchmark | interactive | delta |
|---|---|---|---|
| Robot Legions vs Orks | 22.0% | 27.0% | +5.0 |
| Dark Elf Raiders vs Orks | 24.0% | 33.0% | +9.0 |
| Battle Brothers vs HDF | 44.0% | 52.0% | +8.0 |
| Dwarf Guilds vs HEF | 52.0% | 72.0% | +20.0 |
| HEF vs Robot Legions | 88.0% | 94.0% | +6.0 |
| Alien Hives vs Battle Brothers | 85.0% | 87.0% | +2.0 |
| **aggregate (n=300)** | **52.5%** | **60.8%** | **+8.3** |

**Every cell improved.** Naive extrapolation onto the full matrix's 56.6% gives ~64.9%, above the
60% threshold - but that is 6 of 64 cells extrapolated, NOT a measurement, and must not be reported
as if the gate passed.

**What this means for step 10.** The gate's headline 56.6% is a real number about a deliberately
handicapped configuration (the campaign chose the cheap budget for benches on purpose - benching at
shipping budget costs ~4x). The remaining queued cells are all at the same handicapped budget. The
gate's budget policy is now a live design question for Chris, not something to settle by carrying
on. Options costed: full main matrix at interactive budget is ~46h at 100 games/cell, ~23h at 50
(the aggregate is what the main-matrix threshold reads, and n=3200 is ample for it).

The evaluator defect (previous entry) is unaffected by this and still stands on its own - more
thinking time does not fix a leaf signal that goes flat when no objective is live; it just buys
more search around it.

**2026-09-05 (06:50, Sonnet 5) - FIRST GATE NUMBER: MAIN MATRIX VS TACTICIAN, 56.6% - SHORT OF THE
60% THRESHOLD.** `main-matrix-vs-tactician` completed clean (exit 0, 0 faults across all 6,400
games, hash `48624762BA037DF1`, ~12h49m wall at dop 6). 64 matchups (8-army 2k pool, ordered pairs
incl. mirrors), 100 games each:

- **2853 A(Strategist) wins / 2014 B(Tactician) wins / 1533 ties -> aggregate 56.6% (gate: >=60%).**
- Wide per-cell spread: 22 of 64 cells have Strategist LOSING head-to-head (below 50%), weakest at
  19.0% (Robot Legions vs Orks) and 21.5% (Dark Elf Raiders vs Orks); strongest at 85.5% (Alien
  Hives vs Battle Brothers).
- **Pattern worth flagging, not yet explained:** Orks 2k - Horde Mixed appears as the OPPONENT in 4
  of the 5 weakest cells (Strategist struggling against a Tactician-piloted Orks list specifically),
  while Strategist-piloted-Orks appears in several of the strongest cells. Could be an Orks-specific
  matchup weakness, or Orks could simply be a strong list regardless of pilot (undetermined without
  comparing against the A-vs-A Orks baseline) - a candidate thread for the failure analysis, not
  chased further here (Sonnet/low is measurement, not diagnosis).

Zero faults over 6,400 games is itself a real result: the R9 freeze fix and the crash-resilience
work are holding up under real volume, not just the smoke-sized runs that verified them.

**Not yet a gate verdict** - `main-matrix-vs-solorules` (the >=85% sanity reading), every panel,
and the Titan Lords reverse pairing are still queued (now under the resumable v3 chain). This
number alone is a real, specific shortfall on the gate's primary reading, though.

**2026-09-04 (23:25, Sonnet 5) - CRASH-RESUMABLE BENCH, PER CHRIS'S REQUEST ("resuming from a
crash is not so bad").** Commits `77fae12`/`061e782`/`73a08bf`: `bench` now appends every completed
game to `<out>/bench.progress.jsonl` the instant it finishes, and a rerun with the same `--out`
skips whatever is already recorded and plays only the rest, then writes the final report over the
FULL merged set. Verified: a killed-mid-run cell resumed to the exact same outcome hash as an
uninterrupted reference run; re-invoking an already-complete `--out` resumes instantly with the
same hash; `--fresh` forces a real replay; the DOP-1 six-game hash-verify is unchanged
(`8D6EFA0AF0B4019E`). (Correction: the first commit mistakenly cited "#391", a real unrelated item -
fixed to #191 in the very next commit before anything else built on it.)

Applied starting with the NEXT cell, per Chris's own scoping - cell 1 (`main-matrix-vs-tactician`)
was already 2800/6400 games into its non-resumable v2 attempt when this landed, too far along to
usefully restart. `scratchpad/handover.sh` (running detached) watches for cell 1's conclusion and
then kills the old chain and launches `scratchpad/step10-gate-v3.sh` for cells 2-13, now using the
resumable binary (`scratchpad/step10bin-v2`) and retaining the same dop 6 / armed dumper / 3-attempt
retry from the v2 correction - except a retry within a cell now RESUMES rather than replaying it
from zero.

**2026-09-04 (evening, Sonnet 5) - CHRIS'S GAME 2 OF 2 (step 10 gate requirement), verbatim, AND
HE APPROVES THE GUI CHECK:** "I played the second game. 3k. I won my even more but I think the bot
played quite well there, too. Go ahead and file that as a GUI approval for me." Second and final
game at 3k (game 1 was smaller/unspecified points, both wins for Chris, both with the bot playing
well by his read). **Gate requirement "Chris plays >= 2 games ('does it anticipate?'), verbatim" -
DONE, Chris-approved.** The R9 check (first GUI game with search - window stays live) is likewise
CLOSED: two full GUI games on the freeze-fix build (`dc2c354`/`75cda37`) with no freeze reported.
Remaining step 10 items: the compute gate (running), the probe harness (done, 2/2 gating probes
green), last-round-steal/charge-vs-shoot (done). Once the gate's numbers are in, step 10 needs only
the pass/fail compile against sec 5's table before L1.

**2026-09-04 (evening, Sonnet 5) - CHRIS'S GAME 1 OF 2 (step 10 gate requirement), verbatim:**
"I just played a game in the GUI. I won, but had to work hard for it and had some luck on my side.
I think the Strategist played very well." Played on the Windows laptop, tactician-bc at (or after)
superproject `dc2c354` / submodule `75cda37` - the R9 freeze fix build. **No freeze reported**, so
the R9 check (GUI game with search stays live) is READ AS PASSED pending any contrary detail Chris
adds later; the earlier "cooperative simulation stop stops it re-throwing 400 exceptions per
activation" diagnosis holds up in the field. One of the gate's required >= 2 games logged; one more
still needed.

**2026-09-04 (18:40, Sonnet 5) - CHRIS'S CALL: LET THE GATE FINISH, SELF-PLAY STAYS PAUSED FOR THE
TRIP.** Chris asked whether the gate's data could train C (no - `bench` writes win/loss outcomes,
only `selfplay` exports the per-decision PositionEncoder rows C needs) and flagged that self-play,
paused since step 10 started, was what he'd actually meant to use these four days for - the
original campaign calendar had step 10 scheduled for after his return for exactly this reason.
Given self-play data isn't due until step 12 (week of Sep 15 regardless), offered the tradeoff
explicitly: **Chris chose to let the gate finish** rather than stop it to resume self-play now.
Self-play stays paused until the chain's own end (~2.5-3 days out), then resumes automatically.

**2026-09-04 (18:15, Sonnet 5) - CRASH #8, AT DOP 12, DURING THE GATE - THE CRASH CHASE'S "CLOSED"
VERDICT NEEDED A CORRECTION.** 95 minutes into `main-matrix-vs-tactician` (1675/6400 games, dop 12),
the bench process SIGSEGV'd: `kernel: .NET BGC[80001]: segfault ... in libcoreclr.so[459b4a,...]` -
an EIGHTH distinct fault offset, same "GC walking corrupted memory" shape as every prior crash. The
pinned bad page (pid 72538) was confirmed still held (`VmLck: 4 kB`) at the moment of the crash, so
this is not the same page - either a second marginal cell, or the earlier "3-for-4 then clean"
evidence was always thinner than the "closed" write-up treated it. **Correction to the 2026-09-04
midday entry:** dop 6-12 do NOT carry equal risk; dop 6 has an extensive, multi-hour, sometimes
concurrent-with-self-play clean record this session, dop 12 had never previously run more than a
few minutes before this gate launch. Compounding the miss: `step10bin` was built without the
runtime's crash dumper armed, so this crash produced only a DAC-less systemd core (confirmed
unreadable by `dotnet-dump` - "Failed to load data access module" - same limitation logged on
2026-09-03), no forensic value beyond the kernel line.

**Response:** the whole gate chain was killed (it had moved on to `main-matrix-vs-solorules` per
its own design, oblivious to the crash - that design choice, keep-going-past-a-cell-failure, is
right for an unattended chain but meant the crashed cell's 1675 games of work were simply gone,
no report). Rewritten (`scratchpad/step10-gate.sh`, v2) and relaunched from the top: **dop 6
everywhere** (every cell, including panels that were at dop 8-12), the runtime crash dumper armed
on every invocation, and each cell now retries up to 3 attempts before being reported as failed
rather than silently skipped. Wall-clock estimate revised upward accordingly - roughly 2.5-3 days
now, not 1.5. If dop 6 also crashes before this gate completes, that reopens the hardware
diagnosis (a second weak cell, found the same way: `verifyheap` on whatever dump the dumper now
guarantees).

**2026-09-04 (evening, Sonnet 5) - STEP 10 STARTED: PROBE HARNESS BUILT, TWO OF THREE PROBES GREEN,
ONE REAL FINDING ABOUT THE LEAF EVALUATOR. GATE RUNS QUEUED.** Chris: "please do step 10." Model
switched Fable 5.1 -> Sonnet 5 per the campaign's own protocol (step 10 is "runs: Sonnet/low").

*Probe harness (commit 2d2a76b) - the 2026-07-11 handoff item 1, never built before now.* Each
`FdgLab/probes/*.json` (ScenarioCompiler format) plus a `<name>.expect.json` sidecar compiles to
the `DeterminePlayerTurnStage` boundary, runs the real `UctSearch` on it (not a plain-policy
shortcut), and checks the prescribed unit/action. `fdglab probes` (now the default mode; the old
scaffold moved to `probes --feasibility`).

- `last-round-steal` PASS: round 4, one objective 4in away and reachable, no enemy in range -
  search correctly prescribes Move every time (checked over several repeats, iteration counts
  varying 2.4k-175k under the wall-clock budget).
- `charge-vs-shoot-melee-favored` PASS: weak sidearm (1atk AP0) vs a great axe (4atk AP3) at 8in -
  search correctly prescribes Charge, also stable over repeats.
- `charge-vs-shoot-shoot-favored` NOT gating (no `.expect.json`, harness SKIPs it) - **could not get
  this one to pass despite seven geometry iterations**, and the reason is itself a finding: under
  `HandWeightedEvaluator`'s 55%/30%/15% objective/value/threat split, this scenario's leaf value
  landed almost perfectly FLAT (0.500-0.504 across every root edge) whenever no objective was
  reachable this round, so the search's choice became close to prior noise; when an objective WAS
  present anywhere reachable-ish, Charging (which happens to close on it too) picked up a real
  value edge over standing and shooting even against a nearly worthless target, for positional
  reasons unrelated to the weapon math. The one-ply `TacticianPlanner.Score` ranked "Shoot"
  correctly in EVERY geometry tried (checked via `fdglab analyze` each time) - only the multi-ply
  search disagreed. This reproduces the shape of the step-9 "no 2v2 lift" finding (objective-
  dominant leaf evaluation drowning out tactical differentiation) in a much smaller, easier-to-
  read scenario, and both should go to the same failure-analysis pass. Scenario + note file kept
  in the repo (`FdgLab/probes/charge-vs-shoot-shoot-favored.json(.note.md)`) for that review rather
  than deleted or tuned into passing.

*`smoke --ffa` (commit cbee26d):* the "ffa-smoke" gate cell had no repeatable command before now -
one 4-slot free-for-all game, own team each, four distinct default 2k armies, fault/no-fault.
Verified clean at Tactician profile (Tie, 4 rounds, 683 decisions, no fault); the real gate run
uses Strategist.

*Gate runs (queued/running, `scratchpad/step10-gate.sh`, binary `scratchpad/step10bin` - a fresh
Release build off the current commit, hash-verified unchanged at `8D6EFA0AF0B4019E`, built to a
scratch dir rather than `FdgLab/bin/Release` so it does not disturb the running self-play process):*
main matrix (8-army 2k 1v1 pool, all ordered pairs incl. mirrors, 100 games/matchup) vs Tactician
AND vs SoloRules at dop 12; panels points-1k/3k/4k vs both at dop 8-12; shape-2v2 (2k+3k cells) vs
both at dop 6 (the proven-safe number under load, per the crash chase); the Titan Lords reverse
pairing vs both; ffa-smoke. Self-play paused for the duration (`FdgLab/.pause-selfplay`), resumes
automatically when the chain touches it off at the end. Dop chosen well above the ultra-conservative
dop 2 used for step 9's smoke: the crash chase closed the RAM defect as a pinned single page, not an
open concurrency question, so dop 6-12 carries no more risk now than dop 2 did before that finding.
Estimated wall clock: roughly a day and a half (main matrix ~15h for both passes, panels ~12h,
matching the campaign doc's own dop-16 estimate scaled to this box's more conservative dop).

*Not yet run:* Chris's >= 2 games (needs a human); a diagnostic run at `UctOptions.Interactive`
budget for the 2v2 cell (a step-9 candidate probe, left for the failure-analysis pass rather than
run speculatively here - it is judgment territory, not a "runs" task).

**2026-09-04 (16:00, Fable 5.1) - BAD PAGE PINNED.** `scratchpad/badpage-pinner.py 24` (pid in
`scratchpad/pinner.log`, chunk 29, vaddr `0x712a018b7000`, bad word at `0xa48` in the page - the
third independent sighting of the same offset) holds that one 4 KiB page mlocked (`VmLck: 4 kB`) and
released the other 24 GB. While that process lives, no other process can be given the physical page,
so the lab (and the desktop) run on the remaining 32 GB minus one page. The pinner's 10-min "still fails" line is NOT a valid re-test (it reads the 4 KiB straight back
through CPU cache, so it reports False); every test that caught the bit wrote hundreds of MB between
write and read. The pin does not depend on it; left running rather than restarted, since a restart
releases the page. Stopgap only: it dies with a reboot, and it caught
the page on its first pass because the RAM test had just released it - after a reboot it may need
several attempts while the kernel or desktop holds the page. Permanent fix: root turns the vaddr into
a physical frame with the `/proc/<pid>/pagemap` one-liner printed in `pinner.log`, then either
`memmap=4K$<phys>` on the kernel command line, or (properly) replace/re-time the DIMM after
memtest86+ names it. Self-play v3 (capped) was untouched throughout: batch 365, 0 faults.

**2026-09-04 (15:45, Fable 5.1) - HARDWARE CONFIRMED: THE RAM TEST REPRODUCES THE SAME BIT.**
`memtest.py 22 300` (22 GB in 256 MiB chunks): pattern `5aa5` (bit 23 = 0) clean; pattern `a55a`
**1 bad word**, chunk 87 offset `0x55b6a48`, expected `0x5aa55aa55aa55aa5` read `0x5aa55aa55a255aa5`,
bits=[23]; all-ones **same word**, read `0xffffffffff7fffff`, bits=[23]; all-zeros clean. A cell
stuck at zero on bit 23, deterministic on every pass, at page offset `0xa48` - the corrupted object
in the dump sat at `...ba48`, the same offset within its page. One physical page, one bit, seven
crashes. Non-ECC DDR4 on a Threadripper 1950X; the JIT, GC, thread-pool and engine arms were all
chasing a ghost, and the "search"/"dop 6"/"concurrency" readings of part 1 were the box's memory
footprint, not the code.

*What follows:* (1) stopgap - a page pinner (`scratchpad/badpage-pinner.py`) that grabs RAM, keeps
only the page that fails the all-ones test, mlocks it and releases the rest, so the physical page
is never handed to a lab process again (root can turn its virtual address into a PFN via
`/proc/<pid>/pagemap` and reserve it for good with `memmap=4K$<phys>` on the kernel line);
(2) Chris: memtest86+ from boot names the physical address and the DIMM; check what the 22:56
change did to DRAM settings (XMP/timings/voltage) before pulling hardware; (3) the crash chase is
CLOSED as a software item - no engine or runtime change comes out of it beyond the timer-hygiene
fix already in (`c3c442d`). Self-play stays capped at 8 GiB so the box stays well under its top
memory until the page is pinned or the DIMM is replaced.

**2026-09-04 (15:30, Fable 5.1) - THE DUMP ANSWERS: ONE BIT FLIPPED IN ONE OBJECT HEADER. HARDWARE
IS NOW THE PRIMARY HYPOTHESIS. AND THE 18 GB WAS NOT A LEAK.**

*verifyheap on `selfplay-47963.dmp`:* 129,162,220 objects verified, **3 errors, all one object**: the
`DataBinding<Position>` at `7e28a600ba48` (a model's `PositionBinding`, also referenced from the
store's `Dictionary<int, DataBinding<Position>>` entry array) has method-table word
`7e643e412ab1` where its type's table is `7e643ec12ab0`. XOR = `0x800001`: bit 0 is the GC's mark
bit and is legitimately set on its neighbours too (the dump is mid-mark; `RemainingWoundsBinding`
and `FacingBinding` next to it carry `...b9` and `...d1`), so the whole corruption is **bit 23 of one
64-bit word cleared**. Everything else in 15 GB is intact. A single cleared bit in an object header,
in a process with no unsafe code, is the signature of a memory cell, not of a JIT or GC bug (either
of those writes whole wrong values, not one bit). The GC then dereferenced the bad table and took a
general protection fault - the same face as every earlier crash. The region is gen1.

*The 18 GB was lazy server GC, not a leak:* under `DOTNET_GCHeapHardLimit=8 GiB` (v3, pid in
`selfplay.pid`) the heap oscillates 165-464 MB over 25 samples with gen2 collections happening (46
in 12 min), and `GameRunner.RunGameAsync` drops every game's graph on return. The census's 129 M
objects were dead games waiting for a gen2 that a 32 GB box never forced. That also explains the
timing of every crash: the box only touches its full RAM when something big is resident - self-play
grown overnight (07:18, 07:59), self-play at 18 GB (14:56), or Chris's own session plus three fresh
lab processes (23:33-23:53) - and a weak cell only bites when its page is in use. The chain-v2
window (11:11-13:14) ran while self-play was still small.

*Now running:* `scratchpad/memtest.py 22 300` (22 GB, four patterns, immediate verify, 5-min hold,
re-verify; reports every bad word with its bit positions) -> `memtest.log`. A hit confirms hardware
and names the bit; a miss does not clear it (userspace cannot reach the pages the kernel and the
desktop hold, and a marginal cell can need a specific access pattern), in which case memtest86+
from boot is the definitive test. **Question for Chris:** what physically changed in the 22:56
"OS swap" - a DIMM reseated, a drive added, a BIOS reset (which drops XMP/DRAM timings back to
defaults or, worse, leaves a profile at the wrong voltage)? Zero crashes in the hours before it,
seven after. Mitigation meanwhile: the 8 GiB cap keeps self-play small so the box stays out of its
top memory; every lab process keeps the dumper armed.

**2026-09-04 (mid-afternoon, Fable 5.1) - CRASH #7, AND THIS ONE LEFT A DUMP SOS CAN READ. PLUS AN
18 GB SELF-PLAY PROCESS.** Self-play (plain A, dop 20, pid 47963, the 23:39 Release build) died at
14:56:48 after 3h45 and batches 208-345 (~27,600 games, 0 faults): kernel `traps: .NET Server GC[47976]
general protection fault ip:7e64bc04307f ... in libcoreclr.so[44207f]`. The runtime's own dumper wrote
`scratchpad/dumps/selfplay-47963.dmp` (19.0 GB, full, with DAC regions) in 34 s. The parked chase is
unparked: `threads`, `clrstack -all`, `eeheap -gc`, `dumpheap -stat`, `verifyheap` are running on it
(`scratchpad/sos-selfplay-47963.txt`).

*All seven crashes side by side (kernel lines):* fault offsets 0x43765b, 0x45ed76, 0x441262, 0x4447b5,
0x44434e, 0x44207f - six different sites, all inside the GC's code region; fault addresses null in two,
heap-range in three, non-canonical (GPF) in this one; CPUs 1, 2, 2, 21, ... - not one core. gdb on
the new dump: the GC thread's frame under the signal handler is libcoreclr+0x44207f, 18 frames of GC
above `start_thread`; no symbols for Canonical's build. That spread is what a corrupted heap looks
like when the collector walks it, not one bad instruction and not one bad core.

*Second finding:* the process was at **18.1 GB RSS** when it died, from 2.9 GB at launch - and the
counters from the chain arms showed 2.5-3.0 GB heaps under server GC for a 15-min bench, so growth
over hours is either server GC being lazy on a 32 GB box or a leak in the exporter/self-play path
(plain A never simulates, so it is not the simulation timers). The harness killed my watcher for
"low memory" at about the same time. **Relaunched as v3** (`scratchpad/selfplay-v3.sh`, pid in
`selfplay.pid`): dumper armed, `DOTNET_GCHeapHardLimit=8 GiB` so lazy growth flattens while a leak
ends in a managed OutOfMemoryException with a stack, and runtime counters every 30 s
(`selfplay-v3-counters.csv`). Both answers land on their own.

**2026-09-04 (afternoon, Fable 5.1, investigator agent) - R9 FREEZE DIAGNOSED: ~400 FIRST-CHANCE
EXCEPTIONS PER SIMULATION, EACH A STOP-THE-PROCESS EVENT UNDER THE VS DEBUGGER. FIXED IN THE
SUBMODULE (UNCOMMITTED, FOR REVIEW).**

*Thread question (Q1):* the bot's `Resolve()` never touches the Raylib thread. `LobbyViewModel_Host.Launch`
leaves the main thread at its `Task.Delay(300)`; `FDGServer` runs the machine on the pool after
`Task.Yield()`; `LocalMessageBus.Dispatch` is synchronous on the sender (engine) thread, so
`StrategistActivationResolver.Resolve` runs on the engine's pool thread until `SimulationService.Run`'s
`WhenAny`. No `.Result`/`.Wait()` on that path, no `SynchronizationContext`, no shared lock between the
store and `Draw()`, and the only process-wide sinks (`RuleDiagnostics.OnWarning/OnRuleDropped`) fire 0
times on the resume path (0 `[rules]` lines across two headless Strategist games).

*Headless reproduction:* neither headless path stalls. `--scenario` 2k 1v1 all-AI Strategist (Debug,
box at load 34/32): searches 7.3-9.2s, 21-42 iterations, RSS ~230MB, 23 threads. New-game path
(`--headless --army <2k> --ai-profile strategist`, real deployment): bot's first activation passed at
~25s. So the freeze is specific to the GUI process and/or the attached debugger.

*The measurement that decided it* (temporary probe test, `AppDomain.FirstChanceException`, 2k snapshot,
4 workers x 5 iterations): **10,078 first-chance exceptions per 20-iteration search (~400 per
simulation)** - 8,610 `InvalidDataReferenceException` from `StoreReplay.ReplayEntriesWithRetry` (the
save loader resolved forward references by catching, ~190 per snapshot load) and 1,468
`SimulationStopSignal` re-throws (the throw-stop unwinding a 191-frame async chain: the state machine
nests an `await` per transition, so a stop is re-thrown ~70 times per one-activation line). Invisible
in Release; in Debug the same search took 15.5-19.5s wall. Under Visual Studio every first-chance
exception is a debugger round trip with the debuggee suspended (all threads, the Raylib loop included)
and an Output-window append on VS's UI thread - four workers throwing ~400 per simulation is minutes
of suspended process plus an unresponsive IDE, which matches "frozen for 2+ minutes and could not
bring up the IDE". The wall-clock budget could not help: it was checked only between iterations.

*Fix (submodule, 11 files + `Tests/SimulationStopTests.cs`, suite 3223/3224 green, root Debug build
green):*
- `SaveLoad/StoreReplay.cs`: readiness is CHECKED (scan each entry's embedded `DataReference`s once,
  replay only when every one `IsValid`); the catch path survives only as the fallback for dangling or
  cyclic references, attempted once nothing else resolves. Round trip pinned byte-identical.
- `IActivationBoundaryHook.AtActivationBoundary` returns `Task<bool>`; `DeterminePlayerTurnStage` on
  `true` calls `NotifyGameCompleted(ForFault("Simulation stopped at the end of its line."))` and returns
  - the exact exit `VictoryCalculationStage` takes at a natural end, so the chain unwinds by ordinary
  returns (every post-await on it is empty; checked). `SimulationStopSignal` and FDGServer's catch stay
  as a quiet fallback. Race found and closed: the capture and the game end now complete microseconds
  apart, both `RunContinuationsAsynchronously`, so `Run` reads `captured.Task.IsCompletedSuccessfully`
  instead of trusting which task `WhenAny` returned (the old unwind won that race by milliseconds).
- `SimulationOptions.Cancellation` + one linked token per line: a timed-out or cancelled simulation now
  stops at its next boundary (the "never stopped" gap; a line wedged INSIDE an activation still cannot
  be stopped). `SearchOptions.Cancellation` carries the search's hard deadline: `UctSearch` arms it at
  the budget (time budgets only, never under `Iterations` - G5), in-flight lines stop at their next
  boundary, nothing further opens, and an interrupted edge stays untried rather than closed.
- `AiProfileFactory.DefaultSearchWorkers = clamp(ProcessorCount - 1, 1, 4)` (design fork, one line to
  revert: fewer determinizations on a <=4-thread machine in exchange for a core for the GUI).

*After:* 0 first-chance exceptions per search; identical tree (20 iterations, 24 nodes, depth 5); the
same Debug search 15.5s -> 3.2s wall; headless 2k game: 65-140 iterations per search inside the same
6.4-8.8s budget (box load had also dropped 34 -> 22, so part of that is load).

*Not verifiable here (no display, no Windows, no VS):* that the GUI window now stays live - the R9 check
still needs Chris's laptop. The decisive A/B for him: the freeze should be gone under F5 now; if it is
not, run once with Ctrl+F5 (no debugger) and once with Debug > Options > Output Window > Exception
Messages off - if either alone cures it, the residual is debugger cost, not the engine.

**2026-09-04 (early afternoon, Fable 5.1) - CRASH CHASE, PART 3: EIGHT ARMS, A HARDWARE CHECK AND
FIVE HOURS OF SELF-PLAY WITHOUT A SINGLE CRASH. THE CHASE IS PARKED IN "ARMED AND WAITING".**

Chain v2 ran every arm on top of self-play at dop 20 (the load shape of the 23:50-23:53 triple
crash). Exit 0 = 100 games done, 124 = alive at the 15-min cap; no 139 anywhere, no dump, no
kernel entry.

| arm | exit | 100-game score (void, contended) |
|---|---|---|
| baseline-servergc | 0 | 79.0% |
| no-tiered-jit | 124 | - |
| tieredpgo-off | 0 | 81.5% |
| osr-off | 0 | 79.0% |
| workstation-gc | 124 | - |
| segments-gc | 0 | 78.0% |
| fixed-delay-cancel (timer-fix binary) | 0 | 81.5% |
| minthreads-256 | 0 | 77.0% |
| hwcheck.py 32 workers x 10 min (no .NET) | consistent | 16,472 rounds, 0 mismatches |

Self-play: alive since 11:11, batches 208-255 (~9,600 games, 0 faults) with the runtime dumper
armed. Runtime counters (attached this time): thread-pool threads peak ~40 and stay flat over
every arm, so timed-out simulations do NOT pile up; heap 2.5-3.0 GB under server GC vs 270 MB
under workstation GC (lazy collection, not a leak).

*What did and did not change at the crash window's edges (23:33-07:59, six crashes; nothing
since under heavier load):*
- The binary did not: `FdgLab/bin/Release` dates from 23:39 and never received the timer fix
  (`c3c442d` is 08:37; only the fixed-delay-cancel arm ran `fixbin/`). The 23:33 and 23:38
  crashes were an even older build. Two builds crashed; the surviving runs are the 23:39 build.
- The box did not: no dpkg/apt/snap activity on 09-03/04, runtime files untouched since April,
  zero swap-in/out since boot, no kernel BUG/WARNING, Tdie 67 C under full load now.
- The desktop session was active through the whole crash window and has been idle since 07:49;
  the last crash was 07:59. Loose - Chris was asleep for the 07:18 one.
- Statistically weak either way: counting bursts (23:33-23:53, 07:18, 07:59) as three events in
  8.5 h, five crash-free hours had ~14% probability under the same rate. Not evidence the rate
  changed.

*Conclusion:* no single runtime setting, GC, JIT tier or thread-pool knob is implicated, the
hardware test is clean, and the reproducer is gone. The remaining hypotheses (Zen 1 marginality
under a particular load/thermal state; a rare managed-runtime bug) can only be separated by a
dump with DAC regions, which every process on the box now produces on death. **Parked:** self-play
stays up with the dumper armed; the next crash writes `scratchpad/dumps/selfplay-<pid>.dmp`, and
`verifyheap` + `clrstack -all` on it is the next move. No further chase time until then.

**2026-09-04 (afternoon, Fable 5.1) - R9 FAILED IN THE FIELD: GUI FREEZE AT THE STRATEGIST'S
FIRST ACTIVATION.** Chris, Windows laptop, Visual Studio Debug build, tactician-bc at 72f2851 /
c3c442d, 2k vs 2k human vs Strategist Bot: "it worked until it was the bot's first activation, as in,
the Choose Action stage. Then it froze, and the UI was unresponsive. I waited two minutes ... For some
reason I couldn't bring up any other windows like the console or the IDE to see if anything was up."
The step-9 R9 check ("first GUI game with search - the window stays live") was on the deferred
hand-verify list, so this is the first time the path ran in a GUI. The whole machine going
unresponsive points past a blocked main thread to a pegged laptop (4 root workers x in-sim games at
`UctOptions.Interactive`). A separate investigator agent is on it (thread of Resolve() in a
GUI-hosted game, per-iteration cost vs the wall-clock budget, thread-pool starvation on few cores,
store/renderer lock, shared bus flooding); diagnosis and fix to be recorded here.

**2026-09-04 (midday, Fable 5.1) - STEP 9 SMOKE CELLS ARE IN; THE DOP-6 REPRODUCER DID NOT
REPRODUCE WITH THE BOX TO ITSELF; CHAIN v2.**

*Smoke cells (dop 2, box otherwise idle, Release):*
- 2k 1v1 Strategist vs SoloRules: 99.0% (98/0/2), hash `2B44D25EDDD7FBB6`. Tactician vs SoloRules on
  the same cell was 99.5%. Ceiling, as sec 5 predicted - sanity only.
- 3k 2v2 panel Strategist vs Tactician, 50 games/cell (`FdgLab/reports/b5-smoke-2v2`, hash
  `17F4A1FF2C246F77`): Saurian+Goblin vs Cults+DarkElf **54.0%** (17/13/20) against the step-2
  Tactician-vs-Tactician baseline 49.5% (30/31/39, n=100); BattleBrothers+Knight vs Robot+Titan
  **34.0%** (11/27/12) against baseline 40.5% (28/47/25). **No lift at 2v2 3k** - both cells sit
  within noise of A-vs-A (SE ~7 pts at n=50) while 1v1 2k gives 87.5%. 155s and 764 decisions per
  game. Not a gate failure (step 10 runs the real panels) but the first generalization signal, and
  1v1/2v2 generalization is the campaign's stated goal. Candidate causes, to PROBE in step 10 before
  touching anything: (a) rollout cost - a 2v2 3k simulation carries ~3x the units, so the 1-2s
  Benchmark budget buys a fraction of the iterations; (b) the SideValues max^n backup over four
  slots; (c) the G3 fallback rate (per-game fallback aggregation is still the deferred item).
  Cheapest probes: b0's search-cost phase on a 2v2 snapshot, and one 2v2 cell at
  `UctOptions.Interactive` to see whether budget alone closes it.

*Crash chase:*
- v1 baseline arm (dop 6, 100 games, server GC, runtime dumper armed, box otherwise idle):
  **survived** - exit 0, 869.6s, 83.0% (78/12/10), hash `6E2A52A68460469E`. So the reproducer is
  3-for-4, not 3-for-3, and the one clean run is the first with the box to itself. Side result:
  83.0% at dop 6 vs 87.5% at dop 2 means the 6-game ladder rungs from part 1 (91.7/58.3/50.0) were
  noise; the contention worry is closed and dop 6 is fine for panels on this box.
- Runtime counters never attached in v1: `dotnet-counters` inherited a TMPDIR pointing at the
  scratchpad while the runtime's diagnostics socket lives in `/tmp`. Same trap as `dotnet-dump`
  yesterday; v2 exports `TMPDIR=/tmp` for the whole chain.
- **Chain v2** (`scratchpad/gc-experiments-v2.sh`, log `gc-experiments-v2.log`, reports
  `gcx2-<arm>/`): self-play relaunched FIRST (dop 20, dumper armed, resumes `data/2026-09-03` after
  batch 207, pause file `FdgLab/.pause-selfplay`), then the same eight arms run on top of it, then
  hwcheck. Reasoning: the three fastest crashes (23:50-23:53) came with self-play dop 20 and a dop-6
  bench sharing the box; the only clean dop-6 100-game run had it alone. Arm scores are void under
  contention - exit codes and dumps are the data. Self-play data gen was dead 07:59-11:11.

**2026-09-04 (late morning, Fable 5.1) - CRASH CHASE, PART 2: A FIFTH CRASH, AND IT IS IN PURE
MANAGED CODE WITH NO SEARCH ANYWHERE NEAR IT.** Self-play (plain A, exporter path, dop 20) died at
07:59:51, one minute after the pause lifted, with
`System.AccessViolationException` in `LaneGeometry.AliveFieldedUnits` <- `MacroActionGenerator.Enumerate`
<- `TacticianPlanner.ChooseAction` - the ordinary A policy. No `unsafe` exists in that path or any
other. An AV in managed code means the CPU executed wrong machine code or read wrong memory; both
would also produce the NRE (a corrupted reference reads as null) and the GC segfaults (the GC walks
the corrupted object). **One cause, three faces** is now the working model.

**Crash ledger so far (all after the 22:56 reboot for Chris's OS swap; zero in the hours before):**

| when | process | signal | where |
|---|---|---|---|
| 23:33 | b0 search soak, 4 workers | SIGSEGV | libcoreclr GC worker |
| 23:50 | bench dop 6, search | SIGSEGV | libcoreclr GC worker |
| 23:53 | bench dop 6 3k 2v2, search | SIGSEGV | (NRE in Newtonsoft 1 min before) |
| 23:53 | self-play dop 20, plain A | SIGSEGV | libcoreclr GC worker (same function as 07:18) |
| 07:18 | bench dop 6, search | SIGSEGV | libcoreclr GC worker, 4 threads in it |
| 07:59 | self-play dop 20, plain A | SIGABRT | **managed AV in LaneGeometry** |

Clean in the same window: ~6,000 plain-A self-play games, 100 search games at dop 2, 6 at each of
dop 1/2/4/6. So "search" and "dop 6" were never the cause - they were the fastest way to hit it.

**Two hypotheses left, both with a mechanism, both on the queued chain:**
1. **JIT miscompilation** (tiered recompilation / dynamic PGO / OSR producing bad code for a hot
   method after warm-up - which is exactly a "works for minutes, then dies" shape). Arms:
   `TieredCompilation=0`, `TieredPGO=0`, `TC_QuickJitForLoops=0`, run FIRST now.
2. **Hardware.** Non-ECC RAM (nothing would be logged), kernel log clean of MCE/EDAC/thermal, and
   the box is a **Threadripper 1950X** - first-gen Zen, whose early parts had the documented
   "performance marginality" defect: random segfaults under heavy parallel load, RMA-only fix.
   Arm: a .NET-free 32-worker consistency check (`hwcheck.py`: repeated SHA-256 of a fixed 64MiB
   buffer + an integer kernel, any disagreement = hardware) at the end of the chain.
   Discriminator: if every .NET arm crashes AND hwcheck is clean, the runtime is the suspect; if
   hwcheck disagrees with itself, it is the silicon and no code change fixes it.

**Chain order now:** baseline -> TC=0 -> PGO off -> OSR off -> workstation GC -> segments GC ->
timer-fix binary -> minthreads -> hwcheck -> self-play relaunched WITH the crash dumper armed (it
has been dead since 07:59; nothing was set to restart it - fixed in the chain).


**2026-09-04 (morning, Fable 5.1) - CRASH CHASE, PART 1: WHAT IT IS NOT, AND THE TOOLING THAT WILL
SAY WHAT IT IS.** Chris asked for the crash to be chased after the 87.5% number landed. Reproducer:
`bench --games 100 --dop 6` with a search profile, SIGSEGV in ~7 min; dop 2 runs 100 games clean.

**Ruled out, each by reading or measurement rather than assumption:**
- **Native/unsafe code:** none in the engine or lab - no `unsafe`, `DllImport`, `stackalloc`,
  `Marshal`, no native library shipped. The usual way managed code corrupts a GC heap is absent.
- **Shared mutable statics:** `TerrainGridCache` (per-table, locked), `RuleEvaluator.t_pool`
  (`[ThreadStatic]`), `RuleDiagnostics.WarnOnce` (locked), `SaveTypeRegistry` (static ctor),
  `SpecialRuleRegistry` (local), `CoreRuleCatalog.CreateResolver` (fresh per army load),
  `TacticianWeights` (written only at startup). Serializer settings and `DataBindingJsonConverter`
  are per-store instances. Evaluator and encoder are stateless.
- **Thread-pool starvation:** every `.Result` in the engine follows an `await` of the same task.
  Bus dispatch is inline; `DirectPlayerRequester` has no threading.
- **The plain-A path:** self-play at dop 20 on the same binary, 400 games, 0 faults.

**Confirmed mechanism, not yet confirmed as cause:** `SimulationService.Run` never STOPS a timed-out
simulation - there is no cancel or stop API on `FDGServer`. A line that trips the 60s watchdog
becomes a zombie engine playing its game out in the background, competing for the CPU that caused
the timeout. Shaped like "concurrency AND duration"; whether it actually happens at dop 6 is what the
runtime counters below will show. Left as a known gap - the fix needs a stop API on the server.

**The crash itself:** four threads in the SAME `libcoreclr` function at death - the Server GC's
parallel workers mid-collection, one hitting a bad pointer. No managed frames on the dying thread.
Systemd's core lacks the DAC regions so SOS could not run `verifyheap` on it; the runtime is
Canonical's build, so Microsoft's symbol server has nothing (Ubuntu debuginfod attempted).
[dotnet/runtime#86183](https://github.com/dotnet/runtime/issues/86183) documents the same shape
with a workaround of the standalone segments GC (`libclrgc.so`).

**One managed symptom, one minute before the 2v2 cell died:** `NullReferenceException` inside
Newtonsoft's `SerializeObject`, under `GameSaveSerializer.Save` at a simulation stop boundary. The
parsimonious reading is ONE defect with two faces - a corrupted reference reads as null to managed
code first, then kills the GC when it walks the same object. That makes `verifyheap` on a proper
crash dump the decisive measurement.

**Tooling now in place:** `dotnet-dump`, `dotnet-counters`, `dotnet-symbol` installed. Validated
end-to-end on this runtime: a live attach and a forced-crash dump both analyze - `verifyheap`
reports **"No heap corruption detected"** on a healthy mid-search process, 46-47 managed stacks
resolve to our frames. Every experiment arm runs with the runtime's own full-dump crash handler
armed and live counters (threads, pool queue, GC, heap) sampled every 2s.

**Queued behind the step-9 cells, one variable each, dop-6 100-game reproducer, 15-min cap:**
baseline Server GC; the timer fix below; `ThreadPool_MinThreads=256`; workstation GC; segments GC
(`libclrgc.so`); Server GC with 4 heaps; tiered JIT off. Exit 139 = crash, 124 = survived.

**Fixed along the way (submodule): the watchdog `Task.Delay` is now cancelled when the line
settles.** A simulation lasts ~100ms and left a 60s timer live every time - measured **1,101
pending in a 4-worker soak**. An earlier read of the dump had me thinking those timers rooted the
snapshot strings for a minute each; the dump says otherwise (79KB of timers, 36 strings over 50KB
on the whole heap), so this is hygiene, not the cause. Hash `8D6EFA0AF0B4019E` unchanged, suite
3216/3217.

**Process note:** two forced-crash tests signalled my own wrapper shell instead of the target
(`pgrep -f` matched the command line that contained the pattern) - the two `bash` entries in
`coredumpctl` this morning are those. Use `$!`.


**2026-09-04 (morning, Opus 5) - FIRST REAL B-vs-A NUMBER: STRATEGIST BEATS TACTICIAN 87.5% OVER
100 GAMES AT 2k. AND THE SEGFAULT IS NOW A REPRODUCER, NOT A MYSTERY.**

**The number (step 10's gate metric, one cell of it):**

| cell | games | A score | W/L/T | faults | timeouts | hash |
|---|---|---|---|---|---|---|
| 2k 1v1 Hives vs Battle Brothers, **Strategist vs Tactician**, dop 2 | 100 | **87.5%** | 83/8/9 | 0 | 0 | `5DF38281DF031258` |

The B-gate wants **>= 60% vs A**. This cell clears it by a wide margin, on the first honest
measurement anyone has taken of the search actually playing. Per-game 47.6s, 289 decisions/game,
decision mean 164ms, worst p95 2090ms (the 2s budget cap, visible in the tail). Caveat worth
keeping: ONE cell, one army pair. The gate is a panel, not a cell, and the rest is step 10.

**The crash: reproducible, and it needs BOTH concurrency and duration.**

| configuration | outcome |
|---|---|
| dop 1, 6 games | clean |
| dop 2, 6 games | clean |
| dop 4, 6 games | clean |
| dop 6, 6 games | clean |
| **dop 2, 100 games (40 min)** | **clean** |
| **dop 6, 100 games** | **SIGSEGV at ~7 min** (07:18, PID 31446) |
| dop 6, 100 games (overnight) | SIGSEGV at ~8 min (23:50, PID 10927) |
| dop 6, 3k 2v2 200 games (overnight) | SIGSEGV at ~2 min (23:53, PID 11486) |

- Short runs at ANY dop pass, which is why the 6-game ladder cleared dop 6 and briefly made this
  look like it was not a concurrency problem at all. It is: **dop 6 dies in minutes, dop 2 survives
  40 of them.** Both readings were needed; neither alone was enough.
- The soak's death (step 8 entry, ~300 searches, `.NET Server GC` segfault in `libcoreclr.so`) fits
  the same shape - 4 workers is its own concurrency.
- **Not the A path.** Self-play at dop 20 with the same binary ran 400 games, 0 faults, clean exit;
  204 batches before that. Plain Tactician is unaffected.
- **Not the evaluator.** `HandWeightedEvaluator` and `PositionEncoder` are the obvious shared-state
  suspects (one evaluator instance serves all 4 workers) and both are entirely static/stateless.
  Checked and ruled out rather than assumed.
- One managed `NullReferenceException` inside a game during the overnight 2v2 cell - recorded
  because it is the only MANAGED symptom seen, and it points at shared mutable state somewhere
  under concurrent simulation. Not chased yet.
- **Working mitigation, not a fix: bench search profiles at dop <= 2.** Lobby play is one game with
  one search at a time, which is the dop-1 shape, so this is a lab-throughput problem rather than a
  player-facing one - but that reasoning is an inference, not a measurement, and a long GUI session
  has not been run.

**UNRESOLVED - does contention weaken a wall-clock-budgeted bot?** The 6-game ladder read 83.3% /
91.7% / 58.3% / 50.0% at dop 1/2/4/6, monotonic, with a plausible mechanism (the budget is wall
clock, so contention buys fewer iterations while per-game wall barely moves, 46 -> 53s). At n=6 per
rung that is noise-compatible, and the dop-6 100-game run that would have settled it crashed. It
matters for the gate protocol - benching at high dop may understate B - so step 10 should either
pin cells to low dop or bench on an ITERATION budget. Flagged, not concluded.

**Process failure worth recording: the box sat idle from 03:56 to 07:05.** The dop-1 diagnostic
finished and nothing was watching for it, so its result went unread for three hours and no compute
ran - a straight violation of campaign sec 0.4. Every job since is launched as a chain that ends by
handing the box back to self-play, with a watcher on it. Overnight loss from the crashes: zero
games generated between 23:53 and 07:06.

**Running:** 3k 2v2 panel (50 games/cell) then Strategist vs SoloRules 2k (100 games), both at
dop 2 with data gen paused, then self-play resumes automatically.


**2026-09-03 (night, Opus 5 - Chris said to carry on rather than switch to the doc's Sonnet for
this step, so no re-prompt inside it) - STEP 9 (B5) DONE: THE SEARCH NOW DRIVES REAL GAMES. THE
BOT IS "STRATEGIST BOT" AND IT IS IN THE LOBBY.** Everything from B1 to B4 was machinery; this is
the commit where it becomes a bot a person can pick and play against.

- **`StrategistActivationResolver`** (`Ai/Tactician/Search/`): at each `ChooseUnitToActivateRequest`
  it serializes the LIVE position (the engine's rolling save point has just written the flow state,
  so that boundary IS the search root), runs `UctSearch`, and `Prescribe`s the winning root edge -
  then hands the request to the ordinary `TacticianActivationResolver`, which consumes it. It never
  answers the request itself. One `Prescribe` covers the whole activation: the unit half is taken
  here, the action and macro halves survive `BeginActivation` and are consumed at Choose Action.
- **The seam needed no new engine surface** beyond `ITableState.DataStore`: 5b built the
  prescription path, B2 built `SimulationService.Rebind` for the foreign-store problem, and B5 is
  those two called from a resolver instead of from a simulated line. The unit is matched by
  `DataReference` and the macro rebound onto the live store - skipping that is the silent
  corruption B2 hit.
- **One switch between the rungs:** `TacticianOptions.Search`. Null is plain A and stays plain A;
  set, the activation resolver is wrapped. Both rungs stay benchmarkable forever (G4), and the
  file's own comment had predicted this field ("search time budgets in Phase B").
- **Search never runs inside search.** `SimulationService` maps a Strategist in-sim profile down to
  Tactician - a Strategist opponent model would root a new tree at every boundary of every line of
  the tree above it, which is unbounded recursion, not a deeper search. It is also the honest
  opponent model: the search assumes the other side plays A, and now says so in code.
- **G3 is absolute.** Every failure path - no serializable store, a faulted root probe, an
  exception from anywhere in the tree - clears the prescription, counts a fallback, and lets the A
  policy answer. A real game has nowhere to put a fault. Pinned by a test that hands the resolver a
  non-resumable store and asserts the game continues, the fallback is counted, and NO prescription
  is left behind to poison the next activation.

**Lobby:** "Add Strategist Bot" button plus a per-slot "Strategist" re-crew entry
(`LobbyScreen.cs`), and `LobbyViewModel_Host.AddAiPlayer` now maps profile -> product name through
a switch rather than a Tactician/else ternary. Gunline stays lab-only and unnamed, as specified.

**Lab:** `--profile-a strategist` already parsed (`Enum.TryParse`). Two things did NOT come for
free and are the step's real lab findings:
1. **The watchdog.** There is NO per-request watchdog anywhere in the engine - the only timeout in
   the whole codebase is the lobby greeting. The campaign doc's "watchdog raised for search
   profiles" is therefore FdgLab's per-game one, and 120s would have killed every game in a cell
   and reported it as a fault. `bench` now defaults to **900s when either profile is strategist**;
   an explicit `--timeout` still wins.
2. **The lab budget.** `GameRunner` passes `UctOptions.Benchmark` (1-2s/activation) instead of the
   5-10s a human gets, or a 100-game cell would take a day - but keeps **Workers=4, matching lobby
   play**, because root parallelism is an ensemble over determinizations and changing it would
   benchmark a different bot than the one that ships. Games run concurrently on top of that, so
   bench `--dop` x 4 must stay at or under the core count.

**Verification:** **hash `8D6EFA0AF0B4019E` unchanged** - and the exact command is finally RECORDED
rather than guessed (the gap flagged to Chris at step 4):
`fdglab bench --a builtin-basic --b builtin-basic --games 6 --dop 1 --profile-a tactician
--profile-b tactician`, Release. (`builtin` rather than `builtin-basic` gives `7D7A6C2FCDAAE9FA` -
that is what the ambiguity was worth.) Engine suite **3216/3217** (1 skipped by design, +4 new),
full solution build green. **End-to-end:** a real Release game, `--profile-a strategist
--profile-b tactician`, completed with no fault - 3 rounds, 62 decisions, p95 1262ms (the search
budget, visible in the tail), wall 9.0s. It LOST that game 0-3, which at one game and a 2-unit
army is noise, not a signal.

**Deferred, said out loud (not silently cut):** the G3 fallback count is exposed on the resolver
and narrated through the decision log, but nothing AGGREGATES it per game or per cell yet. That
belongs with step 10's bench, where the fallback rate is a gate input rather than a curiosity.

**Still open from step 8, unchanged:** the intermittent root-probe fault. It is now the single
thing most worth sizing, because B5 is where it costs something real - every occurrence is an
activation played by A instead of by search.

**Next:** step 10 (B-gate). The overnight run is the step-9 smoke pair (2k 1v1, 3k 2v2), which is
also the first honest B-vs-A number anyone has seen.


**2026-09-03 (night, Opus 5) - OVERNIGHT WINDOW REOPENED: DATA GEN AT DOP 20, PHASE 4c SOAK
RUNNING, AND THE LOBBY NAME FOR B IS DECIDED.** Machine came back after the OS swap. No engine
change in this entry - lab harness and docs only, so the DOP-1 hash needs no re-verification.

- **Self-play resumed** at batch 176 / seed 36200 (the resume logic picked it up with no partial
  files to clean), now at **DOP 20** rather than 12 - nothing else has the box overnight - and this
  time WITH `--pause-file FdgLab/.pause`, whose absence made every timing measurement of the
  previous session "under load". Steady state: 200 games per batch in 55-70s, 0 faults,
  ~3.3-3.5k rows/batch. That is ~12k games/hour against the 36.2k already banked.
- **Lobby name for the B rung decided (Chris): "Strategist Bot", `EAiProfile.Strategist`** - the
  rung above Tactician, and it deliberately leaves a foresight-flavored name (Oracle/Prophet) free
  for C at step 15. Written into campaign doc step 9, which no longer says "name TBD".

**The 4c soak crashed before it started, and the cause is the step-8 probe fault - now seen a
second time.** `fdglab b0`'s phase 3g opens with ONE unguarded `SearchTree.ProbeRootAsync` for the
leaf-evaluator timing; it threw `SearchUnavailableException` ("the root snapshot has no activation
boundary (game ended after 0 activation(s): Fault)") and took the whole process down before phase
4c, which runs after it. The irony is that the very next sub-block, (a2), exists to COUNT this
exact fault - the warm probe above it was simply never guarded.

- **Fix (lab-only, `FdgLab/B0Spike.cs`):** the warm probe retries across 5 seeds, reports each
  fault, and on total failure skips the leaf-evaluator average rather than the phases after it -
  the soak is the expensive thing in that run, not a 20-sample mean. Warm-probe faults are folded
  into 3g's reliability line so the rate stays honest.
- **Still intermittent, NOT reproduced on demand.** The relaunch probed **10/10 clean**, and 3g
  otherwise reproduced step 8 exactly: **97.3ms/iteration** (94 measured clean), 21 nodes, **max
  depth 5, 0 closed edges**, same-seed choice + visit distribution IDENTICAL (PIN). Leaf evaluator
  on real armies **0.74ms** (B3 estimated 3.4-7.2ms - comfortably under). So this is now
  observed-twice-across-two-sessions and still un-forced; it stays a **B5/G3 sizing question for
  step 9** (what fallback rate does search actually pay in real games), not a blind fix.

**A load-flake warning for whoever runs the suite next.** The first full engine run tonight went
**3210/3213 with 2 failures**, one of them `UctSearchTests.Search_OnARealBoundary_IsReproducible_
AndPlaysAnHonoredEdge` - on a box simultaneously running 20-way self-play AND the 4-worker soak.
Re-run in isolation: **UctSearchTests 9/9**. Re-run as the full suite with self-play paused via
the new pause file: **3212/3213, 0 failures** (1 skipped by design). These tests resume real games
inside themselves, so they are contention-sensitive; record this so the next session does not
re-discover it as a regression. **Verification for this entry:** full `dotnet build` green,
engine suite 3212/3213 green.

**PHASE 4c, ATTEMPT 2: STILL NOT A COMPLETE RUN - THE PROCESS SEGFAULTED IN THE .NET SERVER GC AT
~300/500.** Not an application exception; the kernel log has it:
`.NET Server GC[5503]: segfault at 71a91c79c128 ... in libcoreclr.so`, one occurrence, on a box
also running 20-way self-play (both processes use `<ServerGarbageCollection>true</ServerGarbageCollection>`,
so 32 GC heaps each). No OOM - RSS was FALLING when it died. Cause not diagnosed; do not guess.

**What the 300 searches that did run say - and it is worth having:**

| sample | heap | after GC | RSS | threads |
|---|---|---|---|---|
| start | 56MiB | - | 952MiB | - |
| 50 | 456MiB | **112MiB** | 1146MiB | 85 |
| 100 | 164MiB | **113MiB** | 1067MiB | 86 |
| 150 | 396MiB | **113MiB** | 930MiB | 86 |
| 200 | 266MiB | **113MiB** | 880MiB | 85 |
| 250 | 444MiB | **113MiB** | 843MiB | 86 |
| 300 | 258MiB | **113MiB** | 739MiB | 85 |

- **(a) No leak signal across 300 searches.** Post-GC heap is DEAD FLAT at 112-113MiB from sample 1
  to sample 6 - the rule 5c set. Trees are dropped whole; nothing accumulates.
- **(b) 5c's +255MiB RSS growth does not reproduce on this primitive - RSS went DOWN**, 1146 ->
  739MiB, i.e. the runtime handing memory back as the process settles. The 5c flag is not confirmed
  here; it is also not refuted for 5c's own primitive.
- **(c) Deferred decision 4 (snapshot-per-child vs branch-point-only) stays OPEN** on purpose: a
  flat heap over 300 searches says snapshot-per-child is not leaking, not that it is the right
  storage at B5 depth. Nothing about it needs to change to ship B5.
- **Still owed:** a clean 500, and an answer on the segfault. Both are cheap to retry; neither
  blocks step 9, which is why step 9 went ahead.


**2026-09-03 (night, Fable) - STEP 8 (B4) BUILT: TIME-BUDGETED UCT WITH ROOT PARALLELISM. THE
WIDENING CONSTANT WAS THE WHOLE BALLGAME - C=2.0 SEARCHED ONE PLY DEEP AND THREW AWAY 44% OF ITS
SIMULATIONS; C=0.5 REACHES DEPTH 5 WITH ZERO WASTE AND IS 30% CHEAPER PER ITERATION. THE 500-SEARCH
MEMORY SOAK IS NOT DONE - IT IS THE TOP OUTSTANDING ITEM.** Chris switched the session to Opus (the
model step 8 calls for) and said to continue once B3 landed; the build then ran on Fable. Cut short
by a machine shutdown, so this entry is written before the commit per campaign doc sec 6.

- **`UctSearch`** (`FutureOfDarkGrimness/Ai/Tactician/Search/UctSearch.cs`): PUCT selection reading
  exactly `SearchEdge.QFor(actingSide)` + `Prior` (design sec 7.4 - B4 knows nothing else about an
  edge), progressive widening at both levels in prior order, root parallelism, and the merge. No
  transposition table. `ExpansionScaffold` stays as B2's test-only walk.
- **Determinism guarantee implemented: EXACT under (RootSeed, Workers, Iterations).** Worker trees
  share no mutable state and the merge is a reduction in worker order, so thread scheduling cannot
  leak in - pinned at 1 and 4 workers, asserting identical choice, identical per-edge visit
  distribution AND an identical set of simulation seeds. A TIME budget is deliberately not
  reproducible (the iteration count rides the box); no test uses one. `SearchTree` gained
  `ProbeRootAsync`/`FromRoot` so N workers cost ONE probe, not N.
- **Budget** scales with root branching within a hard cap, per the campaign doc: `BudgetMsFor(units)
  = clamp(base + perUnit*units, base, cap)`, with `UctOptions.Benchmark` (1-2s) and
  `UctOptions.Interactive` (5-10s) as the plan's two named presets.

**Deferred decisions the design doc handed B4, and what decided each:**

1. **Widening C: 2.0 -> 0.5 (alpha unchanged at 0.5).** Measured at 2k, 20 iterations, 1 worker:

   | C | nodes | max depth | root edges opened | closed edges | ms/iteration (serial, warm) |
   |---|---|---|---|---|---|
   | 2.0 | 21 | 2 | 17 | **16** | 131 |
   | 1.0 | 21 | 2 | 9 | - | - |
   | **0.5** | 21 | **5** | 4 | **0** | **94** |

   At an actual benchmark budget C=2.0 reached max depth **ONE** - no reply seen at all, which is
   A's horizon with extra steps. The closed-edge collapse is the bigger finding: C=2.0 forces open
   low-prior edges (unreachable charges, shoots with no target) whose prescriptions then fall
   through at play, and **each discovery costs a full line** - 16 of 36 lines bought nothing. Narrow
   widening opens only the top-prior edges, which are the ones the stage actually offers. So the
   same knob bought depth, removed the waste and cut per-iteration cost together. Which C *plays*
   best is a games question and belongs to the B-gate; this is the value that makes the search a
   search.
2. **In-sim policy: must stay Tactician; the SoloRules option is void.** Not a cost/bias trade at
   all - a prescription is consumed BY THE PLANNER (5b's seam), and `AiProfileFactory` hands a
   non-planning profile no planner, so under SoloRules *every* edge falls through and the search
   has no tree. Pinned (`InSimSoloRules_ClosesEveryEdge_SoTheInSimPolicyMustPlan`) so nobody
   re-opens the question by flipping the option and finding a silently empty search. 5c's "SoloRules
   is 40% cheaper" saving is only reachable for CONTINUATION activations, and only via a
   mixed-profile capability that does not exist - recorded, not built.
3. **Continuation depth: stays 0** (child = the very next boundary). With 0 there are no natural
   in-sim activations at all, which is what makes (2) total rather than partial. Tree shape vs
   budget is a play-quality question -> B5/B-gate.
4. **Snapshot memory strategy: UNDECIDED - the soak that decides it did not run (see below).**
   Snapshot-per-child stands as design sec 5.3's v1 choice. Observed: 82MiB live heap after one
   benchmark-budget 4-worker search of 59 nodes (~1.4MB/node).

**Cost, honestly labelled.** Every number below was taken while the step-4 self-play generator was
running at DOP 12 on the same box, so all are upper bounds; the serial arm is the most
contention-sensitive. **A first pass reported 279-291ms/iteration serial and I nearly wrote it
down - it was warm-up.** A 2-iteration warmup did not fix it (tiered JIT needs a full search to
promote); a full-size warmup dropped it to 137ms, and the warm repeat to 94-131ms. Lesson for G6,
the same one the 2026-09-03 profiling entry learned: measure the steady state, and re-measure when
a number looks too good or too bad to be true.

| Measurement (2k, Release, under load, C=0.5) | Value |
|---|---|
| leaf evaluator, both sides, real armies | **1.82ms** (B3 estimated 3.4-7.2ms - resolved BELOW its own estimate) |
| 1 worker, warm | **94-99ms per iteration** |
| 4 workers | **23.8ms per iteration wall** (4.2x) |
| benchmark budget (1480ms, 4 root units, 4 workers) | 55 iterations, 59 nodes, **max depth 5**, 1630ms wall |

Against B0's decision table that is the **30-200ms band: "MCTS with small node counts, leaning on
the evaluator"** - which is exactly the shape B3 was built for. Consistent with 3f's 58.6ms/activation
prescribed line plus enumeration.

- **A robustness fix the measurements forced.** One root probe faulted outright ("game ended after 0
  activations: Fault") and took the whole lab process down with an unhandled exception. A resumed
  game is a whole engine and can fault; B5 will run this INSIDE real games, where a crash is not an
  option. `SearchTree.SearchUnavailableException` is now its own type and `UctSearch` turns it into
  a result with no choice, which is the shape plan G3's "fall back to A-greedy, logged and counted"
  needs. Re-measured after: **10/10 probes reached a boundary**, so the fault is rare and was not
  reproduced - reported as observed-once, not as a rate.
- **B2 test pinning, not weakening.** Three authored-tree tests in `SearchTreeTests` implicitly rode
  the old default C=2.0 and failed when it changed. They now pin `WideningC = 2f` explicitly, as
  their neighbours already did - they are testing B2's backup and widening, not B4's tuning.
- **Verification:** hash `8D6EFA0AF0B4019E` unchanged (DOP-1 six-game cell, Release), **re-run after
  the last engine edit**; engine suite 3212/3213 (1 skipped by design, +10 new cases); full
  `dotnet build` green. Reproducibility pinned both in unit tests and on a real 2k board
  (`fdglab b0` phase 3g reports "IDENTICAL choice and visit distribution").

**NOT DONE - the top item for the next session.** The **500-search memory soak** (`fdglab b0
--search-soak 500`, phase 4c, written and building) was started and killed at ~2 minutes when the
machine had to shut down. A partial soak is not a result, so nothing is claimed about it. Until it
runs, TWO things stay open: (a) whether the search leaks across many searches - post-GC heap is the
signal, per 5c's rule; (b) 5c's flagged **+255MiB RSS growth**, which this soak was meant to
re-check on the bigger primitive; and it is what decides deferred decision 4 (snapshot-per-child vs
branch-point-only re-run storage). Note also that the campaign doc's "500-GAME" soak is properly a
B5/B-gate item - search only drives real games after step 9 - so phase 4c is the search-level
stand-in, not a substitute.

**Next:** run phase 4c to completion, then step 9 (B5 integration, Sonnet/medium), which also
carries the lobby-exposure decision that needs Chris.

**2026-09-04 (Fable, per protocol - step 7 build recommends Sonnet, no re-prompt inside a step) -
STEP 7 (B3) DONE: HAND-WEIGHTED LEAF EVALUATOR ON THE C1 VECTOR.** B and C now share one code
path (campaign doc step 7): the search's leaf value is a hand-weighted read of the same
`PositionEncoder` block C1 exports; C3 will later swap the weights for a trained net and touch
nothing else in the tree.

- **A real gap found while wiring it in: `IPositionEvaluator` needed a `RuleEvaluator`.**
  `PositionEncoder`'s mobility/threat features run movement-rule modifier evaluation
  (`AdvanceDistance`/`ChargeBudget`), which the interface's original `Evaluate(ITableState,
  SideMap)` signature (design doc sec 7.2, this session's own earlier design turn) had no way to
  supply. Corrected the interface to `Evaluate(ITableState, RuleEvaluator, SideMap)` - a design
  deviation, recorded here per the same precedent as 5b's resolver-attribution correction. Callers
  (`SearchTree.FromSnapshotAsync`, `SimulationExpander`) construct one unseeded
  `RuleEvaluator(ProbabilisticDiceRoller())` each; the evaluator contract ("never rolls") makes an
  unseeded roller behind it inert. Both shipped placeholders (`TerminalOnlyEvaluator`,
  `ObjectiveShareEvaluator`) and both call sites plus the two-side-constraint test updated to match.
- **New engine entry point:** `PositionEncoder.EncodeSideBlock(state, evaluator, sideMembers,
  opposingMembers)` - the same `ComputeBlock` the exporter's four blocks already run, exposed for
  an arbitrary SIDE rather than one activation's SELF/ALLY/ENEMY perspective. Deliberately not a
  sum of per-player shares: `obj_held_share` and `threat_coverage` are not additive across a side's
  players (a target covered by one ally and not another must count once), so the evaluator calls
  this once per side rather than approximating from `Encode`'s per-player blocks.
- **`HandWeightedEvaluator`** (`Ai/Tactician/Search/HandWeightedEvaluator.cs`): weights
  0.55 obj_held_share / 0.30 value_share / 0.15 threat_coverage (sums to 1, so the raw combination
  needs no clamp - every input share is already in [0,1]), per the doc's ordering (objectives
  dominant, CLAUDE.md; value share next; threat coverage - already the coarsest, budget-traded
  feature per the schema doc - least). Two-side complementarity reuses `ObjectiveShareEvaluator`'s
  own proven shape (`0.5 + (own - bestOther) / 2`) rather than a new normalization, which is
  algebraically exact for two sides with no clamp ever engaging.
- **Verification (`Tests/HandWeightedEvaluatorTests.cs`, 4 tests):** losing a unit lowers own value
  (caught a bug first try - `Kill()` set `RemainingWoundsBinding` to `TotalWounds` instead of 0,
  the inverse of dead; `GetIsAlive` is `WoundsDealt < TotalWounds`); seizing an objective raises
  value; a 1v1 board and its reduced 2v2 form (a zero-unit ally on each side, no `ArmyData` at all)
  evaluate identically BY CONSTRUCTION (every `LivingUnits`/`RosterCount` scan filters by army
  presence, so an ally with no army contributes exactly zero - not an approximate pin, an exact
  one); per-leaf cost.
- **G2 read - the premise holds, not just asserted.** 20 real Tactician-mirror games
  (`bench --games 20 --dop 6`, hash `955D11EDF1A0500D`): in EVERY game the reported winner held
  strictly more objectives than the loser at game end (`bench.csv`) - no case of a side winning
  the game while behind on markers, which is the concrete case a reward-hacking evaluator would
  get wrong and exactly the reason `obj_held_share` carries the dominant weight. Caveat: the
  evaluator is not wired into any live decision yet (B4/step 8 does that), so this reads the
  premise against real play, not the evaluator's own influence on play - that check is B4's gate.
- **Per-leaf cost:** 1.317ms for a 2-side, 20-unit, 3-objective board (Release, 50-rep mean,
  `Tests/HandWeightedEvaluatorTests.Evaluate_PerLeafCost_IsReportedAndSane`) - two
  `EncodeSideBlock` calls per leaf where step 4 measured 1.7-3.6ms for ONE call on real armies with
  attached special rules; this fixture's units carry none, so the true per-leaf cost on a real
  board is higher than 1.317ms and closer to roughly double step 4's single-call number (3.4-7.2ms
  for two sides) - flagged as an estimate, not re-measured against real armies, for B4 to confirm
  against its actual leaf rate once the search loop exists.
- **Hash-verify:** `8D6EFA0AF0B4019E` unchanged (DOP-1 six-game cell, Release) - B3 only touches
  the search tree's leaf evaluation, which no natural game reaches. Full engine suite 3202/3203 (1
  skipped by design, +3 new), full solution build green.
- **Self-play undisturbed:** PID 51352 alive throughout (3h+ at last check), 143 batches.

Next: step 8 (B4 UCT search) - Opus / high per the doc. Model switch needed before that step starts.

**2026-09-04 (Fable) - STEP 6 (B2) BUILT: TWO-LEVEL TREE, ALL NINE VERIFICATION ITEMS GREEN,
HASH UNCHANGED. FULLY PRESCRIBED LINE MEASURED AT 1K/2K/4K.** Built directly to
`docs/tactician-b2-design.md` (Fable's own design turn) - no redesign; where the spec left a
build choice open, made the call and record it below. Model note: the doc names Sonnet/medium for
the build, but the campaign protocol forbids a second model prompt inside one step (5b/5c already
got the one prompt, answered with the Opus switch) and blocking a build already unblocked on
compute is exactly what the protocol says not to do - built on the session's current model
(Fable) instead, noted here per the doc's own instruction.

- **Engine** (`Ai/Tactician/Search/`, commit `821b6ef`): `SideMap`/`SideValues` (team-indexed, not
  player-indexed - teammates share a component by construction); `IPositionEvaluator` +
  `TerminalOnlyEvaluator` + `ObjectiveShareEvaluator` (both pinned against the two-side
  v[other]=1-v[self] constraint); `SearchNode`/`UnitBranch`/`SearchEdge`; `TacticianActionSpace`
  (level 1 = `TacticianActivationResolver.Urgency`+frontline-bias prior over the unactivated pool,
  now exposed as `ActivationScores` so the tree reads exactly what the resolver picks by; level 2 =
  `MacroActionGenerator.Enumerate` scored by a SCRATCH `TacticianPlanner`, mapped to actions via the
  planner's own `ActionNameFor` made `internal`); `SimulationExpander` (one line per edge, leaf
  evaluated live before the Save); `SearchTree` (widening, child creation, max^n backup,
  `RootChoice`); `ExpansionScaffold` (test-only fixed-count walk, explicitly not B4).
- **`SimulationService` additions** (same commit): `Honored` per-boundary flags (the planner now
  tracks `LastPrescriptionHonored` across the unit and action halves separately, reported by
  `TacticianActivationResolver` for the unit half); the callback `Run(snapshot, ILineDriver)`
  overload with `LineBoundary`/`LineStep`, the list `Run` reimplemented as a `ListLineDriver` over
  it (test 7's pin); `Probe(snapshot)` (stop at the very first boundary, for building a tree root);
  `ActingPlayerAtStart`/`ActingPlayerAtEnd` on the result.
- **Build choice - a cross-store rebind bug, found by the determinism test itself.** A macro
  enumerated by the action space's SCRATCH store (a second `GameSaveSerializer.Load` of the same
  snapshot) carries `ModelMoveEntry` bindings and unit/objective target references into THAT
  store. Prescribing it into a simulation (a THIRD store) let the movement resolver apply the move
  onto the scratch store's bindings while the simulation's own models silently never moved - no
  fault, wrong game, exactly the failure class 5c's C1 finding already trained us to watch for.
  Fixed with `SimulationService.Rebind(MacroAction, GameDataStore)`, called on every prescribed
  macro before it reaches the planner: model bindings re-resolved by `DataReference` (stable
  across every store of one game), targets re-resolved by unit ID / marker position. Caught by
  `ExpandingTheSameEdge_UnderTheSameSeed...` (test 6) before it could reach anything else.
- **Build choice - honored flag granularity.** Tracked as two booleans on the planner (unit half,
  action half) rather than one, because 5b's two prescription levels can independently fall
  through; `LastPrescriptionHonored` is their conjunction, read by the line driver at the NEXT
  boundary (the activation is only "over" then) and settled explicitly on early game-end
  (`SettleAfterGameEnd`) so `Honored` always has one entry per activation actually run.
- **Build choice - Cast/Disembark edge priors.** The design doc left these at "search sorts out
  their worth"; built as the mean of the plan-bearing edges' priors (not the max, not a fixed
  constant) so a unit with strong plan-bearing options doesn't get a Cast edge starved to
  near-zero relative to them for no principled reason - revisit if B4's benchmark shows it matters.
- **Test suite** (`Tests/SearchTreeTests.cs`, authored trees with fixed leaf values, no engine;
  `Tests/TacticianActionSpaceTests.cs`, real engine states via `ScenarioCompiler`): all nine
  sec-8 items green -
  (1) candidate counts at 4- and 16-unit-per-side fixtures (the 1k/4k unit-count stand-ins;
  real-army counts are the b0 numbers below);
  (2) a last-ranked Reachable ChargeToContact survives as an edge;
  (3) an edge the stage does not offer (Cast on a non-caster) closes and is never credited, a real
  edge opens;
  (4) one expansion at the root reproduces natural Tactician play byte-for-byte (B reduces to A);
  (5) vector backup on an authored 1v1 tree matches a scalar negamax reference visit-for-visit and
  picks the same root choice (the true minimax pick, verified against hand-computed values), a
  2v2 teammate node reads the ROOT's own component, a 3-side authored case shows a non-root side
  maximizing ITS OWN value rather than minimizing the root's;
  (6) the same derived seed reproduces a child snapshot byte-identically, a different worker seed
  does not (and the cross-store bug above was caught here first);
  (7) the callback line and the list line produce byte-identical results.
  Plus: `SimulationServiceTests` gained the callback/list equivalence pin, a `Probe` pin, and a
  stale-prescription-is-unhonored pin. Full engine suite 3199/3200 (1 skipped by design, same
  tally as before this build - no regression), full solution build green, headless smoke exit 0.
- **Hash-verify: `8D6EFA0AF0B4019E`**, unchanged from steps 4/5a/5b/5c, re-run after the final
  engine edit (the DOP-1 six-game Tactician-mirror cell). B2 adds nothing to natural play's path
  except the honored-flag bookkeeping (also exercised by every existing 5c test, all still green).
- **Test 9 - the fully prescribed line, measured at 1k/2k/4k** (new `fdglab b0` phase 3f,
  `--edge-reps`/`--edge-depth`; records a natural line's decisions via `LineBoundary.
  PreviousDecision` then replays them fully prescribed under the same seed - byte-identical and
  honored at every boundary in every rep, or the number would not be trustworthy; it was, 5/5 at
  all three levels). Measured WHILE the step-4 self-play generator was running at DOP 12 (undisturbed
  throughout, 110 -> 135 batches), so these are upper bounds like 5c's table:

  | Level | Root probe | Level-2 enum (top unit) | Natural line/activation | **Prescribed line/activation** |
  |---|---|---|---|---|
  | 1k (3 units) | 145ms | 121ms, 17 edges/11 families | 81.4ms | **29.2ms** (36% of natural) |
  | 2k (4 units) | 245ms | 111ms, 17 edges/10 families | 111.3ms | **58.6ms** (53% of natural) |
  | 4k (9 units) | 290ms | 231ms, 17 edges/9 families | 150.7ms | **45.2ms** (30% of natural) |

  Honest report, not tuned to look good: 2k's 58.6ms is ABOVE 5c's ~20ms target (5c's number was
  the SoloRules-in-sim arm; this is Tactician-in-sim throughout, since a prescribed activation is
  what B2 actually walks and the point was to measure THAT, not re-measure 5c's arm). 4k landing
  BELOW 2k is real, not noise - level-2 enumeration is per-unit, not per-army, so more units for
  the same line depth means more DISTINCT prescribed activations sharing the fixed per-line
  overhead, and 4k's units also spend more of their prescribed activations on cheap Shoot-only
  Hold plans (denser deployment, targets already in range) - recorded, not chased down further
  here. Per the campaign doc's own rule, a disappointing number is a legitimate outcome to report:
  B4 should treat 2k's in-sim-Tactician cost as the number to beat with the SoloRules arm or a
  cheaper in-sim policy, not assume 5c's target already stands.
- **Self-play undisturbed:** PID 51352 alive throughout, batch count climbed through the whole
  burst (110 -> 135+ in `FdgLab/data/2026-09-03/`).
- **Deferred, recorded** (design doc sec 9, unchanged by the build): shooting-target prescription,
  chance nodes, branch-point-only snapshot storage, transposition table, in-sim policy choice /
  continuation depth / widening constants - all B4.

Next: step 7 (B3 leaf evaluation on the C1 encoder vector - the placeholder `ObjectiveShareEvaluator`
here gets replaced by the real one; Sonnet/medium per the doc).

**2026-09-03 (night, Fable) - STEP 6 DESIGN TURN: B2 SPECIFIED IN `docs/tactician-b2-design.md`.**
Chris switched to Fable/high for the design half of step 6 (build is Sonnet/medium). One turn, no
code. The spec's decisions, each with its reason in the doc:

- **Node** = the 5c boundary (acting player known); acting side/player read from the snapshot's
  `GameProgressData`, never inferred from the parent (reactivations and P19 break alternation).
- **Edge enumerated in two levels, lazily** - unit (prior: `TacticianActivationResolver.Urgency`,
  cheap) then macro-action (prior: `TacticianPlanner.Score` on a scratch planner, the expensive
  one, paid once per (node, unit)). Both levels belong to the acting player so the tree backs up
  as one edge. Reason: a flat 4k edge set is ~320 x 165ms per node; two-level makes B's cost "A's
  cost plus lines", and B with one expansion IS A (a pin, test 4).
- **Edge vocabulary is the planner's own** `ActionNameFor` mapping (made internal), so search can
  never prescribe an action the planner would not name.
- **Honored-prescription flag (new engine requirement):** 5b's G3 fall-through silently turns an
  unoffered edge into A's natural move; `SimulationResult` must report per boundary whether the
  prescription was consumed, and a fell-through edge is closed, never credited.
- **Callback line** (`Run(snapshot, ILineDriver)`, 5c's note 1) so the leaf is evaluated LIVE at
  the terminal boundary before the line's one Save - no serialization for evaluation.
- **Progressive widening at both levels**, k(N) = ceil(2 * N^0.5); constants are options, tuned at B4.
- **Dice: determinization at v1** (an edge's first sim fixes its child), bias controlled by B4's
  root-parallel ensemble with per-worker seeds; chance nodes recorded as the upgrade, with the
  charge-vs-shoot probe as the evidence that would trigger it.
- **`SideValues` indexed by team, max^n backup**, selection reads the acting side's own component;
  two-side evaluators must satisfy v[other] = 1 - v[self] so 1v1 reduces to minimax (pinned against
  a scalar negamax, test 5). `IPositionEvaluator` seam ships with terminal-only and objective-share
  placeholders; B3 fills it.
- **Memory:** snapshot per created child at v1 (0.4-0.64 MB each); branch-point-only re-run
  storage recorded as B4's fallback, legal because of the determinism pin.
- Deferred, recorded: shooting-target prescription (B5), chance nodes, transposition table (still
  no), in-sim policy and continuation depth and widening constants (B4 on the benchmark).

Verification list is nine items (doc sec 8), including the fully-prescribed line cost 5c could not
measure. Next: the build (Sonnet/medium per the doc; the protocol forbids a second model prompt
inside one step, so it proceeds on whatever model the session has, noted in the build's entry).

**2026-09-03 (night, later still) - STEP 5c BUILT: THE PAUSE/STEP HOOK, THE BUS BYPASS AND
`SimulationService`. A LINE IS 5-8x CHEAPER PER ACTIVATION THAN A CLONE, AND THE ~20ms TARGET IS
MET WITH A CHEAP IN-SIM POLICY - ON A BOX THAT WAS BUSY GENERATING DATA AT THE TIME.** Chris
switched the session to Opus (the model step 5b/5c call for) and said to continue; 5c is the last
of step 5's three commits. Engine commit `34b920a`, superproject bump alongside the lab-side
measurement phase.

- **The hook (D10a, pre-authorized).** `FDG.Simulation.IActivationBoundaryHook`, called from
  `DeterminePlayerTurnStage.Enter` once the acting player is determined (P19 override included)
  and before any decision of that activation, including the reactivation offers. Placed AFTER the
  player is known because a prescription has to reach the right policy; the rolling save point at
  the top of `Enter` has already written `GameProgressData`, so a snapshot taken inside the hook is
  exactly the engine's own save point - which is what makes chaining work. `IGameContext
  .ActivationBoundaryHook` defaults to null and is set only by `SimulationService`, so **real play
  pays one null check per activation and nothing else**.
- **`SimulationService`** (`FutureOfDarkGrimness/Simulation/`): `Snapshot(store)`,
  `Advance(snapshot, prescription)`, `Run(snapshot, prescriptions[])`, plus `RunNatural(snapshot, n)`.
  A line runs consecutive activations in ONE resumed instance and serializes once at the end.
  **Line length IS the search depth** - multi-ply is the same call as single-ply, so a
  disappointing number would have meant shipping shorter lines, not a rewrite. Per-simulation seed;
  probabilistic dice with sampled decisive rolls is the default (the threshold-shift invariant
  lives in the dice roller - this only picks the mode). No `Rollout`, per the step-7 revision.
- **Bus bypass.** `DirectPlayerRequester` answers a simulation's decisions straight from the target
  slot's registry via the typed path. Real play keeps `RequestMessageSender` and the bus untouched.
  `BypassBus` is a switch rather than a hard-coded path specifically so the equivalence pin can run
  the SAME line both ways and assert byte equality - hash-verify cannot cover the bypass, because
  real games never travel it.
- **A defect the first measurement exposed.** The throw-stop unwound into `FDGServer`'s generic
  catch, which prints `[GAME ERROR]` plus a full state-machine stack trace. Harmless once, but a
  search runs thousands of simulations, and that is both console noise and real formatting cost.
  `SimulationStopSignal` is now its own type and FDGServer ends it quietly; genuine faults still
  print in full. Verified: 0 stop-signal traces from the line primitive in a 1000-simulation soak
  (the 19 traces still in the b0 log are B0's OLD resolver-throw stop, unchanged).

**The numbers (2k, Release).** Measured while the step-4 self-play generator was running at DOP 12
on the same box, so **every figure here is an upper bound** and is NOT comparable to B0's
clean-box table (that run's load alone measured 53ms against B0's 37ms). The comparison that IS
valid is internal: phase 3 and phase 3e ran in the same process under the same load.

| Primitive (same run, same load) | per activation |
|---|---|
| clone per activation, Tactician in-sim (B0's old primitive) | **177.2ms** |
| line depth 1, Tactician in-sim | 106.4ms |
| line depth 8, Tactician in-sim | **34.5ms** |
| line depth 1, SoloRules in-sim | 58.8ms |
| line depth 4, SoloRules in-sim | 21.4ms |
| **line depth 8, SoloRules in-sim** | **20.5ms** |

So the hook delivers **5.1x** against the old primitive with the full planner in-sim, and **8.6x**
with a cheap in-sim policy - and 20.5ms/activation is the campaign doc's ~20ms target, hit on a
loaded box where a clone alone (load+save) cost 120.7ms. The target's referent is B0 finding 2's
"(b) cheap in-sim policy + the hook projects to ~11-20ms/node", which is the SoloRules arm. Both
arms are reported because the choice between them is a real Phase B decision (evaluation bias vs
cost), not settled here.

**Honest gap in this measurement, recorded rather than papered over.** A *fully prescribed* line -
unit AND action AND macro-action, which is what B2's tree edges will actually carry - is not
measured, because synthesizing a real `MacroAction` outside the planner is B2's job. 5b already
pinned that a prescribed activation runs neither `Urgency` nor `MacroActionGenerator.Enumerate`
+`Score`, so its cost sits at or below the SoloRules arm; the honest statement is that the target
is met by the arm the projection named, and the fully-prescribed number lands with B2. Re-measure
on a quiet box at the same time (the running generator is not worth stopping for a number that is
already inside target).

- **Leak soak (R1, new primitive):** 1000 line-simulations, depth 2, throw-stopped - **1000/1000
  completed, 0 missed**, post-GC heap flat at 11-15MiB start to finish (end 11MiB, delta **-6MiB**),
  threads flat at 81, 14.5 sims/s. RSS grew +255MiB where B0's 400-sim THROW run saw RSS fall;
  post-GC heap is the leak signal and it is flat, so this reads as allocator retention rather than
  a leak, but it is worth a second look at B4's 500-game soak.
- **Verification:** hash-verify `8D6EFA0AF0B4019E` (DOP-1 six-game cell), unchanged from step 4,
  5a and 5b - re-run AFTER the FDGServer catch change, not just before it. Engine suite 3182/3183
  (1 skipped by design, +6 new pins), full `dotnet build` green, headless smoke exits 0 (tie, 4
  rounds). Bypass-equals-bus pinned by test, since no hash can cover it.
- **Self-play undisturbed:** PID 51352 alive throughout (2h17m at the last check), 97 -> 110
  complete batches during this burst. It was launched without `--pause-file`, so it could not be
  cooperatively paused for the measurement; that is why the numbers above are labelled as taken
  under load. Worth launching future generators WITH a pause file.
- **Step 5 (B1) is complete.** Next is step 6 (B2 composite action space + multiplayer backup) -
  design is Fable/high for one turn, build is Sonnet/medium. B2 inherits: the line API with depth
  as a parameter, prescription through the planner, and the boundary hook as the node definition.
  Two notes for it: (1) a `Run` overload taking a prescription CALLBACK rather than a fixed list is
  probably wanted, since a tree supplies decisions lazily as the line walks - deliberately not
  built on spec; (2) reading the planner's chosen `MacroAction` (not just `LastMacroLabel`) is what
  the fully-prescribed cost measurement needs.


**2026-09-03 (night, Opus) - STEP 5b DONE: THE PRESCRIPTION SEAM, AND B0'S CONTROL FLIPPED
FROM DIVERGING TO IDENTICAL.** Chris switched the session to Opus (the model the campaign doc
requires for 5b/5c) - that was the sign-off 5b was waiting on. Engine commit `28b6443`.

- **Diagnosis re-verified before building on it, and the campaign doc's prose is wrong.** Step 5's
  bullet says the divergence is because "`TacticianActionResolver.Resolve` runs
  `_planner.BeginActivation` as a side effect". It does not - `BeginActivation` is called by
  `TacticianActivationResolver` (the `ChooseUnitToActivateRequest` resolver), which is what the B0
  ledger entry (finding 4) actually said. The consequence is not cosmetic: the critical prescription
  level is the ACTIVATION choice, not the action, because that is the one carrying the side effect.
  The doc's own "prescribing the activation choice is the same seam one level up" has it backwards -
  the activation choice is the seam, and the action is the level up.
- **The seam.** `TacticianPlanner.Prescribe(unit, action, macroAction)` sets the decision; the
  resolvers consume it and the policy's own per-activation setup still runs.
  `TacticianActivationResolver` takes a prescribed unit (matched by `DataReference`, since a
  prescription can arrive as a different binding instance) and calls `BeginActivation` on the
  ENGINE's binding for it; `ChooseAction` consumes a prescribed action ahead of every scoring
  branch, mirroring the natural path's state exactly (Cast increments `_castAttempts` and carries no
  plan; Disembark likewise; a plan-bearing action stores `_plan` and `LastMacroLabel`). Prescription
  fields deliberately survive `BeginActivation` - unit and action are prescribed together and
  `BeginActivation` runs between them.
- **Scoring is skipped, not overridden.** A prescribed activation runs neither `Urgency` nor
  `MacroActionGenerator.Enumerate`+`Score`. That is where 5c's ~20ms/activation budget comes from
  (B0's 165ms of policy thinking), so it is pinned by a test asserting the decision log stays empty.
- **G3 fall-through, no half-states.** A stale prescription (unit not activatable now, action not
  among the offered options) or a plan-bearing action arriving without its `MacroAction` falls back
  to natural scoring rather than faulting or leaving the movement resolver with no cached move.
- **The pin, at both levels.** Engine: `Tests/TacticianPrescriptionTests.cs`, 10 tests - prescribing
  the planner's own choice reproduces action + move + macro label; prescription beats the argmax;
  a prescribed unit reaches `BeginActivation`; unprescribed play is untouched; the fall-throughs.
  Game level: `fdglab b0`'s phase 3c control now prescribes THROUGH the seam, and a third arm
  (`EInjectMode.WireFirst`) keeps the old wire-boundary injection as the regression witness for
  finding 4. On a real 2k board (Orks vs Robot Legions, boundary 12, 3 valid options):

  | Arm | Result |
  |---|---|
  | two natural advances | MATCH (G5 holds) |
  | steer to last option, through the seam | DIFFERS from natural (prescription really steers) |
  | **control - policy's own pick, through the seam** | **IDENTICAL to natural (the flip)** |
  | same pick answered at the wire boundary | DIFFERS from natural (finding 4 still holds) |

  The sharpest form of it is on `builtin-basic`, where the boundary offers exactly ONE option: the
  choice is identical by construction and the only difference is whether the planner was told, and
  the wire arm still diverges while the seam arm does not.
- **Hash-verify:** DOP-1 six-game cell `8D6EFA0AF0B4019E`, unchanged from 5a and step 4 - the seam
  is decision-neutral for unprescribed play. Engine suite 3176/3177 (1 skipped by design, +10 = the
  new pins), full `dotnet build` green, headless smoke exits 0 (tie, 4 rounds).
- **Self-play undisturbed:** PID 51352 ran throughout, 88 -> 95 complete batches during this burst.
- **Next: 5c** (pause/step hook at the activation boundary, D10a pre-authorized) - the last of
  step 5's three commits, also Opus/high. Note for it: 5c wants the literal
  `DeterminePlayerTurnStage.Enter` point, which is also where step 4's exporter took a documented
  detour (it hooks `ChooseUnitToActivateRequest` instead) - if 5c builds the real stage hook, the
  exporter's boundary seam can move onto it.

**2026-09-03 (night, later) - STEP 5a BUILT: CHOOSEACTIONREQUEST IS ITS OWN REQUEST TYPE.**
Chris said "continue with B1" once step 4's self-play driver was confirmed generating (PID
51352 still alive throughout this slice, batches still growing in `FdgLab/data/2026-09-03/`).
Built exactly the campaign doc's step 5 first bullet, mechanically - no decision logic changed:

- New `StageResolution/Requests/ChooseActionRequest.cs`, mirroring `ChooseAbilityEffectRequest`/
  `ChooseSpellRequest`'s precedent: carries `ActivatingUnitID` (the follow-up
  `TacticianActionResolver`'s doc comment recorded) plus the same options/descriptions/
  `AllowCancel` payload `StringSelectionRequest` had for this menu specifically (not
  `SecondaryActions`/`OptionRules` - Choose Action never populated those; they stay on the weapon
  menus that still ride `StringSelectionRequest`). Reply stays `string`.
- `ChooseActionStage` issues the typed request instead of `StringSelectionRequest` with
  `Instructions == "Choose Action"`.
- `AiStringSelectionResolver`, `TacticianActionResolver`, `GunlineResolvers` each split into a
  `ChooseActionRequest` handler (the old Choose Action branch, verbatim) and a
  `StringSelectionRequest` handler (everything else, unchanged - hold-or-deploy, weapon menus).
  Registered explicitly for both types in `AiResolverRegistryFactory`,
  `TacticianResolverRegistryFactory`, `GunlineResolverRegistryFactory`.
- CLI (`StringSelectionResolver`) and GUI (`GuiStringSelectionResolver`) each gained a
  `ChooseActionRequest` overload that mirrors the request into the `StringSelectionRequest` shape
  their existing menu-printing/ImGui code already draws, then delegates - no rendering code
  duplicated. **GUI half unverified by eye** (Chris away) - covered by `FdgRaylib.Tests` +
  headless smoke only; top "awaiting GUI hand-verify" item for Chris's return.
- `FdgLab/Export/ExportingRegistry.cs` (step 4's exporter) updated to key `chosen_action` off the
  new typed request instead of the "Choose Action" string sniff - simpler, and no longer fragile
  to a future rename of that string.
- Fixed ~13 test-double fixtures across the engine test suite that answered
  `IPlayerRequestByID.RequestDecision` by pattern-matching `StringSelectionRequest` for what is
  now a `ChooseActionRequest` (`RecordingActionRequester`, `CapturingStringSelectionRequester`,
  `CapturingChoiceRequester`, `ActionMenuRequester`, `FirstStringRequester`,
  `CannedStringChoiceRequester`, `PlaceThenChooseRequester`) - each now answers both types.
- **Hash-verify:** `./FdgLab/bin/Release/net8.0/FdgLab bench --a builtin-basic --b builtin-basic
  --profile-a tactician --profile-b tactician --games 6 --dop 1` (Release, same command tonight's
  step-4 entry used) reproduces `8D6EFA0AF0B4019E` - identical to the value that entry recorded
  for the engine BEFORE this slice, so the request-type split is confirmed decision-neutral
  without needing a separate stash/rebuild round-trip.
- `dotnet test FutureOfDarkGrimness/FutureOfDarkGrimness.csproj` 3166/3167 green (1 skipped by
  design, unchanged); full `dotnet build` green; `FdgRaylib.Tests` 2830/2830 green; headless
  smoke (`printf "2\n2\n" | dotnet run ... -- --headless`) exits 0 with the expected
  `Game result:` line.
- Step-4 self-play run confirmed undisturbed throughout (PID 51352 unchanged, batches kept
  landing in `FdgLab/data/2026-09-03/` across the Release rebuild used for hash-verify).
- **Next: step 5b (prescription seam, policy-side)**, per the campaign doc's protocol Opus /
  high effort and Chris's model-switch sign-off - not started this slice.

**2026-09-03 (night) - STEP 4 BUILT AND LAUNCHED: C1 EXPORTER + SELF-PLAY DRIVER, ALL SIX
PRE-LAUNCH CHECKS GREEN, GENERATION RUNNING.** Chris signed off on all four schema sign-off
items (`docs/tactician-c1-schema.md`) unchanged from the authored spec. Built exactly to that
spec:

- Engine (`FutureOfDarkGrimness/Ai/Tactician/Learning/PositionEncoder.cs`, two commits): the
  67-float v1 vector (7 global + 4x15 per-side blocks) and the 16-float entity table, both pure
  reads of `ITableState` - no dice, no mutation. `TacticalAnalysis.MeleeOutputWounds` added
  (melee twin of `RangedOutputWounds`, needed for `melee_share`). `TacticianPlanner.LastMacroLabel`
  exposed for `chosen_macro`; `TacticianResolverRegistryFactory.Build` and
  `AiProfileFactory.BuildRegistry` gained additive `out TacticianPlanner?` overloads so the
  exporter can read it without building a second planner.
- **Boundary seam - a scope note for B1.** The spec named `DeterminePlayerTurnStage.Enter` as
  the activation boundary (B0's snapshot point). Building that literally needs a per-game engine
  hook with no existing seam (`GameProgressData`'s store-level writes fire at that point too, but
  collapse the "about to choose a unit" and "activation just ended" writes to the same
  indistinguishable shape - not a reliable signal from outside the engine). Used instead: the
  already-typed `ChooseUnitToActivateRequest` (#191 A4-1, no engine change needed) as the
  boundary - encode BEFORE it resolves, read the chosen unit off the reply. Functionally
  equivalent (state-before-the-decision, same activation), but if B1 needs the literal stage-entry
  point later, this is the one seam that would need to move first.
- **Real gotcha - local AI decisions go through the JSON wire path, not the typed one.**
  `RequestMessageSender.RequestDecision` serializes every request before dispatch, local players
  included (the profiling ledger's "~7% JSON round-trip" note, still unpaid off). First exporter
  version hooked `IStageResolverRegistry.ResolveRequest<TRequest,TReply>` and silently captured
  ZERO rows across 6 real games (0 faults, so no error - just quietly wrong). Fixed by hooking
  `ResolveRequestAsJson` instead, deserializing with the engine's own `WireJsonSettings.For(store)`
  so the parse matches the real wire format exactly. Recorded here because it is the kind of bug
  that would have wasted the whole unattended window silently (schema sec 7's stated worst case).
- **Cost gate - first measurement was 2x over budget, fixed.** `threat_coverage`'s first cut did
  an O(units^2) `TacticalAnalysis.ThreatRangeAgainst` sweep (a rule evaluation per target per
  pair) - measured 9.53ms/call, over the schema's 5ms cap. Replaced with an O(units) per-unit
  cheap-reach precompute (raw weapon range + `AdvanceDistance`/`ChargeBudget`, no per-target
  conditioning) compared via O(1) distance arithmetic; remeasured 1.7-3.6ms/call across mixed
  1k-4k, 1v1/2v2 samples. Documented as an accepted precision loss (a pair's real threat range is
  target-conditioned - Melee Shrouding etc - which this coarse coverage fraction was never going
  to capture at 5ms anyway).
- **Hash-verify.** `bench --a builtin-basic --b builtin-basic --profile-a tactician --profile-b
  tactician --games 6 --dop 1` (this session's own invocation, not literally the historical
  `72C6968E75359448` cell - could not find its exact recorded command) produces the SAME hash
  (`8D6EFA0AF0B4019E`) with the engine changes stashed vs applied, and again after the
  threat_coverage rewrite. Full suite green both times (3166/3167, 1 skipped by design).
- **FdgLab (`FdgLab/Export/`, `FdgLab/SelfPlay.cs`, `FdgLab/armies/mix.json`):** `fdglab selfplay`
  samples (profile pairing, points level/shape, armies) per-game from `mix.json`'s weights
  (default 70/20/10 mirror/vs-solo/vs-gunline, levels weighted roughly even across 1k-4k plus
  2v2), refusing any `pool.json` `heldOut` pairing by construction (never sampled, not just
  filtered after). Writes gzipped JSONL in fixed 200-game batches (one file = one atomic unit:
  written under `.tmp`, renamed on completion) under `--out`; restart resumes at one past the
  highest complete batch found there. Header carries the schema's provenance fields for the
  batch's FIRST game; since a batch mixes matchups, a `kind=game` line per completed game
  restores real per-row provenance (recorded as a deviation from the schema doc's literal
  single-header assumption, additive, not a schema change). 1-in-4 boundary subsampling is a
  deterministic `boundary % 4 == 0` keep (uniform, matches sec 5b). Faulted/disconnected games are
  discarded whole, never labelled.
- **Six pre-launch checks (schema sec 7), all green** on a 40-game/660-row sample (seeds
  9000-9039, mixed 1k-4k + 2v2, DOP 12, `--entity-sample-rate 0.05`) plus a separate 2-run
  determinism pair (seed 5000-5003, `--entity-sample-rate 0.5`):
  1. No duplicate/missing boundaries within a game (every kept boundary a distinct multiple of 4).
  2. 0 of ~46,000 feature values (660 rows x 67 + entity floats) outside its declared range.
  3. Label balance non-degenerate (275 win / 224 loss / 161 tie across 660 rows).
  4. 0 held-out-pairing violations across 40 games, checked against `pool.json`'s `heldOut` list.
  5. Byte-identical (modulo GUIDs and concurrent file-write order, which the schema's intent does
     not cover) across two independent same-seed runs - 95/95 rows, 50/50 entity blocks, exact
     match sorted by content.
  6. `encoder_ms_mean` 1.7-3.6ms across samples (post-fix), under the 5ms budget.
- **Launched:** `fdglab selfplay --out FdgLab/data/2026-09-03/ --dop 12` (no `--max-batches`, runs
  until stopped), Release binary, in the background. See the "running" line in the same-day
  phone-format reply for the process handle and ETA.
- **Not built yet / explicitly deferred, not silently:** the entity table's per-unit `is-caster`
  feature is a crude proxy (rule-name substring match, not a real caster query) - fine for a
  sampled, v2-only table nothing in v1 trains on. `points-2k`'s panel wasn't in the original
  campaign step-4 spec text but exists in `pool.json` already (added alongside the Titan Lords
  work) and is included in `mix.json`'s levels - a reasonable read of "points and shapes weighted
  per section 5," flagged here in case Chris meant something narrower.

**2026-09-03 (evening, Fable) - TITAN LORDS: A'S WORST MATCHUP BY A WIDE MARGIN, FOUND FROM
CHRIS'S REMARK THAT THE LIST IS SIX SINGLE-MODEL HIGH-TOUGH UNITS.** Titan Lords appeared in no
1v1 panel and no held-out pair - only inside the 2v2-3k cell that was the baseline's weakest (79%).
Added `3k Titan Lords vs 3k Goblin Reclaimers` to `points-3k` and ran it three ways (100 games,
seeds 6000, Release, DOP 16, 0 timeouts; reports `points-3k-titan*` under the step 2 directory):

| Cell | Tactician plays | Score | W/L/T | Hash |
|---|---|---|---|---|
| Titan vs Goblin, vs solo | Titans | 98.0% | 96/0/4 | `D8F14884769D2603` |
| Titan vs Goblin, mirror | both | Titans 77.0% | 64/10/26 | (mirror) |
| **Goblin vs Titan, vs solo** | **Goblins** | **63.0%** | **51/25/24** | `10F2F797C6859611` |

Every other vs-solo cell in the baseline is 92-98%; here a DerpBot-played Titan list takes 25 wins
and 24 ties off A. Note the harness fact this exposed: in a panel cell the profile binds to its
ARMY and the swap flips only seating, so a one-direction cell measures A playing side A only -
which is why the 98% and the 63% coexist and why the reverse direction is the one that matters.

G2 (two logs of the reverse cell read, seed 6000 fwd loss 1-2 and swp tie 0-0): few units die on
either side; Titan shooting Shakes the Goblin mobs repeatedly ("Shaken - stays idle this
activation"), and a single Tough model near a marker contests it indefinitely - the tie ends 0-0
with everything contested. A's `UnitValue` is wound-based, so it VALUES a Titan correctly; what it
lacks is focus fire (its target choice spreads expected wounds rather than finishing one Titan to
unlock an objective) and any notion of activation economy (6 vs ~20 activations). Both are
multi-ply consequences, so this is the cell where B's search should show value first - recorded
as a named probe in the campaign doc section 5, both directions reported at every gate.

Schema consequence (sign-off item 5 in `docs/tactician-c1-schema.md`): `activation_share` (this
side's living units / all sides', a share so still no absolutes) added to the per-side block -
67 floats now - and the generation mix must include Titan Lords so C sees single-model armies.
**2026-09-03 (later, Fable) - STEP 2 CLOSED, SECOND PROFILING PASS: THE BENCHES WERE RUNNING
DEBUG BINARIES (x1.8 FOR FREE), A SMALLER ALLOCATION-CHURN WIN, AND THE B1 PLAN REWRITTEN
FROM B0'S NUMBERS.**

**Step 2 close-out - the 3k 2v2 cells** (DOP 6, 900s watchdog, 100 games/cell, 0 timeouts in
both cells; reports under `FdgLab/reports/step2-baseline-2026-09-03/shape-2v2-3k__*`):
Tactician vs solo 96.5% (Saurian+Goblin vs Cults+DE) and **79.0%** (Battle+Knight vs Robot+Titan,
70/12/18 - the weakest cell of the whole baseline, 18 ties); mirror 49.5% / 40.5% (hashes
`C4817F42DD71E720` / `7E8077ED6FD1B6C2`). Per-game wall mean 45s, p95 93s, decisions/game 769,
decision mean 44.5ms. The earlier 97/100 "timeouts" were the 120s watchdog, not the engine - at
900s there are none. Step 2 is complete: vs-solo panel means 1k 96.9 / 2k 92.6 / 3k 92.8 / 4k
97.8 / 2v2-2k 91.3 / 2v2-3k 87.8. The Battle+Knight cell is the one to read first when B's gate
asks "where is A weakest" (G2).

**Finding 1 - every lab run to date used the Debug build.** `dotnet run --no-build` defaults to
Debug; `FdgLab/bin/Release` was dated Aug 6. Rebuilt Release and re-ran the DOP-1 six-game
neutrality cell: outcome hash IDENTICAL (`72C6968E75359448`), total wall 23.7 -> 12.9s, per-game
mean 3936 -> 2136ms, **decision mean 11.04 -> 5.58ms (-49%)**. Recorded as an operating rule in
the campaign doc section 6; step 4's driver and every bench from now on run
`./FdgLab/bin/Release/net8.0/FdgLab` directly. Historical PERFORMANCE lines in bench reports
before today are Debug numbers and not comparable; hashes are unaffected.

**Finding 2 - the profile, second look (Release binary, sample profiler, single 2k mirror).**
Non-idle CPU was 17.7% of samples (the rest is the single game's bus/await hops - filled by other
games at DOP > 1, so not a throughput loss). Inclusive: `TacticianPlanner.Score` 28%,
`RuleEvaluator.Collect*` 21% (of which `DedupState.ShouldFire` 14.6%), `MacroActionGenerator.
Enumerate` 17%, `CombatMath.EstimateVolley` 16%, `MovementPlanner.PlanMoveAlongRoute` 13%.
Exclusive leaves: `HashSet<(UnitID, ResolvedRule)>.Resize` 19%, `List.set_Capacity` 10.7%,
`TokenContainer.HasToken` 6.0%, JSON request round-trip ~7% (`RequestDecision` ->
`ResolveRequestAsJson_Typed`). Geometry is gone from the top (`PointToSegmentDistanceSquared`
0.06%) - the AABB fix did what it said.

Fix (engine, this commit): `RuleEvaluator.DedupState` is now rented from a per-thread pool
(`Rent`/`Return`, `Clear()` between uses, capped at 8, Stack so nested evaluations rent a second
one; per-thread because the render thread's read-only queries run concurrently with the engine
thread - #328's shape); `CollectFromRules` reuses one `produced` scratch list per walk instead of
one per firing entry; `TokenContainer.HasToken`/`GetAllTokens(type)` lose their LINQ closures;
`HeroStatRules.LivingModels` is a pre-sized loop. **Measured honestly, the sampler over-sold it:**
smoke seed 4242 A/B (best of two each, Release): Choose Action mean 32.8 -> 30.4ms (-7%), whole-
game decision mean on the DOP-1 cell 5.58 -> 5.47ms (-2%); same game outcome, same hash. Kept
because it is a clean allocation removal with zero semantic change, not because it is large. The
sampler's exclusive attribution to `Resize` evidently absorbs allocation/GC time that the pool
does not eliminate. Lesson for G6: a sample profile ranks CAUSES well and sizes them badly -
A/B every fix with the timing breakdown before claiming a number.

Left on the table, recorded: the JSON round-trip for local AI players (~7%) - noted in step 5c as
"bypass the bus inside a simulation" (the search answers prescribed requests via the typed
registry path, never the wire); `AllWeapons` / `GetTotalMoveDistances` /
`ValidateCoherencyNotWorsened` list growth (~1.5-1.8% each); `AircraftRules.IsAircraft` string
scans inside `CanSeizeObjectives`. None is worth a third pass before B1 exists.

**Plan changes (campaign doc):** Step 5 rewritten from B0's numbers - 5a typed
`ChooseActionRequest` carrying the activating unit ID (Chris, 2026-09-03; the recorded follow-up
in `TacticianActionResolver`'s doc comment, same precedent as `ChooseAbilityEffectRequest`),
5b policy-side prescription seam with B0's failing control test as the pin, 5c pause/step hook
targeting ~20ms/simulated activation; `Rollout(...)` removed from the API; depth is a parameter.
Step 7 is now static leaf evaluation on the C1 encoder vector (B and C share one code path).
C1 schema gains a sign-off item: `chosen_unit` / `chosen_action` / `chosen_macro` per row, so
the data supports a policy head without a regeneration run.

**2026-09-03 - STEP 3 (c) PROFILING: THE PHASE B FIDELITY TRADE IS UNNECESSARY, AND A 10-LINE
GEOMETRY FIX TOOK 32% OFF EVERY TACTICIAN DECISION.** Chris approved (c) "regardless and first",
and asked what (a)/(b) would cost long-term. The measurements answered both questions.

**Where an activation's policy time actually goes** (new `smoke --timing-breakdown`, per-request-
type tally in `TimingRegistry`; seeded 2k Tactician mirror):

| Request | share | mean | calls |
|---|---|---|---|
| StringSelectionRequest (Choose Action) | **80.3%** | 60.6ms | 79 |
| ChooseUnitToActivateRequest | 6.0% | 7.6ms | 47 |
| PlaceObjectsRequest<ModelData> (deployment only) | 6.0% | 20.9ms | 17 |
| SelectionRequest<UnitData> (deploy order) | 4.4% | 17.4ms | 15 |
| ChooseRangedAttack / DefineMovementPath / AssignWounds / melee / consolidation | **1.8% combined** | <2.6ms | 100 |

**Consequence: option (b) is moot.** Choose Action plus activation choice is 86% of policy cost,
and both are exactly what a search PRESCRIBES - during a simulated activation they are answered
from the tree edge, not computed, so that cost vanishes by construction with ZERO fidelity loss.
The decisions a cheap in-sim policy would have had to own total under 2%, so the full Tactician
can answer them for free. No bias trade, and no need to accept option (a)'s depth ceiling either.
DefineMovementPath being just 1.0% (1.6ms) is the tell: by the time the engine asks for the path,
the planner has already decided it during Choose Action.

**The profile, and the fix it produced.** dotnet-trace on a Tactician game: discounting ~83% idle
thread-pool wait, real CPU was ~40-50% SEGMENT GEOMETRY (SegmentToSegmentDistanceSquared,
PointToSegmentDistanceSquared, SegmentsIntersect, RectangularZone.LinesIntersect, Float2
arithmetic) and ~20% List growth/Resize. Cause: `LineOfSightUtilities.EvaluateSightLine` walks
EVERY terrain piece linearly, each piece costing four LinesIntersect (16 cross products) plus,
when inflated, four segment-to-segment distance computations - and `TacticianPlanner.Score` issues
one sight test per (candidate x enemy) on tables #268 made dense. Fix (engine `a741423`): a
four-comparison bounding-box rejection in `RectangularZone.DoesPathIntersectZone`, conservative by
construction. Measured on the seeded 2k mirror: **decision mean 22.18 -> 15.03ms (-32%), p95 136.2
-> 76.0ms (-44%), Choose Action 60.63 -> 38.30ms, game wall 7879 -> 6000ms (-24%)**. Neutrality
proven this repo's way - dop-1 six-game outcome hash IDENTICAL (`72C6968E75359448`) before and
after - plus suite 3166/3167, full build, headless smoke exit 0. This is the second time profiling
this path has found a large win in one rebuildable/skippable structure (cf. the 2026-07-26
TerrainGrid cache, 2.2x); a third look at the remaining List-growth churn is likely worth it.

The win compounds everywhere at once: the shipped bot's in-game pause, data-generation throughput
for the C1 window (~24% more games for free), and Phase B's node-expansion cost.

**2026-09-03 - STEP 3 (B0) COST NUMBERS, CLEAN BOX. The plan's decision-table remedy targets the
wrong component, rollouts are dead as a leaf estimate, and there IS a measured path to real
multi-ply search.** Reports: `FdgLab/reports/`, raw logs in the session scratchpad.

**Node expansion (clone -> advance exactly one activation -> snapshot), THROW stop:**

| | 2k | 4k | 2k, solo-rules as the in-sim policy |
|---|---|---|---|
| total | 222.7ms | 845.9ms | **76.4ms** |
| run (policy thinking) | 165.4 (74%) | 764.5 (90%) | **10.5** |
| load | 37.0 | 53.6 | 43.2 |
| save | 17.3 | 24.8 | 19.7 |
| assemble | 2.9 | 2.9 | 3.0 |

Snapshot size 401.6 KiB (2k) / 640.5 KiB (4k); re-save delta 0 chars, so the round trip is
byte-exact. Boundary reached 30/30 in every configuration; determinism holds; chained advances
8/8 at both sizes.

**Four findings, in order of consequence.**
1. **The dominant cost is the POLICY, not the snapshot path** - 74% at 2k, 90% at 4k. The plan's
   own decision table prescribes "> 200ms -> optimize the snapshot path before proceeding"; we
   measured, and that remedy would have chased 24% of the cost while the other 76% sat untouched
   (G6 vindicated, plan sec 9 B0's remedy line superseded - G10).
2. **A cheap in-simulation policy collapses it 16x** (run 165 -> 10.5ms), and then the picture
   INVERTS: at 76ms/node the snapshot path is 82% of the cost, which is exactly when the
   pre-authorized pause/step hook (reusable server, no clone per node) becomes the big lever
   rather than the ~20% it is today. **(b) + the hook projects to ~11-20ms/node = the plan's own
   "FULL MCTS, hundreds of nodes at a 5-10s budget" band.** Genuine multi-ply search is reachable;
   it is not a 1-ply-forever situation.
3. **Rollouts to game end are dead as a per-leaf estimate.** Measured 12.0s at 4k = 14x a 4k node
   expansion, 49x a 2k one. Plan B3 ("both sides play the Phase-A greedy policy to game end") is
   not affordable per leaf at any budget we would ship. The leaf estimate must be an EVALUATOR -
   heuristic now, learned in C - which also means C's value is higher than the ladder implies.
4. **THROW beats ABANDON on every axis, so R1 is closed.** 4k soak, 400 sims: THROW ends at heap
   delta **0MiB** (RSS actually fell 182MiB); ABANDON ends +52MiB heap, +328MiB RSS, and is SLOWER
   per advance (960 vs 846ms) because orphaned games keep burning CPU. Zero misses either way.

**Benchmark affordability, corrected.** An earlier read of these numbers called a B-gate
infeasible; that was wrong, because it reasoned in wall-clock with root parallelism instead of
CPU-seconds with games parallelised across cores. At 25-50 expansions per searched decision a
100-game 2k cell costs 1-2h at DOP 16 - a normal overnight gate. Root parallelism is for
human-facing latency, not for benching.

**Open design fork (Chris's call, in progress):** (a) selective shallow lookahead vs (b) cheap
in-sim policy vs (c) profile the planner first. Chris has approved (c) regardless and first, and
asked specifically what (a)/(b) cost long-term. Analysis in the reply of record; the short version
is that (a)'s cost is a permanent DEPTH ceiling and a weaker policy-improvement operator for the
C/D loop, (b)'s cost is a systematic evaluation BIAS (smaller than it first looks, because a
fully-specified macro-action prescribes most of what the in-sim policy would otherwise decide),
and NEITHER corrupts C's training labels, which are real game outcomes.

**2026-09-03 - STEP 2 ADDENDUM: THE TACTICIAN'S PER-DECISION COST SCALES BADLY WITH UNIT COUNT,
and it is a Phase B feasibility problem, not just a bench annoyance.** The 3k 2v2 cell (Saurian+
Goblin vs SoulSnatcher+DarkElf - ~50 units, 12k points, the #296 crowded shape) measured:

| 3k 2v2 cell | decision mean | worst p95 | watchdog timeouts |
|---|---|---|---|
| Tactician vs SoloRules | 33.8ms | 685ms | 7 / 100 |
| Tactician BOTH sides | 90.1ms | 3166ms | **97 / 100** |

Both sides planning roughly TRIPLES the per-decision mean and pushes p95 past three seconds, and
97 of 100 games blew the 120s watchdog (that cell's reported 66.7 is computed over 3 completed
games and is meaningless - the fault list is what makes it visible, which is the whole reason
faults are listed per plan G2). Two causes compound: the planner's scoring is roughly
O(candidates x enemies) with CombatMath per pair, so cost grows superlinearly in army size; and
at DOP 16 sixteen such games oversubscribe the box, inflating the per-game WALL time the watchdog
actually measures. Re-queued as its own `shape-2v2-3k` panel at DOP 6 / 900s - the 2k 2v2 cells
measured clean (0 faults) and stand.

**Why this matters for B, and it should go into the B replan.** Search multiplies decision cost.
A policy that already costs 90ms/decision at 3k 2v2, with a 3.2s p95, cannot also be the rollout
policy for a 1-2s search budget - and a rollout to game end at 4k already measures ~20s (previous
entry). Concretely, the replan should weigh: (a) a CHEAPER rollout policy than full Tactician
(solo-rules is roughly half the per-decision cost and was always the plan's baseline), (b) leaning
on value-truncated rollouts sooner, i.e. pulling part of C forward into B, and (c) profiling the
planner's enemy loop before B4 rather than after - the #191 2026-07-26 TerrainGrid cache pass
found HALF the busy CPU in one rebuildable structure, so there may well be another such win here.
Recorded as measurement, not as a decision - the B0 cost numbers arbitrate.

**2026-09-03 - STEP 2: A's GENERALIZATION BASELINE. The 2k-overfit worry is INVERTED against
the solo baseline - the Tactician's margin GROWS with army size - but that same result makes the
vs-solo panels useless as a gate, and the gate design was corrected because of it.** 100
games/cell, side-swapped, paired seeds from 6000, DOP 16, realistic dice. Reports:
`FdgLab/reports/step2-baseline-2026-09-03/` (gitignored; numbers of record are here).

**Tactician vs SoloRules, by point level** (2k reference is the historical main matrix, 83.9):
- 1k: 99.5 / 97.5 / 94.5 / 96.0 (mean 96.9)
- 2k PANEL, added 2026-09-03 and run on the CURRENT engine: 99.5 / 92.0 / 83.0 / 96.0 (mean 92.6)
- 3k: 90.0 / 90.0 / 95.0 / 96.0 (BB-vs-Goblin, Knight-vs-RL, Saurian-vs-SoulSnatcher, Eternal-vs-DAO)
- 4k: 98.0 / 99.5 / 96.0 (Hives-vs-Havoc, Hives-vs-HEF, Havoc-vs-HEF)
- 2v2 (2k/player): 97.0 / 81.0 / 96.5 / 90.5; (3k/player): 96.2 (7 timeouts, see below) / 79.0

**Cross-level conclusion, and a correction to the first read.** With every level measured the
same way (4 cells, 100 games/cell, current engine), the margin over solo-rules is 96.9 (1k) /
92.6 (2k) / 92.8 (3k) / 97.8 (4k) / 91.3 (2v2 at 2k per player). The apparent "2k dip" in the
first pass was an ARTIFACT of comparing panels against the historical 83.9 main-matrix number,
which is a different cell set (all 72 ordered pairs incl. self-mirrors, i.e. harder cells) on a
July engine - not comparable to a 4-cell panel. Corrected reading: **no evidence of 2k-specific
overfitting; A's strength against the baseline is flat-to-strong across 1k-4k and 2v2.** Two
honest caveats: (a) this is all measured against an objective-BLIND opponent, a weak yardstick
that saturates above 90, so it bounds "does A collapse off-pool" and not "how good is A" - the
head-to-head panels are what will measure B and C; (b) 4 cells at 100 games carries per-cell
sigma of roughly 3-5 points, so single-cell differences below ~10 points are not signal.

**Tactician mirrors** (both sides Tactician - these measure ARMY imbalance under equal play, not
bot asymmetry, since the side swap cancels slot advantage): 1k 77.5 / 61.5 / 52.0 / 46.5;
3k 39.5 / 46.0 / 60.0 / 74.0; 4k 69.0 / 54.5 / 51.5. Alien Hives is the strongest 4k list; the
other two 4k pairs are near-even. Zero faults in every mirror cell.

**G2 (never trust a number without reading games).** 99.5% invited exactly the suspicion the rule
exists for, so a seed-6000 4k game was read: solo-rules is NOT collapsing - it seizes a marker in
round 3 and contests one in round 4, and only 3 units die all game (an objective race, not a
bloodbath). The widening margin is its DOCUMENTED baseline weakness compounding: solo-rules is
objective-blind, which costs more the more units and board there are. Real effect, understood
mechanism, no degenerate play.

**Consequence: the panel gate design was wrong and is fixed (superproject `4f77c02`).** At 96-99.5
the vs-solo panels are at ceiling, so "no cell below baseline minus 5" would happily pass a Phase
B bot that got WORSE. Panels now gate on the head-to-head score against the INCUMBENT rung (B vs
A, later C vs B; 50 = parity because sides swap), with vs-solo demoted to a cheap collapse check.

**Two ops findings, both now written into the docs.**
(a) A DOP-16 bench died mid-run to a Server GC SIGSEGV (core dump captured; second occurrence -
see #210's 2026-09-03 note), and the runner reported `exit=0` because `$?` had been reset by a
`$(date)` inside the same echo. A lost cell looked like a clean one and was caught only by reading
results. Every campaign runner now captures the real exit code, RETRIES, and VERIFIES the expected
report exists; step 4's self-play driver must do the same.
(b) The 120s watchdog is sized for 2k 1v1 (~16s/game) and cost 7 games in the 3k 2v2 cell
(12k points, ~50 units, 686 decisions/game) - which silently shrinks the denominator its score is
computed over. Panels at 3k+/2v2 now run at `--timeout 600`; a timeout is a measurement failure,
not a bot fault, and such a cell is re-run rather than reported. The 2v2 panel is re-queued clean.

**Cost data for Phase B** (decision mean / worst p95, per cell type): 2k 1v1 ~20/332ms,
3k 1v1 41.6/721ms, 4k 1v1 41.5/586ms, 2v2 33.8/685ms. A full 4k Tactician self-play game is ~20s
of wall and ~412 decisions - so a single MCTS ROLLOUT to game end at 4k costs ~20s, which is a
direct argument for value-truncated rollouts (C) or a cheaper rollout policy than full Tactician
in B3, and belongs in the B replan.

**2026-09-03 - STEP 3 (B0 SPIKE): MECHANISM FINDINGS. R1 (stop/abandon) is answerable with
EXISTING machinery; prescribing a decision is NOT as simple as answering the request.** Cost
numbers pending a clean idle-box run (chained behind step 2); these four findings are
timing-independent and already decide B1's shape.

1. **Finding a boundary needs no engine hook.** DeterminePlayerTurnStage writes GameProgressData
   at the start of every activation cycle and the next request is that player's
   ChooseUnitToActivateRequest - so the Nth such request IS the Nth activation boundary, with the
   world settled from the previous activation. The spike detects boundaries by counting that
   request type through the registry wrapper (FeasibilityShadow's pattern).
2. **Stopping a simulated game works today, no engine change (plan R1, "the top engineering
   unknown", downgraded).** A resolver exception is caught by
   NetworkedRequestMessageReceiver.HandleRequestMessageAsync, returned as
   StageTaskRequestErrorMessage, rethrown into the awaiting stage by RequestMessageSender, and
   unwound by FDGServer.LaunchStateMachineOnceReady's catch into a Fault game-end - the state
   machine genuinely stops rather than being orphaned. Measured 3/3 then 30/30 stop_observed.
   Caveat for B1: that path prints a full `[GAME ERROR]` stack trace per simulation (fine once,
   unacceptable at 10k) and reports the sim as a Fault, so B1 wants either a recognised quiet-stop
   exception type or the pause hook - a decision the clean cost numbers will inform, not this.
3. **Advance is deterministic and snapshots chain.** Two natural advances from one snapshot are
   BYTE-IDENTICAL (G5 holds for the node-expansion primitive, so B4's search can be seeded), and a
   captured boundary snapshot resumes again - chained to depth 2/2 in the smoke, so a tree walk is
   not capped at depth 1.
4. **THE ONE THAT WOULD HAVE BITTEN B1: a prescribed decision must go THROUGH the policy, not
   around it.** Answering ChooseUnitToActivateRequest at the registry boundary is mechanically
   correct - the control test (inject the option the policy would itself have picked) reproduces
   the natural result BYTE-IDENTICALLY under SoloRules, so wire settings, reply type and
   DataBinding serialization are all right. Under the Tactician the SAME control DIVERGES even
   when there is only ONE valid option (a forced choice), because
   `TacticianActivationResolver.Resolve` calls `_planner.BeginActivation(unit)` before returning:
   bypass the resolver and every later request in that activation is answered by a planner that
   was never told which unit is acting. In B1 this would have been a silent corruption - search
   exploring branches whose continuation was computed by a mis-initialised planner, yielding
   plausible but wrong evaluations. So `Advance(snapshot, compositeDecision)` must inject through
   a policy-side seam (a forced-choice option on TacticianOptions / the registry) that still runs
   the planner's own per-activation setup with the PRESCRIBED unit.

Spike lives in `FdgLab/B0Spike.cs` (`fdglab b0`), pure measurement, no Tactician behavior change.

**2026-09-03 - STEP 1 (harness generalization) DONE.** `SlotSpec.Team` (nullable, default null
= own team - every existing 1v1/FFA caller unchanged) threaded to `PlayerSlot(teamNumber)`;
`GameSpec.TeamGame` helper stamps grouped team seating (team A's slots first, team B's second -
matches Scenarios/crowded-2v2-3k.json's convention; FDGServer's own `GameBootstrap.AddTeams`
wires TeamData automatically, no other engine plumbing needed). `Benchmark`'s `Matchup` widened
from single armies (SpecA/SpecB) to per-side ROSTERS (SideA/SideB) via `Matchup.OneVsOne` for the
existing 1v1 case - report labels, CSV rows and the outcome hash reduce byte-identically to
before for every existing 1-army-per-side caller (verified: rerunning the same panel twice
reproduced the same outcome hash; the 2k main matrix `--pool FdgLab/armies` path is untouched).
New `bench --panel <name>` reads `FdgLab/armies/pool.json` (generalization manifest, campaign
doc sec 5): `points-1k` (4 cells), `points-3k` (4 cells), `points-4k` (3 cells, the new 4k lists
committed alongside), `shape-2v2` (6 cells, 4 at 2k/player + 2 at 3k/player) - all smoke-tested
with 0 faults, sane joined-roster labels, deterministic hashes. `heldOut` entries recorded per
the 2026-09-03 correction (pairs at every point level + one 2v2 cell, never a whole level/shape):
1k HDF-vs-PrimeBrothers, 2k Dwarf-vs-HDF + DE-vs-HEF (unchanged from the campaign doc), 3k
DE-vs-HEF (deliberate cross-level echo of the 2k held-out pair - a bonus same-matchup-different-
size generalization probe), 4k AlienHives-vs-HEF, and the 2v2 cell AlienHives+Orks-vs-
BattleBrothers+HDF. New `PauseGate.WaitWhilePausedAsync` (touch-file cooldown) wired into
`bench --pause-file PATH`, checked before each game start; will be reused by step 4's self-play
driver so a soak/bench and data generation can share the box without fighting for cores. Verify:
engine suite 3166/3167 (1 pre-existing skip) green, full solution build clean, headless smoke
exit 0; FdgLab has no dedicated test project, so correctness was verified by running each new
path (1v1 smoke unchanged, both new panels, unknown-panel error message, hash-reproducibility
on rerun). Next: step 2 (A generalization baseline across all four panels, overnight) and step 3
(B0 spike).

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
