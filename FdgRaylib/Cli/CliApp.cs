using FDG;
using FDG.Data;
using FDG.GameModel;
using FDG.Players;
using FDG.SaveLoad;
using FDG.StageResolution;
using FDG.StageResolution.Requests;
using FdgRaylib.Cli.Resolvers;
using Raylib_cs;

namespace FdgRaylib.Cli;

public class CliApp
{
    private readonly bool _headless;

    // Initialized by Prepare(); used by RunAsync().
    private LocalMessageBus? _messageBus;
    private GameDataStore? _gameDataStore;
    private FDGGame_AsLocal? _localGame;

    public ITableState? TableState => _localGame?.TableState;

    // Filled during CreatePlayerSlots(); read by the renderer at draw time.
    public Dictionary<PlayerID, Color> PlayerColors { get; } = new();

    public CliApp(bool headless)
    {
        _headless = headless;
    }

    // Creates the local game instance (and thus TableState) without any user prompts.
    // Call this before starting the Raylib window.
    public void Prepare()
    {
        _messageBus    = new LocalMessageBus();
        _gameDataStore = GameDataStore.GameDataStoreBuilder.GetDefault();
        _localGame     = new FDGGame_AsLocal(_gameDataStore, _messageBus);
    }

    public async Task RunAsync()
    {
        if (_localGame == null) Prepare();

        Console.WriteLine("=== FDG Raylib ===");
        Console.WriteLine(_headless ? "Mode: headless CLI" : "Mode: CLI + Raylib");
        Console.WriteLine();

        var playerSlots = CreatePlayerSlots();

        foreach (var slot in playerSlots)
            _localGame!.AddLocalPlayerID(slot.PlayerID);

        var resolverRegistry = BuildResolverRegistry();
        _localGame!.AssignInterfaces(
            logMessageUI:          new CliLogMessageUI(),
            playerMessageUI:       new CliPlayerMessageUI(),
            stageResolverRegistry: resolverRegistry,
            tempVisualDrawer:      new CliTempVisualDrawer());

        foreach (var slot in playerSlots)
        {
            var controller = new LocalPlayerController(slot.Name, slot.PlayerID, _localGame);
            slot.AssignPlayerController(controller);
        }

        var gameSettings = GameSettings.GetDefault();
        gameSettings.RandomnessType = ERandomnessType.Realistic;
        var server = new FDGServer(_gameDataStore!, _messageBus!, gameSettings, playerSlots);

        Console.WriteLine("Game started. Press Ctrl+C to quit.");
        await Task.Delay(Timeout.Infinite);
    }

    private PlayerSlot[] CreatePlayerSlots()
    {
        var player1ID = new PlayerID(Guid.NewGuid());
        var player2ID = new PlayerID(Guid.NewGuid());

        PlayerColors[player1ID] = Color.Blue;
        PlayerColors[player2ID] = Color.Red;

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

    private IStageResolverRegistry BuildResolverRegistry()
    {
        return new StageResolverRegistry()
            .RegisterResolver(new YesNoResolver())
            .RegisterResolver(new StringSelectionResolver())
            .RegisterResolver(new ChooseDeploymentZoneResolver())
            .RegisterResolver(new ChooseRangedAttackResolver())
            .RegisterResolver(new DefineMovementPathResolver())
            .RegisterResolver(new AssignWoundsResolver())
            .RegisterResolver(new SelectionResolver<UnitData>())
            .RegisterResolver(new SelectionResolver<ModelData>())
            .RegisterResolver(new SelectionResolver<RectangularZone>())
            .RegisterResolver(new PlaceObjectsResolver<ModelData>(_localGame!.TableState));
    }
}
