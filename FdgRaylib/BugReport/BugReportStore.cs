namespace FdgRaylib.BugReport;

/// <summary>
/// Writes bug-report bundles (#226) to <c>BugReports/</c> beside the executable, mirroring
/// <see cref="FdgRaylib.SaveLoad.RecoverySave"/>: UTC-timestamped names that sort chronologically,
/// every failure swallowed and reported through the return value (a report action must never
/// crash the game it is reporting on).
///
/// <para>Unlike #187 there is NO pruning: recovery saves are written automatically and expire in
/// usefulness, but a report is a manual, rare act and the local file may be the only copy of one
/// that never uploaded - silently deleting it would defeat the feature.</para>
/// </summary>
public static class BugReportStore
{
    /// <summary>Folder (relative to the executable) report files are written to.</summary>
    public const string DirectoryName = "BugReports";

    private const string FilePrefix = "report-";

    public static string DirectoryPath => Path.Combine(AppContext.BaseDirectory, DirectoryName);

    /// <summary>
    /// Writes the bundle JSON to a new timestamped report file. Returns the full path written,
    /// or null if the write failed.
    /// </summary>
    public static string? TryWrite(string bundleJson, DateTime timestampUtc) =>
        TryWrite(bundleJson, timestampUtc, DirectoryPath);

    /// <summary>Directory override for tests.</summary>
    internal static string? TryWrite(string bundleJson, DateTime timestampUtc, string directory)
    {
        if (string.IsNullOrWhiteSpace(bundleJson)) return null;

        try
        {
            Directory.CreateDirectory(directory);
            string path = NextFreePath(directory, timestampUtc);
            File.WriteAllText(path, bundleJson);
            return path;
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[#226] Could not write a bug report: {ex.Message}");
            return null;
        }
    }

    // report-20260805-143355.json, with _2, _3... appended if that second already has a report
    // (same-second reports are unlikely but silently overwriting one would lose it).
    private static string NextFreePath(string directory, DateTime timestampUtc)
    {
        string stamp = timestampUtc.ToString("yyyyMMdd-HHmmss");
        string path = Path.Combine(directory, $"{FilePrefix}{stamp}.json");

        for (int suffix = 2; File.Exists(path); suffix++)
        {
            path = Path.Combine(directory, $"{FilePrefix}{stamp}_{suffix}.json");
        }

        return path;
    }
}
