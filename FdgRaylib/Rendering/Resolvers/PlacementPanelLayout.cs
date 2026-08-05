using System;

namespace FdgRaylib.Rendering.Resolvers;

/// <summary>
/// #288 — the placement panel's vertical budget, as arithmetic rather than ImGui calls so it can be
/// unit-tested. <see cref="GuiPlaceObjectsResolver{T}"/> measures the text and style values from the live
/// window and hands them here; this decides how tall the unit-stat box may be.
///
/// <para>The whole point is that the stat box grows to fill the panel — it used to be a fixed 118px
/// keyhole, which is unusable for the case it exists for (an Ambush arrival, where the unit is off-table
/// and cannot be hovered) — WITHOUT ever pushing the Done/Back buttons off the bottom. So the footer is
/// costed first and the stat box takes the remainder.</para>
/// </summary>
public static class PlacementPanelLayout
{
    /// <summary>The stat box never shrinks below this many text lines; it scrolls instead.</summary>
    public const float MinStatsLines = 5f;

    /// <inheritdoc cref="MinStatsLines"/>
    public static float MinStatsHeight(float lineHeight) => lineHeight * MinStatsLines;

    // Footer button heights, the single source of truth for both the drawing code and the measurement.
    // #298: line-height multiples rather than the old 26-32px constants. The ImGui font is 18f * uiScale
    // (up to 25px at 4K), so those buttons were nearly full of text on a high-DPI display; the tiers now
    // come from ResolverPanelLayout, shared with every other resolver panel.
    public static float DoneButtonHeight(float lineHeight)    => ResolverPanelLayout.OptionRowHeight(lineHeight);
    public static float BackButtonHeight(float lineHeight)    => ResolverPanelLayout.ActionRowHeight(lineHeight);
    public static float SecondaryRowHeight(float lineHeight)  => ResolverPanelLayout.ActionRowHeight(lineHeight);   // Undo (#343: Auto-place removed)
    public static float RestartButtonHeight(float lineHeight) => ResolverPanelLayout.ActionRowHeight(lineHeight);

    /// <summary>
    /// Height of everything drawn BELOW the stat box: the status line, the optional cohesion and
    /// table-edge lines, and the button stack (Done, optional Back, Undo, separator, Restart).
    /// </summary>
    /// <param name="itemSpacingY">ImGui's <c>ItemSpacing.Y</c> — the gap the layout adds after each item.</param>
    /// <param name="lineHeight">ImGui's <c>GetTextLineHeight()</c> — what the button heights scale with.</param>
    /// <param name="statusTextHeight">Wrapped height of the hint / error line (always present).</param>
    /// <param name="cohesionTextHeight">Wrapped height of the #269 cohesion warning, or null when absent.</param>
    /// <param name="edgeTextHeight">Wrapped height of the #029 table-edge line, or null when absent.</param>
    /// <param name="allowCancel">Whether the Back button is offered (cancellable placements only).</param>
    public static float FooterHeight(float itemSpacingY, float lineHeight, float statusTextHeight,
        float? cohesionTextHeight, float? edgeTextHeight, bool allowCancel)
    {
        // Two Spacing() calls bracket the text block: one above it, one below.
        float height = itemSpacingY * 2f;

        height += statusTextHeight + itemSpacingY;
        if (cohesionTextHeight.HasValue) height += cohesionTextHeight.Value + itemSpacingY;
        if (edgeTextHeight.HasValue)     height += edgeTextHeight.Value + itemSpacingY;

        height += DoneButtonHeight(lineHeight) + itemSpacingY;
        if (allowCancel) height += BackButtonHeight(lineHeight) + itemSpacingY;
        height += SecondaryRowHeight(lineHeight) + itemSpacingY;
        height += itemSpacingY * 2f + 1f;                 // the Separator above Restart
        height += RestartButtonHeight(lineHeight) + itemSpacingY;

        return height;
    }

    /// <summary>
    /// How tall the unit-stat box may be: everything left over after the footer, floored at
    /// <see cref="MinStatsHeight"/> so a very short panel still shows a scrollable stat block rather
    /// than a zero-height (or negative) child.
    /// </summary>
    public static float StatsHeight(float availableHeight, float footerHeight, float lineHeight) =>
        MathF.Max(MinStatsHeight(lineHeight), availableHeight - footerHeight);
}
