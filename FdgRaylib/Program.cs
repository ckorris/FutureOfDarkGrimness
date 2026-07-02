using System.Text.Json;
using FdgRaylib.Cli;
using FdgRaylib.Rendering;
using FDG.ArmyBuilding;
using FDG.Data;
using FDG.Network.Connection;
using FDG.Network.Connection.Lobby;
using FDG.Rules.Serialization;
using FDG.SaveLoad;
using TinyDialogsNet;

string crashLogPath = Path.Combine(AppContext.BaseDirectory, "crash.log");

void WriteCrash(string source, object? exObj)
{
    string text = $"=== {DateTime.Now:O}  {source} ===\n{exObj}\n\n";
    Console.Error.WriteLine(text);
    try { File.AppendAllText(crashLogPath, text); } catch { /* best-effort */ }
}

AppDomain.CurrentDomain.UnhandledException += (_, e) =>
    WriteCrash("AppDomain.UnhandledException", e.ExceptionObject);

TaskScheduler.UnobservedTaskException += (_, e) =>
{
    WriteCrash("TaskScheduler.UnobservedTaskException", e.Exception);
    e.SetObserved();
};

bool headless = args.Contains("--headless");

// --slow [ms]  — pause N milliseconds before each resolver call (default 1500ms)
int slowDelayMs = 0;
int slowIdx = Array.IndexOf(args, "--slow");
if (slowIdx >= 0)
    slowDelayMs = slowIdx + 1 < args.Length && int.TryParse(args[slowIdx + 1], out int ms) ? ms : 1500;

// --import-opr <in.json> <out.fdgbook>  (#153 P0b): one-time OnePageRules Army Forge JSON → .fdgbook snapshot,
// via the engine importer. Data is OPR's, used under CC-BY-SA (stamped on the book).
int importIdx = Array.IndexOf(args, "--import-opr");
if (importIdx >= 0 && importIdx + 2 < args.Length)
{
    string inJson = args[importIdx + 1];
    string outPath = args[importIdx + 2];
    BookFile book = OprBookImporter.Import(File.ReadAllText(inJson),
        source: "OnePageRules — Army Forge (army-forge.onepagerules.com)",
        license: "CC-BY-SA 4.0");
    Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(outPath))!);
    File.WriteAllText(outPath, JsonSerializer.Serialize(book, RuleJson.Options));
    Console.WriteLine($"Imported '{book.Name}' {book.Version}: {book.Units.Count} units → {outPath}");
    return;
}

// --book-to-army <book.fdgbook> <out.fdgarmy>  (#153): dev/verify — compile every unit of a book at base size,
// proving the whole book compiles; writes a small (first-few-units) playable army for a headless smoke.
int b2aIdx = Array.IndexOf(args, "--book-to-army");
if (b2aIdx >= 0 && b2aIdx + 2 < args.Length)
{
    BookFile book = JsonSerializer.Deserialize<BookFile>(File.ReadAllText(args[b2aIdx + 1]), RuleJson.Options)!;

    BuilderList Base(IEnumerable<FDG.SaveLoad.UnitFileEntry>? _ = null, int take = int.MaxValue)
    {
        var list = new BuilderList { Name = book.Name, BookName = book.Name, PointsLimit = 100000 };
        foreach (RosterUnit u in book.Units.Take(take))
            list.Units.Add(new BuilderUnit { RosterUnitId = u.Id, ModelCount = u.BaseModelCount });
        return list;
    }

    BuiltArmyFile all = ListCompiler.Compile(book, Base());               // proves every unit compiles
    BuiltArmyFile small = ListCompiler.Compile(book, Base(take: 4));      // small, playable for a smoke
    File.WriteAllText(args[b2aIdx + 2], JsonSerializer.Serialize(small, RuleJson.Options));
    Console.WriteLine($"'{book.Name}': all {all.Units.Count} units compiled ({all.TotalPoints} pts); wrote {small.Units.Count}-unit army → {args[b2aIdx + 2]}");
    return;
}

// --army <path> (#153): non-interactive headless smoke — both players load <path>, then EOF defaults take
// over (exactly what the old `printf "1\n<path>\n..." |` pipe idiom did, minus the pipe).
int armyIdx = Array.IndexOf(args, "--army");
if (headless && armyIdx >= 0 && armyIdx + 1 < args.Length)
{
    string armyPath = args[armyIdx + 1];
    Console.SetIn(new StringReader($"1\n{armyPath}\n1\n{armyPath}\n"));
}

