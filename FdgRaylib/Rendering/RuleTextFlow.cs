using System;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using FDG.ArmyBuilding;
using FDG.SaveLoad;
using ImGuiNET;

namespace FdgRaylib.Rendering;

// #259 — inline rule text with per-rule underlines and hover tooltips, the way the real Army Forge shows
// special rules. Everything the Forge prints about a unit is a formatted string with rule names baked into
// it ("4x Blade (24\", A6, AP(4), Reliable)"), which is a single ImGui item and therefore has no per-rule
// hover region. This splits those lines into SEGMENTS - plain text and rule names - and lays them out by
// hand so each rule name gets its own underline and its own hit rectangle.
//
// Two rendering modes, because read-only text and interactive controls need different mechanics:
//   Draw()                 - full manual layout with word wrapping, for the read-only stat lines.
//   DecorateControlLabel() - decorates the label ImGui already drew for a radio/checkbox, so the control
//                            stays clickable exactly as before; no wrapping (nor did those labels wrap).
public static class RuleTextFlow
{
    /// <summary>One run of a rule line: literal text, or a rule name (<paramref name="Rule"/> non-null)
    /// that gets an underline and a hover tooltip. <see cref="Text"/> is always what is displayed, so
    /// concatenating every segment's text reproduces the original line verbatim.</summary>
    public readonly record struct RuleSegment(string Text, SpecialRuleEntry? Rule)
    {
        public bool IsRule => Rule is not null;
    }

    /// <summary>A chunk placed by <see cref="Layout"/>: its text, the rule it belongs to (if any), and its
    /// position as an x offset within wrapped line <see cref="Line"/>.</summary>
    public readonly record struct PlacedChunk(string Text, SpecialRuleEntry? Rule, float X, float Width, int Line);

    // ── Segment builders (pure; unit-tested) ────────────────────────────────────────────────────────────

    /// <summary>The comma-joined rule list the Forge prints under a unit, one segment per rule.</summary>
    public static IReadOnlyList<RuleSegment> RuleList(IEnumerable<SpecialRuleEntry> rules)
    {
        var segments = new List<RuleSegment>();
        foreach (SpecialRuleEntry rule in rules)
        {
            if (segments.Count > 0) segments.Add(new RuleSegment(", ", null));
            segments.Add(new RuleSegment(rule.PrintableName, rule));
        }
        return segments;
    }

    /// <summary>Segmented form of <see cref="ArmyBuilderScreen.WeaponSummary"/> - identical text, with the
    /// trailing special-rule names split out as hoverable segments.</summary>
    public static IReadOnlyList<RuleSegment> WeaponLine(WeaponFileEntry weapon)
    {
        var stats = new List<string>();
        if (weapon.RangeInches > 0) stats.Add($"{weapon.RangeInches}\"");
        stats.Add($"A{weapon.Attacks}");
        if (weapon.ArmorPenetration != 0) stats.Add($"AP({weapon.ArmorPenetration})");

        var segments = new List<RuleSegment>
        {
            new($"{weapon.Quantity}x {weapon.Name} ({string.Join(", ", stats)}", null),
        };
        foreach (SpecialRuleEntry rule in weapon.SpecialRules)
        {
            segments.Add(new RuleSegment(", ", null));
            segments.Add(new RuleSegment(rule.PrintableName, rule));
        }
        segments.Add(new RuleSegment(")", null));
        return segments;
    }

    /// <summary>Segmented form of <see cref="ArmyForgeScreen.ItemSummary"/>.</summary>
    public static IReadOnlyList<RuleSegment> ItemLine(ItemEntry item)
    {
        if (item.Rules.Count == 0)
            return new[] { new RuleSegment($"{item.Quantity}x {item.Name}", null) };

        var segments = new List<RuleSegment> { new($"{item.Quantity}x {item.Name} (", null) };
        foreach (SpecialRuleEntry rule in item.Rules)
        {
            if (segments.Count > 1) segments.Add(new RuleSegment(", ", null));
            segments.Add(new RuleSegment(rule.PrintableName, rule));
        }
        segments.Add(new RuleSegment(")", null));
        return segments;
    }

