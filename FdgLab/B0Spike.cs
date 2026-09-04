using System.Diagnostics;
using FDG;
using FDG.Ai;
using FDG.Ai.Tactician;
using FDG.Data;
using FDG.GameModel;
using FDG.Players;
using FDG.SaveLoad;
using FDG.StageResolution;
using FDG.StageResolution.Requests;

namespace FdgLab;

/// <summary>
/// The B0 spike (docs/ai-agent-plan.md sec 9 B0; campaign step 3): pure measurement, no Tactician
/// behavior change. Answers the three questions Phase B's design depends on -
/// <list type="number">
/// <item>what does <see cref="GameSaveSerializer"/> Save/Load cost, in time and bytes, on real
/// mid-game states at 2k AND 4k (clone cost scales with unit count);</item>
/// <item>can a snapshot be resumed in-process, advanced EXACTLY one activation, and the next
/// activation-boundary state captured (the plan's node-expansion primitive);</item>
/// <item>can those simulation servers be stopped/abandoned without cumulative leaks (the plan's
/// R1, its top engineering risk).</item>
/// </list>
/// <para>
/// Boundary detection rides on the engine's own rolling save point: DeterminePlayerTurnStage writes
/// GameProgressData at the start of every activation cycle, and the very next request is this
/// player's ChooseUnitToActivateRequest. So the Nth such request IS the Nth activation boundary,
/// with the world fully settled from the previous activation - no engine hook needed to FIND a
/// boundary.
/// </para>
/// <para>
/// Two stop modes are measured against each other. THROW rides an existing engine path: a resolver
/// exception is caught by NetworkedRequestMessageReceiver, returned as StageTaskRequestErrorMessage,
/// rethrown into the awaiting stage, and unwound by FDGServer's own catch into a Fault game-end -
/// i.e. the state machine genuinely stops. ABANDON is the pattern GameRunner's watchdog already
/// relies on (orphan the tasks and walk away), which leaves the simulated game RUNNING in the
/// background. The soak measures what each costs across many simulations.
/// </para>
/// </summary>
public static class B0Spike
{
    /// <summary>Thrown from a resolver at the target boundary to unwind the simulated game.</summary>
    public sealed class StopSignal : Exception
    {
        public StopSignal() : base("b0: simulation stop signal") { }
    }

