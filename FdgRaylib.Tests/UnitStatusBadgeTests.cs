using System.Collections.Generic;
using System.Linq;
using FDG.Utilities;
using FdgRaylib.Rendering;
using FdgRaylib.Rendering.Resolvers;
using NUnit.Framework;

namespace FdgRaylib.Tests;

// #337 - the app half of the Shaken badge. The engine appends "(Shaken - recovers)" to the activation
// picker's option label; this splits the finished heading around it so the badge can be drawn amber,
// underlined and hoverable while the rest stays ordinary text.
//
// The load-bearing invariant is LOSSLESSNESS: the row is measured from the plain heading string and the
// label is the option's identity on the wire (#306), so the segments must concatenate back to exactly what
// was passed in - no re-spacing, no paraphrase. Mirrors OptionRuleSegmentsTests, which pins the same
// property for the weapon-rule treatment this borrows.
[TestFixture]
public class UnitStatusBadgeTests
{
    private const string Badge = "(Shaken - recovers)";

    private static string Rebuilt(IReadOnlyList<RuleHoverText.Segment> segments) =>
        string.Concat(segments.Select(s => s.Text));

    [Test]
    public void NoBadge_ReturnsNull_SoTheRowDrawsThroughThePlainPath()
    {
        Assert.That(UnitStatusBadge.Segments("[1] Blade Squad"), Is.Null);
        Assert.That(UnitStatusBadge.Segments("[2] Warriors (in Rhino)"), Is.Null);
        Assert.That(UnitStatusBadge.Segments(""), Is.Null);
    }

    [Test]
    public void Badge_IsItsOwnHoverableSegment_AndTheRestIsLiteral()
    {
        const string heading = "[1] Blade Squad " + Badge;

        var segments = UnitStatusBadge.Segments(heading)!;

        Assert.That(Rebuilt(segments), Is.EqualTo(heading), "the heading must survive the split verbatim");
        Assert.That(segments.Count(s => s.IsRule), Is.EqualTo(1), "exactly one hover target");

        RuleHoverText.Segment badge = segments.Single(s => s.IsRule);
        Assert.That(badge.Text, Is.EqualTo(Badge));
        Assert.That(badge.RuleName, Is.EqualTo("Shaken"),
            "the tooltip heads with the STATE, not the whole parenthetical read back at the player");
        Assert.That(badge.IsDocumented, Is.True, "an undocumented badge would underline faintly and say "
            + "the rule does nothing in play - the opposite of the truth here");
        Assert.That(badge.Description, Is.EqualTo(UnitStatusLabel.ShakenDescription));
    }

    // The badge is appended after the transport suffix, and the invalid-row formatter appends its reason
    // after that - so the badge is in the MIDDLE, with literal text on both sides.
    [Test]
    public void Badge_InTheMiddleOfAHeading_KeepsBothLiteralSides()
    {
        const string heading = "Warriors (in Rhino) " + Badge + "  (Already activated.)";

        var segments = UnitStatusBadge.Segments(heading)!;

        Assert.That(Rebuilt(segments), Is.EqualTo(heading));
        Assert.That(segments.Select(s => s.Text), Is.EqualTo(new[]
        {
            "Warriors (in Rhino) ", Badge, "  (Already activated.)",
        }));
    }

    // A unit could legitimately be NAMED something containing the badge text; the appended one is the last
    // occurrence, so the search runs from the right (the same reason OptionRuleSegments matches backwards).
    [Test]
    public void ARepeatedBadgeString_MatchesTheAppendedOne()
    {
        const string heading = "The " + Badge + " Cult " + Badge;

        var segments = UnitStatusBadge.Segments(heading)!;

        Assert.That(Rebuilt(segments), Is.EqualTo(heading));
        Assert.That(segments.Count(s => s.IsRule), Is.EqualTo(1));
        Assert.That(segments[0].Text, Is.EqualTo("The " + Badge + " Cult "),
            "the name's copy stays literal - only the suffix the engine appended is the hover target");
    }

    [Test]
    public void TheBadgeTracksTheEngineConstant()
    {
        Assert.That(UnitStatusBadge.Segments("Squad " + UnitStatusLabel.ShakenSuffix), Is.Not.Null,
            "the locator must key off the engine's own constant, not a hand-copied string");
    }
}
