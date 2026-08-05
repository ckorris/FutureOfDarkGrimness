using System;

namespace FdgRaylib.Rendering.Resolvers;

/// <summary>
/// #333 — the vertical budget for the rule-details strip under a string menu's option list, expressed as
/// arithmetic so it is unit-testable (the drawing around it is hand-verified, exactly like
/// <see cref="ActionMenuLayout"/> and <see cref="PlacementPanelLayout"/>).
///
/// <para>The melee weapon menu's counterpart to the shoot panel's Details pane: the rules of the option
/// under attention, spelled out once with room to read, instead of every option's rules expanded under
/// every button (#298). It scrolls internally rather than growing, so a weapon with five rules cannot
/// push the options it is meant to be compared against out of the panel.</para>
///
/// <para>Every gap is a line-height multiple (#298's rule): the ImGui font is <c>18f * uiScale</c>, up to
/// 25px at 4K, so a pixel constant that reads as padding on the developer's display is a hairline on a
/// high-DPI one.</para>
///
/// <para>The strip exists only on menus whose request carries
/// <see cref="FDG.StageResolution.Requests.StringSelectionRequest.OptionRules"/> — the melee weapon
/// picker today. <see cref="GuiStringSelectionResolver"/> is shared with the action, spell and ability
/// menus, and those keep their layout untouched.</para>
/// </summary>
public static class OptionRuleDetailsLayout
{
    /// <summary>Share of the panel's height the strip takes when the panel is roomy enough to spend it.</summary>
    public const float HeightFraction = 0.28f;

    /// <summary>The strip never shrinks below this many text lines: a header plus two rules' worth of
    /// name-and-first-description-line, which is the point below which it says nothing useful.</summary>
    public const float MinLines = 5f;

    /// <summary>Nor does it ever take more of the panel than this — the options are the thing being
    /// chosen between, and they keep the majority of the height however many rules a weapon has.</summary>
    public const float MaxHeightFraction = 0.38f;

    /// <summary>Gap between the option list and the strip, so the two read as separate things.</summary>
    public const float GapAboveLineMultiple = 0.5f;

    /// <inheritdoc cref="GapAboveLineMultiple"/>
    public static float GapAbove(float lineHeight) => lineHeight * GapAboveLineMultiple;

    /// <summary>
    /// Height of the strip itself: <see cref="HeightFraction"/> of the panel, floored at
    /// <see cref="MinLines"/> and capped at <see cref="MaxHeightFraction"/>. On a panel too short to
    /// honour both, the cap wins — the floor must never hand the strip more than the options have.
    /// </summary>
    public static float StripHeight(float panelHeight, float lineHeight)
        => MathF.Min(panelHeight * MaxHeightFraction,
                     MathF.Max(lineHeight * MinLines, panelHeight * HeightFraction));

    /// <summary>What the strip costs the option list above it: the gap plus the strip.</summary>
    public static float TotalHeight(float panelHeight, float lineHeight)
        => GapAbove(lineHeight) + StripHeight(panelHeight, lineHeight);
}
