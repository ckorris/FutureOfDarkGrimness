using System;
using System.Collections.Generic;
using FDG;

namespace FdgRaylib.Rendering.Resolvers;

/// <summary>
/// #326 — the single-mode model roster: the list of a unit's models shown in the movement panel while one
/// model at a time is being moved, with how far each has travelled against its own budget.
///
/// <para>#295 moved "switch to another model" from a Space cycle onto a click on the model itself, which
/// freed Space to confirm but left the set of models with no representation anywhere except the table. The
/// only affordance was a hover highlight that appears when the cursor happens to cross a base — and during
/// a move the cursor is already busy aiming the waypoint ghost, so the highlight reads as ghost feedback
/// rather than as "click me". Players who were never told the gesture existed never found it. This is the
/// canvas-plus-object-list answer: the table holds the workspace, the panel holds the roster, and selection
/// is bound both ways (#286). It also gives the left click on the table ONE meaning again — place a
/// waypoint — instead of "place a waypoint unless you happened to be over a base".</para>
///
/// <para>Arithmetic only, no ImGui calls, so the vertical budget is unit-testable exactly like
/// <see cref="PlacementPanelLayout"/> and <see cref="ActionMenuLayout"/>; the drawing around it is
/// hand-verified.</para>
/// </summary>
public static class ModelRoster
{
    /// <summary>Height of one roster row as a multiple of the text line height (#298's rule: never a pixel
    /// constant — the font is <c>18f * uiScale</c>, so a comfortable row on one display is a sliver on a
    /// 4K one). Shorter than an option row: these are list entries, not buttons to aim at.</summary>
    public const float RowLineMultiple = 1.35f;

    /// <summary>How many rows the roster shows before it scrolls. A unit big enough to overflow this is
    /// exactly the unit whose roster must not push Done off the bottom of the panel.</summary>
    public const int MaxVisibleRows = 5;

    /// <summary>The roster never shrinks below this many rows; it scrolls instead. A safety valve for an
    /// absurdly short panel, not a case the real layout hits.</summary>
    public const int MinVisibleRows = 2;

    /// <summary>Text lines drawn between the roster and the button stack: the selected model's distance /
    /// band readout plus the two control-hint lines. None of them wrap (they are unwrapped
    /// <c>TextDisabled</c> / <c>TextUnformatted</c> calls), so the count is exact.</summary>
    public const int HintLines = 3;

    /// <inheritdoc cref="RowLineMultiple"/>
    public static float RowHeight(float lineHeight) => lineHeight * RowLineMultiple;

    /// <summary>
    /// Height of everything drawn BELOW the roster: the hint block, the mode button, the two checkboxes,
    /// and the button stack (Done, optional Back, Skip/Auto-advance, separator, Clear). Costed first so the
    /// roster can take the remainder without ever pushing Done past the bottom edge (#288).
    /// </summary>
    /// <param name="itemSpacingY">ImGui's <c>ItemSpacing.Y</c> — the gap the layout adds after each item.</param>
    /// <param name="lineHeight">ImGui's <c>GetTextLineHeight()</c> — what the button tiers scale with.</param>
    /// <param name="frameHeight">ImGui's <c>GetFrameHeight()</c> — a default button / checkbox row.</param>
    /// <param name="allowCancel">Whether the Back button is offered (player-chosen moves only).</param>
    public static float FooterHeight(float itemSpacingY, float lineHeight, float frameHeight, bool allowCancel)
    {
        float height = (lineHeight + itemSpacingY) * HintLines;

        height += frameHeight + itemSpacingY;              // Mode: Single (G)
        height += (frameHeight + itemSpacingY) * 2f;       // Stay within Advance / Show targeting
        height += itemSpacingY;                            // the Spacing() above the button stack

        height += ResolverPanelLayout.OptionRowHeight(lineHeight) + itemSpacingY;               // Done
        if (allowCancel) height += ResolverPanelLayout.ActionRowHeight(lineHeight) + itemSpacingY; // Back
        height += ResolverPanelLayout.ActionRowHeight(lineHeight) + itemSpacingY;               // Skip / Auto
        height += itemSpacingY * 2f + 1f;                                                       // Separator
        height += ResolverPanelLayout.ActionRowHeight(lineHeight) + itemSpacingY;               // Clear

        return height;
    }

    /// <summary>
    /// How tall the scrolling roster may be: <see cref="MaxVisibleRows"/> rows at most, fewer when the unit
    /// is smaller, less again when the panel cannot afford them — floored at <see cref="MinVisibleRows"/> so
    /// a very short panel still gets a scrollable list rather than a zero-height (or negative) child.
    /// </summary>
    public static float RosterHeight(float availableHeight, float footerHeight, float lineHeight, int rowCount)
    {
        float rowH   = RowHeight(lineHeight);
        float wanted = rowH * Math.Clamp(rowCount, MinVisibleRows, MaxVisibleRows);
        return MathF.Max(rowH * MinVisibleRows, MathF.Min(wanted, availableHeight - footerHeight));
    }

