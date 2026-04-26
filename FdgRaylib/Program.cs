using FdgRaylib.Cli;
using FdgRaylib.Rendering;

bool headless = args.Contains("--headless");

var app = new CliApp(headless);
app.Prepare();  // creates TableState before either thread starts

if (headless)
{
    await app.RunAsync();
}
else
{
    // Game loop on a background thread; Raylib window owns the main thread.
    _ = Task.Run(() => app.RunAsync());

    var renderer = new RaylibRenderer(
        app.TableState!,
        playerID => app.PlayerColors.GetValueOrDefault(playerID, Raylib_cs.Color.White));

    renderer.Run();
}
