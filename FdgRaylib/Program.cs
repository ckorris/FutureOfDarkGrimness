using FdgRaylib.Cli;

// --headless skips the Raylib window; used for automated testing and CLI-only play
bool headless = args.Contains("--headless");

var app = new CliApp(headless);
await app.RunAsync();
