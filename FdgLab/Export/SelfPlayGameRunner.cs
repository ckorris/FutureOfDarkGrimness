using FDG;
using FDG.Ai;
using FDG.Ai.Tactician;
using FDG.Data;
using FDG.GameModel;
using FDG.Players;
using FDG.Rules.Dispatch;
using FDG.SaveLoad;
using FDG.StageResolution;

namespace FdgLab.Export;

/// <summary>
/// Runs one in-process AI-vs-AI game with a <see cref="ExportingRegistry"/> wrapped around every
/// slot's resolvers (#191 step 4). A dedicated runner rather than a <see cref="GameRunner"/>
/// registry-wrapper reuse: the exporter needs each slot's PlayerID, slot index, and (for a
/// Tactician profile) the TacticianPlanner instance, none of which GameRunner's generic wrapper
/// hook exposes - duplicating the fresh-store-per-game assembly here keeps that hook's existing
/// callers (TimingRegistry, FeasibilityShadow) untouched.
/// </summary>
public static class SelfPlayGameRunner
{
    public static async Task<(GameResult Result, GameExportState Export, IReadOnlyList<PlayerID> SlotPlayerIDs,
        IReadOnlyList<int> SlotTeams)> RunGameAsync(GameSpec spec, bool entitySampled, float totalGamePoints)
    {
        var store = GameDataStore.GameDataStoreBuilder.GetDefault();
        var bus = new LabMessageBus();
        var exportState = new GameExportState(entitySampled);

        var slots = new PlayerSlot[spec.Slots.Count];
        for (int i = 0; i < slots.Length; i++)
        {
            SlotSpec slotSpec = spec.Slots[i];
            slots[i] = new PlayerSlot(i, teamNumber: slotSpec.Team ?? i, new PlayerID(Guid.NewGuid()), slotSpec.Army, store);

            var aiGame = new FDGGame_AsLocal(store, bus);
            IStageResolverRegistry inner = AiProfileFactory.BuildRegistry(slotSpec.Profile, aiGame.TableState,
                slots[i].PlayerID, out TacticianPlanner? planner, spec.Seed, i);
            var queryEvaluator = new RuleEvaluator(new ProbabilisticDiceRoller());
            var exportRegistry = new ExportingRegistry(inner, exportState, slots[i].PlayerID, i,
                () => aiGame.TableState, queryEvaluator, planner, totalGamePoints);
            slots[i].AssignPlayerController(new LabPlayerController(
                $"{slotSpec.ArmyLabel} (slot {i})", slots[i].PlayerID, aiGame, exportRegistry));
        }

        var settings = GameSettings.GetDefault();
        settings.RandomnessType = spec.Randomness;
        settings.DiceSeed = spec.Seed;

        var completed = new TaskCompletionSource<GameResult>(TaskCreationOptions.RunContinuationsAsynchronously);
        var server = new FDGServer(store, bus, settings, slots);
        server.OnGameCompleted += result => completed.TrySetResult(result);

        Task first = await Task.WhenAny(completed.Task, Task.Delay(TimeSpan.FromSeconds(spec.WatchdogSeconds)));
        GameResult result = first == completed.Task
            ? await completed.Task
            : GameResult.ForFault($"watchdog: game exceeded {spec.WatchdogSeconds}s");

        IReadOnlyList<PlayerID> slotPlayerIDs = slots.Select(s => s.PlayerID).ToList();
        IReadOnlyList<int> slotTeams = spec.Slots.Select((s, i) => s.Team ?? i).ToList();
        return (result, exportState, slotPlayerIDs, slotTeams);
    }
}
