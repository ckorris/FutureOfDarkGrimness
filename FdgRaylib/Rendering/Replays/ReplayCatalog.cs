using System.Globalization;
using FDG.Replay;
using FdgRaylib.SaveLoad;
using Newtonsoft.Json;

namespace FdgRaylib.Rendering.Replays;

/// <summary>
/// One row of the replay browser (#456), from the file's header line alone (its <see cref="ReplayHeader.Summary"/>
/// says how far the game got). A file that cannot be read is still a row - with <see cref="Error"/> set - so the
/// browser shows why rather than hiding it.
/// </summary>
public sealed record ReplayBrowserRow(string Path, string Started, string Armies, string Players, int Rounds, int Steps,
    string Outcome, long SizeBytes, string? Error)
{
    public string FileName => System.IO.Path.GetFileName(Path);

    public string Size => ReplayLabels.Bytes(SizeBytes);
}

/// <summary>Lists the replays in <c>Replays/</c> for the browser, newest first, reading one line of each.</summary>
public static class ReplayCatalog
{
    /// <summary>Every replay in <paramref name="directory"/> (default <see cref="ReplaySave.DirectoryPath"/>),
    /// newest first.</summary>
    public static IReadOnlyList<ReplayBrowserRow> List(string? directory = null) =>
        ReplaySave.List(directory).Select(Read).ToList();

    /// <summary>A file's row; never throws.</summary>
    public static ReplayBrowserRow Read(string path)
    {
        long size = 0;
        try
        {
            size = new FileInfo(path).Length;
            using FileStream file = File.OpenRead(path);
            return From(path, ReplayReader.ReadHeaderGzip(file), size);
        }
        catch (Exception exception) when (exception is ReplayFormatException or IOException or UnauthorizedAccessException
            or JsonException)
        {
            return new ReplayBrowserRow(path, "", System.IO.Path.GetFileName(path), "", 0, 0, "", size,
                ReplayLabels.Ascii(exception.Message));
        }
    }

    public static ReplayBrowserRow From(string path, ReplayHeader header, long sizeBytes)
    {
        string started = header.StartedUtc.ToLocalTime().ToString("yyyy-MM-dd HH:mm", CultureInfo.InvariantCulture);
        if (header.ResumedUtc.Count > 0)
        {
            started += $" ({ReplayLabels.Plural(header.ResumedUtc.Count, "resume")})";
        }

        ReplaySummary? summary = header.Summary;
        return new ReplayBrowserRow(path, started, Armies(header), Players(header), summary?.Rounds ?? 0,
            summary?.Steps ?? 0, Outcome(summary), sizeBytes, Error: null);
    }

    /// <summary>"Battle Brothers vs Orks"; teams of several "A &amp; B vs C &amp; D"; a free-for-all "A vs B vs C".</summary>
    public static string Armies(ReplayHeader header)
    {
        IEnumerable<string> teams = header.Slots
            .OrderBy(slot => slot.SlotId)
            .GroupBy(slot => slot.Team)
            .Select(team => string.Join(" & ", team.Select(slot => ReplayLabels.Ascii(ReplayLabels.Or(slot.ArmyName)))));
        string armies = string.Join(" vs ", teams);
        return armies.Length > 0 ? armies : "(no players)";
    }

    /// <summary>"Chris (Human), Bot (AI)" - the controller as the recording side saw it, unless the name already
    /// says so ("Player 2 (AI)", as scenarios name their AI slots).</summary>
    public static string Players(ReplayHeader header) =>
        string.Join(", ", header.Slots.OrderBy(slot => slot.SlotId).Select(slot =>
        {
            string name = ReplayLabels.Ascii(ReplayLabels.Or(slot.Name));
            string tag = $"({ReplayLabels.Controller(slot.Controller)})";
            return name.EndsWith(tag, StringComparison.Ordinal) ? name : $"{name} {tag}";
        }));

    /// <summary>The outcome name, "Unfinished" when the game never ended, "?" when the file carries no summary.</summary>
    public static string Outcome(ReplaySummary? summary) => summary == null ? "?" : summary.Outcome ?? "Unfinished";
}