    /// <summary>
    /// An upgrade option's label, with any rule names inside it made hoverable. Unlike weapon/item lines the
    /// label is free text straight from the imported book ("Energy Spear (A2, Rending)"), so the rules have
    /// to be found by scanning - but only against the rules THIS option actually grants, never the whole
    /// ~300-name corpus, so an unrelated word can never light up as a rule.
    /// </summary>
    public static IReadOnlyList<RuleSegment> OptionLabel(UpgradeOption option, string displayedLabel)
    {
        var candidates = new List<SpecialRuleEntry>(option.RulesGained);
        foreach (WeaponFileEntry weapon in option.WeaponsGained) candidates.AddRange(weapon.SpecialRules);
        foreach (ItemEntry item in option.ItemsGained) candidates.AddRange(item.Rules);
        return ScanLabel(displayedLabel, candidates);
    }

    /// <summary>
    /// Splits <paramref name="label"/> around the printable names of <paramref name="candidates"/>. Longest
    /// name first (so "Shred in melee" wins over "Shred"), and a match must stand on word boundaries, so the
    /// rule "Shred" does not light up inside the weapon name "Shredder Rifle".
    /// </summary>
    internal static IReadOnlyList<RuleSegment> ScanLabel(string label, IEnumerable<SpecialRuleEntry> candidates)
    {
        List<SpecialRuleEntry> ordered = candidates
            .Where(c => !string.IsNullOrEmpty(c.PrintableName))
            .GroupBy(c => c.PrintableName, StringComparer.Ordinal)
            .Select(g => g.First())
            .OrderByDescending(c => c.PrintableName.Length)
            .ToList();

        var segments = new List<RuleSegment>();
        int plainStart = 0;
        int i = 0;
        while (i < label.Length)
        {
            SpecialRuleEntry? hit = null;
            foreach (SpecialRuleEntry candidate in ordered)
            {
                string name = candidate.PrintableName;
                if (i + name.Length > label.Length) continue;
                if (string.CompareOrdinal(label, i, name, 0, name.Length) != 0) continue;
                if (!IsBoundary(label, i - 1) || !IsBoundary(label, i + name.Length)) continue;
                hit = candidate;
                break;
            }

            if (hit is null) { i++; continue; }

            if (i > plainStart) segments.Add(new RuleSegment(label[plainStart..i], null));
            segments.Add(new RuleSegment(hit.PrintableName, hit));
            i += hit.PrintableName.Length;
            plainStart = i;
        }
        if (plainStart < label.Length) segments.Add(new RuleSegment(label[plainStart..], null));
        return segments;
    }

    // A match may not run into an adjacent word character. Off-the-end indices count as boundaries.
    private static bool IsBoundary(string text, int index) =>
        index < 0 || index >= text.Length || !char.IsLetterOrDigit(text[index]);

    /// <summary>The literal line a segment list renders as - the invariant that keeps the segmented lines
    /// byte-identical to the strings they replaced.</summary>
    public static string Flatten(IReadOnlyList<RuleSegment> segments) =>
        string.Concat(segments.Select(s => s.Text));

    // ── Layout (pure given a text measurer; unit-tested) ────────────────────────────────────────────────

