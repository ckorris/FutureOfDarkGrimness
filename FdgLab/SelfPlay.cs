using System.Text.Json;
using System.Text.Json.Serialization;
using FDG;
using FDG.Ai;
using FdgLab.Export;

namespace FdgLab;

public sealed class MixEntry
{
    public string Name { get; set; } = "";
    public double Weight { get; set; }
    public string ProfileA { get; set; } = "SoloRules";
    public string ProfileB { get; set; } = "SoloRules";
}

public sealed class LevelEntry
{
    public string Panel { get; set; } = "";
    public double Weight { get; set; }
}

public sealed class MixConfig
{
    public List<MixEntry> ProfileMix { get; set; } = new();
    public List<LevelEntry> Levels { get; set; } = new();

    private static readonly JsonSerializerOptions Options = new() { PropertyNameCaseInsensitive = true };

    public static MixConfig Load(string path)
    {
        MixConfig? config = JsonSerializer.Deserialize<MixConfig>(File.ReadAllText(path), Options);
        if (config == null || config.ProfileMix.Count == 0 || config.Levels.Count == 0)
            throw new InvalidOperationException($"{path}: mix config missing profileMix/levels.");
        return config;
    }
}

public sealed record SelfPlayOptions(
    string OutDir,
    string MixPath,
    int Dop,
    int SeedBase,
    int GamesPerFile,
    double EntitySampleRate,
    int BoundarySampleEvery, // keep 1 row in N (uniform)
    int WatchdogSeconds,
    string? PauseFilePath,
    int? MaxBatches = null); // null = run forever (the real launch); set for the verification sample

