using System.Globalization;
using System.Runtime.CompilerServices;
using System.Text;
using FDG.Replay;

namespace FdgRaylib.SaveLoad;

/// <summary>
/// Work item #452 - every game leaves a replay. Whatever way a game ends (game over, a disconnect, the escape
/// menu, closing the window, a headless run finishing, a crash) the app hands the game's recording to
/// <see cref="TryWrite"/>, which writes it to a <c>.fdgreplay</c> in <see cref="DirectoryName"/> beside the
/// executable - on the host and on every client alike.
///
/// <para>Modelled on <see cref="RecoverySave"/> (#187): same folder policy, UTC-stamped names that sort
/// chronologically, every failure swallowed and reported through the return value plus a <c>[replay]</c>
/// console line. Unlike it nothing is ever pruned (plan fork F3: a 2k game is ~0.5 MB, keep them all), and a
/// game that never reached a decision writes nothing.</para>
///
/// <para>One file per recording. Several exit paths can see the same game (it ends, then the player leaves
/// through the menu or closes the window), so the path written for each recording is remembered: a later call
/// rewrites that same file if anything was recorded since, and is a no-op otherwise. Keyed on the recording
/// itself, not its <see cref="ReplayHeader.GameId"/>: a resumed save keeps its game ID, and its new session
/// must not overwrite the earlier session's file.</para>
/// </summary>
public static class ReplaySave
{
    /// <summary>Folder (relative to the executable) replays are written to.</summary>
    public const string DirectoryName = "Replays";

    /// <summary>Longest army name kept in a file name; the full names are in the file's header.</summary>
    public const int MaxArmyNameLength = 24;

    private const string FilePrefix = "replay-";

    public static string DirectoryPath => Path.Combine(AppContext.BaseDirectory, DirectoryName);

    private sealed record WrittenFile(string Path, ReplaySummary Summary);

    // Engine-thread game-end handlers and main-thread exit paths can race to write one game; the lock makes
    // the "already written?" check and the write one step.
    private static readonly object WriteLock = new();
    private static readonly ConditionalWeakTable<IReplaySource, WrittenFile> WrittenFiles = new();

    /// <summary>
    /// Writes <paramref name="replay"/> to its file and prints <c>Replay saved to: PATH</c>. Returns the
    /// recording's file - just written, or written earlier and unchanged since - or null when there was
    /// nothing to write (no recording, or not one decision yet) or the write failed.
    /// </summary>
    /// <param name="timestampUtc">Stamp for a new file's name. Passed in rather than read here so a caller
    /// can produce a deterministic name; a rewrite keeps the name the first write chose.</param>
    /// <param name="directory">Where to write; null means <see cref="DirectoryPath"/> (<c>--replay-out</c>
    /// overrides it).</param>
    public static string? TryWrite(IReplaySource? replay, DateTime timestampUtc, string? directory = null)
    {
        if (replay == null) return null;

        try
        {
            lock (WriteLock)
            {
                // Read before the export: anything recorded in between makes the next call's summary differ
                // from this one, so it rewrites rather than wrongly skipping.
                ReplaySummary summary = replay.Summary;
                if (summary.Steps == 0) return null;

                WrittenFiles.TryGetValue(replay, out WrittenFile? earlier);
                if (earlier != null && earlier.Summary == summary) return earlier.Path;

                ReplayHeader? header = replay.Header;
                if (header == null) return null;

                string path = earlier?.Path ?? NextFreePath(directory ?? DirectoryPath, FileStem(header, timestampUtc));
                byte[] gzip = replay.ExportGzip();
                Directory.CreateDirectory(Path.GetDirectoryName(path)!);

                // Through a temporary file, so a failed rewrite leaves the earlier, complete file in place.
                string temporary = path + ".tmp";
                File.WriteAllBytes(temporary, gzip);
                File.Move(temporary, path, overwrite: true);

                WrittenFiles.AddOrUpdate(replay, new WrittenFile(path, summary));
                Console.WriteLine($"Replay saved to: {path}");
                return path;
            }
        }
        catch (Exception ex)
        {
            // A lost replay is a shame, not a reason to turn a finished game (or an exit) into a crash.
            Console.WriteLine($"[replay] Could not write the replay: {ex.Message}");
            return null;
        }
    }

    /// <summary>Existing replays in <paramref name="directory"/> (default <see cref="DirectoryPath"/>), newest
    /// first.</summary>
    public static IReadOnlyList<string> List(string? directory = null)
    {
        string folder = directory ?? DirectoryPath;
        try
        {
            if (!Directory.Exists(folder)) return Array.Empty<string>();

            // By name, not write time, as RecoverySave does: the UTC stamp leads the name, and copying a folder
            // can rewrite every timestamp at once.
            return Directory.GetFiles(folder, $"{FilePrefix}*{ReplayFile.EXTENSION_WITH_PERIOD}")
                .OrderByDescending(path => path, StringComparer.Ordinal)
                .ToList();
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[replay] Could not list replays: {ex.Message}");
            return Array.Empty<string>();
        }
    }

    /// <summary>
    /// <c>replay-20261031-143355-Battle-Brothers-vs-Orks</c>: the stamp, then the first slot's army and the
    /// first army on another team (slot order). More than two slots (FFA, 2v2) appends <c>-4p</c>.
    /// </summary>
    internal static string FileStem(ReplayHeader header, DateTime timestampUtc)
    {
        List<ReplaySlot> slots = header.Slots.OrderBy(slot => slot.SlotId).ToList();
        ReplaySlot? first = slots.FirstOrDefault();
        ReplaySlot? opponent = slots.FirstOrDefault(slot => first != null && slot.Team != first.Team)
            ?? slots.Skip(1).FirstOrDefault();

        string stem = $"{FilePrefix}{timestampUtc.ToString("yyyyMMdd-HHmmss", CultureInfo.InvariantCulture)}"
            + $"-{FileNamePart(first?.ArmyName)}-vs-{FileNamePart(opponent?.ArmyName)}";
        return slots.Count > 2 ? $"{stem}-{slots.Count}p" : stem;
    }

    /// <summary>An army name cut down to <c>[A-Za-z0-9-]</c>: every other run of characters becomes one '-',
    /// ends trimmed, at most <see cref="MaxArmyNameLength"/> long. "army" when nothing is left.</summary>
    internal static string FileNamePart(string? armyName)
    {
        var part = new StringBuilder();
        foreach (char c in armyName ?? "")
        {
            bool keep = c is >= 'A' and <= 'Z' or >= 'a' and <= 'z' or >= '0' and <= '9' or '-';
            if (keep) part.Append(c);
            else if (part.Length > 0 && part[^1] != '-') part.Append('-');
        }

        string trimmed = part.ToString().Trim('-');
        if (trimmed.Length > MaxArmyNameLength) trimmed = trimmed[..MaxArmyNameLength].TrimEnd('-');
        return trimmed.Length > 0 ? trimmed : "army";
    }

    // Same-second collisions (one machine hosting two quick games, or a client and a host process side by side
    // with the same armies) get _2, _3...: '_' sorts above '.', so the later file still ranks newer (see
    // RecoverySave.NextFreePath).
    private static string NextFreePath(string directory, string stem)
    {
        string extension = ReplayFile.EXTENSION_WITH_PERIOD;
        string path = Path.Combine(directory, stem + extension);

        for (int suffix = 2; File.Exists(path); suffix++)
        {
            path = Path.Combine(directory, $"{stem}_{suffix}{extension}");
        }

        return path;
    }
}
