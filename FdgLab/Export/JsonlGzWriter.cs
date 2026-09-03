using System.IO.Compression;
using System.Text;
using System.Text.Json;

namespace FdgLab.Export;

/// <summary>
/// Writes one gzipped JSONL file for a batch of completed games (schema sec 6): a header record
/// first, then one JSON line per row (kind="row") and, for the entity-sampled games in the batch,
/// one line per boundary's entity table (kind="entity"). Written under a ".tmp" name and renamed
/// to its final name only once the whole batch is done - a restart can then trust every
/// non-.tmp file in the output directory as complete (schema sec 4's crash-tolerance requirement).
/// </summary>
public static class JsonlGzWriter
{
    public static string Write(string outDir, string fileNameNoExt, FileHeader header,
        IEnumerable<ExportRow> rows, IEnumerable<(int Boundary, string GameId, List<float[]> Entities)> entityRows,
        IEnumerable<GameLine> games)
    {
        Directory.CreateDirectory(outDir);
        string finalPath = Path.Combine(outDir, fileNameNoExt + ".jsonl.gz");
        string tmpPath = finalPath + ".tmp";

        using (FileStream fs = File.Create(tmpPath))
        using (var gz = new GZipStream(fs, CompressionLevel.Optimal))
        using (var writer = new StreamWriter(gz, new UTF8Encoding(encoderShouldEmitUTF8Identifier: false)))
        {
            writer.WriteLine(JsonSerializer.Serialize(header));
            foreach (GameLine game in games)
                writer.WriteLine(JsonSerializer.Serialize(game));
            foreach (ExportRow row in rows)
                writer.WriteLine(JsonSerializer.Serialize(new RowLine(row)));
            foreach ((int boundary, string gameId, List<float[]> entities) in entityRows)
                writer.WriteLine(JsonSerializer.Serialize(new EntityLine(gameId, boundary, entities)));
        }

        File.Move(tmpPath, finalPath, overwrite: true);
        return finalPath;
    }

    public sealed record FileHeader(
        int Schema, string EngineCommit, string SuperprojectCommit, string CreatedUtc,
        string ProfileA, string ProfileB, string SeedRange, string Shape, string PointsLevel,
        string ArmyA, string ArmyB, bool HeldOut, double EntitySampleRate, double BoundarySampleRate,
        double EncoderMsMean)
    {
        public string Kind => "header";
    }

    private sealed record RowLine
    {
        public string Kind => "row";
        public string GameId { get; }
        public int Boundary { get; }
        public int Round { get; }
        public int ActingSlot { get; }
        public float[] Features { get; }
        public int ChosenUnit { get; }
        public string ChosenAction { get; }
        public string ChosenMacro { get; }
        public float Result { get; }
        public float ObjDiffNorm { get; }
        public int RoundsPlayed { get; }

        public RowLine(ExportRow row)
        {
            GameId = row.GameId;
            Boundary = row.Boundary;
            Round = row.Round;
            ActingSlot = row.ActingSlot;
            Features = row.Features;
            ChosenUnit = row.ChosenUnit;
            ChosenAction = row.ChosenAction;
            ChosenMacro = row.ChosenMacro;
            Result = row.Result;
            ObjDiffNorm = row.ObjDiffNorm;
            RoundsPlayed = row.RoundsPlayed;
        }
    }

    private sealed record EntityLine(string GameId, int Boundary, List<float[]> Units)
    {
        public string Kind => "entity";
    }

    /// <summary>
    /// Per-game provenance (one line per completed game in the batch): the sampled matchup, AI
    /// profiles, and outcome - restores real per-row provenance when a batch mixes matchups, which
    /// the file <see cref="FileHeader"/>'s single army_a/profile_a/etc pair (schema sec 6) cannot
    /// describe on its own. Faulted/disconnected games are never in this list (they are discarded
    /// entirely, schema sec 1) or in <c>rows</c>.
    /// </summary>
    public sealed record GameLine(string GameId, int Seed, string PointsLevel, string Shape,
        string ArmyA, string ArmyB, string ProfileA, string ProfileB, string Outcome, int RoundsPlayed)
    {
        public string Kind => "game";
    }
}
