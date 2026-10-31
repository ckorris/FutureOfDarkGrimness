using FdgRaylib.Rendering.TacticalOverlay;
using NUnit.Framework;

namespace FdgRaylib.Tests;

/// <summary>
/// #247 — the field's anchor used to be decided in four places: the move-request branch, the
/// GhostAnchoredField mode flag (since retired), the pins/hover target resolve, and #230's placement
/// fallback. That is how two of them could in principle paint at once, and how a move job with nothing
/// pinned could end up painting nothing at all. One contest now picks a single winner; these pin the
/// priority order, since "exactly one field, and never zero when there is something to show" is the
/// property the feature rests on.
/// </summary>
[TestFixture]
public class FieldAnchorPlanTests
{
    // Named so each case reads as a scenario rather than five bools.
    private static FieldAnchorKind Resolve(bool showReach = true, bool hover = false,
        bool moveJob = false, bool pin = false, bool placement = false) =>
        FieldAnchorPlan.Resolve(showReach, hover, moveJob, pin, placement);

    [Test]
    public void ToggledOff_NothingAnchorsAtAll()
    {
        // Every source live at once: V still wins over all of them.
        Assert.That(Resolve(showReach: false, hover: true, moveJob: true,
            pin: true, placement: true), Is.EqualTo(FieldAnchorKind.None));
    }

    [Test]
    public void Idle_WithNothingHovered_DrawsNothing()
    {
        Assert.That(Resolve(), Is.EqualTo(FieldAnchorKind.None));
    }

    [Test]
    public void Idle_HoveringAUnit_AnchorsOnIt()
    {
        // The case that did not exist before #247: no move job, no placement, just inspecting the map.
        Assert.That(Resolve(hover: true), Is.EqualTo(FieldAnchorKind.Hover));
    }

    [Test]
    public void Hover_BeatsAPlacementInProgress()
    {
        Assert.That(Resolve(hover: true, placement: true), Is.EqualTo(FieldAnchorKind.Hover));
    }

    [Test]
    public void Hover_BeatsBothMoveJobPictures()
    {
        Assert.That(Resolve(hover: true, moveJob: true),
            Is.EqualTo(FieldAnchorKind.Hover), "move job showing its own ghosts");
        Assert.That(Resolve(hover: true, moveJob: true, pin: true),
            Is.EqualTo(FieldAnchorKind.Hover), "move job showing a pinned target");
    }

    [Test]
    public void MoveJob_WithNothingPinned_ShowsItsOwnGhosts()
    {
        // The regression that prompted this: with the retired GhostAnchoredField defaulting off, a move
        // job with no pin drew NOTHING, and the only way to a field was a checkbox in the Esc menu.
        Assert.That(Resolve(moveJob: true), Is.EqualTo(FieldAnchorKind.Ghost));
    }

    [Test]
    public void MoveJob_WithAPin_ShowsTheTargetAnchoredPicture()
    {
        // Pinning IS the gesture that asks for "where can I stand to shoot it".
        Assert.That(Resolve(moveJob: true, pin: true), Is.EqualTo(FieldAnchorKind.Target));
    }

    [Test]
    public void MoveJob_DoesNotFallThroughToAPlacement()
    {
        // The two can't be live together in practice, but the contest must still name one winner rather
        // than letting a stale placement source paint under a move job.
        Assert.That(Resolve(moveJob: true, placement: true), Is.EqualTo(FieldAnchorKind.Ghost));
        Assert.That(Resolve(moveJob: true, pin: true, placement: true), Is.EqualTo(FieldAnchorKind.Target));
    }

    [Test]
    public void Placement_AnchorsOnGhosts()
    {
        Assert.That(Resolve(placement: true), Is.EqualTo(FieldAnchorKind.Ghost));
    }

    [Test]
    public void APinAloneWithoutAMoveJob_DrawsNothing()
    {
        // Pins are scoped to a move job; a leftover pin must not resurrect a field while idle.
        Assert.That(Resolve(pin: true), Is.EqualTo(FieldAnchorKind.None));
    }
}
