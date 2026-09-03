using System.Security.Cryptography;
using System.Text;
using FDG;

namespace FdgLab;

/// <summary>
/// One pairing to be played N times with side-swapping. Each side is a ROSTER (usually one army;
/// two for a 2v2 panel cell, B+C campaign section 5) - every existing 1v1 caller builds a
/// single-element roster via <see cref="OneVsOne"/>, so its report labels, CSV rows and outcome
/// hash are byte-identical to before this generalization.
/// </summary>
public sealed record Matchup(IReadOnlyList<string> SideA, IReadOnlyList<string> SideB)
{
    public static Matchup OneVsOne(string specA, string specB) => new(new[] { specA }, new[] { specB });
}

public sealed record BenchmarkOptions(
    IReadOnlyList<Matchup> Matchups,
    int GamesPerMatchup,          // total games; half the seeds, each played once per side assignment
    int SeedBase,
    int DegreeOfParallelism,
    int WatchdogSeconds,
    ERandomnessType Randomness,
    string OutDir,
    // #191 A4: which AI drives each side. The profile binds to its ARMY (A or B) - the side swap
    // still exchanges slots, so slot advantage cancels while "profile A playing army A" stays the
    // measured quantity. Tactician-vs-solo comparisons set these differently.
    FDG.Ai.EAiProfile ProfileA = FDG.Ai.EAiProfile.SoloRules,
    FDG.Ai.EAiProfile ProfileB = FDG.Ai.EAiProfile.SoloRules,
    // #210: per-game log dumping for divergence hunting. Filenames are stable across runs (no
    // outcome in the name), so two same-options runs diff file by file; Trace additionally writes
    // the #198 position-write trace next to each log.
    string? DumpLogsDir = null,
    bool Trace = false,
    // #191 automated tuning: the raw --weights spec already applied to TacticianWeights, recorded
    // in the report header so a tuned run can never be mistaken for a default one.
    string? WeightOverrides = null,
    // B+C campaign: touch this file to pause new game starts (a soak/data-gen driver sharing the
    // box signals through it); already-running games finish normally.
    string? PauseFilePath = null);

/// <summary>
/// The seeded, side-swapped benchmark matrix (#194; plan sec. 6.1). Scoring: for a matchup (A, B),
/// A's score = (A wins + 0.5 * ties) / completed games — faults count for neither side and are
/// reported separately, because a win by opponent-crash is not a win (plan G2).
/// </summary>
public static class Benchmark
{
    public static async Task<int> RunAsync(BenchmarkOptions options)
    {
        Directory.CreateDirectory(options.OutDir);
        bool dump = options.DumpLogsDir != null;
        if (dump) Directory.CreateDirectory(options.DumpLogsDir!);

        // Build every game spec up front: per matchup, seeds seedBase..seedBase+N/2-1, each seed played
        // twice with sides swapped. Same options => same specs => (via #193) same outcomes.
        var work = new List<(Matchup Matchup, int Seed, bool Swapped, GameSpec Spec)>();
        foreach (Matchup matchup in options.Matchups)
        {
            // #392: a FRESH ArmyListFile per game, never one shared instance per matchup. Army
            // creation used to sort the shared file's weapon lists in place (engine-side, fixed
            // there too), and concurrent games racing that sort captured different weapon orders -
            // outcomes then depended on which games overlapped (7/16 flips between GC modes in the
            // repro). Per-game loading makes game isolation structural instead of relying on the
            // engine treating its input as read-only; the extra deserializations are microseconds
            // against a 5s game.
            int seeds = Math.Max(1, options.GamesPerMatchup / 2);
            for (int s = 0; s < seeds; s++)
            {
                int seed = options.SeedBase + s;
                work.Add((matchup, seed, false, BuildSpec(matchup, seed, swapped: false, options, dump)));
                work.Add((matchup, seed, true, BuildSpec(matchup, seed, swapped: true, options, dump)));
            }
        }

        Console.WriteLine($"Benchmark: {options.Matchups.Count} matchup(s), {work.Count} games, " +
                          $"DOP {options.DegreeOfParallelism}, seeds from {options.SeedBase}, {options.Randomness} dice, " +
                          $"A={options.ProfileA} B={options.ProfileB}.");

        var records = new GameRecord[work.Count];
        int done = 0;
        var overall = System.Diagnostics.Stopwatch.StartNew();

        await Parallel.ForEachAsync(
            Enumerable.Range(0, work.Count),
            new ParallelOptions { MaxDegreeOfParallelism = options.DegreeOfParallelism },
            async (i, ct) =>
            {
                await PauseGate.WaitWhilePausedAsync(options.PauseFilePath, ct);
                GameRecord record = await GameRunner.RunGameAsync(work[i].Spec);
                if (dump)
                    record = DumpGameFiles(options.DumpLogsDir!, work[i], record);
                records[i] = record;
                int n = Interlocked.Increment(ref done);
                if (n % 25 == 0 || n == work.Count)
                    Console.WriteLine($"  {n}/{work.Count} games ({overall.Elapsed.TotalSeconds:F0}s)");
            });

        overall.Stop();

        var rows = work.Zip(records, (w, r) => new GameRow(w.Matchup, w.Seed, w.Swapped, r)).ToList();
        string outcomeHash = OutcomeHash(rows);

        string md = WriteMarkdown(rows, options, overall.Elapsed, outcomeHash);
        string csv = WriteCsv(rows, options);
        Console.WriteLine($"Outcome hash: {outcomeHash}");
        Console.WriteLine($"Reports: {md}");
        Console.WriteLine($"         {csv}");

        int faults = rows.Count(r => r.Record.Result.Outcome == EGameOutcome.Fault);
        return faults == rows.Count ? 1 : 0; // all-fault run means the harness itself is broken
    }

