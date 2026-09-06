using FDG;
using FDG.SaveLoad;

namespace FdgRaylib.Rendering.Resolvers;

/// <summary>
/// #394 - the terrain picker's type filter. The palette is 40-odd templates now (#393 widened the heavy
/// end), and the one thing a player is actually sorting by when they scroll it is what the piece DOES:
/// "I need something impassible", "I need cover". This holds the four filterable flags, the label each
/// button carries, and the single predicate the button row and the list both run, so a button can never
/// claim a count the list disagrees with.
///
/// <para><see cref="ETerrainType.Blocking"/> deliberately has no button: every Blocking piece in the
/// built-in palette is also Impassible, so it would duplicate that button's contents. A hand-authored
/// layout file with a Blocking-only piece is reachable under All, which is always offered.</para>
///
/// <para><see cref="ETerrainType.Elevated"/> has no button either - no engine code reads the flag, so
/// filtering by it would always come back empty (see <c>DefaultTerrainPool</c>).</para>
/// </summary>
internal static class TerrainTypeFilter
{
    /// <summary>The no-filter state: every piece in the pool shows. Also what the All button selects.</summary>
    internal const ETerrainType All = ETerrainType.None;

    /// <summary>
    /// The filter buttons, in the order they are drawn. Ordered by how strongly the flag shapes where a
    /// piece goes - the same priority the ghost/thumbnail tint uses in
    /// <c>GuiPlaceOneTerrainResolver.TerrainTypeColors</c> - minus Blocking, which has no button.
    /// </summary>
    internal static readonly IReadOnlyList<(ETerrainType Flag, string Label)> Options = new[]
    {
        (ETerrainType.Impassible, "Impassible"),
        (ETerrainType.Cover,      "Cover"),
        (ETerrainType.Difficult,  "Difficult"),
        (ETerrainType.Dangerous,  "Dangerous"),
    };

    /// <summary>Whether a piece of <paramref name="pieceType"/> shows under <paramref name="filter"/>.</summary>
    internal static bool Matches(ETerrainType pieceType, ETerrainType filter) =>
        filter == All || pieceType.HasFlag(filter);

    /// <summary>How many pieces in <paramref name="pool"/> the filter admits.</summary>
    internal static int CountMatching(IReadOnlyList<TerrainPieceEntry> pool, ETerrainType filter)
    {
        int count = 0;
        for (int i = 0; i < pool.Count; i++)
        {
            if (Matches(pool[i].TerrainType, filter)) count++;
        }
        return count;
    }

    /// <summary>The label for <paramref name="filter"/>, or "All" for the no-filter state.</summary>
    internal static string LabelFor(ETerrainType filter)
    {
        foreach ((ETerrainType flag, string label) in Options)
        {
            if (flag == filter) return label;
        }
        return "All";
    }

    /// <summary>
    /// The line under the button row: what the list is currently showing. Silent (null) when nothing is
    /// filtered out, so the unfiltered picker looks exactly as it did before #394. ASCII only, per CLAUDE.md.
    /// </summary>
    internal static string? SummaryLine(int shown, int total, ETerrainType filter)
    {
        if (filter == All) return null;
        return shown == 0
            ? $"No {LabelFor(filter)} pieces in this pool - All shows every piece."
            : $"Showing {shown} of {total} pieces - {LabelFor(filter)}.";
    }
}
