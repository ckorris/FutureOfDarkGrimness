namespace FdgRaylib.Rendering;

/// <summary>
/// #399 - where the in-game army list's unit card puts its "Activated" tag. The header
/// ("Name [5] - 285pts") is CENTERED and the tag was right-aligned onto the same line with nothing
/// reserved for it, so a long enough name grew under the red text and the two overlapped.
///
/// <para>The rule: reserve the tag's room first. If the header still fits in what is left, it centres
/// in THAT space and the tag sits beside it - the look the card has always had for ordinary names. If
/// it does not, the tag drops to its own right-aligned line beneath, which is what the compact table
/// view already does. Nothing is truncated: a unit's name is how the player finds it in the list.</para>
///
/// <para>Pure arithmetic in its own file so it can be pinned by a test - the caller's widths come from
/// <c>ImGui.CalcTextSize</c>, which needs a live ImGui frame.</para>
/// </summary>
internal static class UnitCardHeaderLayout
{
    /// <summary>One header line's layout decision.</summary>
    /// <param name="TagOnHeaderLine">
    /// True to draw the tag beside the header (SameLine); false to drop it to its own line below.
    /// Always false when there is no tag to draw.
    /// </param>
    /// <param name="CenterHeaderWithin">
    /// The width the header centres inside: the space left over beside the tag when they share a line,
    /// otherwise the full content width.
    /// </param>
    internal readonly record struct Plan(bool TagOnHeaderLine, float CenterHeaderWithin);

    /// <summary>
    /// Decides the header line's layout. <paramref name="tagWidth"/> is 0 when the unit carries no tag.
    /// <paramref name="spacing"/> is the gap that must remain between header and tag, so a header
    /// ending exactly where the tag begins still counts as not fitting.
    /// </summary>
    internal static Plan Decide(float headerWidth, float tagWidth, float spacing, float availableWidth)
    {
        if (tagWidth <= 0f) return new Plan(false, availableWidth);

        float room = availableWidth - (tagWidth + spacing);
        return headerWidth <= room
            ? new Plan(true, room)
            : new Plan(false, availableWidth);
    }
}