    // #392: a FRESH ArmyListFile per game, never one shared instance per matchup (see the comment
    // this replaced, above the old inline build). Team seating: side A occupies the FIRST slot
    // block when not swapped, the SECOND block when swapped - matches GameSpec.TeamGame's
    // convention and reduces to the historical [a,b]/[b2,a2] order for one-army-per-side matchups.
    private static GameSpec BuildSpec(Matchup matchup, int seed, bool swapped, BenchmarkOptions options, bool dump)
    {
        List<SlotSpec> BuildSide(IReadOnlyList<string> specs, FDG.Ai.EAiProfile profile, int team) =>
            specs.Select(spec => Armies.LoadSlot(spec) with { Profile = profile, Team = team }).ToList();

        List<SlotSpec> sideA = BuildSide(matchup.SideA, options.ProfileA, team: swapped ? 1 : 0);
        List<SlotSpec> sideB = BuildSide(matchup.SideB, options.ProfileB, team: swapped ? 0 : 1);
        List<SlotSpec> slots = swapped ? sideB.Concat(sideA).ToList() : sideA.Concat(sideB).ToList();
        return new GameSpec(slots, seed, options.Randomness, options.WatchdogSeconds,
            CaptureLog: dump, Trace: dump && options.Trace);
    }

    // #210: write the game's log/trace the moment it completes and strip them from the kept
    // record, so a full 1800-game run's memory stays flat. The filename is STABLE across runs
    // (matchup + seed + side, never the outcome), so two runs of the same options diff file by
    // file and a flipped game shows up as a content diff, not a missing file.
    private static GameRecord DumpGameFiles(string dir,
        (Matchup Matchup, int Seed, bool Swapped, GameSpec Spec) game, GameRecord record)
    {
        string baseName = $"{Sanitize(string.Join("_", game.Matchup.SideA.Select(Path.GetFileNameWithoutExtension)))}__vs__" +
                          $"{Sanitize(string.Join("_", game.Matchup.SideB.Select(Path.GetFileNameWithoutExtension)))}" +
                          $"__s{game.Seed}_{(game.Swapped ? "swp" : "fwd")}";
        if (record.Log != null)
            File.WriteAllLines(Path.Combine(dir, baseName + ".log"), record.Log);
        if (record.Trace != null)
            File.WriteAllLines(Path.Combine(dir, baseName + ".trace"), record.Trace);
        return record with { Log = null, Trace = null };
    }

