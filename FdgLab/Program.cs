using FDG;
using FdgLab;

// FdgLab (#194): self-play harness for the Tactician AI effort (#191). Engine-only dependency; see
// docs/ai-agent-plan.md sections 6-7 for what each command is for.

return args.FirstOrDefault() switch
{
    "bench" => await RunBench(args.Skip(1).ToArray()),
    "smoke" => await RunSmoke(args.Skip(1).ToArray()),
    "probes" => RunProbes(),
    _ => Usage(),
};

static int Usage()
{
    Console.WriteLine("""
        FdgLab - self-play harness (#194)

        Commands:
          bench   --a <army> --b <army> | --pool <dir>   seeded, side-swapped benchmark matrix
                  [--games N]        total games per matchup (default 200; played as N/2 seeds x 2 sides)
                  [--seed-base S]    first seed (default 1000)
                  [--dop D]          concurrent games (default: min(16, cores))
                  [--timeout T]      per-game watchdog seconds (default 120)
                  [--dice realistic|probabilistic]   (default realistic)
                  [--out DIR]        report directory (default FdgLab/reports)
          smoke   [--seed S] [--a <army>] [--b <army>]   one game, prints the record
          probes  strategy-probe scaffold (probes authored from Phase A onward)

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

    var matchups = new List<Matchup>();
    if (pool != null)
    {
        // Every unordered pair including self-mirrors; mirrors are the symmetry baseline.
        string[] armies = Directory.GetFiles(pool, "*.fdgarmy").OrderBy(p => p).ToArray();
        if (armies.Length == 0) { Console.Error.WriteLine($"No .fdgarmy files in {pool}"); return 2; }
        for (int i = 0; i < armies.Length; i++)
            for (int j = i; j < armies.Length; j++)
                matchups.Add(new Matchup(armies[i], armies[j]));
    }
    else if (a != null && b != null)
    {
        matchups.Add(new Matchup(a, b));
    }
    else
    {
        Console.Error.WriteLine("bench needs --a and --b, or --pool. See 'fdglab' for usage.");
        return 2;
    }

    var options = new BenchmarkOptions(
        Matchups: matchups,
        GamesPerMatchup: IntArg(args, "--games", 200),
        SeedBase: IntArg(args, "--seed-base", 1000),
        DegreeOfParallelism: IntArg(args, "--dop", Math.Min(16, Environment.ProcessorCount)),
        WatchdogSeconds: IntArg(args, "--timeout", 120),
        Randomness: Arg(args, "--dice") == "probabilistic" ? ERandomnessType.Probabilistic : ERandomnessType.Realistic,
        OutDir: Arg(args, "--out") ?? Path.Combine("FdgLab", "reports"));

    return await Benchmark.RunAsync(options);
}

static async Task<int> RunSmoke(string[] args)
{
    var spec = GameSpec.TwoPlayer(
        Armies.LoadSlot(Arg(args, "--a") ?? Armies.BuiltinSpec),
        Armies.LoadSlot(Arg(args, "--b") ?? Armies.BuiltinSpec),
        seed: IntArg(args, "--seed", 42));

    // --repeat N: play the SAME spec N times in one process. Any variation between iterations is a
    // determinism bug — either leaked cross-game state or in-game nondeterminism (#193 contract).
    // --dump-logs DIR: write each iteration's full game log for transcript diffing.
    int repeat = IntArg(args, "--repeat", 1);
    string? dumpDir = Arg(args, "--dump-logs");
    if (dumpDir != null)
    {
        Directory.CreateDirectory(dumpDir);
        spec = spec with { CaptureLog = true };
    }

    bool anyFault = false;
    for (int i = 0; i < repeat; i++)
    {
        GameRecord record = await GameRunner.RunGameAsync(spec);
        if (dumpDir != null && record.Log != null)
            File.WriteAllLines(Path.Combine(dumpDir, $"game_{i:D2}_{record.Result.Outcome}.log"), record.Log);
        Console.WriteLine($"Game result: {record.Result.ToSummaryLine()}");
        if (repeat == 1)
            Console.WriteLine($"winner_army={record.WinnerArmy ?? "none"} wall={record.WallClock.TotalMilliseconds:F0}ms " +
                              $"decisions={record.Decisions.Count} (mean {record.Decisions.MeanMs:F2}ms, p95 {record.Decisions.P95Ms:F1}ms)");
        anyFault |= record.Result.Outcome == EGameOutcome.Fault;
    }
    return anyFault ? 1 : 0;
}

static int RunProbes()
{
    // Scaffold only (#194): probes are hand-authored scenarios with one known-best decision, scored
    // automatically. They arrive with Phase A (plan sec. 6.2) as ScenarioCompiler JSONs under
    // FdgLab/probes/ once there is a Tactician whose choices are worth scoring.
    string probesDir = Path.Combine("FdgLab", "probes");
    int count = Directory.Exists(probesDir) ? Directory.GetFiles(probesDir, "*.json").Length : 0;
    Console.WriteLine($"{count} probe(s) found in {probesDir}. Probes are authored from Phase A (see docs/ai-agent-plan.md sec. 6.2).");
    return 0;
}

static string? Arg(string[] args, string name)
{
    int i = Array.IndexOf(args, name);
    return i >= 0 && i + 1 < args.Length ? args[i + 1] : null;
}

static int IntArg(string[] args, string name, int fallback) =>
    int.TryParse(Arg(args, name), out int value) ? value : fallback;
