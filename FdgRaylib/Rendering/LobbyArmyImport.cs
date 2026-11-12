using System.Text;
using FDG.ArmyBuilding;
using FDG.SaveLoad;

namespace FdgRaylib.Rendering;

/// <summary>
/// #460: the ImGui-free half of the lobby's Import popup (<see cref="LobbyImportPopup"/>) - its wording and
/// where the optional saved copy goes - split out so both can be unit-tested, the same split as
/// <see cref="LobbyArmySource"/> and <see cref="LobbyPointsStatus"/>.
/// </summary>
public static class LobbyArmyImport
{
    /// <summary>How to get the link out of Army Forge (the same steps the README gives). ASCII-only, as all
    /// game text must be; a test pins it.</summary>
    public const string Instructions =
        "In Army Forge (army-forge.onepagerules.com), open your list, click the three dots in the top " +
        "right, then Share List -> Share as Link. Copy the link and paste it below.";

    /// <summary>Shown while the field holds something that is not a share link, once typing has paused.</summary>
    public const string NotALinkHint =
        "That does not look like an Army Forge share link. It should look like " +
        "https://army-forge.onepagerules.com/share?id=...";

    /// <summary>The lead line of the "breaks the lobby settings" confirm.</summary>
    public const string SettingsConfirmLead = "This army does not fit the lobby settings:";

    /// <summary>Why importing it anyway is allowed, and what it costs.</summary>
    public const string SettingsConfirmBody =
        "You can still import it - the host may change the settings - but LAUNCH stays blocked until it fits.";

    /// <summary>The importer's name for a list Army Forge left unnamed (OprListImporter). Not worth putting
    /// in a file name, which already says the faction.</summary>
    private const string UnnamedListName = "Imported Army";

    /// <summary>Longest file-name stem we write, so a pasted essay of a list name can't hit a path limit.</summary>
    private const int MaxStemLength = 100;

    /// <summary>
    /// The saved copy's file name, in the shipped armies' pattern - <c>2k - Orks - Horde Mixed.fdgarmy</c>:
    /// points, faction, then the list's own name. The points are the list's Army Forge target when it has one
    /// (in "Nk" form when it is a round thousand, as the shipped files are), otherwise its actual total. The
    /// faction is left out when the list name already says it, and the name when Army Forge had none.
    /// </summary>
    public static string SaveFileName(ArmyListFile army)
    {
        string points = army.PointsLimit > 0
            ? (army.PointsLimit % 1000 == 0 ? $"{army.PointsLimit / 1000}k" : army.PointsLimit.ToString())
            : army.TotalPoints.ToString();

        string name = army.Name.Trim();
        string faction = army.Faction.Trim();
        if (name == UnnamedListName) name = string.Empty;

        var parts = new List<string> { points };
        if (faction.Length > 0 && !name.Contains(faction, StringComparison.OrdinalIgnoreCase)) parts.Add(faction);
        if (name.Length > 0) parts.Add(name);
        if (parts.Count == 1) parts.Add("Army"); // neither a faction nor a name

        return Sanitize(string.Join(" - ", parts)) + ArmyListFile.EXTENSION_WITH_PERIOD;
    }

    /// <summary>
    /// Which folder the copy goes in: the armies folder's per-system subfolder when it has one (the shipped
    /// layout splits into <c>GDF/</c> and <c>AoF/</c>, #402), else the armies folder itself. Null when there is
    /// no armies folder at all, in which case the popup offers no copy rather than inventing a location.
    /// </summary>
    public static string? SaveFolder(string? armiesFolder, string? gameSystem, Func<string, bool> directoryExists)
    {
        if (armiesFolder is null) return null;

        string? sub = GameSystems.Normalize(gameSystem) switch
        {
            GameSystems.GrimdarkFuture => "GDF",
            GameSystems.AgeOfFantasy => "AoF",
            _ => null,
        };
        if (sub is not null)
        {
            string candidate = Path.Combine(armiesFolder, sub);
            if (directoryExists(candidate)) return candidate;
        }
        return armiesFolder;
    }

    /// <summary><paramref name="fileName"/> in <paramref name="folder"/>, or the first free " (2)", " (3)"...
    /// variant of it. A copy never overwrites an existing army - it may be one the player edited by hand.</summary>
    public static string UniquePath(string folder, string fileName, Func<string, bool> fileExists)
    {
        string path = Path.Combine(folder, fileName);
        string stem = Path.GetFileNameWithoutExtension(fileName);
        string ext = Path.GetExtension(fileName);
        for (int n = 2; fileExists(path); n++)
            path = Path.Combine(folder, $"{stem} ({n}){ext}");
        return path;
    }

    /// <summary>The line naming what LOBBY problems an army has, one per <see cref="LaunchGate.SettingsProblems"/>
    /// entry, capitalised to stand alone (the gate's own lines follow a player name).</summary>
    public static string ProblemLine(string settingsProblem) =>
        settingsProblem.Length == 0 ? settingsProblem : char.ToUpperInvariant(settingsProblem[0]) + settingsProblem[1..];

    // Every character any of the three desktop platforms refuses in a file name becomes a space, so a copy
    // saved on Linux still opens on Windows; then runs of spaces collapse and trailing dots/spaces (which
    // Windows silently strips) go.
    private static string Sanitize(string stem)
    {
        var sb = new StringBuilder(stem.Length);
        foreach (char c in stem)
            sb.Append(c < 32 || "<>:\"/\\|?*".Contains(c) ? ' ' : c);

        string collapsed = string.Join(' ', sb.ToString().Split(' ', StringSplitOptions.RemoveEmptyEntries));
        if (collapsed.Length > MaxStemLength) collapsed = collapsed[..MaxStemLength];
        collapsed = collapsed.TrimEnd('.', ' ');
        return collapsed.Length > 0 ? collapsed : "Army";
    }
}
