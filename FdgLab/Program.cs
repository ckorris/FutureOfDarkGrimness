using FDG;
using FdgLab;

// FdgLab (#194): self-play harness for the Tactician AI effort (#191). Engine-only dependency; see
// docs/ai-agent-plan.md sections 6-7 for what each command is for.

return args.FirstOrDefault() switch
{
    "bench" => await RunBench(args.Skip(1).ToArray()),
    "smoke" => await RunSmoke(args.Skip(1).ToArray()),
    "probes" => await RunProbes(args.Skip(1).ToArray()),
    "analyze" => FdgLab.Analyze.Run(args.Skip(1).ToArray()),
    _ => Usage(),
};

static int Usage()
{
    Console.WriteLine("""
        FdgLab - self-play harness (#194)

        Commands:
          bench   --a <army> --b <army> | --pool <dir> | --panel <name>   seeded, side-swapped
                                     benchmark matrix. --panel reads FdgLab/armies/pool.json's
                                     named generalization panel (B+C campaign, docs/tactician-bc-
                                     campaign.md sec 5): points-1k | points-3k | points-4k |
                                     shape-2v2
                  [--profile-a P] [--profile-b P]  AI per army side: solorules | tactician (#191 A4)
                  [--pause-file PATH]  before each game, wait while PATH exists (cooperative pause
                                     so a soak/self-play driver can share the box, B+C campaign)
                  [--weights "Name=V;Name=V"]  override TacticianWeights fields for this process
                                     (#191 automated tuning; recorded in the report header)
                  [--games N]        total games per matchup (default 200; played as N/2 seeds x 2 sides)
                  [--seed-base S]    first seed (default 1000)
                  [--dop D]          concurrent games (default: min(16, cores))
                  [--timeout T]      per-game watchdog seconds (default 120)
                  [--dice realistic|probabilistic]   (default realistic)
                  [--out DIR]        report directory (default FdgLab/reports)
                  [--dump-logs DIR]  write each game's full log (stable filenames - diff two runs
                                     file by file to hunt divergence, #210); [--trace] adds the
                                     #198 position-write trace next to each log
                  [--triangle]       pool: unordered pairs only (pre-2026-07-10 shape; skews the
                                     aggregate toward profile A's alphabetically-early armies)
          smoke   [--seed S] [--a <army>] [--b <army>]   one game, prints the record
                  [--profile-a P] [--profile-b P]        AI per slot: solorules | tactician |
                                                         gunline (scripted human stand-in: holds
                                                         its line, shoots, claims safe objectives)
                  [--log-decisions]  with --dump-logs: interleave each planning AI's Choose Action
                                     narration ("[ai N] plan ..." + full scored candidate table)
                                     into the game log - a decision replay (#191 tooling)
          analyze <save.fdgsave> [--unit substr] [--no-board]   per-unit Tactician candidate-score
                  dump + the action it would take from that exact state - point it at a parked
                  save from a hand-played game (#191 tooling); [--urgency] prepends each army's
                  activation-urgency table + the unit the resolver would pick next (#389)
          probes  --feasibility [--games N] [--seed-base S] [--a/--b <army>]   #191 A3 gate metric:
                  shadow-runs the MacroActionGenerator at every movement decision of real games and
                  reports the fraction of activations with a valid non-Hold candidate (target >= 95%)

        An <army> is a .fdgarmy path, 'builtin' (the CLI's EOF-fallback test army), or
        'builtin-basic' (builtin minus its Ambush unit - the harness-determinism gate army, see #198).
        """);
    return 2;
}

