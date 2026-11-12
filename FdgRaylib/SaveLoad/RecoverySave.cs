using FDG.SaveLoad;

namespace FdgRaylib.SaveLoad;

/// <summary>
/// Work item #187 — recovery saves. When a networked game ends because a player's connection dropped,
/// the host writes the still-intact game to a <c>.fdgsave</c> here, with no dialog and no clicks: over the
/// internet a 20-second wifi blip is common, and the point is that the game survives it even if the host
/// closes the window straight afterwards. The file is loaded back through the ordinary Load Game flow.
///
/// <para>Policy lives app-side (the engine only reports that a disconnect ended the game, via
/// <see cref="FDG.EGameOutcome.Disconnect"/>): files land in <see cref="DirectoryName"/> beside the
/// executable, named by UTC timestamp so they sort chronologically, and the newest
/// <see cref="KeepNewest"/> are kept so a long session can't fill a disk.</para>
///
/// <para>Every failure here is swallowed and reported through the return value. This runs on the engine
/// thread inside the end-of-game handler, where an escaping IOException (read-only install directory,
/// full disk) would surface as a second, more alarming "crash" on top of the disconnect.</para>
/// </summary>
public static class RecoverySave
{
    /// <summary>Folder (relative to the executable) recovery files are written to.</summary>
    public const string DirectoryName = "Saves";

    /// <summary>How many recovery files survive a write; older ones are deleted.</summary>
    public const int KeepNewest = 5;

    private const string FilePrefix = "recovery-";

    public static string DirectoryPath => Path.Combine(AppContext.BaseDirectory, DirectoryName);

    /// <summary>
    /// Writes <paramref name="saveJson"/> to a new timestamped recovery file and prunes older ones.
    /// Returns the full path written, or null if there was nothing to save or the write failed.
    /// </summary>
    /// <param name="saveJson">A serialized game from <see cref="GameSaveSerializer.Save"/>, or null on a
    /// client (which has no authoritative store to save - see work item #054).</param>
    /// <param name="timestampUtc">Stamp for the file name. Passed in rather than read here so a caller
    /// can produce a deterministic name.</param>
    public static string? TryWrite(string? saveJson, DateTime timestampUtc)
    {
        if (string.IsNullOrWhiteSpace(saveJson)) return null;

        try
        {
            Directory.CreateDirectory(DirectoryPath);

            string path = NextFreePath(timestampUtc);
            File.WriteAllText(path, saveJson);
            PruneOldest();
            return path;
        }
        catch (Exception ex)
        {
            // Losing the recovery file is bad, but it must not turn a handled disconnect into a crash.
            Console.WriteLine($"[#187] Could not write a recovery save: {ex.Message}");
            return null;
        }
    }

    /// <summary>Existing recovery files, newest first.</summary>
    public static IReadOnlyList<string> List()
    {
        try
        {
            if (!Directory.Exists(DirectoryPath)) return Array.Empty<string>();

            // Ordered by name, not write time: the names are UTC timestamps (so they sort chronologically
            // by construction) and copying a folder around can rewrite every timestamp at once.
            return Directory.GetFiles(DirectoryPath, $"{FilePrefix}*{GameSaveFile.EXTENSION_WITH_PERIOD}")
                .OrderByDescending(path => path, StringComparer.Ordinal)
                .ToList();
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[#187] Could not list recovery saves: {ex.Message}");
            return Array.Empty<string>();
        }
    }

    // recovery-20260726-143355.fdgsave, with _2, _3... appended if that name is taken. Two games can end
    // in the same second (one host, several lobbies over a session), and silently overwriting the first
    // game's recovery file would defeat the point of keeping several.
    //
    // The separator is '_' rather than '-' so the collision names still sort in write order: '_' (0x5F)
    // is above '.' (0x2E), so "...143355_2.fdgsave" ranks ABOVE "...143355.fdgsave" — with '-' (0x2D) it
    // would rank below, and List()/PruneOldest would treat the newer file of a same-second pair as the
    // older one and could delete it first.
    private static string NextFreePath(DateTime timestampUtc)
    {
        string stamp = timestampUtc.ToString("yyyyMMdd-HHmmss");
        string path = Path.Combine(DirectoryPath, $"{FilePrefix}{stamp}{GameSaveFile.EXTENSION_WITH_PERIOD}");

        for (int suffix = 2; File.Exists(path); suffix++)
        {
            path = Path.Combine(DirectoryPath,
                $"{FilePrefix}{stamp}_{suffix}{GameSaveFile.EXTENSION_WITH_PERIOD}");
        }

        return path;
    }

    private static void PruneOldest()
    {
        IReadOnlyList<string> files = List();

        for (int i = KeepNewest; i < files.Count; i++)
        {
            try
            {
                File.Delete(files[i]);
            }
            catch (Exception ex)
            {
                // One undeletable file (open elsewhere, permissions) shouldn't stop the rest of the prune.
                Console.WriteLine($"[#187] Could not prune recovery save '{files[i]}': {ex.Message}");
            }
        }
    }
}
