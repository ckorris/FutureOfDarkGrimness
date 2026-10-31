using FdgRaylib.Rendering;
using NUnit.Framework;

namespace FdgRaylib.Tests;

/// <summary>
/// #214 — a reposition placement (Teleport, Fanatic, reposition-at-activation) bounds each model within
/// MaxDistanceFromStartInches of its OWN start and passes the whole table as the deployment zone, so the
/// zone outline shows nothing. These pin which models get a reach ring and which one reads as live.
/// </summary>
[TestFixture]
public class ReachRingPlanTests
{
    [Test]
    public void NothingPlacedYet_EveryModelGetsARing_AndTheFirstIsActive()
    {
        var rings = ReachRingPlan.Build(modelCount: 3, placedCount: 0, dragIndex: null, groupDrop: false);

        Assert.That(rings.Select(r => r.ModelIndex), Is.EqualTo(new[] { 0, 1, 2 }));
        Assert.That(rings.Select(r => r.IsActive), Is.EqualTo(new[] { true, false, false }),
            "single mode places models in order, so only the next one is live.");
    }

    [Test]
    public void PlacedModels_DropOutOfTheRingSet()
    {
        var rings = ReachRingPlan.Build(modelCount: 4, placedCount: 2, dragIndex: null, groupDrop: false);

        Assert.That(rings.Select(r => r.ModelIndex), Is.EqualTo(new[] { 2, 3 }),
            "a model that has already been placed is bounded by nothing further - its ring is spent.");
        Assert.That(rings.Single(r => r.IsActive).ModelIndex, Is.EqualTo(2));
    }

    [Test]
    public void AllPlaced_NoRings()
    {
        var rings = ReachRingPlan.Build(modelCount: 3, placedCount: 3, dragIndex: null, groupDrop: false);

        Assert.That(rings, Is.Empty,
            "with every model down the resolver is waiting on Done - no click is bounded by a radius.");
    }

    [Test]
    public void DraggingAPlacedModel_BringsItsRingBack_AsTheActiveOne()
    {
        // All three placed, then model 1 is picked up to be re-dropped.
        var rings = ReachRingPlan.Build(modelCount: 3, placedCount: 3, dragIndex: 1, groupDrop: false);

        Assert.That(rings.Select(r => r.ModelIndex), Is.EqualTo(new[] { 1 }),
            "the dragged model is bounded by its own start again; the others are settled.");
        Assert.That(rings[0].IsActive, Is.True);
    }

    [Test]
    public void DraggingWhileOthersAreStillUnplaced_TheDragTakesTheActiveRing()
    {
        // Two of four down, and one of those two picked back up: the click lands on the dragged model,
        // not on the next unplaced one, so the "next" ring must not also claim to be live.
        var rings = ReachRingPlan.Build(modelCount: 4, placedCount: 2, dragIndex: 0, groupDrop: false);

        Assert.That(rings.Select(r => r.ModelIndex), Is.EqualTo(new[] { 2, 3, 0 }));
        Assert.That(rings.Where(r => r.IsActive).Select(r => r.ModelIndex), Is.EqualTo(new[] { 0 }),
            "exactly one ring is live, and it is the dragged model - not the next unplaced one.");
    }

    [Test]
    public void GroupDrop_EveryRingIsLive()
    {
        var rings = ReachRingPlan.Build(modelCount: 3, placedCount: 0, dragIndex: null, groupDrop: true);

        Assert.That(rings.Select(r => r.ModelIndex), Is.EqualTo(new[] { 0, 1, 2 }));
        Assert.That(rings.All(r => r.IsActive), Is.True,
            "one click drops the whole unit, so no single model's ring is more relevant than another's.");
    }

    [Test]
    public void NoModels_NoRings()
    {
        Assert.That(ReachRingPlan.Build(modelCount: 0, placedCount: 0, dragIndex: null, groupDrop: false),
            Is.Empty);
    }
}