    /// <summary>
    /// Places every segment left to right, wrapping at <paramref name="wrapWidth"/>. Plain text wraps at
    /// word boundaries; a rule name is atomic and never splits across lines (a broken-in-half underline
    /// would read as two rules). <paramref name="measure"/> is the text width function - ImGui's at draw
    /// time, a stub in tests.
    /// </summary>
    public static IReadOnlyList<PlacedChunk> Layout(
        IReadOnlyList<RuleSegment> segments, float wrapWidth, Func<string, float> measure)
    {
        var placed = new List<PlacedChunk>();
        float x = 0f;
        int line = 0;

        foreach (RuleSegment segment in segments)
        {
            foreach (string chunk in segment.IsRule ? new[] { segment.Text } : SplitWords(segment.Text))
            {
                float width = measure(chunk);
                // Wrap before an over-long chunk, but never onto an empty line (that would loop forever on
                // a chunk wider than the pane - it just overflows instead, as the un-segmented text did).
                if (x > 0f && x + width > wrapWidth)
                {
                    // A wrap swallows the space that caused it, so the next line doesn't start indented.
                    if (chunk.Trim().Length == 0) { line++; x = 0f; continue; }
                    line++;
                    x = 0f;
                }
                placed.Add(new PlacedChunk(chunk, segment.Rule, x, width, line));
                x += width;
            }
        }
        return placed;
    }

    /// <summary>How many wrapped lines <paramref name="segments"/> occupies - what a caller needs before
    /// drawing when it sizes a hit rectangle around the text (see the list pane's row selectable).</summary>
    public static int MeasureLines(IReadOnlyList<RuleSegment> segments, float wrapWidth, Func<string, float> measure)
    {
        IReadOnlyList<PlacedChunk> placed = Layout(segments, wrapWidth, measure);
        return placed.Count == 0 ? 1 : placed[^1].Line + 1;
    }

    /// <inheritdoc cref="MeasureLines(IReadOnlyList{RuleSegment}, float, Func{string, float})"/>
    public static int MeasureLines(IReadOnlyList<RuleSegment> segments, float wrapWidth) =>
        MeasureLines(segments, wrapWidth, MeasureImGui);

    // Words keep their trailing whitespace, so a wrap lands between words and the text reassembles exactly.
    private static IEnumerable<string> SplitWords(string text)
    {
        int start = 0;
        for (int i = 0; i < text.Length; i++)
        {
            if (text[i] != ' ') continue;
            while (i + 1 < text.Length && text[i + 1] == ' ') i++;
            yield return text[start..(i + 1)];
            start = i + 1;
        }
        if (start < text.Length) yield return text[start..];
    }

    // ── Drawing ─────────────────────────────────────────────────────────────────────────────────────────

    private static float MeasureImGui(string text) => ImGui.CalcTextSize(text).X;

    /// <summary>
    /// Draws a segmented line at the cursor in <paramref name="color"/>, wrapping at the remaining content
    /// width, with each rule underlined and tooltipped. Painted straight onto the window draw list and
    /// backed by a <c>Dummy</c> of the same footprint, so it takes exactly the vertical space the
    /// <c>Text</c> call it replaces did and stays click-through (the list pane's full-row selectable, which
    /// sits underneath, keeps receiving its clicks).
    /// </summary>
    public static void Draw(IReadOnlyList<RuleSegment> segments, RuleGlossary glossary, ImGuiCol color)
    {
        float wrapWidth = MathF.Max(1f, ImGui.GetContentRegionAvail().X);
        IReadOnlyList<PlacedChunk> placed = Layout(segments, wrapWidth, MeasureImGui);

        Vector2 origin = ImGui.GetCursorScreenPos();
        float lineHeight = ImGui.GetTextLineHeight();
        float lineAdvance = ImGui.GetTextLineHeightWithSpacing();
        uint textColor = ImGui.GetColorU32(color);
        int lines = placed.Count == 0 ? 1 : placed[^1].Line + 1;

        ImDrawListPtr drawList = ImGui.GetWindowDrawList();
        bool windowHovered = ImGui.IsWindowHovered(ImGuiHoveredFlags.None);
        Vector2 mouse = ImGui.GetMousePos();
        string? tooltip = null;

        foreach (PlacedChunk chunk in placed)
        {
            Vector2 pos = origin + new Vector2(chunk.X, chunk.Line * lineAdvance);
            drawList.AddText(pos, textColor, chunk.Text);
            if (chunk.Rule is null) continue;

            bool known = glossary.Describe(chunk.Rule) is not null;
            // An unimplemented rule underlines faintly: it is still hoverable (the tooltip says it does
            // nothing in play), but it must not advertise itself as documented.
            uint underline = known ? textColor : Fade(textColor, 0.45f);
            float baseline = pos.Y + lineHeight - 1f;
            drawList.AddLine(new Vector2(pos.X, baseline), new Vector2(pos.X + chunk.Width, baseline), underline);

            if (windowHovered &&
                mouse.X >= pos.X && mouse.X < pos.X + chunk.Width &&
                mouse.Y >= pos.Y && mouse.Y < pos.Y + lineHeight)
            {
                tooltip = glossary.Tooltip(chunk.Rule);
            }
        }

        // Reserve the footprint: N text lines plus the inter-line spacing between them. The layout system
        // adds the usual ItemSpacing.Y after the Dummy, matching a plain Text call's trailing gap.
        float height = lines * lineHeight + MathF.Max(0, lines - 1) * ImGui.GetStyle().ItemSpacing.Y;
        ImGui.Dummy(new Vector2(wrapWidth, height));

        if (tooltip is not null) SetWrappedTooltip(tooltip);
    }

