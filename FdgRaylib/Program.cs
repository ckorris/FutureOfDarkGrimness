using FdgRaylib.Cli;
using FdgRaylib.Rendering;

bool headless = args.Contains("--headless");

// --slow [ms]  — pause N milliseconds before each resolver call (default 1500ms)
int slowDelayMs = 0;
int slowIdx = Array.IndexOf(args, "--slow");
if (slowIdx >= 0)
    slowDelayMs = slowIdx + 1 < args.Length && int.TryParse(args[slowIdx + 1], out int ms) ? ms : 1500;

var app = new CliApp(headless, slowDelayMs);
app.Prepare();

if (headless)
{
    await app.RunAsync();
}
else
{
    _ = Task.Run(() => app.RunAsync());

    var renderer = new RaylibRenderer(
        app.TableState!,
        playerID => app.PlayerColors.GetValueOrDefault(playerID, Raylib_cs.Color.White),
        app.Log);

    renderer.Run();
}
