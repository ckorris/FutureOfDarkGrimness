using System;
using System.Collections.Generic;
using FDG.StageResolution.Requests;

namespace FdgRaylib.Rendering.Resolvers;

/// <summary>
/// #336 — turns a string menu's option label plus its <see cref="StringSelectionRequest.OptionRules"/>
/// into the <see cref="RuleHoverText.Segment"/>s the shoot panel's rule treatment is drawn from, so a
/// melee weapon button explains its rules the same way a shooting weapon row does: names underlined
/// in place, descriptions on hover, instead of a permanent block of prose under the button (#298).
///
/// <para>The engine never formats the label out of parts a front end could reassemble - the label is the
/// option's identity on the wire (#306) and is matched back verbatim - so this LOCATES the rule names
/// inside the finished string rather than rebuilding it. Concatenating every returned segment's
/// <c>Text</c> therefore reproduces the label exactly, which is what keeps the button reading the same
/// as the option the player is actually picking.</para>
/// </summary>
internal static class OptionRuleSegments
{
    /// <summary>
    /// Splits <paramref name="label"/> around the rule names in <paramref name="rules"/>, marking each
    /// name as a hoverable segment and everything else as literal text.
    ///
    /// <para>Matched from the RIGHT, in reverse order. The rule names are appended to the end of a
    /// weapon's datasheet line in attachment order, so the last rule owns the last occurrence of its name;
    /// scanning forward from the start would let a weapon whose NAME contains a rule word ("Rending
    /// Blade - A2, AP0, Rending") underline the wrong run. A name that cannot be found is skipped, leaving
    /// that stretch as ordinary text - the label still renders correctly, it just loses one hover target.</para>
    /// </summary>
    public static IReadOnlyList<RuleHoverText.Segment> Build(
        string label, IReadOnlyList<StringSelectionRequest.OptionRule>? rules)
    {
        if (string.IsNullOrEmpty(label) || rules == null || rules.Count == 0)
            return new[] { new RuleHoverText.Segment(label, null, null) };

        // Where each rule's name starts in the label, or -1 for one that isn't there. Searched over the
        // span still up for grabs rather than via LastIndexOf's start-index overload, whose match window
        // is easy to get off by a name's length - and being off by one here silently underlines the wrong
        // run instead of failing.
        int[] starts = new int[rules.Count];
        int searchEnd = label.Length;   // nothing at or past here is still unclaimed
        for (int i = rules.Count - 1; i >= 0; i--)
        {
            string name = rules[i].Name;
            starts[i] = string.IsNullOrEmpty(name) || name.Length > searchEnd
                ? -1
                : label.AsSpan(0, searchEnd).LastIndexOf(name.AsSpan(), StringComparison.Ordinal);
            if (starts[i] >= 0) searchEnd = starts[i];
        }

        var segments = new List<RuleHoverText.Segment>();
        int cursor = 0;
        for (int i = 0; i < rules.Count; i++)
        {
            if (starts[i] < cursor) continue;   // absent, or overlapping one already taken
            if (starts[i] > cursor)
                segments.Add(new RuleHoverText.Segment(label[cursor..starts[i]], null, null));
            segments.Add(new RuleHoverText.Segment(rules[i].Name, rules[i].Name, rules[i].Description));
            cursor = starts[i] + rules[i].Name.Length;
        }
        if (cursor < label.Length)
            segments.Add(new RuleHoverText.Segment(label[cursor..], null, null));

        return segments;
    }

    /// <summary>
    /// Word-wraps a run of segments to <paramref name="maxWidth"/>, keeping each rule name whole so it
    /// stays one hover target, and coalescing the literal runs back together per line so the drawn line
    /// measures as one string rather than accumulating per-word rounding.
    ///
    /// <para>Sibling of <see cref="ArmyListLayout.WrapSegments"/>, with a different break policy for a
    /// different job: that one wraps a short rules list and may only break BEFORE a rule (so its ", "
    /// separators stay glued to the line above), while an option label leads with a long literal
    /// ("2x Great Sword - A2, AP1") that has to word-wrap like ordinary text. Always returns at least one
    /// line, matching <see cref="ResolverText.Wrap"/>.</para>
    /// </summary>
    /// <param name="measure">Text width in pixels (ImGui.CalcTextSize in production; anything in tests).</param>
    public static List<List<RuleHoverText.Segment>> Wrap(
        IReadOnlyList<RuleHoverText.Segment> segments, Func<string, float> measure, float maxWidth)
    {
        var lines = new List<List<RuleHoverText.Segment>>();
        var line = new List<RuleHoverText.Segment>();
        float lineWidth = 0f;

        foreach (RuleHoverText.Segment chunk in Chunks(segments))
        {
            float width = measure(chunk.Text);
            if (line.Count > 0 && lineWidth + width > maxWidth)
            {
                lines.Add(Coalesce(line));
                line = new List<RuleHoverText.Segment>();
                lineWidth = 0f;
            }
            line.Add(chunk);
            lineWidth += width;
        }

        lines.Add(Coalesce(line));
        return lines;
    }

    /// <summary>
    /// The smallest pieces a line may break between: a rule name is one indivisible chunk, and literal
    /// text splits into words. Each word keeps the spaces that FOLLOWED it, so concatenation is lossless
    /// and a break leaves the trailing space at the end of the line above rather than indenting the next.
    /// </summary>
    private static IEnumerable<RuleHoverText.Segment> Chunks(IReadOnlyList<RuleHoverText.Segment> segments)
    {
        foreach (RuleHoverText.Segment segment in segments)
        {
            if (segment.IsRule || segment.Text.Length == 0) { yield return segment; continue; }

            string text = segment.Text;
            int i = 0;
            while (i < text.Length)
            {
                int wordEnd = i;
                while (wordEnd < text.Length && text[wordEnd] != ' ') wordEnd++;
                while (wordEnd < text.Length && text[wordEnd] == ' ') wordEnd++;
                yield return new RuleHoverText.Segment(text[i..wordEnd], null, null);
                i = wordEnd;
            }
        }
    }

    /// <summary>Merges each run of adjacent literal chunks on a line back into one segment.</summary>
    private static List<RuleHoverText.Segment> Coalesce(List<RuleHoverText.Segment> line)
    {
        var merged = new List<RuleHoverText.Segment>(line.Count);
        foreach (RuleHoverText.Segment segment in line)
        {
            if (!segment.IsRule && merged.Count > 0 && !merged[^1].IsRule)
            {
                merged[^1] = new RuleHoverText.Segment(merged[^1].Text + segment.Text, null, null);
                continue;
            }
            merged.Add(segment);
        }
        return merged;
    }
}