    public static async Task<int> RunAsync(string[] args)
    {
        string armyA = Arg(args, "--a") ?? "FdgLab/armies/Alien Hives 2k - Horde Melee.fdgarmy";
        string armyB = Arg(args, "--b") ?? "FdgLab/armies/Battle Brothers 2k - Elite Shooting.fdgarmy";
        string label = Arg(args, "--label") ?? "2k";
        int boundary = IntArg(args, "--boundary", 20);
        int roundTrips = IntArg(args, "--round-trips", 20);
        int advances = IntArg(args, "--advances", 20);
        int soak = IntArg(args, "--soak", 0);
        int chain = IntArg(args, "--chain", 8);
        int rollouts = IntArg(args, "--rollouts", 5);
        int timeoutSeconds = IntArg(args, "--timeout", 60);
        EAiProfile profile = (Arg(args, "--profile") ?? "tactician").ToLowerInvariant() switch
        {
            "solorules" => EAiProfile.SoloRules,
            "gunline" => EAiProfile.Gunline,
            _ => EAiProfile.Tactician,
        };

        Console.WriteLine($"=== B0 spike [{label}] profile={profile} boundary={boundary} ===");
        Console.WriteLine($"  A: {Path.GetFileNameWithoutExtension(armyA)}");
        Console.WriteLine($"  B: {Path.GetFileNameWithoutExtension(armyB)}");

        // --- Phase 1: capture a real mid-game activation-boundary snapshot -----------------------
        var captureWall = Stopwatch.StartNew();
        (string? snapshot, string captureNote) = await CaptureBoundarySnapshotAsync(
            armyA, armyB, profile, boundary, seed: 4242, timeoutSeconds);
        captureWall.Stop();

        if (snapshot == null)
        {
            Console.Error.WriteLine($"FAILED to capture a boundary snapshot: {captureNote}");
            return 1;
        }

        Console.WriteLine($"\n[1] Capture: {captureNote} in {captureWall.Elapsed.TotalSeconds:F1}s");
        Console.WriteLine($"    Snapshot size: {snapshot.Length / 1024.0:F1} KiB ({snapshot.Length} chars)");

        // --- Phase 2: Save/Load round-trip cost --------------------------------------------------
        var loadMs = new List<double>();
        var saveMs = new List<double>();
        for (int i = 0; i < roundTrips; i++)
        {
            var sw = Stopwatch.StartNew();
            GameDataStore store = GameSaveSerializer.Load(snapshot);
            sw.Stop();
            loadMs.Add(sw.Elapsed.TotalMilliseconds);

            sw.Restart();
            string reSaved = GameSaveSerializer.Save(store);
            sw.Stop();
            saveMs.Add(sw.Elapsed.TotalMilliseconds);

            if (i == 0)
                Console.WriteLine($"    Re-save size: {reSaved.Length / 1024.0:F1} KiB " +
                                  $"(delta {reSaved.Length - snapshot.Length} chars)");
        }
        Console.WriteLine($"\n[2] Round trip over {roundTrips} iterations:");
        Console.WriteLine($"    Load: {Describe(loadMs)}");
        Console.WriteLine($"    Save: {Describe(saveMs)}");
        Console.WriteLine($"    Clone (load+save): mean {loadMs.Average() + saveMs.Average():F1}ms");

        // --- Phase 3: advance exactly one activation, both stop modes ----------------------------
        foreach (bool throwToStop in new[] { true, false })
        {
            string mode = throwToStop ? "THROW (unwind via resolver exception)" : "ABANDON (orphan the tasks)";
            Console.WriteLine($"\n[3] Advance one activation x{advances} - stop mode: {mode}");

            var totalMs = new List<double>();
            var loadPart = new List<double>();
            var assemblePart = new List<double>();
            var runPart = new List<double>();
            var savePart = new List<double>();
            int reachedBoundary = 0, gameEnded = 0, timedOut = 0, stoppedCleanly = 0;

            for (int i = 0; i < advances; i++)
            {
                AdvanceResult r = await AdvanceOneActivationAsync(snapshot, profile, throwToStop, timeoutSeconds);
                if (r.ReachedBoundary) reachedBoundary++;
                if (r.GameEndedFirst) gameEnded++;
                if (r.TimedOut) timedOut++;
                if (r.StopObserved) stoppedCleanly++;
                if (r.ReachedBoundary)
                {
                    totalMs.Add(r.TotalMs);
                    loadPart.Add(r.LoadMs);
                    assemblePart.Add(r.AssembleMs);
                    runPart.Add(r.RunMs);
                    savePart.Add(r.SaveMs);
                }
            }

            Console.WriteLine($"    reached_boundary={reachedBoundary}/{advances} " +
                              $"game_ended_first={gameEnded} timed_out={timedOut} stop_observed={stoppedCleanly}");
            if (totalMs.Count > 0)
            {
                Console.WriteLine($"    total   : {Describe(totalMs)}");
                Console.WriteLine($"      load  : {Describe(loadPart)}");
                Console.WriteLine($"      assemb: {Describe(assemblePart)}");
                Console.WriteLine($"      run   : {Describe(runPart)}");
                Console.WriteLine($"      save  : {Describe(savePart)}");
                Console.WriteLine($"    reusable-server ceiling (run only, if a paused server could be " +
                                  $"stepped instead of rebuilt): mean {runPart.Average():F0}ms => " +
                                  $"{DecisionBand(runPart.Average())}");
                Console.WriteLine($"    -> plan decision table: node expansion mean " +
                                  $"{totalMs.Average():F0}ms => {DecisionBand(totalMs.Average())}");
            }
        }

        // --- Phase 3b: chained advances ----------------------------------------------------------
        // The tree-search question the single-advance numbers do NOT answer: does a captured
        // boundary snapshot resume again? MCTS walks several plies down a path, each expansion
        // starting from the previous one's output, so a snapshot that cannot itself be advanced
        // would cap the tree at depth 1.
        Console.WriteLine($"\n[3b] Chained advances (each from the previous capture) x{chain}");
        string chained = snapshot;
        int depth = 0;
        var chainMs = new List<double>();
        for (int i = 0; i < chain; i++)
        {
            var sw = Stopwatch.StartNew();
            (string? next, string note) = await AdvanceCapturingAsync(chained, profile, timeoutSeconds);
            sw.Stop();
            if (next == null)
            {
                Console.WriteLine($"    depth {i + 1}: STOPPED - {note}");
                break;
            }
            chained = next;
            depth++;
            chainMs.Add(sw.Elapsed.TotalMilliseconds);
            Console.WriteLine($"    depth {i + 1}: ok, {next.Length / 1024.0:F1} KiB, " +
                              $"{sw.Elapsed.TotalMilliseconds:F0}ms, round {RoundOf(next)}");
        }
        Console.WriteLine($"    chained depth reached: {depth}/{chain}" +
                          (chainMs.Count > 0 ? $" | {Describe(chainMs)}" : ""));

        // --- Phase 3c: determinism + prescribed-decision injection -------------------------------
        // Three things B1 needs that the cost numbers do not establish: that advancing the SAME
        // snapshot twice gives the SAME result (reproducible search, G5), that the harness can
        // PRESCRIBE a decision rather than accept the AI's (search explores chosen branches), and
        // that prescribing actually steers the game (a no-op injection would look like success).
        Console.WriteLine("\n[3c] Determinism and decision injection");
        (string? natural1, string n1) = await AdvanceCapturingAsync(snapshot, profile, timeoutSeconds);
        (string? natural2, string n2) = await AdvanceCapturingAsync(snapshot, profile, timeoutSeconds);
        (string? injected, string n3) = await AdvanceCapturingAsync(snapshot, profile, timeoutSeconds,
            injectAt: 1, mode: EInjectMode.SeamLast);
        // CONTROL (#191 B1 5b): prescribe the option the policy would have chosen anyway, THROUGH
        // the planner's prescription seam. A sound prescription must reproduce the natural result
        // byte for byte.
        (string? control, string n4) = await AdvanceCapturingAsync(snapshot, profile, timeoutSeconds,
            injectAt: 1, mode: EInjectMode.SeamFirst);
        // The same control answered at the registry/wire boundary instead - B0 finding 4's witness.
        // Under the Tactician this SKIPS TacticianActivationResolver, so BeginActivation never runs
        // and the rest of the activation is answered by a planner that does not know its unit. Kept
        // so the divergence stays measured rather than remembered.
        (string? bypass, string n5) = await AdvanceCapturingAsync(snapshot, profile, timeoutSeconds,
            injectAt: 1, mode: EInjectMode.WireFirst);

        if (natural1 == null || natural2 == null)
        {
            Console.WriteLine($"    inconclusive - natural advance failed ({n1} / {n2})");
        }
        else
        {
            bool deterministic = natural1 == natural2;
            Console.WriteLine($"    determinism: two natural advances {(deterministic ? "MATCH" : "DIFFER")}" +
                              $" -> {(deterministic ? "reproducible (G5 holds for Advance)" : "NOT reproducible - B4 search cannot be seeded")}");
            if (injected == null)
                Console.WriteLine($"    injection: FAILED - {n3}");
            else
                Console.WriteLine($"    injection (last option): accepted, {n3}; result " +
                                  $"{(injected != natural1 ? "DIFFERS from natural" : "IDENTICAL to natural")}");

            if (control == null)
                Console.WriteLine($"    prescription control: FAILED - {n4}");
            else
                Console.WriteLine($"    prescription control (policy's own pick THROUGH the seam, {n4}): " +
                    (control == natural1
                        ? "IDENTICAL to natural -> the 5b seam reproduces natural play (PIN)"
                        : "DIFFERS from natural -> THE SEAM IS NOT FAITHFUL; nothing built on it can be trusted"));

            if (bypass == null)
                Console.WriteLine($"    wire-bypass witness: FAILED - {n5}");
            else
                Console.WriteLine($"    wire-bypass witness (same pick answered at the boundary, {n5}): " +
                    (bypass == natural1
                        ? "identical to natural -> this policy keeps no per-activation state"
                        : "DIFFERS from natural -> B0 finding 4 still holds; prescription must go " +
                          "THROUGH the policy (this is why the seam exists)"));
        }

        // --- Phase 3d: rollout-to-game-end cost --------------------------------------------------
        // The number that decides B3. The plan specifies rollouts to game end as the leaf estimate;
        // if one rollout costs many multiples of a node expansion, that is unaffordable per leaf and
        // the leaf estimate has to be an EVALUATOR instead (which is what Phase C then improves).
        // Measured, not inferred from bench throughput (G6).
        if (rollouts > 0)
        {
            Console.WriteLine($"\n[3d] Rollout to game end x{rollouts} (from the same mid-game snapshot)");
            var rollMs = new List<double>();
            int completed = 0;
            for (int i = 0; i < rollouts; i++)
            {
                var sw = Stopwatch.StartNew();
                bool ok = await RolloutToEndAsync(snapshot, profile, timeoutSeconds);
                sw.Stop();
                if (ok) { completed++; rollMs.Add(sw.Elapsed.TotalMilliseconds); }
            }
            if (rollMs.Count > 0)
            {
                Console.WriteLine($"    completed={completed}/{rollouts} | {Describe(rollMs)}");
                Console.WriteLine($"    ONE rollout costs {rollMs.Average() / Math.Max(1, 1):F0}ms " +
                                  $"= {rollMs.Average() / 243.0:F0}x a measured node expansion (2k reference 243ms)");
            }
            else Console.WriteLine($"    no rollout completed within {timeoutSeconds}s");
        }

        // --- Phase 4: leak soak ------------------------------------------------------------------
        if (soak > 0)
        {
            foreach (bool throwToStop in new[] { true, false })
            {
                string mode = throwToStop ? "THROW" : "ABANDON";
                Console.WriteLine($"\n[4] Soak: {soak} simulations, stop mode {mode}");
                await SoakAsync(snapshot, profile, throwToStop, soak, timeoutSeconds);
            }
        }

        Console.WriteLine("\n=== B0 spike complete ===");
        return 0;
    }

