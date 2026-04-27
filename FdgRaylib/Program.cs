using FdgRaylib.Cli;
using FdgRaylib.Rendering;

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
        Console.WriteLine("[Client] Not yet implemented.");

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

    // ── Lobby ──────────────────────────────────────────────────────────────────
    renderer.LobbyScreen.OnBack = () =>
        renderer.NavigateTo(renderer.MainMenu);

    renderer.LobbyScreen.OnGameLaunched = (tableState, colorFunc, log) =>
        renderer.TransitionToGame(tableState, colorFunc, log);

    // ── Local play (Host with no network players) also still works via CliApp ─
    // The old "Host" path now goes through the lobby. CliApp is only used
    // in headless mode above.

    renderer.Run();
}
