using System.Collections.Generic;
using FDG.Utilities;

namespace FdgRaylib.Rendering.Resolvers;

/// <summary>
/// #337 — finds the status badge the engine appended to a unit-picker label
/// (<see cref="UnitStatusLabel.ShakenSuffix"/>) and hands it back as the
/// <see cref="RuleHoverText.Segment"/>s the row is drawn from, so the badge reads amber and explains
/// itself on hover while the rest of the heading stays ordinary text.
///
/// <para>Locates the suffix inside the finished label rather than rebuilding the label out of parts, for
/// the same reason <see cref="OptionRuleSegments"/> does (#336/#306): the label is the option's identity
/// and the front end must not paraphrase it. Concatenating every returned segment's <c>Text</c> therefore
/// reproduces the heading exactly — including the "[1] " hotkey prefix and any "(reason)" the invalid-row
/// formatter appended — so a row with no badge draws byte-identically to how it did before.</para>
///
/// <para>Matched from the RIGHT: the badge is appended last, after the transport suffix, and a unit could
/// legitimately be NAMED something containing the word Shaken.</para>
/// </summary>
internal static class UnitStatusBadge
{
    /// <summary>The tooltip heading — the state, not the whole badge, so the hover reads "Shaken" over the
    /// catalog's sentence rather than repeating the parenthetical back at the player.</summary>
    private const string ShakenRuleName = "Shaken";

    /// <summary>
    /// <paramref name="heading"/> split around the status badge, or null when it carries none (the caller
    /// then draws the heading as one plain string, which is the overwhelmingly common case).
    /// </summary>
    public static IReadOnlyList<RuleHoverText.Segment>? Segments(string heading)
    {
        if (string.IsNullOrEmpty(heading)) return null;

        int at = heading.LastIndexOf(UnitStatusLabel.ShakenSuffix, System.StringComparison.Ordinal);
        if (at < 0) return null;

        var segments = new List<RuleHoverText.Segment>(3);
        if (at > 0) segments.Add(new RuleHoverText.Segment(heading[..at], null, null));
        segments.Add(new RuleHoverText.Segment(UnitStatusLabel.ShakenSuffix, ShakenRuleName,
            UnitStatusLabel.ShakenDescription));

        int after = at + UnitStatusLabel.ShakenSuffix.Length;
        if (after < heading.Length)
            segments.Add(new RuleHoverText.Segment(heading[after..], null, null));

        return segments;
    }
}