    // ---------------------------------------------------------------------------------------------

    private sealed record AdvanceResult(bool ReachedBoundary, bool GameEndedFirst, bool TimedOut,
        bool StopObserved, double TotalMs, double LoadMs, double AssembleMs, double RunMs, double SaveMs);

    /// <summary>
    /// Plays a fresh game until the Nth activation boundary and returns the store serialized at that
    /// exact moment. Stops the game by throwing from the resolver (which also gives the throw-stop
    /// mechanism its first live test).
    /// </summary>
    private static async Task<(string? Snapshot, string Note)> CaptureBoundarySnapshotAsync(
        string armyA, string armyB, EAiProfile profile, int boundary, int seed, int timeoutSeconds)
    {
        var store = GameDataStore.GameDataStoreBuilder.GetDefault();
        var bus = new LabMessageBus();
        var captured = new TaskCompletionSource<string>(TaskCreationOptions.RunContinuationsAsynchronously);

        var watcher = new BoundaryWatcher(targetOccurrence: boundary, onBoundary: () =>
        {
            captured.TrySetResult(GameSaveSerializer.Save(store));
        }, throwAfter: true);

        SlotSpec specA = Armies.LoadSlot(armyA) with { Profile = profile };
        SlotSpec specB = Armies.LoadSlot(armyB) with { Profile = profile };
        var slotSpecs = new[] { specA, specB };

        var slots = new PlayerSlot[2];
        for (int i = 0; i < 2; i++)
        {
            slots[i] = new PlayerSlot(i, teamNumber: i, new PlayerID(Guid.NewGuid()), slotSpecs[i].Army, store);
            var aiGame = new FDGGame_AsLocal(store, bus);
            IStageResolverRegistry registry = AiProfileFactory.BuildRegistry(
                slotSpecs[i].Profile, aiGame.TableState, slots[i].PlayerID, seed, i);
            slots[i].AssignPlayerController(new LabPlayerController(
                $"slot {i}", slots[i].PlayerID, aiGame, watcher.Wrap(registry)));
        }

        GameSettings settings = GameSettings.GetDefault();
        settings.RandomnessType = ERandomnessType.Realistic;
        settings.DiceSeed = seed;

        var ended = new TaskCompletionSource<GameResult>(TaskCreationOptions.RunContinuationsAsynchronously);
        var server = new FDGServer(store, bus, settings, slots);
        server.OnGameCompleted += r => ended.TrySetResult(r);

        Task finished = await Task.WhenAny(captured.Task, ended.Task,
            Task.Delay(TimeSpan.FromSeconds(timeoutSeconds)));

        if (finished == captured.Task)
        {
            string snapshot = await captured.Task;
            // Give the throw-unwind a moment so we can report whether the game actually stopped.
            Task stopRace = await Task.WhenAny(ended.Task, Task.Delay(TimeSpan.FromSeconds(5)));
            string stopped = stopRace == ended.Task
                ? $"game stopped ({(await ended.Task).Outcome})"
                : "game did NOT stop within 5s";
            return (snapshot, $"boundary {boundary} reached, {stopped}");
        }

        if (finished == ended.Task)
            return (null, $"game ended before boundary {boundary}: {(await ended.Task).ToSummaryLine()}");

        return (null, $"timed out after {timeoutSeconds}s before boundary {boundary}");
    }

