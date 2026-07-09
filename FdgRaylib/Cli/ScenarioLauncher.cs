using FDG;
using FDG.Ai;
using FDG.Data;
using FDG.GameModel;
using FDG.Players;
using FDG.SaveLoad;

namespace FdgRaylib.Cli;

/// <summary>
/// Assembles a lobby-free resume for <c>--scenario</c> (#167): loads (or compiles) the save, rebuilds
/// player slots on the saved IDs — slot 0 as the local human, every other slot AI — and hands back the
/// pieces the headless and GUI launch paths wire their own interfaces onto. Mirrors
/// <c>LobbyViewModel_Host.LaunchResume</c> minus networking and re-crew UI.
/// </summary>
public static class ScenarioLauncher
{
    public record ResumeParts(GameDataStore Store, LocalMessageBus Bus, PlayerSlot[] Slots,
        FDGGame_AsLocal HumanGame, IReadOnlyList<PlayerSlotInfo> SavedInfos);

    /// <summary>A scenario JSON compiles in-memory; a .fdgsave loads directly.</summary>
    public static GameDataStore LoadStore(string path)
    {
        if (path.EndsWith(GameSaveFile.EXTENSION_WITH_PERIOD, StringComparison.OrdinalIgnoreCase))
        {
            if (!File.Exists(path))
                throw new ScenarioCompileException($"Save file not found: {path}");
            return GameSaveSerializer.Load(File.ReadAllText(path));
        }

        return ScenarioCompiler.CompileFromFile(path);
    }

    /// <param name="seedOverride">
    /// #193 <c>--seed</c>: replaces the seed saved in the scenario, so the same scenario can be replayed
    /// under different (but individually reproducible) dice. Null keeps whatever the save carries. The
    /// resume path reads settings from the store's <see cref="GameProgressData"/>, so the override is
    /// written there before the server is built.
    /// </param>
    public static ResumeParts BuildResume(GameDataStore store, int? seedOverride = null)
    {
        // Mirror LaunchResume: capture the saved slot infos, then drop the old records so the
        // rebuilt slots don't create duplicates.
        List<PlayerSlotInfo> savedInfos = store.GetAllValues<PlayerSlotInfo>()
            .OrderBy(info => info.SlotID).ToList();
        if (savedInfos.Count == 0)
            throw new ScenarioCompileException("This save carries no player slots - it can't be resumed.");

        GameProgressData? progress = GameProgressUtilities.TryGetProgress(store);
        if (seedOverride.HasValue && progress != null)
        {
            GameSettings settings = progress.Settings;
            settings.DiceSeed = seedOverride;
            progress.Settings = settings;
        }
        int? effectiveSeed = progress?.Settings.DiceSeed;

        foreach (DataReference oldInfo in store.GetAllDataReferences<PlayerSlotInfo>().ToList())
            store.Destroy(oldInfo);

        var bus = new LocalMessageBus();
        var humanGame = new FDGGame_AsLocal(store, bus);

        var slots = new PlayerSlot[savedInfos.Count];
        for (int i = 0; i < savedInfos.Count; i++)
        {
            // ArmyListFile is vestigial on resume (armies already live in the loaded store), but the
            // PlayerSlot ctor requires one.
            slots[i] = new PlayerSlot(i, savedInfos[i].TeamNumber, savedInfos[i].PlayerID,
                new ArmyListFile(), store);

            if (i == 0)
            {
                humanGame.AddLocalPlayerID(savedInfos[i].PlayerID);
                slots[i].AssignPlayerController(
                    new LocalPlayerController("Player 1", savedInfos[i].PlayerID, humanGame));
            }
            else
            {
                var aiGame = new FDGGame_AsLocal(store, bus);
                slots[i].AssignPlayerController(AiResolverRegistryFactory.CreateSoloRulesController(
                    $"Player {i + 1} (AI)", savedInfos[i].PlayerID, aiGame, effectiveSeed, slots[i].SlotID));
            }
        }

        return new ResumeParts(store, bus, slots, humanGame, savedInfos);
    }
}
