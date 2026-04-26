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

        var playerSlots = CreateTestPlayerSlots();

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

    private static PlayerSlot[] CreateTestPlayerSlots()
    {
        var player1ID = new PlayerID(Guid.NewGuid());
        var player2ID = new PlayerID(Guid.NewGuid());

        return new[]
        {
            new PlayerSlot(slotID: 0, teamNumber: 0, playerID: player1ID,
                armyListFile: MakeTestArmy("Player 1")),
            new PlayerSlot(slotID: 1, teamNumber: 1, playerID: player2ID,
                armyListFile: MakeTestArmy("Player 2")),
        };
    }

    private static ArmyListFile MakeTestArmy(string playerName)
    {
        return new ArmyListFile
        {
            Name = $"{playerName}'s Army",
            Faction = "Test",
            PointsLimit = 500,
            Units = new List<UnitFileEntry>
            {
                new UnitFileEntry
                {
                    Name = "Warriors",
                    ModelCount = 5,
                    Quality = 4,
                    Defense = 4,
                    PointCost = 150,
                    Weapons = new List<WeaponFileEntry>
                    {
                        new WeaponFileEntry { Name = "Rifle", RangeInches = 24, Attacks = 1 }
                    }
                },
                new UnitFileEntry
                {
                    Name = "Heavy Gunners",
                    ModelCount = 3,
                    Quality = 4,
                    Defense = 4,
                    PointCost = 120,
                    Weapons = new List<WeaponFileEntry>
                    {
                        new WeaponFileEntry { Name = "Heavy Rifle", RangeInches = 36, Attacks = 1 }
                    }
                }
            }
        };
    }

    private static IStageResolverRegistry BuildResolverRegistry()
    {
        return new StageResolverRegistry()
            .RegisterResolver(new YesNoResolver())
            .RegisterResolver(new StringSelectionResolver())
            .RegisterResolver(new ChooseDeploymentZoneResolver())
            .RegisterResolver(new DefineMovementPathResolver())
            .RegisterResolver(new AssignWoundsResolver())
            // SelectionRequest<T> needs a registration per concrete type T the engine uses.
            .RegisterResolver(new SelectionResolver<UnitData>())
            .RegisterResolver(new SelectionResolver<ModelData>())
            .RegisterResolver(new SelectionResolver<RectangularZone>());
    }
}