/// <summary>
/// The C1 self-play driver (#191 campaign step 4): samples (points level, shape, armies, profiles)
/// from a mix config, plays games with <see cref="SelfPlayGameRunner"/>, joins labels at game end,
/// and writes gzipped JSONL in fixed-size batches (schema sec 6). One batch = one file = the unit
/// of crash-tolerant restart: a batch is generated fully in-memory and only written (atomically,
/// via a .tmp rename) once every game in it has finished, so any file present in the output
/// directory without a .tmp sibling is complete and never rewritten.
/// </summary>
public static class SelfPlay
{
    public static async Task<int> RunAsync(SelfPlayOptions options)
    {
        Directory.CreateDirectory(options.OutDir);
        MixConfig mix = MixConfig.Load(options.MixPath);
        IReadOnlyList<Pool.HeldOutEntry> heldOut = Pool.LoadHeldOut() ?? Array.Empty<Pool.HeldOutEntry>();
        string engineCommit = GitCommit("FutureOfDarkGrimness");
        string superCommit = GitCommit(".");

        int startBatch = DetermineStartBatch(options.OutDir, options.SeedBase, options.GamesPerFile);
        Console.WriteLine($"selfplay: starting at batch {startBatch} (seed {options.SeedBase + startBatch * options.GamesPerFile}), " +
            $"{options.GamesPerFile} games/file, DOP {options.Dop}, out={options.OutDir}");

        int batch = startBatch;
        while (options.MaxBatches == null || batch < startBatch + options.MaxBatches)
        {
            int seedStart = options.SeedBase + batch * options.GamesPerFile;
            var overall = System.Diagnostics.Stopwatch.StartNew();
            var completedRows = new System.Collections.Concurrent.ConcurrentBag<ExportRow>();
            var completedEntities = new System.Collections.Concurrent.ConcurrentBag<(int, string, List<float[]>)>();
            var encoderMs = new System.Collections.Concurrent.ConcurrentBag<double>();
            // #191 step 4 provenance gap: a batch mixes many matchups/profiles (the campaign doc's
            // per-game sampling), so the file HEADER's single profile_a/army_a/etc fields (schema
            // sec 6) can only describe the batch's first game. One "game" record per completed game
            // restores real per-row provenance (G9) without changing the header's documented shape.
            var completedGames = new System.Collections.Concurrent.ConcurrentBag<JsonlGzWriter.GameLine>();
            int played = 0, faults = 0;
            (GameSpec Spec, string Level, bool Shape2v2, string ArmyA, string ArmyB, EAiProfile ProfileA,
                EAiProfile ProfileB, bool EntitySampled, float TotalPoints)? firstSample = null;

            await Parallel.ForEachAsync(Enumerable.Range(0, options.GamesPerFile),
                new ParallelOptions { MaxDegreeOfParallelism = options.Dop },
                async (i, ct) =>
                {
                    await PauseGate.WaitWhilePausedAsync(options.PauseFilePath, ct);
                    int seed = seedStart + i;
                    var sample = SampleGame(mix, heldOut, seed, options.EntitySampleRate);
                    if (sample == null) return; // every panel cell for the drawn level was held out (should not happen)
                    if (firstSample == null) firstSample = sample;

                    (GameResult result, GameExportState export, IReadOnlyList<PlayerID> slotIds,
                        IReadOnlyList<int> slotTeams) = await SelfPlayGameRunner.RunGameAsync(
                        sample.Value.Spec, sample.Value.EntitySampled, sample.Value.TotalPoints);

                    Interlocked.Increment(ref played);
                    encoderMs.Add(export.EncoderMsMean);
                    if (result.Outcome is EGameOutcome.Fault or EGameOutcome.Disconnect)
                    {
                        Interlocked.Increment(ref faults);
                        return;
                    }

                    int objectiveCount = 5; // overwritten per row from its own objective_count_norm feature below
                    foreach (ExportRow row in export.Rows)
                    {
                        if (row.Boundary % options.BoundarySampleEvery != 0) continue; // sec 5b uniform subsample
                        objectiveCount = Math.Max(1, (int)Math.Round(row.Features[2] * 5f));
                        int team = slotTeams[row.ActingSlot];
                        int ownSum = 0, bestEnemy = 0;
                        var enemySums = new Dictionary<int, int>();
                        for (int s = 0; s < result.Scores.Count; s++)
                        {
                            int t = slotTeams[s];
                            int score = result.Scores[s].ObjectiveCount;
                            if (t == team) ownSum += score;
                            else enemySums[t] = enemySums.GetValueOrDefault(t) + score;
                        }
                        bestEnemy = enemySums.Count == 0 ? 0 : enemySums.Values.Max();
                        row.ObjDiffNorm = Math.Clamp((ownSum - bestEnemy) / (float)objectiveCount, -1f, 1f);
                        row.RoundsPlayed = result.RoundsPlayed;
                        row.Result = result.Outcome == EGameOutcome.Tie ? 0.5f
                            : result.WinnerPlayers.Contains(slotIds[row.ActingSlot]) ? 1f : 0f;
                        completedRows.Add(row);
                    }
                    if (sample.Value.EntitySampled)
                        foreach ((int b, List<float[]> ent) in export.EntityRows)
                            completedEntities.Add((b, export.GameId, ent));

                    completedGames.Add(new JsonlGzWriter.GameLine(export.GameId, seed, sample.Value.Level,
                        sample.Value.Shape2v2 ? "2v2" : "1v1", sample.Value.ArmyA, sample.Value.ArmyB,
                        sample.Value.ProfileA.ToString(), sample.Value.ProfileB.ToString(),
                        result.Outcome.ToString(), result.RoundsPlayed));
                });

            if (firstSample == null)
            {
                Console.Error.WriteLine("selfplay: no games sampled this batch (mix/pool misconfigured?) - stopping.");
                return 2;
            }

            double encMean = encoderMs.IsEmpty ? 0 : encoderMs.Average();
            var header = new JsonlGzWriter.FileHeader(
                PositionEncoderSchema, engineCommit, superCommit, DateTime.UtcNow.ToString("o"),
                firstSample.Value.ProfileA.ToString(), firstSample.Value.ProfileB.ToString(),
                $"{seedStart}-{seedStart + options.GamesPerFile - 1}",
                firstSample.Value.Shape2v2 ? "2v2" : "1v1", firstSample.Value.Level,
                firstSample.Value.ArmyA, firstSample.Value.ArmyB, false,
                options.EntitySampleRate, 1.0 / options.BoundarySampleEvery, encMean);

            string fileName = $"selfplay_{seedStart:D8}-{seedStart + options.GamesPerFile - 1:D8}";
            string written = JsonlGzWriter.Write(options.OutDir, fileName, header, completedRows,
                completedEntities.Select(e => (e.Item1, e.Item2, e.Item3)), completedGames);

            Console.WriteLine($"batch {batch}: seeds {seedStart}-{seedStart + options.GamesPerFile - 1}: " +
                $"{played} played ({faults} faults), {completedRows.Count} rows -> {written} ({overall.Elapsed.TotalSeconds:F0}s)");
            batch++;
        }

        return 0;
    }

    public const int PositionEncoderSchema = FDG.Ai.Tactician.Learning.PositionEncoder.SchemaVersion;

