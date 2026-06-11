using FdgRaylib.Cli;
using FdgRaylib.Rendering;
using FDG.Data;
using FDG.Network.Connection;
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

    renderer.LobbyScreen.OnGameLaunched = (tableState, colorFunc, log, overlay, taskDisplay, presentationPlayer, saveGame) =>
        renderer.TransitionToGame(tableState, colorFunc, log, overlay, taskDisplay, presentationPlayer, saveGame);

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
