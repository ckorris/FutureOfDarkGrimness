using System.Text.Json;
using FDG.SaveLoad;

namespace FdgRaylib;

/// <summary>
/// One army file as the lobby's bot picker sees it: enough to rank and label it, without the units,
/// rules, spells or (for a Forge-built list) the embedded book.
/// </summary>
/// <param name="Path">Absolute path, handed to the loader when the army is actually assigned.</param>
/// <param name="Name">The army's own name - "Grumpy Bugs", not the file name.</param>
/// <param name="Points">
/// <see cref="ArmyListFile.TotalPoints"/>: the units' costs plus <see cref="ArmyListFile.UnattributedPoints"/>.
/// </param>
public readonly record struct ArmyCatalogEntry(string Path, string Name, string Faction, int Points)
{
    /// <summary>
    /// Identity used to tell "another player already has this army" without knowing where they loaded it
    /// from - a remote client's army arrives as a <see cref="Players.ArmyListSummary"/> with no path at
    /// all. Two files agreeing on all three fields are the same list for the purpose of not handing two
    /// bots the same army.
    /// </summary>
    public string Key => $"{Name}|{Faction}|{Points}";

    /// <inheritdoc cref="Key"/>
    public static string KeyFor(string name, string faction, int points) => $"{name}|{faction}|{points}";
}

/// <summary>
/// A lightweight index of the <c>armies/</c> folder (<see cref="ArmyPaths"/>), used to hand bots a
/// starter army in the lobby.
///
/// <para>Scanning matters here: the bundled lists are Forge-built, so each one carries a full snapshot of
/// its source book and runs 300-600 KB - about 12 MB across the folder. Deserializing them properly, even
/// into a trimmed DTO, walks all of that. <see cref="ReadEntry"/> instead streams each file with a
/// <see cref="Utf8JsonReader"/> and <c>Skip()</c>s every top-level property it doesn't need (<c>book</c>
/// and <c>selections</c> above all), so no rule graph is ever materialized. The scan still runs on a
/// background task - the lobby must not stall on first paint - and every reader joins it via
/// <see cref="Entries"/>.</para>
/// </summary>
public sealed class ArmyCatalog
{
    private readonly Task<IReadOnlyList<ArmyCatalogEntry>> _load;

    /// <summary>Scans <paramref name="folder"/> in the background. A null or missing folder yields an
    /// empty catalog rather than throwing - the app runs fine with no armies folder beside it.</summary>
    public ArmyCatalog(string? folder) => _load = Task.Run(() => Scan(folder));

    /// <summary>Scans the app's <see cref="ArmyPaths.FolderPath"/>.</summary>
    public ArmyCatalog() : this(ArmyPaths.FolderPath) { }

    /// <summary>True once the background scan has finished. The lobby uses this to hold off auto-picking
    /// (and to grey the re-roll button) rather than blocking its draw thread on the scan.</summary>
    public bool IsLoaded => _load.IsCompleted;

    /// <summary>The indexed armies, sorted by name for a stable order. Blocks until the scan finishes.</summary>
    public IReadOnlyList<ArmyCatalogEntry> Entries => _load.GetAwaiter().GetResult();

    private static IReadOnlyList<ArmyCatalogEntry> Scan(string? folder)
    {
        if (folder is null || !Directory.Exists(folder)) return Array.Empty<ArmyCatalogEntry>();

        var entries = new List<ArmyCatalogEntry>();
        foreach (string path in Directory.EnumerateFiles(folder, "*" + ArmyListFile.EXTENSION_WITH_PERIOD))
        {
            if (ReadEntry(path) is { } entry) entries.Add(entry);
        }

        // Stable order so the ranking below is reproducible run to run; EnumerateFiles is not ordered.
        entries.Sort((a, b) => string.CompareOrdinal(a.Path, b.Path));
        return entries;
    }

    /// <summary>
    /// Reads one army file's name, faction and total points without materializing anything else.
    /// Returns null for a file that isn't a PLAYABLE army - unparseable, or carrying no units - so a
    /// stray, half-written or empty file in the folder can neither take the lobby down nor be handed
    /// to a bot.
    /// </summary>
    public static ArmyCatalogEntry? ReadEntry(string path)
    {
        try
        {
            byte[] bytes = File.ReadAllBytes(path);
            var reader = new Utf8JsonReader(bytes,
                new JsonReaderOptions { CommentHandling = JsonCommentHandling.Skip, AllowTrailingCommas = true });

            if (!reader.Read() || reader.TokenType != JsonTokenType.StartObject) return null;

            string name = "", faction = "";
            int points = 0;
            int unitCount = 0;

            // Each iteration consumes one whole top-level property, so the next Read() is always either
            // the next property name or the closing brace - no depth arithmetic needed.
            while (reader.Read() && reader.TokenType != JsonTokenType.EndObject)
            {
                string property = reader.GetString() ?? "";
                if (!reader.Read()) return null;

                switch (property)
                {
                    case "name":                 name    = reader.GetString() ?? ""; break;
                    case "faction":              faction = reader.GetString() ?? ""; break;
                    case "unattributedPoints":   points += reader.GetInt32();        break;
                    case "units":                points += SumUnitPoints(ref reader, out unitCount); break;
                    default:                     reader.Skip();                      break;
                }
            }

            if (unitCount == 0) return null;
            return new ArmyCatalogEntry(path, name, faction, points);
        }
        catch (Exception e) when (e is IOException or JsonException or UnauthorizedAccessException)
        {
            return null;
        }
    }

    /// <summary>Sums <c>pointCost</c> over the elements of the <c>units</c> array the reader is parked on.
    /// Nested objects inside a unit (weapons, special rules) are skipped whole, so only the unit's own
    /// cost is counted.</summary>
    private static int SumUnitPoints(ref Utf8JsonReader reader, out int unitCount)
    {
        unitCount = 0;
        if (reader.TokenType != JsonTokenType.StartArray)
        {
            reader.Skip();
            return 0;
        }

        int total = 0;
        while (reader.Read() && reader.TokenType != JsonTokenType.EndArray)
        {
            if (reader.TokenType != JsonTokenType.StartObject)
            {
                reader.Skip();
                continue;
            }

            unitCount++;

            while (reader.Read() && reader.TokenType != JsonTokenType.EndObject)
            {
                bool isPointCost = reader.ValueTextEquals("pointCost");
                if (!reader.Read()) return total;
                if (isPointCost) total += reader.GetInt32();
                else reader.Skip();
            }
        }

        return total;
    }
}