    private static (GameSpec Spec, string Level, bool Shape2v2, string ArmyA, string ArmyB,
        EAiProfile ProfileA, EAiProfile ProfileB, bool EntitySampled, float TotalPoints)? SampleGame(
        MixConfig mix, IReadOnlyList<Pool.HeldOutEntry> heldOut, int seed, double entitySampleRate)
    {
        var rnd = new Random(seed);
        MixEntry mixEntry = WeightedPick(mix.ProfileMix, e => e.Weight, rnd);
        LevelEntry levelEntry = WeightedPick(mix.Levels, e => e.Weight, rnd);

        IReadOnlyList<Matchup>? cells = Pool.LoadPanel(levelEntry.Panel);
        if (cells == null || cells.Count == 0) return null;
        List<Matchup> allowed = cells.Where(c => !IsHeldOut(c, heldOut)).ToList();
        if (allowed.Count == 0) return null;
        Matchup matchup = allowed[rnd.Next(allowed.Count)];

        bool swap = rnd.NextDouble() < 0.5; // #392/Program.cs lesson: don't always bind profile A to side A
        EAiProfile profileA = Enum.Parse<EAiProfile>(mixEntry.ProfileA, ignoreCase: true);
        EAiProfile profileB = Enum.Parse<EAiProfile>(mixEntry.ProfileB, ignoreCase: true);
        if (swap) (profileA, profileB) = (profileB, profileA);

        bool entitySampled = rnd.NextDouble() < entitySampleRate;
        bool shape2v2 = matchup.SideA.Count > 1 || matchup.SideB.Count > 1;

        List<SlotSpec> BuildSide(IReadOnlyList<string> specs, EAiProfile profile, int team) =>
            specs.Select(spec => Armies.LoadSlot(spec) with { Profile = profile, Team = team }).ToList();

        List<SlotSpec> sideA = BuildSide(matchup.SideA, profileA, 0);
        List<SlotSpec> sideB = BuildSide(matchup.SideB, profileB, 1);
        float totalPoints = sideA.Concat(sideB).Sum(s => s.Army.PointsLimit);

        GameSpec spec = shape2v2
            ? GameSpec.TeamGame(sideA, sideB, seed)
            : GameSpec.TwoPlayer(sideA[0], sideB[0], seed);
        spec = spec with { WatchdogSeconds = shape2v2 ? 600 : 120 };

        string armyA = string.Join("+", matchup.SideA.Select(Path.GetFileNameWithoutExtension));
        string armyB = string.Join("+", matchup.SideB.Select(Path.GetFileNameWithoutExtension));
        return (spec, levelEntry.Panel, shape2v2, armyA, armyB, profileA, profileB, entitySampled, totalPoints);
    }

    private static bool IsHeldOut(Matchup m, IReadOnlyList<Pool.HeldOutEntry> heldOut)
    {
        bool SameSet(IReadOnlyList<string> a, IReadOnlyList<string> b) =>
            a.Count == b.Count && a.All(x => b.Any(y => NormalizedPath(x) == NormalizedPath(y)));
        foreach (Pool.HeldOutEntry entry in heldOut)
        {
            if ((SameSet(m.SideA, entry.SideA) && SameSet(m.SideB, entry.SideB))
                || (SameSet(m.SideA, entry.SideB) && SameSet(m.SideB, entry.SideA)))
                return true;
        }
        return false;
    }

    private static string NormalizedPath(string path) => Path.GetFileName(path).Trim().ToLowerInvariant();

    private static T WeightedPick<T>(IReadOnlyList<T> items, Func<T, double> weight, Random rnd)
    {
        double total = items.Sum(weight);
        double roll = rnd.NextDouble() * total;
        double acc = 0;
        foreach (T item in items)
        {
            acc += weight(item);
            if (roll < acc) return item;
        }
        return items[^1];
    }

    // The batch index to resume at: one past the highest complete (non-.tmp) selfplay_* file
    // already in the output directory (schema sec 6 / campaign doc step 4's restart requirement).
    // Only files whose seed range aligns exactly to a gamesPerFile-sized batch count - a directory
    // from a run with a different --games-per-file would otherwise misalign silently.
    private static int DetermineStartBatch(string outDir, int seedBase, int gamesPerFile)
    {
        if (!Directory.Exists(outDir)) return 0;
        int maxBatch = -1;
        foreach (string file in Directory.GetFiles(outDir, "selfplay_*.jsonl.gz"))
        {
            string name = Path.GetFileNameWithoutExtension(Path.GetFileNameWithoutExtension(file));
            string[] parts = name.Replace("selfplay_", "").Split('-');
            if (parts.Length != 2 || !int.TryParse(parts[0], out int seedStart)
                || !int.TryParse(parts[1], out int seedEnd)) continue;
            if (seedEnd - seedStart + 1 != gamesPerFile || (seedStart - seedBase) % gamesPerFile != 0) continue;
            maxBatch = Math.Max(maxBatch, (seedStart - seedBase) / gamesPerFile);
        }
        return maxBatch + 1;
    }

    private static string GitCommit(string dir)
    {
        try
        {
            var psi = new System.Diagnostics.ProcessStartInfo("git", $"-C \"{dir}\" rev-parse HEAD")
            { RedirectStandardOutput = true, UseShellExecute = false };
            using var proc = System.Diagnostics.Process.Start(psi)!;
            string output = proc.StandardOutput.ReadToEnd().Trim();
            proc.WaitForExit();
            return output.Length == 0 ? "unknown" : output;
        }
        catch { return "unknown"; }
    }
}
