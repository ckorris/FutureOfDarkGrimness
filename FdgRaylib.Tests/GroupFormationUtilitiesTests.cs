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

    // ---- #277: deployment layout now rides the engine FormationLibrary via FormationCycle. The old
    // ComputeDeploymentOffsets defaults are pinned as "the cycle's first entry reproduces them": a line
    // when it fits the 9" span, else the first legal (balanced-rows) partition. The forward-sign mirror
    // moved into GuiPlaceObjectsResolver (front row 0 lays toward +z here; the resolver flips dz when
    // the zone's forward direction is -z).

    private static (float dx, float dz)[] DefaultDeployOffsets(int n, float radius)
    {
        var radii = Enumerable.Repeat(radius, n).ToList();
        var cycle = FormationCycle.Build(radii, radii, radii, includeCurrentShape: false);
        Assert.That(cycle.Count, Is.GreaterThan(0), "catalog never empty for a deployable unit");
        var unplaced = Enumerable.Repeat(new Position(0f, 0f), n).ToList();
        return FormationLibrary.PlanFormationOffsets(unplaced, radii, radii, cycle.Selected.RowCounts, 0.1f);
    }

    [Test]
    public void Deployment_SmallUnit_DefaultsToSingleRow()
    {
        var offsets = DefaultDeployOffsets(3, 0.5f);

        foreach (var o in offsets) Assert.That(o.dz, Is.EqualTo(0f).Within(Tol), "single row z");
        Assert.That(offsets.Sum(o => o.dx) / 3f, Is.EqualTo(0f).Within(Tol), "centroid x");
    }

    [Test]
    public void Deployment_WideUnit_DefaultsToTwoBalancedRows()
    {
        var offsets = DefaultDeployOffsets(12, 0.5f); // line would span ~12" > 9" → filtered out

        var zs = offsets.Select(o => o.dz).Distinct().OrderBy(z => z).ToList();
        Assert.That(zs.Count, Is.EqualTo(2), "exactly two rows");
        Assert.That(offsets.Count(o => o.dz > 0f), Is.EqualTo(6), "front row count");
        Assert.That(offsets.Count(o => o.dz < 0f), Is.EqualTo(6), "back row count");
        Assert.That(offsets.Sum(o => o.dx) / offsets.Length, Is.EqualTo(0f).Within(Tol), "centroid x");
        Assert.That(offsets.Sum(o => o.dz) / offsets.Length, Is.EqualTo(0f).Within(Tol), "centroid z");
    }

    [Test]
    public void Deployment_OddUnit_LongerRowIsForward()
    {
        var offsets = DefaultDeployOffsets(13, 0.5f);

        float frontZ = offsets.Max(o => o.dz);
        int frontCount = offsets.Count(o => o.dz > offsets.Min(o2 => o2.dz) + Tol);
        Assert.That(frontCount, Is.EqualTo(7), "forward row is the longer one");
        Assert.That(frontZ, Is.GreaterThan(0f), "forward row is toward +z");
    }

    [Test]
    public void FormationCycle_Reposition_LeadsWithCurrentShape_AndWraps()
    {
        var radii = Enumerable.Repeat(0.5f, 6).ToList();
        var cycle = FormationCycle.Build(radii, radii, radii, includeCurrentShape: true);

        Assert.That(cycle.IsCurrentShape, Is.True, "index 0 = current shape for a reposition");
        Assert.That(cycle.Label, Is.EqualTo("current"));

        cycle.Cycle(1);
        Assert.That(cycle.IsCurrentShape, Is.False);
        Assert.That(cycle.Label, Is.EqualTo("line (6)"), "first catalog entry is the line");

        cycle.Cycle(-1);
        Assert.That(cycle.IsCurrentShape, Is.True, "cycling back returns to current");
        cycle.Cycle(-1);
        Assert.That(cycle.IsCurrentShape, Is.False, "cycling wraps past the ends");

        cycle.Reset();
        Assert.That(cycle.IsCurrentShape, Is.True, "Reset returns to current shape");
    }

    // ---- #094: coherency repair (contract toward centroid) ----

    private const float MaxNearest = 1f;   // GameWideConstants.MAX_MODEL_DISTANCE_FROM_ANY_OTHER_MODEL_INCHES
    private const float MaxFarthest = 9f;   // GameWideConstants.MAX_MODEL_DISTANCE_FROM_ALL_OTHER_MODELS_INCHES

    /// <summary>(worst nearest-neighbour b2b across models, farthest-pair b2b) — mirrors the resolver's
    /// CheckCohesion. A formation is coherent when worstNearest &lt;= 1" and farthest &lt;= 9".</summary>
    private static (float worstNearest, float farthest, float minB2B) CohesionMetrics(Position[] pos, float[] radii)
    {
        float worstNearest = 0f, farthest = 0f, minB2B = float.PositiveInfinity;
        for (int i = 0; i < pos.Length; i++)
        {
            float nearest = float.PositiveInfinity;
            for (int j = 0; j < pos.Length; j++)
            {
                if (i == j) continue;
                float b2b = Dist(pos[i], pos[j]) - radii[i] - radii[j];
                if (b2b < nearest) nearest = b2b;
                if (b2b < minB2B) minB2B = b2b;
                if (i < j && b2b > farthest) farthest = b2b;
            }
            if (nearest > worstNearest) worstNearest = nearest;
        }
        return (worstNearest, farthest, minB2B);
    }

    [Test]
    public void RepairCoherency_AlreadyCoherent_ReturnsUnchanged()
    {
        // Two bases touching (b2b = 0) — already coherent, must not move.
        var pos = new[] { new Position(0f, 0f), new Position(1f, 0f) };
        var radii = new[] { 0.5f, 0.5f };

        var repaired = GroupFormationUtilities.RepairCoherencyByContraction(pos, radii, MaxNearest, MaxFarthest);

        AssertPos(repaired[0], 0f, 0f, "model0");
        AssertPos(repaired[1], 1f, 0f, "model1");
    }

    [Test]
    public void RepairCoherency_PullsStragglerIntoCohesion()
    {
        // Two models together at the left, a straggler 11" away (nearest b2b 10" and farthest 11" — both
        // rules broken). After repair every model must be coherent and no bases may overlap.
        var pos = new[] { new Position(0f, 0f), new Position(1f, 0f), new Position(12f, 0f) };
        var radii = new[] { 0.5f, 0.5f, 0.5f };

        var before = CohesionMetrics(pos, radii);
        Assert.That(before.worstNearest, Is.GreaterThan(MaxNearest), "precondition: straggler out of cohesion");

        var repaired = GroupFormationUtilities.RepairCoherencyByContraction(pos, radii, MaxNearest, MaxFarthest);
        var after = CohesionMetrics(repaired, radii);

        Assert.That(after.worstNearest, Is.LessThanOrEqualTo(MaxNearest + Tol), "every model within 1\" of a neighbour");
        Assert.That(after.farthest, Is.LessThanOrEqualTo(MaxFarthest + Tol), "all pairs within 9\"");
        Assert.That(after.minB2B, Is.GreaterThanOrEqualTo(-Tol), "no overlap introduced");
    }

    [Test]
    public void RepairCoherency_SharesBurden_GradedAndCentroidPreserved()
    {
        // A coherent chain (0,1,2) plus a straggler 4" out at x=6 (only the 1" rule is broken). If only the
        // straggler moved it would travel ~2.02" (to b2b 0.98 of x=2). Relaxation shares that: the straggler
        // moves less, the nearest body model moves toward it, and the motion grades down the chain.
        var pos = new[] { new Position(0f, 0f), new Position(1f, 0f), new Position(2f, 0f), new Position(6f, 0f) };
        var radii = new[] { 0.5f, 0.5f, 0.5f, 0.5f };
        const float soloTravel = 2.02f; // 6 - (2 + 0.98 + 1) if the straggler alone closed the gap

        var c0 = GroupFormationUtilities.Centroid(pos);
        var repaired = GroupFormationUtilities.RepairCoherencyByContraction(pos, radii, MaxNearest, MaxFarthest);

        var after = CohesionMetrics(repaired, radii);
        Assert.That(after.worstNearest, Is.LessThanOrEqualTo(MaxNearest + Tol), "result coherent");
        Assert.That(after.minB2B, Is.GreaterThanOrEqualTo(-Tol), "no overlap");

        float dStraggler = Dist(repaired[3], pos[3]);
        float dNearBody  = Dist(repaired[2], pos[2]);
        float dFarBody   = Dist(repaired[0], pos[0]);

        // Burden shared: straggler moves less than it would alone, and the nearest body model pitches in.
        Assert.That(dStraggler, Is.LessThan(soloTravel), "straggler carries less than the whole gap");
        Assert.That(dNearBody, Is.GreaterThan(0.1f), "nearest body model shares the move");
        // Graded: nearest body model moves more than the far end; straggler still moves the most.
        Assert.That(dNearBody, Is.GreaterThan(dFarBody), "motion grades down the chain");
        Assert.That(dStraggler, Is.GreaterThanOrEqualTo(dNearBody - Tol), "straggler is the most-displaced");

        // Equal-and-opposite pulls (and separation) conserve the centroid exactly.
        var c1 = GroupFormationUtilities.Centroid(repaired);
        AssertPos(c1, c0.x, c0.z, "centroid conserved");
    }

    [Test]
    public void RepairCoherency_FixesTooSpreadUnit_NearestOk()
    {
        // A chain spaced ~3" apart: each neighbour is fine on the 1" rule (3-1=2"? no — b2b 2" > 1").
        // Use ends 10" apart so the 9" rule is the violation while neighbours are within 1".
        var pos = new[] { new Position(0f, 0f), new Position(1.4f, 0f), new Position(2.8f, 0f),
                          new Position(4.2f, 0f), new Position(10.4f, 0f) };
        var radii = Enumerable.Repeat(0.5f, 5).ToArray();

        var repaired = GroupFormationUtilities.RepairCoherencyByContraction(pos, radii, MaxNearest, MaxFarthest);
        var after = CohesionMetrics(repaired, radii);

        Assert.That(after.worstNearest, Is.LessThanOrEqualTo(MaxNearest + Tol));
        Assert.That(after.farthest, Is.LessThanOrEqualTo(MaxFarthest + Tol));
        Assert.That(after.minB2B, Is.GreaterThanOrEqualTo(-Tol));
    }

    [Test]
    public void PlanGroupMove_BudgetMeasuredFromOrigin_IncludesRepairTravel()
    {
        // Origin spaced 2" apart; base contracted 0.5" inward on each side (so each model has already
        // "travelled" 0.5" from its real start before any drag). Budget 0.6" each, big +x drag.
        var origin = new[] { new Position(0f, 0f), new Position(2f, 0f) };
        var baseP  = new[] { new Position(0.5f, 0f), new Position(1.5f, 0f) };
        var budgets = new[] { 0.6f, 0.6f };
        var pivot = GroupFormationUtilities.Centroid(baseP);

        var result = GroupFormationUtilities.PlanGroupMove(baseP, origin, budgets, pivot, 1f, 0f, 100f, 0f);

        Assert.That(result.WithinBudget, Is.True);
        // model0 already moved +0.5 by the repair, so it may only advance 0.1" more → travels exactly 0.6" total.
        Assert.That(Dist(origin[0], result.NewPositions[0]), Is.EqualTo(0.6f).Within(Tol), "model0 total travel == budget");
        // model1 was contracted toward model0 (−x), so the +x drag partly cancels it; still within budget.
        Assert.That(Dist(origin[1], result.NewPositions[1]), Is.LessThanOrEqualTo(0.6f + Tol), "model1 within budget");
    }

    [Test]
    public void PlanGroupMove_RepairTravelAloneOverBudget_FlaggedNotWithinBudget()
    {
        // Base sits 0.5" from origin (the repair displacement), but the budget is only 0.4" — even with no
        // rotation and no drag the move is distance-illegal.
        var origin = new[] { new Position(0f, 0f), new Position(2f, 0f) };
        var baseP  = new[] { new Position(0.5f, 0f), new Position(1.5f, 0f) };
        var budgets = new[] { 0.4f, 0.4f };
        var pivot = GroupFormationUtilities.Centroid(baseP);

        var result = GroupFormationUtilities.PlanGroupMove(baseP, origin, budgets, pivot, 1f, 0f, 0f, 0f);

        Assert.That(result.WithinBudget, Is.False);
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