static async Task<int> RunBench(string[] args)
{
    string? a = Arg(args, "--a");
    string? b = Arg(args, "--b");
    string? pool = Arg(args, "--pool");
    string? panel = Arg(args, "--panel");

    var matchups = new List<Matchup>();
    if (panel != null)
    {
        var loaded = Pool.LoadPanel(panel);
        if (loaded == null) return 2;
        matchups.AddRange(loaded);
    }
    else if (pool != null)
    {
        // Every ORDERED pair plus each self-mirror once (Chris, 2026-07-10): profile A binds to
        // army A, so an unordered triangle made profile A play alphabetically-early armies far
        // more often (Hives in 8 matchups, Robot Legions in 1) and skewed the aggregate toward
        // its best armies. Ordered pairs give every army equal coverage on both sides of the
        // profile split. --triangle restores the old (cheaper, skewed) shape for comparisons.
        string[] armies = Directory.GetFiles(pool, "*.fdgarmy").OrderBy(p => p).ToArray();
        if (armies.Length == 0) { Console.Error.WriteLine($"No .fdgarmy files in {pool}"); return 2; }
        bool triangle = args.Contains("--triangle");
        for (int i = 0; i < armies.Length; i++)
            for (int j = triangle ? i : 0; j < armies.Length; j++)
                matchups.Add(Matchup.OneVsOne(armies[i], armies[j]));
    }
    else if (a != null && b != null)
    {
        matchups.Add(Matchup.OneVsOne(a, b));
    }
    else
    {
        Console.Error.WriteLine("bench needs --a and --b, or --pool, or --panel. See 'fdglab' for usage.");
        return 2;
    }

    if (!TryProfileArg(args, "--profile-a", out FDG.Ai.EAiProfile benchProfileA) ||
        !TryProfileArg(args, "--profile-b", out FDG.Ai.EAiProfile benchProfileB))
        return 2;

    if (!TryApplyWeights(args)) return 2;

    var options = new BenchmarkOptions(
        Matchups: matchups,
        GamesPerMatchup: IntArg(args, "--games", 200),
        SeedBase: IntArg(args, "--seed-base", 1000),
        DegreeOfParallelism: IntArg(args, "--dop", Math.Min(16, Environment.ProcessorCount)),
        WatchdogSeconds: IntArg(args, "--timeout", 120),
        Randomness: Arg(args, "--dice") == "probabilistic" ? ERandomnessType.Probabilistic : ERandomnessType.Realistic,
        OutDir: Arg(args, "--out") ?? Path.Combine("FdgLab", "reports"),
        ProfileA: benchProfileA,
        ProfileB: benchProfileB,
        PauseFilePath: Arg(args, "--pause-file"),
        DumpLogsDir: Arg(args, "--dump-logs"),
        Trace: args.Contains("--trace"),
        WeightOverrides: Arg(args, "--weights"));

    return await Benchmark.RunAsync(options);
}

static async Task<int> RunSmoke(string[] args)
{
    if (!TryProfileArg(args, "--profile-a", out FDG.Ai.EAiProfile profileA) ||
        !TryProfileArg(args, "--profile-b", out FDG.Ai.EAiProfile profileB))
        return 2;

    if (!TryApplyWeights(args)) return 2;

    var spec = GameSpec.TwoPlayer(
        Armies.LoadSlot(Arg(args, "--a") ?? Armies.BuiltinSpec) with { Profile = profileA },
        Armies.LoadSlot(Arg(args, "--b") ?? Armies.BuiltinSpec) with { Profile = profileB },
        seed: IntArg(args, "--seed", 42));

    // --repeat N: play the SAME spec N times in one process. Any variation between iterations is a
    // determinism bug — either leaked cross-game state or in-game nondeterminism (#193 contract).
    // --dump-logs DIR: write each iteration's full game log for transcript diffing.
    int repeat = IntArg(args, "--repeat", 1);
    string? dumpDir = Arg(args, "--dump-logs");
    bool trace = args.Contains("--trace"); // #198: dump the position-write trace alongside the log
    bool logDecisions = args.Contains("--log-decisions"); // #191 tooling: decision replay
    if (logDecisions && dumpDir == null)
    {
        Console.Error.WriteLine("--log-decisions needs --dump-logs DIR (the narration goes into the game log).");
        return 2;
    }
    if (dumpDir != null)
    {
        Directory.CreateDirectory(dumpDir);
        spec = spec with { CaptureLog = true, Trace = trace, LogDecisions = logDecisions };
    }

    bool anyFault = false;
    for (int i = 0; i < repeat; i++)
    {
        GameRecord record = await GameRunner.RunGameAsync(spec);
        if (dumpDir != null && record.Log != null)
            File.WriteAllLines(Path.Combine(dumpDir, $"game_{i:D2}_{record.Result.Outcome}.log"), record.Log);
        if (dumpDir != null && record.Trace != null)
            File.WriteAllLines(Path.Combine(dumpDir, $"game_{i:D2}_{record.Result.Outcome}.trace"), record.Trace);
        Console.WriteLine($"Game result: {record.Result.ToSummaryLine()}");
        if (repeat == 1)
            Console.WriteLine($"winner_army={record.WinnerArmy ?? "none"} wall={record.WallClock.TotalMilliseconds:F0}ms " +
                              $"decisions={record.Decisions.Count} (mean {record.Decisions.MeanMs:F2}ms, p95 {record.Decisions.P95Ms:F1}ms)");
        anyFault |= record.Result.Outcome == EGameOutcome.Fault;
    }
    return anyFault ? 1 : 0;
}

