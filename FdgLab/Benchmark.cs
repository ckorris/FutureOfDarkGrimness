using System.Security.Cryptography;
using System.Text;
using FDG;

namespace FdgLab;

/// <summary>One army pairing to be played N times with side-swapping.</summary>
public sealed record Matchup(string SpecA, string SpecB);

public sealed record BenchmarkOptions(
    IReadOnlyList<Matchup> Matchups,
    int GamesPerMatchup,          // total games; half the seeds, each played once per side assignment
    int SeedBase,
    int DegreeOfParallelism,
    int WatchdogSeconds,
    ERandomnessType Randomness,
    string OutDir);

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

        // Build every game spec up front: per matchup, seeds seedBase..seedBase+N/2-1, each seed played
        // twice with sides swapped. Same options => same specs => (via #193) same outcomes.
        var work = new List<(Matchup Matchup, int Seed, bool Swapped, GameSpec Spec)>();
        foreach (Matchup matchup in options.Matchups)
        {
            SlotSpec a = Armies.LoadSlot(matchup.SpecA);
            SlotSpec b = Armies.LoadSlot(matchup.SpecB);
            int seeds = Math.Max(1, options.GamesPerMatchup / 2);
            for (int s = 0; s < seeds; s++)
            {
                int seed = options.SeedBase + s;
                work.Add((matchup, seed, false, new GameSpec(new[] { a, b }, seed, options.Randomness, options.WatchdogSeconds)));
                work.Add((matchup, seed, true, new GameSpec(new[] { b, a }, seed, options.Randomness, options.WatchdogSeconds)));
            }
        }

        Console.WriteLine($"Benchmark: {options.Matchups.Count} matchup(s), {work.Count} games, " +
                          $"DOP {options.DegreeOfParallelism}, seeds from {options.SeedBase}, {options.Randomness} dice.");

        var records = new GameRecord[work.Count];
        int done = 0;
        var overall = System.Diagnostics.Stopwatch.StartNew();

        await Parallel.ForEachAsync(
            Enumerable.Range(0, work.Count),
            new ParallelOptions { MaxDegreeOfParallelism = options.DegreeOfParallelism },
            async (i, _) =>
            {
                records[i] = await GameRunner.RunGameAsync(work[i].Spec);
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

    private sealed record GameRow(Matchup Matchup, int Seed, bool Swapped, GameRecord Record)
    {
        // Army A's slot depends on the side assignment; ties/faults have no winner.
        public string LabelA => Path.GetFileNameWithoutExtension(Matchup.SpecA);
        public string LabelB => Path.GetFileNameWithoutExtension(Matchup.SpecB);
        public bool AWon => Record.WinnerSlot == (Swapped ? 1 : 0);
        public bool BWon => Record.WinnerSlot == (Swapped ? 0 : 1);
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
            sb.Append($"{row.Matchup.SpecA}|{row.Matchup.SpecB}|{row.Seed}|{row.Swapped}|" +
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
            // Scores are per slot in slot order; map back to (A, B) through the side assignment.
            int scoreSlot0 = result.Scores.Count > 0 ? result.Scores[0].ObjectiveCount : 0;
            int scoreSlot1 = result.Scores.Count > 1 ? result.Scores[1].ObjectiveCount : 0;
            int scoreA = row.Swapped ? scoreSlot1 : scoreSlot0;
            int scoreB = row.Swapped ? scoreSlot0 : scoreSlot1;
            sb.AppendLine($"{Csv(row.LabelA)},{Csv(row.LabelB)},{row.Seed},{row.Swapped}," +
                          $"{result.Outcome},{Csv(row.Record.WinnerArmy ?? "")},{scoreA},{scoreB},{result.RoundsPlayed}," +
                          $"{row.Record.WallClock.TotalMilliseconds:F0},{row.Record.Decisions.Count},{row.Record.Decisions.MeanMs:F2}");
        }

        string path = Path.Combine(options.OutDir, "bench.csv");
        File.WriteAllText(path, sb.ToString());
        return Path.GetFullPath(path);

        static string Csv(string s) => s.Contains(',') ? $"\"{s}\"" : s;
    }
}
