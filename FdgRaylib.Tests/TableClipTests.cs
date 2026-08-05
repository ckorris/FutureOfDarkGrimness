using FDG;
using FdgRaylib.Rendering;
using NUnit.Framework;
using Raylib_cs;

namespace FdgRaylib.Tests;

/// <summary>
/// The clip rect that keeps objective rings (3" seizure, 9" placement exclusion) on the felt. A marker
/// may legally sit within 3" of an edge, so its ring geometry genuinely extends past the table; the fix
/// is the clip, not the geometry. These pin the rect to the felt bounds the renderer paints - if it
/// drifts, rings either spill onto the background again or get cut short of the edge.
/// </summary>
[TestFixture]
public class TableClipTests
{
    private const float TableW = GameWideConstants.DEFAULT_TABLE_WIDTH_INCHES;
    private const float TableH = GameWideConstants.DEFAULT_TABLE_HEIGHT_INCHES;

    [Test]
    public void RectMatchesTheFeltTheRendererPaints()
    {
        // Same origin/scale math (and the same int truncation) as RaylibRenderer.DrawTable.
        Rectangle r = TableClip.Rect(scale: 12f, originX: 40, originY: 25, tableH: TableH);

        Assert.That(r.X, Is.EqualTo(40f));
        Assert.That(r.Y, Is.EqualTo(25f));
        Assert.That(r.Width, Is.EqualTo((int)(TableW * 12f)));
        Assert.That(r.Height, Is.EqualTo((int)(TableH * 12f)));
    }

    [Test]
    public void RectFollowsPanAndZoom()
    {
        // The table pans and zooms under the cursor, so a clip captured against fixed screen bounds would
        // slide off the felt. Origin is passed through verbatim; extent scales.
        Rectangle a = TableClip.Rect(scale: 8f, originX: -120, originY: -60, tableH: TableH);
        Rectangle b = TableClip.Rect(scale: 16f, originX: -120, originY: -60, tableH: TableH);

        Assert.That(a.X, Is.EqualTo(-120f));
        Assert.That(a.Y, Is.EqualTo(-60f));
        Assert.That(b.Width, Is.EqualTo(a.Width * 2f).Within(1f));
        Assert.That(b.Height, Is.EqualTo(a.Height * 2f).Within(1f));
    }

    [Test]
    public void AnEdgeMarkersSeizureRingReachesOutsideTheRect()
    {
        // The case the clip exists for: an objective 1" from the right edge. Its 3" ring extends 2" past
        // the felt, so without a clip those pixels land on the background.
        const float scale = 12f;
        const float seizureInches = 3f;
        Rectangle felt = TableClip.Rect(scale, originX: 0, originY: 0, tableH: TableH);

        float centerPx = (TableW - 1f) * scale;
        float ringRightPx = centerPx + seizureInches * scale;

        Assert.That(ringRightPx, Is.GreaterThan(felt.X + felt.Width),
            "ring geometry must overhang - if it did not, the clip would be pointless");
        Assert.That(centerPx, Is.LessThan(felt.X + felt.Width), "the marker itself is still on the table");
    }
}