    /// <summary>
    /// Move a selection by <paramref name="delta"/> places through <paramref name="count"/> models,
    /// wrapping at both ends. A negative <paramref name="current"/> means "nothing selected yet": stepping
    /// forward lands on the first model, stepping back on the last. Returns -1 for an empty roster.
    /// </summary>
    public static int Cycle(int current, int count, int delta)
    {
        if (count <= 0) return -1;
        if (current < 0) return delta >= 0 ? 0 : count - 1;
        return ((current + delta) % count + count) % count;
    }

    /// <summary>
    /// How far a model must travel before it counts as having moved. A float epsilon, not a game rule: a
    /// committed distance is a sum of 2D hops, so a waypoint dropped on a model's own position can land a
    /// hair off zero. Shared by <see cref="BuildRow"/> and <see cref="UnmovedOrdinals"/> so the greyed rows
    /// and #333's Done warning can never disagree about who has moved.
    /// </summary>
    public const float DistanceEpsilon = 0.0001f;

    /// <summary>
    /// #333: the 1-based roster ordinals of the models that have not moved at all, in roster order — the
    /// same "Model N" numbers the rows show. Done raises its confirmation from this list: finishing with
    /// models still on the start line is nearly always one the player forgot, because single mode moves one
    /// model at a time and this roster is the only place the stragglers are visible.
    /// </summary>
    /// <param name="movedInches">Committed distance per living model, in roster order.</param>
    public static List<int> UnmovedOrdinals(IReadOnlyList<float> movedInches)
    {
        var ordinals = new List<int>();
        for (int i = 0; i < movedInches.Count; i++)
            if (movedInches[i] <= DistanceEpsilon) ordinals.Add(i + 1);
        return ordinals;
    }

    /// <summary>
    /// One roster row's numbers. <paramref name="cappedByTerrain"/> is #155's difficult-terrain cap: a model
    /// whose COMMITTED path already crossed difficult terrain has a lower real maximum than its budget, and
    /// the row must show the number the move will actually be held to.
    /// </summary>
    public static ModelRosterRow BuildRow(int ordinal, float movedInches, float maxAdvanceInches,
        float maxDistanceInches, bool cappedByTerrain)
    {
        const float Epsilon = DistanceEpsilon;
        float max = cappedByTerrain
            ? MathF.Min(maxDistanceInches, GameWideConstants.DIFFICULT_TERRAIN_MOVE_CAP_INCHES)
            : maxDistanceInches;
        return new ModelRosterRow(ordinal, movedInches, max,
            InRush: movedInches + Epsilon >= maxAdvanceInches,
            Started: movedInches > Epsilon,
            CappedByTerrain: cappedByTerrain);
    }

    /// <summary>The row's left-hand text. Models carry no name in the engine, so they are numbered — the
    /// same "Model N" vocabulary the wound-assignment list uses. The caret marks the selection for anyone
    /// who cannot rely on the highlight colour alone.</summary>
    public static string RowNameText(int ordinal, bool selected) => (selected ? "> " : "  ") + "Model " + ordinal;

    /// <summary>The row's right-hand text: distance travelled against this model's own maximum. Two decimals
    /// to match the selected model's detail line — a roster reading 6.0" beside a detail line reading 5.96"
    /// looks like a bug.</summary>
    public static string RowDistanceText(ModelRosterRow row) =>
        $"{row.MovedInches:F2}\" / {FormatInches(row.MaxInches)}\"";

    /// <summary>Whole inches without a pointless ".0", one decimal otherwise. Shared with the movement
    /// panel's own readouts so one number never renders two ways in one panel.</summary>
    public static string FormatInches(float value)
    {
        float frac = value - MathF.Floor(value);
        if (frac < 0.05f || frac > 0.95f) return MathF.Round(value).ToString("0");
        return value.ToString("0.0");
    }
}

/// <summary>
/// #326 — one roster row's numbers, built by <see cref="ModelRoster.BuildRow"/>.
/// </summary>
/// <param name="Ordinal">1-based position in the unit's living models — the N in "Model N".</param>
/// <param name="MovedInches">Distance this model's committed waypoints already cover.</param>
/// <param name="MaxInches">The most it may travel, difficult-terrain cap included.</param>
/// <param name="InRush">Past its Advance allowance, so it can no longer shoot this activation.</param>
/// <param name="Started">Has at least one committed waypoint — the "already dealt with" cue.</param>
/// <param name="CappedByTerrain">#155's 6" cap is what <see cref="MaxInches"/> reflects.</param>
public readonly record struct ModelRosterRow(int Ordinal, float MovedInches, float MaxInches,
    bool InRush, bool Started, bool CappedByTerrain);
