using System;
using System.Collections.Generic;
using System.Linq;
using FDG;
using FdgRaylib.Rendering.Resolvers;
using NUnit.Framework;

namespace FdgRaylib.Tests;

// #409 - "even with the rotating command, it's very hard to maneuver with cavalry". A group step faces every
// model along one heading but keeps the centres' spacing, so a packed rank of 35x60mm bases could not turn
// more than about 20 degrees before the #399 gate (rightly) refused the stack. The fix re-forms the unit to
// face the new heading, choosing the arrangement that costs the worst-off model least. These pin the planner
// on the reporter's own unit, taken from their save: Iron Tusk-Beast Riders, ten riders and a joined Orc Boss
// on a 32mm round base, in ranks of six and five.
[TestFixture]
public class GroupReformTests
{
    private static IBaseShape Rider() => new RectangleBase(1.3779528f, 2.3622048f);
    private static IBaseShape Boss() => new CircleBase(0.62992126f);

    // The save's facing: about 20 degrees off the ranks' own normal, i.e. already at the edge of stacking.
    private static readonly Float2 SaveFacing = new(-0.33816084f, -0.94108826f);
    private const int HeroIndex = 10;
    private const float FastAdvance = 8f;

    private static (List<IBaseShape> shapes, List<Position> at) ReportedUnit()
    {
        var at = new List<Position>
        {
            new(59.85822f, 16.325695f), new(61.336174f, 16.325695f), new(62.81413f, 16.325695f),
            new(64.29208f, 16.325695f), new(65.77004f, 16.325695f), new(67.24799f, 16.325695f),
            new(60.65625f, 18.787899f), new(62.134212f, 18.787899f), new(63.612164f, 18.787899f),
            new(65.09012f, 18.787899f), new(66.50902f, 18.787899f),
        };
        var shapes = Enumerable.Range(0, 10).Select(_ => Rider()).Append(Boss()).ToList();
        return (shapes, at);
    }

    private static Float2 Rotate(Float2 f, float degrees)
    {
        float r = degrees * MathF.PI / 180f;
        return new Float2(f.X * MathF.Cos(r) - f.Y * MathF.Sin(r), f.X * MathF.Sin(r) + f.Y * MathF.Cos(r));
    }

    private static bool AnyOverlap(IReadOnlyList<IBaseShape> shapes, IReadOnlyList<Position> at, Float2 facing)
    {
        for (int i = 0; i < at.Count; i++)
            for (int j = i + 1; j < at.Count; j++)
                if (BaseShapeGeometry.AreColliding(shapes[i], at[i], facing, shapes[j], at[j], facing)) return true;
        return false;
    }

    [Test]
    public void TheReportedRank_TurnedRigidly_Stacks()
    {
        // The bug as it stood: the same centres, every base turned 90 degrees.
        var (shapes, at) = ReportedUnit();
        Assert.That(AnyOverlap(shapes, at, SaveFacing), Is.False, "the unit stands legally");
        Assert.That(AnyOverlap(shapes, at, Rotate(SaveFacing, 90f)), Is.True, "and a rigid 90-degree turn stacks it");
    }

    [TestCase(90f)]
    [TestCase(-90f)]
    [TestCase(45f)]
    [TestCase(180f)]
    public void TheReportedRank_TurnsInPlace_WithinAFastAdvance(float degrees)
    {
        var (shapes, at) = ReportedUnit();
        Float2 heading = Rotate(SaveFacing, degrees);
        var budgets = Enumerable.Repeat(FastAdvance, at.Count).ToList();

        var result = GroupReform.Solve(shapes, at, budgets, heading, target: null, HeroIndex);

        Assert.That(result, Is.Not.Null, "a legal re-form exists");
        Position[] slots = result!.Value.Slots;
        Assert.That(AnyOverlap(shapes, slots, heading), Is.False, "nothing stacks at the new heading");
        Assert.That(GroupReform.IsCoherent(shapes, slots, heading), Is.True, "1\" and 9\" rules hold");
        for (int i = 0; i < at.Count; i++)
            Assert.That(Position.GetDistance2D(at[i], slots[i]), Is.LessThanOrEqualTo(FastAdvance),
                $"model {i} stays inside its move");
    }

