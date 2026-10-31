namespace FdgRaylib.Rendering;

/// <summary>
/// App-side transport occupancy indicator (#096). A compact "Carrying X/Y" badge above a Transport
/// unit (X = occupied spaces, Y = capacity), plus an occupant breakdown in the hover tooltip.
///
/// Embarked units ride off-table (at origin), so occupancy is token-derived — this is a pure render
/// of <c>TransportUtilities</c> queries with no engine state. Only the text formatting lives here;
/// the overlay reads the counts live from <c>TransportUtilities</c> each frame and draws the badge.
/// The badge shows for any transport, empty included, so remaining capacity always reads at a glance.
/// </summary>
public static class TransportBadgeRenderer
{
    /// <summary>The on-table badge, e.g. "Carrying 4/6" (occupied spaces / capacity).</summary>
    public static string FormatBadge(int occupiedSpaces, int capacity) =>
        $"Carrying {occupiedSpaces}/{capacity}";

    /// <summary>
    /// Tooltip header for the cargo list, e.g. "2 units aboard (4/6 spaces):" or, when empty,
    /// "Empty (0/6 spaces)". Spaces are spelled out here because the compact on-table badge's "X/Y"
    /// counts spaces, not units, and a Tough model can cost more than one.
    /// </summary>
    public static string FormatAboardHeader(int unitCount, int occupiedSpaces, int capacity) =>
        unitCount == 0
            ? $"Empty (0/{capacity} spaces)"
            : $"{unitCount} unit{(unitCount == 1 ? "" : "s")} aboard ({occupiedSpaces}/{capacity} spaces):";

    /// <summary>A single occupant line, e.g. "Heavy Gunners (3 spaces)" or "Warriors (1 space)".</summary>
    public static string FormatOccupant(string unitName, int spaceCost) =>
        $"{unitName} ({spaceCost} space{(spaceCost == 1 ? "" : "s")})";
}
