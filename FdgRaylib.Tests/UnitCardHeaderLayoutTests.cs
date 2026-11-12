using FdgRaylib.Rendering;
using NUnit.Framework;

namespace FdgRaylib.Tests;

// #399 - the in-game army list card centred its header and then right-aligned the red "Activated" tag
// onto the same line with nothing reserved for it, so a long enough unit name grew under the tag and
// the two overlapped. The widths come from ImGui.CalcTextSize (which needs a live frame), so the
// decision they feed is what is pinned here.
[TestFixture]
public class UnitCardHeaderLayoutTests
{
    private const float Available = 300f;
    private const float TagWidth  = 60f;
    private const float Spacing   = 8f;

    [Test]
    public void ShortHeader_KeepsTheTagBesideIt()
    {
        var plan = UnitCardHeaderLayout.Decide(headerWidth: 100f, TagWidth, Spacing, Available);

        Assert.That(plan.TagOnHeaderLine, Is.True, "the ordinary case must look exactly as it always has");
        Assert.That(plan.CenterHeaderWithin, Is.EqualTo(Available - TagWidth - Spacing),
            "and it centres in the room the tag left, not across the tag");
    }

    [Test]
    public void LongHeader_DropsTheTagToItsOwnLine()
    {
        // The reported case: a name long enough that the centred header reaches the tag's ground.
        var plan = UnitCardHeaderLayout.Decide(headerWidth: 280f, TagWidth, Spacing, Available);

        Assert.That(plan.TagOnHeaderLine, Is.False);
        Assert.That(plan.CenterHeaderWithin, Is.EqualTo(Available),
            "with the line to itself the header centres across the whole card again");
    }

    [Test]
    public void HeaderThatExactlyFillsTheRoom_StillSharesTheLine()
    {
        float room = Available - TagWidth - Spacing;

        Assert.That(UnitCardHeaderLayout.Decide(room, TagWidth, Spacing, Available).TagOnHeaderLine,
            Is.True, "the spacing is already reserved, so filling the room is not yet a collision");
    }

    [Test]
    public void OneMorePixel_TipsItOntoTheSecondLine()
    {
        float room = Available - TagWidth - Spacing;

        Assert.That(UnitCardHeaderLayout.Decide(room + 1f, TagWidth, Spacing, Available).TagOnHeaderLine,
            Is.False, "the boundary is where the overlap used to start");
    }

    [Test]
    public void NoTag_LeavesTheHeaderTheWholeCard()
    {
        // A unit that has not activated (and every unit in a destroyed card) passes no tag width; the
        // header must not be squeezed into a gap reserved for something that is never drawn.
        var plan = UnitCardHeaderLayout.Decide(headerWidth: 280f, tagWidth: 0f, Spacing, Available);

        Assert.That(plan.TagOnHeaderLine, Is.False);
        Assert.That(plan.CenterHeaderWithin, Is.EqualTo(Available));
    }
}