    /// <summary>
    /// The node-expansion primitive under test: load the snapshot, resume it in-process, let exactly
    /// ONE activation play out, and capture the state at the next activation boundary.
    /// <para>
    /// On resume the state machine re-enters at MainPhaseRoundStage -> DeterminePlayerTurnStage,
    /// so the FIRST ChooseUnitToActivateRequest is the replay of the snapshotted activation and the
    /// SECOND is the next boundary - which is why the watcher targets occurrence 2.
    /// </para>
    /// </summary>
    private static async Task<AdvanceResult> AdvanceOneActivationAsync(string snapshot, EAiProfile profile,
        bool throwToStop, int timeoutSeconds)
    {
        var total = Stopwatch.StartNew();

        var loadSw = Stopwatch.StartNew();
        GameDataStore store = GameSaveSerializer.Load(snapshot);
        loadSw.Stop();

        double saveMs = 0;
        var captured = new TaskCompletionSource<string>(TaskCreationOptions.RunContinuationsAsynchronously);
        var watcher = new BoundaryWatcher(targetOccurrence: 2, onBoundary: () =>
        {
            var saveSw = Stopwatch.StartNew();
            string next = GameSaveSerializer.Save(store);
            saveSw.Stop();
            saveMs = saveSw.Elapsed.TotalMilliseconds;
            captured.TrySetResult(next);
        }, throwAfter: throwToStop);

        var ended = new TaskCompletionSource<GameResult>(TaskCreationOptions.RunContinuationsAsynchronously);
        // Assembly (rebuilding slots, registries, RuleResolver, the server) is timed apart from the
        // activation itself: if assembly dominates, the lever is a REUSABLE simulation server (the
        // plan's pause/step hook), not a faster serializer.
        var assembleSw = Stopwatch.StartNew();
        BuildResumedServer(store, profile, watcher, ended);
        assembleSw.Stop();

        var runSw = Stopwatch.StartNew();
        Task finished = await Task.WhenAny(captured.Task, ended.Task,
            Task.Delay(TimeSpan.FromSeconds(timeoutSeconds)));
        runSw.Stop();
        total.Stop();

        if (finished == captured.Task)
        {
            bool stopObserved = false;
            if (throwToStop)
            {
                Task stopRace = await Task.WhenAny(ended.Task, Task.Delay(TimeSpan.FromSeconds(5)));
                stopObserved = stopRace == ended.Task;
            }
            return new AdvanceResult(true, false, false, stopObserved,
                total.Elapsed.TotalMilliseconds, loadSw.Elapsed.TotalMilliseconds,
                assembleSw.Elapsed.TotalMilliseconds, runSw.Elapsed.TotalMilliseconds - saveMs, saveMs);
        }

        if (finished == ended.Task)
            return new AdvanceResult(false, true, false, true, total.Elapsed.TotalMilliseconds, 0, 0, 0, 0);

        return new AdvanceResult(false, false, true, false, total.Elapsed.TotalMilliseconds, 0, 0, 0, 0);
    }

