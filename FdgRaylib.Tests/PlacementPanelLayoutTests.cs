using FdgRaylib.Rendering.Resolvers;
using NUnit.Framework;

namespace FdgRaylib.Tests;

// #288 — the placement panel's unit-stat box used to be a fixed 118px keyhole, which is unusable for the
// case it exists for: an Ambush arrival, where the unit is off-table and cannot be hovered for its stats.
// It now fills whatever the footer leaves. The drawing is ImGui (hand-verified); the budget arithmetic
// lives here, because the failure mode it guards against — the stat box overlapping or shoving Done and
// Back off the bottom of the panel — is silent and only shows up on a rule-heavy unit.
[TestFixture]
public class PlacementPanelLayoutTests
{
    private const float Spacing = 8f;
    private const float LineH   = 16f;

    [Test]
    public void FooterHeight_CountsEveryButtonInTheStack()
    {
        float footer = PlacementPanelLayout.FooterHeight(Spacing, LineH, LineH,
            cohesionTextHeight: null, edgeTextHeight: null, allowCancel: false);

        Assert.That(footer, Is.GreaterThan(
            PlacementPanelLayout.DoneButtonHeight(LineH)
            + PlacementPanelLayout.SecondaryRowHeight(LineH)
            + PlacementPanelLayout.RestartButtonHeight(LineH) + LineH),
            "the button stack, the status line and the gaps between them all have to be paid for");
    }

    [Test]
    public void FooterHeight_PaysForTheBackButtonOnlyWhenItIsOffered()
    {
        float withoutBack = PlacementPanelLayout.FooterHeight(Spacing, LineH, LineH, null, null, allowCancel: false);
        float withBack    = PlacementPanelLayout.FooterHeight(Spacing, LineH, LineH, null, null, allowCancel: true);

        Assert.That(withBack - withoutBack,
            Is.EqualTo(PlacementPanelLayout.BackButtonHeight(LineH) + Spacing).Within(0.001f),
            "a cancellable placement (Disembark) shows Back; deployment and Ambush arrival do not");
    }

    [Test]
    public void FooterHeight_GrowsWithEachOptionalWarningLine()
    {
        float bare = PlacementPanelLayout.FooterHeight(Spacing, LineH, LineH, null, null, allowCancel: false);
        float withCohesion = PlacementPanelLayout.FooterHeight(Spacing, LineH, LineH, LineH, null, allowCancel: false);
        float withBoth = PlacementPanelLayout.FooterHeight(Spacing, LineH, LineH, LineH, LineH, allowCancel: false);

        Assert.That(withCohesion - bare, Is.EqualTo(LineH + Spacing).Within(0.001f));
        Assert.That(withBoth - withCohesion, Is.EqualTo(LineH + Spacing).Within(0.001f));
    }

    // A warning that wraps to three lines has to cost three lines, or the stat box eats the difference
    // and covers the Done button exactly when the player most needs to read the warning.
    [Test]
    public void FooterHeight_PaysForWrappedTextByItsMeasuredHeight()
    {
        float oneLine   = PlacementPanelLayout.FooterHeight(Spacing, LineH, LineH, null, null, allowCancel: false);
        float threeLine = PlacementPanelLayout.FooterHeight(Spacing, LineH, LineH * 3f, null, null, allowCancel: false);

        Assert.That(threeLine - oneLine, Is.EqualTo(LineH * 2f).Within(0.001f));
    }

    [Test]
    public void StatsHeight_FillsWhateverTheFooterLeaves()
    {
        Assert.That(PlacementPanelLayout.StatsHeight(availableHeight: 600f, footerHeight: 200f, LineH),
            Is.EqualTo(400f).Within(0.001f));
    }

    // A short panel must never produce a zero-height (or negative) child - the box scrolls instead.
    [Test]
    public void StatsHeight_NeverFallsBelowTheMinimum()
    {
        Assert.That(PlacementPanelLayout.StatsHeight(availableHeight: 120f, footerHeight: 200f, LineH),
            Is.EqualTo(PlacementPanelLayout.MinStatsHeight(LineH)));
        Assert.That(PlacementPanelLayout.StatsHeight(availableHeight: 0f, footerHeight: 0f, LineH),
            Is.EqualTo(PlacementPanelLayout.MinStatsHeight(LineH)));
    }

    // #298: the footer buttons are line-height multiples now, so a bigger UI scale must buy taller
    // buttons - and the stat box, which is costed from the same number, must shrink to pay for them.
    [Test]
    public void FooterHeight_GrowsWithTheFont()
    {
        float atSmallFont = PlacementPanelLayout.FooterHeight(Spacing, LineH, LineH, null, null, allowCancel: true);
        float atLargeFont = PlacementPanelLayout.FooterHeight(Spacing, LineH * 1.4f, LineH, null, null, allowCancel: true);

        Assert.That(atLargeFont, Is.GreaterThan(atSmallFont),
            "buttons sized from the font must cost more footer when the font grows");
        Assert.That(PlacementPanelLayout.DoneButtonHeight(LineH), Is.GreaterThan(LineH * 2f),
            "Done is a full option row - it must clear its own label by a wide margin");
    }

    // The regression this item exists for: with a realistic panel the box is far taller than the 118px
    // it replaced, so a unit with several described special rules is readable without a keyhole scroll.
    [Test]
    public void StatsHeight_IsMuchTallerThanTheOldFixedBox()
    {
        float footer = PlacementPanelLayout.FooterHeight(Spacing, LineH, LineH * 2f, null, null, allowCancel: false);
        // ResolverPanelLayout is 60% of screen height; ~500px of content region is a modest 1080p window.
        float stats = PlacementPanelLayout.StatsHeight(availableHeight: 500f, footerHeight: footer, LineH);

        Assert.That(stats, Is.GreaterThan(250f),
            "the whole point of #288 is that the stat box stops being a 118px keyhole");
    }
}
