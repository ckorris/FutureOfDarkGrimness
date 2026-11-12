using System.Collections.Generic;
using FDG;
using FdgRaylib.Rendering.Resolvers;
using NUnit.Framework;

namespace FdgRaylib.Tests;

// #399 - a grouped move let a unit's own models finish stacked on each other. The reported case was Dark
// Elf Raider jetbikes: a 35x60mm RECTANGLE (1.378" x 2.362"). A group step turns every phantom to one
// shared heading, so dragging a rank of them sideways swings each 2.362"-long base across its neighbours
// while the centres keep exactly the spacing they had - and nothing in the preview looked at the moving
// unit at all. Circles are rotation-invariant, which is why every other unit in the game hid this.
[TestFixture]
public class GroupSelfOverlapTests
{
    // The reported unit's base, and the facings that matter: models are laid out in a rank along X, so
    // "abreast" points them along +Z (the 2.362" axis runs front-to-back, clear of the neighbour) and
    // "turned" points them along +X (the long axis now runs along the rank, into the neighbour).
    private static IBaseShape Jetbike() => new RectangleBase(1.3779528f, 2.3622048f);
    private static readonly Float2 Abreast = new(0f, 1f);
    private static readonly Float2 Turned  = new(1f, 0f);

    private static IBaseShape Round() => new CircleBase(BaseShapeDefaults.CircleRadiusInches);

    private static GroupSelfOverlap.Pose? Pose(IBaseShape shape, float x, float z, Float2 facing)
        => new GroupSelfOverlap.Pose(shape, new Position(x, z), facing);

    [Test]
    public void RankOfRectangles_TurnedAlongTheRank_IsFlagged()
    {
        // Three jetbikes abreast, bases 0.1" apart edge to edge (1.378 + 0.1 = 1.478" centre to centre) -
        // exactly how the formation library packs them. Turn all three 90 degrees in place, as a sideways
        // group drag does, and each 2.362" base now spans well past its neighbour's centre.
        var starts = new List<GroupSelfOverlap.Pose?>
        {
            Pose(Jetbike(), 0f, 0f, Abreast),
            Pose(Jetbike(), 1.478f, 0f, Abreast),
            Pose(Jetbike(), 2.956f, 0f, Abreast),
        };
        var ends = new List<GroupSelfOverlap.Pose?>
        {
            Pose(Jetbike(), 0f, 0f, Turned),
            Pose(Jetbike(), 1.478f, 0f, Turned),
            Pose(Jetbike(), 2.956f, 0f, Turned),
        };
        var blocked = new bool[3];

        var anchor = GroupSelfOverlap.Mark(starts, ends, blocked);

        Assert.That(anchor, Is.Not.Null, "a non-null anchor IS 'the step is illegal'");
        Assert.That(blocked, Is.All.True, "every model of a stacked rank is part of a flagged pair");
    }

    [Test]
    public void SameRank_LeftAbreast_IsNotFlagged()
    {
        // The control: the identical positions, untouched attitudes. If this flagged, the fix would have
        // made rectangular-based units unable to move at all.
        var poses = new List<GroupSelfOverlap.Pose?>
        {
            Pose(Jetbike(), 0f, 0f, Abreast),
            Pose(Jetbike(), 1.478f, 0f, Abreast),
            Pose(Jetbike(), 2.956f, 0f, Abreast),
        };
        var blocked = new bool[3];

        Assert.That(GroupSelfOverlap.Mark(poses, poses, blocked), Is.Null);
        Assert.That(blocked, Is.All.False);
    }

    [Test]
    public void RigidTranslation_NeverFlags()
    {
        // A plain drag moves every base by the same delta at the same attitude, so no pair can newly
        // collide. This is the overwhelmingly common step and must stay free.
        var starts = new List<GroupSelfOverlap.Pose?>
        {
            Pose(Jetbike(), 0f, 0f, Abreast),
            Pose(Jetbike(), 1.478f, 0f, Abreast),
        };
        var ends = new List<GroupSelfOverlap.Pose?>
        {
            Pose(Jetbike(), 6f, 4f, Abreast),
            Pose(Jetbike(), 7.478f, 4f, Abreast),
        };
        var blocked = new bool[2];

        Assert.That(GroupSelfOverlap.Mark(starts, ends, blocked), Is.Null);
        Assert.That(blocked, Is.All.False);
    }