    /// <summary>
    /// One advance that RETURNS the captured snapshot (Phase 3b's chaining), throw-stopped.
    /// </summary>
    private static async Task<(string? Snapshot, string Note)> AdvanceCapturingAsync(string snapshot,
        EAiProfile profile, int timeoutSeconds, int injectAt = 0,
        EInjectMode mode = EInjectMode.None)
    {
        GameDataStore store = GameSaveSerializer.Load(snapshot);
        var captured = new TaskCompletionSource<string>(TaskCreationOptions.RunContinuationsAsynchronously);
        var watcher = new BoundaryWatcher(targetOccurrence: 2,
            onBoundary: () => captured.TrySetResult(GameSaveSerializer.Save(store)), throwAfter: true,
            injectAtOccurrence: injectAt, mode: mode);

        var ended = new TaskCompletionSource<GameResult>(TaskCreationOptions.RunContinuationsAsynchronously);
        BuildResumedServer(store, profile, watcher, ended);

        Task finished = await Task.WhenAny(captured.Task, ended.Task,
            Task.Delay(TimeSpan.FromSeconds(timeoutSeconds)));
        if (finished == captured.Task)
            return (await captured.Task, watcher.InjectedOptionCount >= 0
                ? $"ok (injected over {watcher.InjectedOptionCount} options)" : "ok");
        if (finished == ended.Task) return (null, $"game ended: {(await ended.Task).ToSummaryLine()}");
        return (null, $"timed out after {timeoutSeconds}s");
    }

