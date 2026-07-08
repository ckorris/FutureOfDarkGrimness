using FDG;
using FDG.Ai;
using FDG.Data;
using FDG.GameModel;
using FDG.Players;
using FDG.SaveLoad;
using FDG.StageResolution;
using FDG.StageResolution.Requests;
using FDG.TextInterface;
using FdgRaylib.Cli.Resolvers;
using FdgRaylib.Rendering;
using Raylib_cs;

namespace FdgRaylib.Cli;

public class CliApp
{
    private readonly bool _headless;
    private readonly int _slowDelayMs;

    // Initialized by Prepare(); used by RunAsync().
    private LocalMessageBus? _messageBus;
    private GameDataStore? _gameDataStore;
    private FDGGame_AsLocal? _localGame;
    private FDGGame_AsLocal? _aiGame;

    public ITableState? TableState => _localGame?.TableState;
    public GameLog? Log { get; private set; }

    // Filled during CreatePlayerSlots(); read by the renderer at draw time.
    public Dictionary<PlayerID, Color> PlayerColors { get; } = new();

    public CliApp(bool headless, int slowDelayMs = 0)
    {
        _headless = headless;
        _slowDelayMs = slowDelayMs;
    }

    // Creates the local game instance (and thus TableState) without any user prompts.
    // Call this before starting the Raylib window.
    public void Prepare()
    {
        _messageBus    = new LocalMessageBus();
        _gameDataStore = GameDataStore.GameDataStoreBuilder.GetDefault();
        _localGame     = new FDGGame_AsLocal(_gameDataStore, _messageBus);

        if (!_headless)
            Log = new GameLog();
    }

    public async Task RunAsync()
    {
        if (_localGame == null) Prepare();

        Console.WriteLine("=== FDG Raylib ===");
        string modeDesc = _headless ? "headless CLI" : "CLI + Raylib";
        if (_slowDelayMs > 0) modeDesc += $" (slow mode: {_slowDelayMs}ms)";
        Console.WriteLine($"Mode: {modeDesc}");
        Console.WriteLine();

        var playerSlots = CreatePlayerSlots();

        // Player 1 — human (CLI)
        _localGame!.AddLocalPlayerID(playerSlots[0].PlayerID);

        ILogMessageUI logUI = Log != null
            ? new GuiLogMessageUI(Log)
            : new CliLogMessageUI();

        IStageResolverRegistry resolverRegistry = BuildResolverRegistry();
        if (_slowDelayMs > 0)
            resolverRegistry = new SlowModeResolverRegistry(resolverRegistry, _slowDelayMs);

        _localGame!.AssignInterfaces(
            logMessageUI:          logUI,
            playerMessageUI:       new CliPlayerMessageUI(),
            stageResolverRegistry: resolverRegistry,
            presentationSink:      null, // headless: beats emitted but not rendered
            outstandingTaskDisplay: null);

        // Name the controllers explicitly: reading playerSlots[i].Name here returns the "[Empty]"
        // placeholder, since the controller that supplies the real name isn't assigned yet.
        var humanController = new LocalPlayerController("Player 1", playerSlots[0].PlayerID, _localGame);
        playerSlots[0].AssignPlayerController(humanController);

        // Player 2 — computer (solo-rules AI)
        _aiGame = new FDGGame_AsLocal(_gameDataStore!, _messageBus!);
        var aiController = AiResolverRegistryFactory.CreateSoloRulesController(
            "Player 2 (AI)", playerSlots[1].PlayerID, _aiGame);
        playerSlots[1].AssignPlayerController(aiController);

        var gameSettings = GameSettings.GetDefault();
        gameSettings.RandomnessType = ERandomnessType.Realistic;
        gameSettings.AutoPlaceObjectivesDebug = true;

        var gameEnded = new TaskCompletionSource();
        var server = new FDGServer(_gameDataStore!, _messageBus!, gameSettings, playerSlots);
        server.OnGameEnded += result =>
        {
            logUI.DisplayLogMessage($"Game ended: {result}", TextColor.White);
            gameEnded.TrySetResult();
        };

        Console.WriteLine("Game started.");
        await gameEnded.Task;
    }

    /// <summary>
    /// #167 --scenario: resumes an already-populated store (a compiled scenario or a loaded save)
    /// headless, lobby-free: slot 0 drives via the CLI resolvers, every other saved slot is AI.
    /// </summary>
    public async Task RunScenarioAsync(GameDataStore loadedStore)
    {
        Console.WriteLine("=== FDG Raylib - scenario ===");

        var parts = ScenarioLauncher.BuildResume(loadedStore);
        _messageBus = parts.Bus;
        _gameDataStore = parts.Store;
        _localGame = parts.HumanGame;

        ILogMessageUI logUI = new CliLogMessageUI();
        IStageResolverRegistry resolverRegistry = BuildResolverRegistry();
        if (_slowDelayMs > 0)
            resolverRegistry = new SlowModeResolverRegistry(resolverRegistry, _slowDelayMs);

        _localGame.AssignInterfaces(
            logMessageUI:          logUI,
            playerMessageUI:       new CliPlayerMessageUI(),
            stageResolverRegistry: resolverRegistry,
            presentationSink:      null,
            outstandingTaskDisplay: null);

        var gameEnded = new TaskCompletionSource();
        var server = new FDGServer(parts.Store, parts.Bus, parts.Slots); // resume constructor
        server.OnGameEnded += result =>
        {
            logUI.DisplayLogMessage($"Game ended: {result}", TextColor.White);
            gameEnded.TrySetResult();
        };

        Console.WriteLine("Scenario resumed.");
        await gameEnded.Task;
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
            new PlayerSlot(slotID: 0, teamNumber: 0, playerID: player1ID, armyListFile: army1, gameDataStore: _gameDataStore!),
            new PlayerSlot(slotID: 1, teamNumber: 1, playerID: player2ID, armyListFile: army2, gameDataStore: _gameDataStore!),
        };
    }

    private IStageResolverRegistry BuildResolverRegistry()
        => ResolverRegistryFactory.Build(_localGame!.TableState);
}