    private static string Sanitize(string label)
    {
        var sb = new StringBuilder(label.Length);
        foreach (char c in label)
            sb.Append(char.IsLetterOrDigit(c) || c == '-' ? c : '_');
        return sb.ToString();
    }

    private sealed record GameRow(Matchup Matchup, int Seed, bool Swapped, GameRecord Record)
    {
        public string LabelA => string.Join("+", Matchup.SideA.Select(Path.GetFileNameWithoutExtension));
        public string LabelB => string.Join("+", Matchup.SideB.Select(Path.GetFileNameWithoutExtension));

        // Slot-block membership depends on the side assignment (BuildSpec's seating convention);
        // reduces to the historical WinnerSlot==(Swapped?1:0) check when each side is one army.
        private int SideACount => Matchup.SideA.Count;
        private int SideBCount => Matchup.SideB.Count;
        public IEnumerable<int> SideASlots => Swapped ? Enumerable.Range(SideBCount, SideACount) : Enumerable.Range(0, SideACount);
        public IEnumerable<int> SideBSlots => Swapped ? Enumerable.Range(0, SideBCount) : Enumerable.Range(SideACount, SideBCount);

        public bool AWon => Record.WinnerSlot.HasValue && SideASlots.Contains(Record.WinnerSlot.Value);
        public bool BWon => Record.WinnerSlot.HasValue && SideBSlots.Contains(Record.WinnerSlot.Value);

        // Summed over the side's own slots this game - equals the single slot's score when a side
        // is one army (the historical 1v1 case).
        public int ScoreA => SideASlots.Where(i => i < Record.Result.Scores.Count).Sum(i => Record.Result.Scores[i].ObjectiveCount);
        public int ScoreB => SideBSlots.Where(i => i < Record.Result.Scores.Count).Sum(i => Record.Result.Scores[i].ObjectiveCount);
    }

