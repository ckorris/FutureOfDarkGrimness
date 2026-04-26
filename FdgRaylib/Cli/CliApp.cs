using FDG;
using FDG.Data;
using FDG.GameModel;
using FDG.Players;
using FDG.SaveLoad;
using FDG.StageResolution;
using FDG.StageResolution.Requests;
using FdgRaylib.Cli.Resolvers;

namespace FdgRaylib.Cli;

public class CliApp
{
    private readonly bool _headless;

    public CliApp(bool headless)
    {
        _headless = headless;
    }

    public async Task RunAsync()
    {
        Console.WriteLine("=== FDG Raylib ===");
        Console.WriteLine(_headless ? "Mode: headless CLI" : "Mode: CLI (GUI not yet implemented)");
        Console.WriteLine();

        var messageBus = new LocalMessageBus();
        var gameDataStore = GameDataStore.GameDataStoreBuilder.GetDefault();

        var playerSlots = CreatePlayerSlots();

        // Client-side game instance (handles requests from the server for local players)
        var localGame = new FDGGame_AsLocal(gameDataStore, messageBus);
        foreach (var slot in playerSlots)
            localGame.AddLocalPlayerID(slot.PlayerID);

        // Assign CLI implementations of all engine-facing interfaces
        var resolverRegistry = BuildResolverRegistry();
        localGame.AssignInterfaces(
            logMessageUI:          new CliLogMessageUI(),
            playerMessageUI:       new CliPlayerMessageUI(),
            stageResolverRegistry: resolverRegistry,
            tempVisualDrawer:      new CliTempVisualDrawer());

        // Register players as local controllers so the server knows they're ready
        foreach (var slot in playerSlots)
        {
            var controller = new LocalPlayerController(slot.Name, slot.PlayerID, localGame);
            slot.AssignPlayerController(controller);
        }

        // Server runs the state machine and dispatches requests through the message bus
        var gameSettings = GameSettings.GetDefault();
        gameSettings.RandomnessType = ERandomnessType.Realistic;
        var server = new FDGServer(gameDataStore, messageBus, gameSettings, playerSlots);

        Console.WriteLine("Game started. Press Ctrl+C to quit.");
        await Task.Delay(Timeout.Infinite);
    }

    private static PlayerSlot[] CreatePlayerSlots()
    {
        var player1ID = new PlayerID(Guid.NewGuid());
        var player2ID = new PlayerID(Guid.NewGuid());

        var army1 = ArmyLoader.PromptForArmy("Player 1");
        Console.WriteLine();
        var army2 = ArmyLoader.PromptForArmy("Player 2");
        Console.WriteLine();

        return new[]
        {
            new PlayerSlot(slotID: 0, teamNumber: 0, playerID: player1ID, armyListFile: army1),
            new PlayerSlot(slotID: 1, teamNumber: 1, playerID: player2ID, armyListFile: army2),
        };
    }

    private static IStageResolverRegistry BuildResolverRegistry()
    {
        return new StageResolverRegistry()
            .RegisterResolver(new YesNoResolver())
            .RegisterResolver(new StringSelectionResolver())
            .RegisterResolver(new ChooseDeploymentZoneResolver())
            .RegisterResolver(new ChooseRangedAttackResolver())
            .RegisterResolver(new DefineMovementPathResolver())
            .RegisterResolver(new AssignWoundsResolver())
            // SelectionRequest<T> needs a registration per concrete type T the engine uses.
            .RegisterResolver(new SelectionResolver<UnitData>())
            .RegisterResolver(new SelectionResolver<ModelData>())
            .RegisterResolver(new SelectionResolver<RectangularZone>())
            // PlaceObjectsRequest<T> needs a registration per concrete type T the engine uses.
            .RegisterResolver(new PlaceObjectsResolver<ModelData>());
    }
}
