using System;
using System.Collections.Generic;
using System.Globalization;

namespace FdgRaylib.Rendering;

/// <summary>
/// #329 — the pure geometry and formatting behind the army list overlay's card view, kept free of
/// ImGui so it can be unit-tested: masonry column packing, tab ordering, segment-boundary wrapping
/// for hoverable rule lines, and the weapon-table cell spellings that make the cards read like the
/// printed Army Forge list. ASCII only (CLAUDE.md).
/// </summary>
public static class ArmyListLayout
{
    /// <summary>How many card columns fit in <paramref name="availWidth"/>, at least one, capped so
    /// very wide windows don't shrink to a strip of tiny cards.</summary>
    public static int ColumnCount(float availWidth, float minCardWidth, int maxColumns = 3)
    {
        if (minCardWidth <= 0f) return 1;
        return Math.Clamp((int)(availWidth / minCardWidth), 1, Math.Max(1, maxColumns));
    }

    /// <summary>
    /// Masonry packing: each card goes to the column that is currently shortest (ties -> leftmost),
    /// in input order, so cards keep their army-list order within a column and the columns stay
    /// near-even without reflowing on every wound. Returns the column index per card.
    /// </summary>
    /// <param name="heights">Estimated or measured card heights, in card order.</param>
    public static int[] PackColumns(IReadOnlyList<float> heights, int columnCount)
    {
        columnCount = Math.Max(1, columnCount);
        var assignment = new int[heights.Count];
        var columnHeight = new float[columnCount];

        for (int i = 0; i < heights.Count; i++)
        {
            int shortest = 0;
            for (int c = 1; c < columnCount; c++)
                if (columnHeight[c] < columnHeight[shortest])
                    shortest = c;

            assignment[i] = shortest;
            columnHeight[shortest] += heights[i];
        }
        return assignment;
    }

    /// <summary>
    /// Tab order: the local player's list(s) first — the one you reach for mid-game — then everyone
    /// else, each group keeping its original (slot) order.
    /// </summary>
    public static List<T> OrderTabs<T>(IReadOnlyList<T> slots, Func<T, bool> isLocal)
    {
        var ordered = new List<T>(slots.Count);
        foreach (T slot in slots)
            if (isLocal(slot)) ordered.Add(slot);
        foreach (T slot in slots)
            if (!isLocal(slot)) ordered.Add(slot);
        return ordered;
    }

    /// <summary>
    /// Wraps a run of <see cref="RuleHoverText.Segment"/>s into lines no wider than
    /// <paramref name="maxWidth"/>, breaking only at segment boundaries (never inside a rule name,
    /// so each name stays one hover target) and only ever BEFORE a rule segment — the ", "
    /// separators stay glued to the end of the line above, the way the printed list reads. A rule
    /// longer than the whole line keeps its own line rather than being broken.
    /// </summary>
    /// <param name="measure">Text width in pixels (ImGui.CalcTextSize in production; anything in tests).</param>
    public static List<List<RuleHoverText.Segment>> WrapSegments(
        IReadOnlyList<RuleHoverText.Segment> segments, Func<string, float> measure, float maxWidth)
    {
        var lines = new List<List<RuleHoverText.Segment>>();
        var line = new List<RuleHoverText.Segment>();
        float lineWidth = 0f;

        foreach (RuleHoverText.Segment segment in segments)
        {
            float width = measure(segment.Text);
            bool overflows = line.Count > 0 && lineWidth + width > maxWidth;
            if (overflows && segment.IsRule)
            {
                lines.Add(line);
                line = new List<RuleHoverText.Segment>();
                lineWidth = 0f;
            }
            line.Add(segment);
            lineWidth += width;
        }
        if (line.Count > 0) lines.Add(line);
        return lines;
    }

    // ── Weapon-table cell spellings (the printed list's vocabulary) ─────────────────────────────────

    /// <summary>RNG cell: 24" for ranged, "-" for melee — the printed list's spelling (the canvas
    /// tooltip says "Melee"; in a column headed RNG the dash reads better).</summary>
    public static string RangeText(float rangeInches) =>
        rangeInches > 0f
            ? rangeInches.ToString("0.##", CultureInfo.InvariantCulture) + "\""
            : "-";

    /// <summary>ATK cell: A3.</summary>
    public static string AttacksText(int attacks) => $"A{attacks}";

    /// <summary>AP cell: the value, or "-" for none.</summary>
    public static string ApText(int armorPenetration) =>
        armorPenetration > 0 ? armorPenetration.ToString(CultureInfo.InvariantCulture) : "-";

    /// <summary>Weapon cell: "6x Razor Claws" for a grouped row, bare name for a single. The counts
    /// come from LIVING models only (AllWeapons filters the dead), so the list shrinks with
    /// casualties — the live-state advantage over the printout.</summary>
    public static string CountedName(int count, string name) =>
        count > 1 ? $"{count}x {name}" : name;
}