    /// <summary>Resume a snapshot and let it play to its natural end (a Phase-B rollout).</summary>
    private static async Task<bool> RolloutToEndAsync(string snapshot, EAiProfile profile, int timeoutSeconds)
    {
        GameDataStore store = GameSaveSerializer.Load(snapshot);
        // Target occurrence int.MaxValue = never stop at a boundary; the game just runs to the end.
        var watcher = new BoundaryWatcher(targetOccurrence: int.MaxValue, onBoundary: () => { },
            throwAfter: false);
        var ended = new TaskCompletionSource<GameResult>(TaskCreationOptions.RunContinuationsAsynchronously);
        BuildResumedServer(store, profile, watcher, ended);
        Task finished = await Task.WhenAny(ended.Task, Task.Delay(TimeSpan.FromSeconds(timeoutSeconds)));
        return finished == ended.Task;
    }

    /// <summary>Round number recorded in a snapshot's GameProgressData, for the chain trace.</summary>
    private static string RoundOf(string snapshot)
    {
        try
        {
            GameDataStore store = GameSaveSerializer.Load(snapshot);
            GameProgressData? progress = GameProgressUtilities.TryGetProgress(store);
            return progress?.RoundCount.ToString() ?? "?";
        }
        catch { return "?"; }
    }

    /// <summary>
    /// FdgLab's port of ScenarioLauncher.BuildResume (which lives app-side, and FdgLab depends on the
    /// engine only): rebuild player slots on the SAVED PlayerIDs, all AI, and enter the resume ctor.
    /// B1 productionizes this engine-side as SimulationService.
    /// </summary>
    private static FDGServer BuildResumedServer(GameDataStore store, EAiProfile profile,
        BoundaryWatcher watcher, TaskCompletionSource<GameResult> ended)
    {
        List<PlayerSlotInfo> savedInfos = store.GetAllValues<PlayerSlotInfo>()
            .OrderBy(info => info.SlotID).ToList();
        if (savedInfos.Count == 0)
            throw new InvalidOperationException("b0: snapshot carries no player slots.");

        GameProgressData? progress = GameProgressUtilities.TryGetProgress(store);
        int? seed = progress?.Settings.DiceSeed;
        bool seeThrough = progress?.Settings.SeeThroughFriendlyUnits ?? false;

        foreach (DataReference oldInfo in store.GetAllDataReferences<PlayerSlotInfo>().ToList())
            store.Destroy(oldInfo);

        var bus = new LabMessageBus();
        var slots = new PlayerSlot[savedInfos.Count];
        for (int i = 0; i < savedInfos.Count; i++)
        {
            slots[i] = new PlayerSlot(i, savedInfos[i].TeamNumber, savedInfos[i].PlayerID,
                new ArmyListFile(), store);
            var aiGame = new FDGGame_AsLocal(store, bus);
            // #191 B1 5b: the slot's planner is what a prescribed decision is set on, so the
            // watcher's control arm can steer THROUGH the policy instead of around it.
            IStageResolverRegistry registry = AiProfileFactory.BuildRegistry(
                profile, aiGame.TableState, savedInfos[i].PlayerID, out TacticianPlanner? planner,
                seed, slots[i].SlotID, decisionLog: null, seeThroughFriendlyUnits: seeThrough);
            slots[i].AssignPlayerController(new LabPlayerController(
                $"slot {i}", savedInfos[i].PlayerID, aiGame, watcher.Wrap(registry, planner)));
        }

        var server = new FDGServer(store, bus, slots);
        server.OnGameCompleted += r => ended.TrySetResult(r);
        return server;
    }