var app = new CliApp(headless, slowDelayMs);

if (headless)
{
    app.Prepare();
    await app.RunAsync();
}
else
{
    var renderer = new RaylibRenderer();

    // ── Main Menu ──────────────────────────────────────────────────────────────
    renderer.MainMenu.OnHostClicked = () =>
        renderer.NavigateTo(renderer.HostModal);

    renderer.MainMenu.OnArmyBuilderClicked = () =>
        renderer.NavigateTo(renderer.ArmyBuilder);

    renderer.MainMenu.OnArmyForgeClicked = () =>
        renderer.NavigateTo(renderer.ArmyForge);

    renderer.MainMenu.OnClientClicked = () =>
        renderer.NavigateTo(renderer.ClientModal);

    // ── Load Game (work item #052): open a .fdgsave, resume it as host ───────────
    renderer.MainMenu.OnLoadGameClicked = () =>
    {
        var saveFilter = new FileFilter(
            $"Saved Game (*{GameSaveFile.EXTENSION_WITH_PERIOD})",
            new[] { $"*{GameSaveFile.EXTENSION_WITH_PERIOD}" });

        var (canceled, paths) = TinyDialogs.OpenFileDialog("Load Game", "", false, saveFilter);
        if (canceled) return;

        string path = paths?.FirstOrDefault() ?? "";
        if (!File.Exists(path)) return;

        GameDataStore loadedStore;
        try
        {
            loadedStore = GameSaveSerializer.Load(File.ReadAllText(path));
        }
        catch (Exception ex)
        {
            WriteCrash("Load game failed", ex);
            return;
        }

        FDGHost host = new FDGHost();
        _ = host.StartAsync();
        var lobby = new LobbyViewModel_Host("Mr. Host", "Loaded Game", "", host, loadedStore);
        renderer.LobbyScreen.SetViewModel(lobby);
        renderer.NavigateTo(renderer.LobbyScreen);
    };

    renderer.MainMenu.OnQuitClicked = renderer.RequestClose;

    // ── Army Builder ───────────────────────────────────────────────────────────
    renderer.ArmyBuilder.OnBack = () =>
        renderer.NavigateTo(renderer.MainMenu);

    // ── Army Forge (#153) ────────────────────────────────────────────────────────
    renderer.ArmyForge.OnBack = () =>
        renderer.NavigateTo(renderer.MainMenu);

    // ── Host Modal ─────────────────────────────────────────────────────────────
    renderer.HostModal.OnCancel = () =>
        renderer.NavigateTo(renderer.MainMenu);

    renderer.HostModal.OnCreated = lobby =>
    {
        renderer.LobbyScreen.SetViewModel(lobby);
        renderer.NavigateTo(renderer.LobbyScreen);
    };

    // ── Client Modal ───────────────────────────────────────────────────────────
    renderer.ClientModal.OnCancel = () =>
        renderer.NavigateTo(renderer.MainMenu);

    renderer.ClientModal.OnConnected = lobby =>
    {
        renderer.LobbyScreen.SetViewModel(lobby);
        renderer.NavigateTo(renderer.LobbyScreen);
    };

    // ── Lobby ──────────────────────────────────────────────────────────────────
    renderer.LobbyScreen.OnBack = () =>
        renderer.NavigateTo(renderer.MainMenu);

    renderer.LobbyScreen.OnGameLaunched = (tableState, colorFunc, log, overlay, taskDisplay, presentationPlayer, saveGame, chatUI) =>
        renderer.TransitionToGame(tableState, colorFunc, log, overlay, taskDisplay, presentationPlayer, saveGame, chatUI);

    renderer.LobbyScreen.OnGameEnded = result => renderer.ShowGameOver(result);

    // ── Local play (Host with no network players) also still works via CliApp ─
    // The old "Host" path now goes through the lobby. CliApp is only used
    // in headless mode above.

    try
    {
        renderer.Run();
    }
    catch (Exception ex)
    {
        WriteCrash("renderer.Run() threw", ex);
        Console.Error.WriteLine("Press Enter to exit.");
        Console.ReadLine();
        throw;
    }
}
