using System.Numerics;
using FdgRaylib.Rendering.MenuBackground;
using NUnit.Framework;

namespace FdgRaylib.Tests;

// #448 slice 3 — the fight on the main menu's war table. The owner's one hard rule: shooting must never
// go through terrain (and the measuring line likewise). The check is the simple local one chosen for
// this cosmetic scene - a 2D segment kept a pad clear of every footprint - so both the check itself and
// the plan built on it are pinned here, for every army pairing (the layout is fixed, but the pairing
// must not be able to change what is chosen into something illegal).
[TestFixture]
public class MainMenuEngagementTests
{
    private static readonly HoloTableScene Scene = HoloTableScene.BuildDefault();

    private static IEnumerable<HoloSkirmish> EveryPairing() =>
        MenuArmies.AllowedPairs.SelectMany(p => new[] { new HoloSkirmish(p.A, p.B), new HoloSkirmish(p.B, p.A) });

    [Test]
    public void IsClear_RejectsLinesThroughOrGrazingTerrain_AndAcceptsOpenGround()
    {
        var box = new[] { new HoloFootprint(0, new Vector2[] { new(-1, -1), new(1, -1), new(1, 1), new(-1, 1) }) };

        Assert.That(HoloEngagement.IsClear(new(-5, 0), new(5, 0), box), Is.False, "straight through");
        Assert.That(HoloEngagement.IsClear(new(-5, 1.2f), new(5, 1.2f), box), Is.False, "grazing within the pad");
        Assert.That(HoloEngagement.IsClear(new(0, 0), new(5, 5), box), Is.False, "starting inside");
        Assert.That(HoloEngagement.IsClear(new(-5, 2), new(5, 2), box), Is.True, "a full inch clear");
        Assert.That(HoloEngagement.IsClear(new(3, -5), new(3, 5), box), Is.True, "passing beside");
    }

    [Test]
    public void EveryShotAndMeasure_IsClearOfTerrain_ForEveryPairing()
    {
        foreach (HoloSkirmish skirmish in EveryPairing())
        {
            var plan = HoloEngagement.Plan(skirmish, Scene.Footprints);
            foreach (HoloShot shot in plan.Shots)
                Assert.That(HoloEngagement.IsClear(shot.From, shot.To, Scene.Footprints), Is.True,
                    $"{shot.Shooter.Label} -> {shot.Target.Label} crosses terrain");
            if (plan.Measure is { } m)
                Assert.That(HoloEngagement.IsClear(m.From, m.To, Scene.Footprints), Is.True, "measure crosses terrain");
        }
    }

    [Test]
    public void Plan_PicksOtherLines_WhenTerrainBlocksTheObviousOnes()
    {
        // On an open table the planner takes the nearest fights and the vehicle-to-vehicle measure. Drop a
        // short wall across the middle of every one of those lines and plan again: whatever it picks now
        // must clear the walls. (On the real layout the nearest choices happen to be clear already, so
        // only a blocked table proves the planner looks at terrain at all.)
        var skirmish = new HoloSkirmish(MenuArmies.All[0], MenuArmies.All[1]);
        var open = HoloEngagement.Plan(skirmish, Array.Empty<HoloFootprint>());
        var lines = open.Shots.Select(s => (s.From, s.To)).ToList();
        if (open.Measure is { } om) lines.Add((om.From, om.To));
        Assert.That(lines, Has.Count.GreaterThan(1));

        var walls = lines.Select((l, i) => Wall(i, l.From, l.To)).ToList();
        var plan = HoloEngagement.Plan(skirmish, walls);

        foreach (HoloShot shot in plan.Shots)
            Assert.That(HoloEngagement.IsClear(shot.From, shot.To, walls), Is.True,
                $"{shot.Shooter.Label} -> {shot.Target.Label} shoots through a wall");
        if (plan.Measure is { } m)
            Assert.That(HoloEngagement.IsClear(m.From, m.To, walls), Is.True, "the measure runs through a wall");
    }

    /// <summary>A 1" x 0.2" wall across the middle of the segment a-b.</summary>
    private static HoloFootprint Wall(int piece, Vector2 a, Vector2 b)
    {
        Vector2 mid = (a + b) * 0.5f, along = Vector2.Normalize(b - a) * 0.1f, across = new Vector2(-along.Y, along.X) * 5f;
        return new HoloFootprint(piece, new[] { mid - across - along, mid + across - along, mid + across + along, mid - across + along });
    }

