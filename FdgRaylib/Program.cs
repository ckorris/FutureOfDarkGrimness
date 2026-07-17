using System.Text.Json;
using FdgRaylib.Cli;
using FdgRaylib.Rendering;
using FDG;
using FDG.ArmyBuilding;
using FDG.Data;
using FDG.GameModel;
using FDG.Network.Connection;
using FDG.Network.Connection.Lobby;
using FDG.Presentation;
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

// --field-harness (#162): offscreen GPU-vs-CPU field pixel diff. Hidden GL window, no game, exits with
// 0 iff every synthetic scene matches the CPU reference renderer within tolerance.
if (args.Contains("--field-harness"))
{
    Environment.Exit(FdgRaylib.Rendering.TacticalOverlay.FieldHarness.Run());
}

bool headless = args.Contains("--headless");

// --trace-rules (#163): narrate every live rule hook evaluation (fired / condition failed / suppressed /
// ability offered) through the Debug log channel — printed as [LOG] lines headless, shown in the GUI
// console's Debug view. The GUI Debug toggle flips the same switch at runtime.
if (args.Contains("--trace-rules"))
{
    FDG.Rules.Dispatch.RuleTrace.Enabled = true;
}

// --slow [ms]  — pause N milliseconds before each resolver call (default 1500ms)
int slowDelayMs = 0;
int slowIdx = Array.IndexOf(args, "--slow");
if (slowIdx >= 0)
    slowDelayMs = slowIdx + 1 < args.Length && int.TryParse(args[slowIdx + 1], out int ms) ? ms : 1500;

// --seed <int> (#193): seeds the whole game — dice, decisive rolls, objective auto-placement, and each
// AI player's own stream. Same seed + same build => identical game. Overrides a seed saved in a scenario.
int? diceSeed = null;
int seedIdx = Array.IndexOf(args, "--seed");
if (seedIdx >= 0 && seedIdx + 1 < args.Length && int.TryParse(args[seedIdx + 1], out int parsedSeed))
    diceSeed = parsedSeed;

// --ai-profile <solorules|tactician> (#191): which AI drives the computer slots on the headless and
// scenario paths (lobby AI selection is a later slice, plan A6). Default: solo-rules.
var aiProfile = FDG.Ai.EAiProfile.SoloRules;
int profileIdx = Array.IndexOf(args, "--ai-profile");
if (profileIdx >= 0 && profileIdx + 1 < args.Length)
{
    if (!Enum.TryParse(args[profileIdx + 1], ignoreCase: true, out aiProfile))
    {
        Console.Error.WriteLine($"Unknown --ai-profile '{args[profileIdx + 1]}'. Known: " +
            string.Join(", ", Enum.GetNames<FDG.Ai.EAiProfile>()).ToLowerInvariant());
        Environment.Exit(2);
    }
}

// --import-opr <in.json> <out.fdgbook> [supplement.json]  (#153 P0b): one-time OnePageRules Army Forge
// JSON → .fdgbook snapshot, via the engine importer. Data is OPR's, used under CC-BY-SA (stamped on the
// book). The optional supplement embeds curated rule definitions the book references (see --apply-rules).
int importIdx = Array.IndexOf(args, "--import-opr");
if (importIdx >= 0 && importIdx + 2 < args.Length)
{
    string inJson = args[importIdx + 1];
    string outPath = args[importIdx + 2];
    BookFile book = OprBookImporter.Import(File.ReadAllText(inJson),
        source: "OnePageRules - Army Forge (army-forge.onepagerules.com)",
        license: "CC-BY-SA 4.0",
        warn: msg => Console.WriteLine($"  {msg}"));
    if (importIdx + 3 < args.Length && !args[importIdx + 3].StartsWith("--"))
    {
        var supplement = BookRuleSupplement.LoadDefinitions(File.ReadAllText(args[importIdx + 3]));
        var embedded = BookRuleSupplement.Apply(book, supplement, msg => Console.WriteLine($"  {msg}"));
        Console.WriteLine($"  supplement: embedded {embedded.Count} rule definitions ({string.Join(", ", embedded)})");
    }
    Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(outPath))!);
    File.WriteAllText(outPath, JsonSerializer.Serialize(book, RuleJson.Options));
    Console.WriteLine($"Imported '{book.Name}' {book.Version}: {book.Units.Count} units, {book.Spells.Count} spells -> {outPath}");
    return;
}

