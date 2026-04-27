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

    renderer.MainMenu.OnHostClicked = () =>
    {
        app.Prepare();
        _ = Task.Run(() => app.RunAsync());
        renderer.TransitionToGame(
            app.TableState!,
            playerID => app.PlayerColors.GetValueOrDefault(playerID, Raylib_cs.Color.White),
            app.Log);
    };

    renderer.MainMenu.OnArmyBuilderClicked = () => renderer.NavigateTo(renderer.ArmyBuilder);
    renderer.ArmyBuilder.OnBack            = () => renderer.NavigateTo(renderer.MainMenu);

    renderer.MainMenu.OnClientClicked = () => Console.WriteLine("[Client] Not yet implemented.");
    renderer.MainMenu.OnQuitClicked   = renderer.RequestClose;

    renderer.Run();
}