    [Test]
    public void Plan_HasAFight_AndAMeasure()
    {
        var plan = HoloEngagement.Plan(new HoloSkirmish(MenuArmies.All[0], MenuArmies.All[1]), Scene.Footprints);

        Assert.That(plan.Shots, Is.Not.Empty, "there is shooting");
        Assert.That(plan.Shots.Select(s => s.Shooter).Distinct().Count(), Is.InRange(1, 3), "at most three fights");
        Assert.That(plan.Shots.All(s => s.Shooter.Side != s.Target.Side), Is.True, "no friendly fire");
        Assert.That(plan.Shots.Select(s => s.Shooter.Side).Distinct().Count(), Is.EqualTo(2), "both sides shoot");
        Assert.That(plan.Shots.All(s => !s.Shooter.IsVehicle && !s.Target.IsVehicle), Is.True,
            "the vehicles are kept for the measuring line");

        Assert.That(plan.Fights, Is.EquivalentTo(plan.Shots.Select(s => (s.Shooter, s.Target)).Distinct()),
            "Fights lists each shooter/target pair once");

        Assert.That(plan.Measure, Is.Not.Null);
        Assert.That(plan.Measure!.A.IsVehicle && plan.Measure.B.IsVehicle, Is.True, "vehicle to vehicle");
    }

    [Test]
    public void Measure_ReadsBaseToBase_NotCentreToCentre()
    {
        var plan = HoloEngagement.Plan(new HoloSkirmish(MenuArmies.All[0], MenuArmies.All[1]), Scene.Footprints);
        HoloMeasure m = plan.Measure!;
        Vector2 a = m.A.Alive.Single(), b = m.B.Alive.Single();

        Assert.That(m.Inches, Is.EqualTo(Vector2.Distance(a, b) - m.A.BaseRadius - m.B.BaseRadius).Within(1e-4f));
        Assert.That(Vector2.Distance(m.From, a), Is.EqualTo(m.A.BaseRadius).Within(1e-4f), "starts on A's base edge");
        Assert.That(Vector2.Distance(m.To, b), Is.EqualTo(m.B.BaseRadius).Within(1e-4f), "ends on B's base edge");
        Assert.That(m.Inches, Is.EqualTo(Vector2.Distance(m.From, m.To)).Within(1e-4f));
    }

    [Test]
    public void Shots_LeaveAndLandOnLivingModels()
    {
        var plan = HoloEngagement.Plan(new HoloSkirmish(MenuArmies.All[0], MenuArmies.All[1]), Scene.Footprints);
        foreach (HoloShot shot in plan.Shots)
        {
            Assert.That(shot.Shooter.Alive, Has.Member(shot.From), "a casualty does not shoot");
            Assert.That(shot.Target.Alive, Has.Member(shot.To), "nor is it shot at");
            Assert.That(Vector2.Distance(shot.Shooter.Centre, shot.Target.Centre), Is.LessThanOrEqualTo(HoloEngagement.MaxShotRange));
        }
    }

    [Test]
    public void CameraCloseUp_AlwaysFramesAFight()
    {
        // Owner, 2026-10-30: "when the camera zooms, make it always zoom somewhere where there's fighting".
        var plan = HoloEngagement.Plan(new HoloSkirmish(MenuArmies.All[0], MenuArmies.All[1]), Scene.Footprints);
        Assert.That(plan.Clashes, Is.Not.Empty);
        foreach (var (a, b) in plan.Clashes)
            Assert.That(plan.Fights.Any(f => (f.Shooter == a && f.Target == b) || (f.Shooter == b && f.Target == a)), Is.True);
        for (int battle = 0; battle < 6; battle++)
        {
            var (centre, half) = plan.FocusBox(battle)!.Value;
            var (shooter, target) = plan.Clashes[battle % plan.Clashes.Count];
            foreach (Vector2 m in shooter.Models.Concat(target.Models))
            {
                Assert.That(MathF.Abs(m.X - centre.X), Is.LessThanOrEqualTo(half.X), $"battle {battle}: {shooter.Label}/{target.Label}");
                Assert.That(MathF.Abs(m.Y - centre.Y), Is.LessThanOrEqualTo(half.Y), $"battle {battle}: {shooter.Label}/{target.Label}");
            }
            Assert.That(half.X, Is.LessThan(HoloTableScene.HalfW), "a close-up, not the whole table");
        }
        Assert.That(Enumerable.Range(0, plan.Clashes.Count).Select(i => plan.FocusBox(i)!.Value.Centre).Distinct().Count(),
            Is.EqualTo(plan.Clashes.Count), "successive battles look at different fights (a pair firing both ways is one)");
        Assert.That(plan.Clashes.Count, Is.LessThan(plan.Fights.Count), "this layout has a pair trading fire both ways");
    }

    [Test]
    public void CameraCloseUp_HasNothingToFrame_WhenNobodyFights()
    {
        var plan = HoloEngagement.Plan(Array.Empty<HoloUnit>(), Array.Empty<HoloFootprint>());
        Assert.That(plan.FocusBox(0), Is.Null);
    }
}
