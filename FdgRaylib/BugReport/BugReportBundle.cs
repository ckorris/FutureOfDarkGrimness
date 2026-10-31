using System.Runtime.InteropServices;
using System.Text.Json;
using System.Text.Json.Serialization;
using FdgRaylib.Config;
using FdgRaylib.Rendering;
using FDG.Network;

namespace FdgRaylib.BugReport;

/// <summary>
/// One in-app bug report (#226): what the player typed plus every diagnostic the app can gather
/// without asking. Written to <c>BugReports/</c> locally and uploaded (gzipped) to the list-server
/// drop box (<c>tools/list-server</c>, <c>POST /reports</c>).
///
/// <para>Serialized with System.Text.Json into a plain record on purpose - same rationale as the
/// list-server DTOs (#186): this crosses the internet, so nothing on either end may run it through
/// a polymorphic ($type-style) deserializer. The save travels as an opaque STRING member for the
/// same reason: the server stores and echoes it, never parses it.</para>
/// </summary>
public sealed record BugReportBundle(
    string Description,
    string CreatedUtc,
    string AppVersion,
    int ProtocolVersion,
    string Platform,
    string PlayerName,
    bool IsHost,
    IReadOnlyList<string> Log,
    string? CrashLog,
    string? Save);

public static class BugReportBundleBuilder
{
    // A marathon session's log could balloon the bundle; the newest lines are the ones that matter.
    // 4000 merged lines is a few hundred KB raw - comfortably inside the server's 1MB gzipped cap.
    public const int MaxLogLines = 4000;

    // crash.log is append-only across runs (Program.cs), so only its tail is current.
    public const int CrashLogTailBytes = 64 * 1024;

    private static readonly JsonSerializerOptions Json = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
        WriteIndented = true, // the local copy doubles as something a human can read
    };

    /// <summary>
    /// Assembles the bundle JSON. Runs on the main thread (from the escape menu), like the menu's
    /// save row - <paramref name="saveGameToJson"/> is null on clients, who can't save (#054), so
    /// their reports are log-only by design (recorded in the #226 ledger).
    /// </summary>
    /// <param name="crashLogPath">Override for tests; null means the real crash.log beside the
    /// executable (the path Program.cs appends to).</param>
    public static string Build(string description, GameLog? log, GameLog? chatLog,
        Func<string?>? saveGameToJson, string? crashLogPath = null)
    {
        var bundle = new BugReportBundle(
            Description: description,
            CreatedUtc: DateTime.UtcNow.ToString("O"),
            AppVersion: FdgRaylib.AppVersion.Value,
            ProtocolVersion: NetworkProtocol.Version,
            Platform: $"{RuntimeInformation.RuntimeIdentifier}; {RuntimeInformation.OSDescription}",
            PlayerName: UserConfig.Current.PlayerName,
            IsHost: saveGameToJson != null,
            Log: MergedLogLines(log, chatLog),
            CrashLog: ReadCrashLogTail(crashLogPath ?? Path.Combine(AppContext.BaseDirectory, "crash.log")),
            Save: saveGameToJson?.Invoke());
        return JsonSerializer.Serialize(bundle, Json);
    }

    // Engine log and chat merged into one column in true arrival order - the same merge the
    // console view does, keyed on the globally-monotonic LogEntry.Sequence.
    private static IReadOnlyList<string> MergedLogLines(GameLog? log, GameLog? chatLog)
    {
        var merged = new List<(long Sequence, string Line)>();
        if (log != null)
        {
            foreach (LogEntry e in log.Snapshot())
                merged.Add((e.Sequence, e.IsDebug ? $"[dbg] {e.Message}" : e.Message));
        }
        if (chatLog != null)
        {
            foreach (LogEntry e in chatLog.Snapshot())
                merged.Add((e.Sequence, $"[chat] {e.Message}"));
        }
        merged.Sort((a, b) => a.Sequence.CompareTo(b.Sequence));

        int skip = Math.Max(0, merged.Count - MaxLogLines);
        var lines = new List<string>(merged.Count - skip);
        if (skip > 0) lines.Add($"[... {skip} earlier lines dropped ...]");
        for (int i = skip; i < merged.Count; i++) lines.Add(merged[i].Line);
        return lines;
    }

    private static string? ReadCrashLogTail(string path)
    {
        try
        {
            if (!File.Exists(path)) return null;
            using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
            long start = Math.Max(0, stream.Length - CrashLogTailBytes);
            stream.Seek(start, SeekOrigin.Begin);
            using var reader = new StreamReader(stream);
            string tail = reader.ReadToEnd();
            return start > 0 ? "[... truncated ...]\n" + tail : tail;
        }
        catch (Exception)
        {
            // A report must never fail because the crash log is unreadable.
            return null;
        }
    }
}