    /// <summary>
    /// SHA-256 over the ordered outcome tuples — everything deterministic, nothing timing-derived.
    /// Two runs with identical options must print identical hashes (#193); this is the single value
    /// the reproducibility gate compares.
    /// </summary>
    private static string OutcomeHash(IReadOnlyList<GameRow> rows)
    {
        var sb = new StringBuilder();
        foreach (GameRow row in rows)
        {
            GameResult result = row.Record.Result;
            string scores = string.Join(",", result.Scores.Select(s => s.ObjectiveCount));
            sb.Append($"{string.Join(";", row.Matchup.SideA)}|{string.Join(";", row.Matchup.SideB)}|{row.Seed}|{row.Swapped}|" +
                      $"{result.Outcome}|{row.Record.WinnerSlot?.ToString() ?? "-"}|{scores}|{result.RoundsPlayed}\n");
        }
        return Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(sb.ToString())))[..16];
    }

    private static string WriteMarkdown(IReadOnlyList<GameRow> rows, BenchmarkOptions options,
        TimeSpan elapsed, string outcomeHash)
    {
        var sb = new StringBuilder();
        sb.AppendLine("# FdgLab benchmark report");
        sb.AppendLine();
        sb.AppendLine($"- Games: {rows.Count} | Seeds from {options.SeedBase} | Dice: {options.Randomness} | DOP: {options.DegreeOfParallelism}");
        sb.AppendLine($"- Profiles: A = {options.ProfileA}, B = {options.ProfileB}");
        if (options.WeightOverrides != null)
            sb.AppendLine($"- Weight overrides: `{options.WeightOverrides}`");
        sb.AppendLine($"- Outcome hash (deterministic): `{outcomeHash}`");
        sb.AppendLine();
        sb.AppendLine("## Matchups");
        sb.AppendLine();
        sb.AppendLine("| Matchup | Games | A score | A wins | B wins | Ties | Faults |");
        sb.AppendLine("|---|---|---|---|---|---|---|");

        foreach (var group in rows.GroupBy(r => r.Matchup))
        {
            var games = group.ToList();
            int faults = games.Count(g => g.Record.Result.Outcome == EGameOutcome.Fault);
            int completed = games.Count - faults;
            int aWins = games.Count(g => g.AWon);
            int bWins = games.Count(g => g.BWon);
            int ties = games.Count(g => g.Record.Result.Outcome == EGameOutcome.Tie);
            double aScore = completed == 0 ? 0 : (aWins + 0.5 * ties) / completed;
            sb.AppendLine($"| {games[0].LabelA} vs {games[0].LabelB} | {games.Count} | " +
                          $"{aScore:P1} | {aWins} | {bWins} | {ties} | {faults} |");
        }

        // Faults are listed individually with their messages: a benchmark number is only trusted with
        // the failures visible (plan G2), and the message distinguishes engine faults from watchdog kills.
        var faultRows = rows.Where(r => r.Record.Result.Outcome == EGameOutcome.Fault).ToList();
        if (faultRows.Count > 0)
        {
            sb.AppendLine();
            sb.AppendLine("## Faults");
            sb.AppendLine();
            foreach (GameRow row in faultRows)
                sb.AppendLine($"- {row.LabelA} vs {row.LabelB}, seed {row.Seed}, swapped={row.Swapped}: {row.Record.Result.Message}");
        }

        // Performance lives in its own section: wall times vary run to run, outcomes must not.
        var wallMs = rows.Select(r => r.Record.WallClock.TotalMilliseconds).OrderBy(x => x).ToArray();
        var decisions = rows.Select(r => r.Record.Decisions).ToArray();
        sb.AppendLine();
        sb.AppendLine("## Performance (varies per run - not part of the outcome hash)");
        sb.AppendLine();
        sb.AppendLine($"- Total wall clock: {elapsed.TotalSeconds:F1}s | Throughput: {rows.Count / Math.Max(0.001, elapsed.TotalSeconds):F2} games/s ({rows.Count / Math.Max(0.001, elapsed.TotalHours):F0}/hour)");
        sb.AppendLine($"- Per-game wall: mean {wallMs.Average():F0}ms | p95 {wallMs[(int)Math.Min(wallMs.Length - 1, Math.Ceiling(wallMs.Length * 0.95) - 1)]:F0}ms | max {wallMs.Max():F0}ms");
        sb.AppendLine($"- Decisions per game: mean {decisions.Average(d => d.Count):F0} | decision mean {decisions.Average(d => d.MeanMs):F2}ms | worst p95 {decisions.Max(d => d.P95Ms):F1}ms");
        sb.AppendLine($"- Timeouts: {rows.Count(r => r.Record.TimedOut)}");

        string path = Path.Combine(options.OutDir, "bench.md");
        File.WriteAllText(path, sb.ToString());
        return Path.GetFullPath(path);
    }

    private static string WriteCsv(IReadOnlyList<GameRow> rows, BenchmarkOptions options)
    {
        var sb = new StringBuilder();
        sb.AppendLine("army_a,army_b,seed,swapped,outcome,winner_army,score_a,score_b,rounds,wall_ms,decisions,decision_mean_ms");
        foreach (GameRow row in rows)
        {
            GameResult result = row.Record.Result;
            // Each side's score is summed over its own slots this game (its team, for a panel
            // cell) - equals the single slot's score for the historical one-army-per-side case.
            sb.AppendLine($"{Csv(row.LabelA)},{Csv(row.LabelB)},{row.Seed},{row.Swapped}," +
                          $"{result.Outcome},{Csv(row.Record.WinnerArmy ?? "")},{row.ScoreA},{row.ScoreB},{result.RoundsPlayed}," +
                          $"{row.Record.WallClock.TotalMilliseconds:F0},{row.Record.Decisions.Count},{row.Record.Decisions.MeanMs:F2}");
        }

        string path = Path.Combine(options.OutDir, "bench.csv");
        File.WriteAllText(path, sb.ToString());
        return Path.GetFullPath(path);

        static string Csv(string s) => s.Contains(',') ? $"\"{s}\"" : s;
    }
}
