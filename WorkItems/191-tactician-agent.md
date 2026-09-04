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