    [Test]
    public void AlreadyOverlappingPair_IsNotFrozenInPlace()
    {
        // "Not worsened", the same clause the engine's ValidateNoSelfOverlap and ValidateEndsOnTable
        // carry: a pair that ALREADY overlaps (an older save, a forced move, the very bug this fixes)
        // must not be rejected on every step it can make, or the unit can never be untangled.
        var starts = new List<GroupSelfOverlap.Pose?>
        {
            Pose(Round(), 0f, 0f, Abreast),
            Pose(Round(), 0.1f, 0f, Abreast),
        };
        var ends = new List<GroupSelfOverlap.Pose?>
        {
            Pose(Round(), 2f, 0f, Abreast),
            Pose(Round(), 2.2f, 0f, Abreast),   // still overlapping, but no worse
        };
        var blocked = new bool[2];

        Assert.That(GroupSelfOverlap.Mark(starts, ends, blocked), Is.Null);
        Assert.That(blocked, Is.All.False);
    }

    [Test]
    public void CirclesDrivenTogether_AreStillFlagged()
    {
        // The rule is not about rectangles - a rectangle is only what made it VISIBLE. Two round bases
        // driven onto the same spot are the same illegal end state.
        var starts = new List<GroupSelfOverlap.Pose?>
        {
            Pose(Round(), 0f, 0f, Abreast),
            Pose(Round(), 4f, 0f, Abreast),
        };
        var ends = new List<GroupSelfOverlap.Pose?>
        {
            Pose(Round(), 2f, 0f, Abreast),
            Pose(Round(), 2.1f, 0f, Abreast),
        };
        var blocked = new bool[2];

        Assert.That(GroupSelfOverlap.Mark(starts, ends, blocked), Is.Not.Null);
        Assert.That(blocked, Is.All.True);
    }

    [Test]
    public void DeadModels_TakeNoPart()
    {
        // A dead model is a null pose. It must neither be blamed nor block a living model that walks
        // over the ground it fell on.
        var starts = new List<GroupSelfOverlap.Pose?> { Pose(Round(), 0f, 0f, Abreast), null };
        var ends   = new List<GroupSelfOverlap.Pose?> { Pose(Round(), 2f, 0f, Abreast), null };
        var blocked = new bool[2];

        Assert.That(GroupSelfOverlap.Mark(starts, ends, blocked), Is.Null);
        Assert.That(blocked, Is.All.False);
    }

    [Test]
    public void Anchor_SitsBetweenTheFirstFlaggedPair()
    {
        // The caption is drawn once for the unit, midway between the two bases fighting over the ground,
        // at the larger of their radii - so it clears both bases whatever sizes are involved.
        var starts = new List<GroupSelfOverlap.Pose?>
        {
            Pose(Round(), 0f, 0f, Abreast),
            Pose(Jetbike(), 8f, 0f, Abreast),
        };
        var ends = new List<GroupSelfOverlap.Pose?>
        {
            Pose(Round(), 3f, 1f, Abreast),
            Pose(Jetbike(), 3.2f, 1f, Abreast),
        };
        var blocked = new bool[2];

        var anchor = GroupSelfOverlap.Mark(starts, ends, blocked);

        Assert.That(anchor, Is.Not.Null);
        Assert.That(anchor!.Value.at.x, Is.EqualTo(3.1f).Within(0.0001f));
        Assert.That(anchor.Value.at.z, Is.EqualTo(1f).Within(0.0001f));
        Assert.That(anchor.Value.radiusInches,
            Is.EqualTo(Jetbike().CircumscribedRadiusInches).Within(0.0001f),
            "the bigger base decides how far above the pair the caption clears");
    }
}