// --import-army <share-link-or-id> <out.fdgarmy>  (#241): fetch an Army Forge share list and write a
// playable army file (same pipeline as the Forge screen's Import Link button). Refuses on an OPR
// version / game-system mismatch. Exit 0 on success, 1 on any failure.
int importArmyIdx = Array.IndexOf(args, "--import-army");
if (importArmyIdx >= 0 && importArmyIdx + 2 < args.Length)
{
    try
    {
        var outcome = FdgRaylib.Import.ArmyForgeShareService
            .FetchAndImportAsync(args[importArmyIdx + 1]).GetAwaiter().GetResult();
        foreach (string w in outcome.Result.Warnings) Console.WriteLine($"  warning: {w}");
        foreach (string e in outcome.Result.ListErrors) Console.WriteLine($"  Army Forge list error: {e}");
        if (outcome.InertRules.Count > 0)
            Console.WriteLine($"  not enforced by engine: {string.Join(", ", outcome.InertRules)}");

        // #241 v2: pricing reconciliation - our ListCompiler vs Army Forge (a #218/#219 detector).
        if (outcome.ForgeSession is { } session)
        {
            foreach ((string name, int pts) in session.ExcludedUnits)
                Console.WriteLine($"  excluded (not in bundled book): {name} ({pts} pts)");
            foreach ((string name, int ours, int theirs) in session.UnitPointsDeltas)
                Console.WriteLine($"  points delta: {name} - our Forge {ours} pts, Army Forge {theirs} pts");
            Console.WriteLine(session.OurTotalPoints == session.TheirTotalPoints
                ? $"  points check: OK ({session.OurTotalPoints} pts both ways)"
                : $"  points check: MISMATCH - our Forge {session.OurTotalPoints} pts vs Army Forge {session.TheirTotalPoints} pts");
        }

        string outArmyPath = args[importArmyIdx + 2];
        if (Path.GetExtension(outArmyPath) != ArmyListFile.EXTENSION_WITH_PERIOD)
            outArmyPath = Path.ChangeExtension(outArmyPath, ArmyListFile.EXTENSION_WITH_PERIOD);
        string? outArmyDir = Path.GetDirectoryName(Path.GetFullPath(outArmyPath));
        if (!string.IsNullOrEmpty(outArmyDir)) Directory.CreateDirectory(outArmyDir);
        File.WriteAllText(outArmyPath, JsonSerializer.Serialize(outcome.Result.Army, RuleJson.Options));
        Console.WriteLine($"Imported '{outcome.Result.Army.Name}' ({outcome.Result.Army.Faction}): " +
            $"{outcome.Result.Army.Units.Count} units, {outcome.Result.Army.TotalPoints} pts -> {outArmyPath}");
    }
    catch (Exception ex)
    {
        Console.WriteLine($"Import failed: {ex.Message}");
        Environment.Exit(1);
    }
    return;
}

// --apply-rules <book.fdgbook> <supplement.json>  (#153): merge curated rule definitions into an existing
// book snapshot in place — the definitions the book references (plus what those grant) embed into the
// book's ruleDefinitions, replace-by-name, so re-applying after editing the supplement is idempotent.
// Validation is hard-fail; an invalid supplement leaves the book untouched.
int applyIdx = Array.IndexOf(args, "--apply-rules");
if (applyIdx >= 0 && applyIdx + 2 < args.Length)
{
    string bookPath = args[applyIdx + 1];
    BookFile book = JsonSerializer.Deserialize<BookFile>(File.ReadAllText(bookPath), RuleJson.Options)!;
    var supplement = BookRuleSupplement.LoadDefinitions(File.ReadAllText(args[applyIdx + 2]));
    var embedded = BookRuleSupplement.Apply(book, supplement, msg => Console.WriteLine($"  {msg}"));
    File.WriteAllText(bookPath, JsonSerializer.Serialize(book, RuleJson.Options));
    Console.WriteLine($"'{book.Name}': embedded {embedded.Count} rule definitions " +
        $"({string.Join(", ", embedded)}) -> {bookPath}");
    return;
}

