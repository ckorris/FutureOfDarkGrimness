using FdgRaylib.Rendering.Presentation;
using NUnit.Framework;

namespace FdgRaylib.Tests;

// #325 — a banner band stacks like the roll stack does: the OLDEST entry keeps the band's anchor and
// newer ones pile up above it. Mid-screen is crowded (toasts, then headlines, then notices, then the
// roll stack at the bottom), so each band gets a ceiling from the band above and trims to fit. The
// direction of that trim is the load-bearing part: dropping from the oldest end shifts the stack back
// down toward its anchor, which is what keeps the NEWEST banner on screen.
//
// BandGap is 10 in the overlay, so an entry costs its height + 10.
[TestFixture]
public class BannerBandLayoutTests
{
    [Test]
    public void ASingleBanner_SitsOnTheAnchor()
    {
        var heights = new[] { 50f };

        Assert.That(BannerOverlay.StackTop(heights, 0, 400f), Is.EqualTo(400f));
        Assert.That(BannerOverlay.FirstVisible(heights, 400f, ceiling: 300f), Is.EqualTo(0));
    }

    [Test]
    public void NewerBanners_StackAboveTheAnchor()
    {
        var heights = new[] { 50f, 50f, 50f };

        // Two entries above the anchored one, each costing 50 + 10.
        Assert.That(BannerOverlay.StackTop(heights, 0, 400f), Is.EqualTo(280f));
        Assert.That(BannerOverlay.StackTop(heights, 1, 400f), Is.EqualTo(340f),
            "dropping the oldest shifts the whole stack back DOWN toward the anchor");
    }

    [Test]
    public void ABandThatOverflows_DropsItsOldest()
    {
        var heights = new[] { 50f, 50f, 50f };

        // Whole stack reaches 280, above a ceiling of 300 - so the oldest goes and the rest fits at 340.
        Assert.That(BannerOverlay.FirstVisible(heights, 400f, ceiling: 300f), Is.EqualTo(1));
        Assert.That(BannerOverlay.FirstVisible(heights, 400f, ceiling: 270f), Is.EqualTo(0),
            "with room for all three, nothing is dropped");
    }

    [Test]
    public void TheNewestBanner_IsNeverDropped()
    {
        // A ceiling below the anchor itself: nothing legitimately fits, but the thing that just happened
        // still has to be shown, even overhanging its band.
        var heights = new[] { 60f, 60f, 60f };

        Assert.That(BannerOverlay.FirstVisible(heights, 400f, ceiling: 900f), Is.EqualTo(2));
        Assert.That(BannerOverlay.StackTop(heights, 2, 400f), Is.EqualTo(400f),
            "the survivor sits on the anchor");
    }

    [Test]
    public void TallBanners_CostMoreRoomThanShortOnes()
    {
        // A two-line headline pushes the band's top further up than a one-liner would.
        var oneLine  = new[] { 40f, 40f };
        var twoLines = new[] { 40f, 90f };

        Assert.That(BannerOverlay.StackTop(oneLine, 0, 400f), Is.EqualTo(350f));
        Assert.That(BannerOverlay.StackTop(twoLines, 0, 400f), Is.EqualTo(300f));
    }
}
