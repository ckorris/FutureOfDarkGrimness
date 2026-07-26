using FdgRaylib.Rendering.TacticalOverlay;
using NUnit.Framework;

namespace FdgRaylib.Tests;

/// <summary>
/// #247 — the field's anchor used to be decided in four places (the move-request branch, the
/// GhostAnchoredField mode flag, the pins/hover target resolve, and #230's placement fallback), which is
/// how two of them could in principle paint at once. One contest now picks a single winner; these pin the
/// priority order, since "exactly one field on screen" is the property the feature rests on.
/// </summary>
[TestFixture]
public class FieldAnchorPlanTests
{
    // Named so each case reads as a scenario rather than six bools.
    private static FieldAnchorKind Resolve(bool showReach = true, bool hover = false,
        bool moveJob = false, bool ghostMode = false, bool pin = false, bool placement = false) =>
        FieldAnchorPlan.Resolve(showReach, hover, moveJob, ghostMode, pin, placement);

    [Test]
    public void ToggledOff_NothingAnchorsAtAll()
    {
        // Every source live at once: V still wins over all of them.
        Assert.That(Resolve(showReach: false, hover: true, moveJob: true, ghostMode: true,
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
    public void Hover_BeatsBothMoveJobModes()
    {
        Assert.That(Resolve(hover: true, moveJob: true, ghostMode: true),
            Is.EqualTo(FieldAnchorKind.Hover), "ghost-anchored move job");
        Assert.That(Resolve(hover: true, moveJob: true, ghostMode: false, pin: true),
            Is.EqualTo(FieldAnchorKind.Hover), "target-anchored move job");
    }

    [Test]
    public void MoveJob_KeepsItsTwoExistingModes()
    {
        Assert.That(Resolve(moveJob: true, ghostMode: true), Is.EqualTo(FieldAnchorKind.Ghost));
        Assert.That(Resolve(moveJob: true, ghostMode: false, pin: true), Is.EqualTo(FieldAnchorKind.Target));
    }

    [Test]
    public void MoveJob_TargetAnchoredWithNoPin_DrawsNothing()
    {
        // Pre-#247 behaviour, deliberately preserved: no pin, no target field.
        Assert.That(Resolve(moveJob: true, ghostMode: false, pin: false), Is.EqualTo(FieldAnchorKind.None));
    }

    [Test]
    public void MoveJob_DoesNotFallThroughToAPlacement()
    {
        // The two can't be live together in practice, but the contest must still name one winner rather
        // than letting a stale placement source paint under a move job.
        Assert.That(Resolve(moveJob: true, ghostMode: false, pin: false, placement: true),
            Is.EqualTo(FieldAnchorKind.None));
    }

    [Test]
    public void Placement_AnchorsOnGhosts_RegardlessOfTheMoveJobMode()
    {
        // GhostAnchoredField picks between the two MOVE pictures; a placement has no pinned target to
        // offer the alternative, so the flag must not reach it in either state.
        Assert.That(Resolve(placement: true, ghostMode: false), Is.EqualTo(FieldAnchorKind.Ghost));
        Assert.That(Resolve(placement: true, ghostMode: true), Is.EqualTo(FieldAnchorKind.Ghost));
    }

    [Test]
    public void APinAloneWithoutAMoveJob_DrawsNothing()
    {
        // Pins are scoped to a move job; a leftover pin must not resurrect a field while idle.
        Assert.That(Resolve(pin: true), Is.EqualTo(FieldAnchorKind.None));
    }
}