    /// <summary>
    /// Underlines + tooltips the rule names inside the label ImGui just drew for a radio button or
    /// checkbox. Call immediately after the control: it reads the item rectangle and re-walks the same
    /// segments to find each rule's x span, leaving the control itself untouched (still clickable, still
    /// keyboard navigable). Single line only - these labels never wrapped.
    /// </summary>
    public static void DecorateControlLabel(IReadOnlyList<RuleSegment> segments, RuleGlossary glossary)
    {
        if (!segments.Any(s => s.IsRule)) return;

        // Where ImGui puts a radio/checkbox label: past the square, plus the inner spacing, at the frame's
        // top padding (see ImGui::Checkbox / RadioButton).
        Vector2 itemMin = ImGui.GetItemRectMin();
        ImGuiStylePtr style = ImGui.GetStyle();
        Vector2 origin = itemMin + new Vector2(
            ImGui.GetFrameHeight() + style.ItemInnerSpacing.X, style.FramePadding.Y);

        ImDrawListPtr drawList = ImGui.GetWindowDrawList();
        float lineHeight = ImGui.GetTextLineHeight();
        uint textColor = ImGui.GetColorU32(ImGuiCol.Text);
        bool windowHovered = ImGui.IsWindowHovered(ImGuiHoveredFlags.None);
        Vector2 mouse = ImGui.GetMousePos();
        string? tooltip = null;

        float x = 0f;
        foreach (RuleSegment segment in segments)
        {
            float width = MeasureImGui(segment.Text);
            if (segment.Rule is not null)
            {
                bool known = glossary.Describe(segment.Rule) is not null;
                float baseline = origin.Y + lineHeight - 1f;
                drawList.AddLine(new Vector2(origin.X + x, baseline), new Vector2(origin.X + x + width, baseline),
                    known ? textColor : Fade(textColor, 0.45f));

                if (windowHovered &&
                    mouse.X >= origin.X + x && mouse.X < origin.X + x + width &&
                    mouse.Y >= origin.Y && mouse.Y < origin.Y + lineHeight)
                {
                    tooltip = glossary.Tooltip(segment.Rule);
                }
            }
            x += width;
        }

        if (tooltip is not null) SetWrappedTooltip(tooltip);
    }

    // Rule descriptions are full sentences; without a wrap they render as one very wide tooltip.
    private static void SetWrappedTooltip(string text)
    {
        ImGui.BeginTooltip();
        ImGui.PushTextWrapPos(ImGui.GetFontSize() * 28f);
        ImGui.TextUnformatted(text);
        ImGui.PopTextWrapPos();
        ImGui.EndTooltip();
    }

    private static uint Fade(uint color, float alphaScale)
    {
        uint alpha = (color >> 24) & 0xFF;
        uint faded = (uint)Math.Clamp((int)MathF.Round(alpha * alphaScale), 0, 255);
        return (color & 0x00FFFFFF) | (faded << 24);
    }
}
