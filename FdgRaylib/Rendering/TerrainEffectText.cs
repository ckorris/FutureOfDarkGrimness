using System.Collections.Generic;
using FDG;

namespace FdgRaylib.Rendering;

/// <summary>
/// #346: plain-language "what does this piece DO on the table" for an <see cref="ETerrainType"/> flag set,
/// for the terrain-placement panel. The type name alone ("Cover, Difficult") names the rules without
/// saying what they cost you, and terrain is placed before anyone has met those rules in play.
///
/// <para>One line per flag actually set, so a compound piece (Cover + Difficult, the classic forest) reads
/// as both of its consequences. ASCII only, per CLAUDE.md.</para>
/// </summary>
internal static class TerrainEffectText
{
    /// <summary>
    /// The effects <paramref name="type"/> confers, one short line each, ordered by how much they shape
    /// where you would put the piece. Never empty: a flagless piece says so rather than leaving a blank.
    /// </summary>
    internal static IReadOnlyList<string> Effects(ETerrainType type)
    {
        var lines = new List<string>();

        if (type.HasFlag(ETerrainType.Blocking))
            lines.Add("Blocking - breaks line of sight through it.");
        if (type.HasFlag(ETerrainType.Impassible))
            lines.Add("Impassible - models cannot move into or through it.");
        if (type.HasFlag(ETerrainType.Cover))
            lines.Add("Cover - +1 to the defender's save roll while it screens them.");
        if (type.HasFlag(ETerrainType.Difficult))
            lines.Add("Difficult - moving through it cuts the move short.");
        if (type.HasFlag(ETerrainType.Dangerous))
            lines.Add("Dangerous - every model that moves through it takes a wound on a 1.");
        // Declared but read by nothing in the rules today (see DefaultTerrainPool). Only a hand-authored
        // layout can set it, and a piece that silently does nothing is worse than one that says so.
        if (type.HasFlag(ETerrainType.Elevated))
            lines.Add("Elevated - no rules effect yet.");

        if (lines.Count == 0)
            lines.Add("No rules effect - scenery only.");

        return lines;
    }
}
