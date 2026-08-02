using System;
using System.Collections.Generic;
using FDG.StageResolution.Requests;

namespace FdgRaylib.Rendering.Resolvers;

/// <summary>
/// #309 — the Choose Action menu's pinned footer, expressed as arithmetic rather than ImGui calls so the
/// vertical budget is unit-testable (the drawing around it is hand-verified, exactly like
/// <see cref="PlacementPanelLayout"/>).
///
/// <para>Pass is the one entry in that menu that cannot be taken back: it ends the activation outright.
/// In a 2026-08-02 multiplayer game both players ended activations that still had actions left by
/// clicking Pass where it happened to sit under the cursor — it was just the last row of an ordinary
/// option list, one row below Shoot. So Pass is lifted out of the scrolling list and pinned to the
/// bottom of the panel: a gap above separates it from the real actions, and a gap below raises it clear
/// of the panel's bottom edge instead of leaving it flush against it. Back (#248) stacks directly above
/// it, so both "leave this menu" actions stay reachable when the option list scrolls.</para>
///
/// <para>Every gap is a line-height multiple (#298's rule): the ImGui font is <c>18f * uiScale</c>, up to
/// 25px at 4K, so a pixel constant that reads as padding on the developer's display is a hairline on a
/// high-DPI one.</para>
///
/// <para>The footer exists only on menus that actually offer Pass — i.e. Choose Action.
/// <see cref="GuiStringSelectionResolver"/> is shared with the weapon / spell / ability menus, and those
/// keep their original single-scroll layout, so this can't move their Back button.</para>
/// </summary>
public static class ActionMenuLayout
{
    /// <summary>
    /// The option whose presence turns a string menu into a pinned-footer action menu. Matched by name,
    /// the same way <see cref="ResolverHotkeys"/> pins its letters to the built-in action names — the
    /// engine describes the menu as a plain list of strings and the client decides how it reads.
    /// A custom action may not take this name (ChooseActionStage skips colliding offers), so the match
    /// can only ever hit the real Pass.
    /// </summary>
    public const string PassOption = FDG.Stages.ChooseActionStage.PASS_CHOICE_NAME;

    /// <summary>Gap between the scrolling option list and the footer — the separation that makes Pass
    /// read as "not one of the actions above".</summary>
    public const float GapAboveLineMultiple = 0.7f;

    /// <summary>Gap under Pass: the padding that raises it off the panel's bottom edge.</summary>
    public const float GapBelowLineMultiple = 0.7f;

    /// <summary>Gap between the Back button and Pass, so the two footer rows don't read as one slab.</summary>
    public const float GapBetweenLineMultiple = 0.4f;

    /// <summary>The option list never shrinks below this many text lines; it scrolls instead. A safety
    /// valve for an absurdly short panel, not a case the real layout hits.</summary>
    public const float MinListLines = 3f;

    /// <inheritdoc cref="GapAboveLineMultiple"/>
    public static float GapAbove(float lineHeight) => lineHeight * GapAboveLineMultiple;

    /// <inheritdoc cref="GapBelowLineMultiple"/>
    public static float GapBelow(float lineHeight) => lineHeight * GapBelowLineMultiple;

    /// <inheritdoc cref="GapBetweenLineMultiple"/>
    public static float GapBetween(float lineHeight) => lineHeight * GapBetweenLineMultiple;

    /// <summary>
    /// Height of everything pinned below the scrolling option list: the gap above, the optional Back
    /// button and its gap, the Pass row, and the gap that lifts it off the bottom edge.
    /// </summary>
    /// <param name="lineHeight">ImGui's <c>GetTextLineHeight()</c> — what every gap scales with.</param>
    /// <param name="passRowHeight">Measured height of the Pass row: a plain option row when Pass is
    /// available, taller when it is greyed and its reason wraps to several lines.</param>
    /// <param name="allowCancel">Whether the Back button is offered (a pristine activation, #248).</param>
    public static float FooterHeight(float lineHeight, float passRowHeight, bool allowCancel)
    {
        float height = GapAbove(lineHeight) + passRowHeight + GapBelow(lineHeight);
        if (allowCancel)
            height += ResolverPanelLayout.ActionRowHeight(lineHeight) + GapBetween(lineHeight);
        return height;
    }

    /// <summary>
    /// How tall the scrolling option list may be: whatever the panel has left under the instructions
    /// once the footer is costed, floored at <see cref="MinListLines"/> so a very short panel still
    /// gets a scrollable list rather than a zero-height (or negative) child.
    /// </summary>
    public static float ListHeight(float panelHeight, float listTop, float footerHeight, float lineHeight)
        => MathF.Max(lineHeight * MinListLines, panelHeight - listTop - footerHeight);

    /// <summary>Index of <see cref="PassOption"/> in an option list, or -1.</summary>
    public static int PassIndex(IReadOnlyList<string> options)
    {
        for (int i = 0; i < options.Count; i++)
            if (options[i] == PassOption) return i;
        return -1;
    }

    /// <summary>
    /// Index of <see cref="PassOption"/> among the greyed-out options, or -1. Pass keeps its pinned spot
    /// when it is unavailable (Instinctive compels an attack, #197) rather than dropping into the
    /// invalid list — the menu above it must not shift under the cursor depending on whether passing
    /// happens to be legal this activation.
    /// </summary>
    public static int PassIndex(IReadOnlyList<StringSelectionRequest.InvalidOption> options)
    {
        for (int i = 0; i < options.Count; i++)
            if (options[i].Option == PassOption) return i;
        return -1;
    }
}