// --validate-rules <supplement.json>  (#153): authoring aid — strict-parse the supplement and validate
// every definition (hook/capability fit, granted names resolve, no duplicates) without touching a book.
int validateIdx = Array.IndexOf(args, "--validate-rules");
if (validateIdx >= 0 && validateIdx + 1 < args.Length)
{
    var supplement = BookRuleSupplement.LoadDefinitions(File.ReadAllText(args[validateIdx + 1]));
    var problems = BookRuleSupplement.ValidateAll(supplement);
    foreach (string problem in problems)
        Console.WriteLine($"  {problem}");
    Console.WriteLine(problems.Count == 0
        ? $"OK: {supplement.Count} definitions, no problems."
        : $"{problems.Count} problem(s) in {supplement.Count} definitions.");
    return;
}

// --rule-coverage <booksDir>  (#196 slice 1 / SYS-5): the import reconciliation report the audit asked
// for. Mirrors what army load actually does — CoreRuleCatalog + each book's own embedded rule
// definitions, walked over every reference at its real attachment scope (unit rules/items, weapon
// profiles, and the same three inside every upgrade option) — so a name with no definition anywhere and
// a name whose definition disagrees with its attachment scope are reported separately, and a name that
// resolves cleanly (including a weapon-scoped rule reached via a unit-level wargear bundle, which #197
// slice 0 made a legal attach, not a mismatch) is not reported at all.
int coverageIdx = Array.IndexOf(args, "--rule-coverage");
if (coverageIdx >= 0 && coverageIdx + 1 < args.Length)
{
    RuleCoverageReport.Run(args[coverageIdx + 1]);
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
    Console.WriteLine($"'{book.Name}': all {all.Units.Count} units compiled ({all.TotalPoints} pts); wrote {small.Units.Count}-unit army -> {args[b2aIdx + 2]}");
    return;
}

// --retrofit-effects <fileOrDir> [more...]  (#239): stamp weapon effect-set keys into existing data.
// Books get their faction's default sets; armies get army-level defaults plus per-weapon keyword keys
// (explicit keys already in a file are never touched). Idempotent — re-run after a keyword-table change.
int retrofitIdx = Array.IndexOf(args, "--retrofit-effects");
if (retrofitIdx >= 0 && retrofitIdx + 1 < args.Length)
{
    List<string> retrofitTargets = args.Skip(retrofitIdx + 1).TakeWhile(a => !a.StartsWith("--"))
        .SelectMany(t => Directory.Exists(t)
            ? Directory.GetFiles(t, "*" + BookFile.EXTENSION_WITH_PERIOD)
                .Concat(Directory.GetFiles(t, "*" + ArmyListFile.EXTENSION_WITH_PERIOD))
            : new[] { t })
        .ToList();
    int retrofitPatched = 0;
    foreach (string path in retrofitTargets)
    {
        bool changed;
        if (path.EndsWith(BookFile.EXTENSION_WITH_PERIOD, StringComparison.OrdinalIgnoreCase))
        {
            BookFile book = JsonSerializer.Deserialize<BookFile>(File.ReadAllText(path), RuleJson.Options)!;
            changed = WeaponEffectAssigner.ApplyToBook(book);
            if (changed) File.WriteAllText(path, JsonSerializer.Serialize(book, RuleJson.Options));
        }
        else
        {
            // Deserialize as BuiltArmyFile so a forge army's selections/book snapshot survives the
            // round-trip (#236); a hand-authored army has both null and re-saves as the base type.
            BuiltArmyFile army = JsonSerializer.Deserialize<BuiltArmyFile>(File.ReadAllText(path), RuleJson.Options)!;
            changed = WeaponEffectAssigner.ApplyToArmy(army);
            if (changed)
                File.WriteAllText(path, army.Book != null
                    ? JsonSerializer.Serialize(army, RuleJson.Options)
                    : JsonSerializer.Serialize<ArmyListFile>(army, RuleJson.Options));
        }
        Console.WriteLine($"  {(changed ? "patched" : "unchanged")}: {path}");
        if (changed) retrofitPatched++;
    }
    Console.WriteLine($"Retrofit complete: {retrofitPatched}/{retrofitTargets.Count} file(s) patched.");
    return;
}

