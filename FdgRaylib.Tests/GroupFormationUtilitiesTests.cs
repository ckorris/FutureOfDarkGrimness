using FDG;
using FdgRaylib.Rendering.Resolvers;
using NUnit.Framework;

namespace FdgRaylib.Tests;

[TestFixture]
public class GroupFormationUtilitiesTests
{
    private const float Tol = 1e-3f;

    private static void AssertPos(Position actual, float x, float z, string msg = "")
    {
        Assert.That(actual.x, Is.EqualTo(x).Within(Tol), $"{msg} x");
        Assert.That(actual.z, Is.EqualTo(z).Within(Tol), $"{msg} z");
    }

    private static float Dist(Position a, Position b) => Position.GetDistance2D(a, b);

    [Test]
    public void Centroid_AveragesPoints()
    {
        var c = GroupFormationUtilities.Centroid(new[] { new Position(0f, 0f), new Position(4f, 2f) });
        AssertPos(c, 2f, 1f);
    }

    [Test]
    public void PureTranslation_ClampsToBudget_AndKeepsShape()
    {
        // Two models 2" apart, each with 3" of budget, mouse pulled far along +x.
        var last = new[] { new Position(0f, 0f), new Position(2f, 0f) };
        var budgets = new[] { 3f, 3f };
        var pivot = GroupFormationUtilities.Centroid(last);

        // theta = 0 (cos=1, sin=0); desired translation toward x=100.
        var result = GroupFormationUtilities.PlanGroupMove(last, budgets, pivot, 1f, 0f, 100f - pivot.x, 0f);

        Assert.That(result.WithinBudget, Is.True);
        // Each model should advance exactly its 3" budget, no more.
        AssertPos(result.NewPositions[0], 3f, 0f, "model0");
        AssertPos(result.NewPositions[1], 5f, 0f, "model1");
        // Shape (inter-model spacing) preserved.
        Assert.That(Dist(result.NewPositions[0], result.NewPositions[1]), Is.EqualTo(2f).Within(Tol));
    }

    [Test]
    public void Translation_TightestBudgetGovernsWholeGroup()
    {
        // model0 can only move 1", model1 can move 5" — the whole rigid group moves 1".
        var last = new[] { new Position(0f, 0f), new Position(2f, 0f) };
        var budgets = new[] { 1f, 5f };
        var pivot = GroupFormationUtilities.Centroid(last);

        var result = GroupFormationUtilities.PlanGroupMove(last, budgets, pivot, 1f, 0f, 100f, 0f);

        Assert.That(result.WithinBudget, Is.True);
        Assert.That(Dist(last[0], result.NewPositions[0]), Is.EqualTo(1f).Within(Tol));
        Assert.That(Dist(last[1], result.NewPositions[1]), Is.EqualTo(1f).Within(Tol));
    }

    [Test]
    public void RotationAloneOverBudget_IsFlaggedNotWithinBudget()
    {
        // Wide formation, 90° rotation makes the outer models travel ~7", but budget is only 1".
        var last = new[] { new Position(-5f, 0f), new Position(5f, 0f) };
        var budgets = new[] { 1f, 1f };
        var pivot = GroupFormationUtilities.Centroid(last); // (0,0)

        // 90°: cos=0, sin=1. No translation requested.
        var result = GroupFormationUtilities.PlanGroupMove(last, budgets, pivot, 0f, 1f, 0f, 0f);

        Assert.That(result.WithinBudget, Is.False);
    }

    [Test]
    public void RotationWithinBudget_PreservesShape()
    {
        // 90° rotation with generous budget — spacing must be identical after the turn.
        var last = new[] { new Position(0f, 0f), new Position(2f, 0f) };
        var budgets = new[] { 100f, 100f };
        var pivot = GroupFormationUtilities.Centroid(last);

        var result = GroupFormationUtilities.PlanGroupMove(last, budgets, pivot, 0f, 1f, 0f, 0f);

        Assert.That(result.WithinBudget, Is.True);
        Assert.That(Dist(result.NewPositions[0], result.NewPositions[1]), Is.EqualTo(2f).Within(Tol));
    }

    [Test]
    public void Deployment_SmallUnit_StaysSingleRow()
    {
        var radii = new[] { 0.5f, 0.5f, 0.5f };
        var offsets = GroupFormationUtilities.ComputeDeploymentOffsets(radii, 0.1f, 9f, 1f);

        foreach (var o in offsets) Assert.That(o.dz, Is.EqualTo(0f).Within(Tol), "single row z");
        // Centred on the centroid.
        Assert.That(offsets.Sum(o => o.dx) / 3f, Is.EqualTo(0f).Within(Tol), "centroid x");
    }

    [Test]
    public void Deployment_WideUnit_WrapsToTwoBalancedRows()
    {
        var radii = Enumerable.Repeat(0.5f, 12).ToArray(); // ~12" span > 9" → wraps
        var offsets = GroupFormationUtilities.ComputeDeploymentOffsets(radii, 0.1f, 9f, 1f);

        var zs = offsets.Select(o => o.dz).Distinct().OrderBy(z => z).ToList();
        Assert.That(zs.Count, Is.EqualTo(2), "exactly two rows");
        Assert.That(offsets.Count(o => o.dz > 0f), Is.EqualTo(6), "front row count");
        Assert.That(offsets.Count(o => o.dz < 0f), Is.EqualTo(6), "back row count");
        // Centroid at origin.
        Assert.That(offsets.Sum(o => o.dx) / offsets.Length, Is.EqualTo(0f).Within(Tol), "centroid x");
        Assert.That(offsets.Sum(o => o.dz) / offsets.Length, Is.EqualTo(0f).Within(Tol), "centroid z");
    }

    [Test]
    public void Deployment_OddUnit_LongerRowIsForward()
    {
        var radii = Enumerable.Repeat(0.5f, 13).ToArray();
        // forwardZSign = +1 → the longer (7-model) row should sit on the +z (forward) side.
        var offsets = GroupFormationUtilities.ComputeDeploymentOffsets(radii, 0.1f, 9f, 1f);

        float frontZ = offsets.Max(o => o.dz);
        int frontCount = offsets.Count(o => o.dz > offsets.Min(o2 => o2.dz) + Tol);
        Assert.That(frontCount, Is.EqualTo(7), "forward row is the longer one");
        Assert.That(frontZ, Is.GreaterThan(0f), "forward row is toward +z");
    }

    [Test]
    public void Deployment_ForwardSignFlipsRowSide()
    {
        var radii = Enumerable.Repeat(0.5f, 13).ToArray();
        var back = GroupFormationUtilities.ComputeDeploymentOffsets(radii, 0.1f, 9f, -1f);
        // With forwardZSign = -1, the longer 7-model row sits on the -z side.
        Assert.That(back.Count(o => o.dz < 0f), Is.EqualTo(7), "longer row toward -z when forward is -z");
    }

    [Test]
    public void NoMovementRequested_LeavesPositionsUnchanged()
    {
        var last = new[] { new Position(1f, 1f), new Position(3f, 1f) };
        var budgets = new[] { 5f, 5f };
        var pivot = GroupFormationUtilities.Centroid(last);

        // No rotation, mouse exactly at centroid (zero translation).
        var result = GroupFormationUtilities.PlanGroupMove(last, budgets, pivot, 1f, 0f, 0f, 0f);

        Assert.That(result.WithinBudget, Is.True);
        AssertPos(result.NewPositions[0], 1f, 1f, "model0");
        AssertPos(result.NewPositions[1], 3f, 1f, "model1");
    }
}