static async Task<int> RunProbes(string[] args)
{
    if (args.Contains("--feasibility"))
        return await RunFeasibilityProbe(args);

    // Scaffold (#194): scenario probes are hand-authored states with one known-best decision, scored
    // automatically. They arrive as ScenarioCompiler JSONs under FdgLab/probes/ once there is a
    // Tactician whose choices are worth scoring (plan sec. 6.2).
    string probesDir = Path.Combine("FdgLab", "probes");
    int count = Directory.Exists(probesDir) ? Directory.GetFiles(probesDir, "*.json").Length : 0;
    Console.WriteLine($"{count} probe(s) found in {probesDir}. Scenario probes arrive with A4+; " +
        "the generator feasibility metric runs now via 'probes --feasibility'.");
    return 0;
}

// #191 A3 gate metric: real solo-rules games, with the MacroActionGenerator shadow-run at every
// movement decision. Decision-neutral: the solo bot still plays, so games match the benchmark.
static async Task<int> RunFeasibilityProbe(string[] args)
{
    int games = IntArg(args, "--games", 20);
    int seedBase = IntArg(args, "--seed-base", 1000);
    var shadow = new FeasibilityShadow();

    var armyA = Armies.LoadSlot(Arg(args, "--a") ?? Armies.BuiltinSpec);
    var armyB = Armies.LoadSlot(Arg(args, "--b") ?? Armies.BuiltinSpec);

    int faults = 0;
    await Parallel.ForEachAsync(Enumerable.Range(0, games),
        new ParallelOptions { MaxDegreeOfParallelism = Math.Min(16, Environment.ProcessorCount) },
        async (i, _) =>
        {
            GameRecord record = await GameRunner.RunGameAsync(
                GameSpec.TwoPlayer(armyA, armyB, seed: seedBase + i),
                (registry, aiGame) => shadow.Wrap(registry, aiGame.TableState));
            if (record.Result.Outcome == EGameOutcome.Fault) Interlocked.Increment(ref faults);
        });

    double pct = shadow.Fraction * 100.0;
    Console.WriteLine($"games={games} (faults={faults}) activations={shadow.Activations} " +
        $"with_non_hold_candidate={shadow.WithNonHoldCandidate} generator_faults={shadow.GeneratorFaults}");
    Console.WriteLine($"feasibility={pct:F1}% (gate: >= 95%) -> {(pct >= 95.0 ? "PASS" : "FAIL")}");
    return pct >= 95.0 ? 0 : 1;
}

static string? Arg(string[] args, string name)
{
    int i = Array.IndexOf(args, name);
    return i >= 0 && i + 1 < args.Length ? args[i + 1] : null;
}

// #191 automated tuning: apply "Name=Value;Name=Value" onto TacticianWeights before any game
// starts (weights are process-global). Invariant culture; any unparseable pair or unknown field
// name is a hard usage error - a silently-skipped override would corrupt a whole tuning campaign.
static bool TryApplyWeights(string[] args)
{
    string? spec = Arg(args, "--weights");
    if (spec == null) return true;
    foreach (string pair in spec.Split(';', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
    {
        int eq = pair.IndexOf('=');
        string name = eq < 0 ? "" : pair[..eq].Trim();
        if (eq < 0
            || !float.TryParse(pair[(eq + 1)..], System.Globalization.NumberStyles.Float,
                System.Globalization.CultureInfo.InvariantCulture, out float value)
            || !FDG.Ai.Tactician.TacticianWeights.TrySet(name, value))
        {
            Console.Error.WriteLine($"--weights: cannot apply '{pair}'. Format is Name=Value with " +
                "Name a public static float field of TacticianWeights.");
            return false;
        }
    }
    return true;
}

static int IntArg(string[] args, string name, int fallback) =>
    int.TryParse(Arg(args, name), out int value) ? value : fallback;

// "solorules" / "tactician" (any case) -> profile; absent -> SoloRules; anything else -> usage error.
static bool TryProfileArg(string[] args, string name, out FDG.Ai.EAiProfile profile)
{
    string? raw = Arg(args, name);
    profile = FDG.Ai.EAiProfile.SoloRules;
    if (raw == null) return true;
    if (Enum.TryParse(raw, ignoreCase: true, out profile)) return true;
    Console.Error.WriteLine($"Unknown AI profile '{raw}' for {name}. Known: " +
        string.Join(", ", Enum.GetNames<FDG.Ai.EAiProfile>()).ToLowerInvariant());
    return false;
}