    [Test]
    public void TheReportedRank_TurnsNinetyAndRidesOff()
    {
        // The report's whole complaint in one step: face 90 degrees left and move off that way. The cursor
        // asks for 6", and the unit gets there - the re-form is part of the move, not paid for separately.
        var (shapes, at) = ReportedUnit();
        Float2 heading = Rotate(SaveFacing, 90f);
        Position c0 = GroupFormationUtilities.Centroid(at);
        var target = new Position(c0.x + 6f * heading.X, c0.z + 6f * heading.Y);
        var budgets = Enumerable.Repeat(FastAdvance, at.Count).ToList();

        var result = GroupReform.Solve(shapes, at, budgets, heading, target, HeroIndex);

        Assert.That(result, Is.Not.Null);
        var plan = GroupFormationUtilities.PlanGroupMove(result!.Value.Slots, at, budgets,
            c0, 1f, 0f, target.x - c0.x, target.z - c0.z);
        Assert.That(plan.WithinBudget, Is.True);
        Assert.That(plan.TranslationScale, Is.EqualTo(1f).Within(1e-4f), "the unit reaches the cursor");
        Assert.That(AnyOverlap(shapes, plan.NewPositions, heading), Is.False);
    }

    [Test]
    public void AJoinedHero_LeadsFromTheFrontRank()
    {
        var (shapes, at) = ReportedUnit();
        Float2 heading = Rotate(SaveFacing, 90f);
        var budgets = Enumerable.Repeat(FastAdvance, at.Count).ToList();

        Position[] slots = GroupReform.Solve(shapes, at, budgets, heading, null, HeroIndex)!.Value.Slots;

        // Depth along the heading: the hero is as far forward as any rider.
        float Depth(Position p) => p.x * heading.X + p.z * heading.Y;
        float front = slots.Max(Depth);
        Assert.That(Depth(slots[HeroIndex]), Is.EqualTo(front).Within(0.9f),
            "the boss's 32mm base sits in the front rank (its centre is shallower than a rider's)");
    }

    [Test]
    public void AShapeThatAlreadyClears_StaysExactlyWhereItIs()
    {
        // Round bases turn freely: the planner must not reshuffle a unit that needs nothing.
        var shapes = Enumerable.Range(0, 5).Select(_ => (IBaseShape)new CircleBase(0.5f)).ToList();
        var at = Enumerable.Range(0, 5).Select(i => new Position(i * 1.1f, 0f)).ToList();
        var budgets = Enumerable.Repeat(6f, 5).ToList();

        var result = GroupReform.Solve(shapes, at, budgets, new Float2(1f, 0f), null, -1);

        Assert.That(result, Is.Not.Null);
        Assert.That(result!.Value.Name, Is.EqualTo("current shape"));
        for (int i = 0; i < at.Count; i++)
            Assert.That(Position.GetDistance2D(at[i], result.Value.Slots[i]), Is.LessThan(1e-4f));
    }

    [Test]
    public void NoBudget_NoReform()
    {
        // Already spent its move: nothing can re-form, so the caller keeps refusing the stacked step.
        var (shapes, at) = ReportedUnit();
        var budgets = Enumerable.Repeat(0.2f, at.Count).ToList();

        Assert.That(GroupReform.Solve(shapes, at, budgets, Rotate(SaveFacing, 90f), null, HeroIndex), Is.Null);
    }

    [Test]
    public void Assignment_KeepsRelativeOrder_WhenNothingIsGainedBySwapping()
    {
        // Three models sliding one slot along: crossing paths would cost the same worst case, so the
        // tie-break on total travel must keep each model's neighbours.
        var cost = new float[3, 3];
        float[] from = { 0f, 1f, 2f }, to = { 0.5f, 1.5f, 2.5f };
        for (int m = 0; m < 3; m++)
            for (int k = 0; k < 3; k++)
                cost[m, k] = MathF.Abs(from[m] - to[k]);

        Assert.That(GroupReform.Assign(cost, heroIndex: -1, heroSlot: -1), Is.EqualTo(new[] { 0, 1, 2 }));
    }

    [Test]
    public void Assignment_MinimisesTheWorstSingleTravel()
    {
        // Total travel alone would send model 0 the long way (sum 1+4 = 5 vs 3+3 = 6); the worst case is
        // what holds the unit back, so the bottleneck wins.
        var cost = new float[,] { { 1f, 3f }, { 3f, 4f } };

        Assert.That(GroupReform.Assign(cost, -1, -1), Is.EqualTo(new[] { 1, 0 }));
    }
}
