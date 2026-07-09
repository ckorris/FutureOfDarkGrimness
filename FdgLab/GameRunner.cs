using System.Diagnostics;
using FDG;
using FDG.Ai;
using FDG.Data;
using FDG.GameModel;
using FDG.Players;
using FDG.SaveLoad;

namespace FdgLab;

/// <summary>
/// Runs one fully in-process AI-vs-AI game (#194): fresh store + bus + server per game, so games are
/// isolated and any number can run concurrently (#193 removed the shared-RNG hazard). Mirrors the
/// proven fresh-game assembly from the engine's DeterminismTests / CliApp.RunAsync, minus stdin.
/// </summary>
public static class GameRunner
{
    public static async Task<GameRecord> RunGameAsync(GameSpec spec)
    {
        var wall = Stopwatch.StartNew();
        var samples = new List<double>();
        var sampleLock = new object();

        var store = GameDataStore.GameDataStoreBuilder.GetDefault();
        var bus = new LabMessageBus();

        // One shared log (slot 0's view - the host broadcasts the same lines to every slot). The
        // tracer, when on, interleaves the log with every position write (#198 divergence hunting);
        // it must attach BEFORE the server constructor creates armies, or it misses the creations.
        List<string>? log = spec.CaptureLog || spec.Trace ? new List<string>() : null;
        var logLock = new object();
        GameTracer? tracer = null;
        if (spec.Trace)
        {
            tracer = new GameTracer();
            tracer.Attach(new TableState(store));
        }

        Action<string>? logSink = log == null ? null : line =>
        {
            lock (logLock) log.Add(line);
            tracer?.AddLog(line);
        };

        var slots = new PlayerSlot[spec.Slots.Count];
        for (int i = 0; i < slots.Length; i++)
        {
            SlotSpec slotSpec = spec.Slots[i];
            slots[i] = new PlayerSlot(i, teamNumber: i, new PlayerID(Guid.NewGuid()), slotSpec.Army, store);

            var aiGame = new FDGGame_AsLocal(store, bus);
            var registry = BuildRegistry(slotSpec.Profile, aiGame, slots[i].PlayerID, spec.Seed, i);
            var timed = new TimingRegistry(registry, samples, sampleLock);
            slots[i].AssignPlayerController(new LabPlayerController(
                $"{slotSpec.ArmyLabel} (slot {i})", slots[i].PlayerID, aiGame, timed,
                logSink: i == 0 ? logSink : null));
        }

        var settings = GameSettings.GetDefault();
        settings.RandomnessType = spec.Randomness;
        settings.DiceSeed = spec.Seed;

        var completed = new TaskCompletionSource<GameResult>(TaskCreationOptions.RunContinuationsAsynchronously);
        var server = new FDGServer(store, bus, settings, slots);
        server.OnGameCompleted += result => completed.TrySetResult(result);

        // Watchdog: a hung game (resolver deadlock, engine bug) must never wedge the fleet. The
        // abandoned game's tasks are simply orphaned - acceptable for a benchmark process, and exactly
        // the leak question the plan's B0 spike measures before search depends on mass simulation.
        Task first = await Task.WhenAny(completed.Task, Task.Delay(TimeSpan.FromSeconds(spec.WatchdogSeconds)));
        GameResult result = first == completed.Task
            ? await completed.Task
            : GameResult.ForFault($"watchdog: game exceeded {spec.WatchdogSeconds}s");

        wall.Stop();

        // Map the winning PlayerID back to its slot HERE, while the slots are in scope: PlayerIDs are
        // minted per game, so outside this method the GUID is meaningless (#193's slot-identity rule).
        int? winnerSlot = null;
        if (result.Winner.HasValue)
        {
            for (int i = 0; i < slots.Length; i++)
                if (slots[i].PlayerID.Equals(result.Winner.Value)) { winnerSlot = i; break; }
        }

        DecisionStats stats;
        lock (sampleLock) stats = DecisionStats.From(samples);
        IReadOnlyList<string>? capturedLog;
        lock (logLock) capturedLog = log?.ToArray();
        return new GameRecord(spec, result, wall.Elapsed, stats, winnerSlot, capturedLog, tracer?.Entries);
    }

    // The game seed goes in whole; the engine derives the per-player stream by slot ID (#193).
    private static FDG.StageResolution.IStageResolverRegistry BuildRegistry(
        EAiProfile profile, FDGGame_AsLocal aiGame, PlayerID playerID, int seed, int slotID) =>
        AiProfileFactory.BuildRegistry(profile, aiGame.TableState, playerID, seed, slotID);
}
