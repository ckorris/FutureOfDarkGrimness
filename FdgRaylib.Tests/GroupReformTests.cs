using System;
using System.Collections.Generic;
using System.Linq;
using FDG;
using FDG.Data;
using FDG.Stages;
using FDG.StageResolution.Requests;
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
    public void DraggingOut_TheShapeDoesNotFlicker()
    {
        // Owner's hand-test: dragging out along the new heading, the best shape alternated between 3-3-3-2
        // and 4-4-3 every inch or two - near ties, each correct and together unpleasant. Fed last frame's
        // shape back, as the resolver does, the sweep settles once and stays.
        var (shapes, at) = ReportedUnit();
        Float2 heading = Rotate(SaveFacing, 90f);
        Position c0 = GroupFormationUtilities.Centroid(at);
        var budgets = Enumerable.Repeat(FastAdvance, at.Count).ToList();

        string? shown = null;
        int changes = 0;
        for (float d = 0f; d <= 20f; d += 0.25f)
        {
            var target = new Position(c0.x + d * heading.X + 3f, c0.z + d * heading.Y);
            string? name = GroupReform.Solve(shapes, at, budgets, heading, target, HeroIndex, preferred: shown)?.Name;
            if (shown != null && name != shown) changes++;
            shown = name;
        }

        Assert.That(changes, Is.LessThanOrEqualTo(1));
    }

    [Test]
    public void MovingTheCursor_NeverReshufflesWhoStandsWhere()
    {
        // Slots are assigned for the turn alone, so two cursor positions on the same heading give the same
        // shape with the same models in the same places - only the whole block slides.
        var (shapes, at) = ReportedUnit();
        Float2 heading = Rotate(SaveFacing, 90f);
        Position c0 = GroupFormationUtilities.Centroid(at);
        var budgets = Enumerable.Repeat(FastAdvance, at.Count).ToList();
        var prepared = GroupReform.Prepare(shapes, at, heading, HeroIndex)!;

        var near = prepared.Solve(budgets, new Position(c0.x + 2f * heading.X, c0.z + 2f * heading.Y), "4-4-3");
        var far = prepared.Solve(budgets, new Position(c0.x + 15f * heading.X - 4f, c0.z + 15f * heading.Y), "4-4-3");

        Assert.That(near?.Name, Is.EqualTo("4-4-3"));
        Assert.That(far?.Name, Is.EqualTo("4-4-3"));
        Assert.That(far!.Value.Slots, Is.EqualTo(near!.Value.Slots));
    }

    [TestCase(0f)]
    [TestCase(37f)]
    [TestCase(90f)]
    [TestCase(-123f)]
    public void EveryFormation_HasTheStandardSpacing_AtAnyHeading(float degrees)
    {
        // Owner's hand-test: the spacing between riders jumped about as the mouse moved a hair, and again
        // on Ctrl+Wheel. Every shape - re-form or pick - now sits 0.1" edge to edge at the bases' true
        // extents, whatever the heading, so nothing but the arrangement ever changes.
        var (shapes, at) = ReportedUnit();
        Float2 heading = Rotate(SaveFacing, degrees);
        var prepared = GroupReform.Prepare(shapes, at, heading, HeroIndex)!;

        foreach (var formation in GroupReform.LegalPartitions(shapes))
        {
            Position[]? slots = prepared.SlotsFor(formation.Name);
            Assert.That(slots, Is.Not.Null, $"{formation.Name} is offered by Ctrl+Wheel, so it must lay out");
            for (int i = 0; i < slots!.Length; i++)
            {
                float nearest = float.MaxValue;
                for (int j = 0; j < slots.Length; j++)
                    if (j != i)
                        nearest = MathF.Min(nearest, BaseShapeGeometry.SurfaceGap2D(
                            shapes[i], slots[i], prepared.Heading, shapes[j], slots[j], prepared.Heading));
                Assert.That(nearest, Is.EqualTo(0.1f).Within(0.01f), $"{formation.Name}, model {i}");
            }
        }
    }

    [Test]
    public void Ctrl_Wheel_Catalog_UsesTrueExtents()
    {
        // Spaced by circumscribing circles, a 35x60mm rider took a 2.7" slot each way and the catalog
        // dropped the shapes it thought broke 9". At true extents the reported unit can form all of these.
        var (shapes, _) = ReportedUnit();

        var names = GroupReform.LegalPartitions(shapes).Select(f => f.Name).ToList();

        Assert.That(names, Is.SupersetOf(new[] { "6-5", "4-4-3", "3-3-3-2" }));
    }

    [TestCase(90f)]
    [TestCase(-90f)]
    [TestCase(45f)]
    public void TheReportedRank_WheelsAsOneBody_AndMovesOff_TheEngineAgrees(float degrees)
    {
        // The default group move (re-form off): the wheel turns centres AND bases together about the
        // centroid, then the unit moves straight off along its new front. Checked by the engine's own Done
        // validation, so it is the real "can the riders turn and go" answer, not the preview's.
        // (Against the reporter's whole table this also holds, except the -90 move-off, which rides into
        // difficult terrain - a genuine 6" cap, not this bug.)
        var (shapes, at) = ReportedUnit();
        GameDataStore store = GameDataStore.GameDataStoreBuilder.GetDefault();
        Position c0 = GroupFormationUtilities.Centroid(at);
        float r = degrees * MathF.PI / 180f, c = MathF.Cos(r), s = MathF.Sin(r);
        Float2 turned = new(SaveFacing.X * c - SaveFacing.Y * s, SaveFacing.X * s + SaveFacing.Y * c);
        Position Wheeled(Position p)
        {
            float dx = p.x - c0.x, dz = p.z - c0.z;
            return new Position(c0.x + dx * c - dz * s, c0.z + dx * s + dz * c);
        }
        float wheelWorst = at.Max(p => Position.GetDistance2D(p, Wheeled(p)));
        float off = FastAdvance - wheelWorst - 0.05f; // everything the end riders have left

        var moves = new List<ModelMoveEntry>();
        for (int i = 0; i < at.Count; i++)
        {
            var binding = store.GetDataBinding<ModelData>(store.Create(
                new ModelData(shapes[i], new List<Weapon>(), at[i], store)));
            binding.GetValue().FacingBinding.SetValue(SaveFacing);
            Position w = Wheeled(at[i]);
            moves.Add(new ModelMoveEntry(binding,
                new List<Position> { w, new(w.x + off * turned.X, w.z + off * turned.Y) },
                new List<Float2> { turned, turned }));
        }

        bool ok = MovementUtilities.ValidatePaths(moves, _ => new ModelMoveBudget(FastAdvance, FastAdvance),
            new List<EnemyModelFootprint>(), false, false, false, null, out var errors, lenientCoherency: true);

        Assert.That(off, Is.GreaterThan(0f), "the wheel leaves move to spare");
        Assert.That(ok, Is.True, string.Join(", ", errors.Select(e => e.ErrorReasonType)));
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
