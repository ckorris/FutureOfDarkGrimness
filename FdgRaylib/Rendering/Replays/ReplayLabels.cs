using System.Globalization;
using FDG;
using FDG.Replay;
using FDG.SaveLoad;

namespace FdgRaylib.Rendering.Replays;

/// <summary>
/// How replays name things, shared by the <c>--replay</c> CLI (#455) and the replay viewer (#456) so both say it
/// the same way: players as their slot (<c>P1</c>), controllers, request types without their namespace, sizes and
/// durations. Every string is ASCII (CLAUDE.md).
/// </summary>
public static class ReplayLabels
{
    /// <summary>"P1" for slot 0, or "P?" for a player the header does not list.</summary>
    public static string PlayerLabel(ReplayHeader header, PlayerID player)
    {
        ReplaySlot? slot = header.Slots.FirstOrDefault(s => s.PlayerId == player);
        return slot == null ? "P?" : $"P{slot.SlotId + 1}";
    }

    public static string Controller(EReplayController controller) => controller switch
    {
        EReplayController.Ai => "AI",
        _ => controller.ToString(),
    };

    /// <summary>"P1 (AI)": who answered a step, from the recording side's point of view.</summary>
    public static string Who(ReplayHeader header, ReplayStep step) =>
        $"{PlayerLabel(header, step.PlayerId)} ({(step.Controller is EReplayController c ? Controller(c) : "?")})";

    /// <summary>The recorded CLR type name without namespace or generic arguments.</summary>
    public static string ShortType(string recordedType)
    {
        string name = recordedType;
        int generic = name.IndexOf('`');
        if (generic >= 0) name = name.Substring(0, generic);
        int dot = name.LastIndexOf('.');
        return dot >= 0 ? name.Substring(dot + 1) : name;
    }

    public static string Bytes(long bytes) => bytes switch
    {
        < 1024 => $"{bytes} B",
        < 1024 * 1024 => (bytes / 1024.0).ToString("0.0", CultureInfo.InvariantCulture) + " KB",
        _ => (bytes / (1024.0 * 1024.0)).ToString("0.00", CultureInfo.InvariantCulture) + " MB",
    };

    public static string Duration(long milliseconds)
    {
        TimeSpan span = TimeSpan.FromMilliseconds(milliseconds);
        return span.TotalHours >= 1
            ? $"{(int)span.TotalHours}h {span.Minutes:00}m {span.Seconds:00}s"
            : $"{span.Minutes}m {span.Seconds:00}s";
    }

    /// <summary>One line of game text: folded to ASCII, newlines flattened.</summary>
    public static string Ascii(string text) => ReplayText.Ascii(text).Replace('\n', ' ');

    public static string Or(string? text) => string.IsNullOrEmpty(text) ? "(unknown)" : text;

    /// <summary>"1 option" / "3 options".</summary>
    public static string Plural(long count, string noun) => $"{count} {noun}{(count == 1 ? "" : "s")}";
}

/// <summary>A replay file that cannot be opened: missing, a save without a replay, unreadable bytes.</summary>
public sealed class ReplayFileException : Exception
{
    public ReplayFileException(string message) : base(message) { }
}

/// <summary>
/// Opens a replay from disk - a <c>.fdgreplay</c>, or the replay a <c>.fdgsave</c> carries (#453) - for the CLI and
/// the viewer alike.
/// </summary>
public static class ReplayFiles
{
    public static bool IsSave(string path) =>
        path.EndsWith(GameSaveFile.EXTENSION_WITH_PERIOD, StringComparison.OrdinalIgnoreCase);

    /// <summary>The replay's gzip bytes. Throws <see cref="ReplayFileException"/> with a one-line reason.</summary>
    public static byte[] ReadGzip(string path)
    {
        if (File.Exists(path) == false)
        {
            throw new ReplayFileException($"File not found: {path}");
        }

        if (IsSave(path) == false)
        {
            return File.ReadAllBytes(path);
        }

        byte[]? replay;
        try
        {
            replay = GameSaveSerializer.LoadWithReplay(File.ReadAllText(path)).ReplayGzip;
        }
        catch (Exception exception) when (exception is InvalidOperationException or Newtonsoft.Json.JsonException)
        {
            throw new ReplayFileException($"{path} does not load as a save: {exception.Message}");
        }

        return replay switch
        {
            null => throw new ReplayFileException($"{path} carries no replay (a compiled scenario, a save of an unrecorded game, or one from before #453)."),
            { Length: 0 } => throw new ReplayFileException($"{path} carries a replay that is not valid base64."),
            _ => replay,
        };
    }

    /// <summary>The whole replay. Throws <see cref="ReplayFileException"/> or <see cref="ReplayFormatException"/>.</summary>
    public static ReplayDocument Read(string path)
    {
        using var stream = new MemoryStream(ReadGzip(path));
        return ReplayReader.ReadGzip(stream);
    }
}