// --make-scenario <scenario.json> <out.fdgsave>  (#167 T1): compile a compact scenario JSON (armies,
// placements, wounds/tokens, whose activation it is) into a resumable save positioned at the start of
// the active player's activation. Author a rule test in ~20 lines of JSON instead of playing to it.
int makeScenarioIdx = Array.IndexOf(args, "--make-scenario");
if (makeScenarioIdx >= 0 && makeScenarioIdx + 2 < args.Length)
{
    string outPath = args[makeScenarioIdx + 2];
    try
    {
        string saveJson = ScenarioCompiler.CompileFileToSaveJson(args[makeScenarioIdx + 1]);
        string? outDir = Path.GetDirectoryName(Path.GetFullPath(outPath));
        if (!string.IsNullOrEmpty(outDir)) Directory.CreateDirectory(outDir);
        File.WriteAllText(outPath, saveJson);
        Console.WriteLine($"Compiled scenario '{args[makeScenarioIdx + 1]}' -> {outPath}");
    }
    catch (ScenarioCompileException ex)
    {
        Console.WriteLine($"Scenario error: {ex.Message}");
        Environment.Exit(1);
    }
    return;
}

// --scenario <file.json|file.fdgsave>  (#167): launch straight into a scenario, skipping the main menu
// AND the lobby — slot 0 is the local player, every other slot is AI. A .json compiles in-memory first;
// a .fdgsave (from --make-scenario or an in-game save) loads directly. Works headless and in the GUI.
int scenarioIdx = Array.IndexOf(args, "--scenario");
string? scenarioPath = scenarioIdx >= 0 && scenarioIdx + 1 < args.Length ? args[scenarioIdx + 1] : null;

// --army <path> (#153): non-interactive headless smoke — both players load <path>, then EOF defaults take
// over (exactly what the old `printf "1\n<path>\n..." |` pipe idiom did, minus the pipe).
int armyIdx = Array.IndexOf(args, "--army");
if (headless && armyIdx >= 0 && armyIdx + 1 < args.Length)
{
    string armyPath = args[armyIdx + 1];
    Console.SetIn(new StringReader($"1\n{armyPath}\n1\n{armyPath}\n"));
}

var app = new CliApp(headless, slowDelayMs, diceSeed, aiProfile);

if (headless)
{
    if (scenarioPath != null)
    {
        try
        {
            await app.RunScenarioAsync(ScenarioLauncher.LoadStore(scenarioPath));
        }
        catch (ScenarioCompileException ex)
        {
            Console.WriteLine($"Scenario error: {ex.Message}");
            Environment.Exit(1);
        }
        return;
    }

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

    // ── --scenario (#167): straight into the game, no menu, no lobby ──────────
    // Load/compile eagerly so a bad scenario fails fast with a message and no window. The game
    // wiring is deferred to OnWindowReady (first thing inside Run(), window + ImGui live):
    // TransitionToGame attaches the tactical overlay, which creates GL resources (#162) and
    // segfaults before InitWindow. Slot 0 = local player, other slots AI; the engine waits on
    // the resolver registry (assigned in GameGuiWiring.Launch) before requesting decisions.
    if (scenarioPath != null)
    {
        try
        {
            GameDataStore scenarioStore = ScenarioLauncher.LoadStore(scenarioPath);
            var parts = ScenarioLauncher.BuildResume(scenarioStore, diceSeed, aiProfile);

            var players = new List<(PlayerID ID, string Name)>();
            for (int i = 0; i < parts.SavedInfos.Count; i++)
                players.Add((parts.SavedInfos[i].PlayerID, i == 0 ? "Player 1" : $"Player {i + 1} (AI)"));

            renderer.OnWindowReady = () =>
            {
                GameGuiWiring.Launch(parts.HumanGame, players,
                    saveGameToJson: () => GameSaveSerializer.Save(parts.Store),
                    onLaunched: renderer.TransitionToGame);

                var scenarioServer = new FDGServer(parts.Store, parts.Bus, parts.Slots,
                    new RealtimePresentationClock());
                scenarioServer.OnGameEnded += result => renderer.ShowGameOver(result);
            };
        }
        catch (ScenarioCompileException ex)
        {
            Console.WriteLine($"Scenario error: {ex.Message}");
            Environment.Exit(1);
        }
    }

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
