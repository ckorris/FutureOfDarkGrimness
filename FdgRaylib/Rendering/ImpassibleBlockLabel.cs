namespace FdgRaylib.Rendering;

/// <summary>
/// #317: the impassible-terrain sibling of <see cref="DifficultShortfallPlan"/>'s label. Impassible terrain
/// doesn't shorten a move the way Difficult does — the whole placement is refused (red piece, red swept
/// footprint at first contact, un-clickable), so there is no would-be phantom and nothing to measure. All
/// that's needed is the same two-line "here is the rule that stopped you" text the difficult clamp gained,
/// kept here so the wording is ASCII-checked like every other piece of on-table game text.
/// </summary>
internal static class ImpassibleBlockLabel
{
    internal const string HEADER = "Impassible Terrain";

    /// <summary>Covers both ways a path trips the check — passing through the piece, and ending with the
    /// base overlapping it — since either way the model tried to enter ground it may not enter.</summary>
    internal const string DETAIL = "Cannot move through it";
}
