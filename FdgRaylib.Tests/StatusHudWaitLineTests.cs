using FdgRaylib.Rendering;
using NUnit.Framework;

namespace FdgRaylib.Tests;

// #399 - "Waiting on Bob: Place Unit Models" is drawn in three runs so the name can carry the player's
// table colour, and the colon sat hard against the name. raylib's MeasureText returns
// sum(advances) + (count - 1) * spacing - no trailing gap, since a string has nothing after its last
// glyph - so laying the runs out at raw measured widths closed one inter-glyph gap at every seam.
// The measuring needs a loaded font (and answers 0 with no window), so what is pinned here is the
// integer layout the drawing calls.
[TestFixture]
public class StatusHudWaitLineTests
{
    private const int AreaWidth = 1000;

    [Test]
    public void EachRunStartsOneSpacingPastThePreviousRunsEnd()
    {
        var (prefixX, nameX, restX) = StatusHudOverlay.WaitLineRuns(
            AreaWidth, prefixW: 100, nameW: 40, restW: 200, glyphSpacing: 2);

        Assert.That(nameX - (prefixX + 100), Is.EqualTo(2),
            "the gap between 'Waiting on ' and the name must match the gaps inside them");
        Assert.That(restX - (nameX + 40), Is.EqualTo(2),
            "the seam the bug was reported against: the colon after the player's name");
    }

    [Test]
    public void TheWholeLineIsCentered_CountingBothSeams()
    {
        const int prefixW = 100, nameW = 40, restW = 200, spacing = 2;

        var (prefixX, _, restX) = StatusHudOverlay.WaitLineRuns(
            AreaWidth, prefixW, nameW, restW, spacing);

        int lineEnd = restX + restW;
        Assert.That(prefixX, Is.EqualTo(AreaWidth - lineEnd),
            "left and right margins must match, so the two seams have to be in the total width too");
    }

    [Test]
    public void ZeroSpacing_ReproducesTheOldFlushLayout()
    {
        // The regression itself, expressed as the degenerate case: with no seam allowance the runs sit
        // flush and the colon touches the name. Pins that the spacing is what moved them apart.
        var (prefixX, nameX, restX) = StatusHudOverlay.WaitLineRuns(
            AreaWidth, prefixW: 100, nameW: 40, restW: 200, glyphSpacing: 0);

        Assert.That(nameX, Is.EqualTo(prefixX + 100));
        Assert.That(restX, Is.EqualTo(nameX + 40));
    }

    [Test]
    public void GlyphSpacing_MirrorsRaylibsDefaultFontRule()
    {
        // raylib: spacing = fontSize / defaultFontSize (10), integer division, size floored at 10.
        Assert.That(StatusHudOverlay.GlyphSpacing(20), Is.EqualTo(2), "the wait line's own size");
        Assert.That(StatusHudOverlay.GlyphSpacing(24), Is.EqualTo(2), "the main strip's size");
        Assert.That(StatusHudOverlay.GlyphSpacing(8), Is.EqualTo(1),
            "raylib floors the size at 10 before dividing, so a tiny size still spaces by 1");
    }
}