    private static async Task SoakAsync(string snapshot, EAiProfile profile, bool throwToStop,
        int iterations, int timeoutSeconds)
    {
        var proc = Process.GetCurrentProcess();
        long baseHeap = GC.GetTotalMemory(forceFullCollection: true);
        proc.Refresh();
        long baseRss = proc.WorkingSet64;
        var wall = Stopwatch.StartNew();
        int boundaries = 0, misses = 0;

        Console.WriteLine($"    start: heap {Mib(baseHeap)} rss {Mib(baseRss)}");
        int sampleEvery = Math.Max(1, iterations / 10);

        for (int i = 1; i <= iterations; i++)
        {
            AdvanceResult r = await AdvanceOneActivationAsync(snapshot, profile, throwToStop, timeoutSeconds);
            if (r.ReachedBoundary) boundaries++; else misses++;

            if (i % sampleEvery == 0 || i == iterations)
            {
                long heapNoCollect = GC.GetTotalMemory(forceFullCollection: false);
                long heapCollected = GC.GetTotalMemory(forceFullCollection: true);
                proc.Refresh();
                Console.WriteLine($"    {i,6}/{iterations}: heap {Mib(heapNoCollect)} " +
                                  $"(after GC {Mib(heapCollected)}) rss {Mib(proc.WorkingSet64)} " +
                                  $"threads {proc.Threads.Count} elapsed {wall.Elapsed.TotalSeconds:F0}s");
            }
        }

        wall.Stop();
        long endHeap = GC.GetTotalMemory(forceFullCollection: true);
        proc.Refresh();
        Console.WriteLine($"    end: heap {Mib(endHeap)} (delta {Mib(endHeap - baseHeap)}) " +
                          $"rss {Mib(proc.WorkingSet64)} (delta {Mib(proc.WorkingSet64 - baseRss)})");
        Console.WriteLine($"    boundaries={boundaries} misses={misses} " +
                          $"throughput={iterations / Math.Max(0.001, wall.Elapsed.TotalSeconds):F1} sims/s");
    }

    // ---------------------------------------------------------------------------------------------

    /// <summary>
    /// Counts ChooseUnitToActivateRequest occurrences (the first request after each activation
    /// boundary) and fires once at the target one. Local games deliver through the JSON path, so
    /// that override is the one that matters; the typed path is covered for completeness.
    /// </summary>
    /// <summary>
    /// How a prescribed decision reaches the game (#191 B1 5b).
    /// </summary>
    internal enum EInjectMode
    {
        /// <summary>No injection - the AI chooses.</summary>
        None,
        /// <summary>Through the planner's prescription seam: the LAST option (the steering test).</summary>
        SeamLast,
        /// <summary>Through the seam: the option the policy would itself have picked (the control).</summary>
        SeamFirst,
        /// <summary>
        /// Answered at the registry/wire boundary, bypassing the resolver - what B0 measured
        /// diverging. Kept as the regression witness for finding 4, not as a supported path.
        /// </summary>
        WireFirst,
    }

    private sealed class BoundaryWatcher
    {
        private readonly int _target;
        private readonly Action _onBoundary;
        private readonly bool _throwAfter;
        private readonly int _injectAt;
        private readonly EInjectMode _mode;
        private int _seen;
        private int _injectedOptionCount = -1;

        /// <summary>How many options the injected decision had (-1 = injection never fired).</summary>
        public int InjectedOptionCount => _injectedOptionCount;

        public BoundaryWatcher(int targetOccurrence, Action onBoundary, bool throwAfter,
            int injectAtOccurrence = 0, EInjectMode mode = EInjectMode.None)
        {
            _target = targetOccurrence;
            _onBoundary = onBoundary;
            _throwAfter = throwAfter;
            _injectAt = injectAtOccurrence;
            _mode = mode;
        }

        /// <param name="planner">
        /// This slot's planner (null for non-Tactician profiles). #191 B1 5b: a prescribed decision
        /// is set on the planner and then travels the NORMAL resolver path, so the policy's own
        /// per-activation setup still runs. Only <see cref="EInjectMode.WireFirst"/> answers at the
        /// boundary, and it exists to keep B0 finding 4 visible.
        /// </param>
        public IStageResolverRegistry Wrap(IStageResolverRegistry inner, TacticianPlanner? planner = null) =>
            new WatchRegistry(inner, this, planner);

        // Returns a reply JSON to inject, or null to let the inner registry answer normally.
        private string? Observe(string? requestJson, IReadableGameDataStore store, TacticianPlanner? planner)
        {
            int seen = Interlocked.Increment(ref _seen);

            if (seen == _injectAt && _injectAt > 0 && requestJson != null && _mode != EInjectMode.None)
            {
                // Mirror StageResolverRegistry.ResolveRequestAsJson_Typed exactly: wire settings for
                // BOTH directions, and serialize against the declared reply type so TypeNameHandling
                // records what the awaiting stage expects.
                Newtonsoft.Json.JsonSerializerSettings wire = FDG.Network.WireJsonSettings.For(store);
                var request = Newtonsoft.Json.JsonConvert
                    .DeserializeObject<ChooseUnitToActivateRequest>(requestJson, wire);
                if (request != null && request.ValidOptions.Count > 0)
                {
                    _injectedOptionCount = request.ValidOptions.Count;
                    // FIRST option is the control: it is what the policy would pick anyway, so a
                    // sound prescription must reproduce the natural result exactly. LAST is the
                    // steering test.
                    DataBinding<UnitData> chosen = _mode == EInjectMode.SeamLast
                        ? request.ValidOptions[^1].Option
                        : request.ValidOptions[0].Option;

                    if (_mode == EInjectMode.WireFirst)
                    {
                        return Newtonsoft.Json.JsonConvert.SerializeObject(
                            chosen, typeof(DataBinding<UnitData>), wire);
                    }

                    // The seam: hand the decision to the policy and let the request through. The
                    // deserialized binding is a different instance from the engine's, which is why
                    // the resolver matches prescriptions by DataReference.
                    planner?.Prescribe(chosen);
                    return null;
                }
            }

            if (seen != _target) return null;
            _onBoundary();
            if (_throwAfter) throw new StopSignal();
            return null;
        }

        private sealed class WatchRegistry : IStageResolverRegistry
        {
            private readonly IStageResolverRegistry _inner;
            private readonly BoundaryWatcher _watcher;
            private readonly TacticianPlanner? _planner;

            public WatchRegistry(IStageResolverRegistry inner, BoundaryWatcher watcher,
                TacticianPlanner? planner)
            {
                _inner = inner;
                _watcher = watcher;
                _planner = planner;
            }

            public IStageResolverRegistry RegisterResolver<TRequest, TReply>(IStageResolver<TRequest, TReply> resolver)
                where TRequest : IStageTaskRequest<TReply>
            {
                _inner.RegisterResolver(resolver);
                return this;
            }

            public Task<TReply> ResolveRequest<TRequest, TReply>(TRequest request)
                where TRequest : IStageTaskRequest<TReply>
            {
                if (request is ChooseUnitToActivateRequest) _watcher.Observe(null, null!, _planner);
                return _inner.ResolveRequest<TRequest, TReply>(request);
            }

            public Task<string> ResolveRequestAsJson(string typeFullName, string requestJson,
                IReadableGameDataStore gameDataStore)
            {
                if (typeFullName == typeof(ChooseUnitToActivateRequest).FullName)
                {
                    string? injected = _watcher.Observe(requestJson, gameDataStore, _planner);
                    if (injected != null) return Task.FromResult(injected);
                }
                return _inner.ResolveRequestAsJson(typeFullName, requestJson, gameDataStore);
            }
        }
    }

    private static string DecisionBand(double meanMs) => meanMs switch
    {
        < 30 => "FULL MCTS (hundreds of nodes at a 5-10s budget)",
        <= 200 => "MCTS with small node counts, leaning on the evaluator",
        _ => "optimize the snapshot path before proceeding (plan G6)",
    };

    private static string Describe(IReadOnlyList<double> ms)
    {
        if (ms.Count == 0) return "no samples";
        var sorted = ms.OrderBy(x => x).ToArray();
        double p95 = sorted[(int)Math.Min(sorted.Length - 1, Math.Ceiling(sorted.Length * 0.95) - 1)];
        return $"mean {sorted.Average():F1}ms | median {sorted[sorted.Length / 2]:F1}ms | " +
               $"p95 {p95:F1}ms | min {sorted[0]:F1}ms | max {sorted[^1]:F1}ms";
    }

    private static string Mib(long bytes) => $"{bytes / 1024.0 / 1024.0:F0}MiB";

    private static string? Arg(string[] args, string name)
    {
        int i = Array.IndexOf(args, name);
        return i >= 0 && i + 1 < args.Length ? args[i + 1] : null;
    }

    private static int IntArg(string[] args, string name, int fallback) =>
        int.TryParse(Arg(args, name), out int value) ? value : fallback;
}
