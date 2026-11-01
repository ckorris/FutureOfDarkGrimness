using System.Numerics;
using FdgRaylib.Rendering;
using FdgRaylib.Rendering.MenuBackground;
using NUnit.Framework;

namespace FdgRaylib.Tests;

// #448 slice 4 — the war table moves: the camera drifts and glides in on the action once per battle,
// advancing units walk in along their trails, and each pairing fades in, fights and dissolves. The
// motion itself is checked by eye; what is pinned is the schedule (no jumps, things in the right order -
// a measuring line drawn to where a vehicle has not arrived yet would read as a bug) and the camera
// still framing the whole table at the far ends of its drift.
[TestFixture]
public class MainMenuMotionTests
{
    [Test]
    public void Battle_FadesIn_PlaysAtFullStrength_ThenFadesOut()
    {
        Assert.That(HoloDirector.Presence(0f), Is.EqualTo(0f));
        Assert.That(HoloDirector.Presence(HoloDirector.FadeIn), Is.EqualTo(1f));
        Assert.That(HoloDirector.Presence(HoloDirector.BattleLength / 2f), Is.EqualTo(1f));
        Assert.That(HoloDirector.Presence(HoloDirector.BattleLength), Is.EqualTo(0f));
    }

    [Test]
    public void Schedule_RunsInOrder()
    {
        Assert.That(HoloDirector.FightsFrom, Is.GreaterThanOrEqualTo(HoloDirector.FadeIn), "no shooting while still materialising");
        Assert.That(HoloDirector.FocusIn, Is.GreaterThan(HoloDirector.StillMoment), "the close-up comes after the action is set up");
        Assert.That(HoloDirector.FocusBack, Is.LessThanOrEqualTo(HoloDirector.BattleLength - HoloDirector.FadeOut),
            "the camera is back on the overview before the battle dissolves");
    }

    [Test]
    public void TimelineCurves_NeverJump()
    {
        // A glide or a fade that jumps between two frames reads as a glitch. At 60fps a frame is ~0.017s;
        // no curve may move more than a smooth ramp's worst-case step in that time.
        const float dt = 1f / 60f;
        for (float t = 0f; t < HoloDirector.BattleLength; t += dt)
        {
            Assert.That(MathF.Abs(HoloDirector.FocusWeight(t + dt) - HoloDirector.FocusWeight(t)), Is.LessThan(0.02f), $"focus at {t}");
            foreach (HoloUnit u in new[] { Mover(EHoloRole.Advances, 5f), Mover(EHoloRole.Patrols, HoloDirector.BattleLength) })
                Assert.That(MathF.Abs(u.ProgressAt(t + dt) - u.ProgressAt(t)), Is.LessThan(0.01f), $"{u.Role} at {t}");
            Assert.That(HoloDirector.FocusWeight(t), Is.InRange(0f, 1f));
        }
        Assert.That(HoloDirector.FocusWeight(0f), Is.EqualTo(0f));
        Assert.That(HoloDirector.FocusWeight((HoloDirector.FocusHeld + HoloDirector.FocusOut) / 2f), Is.EqualTo(1f));
        Assert.That(HoloDirector.FocusWeight(HoloDirector.BattleLength - 0.01f), Is.EqualTo(0f));
    }

    [TestCase(1280, 720)]
    [TestCase(1920, 1080)]
    [TestCase(2560, 1080)]
    [TestCase(1280, 1024)]
    public void Overview_FramesTheWholeTable_AtEveryDriftExtreme(int w, int h)
    {
        var layout = MainMenuLayout.Compute(w, h);
        ScreenRect r = layout.TableRegion;
        foreach (float yaw in new[] { HoloDirector.YawRange.Min, HoloDirector.YawRange.Max })
        foreach (float pitch in new[] { HoloDirector.PitchRange.Min, HoloDirector.PitchRange.Max })
        {
            HoloCamera cam = HoloCamera.FitToRegion(new Vector3(0f, 1f, 0f), HoloTableScene.SlabCorners,
                r.X, r.Y, r.W, r.H, h, pitch, yaw);
            foreach (Vector3 c in HoloTableScene.SlabCorners)
            {
                Vector2 p = cam.Project(c);
                Assert.That(p.X, Is.InRange(r.X - 0.5f, r.Right + 0.5f), $"yaw {yaw} pitch {pitch}");
                Assert.That(p.Y, Is.InRange(r.Y - 0.5f, r.Bottom + 0.5f), $"yaw {yaw} pitch {pitch}");
            }
        }
    }

    [Test]
    public void CameraBlend_StartsAndEndsOnItsTwoFits()
    {
        var a = HoloCamera.FitToRegion(new Vector3(0f, 1f, 0f), HoloTableScene.SlabCorners, 500, 50, 1300, 950, 1080);
        var box = new[] { new Vector3(-10, -8, 0), new Vector3(10, -8, 0), new Vector3(-10, 8, 6), new Vector3(10, 8, 6) };
        var b = HoloCamera.FitToRegion(new Vector3(0f, 0f, 0f), box, 500, 50, 1300, 950, 1080);
        var probe = new Vector3(5f, 3f, 0f);

        Assert.That(Vector2.Distance(HoloCamera.Blend(a, b, 0f).Project(probe), a.Project(probe)), Is.LessThan(1e-3f));
        Assert.That(Vector2.Distance(HoloCamera.Blend(a, b, 1f).Project(probe), b.Project(probe)), Is.LessThan(1e-3f));
        Assert.That(Vector3.Distance(a.Eye, b.Eye), Is.GreaterThan(1f), "the close-up really is closer");
    }

    [Test]
    public void AdvancingUnits_WalkTheirTrail_AndStopOnTheirSpot()
    {
        var skirmish = new HoloSkirmish(MenuArmies.All[0], MenuArmies.All[1]);
        HoloUnit walker = skirmish.Units.First(u => u.Trail.Count >= 2);
        HoloUnit stayer = skirmish.Units.First(u => u.Trail.Count == 0);

        Assert.That(walker.Centre + walker.OffsetAt(0f), Is.EqualTo(walker.Trail[0]), "starts at the trail's first point");
        Assert.That(walker.OffsetAt(1f), Is.EqualTo(Vector2.Zero), "ends on its spot");
        Assert.That(walker.OffsetAt(1.5f), Is.EqualTo(Vector2.Zero), "and stays there");
        Assert.That(stayer.OffsetAt(0f), Is.EqualTo(Vector2.Zero), "a unit with no trail never moves");

        // Halfway by time is halfway by distance along the polyline.
        float total = 0f;
        for (int i = 0; i + 1 < walker.Trail.Count; i++) total += Vector2.Distance(walker.Trail[i], walker.Trail[i + 1]);
        Vector2 mid = walker.Centre + walker.OffsetAt(0.5f);
        float walked = 0f, onPath = float.MaxValue;
        for (int i = 0; i + 1 < walker.Trail.Count; i++)
        {
            Vector2 a = walker.Trail[i], b = walker.Trail[i + 1];
            float t = Math.Clamp(Vector2.Dot(mid - a, b - a) / (b - a).LengthSquared(), 0f, 1f);
            float d = Vector2.Distance(mid, a + (b - a) * t);
            if (d < onPath) { onPath = d; walked = WalkedTo(walker.Trail, i, t); }
        }
        Assert.That(onPath, Is.LessThan(1e-3f), "on the trail");
        Assert.That(walked, Is.EqualTo(total / 2f).Within(1e-3f));
    }

    [Test]
    public void Shots_StartOnlyOnceBothUnitsStandStill()
    {
        // A unit that advances moves first, then shoots; nobody fires at a unit still on the move - a
        // tracer from or to where a unit WILL be reads as a bug.
        var scene = HoloTableScene.BuildDefault();
        foreach (var (a, b) in MenuArmies.AllowedPairs.Take(20))
        {
            var plan = HoloEngagement.Plan(new HoloSkirmish(a, b), scene.Footprints);
            Assert.That(plan.Shots, Is.Not.Empty);
            foreach (HoloShot shot in plan.Shots)
            {
                Assert.That(shot.StartsAt, Is.GreaterThanOrEqualTo(shot.Shooter.ArriveAt), "a unit moves first, then shoots");
                Assert.That(shot.StartsAt, Is.GreaterThanOrEqualTo(shot.Target.ArriveAt), "and is only shot at once it stands still");
                Assert.That(shot.StartsAt, Is.GreaterThanOrEqualTo(HoloDirector.FightsFrom));
            }
        }
    }

    [Test]
    public void EachRole_MovesAsDescribed()
    {
        // Owner, 2026-10-30: "make things that move keep moving the whole time. Something can also move
        // and then switch to shooting."
        HoloUnit holds = Mover(EHoloRole.Holds, 0f), advances = Mover(EHoloRole.Advances, 6f), patrols = Mover(EHoloRole.Patrols, HoloDirector.BattleLength);

        Assert.That(holds.ProgressAt(0f), Is.EqualTo(1f), "a holder is in place from the start");
        Assert.That(advances.ProgressAt(0f), Is.EqualTo(0f));
        Assert.That(advances.ProgressAt(3f), Is.InRange(0.01f, 0.99f), "an advancer is on its way");
        Assert.That(advances.ProgressAt(6f), Is.EqualTo(1f), "and stops when it arrives");
        Assert.That(advances.ProgressAt(20f), Is.EqualTo(1f));

        float last = -1f;
        for (float t = 0f; t < HoloDirector.BattleLength; t += 0.5f)
        {
            float p = patrols.ProgressAt(t);
            Assert.That(p, Is.GreaterThan(last), $"a patroller is still moving at {t}s");
            Assert.That(p, Is.LessThan(1f), $"and has not finished its route at {t}s");
            last = p;
        }
    }

    /// <summary>A three-model unit with a trail and the given role (no position needed for timing).</summary>
    private static HoloUnit Mover(EHoloRole role, float arriveAt) =>
        new("M", 0, MenuArmies.All[0], new Vector2(0f, -10f), 0.5f, 0, new[] { new Vector2(0f, -10f) },
            new[] { new Vector2(0f, -20f), new Vector2(0f, -10f) }) { Role = role, ArriveAt = arriveAt };

    [Test]
    public void APatroller_IsPassedOver_EvenWhenItIsTheNearestTarget_ButAnAdvancerJoinsIn()
    {
        // A patroller never stops, so it never fights - even as the closest enemy. An advancer in the same
        // spot does fight, from the moment it arrives.
        MenuArmy near = MenuArmies.All[0], far = MenuArmies.All[1];
        HoloUnit Squad(string label, int side, MenuArmy army, Vector2 c, EHoloRole role, float arrive, params Vector2[] trail) =>
            new(label, side, army, c, 0.5f, 0, new[] { c + new Vector2(-1.2f, 0f), c, c + new Vector2(1.2f, 0f) },
                trail.Length == 0 ? Array.Empty<Vector2>() : trail.Append(c).ToArray()) { Role = role, ArriveAt = arrive };

        var patroller = Squad("P", 0, near, new Vector2(0f, -5f), EHoloRole.Patrols, HoloDirector.BattleLength, new Vector2(-8f, -5f));
        var other = Squad("N", 0, near, new Vector2(20f, -5f), EHoloRole.Holds, 0f);
        var target = Squad("F", 1, far, new Vector2(0f, 5f), EHoloRole.Holds, 0f);
        var plan = HoloEngagement.Plan(new[] { patroller, other, target }, Array.Empty<HoloFootprint>());
        Assert.That(plan.Shots, Is.Not.Empty, "the standing pair still fight");
        Assert.That(plan.Shots.Any(s => s.Shooter == patroller || s.Target == patroller), Is.False);
        Assert.That(plan.Measure is null || (plan.Measure.A != patroller && plan.Measure.B != patroller), Is.True, "nor is it measured to");

        var advancer = patroller with { Role = EHoloRole.Advances, ArriveAt = 7f };
        var plan2 = HoloEngagement.Plan(new[] { advancer, other, target }, Array.Empty<HoloFootprint>());
        Assert.That(plan2.Shots.Where(s => s.Shooter == advancer).Select(s => s.StartsAt).Distinct(), Is.EqualTo(new[] { 7f }),
            "the advancer opens fire as it arrives");
    }

    [Test]
    public void Clock_StandsStillWhilePaused_AndResumesWhereItWas()
    {
        var clock = new HoloClock();
        clock.Tick(0.05f, paused: false);
        clock.Tick(0.05f, paused: false);
        float before = clock.Battle;
        for (int i = 0; i < 100; i++) clock.Tick(0.05f, paused: true);

        Assert.That(clock.Battle, Is.EqualTo(before), "a dialog open over the menu freezes the battle");
        Assert.That(clock.Wall, Is.EqualTo(0.1f).Within(1e-5f), "and the drift");
        clock.Tick(0.05f, paused: false);
        Assert.That(clock.Battle, Is.EqualTo(before + 0.05f).Within(1e-5f), "it picks up from the same moment");
    }

    [Test]
    public void Clock_AHitchCannotFastForwardTheBattle()
    {
        var clock = new HoloClock();
        clock.Tick(5f, paused: false);   // a file dialog blocked the loop for five seconds
        Assert.That(clock.Wall, Is.EqualTo(HoloClock.MaxStep));
    }

    [Test]
    public void Clock_StartsANewBattle_OnceEachTimeOneRunsOut()
    {
        var clock = new HoloClock();
        int newBattles = 0;
        int frames = (int)(HoloDirector.BattleLength * 2.5f / 0.05f);
        for (int i = 0; i < frames; i++)
        {
            if (clock.Tick(0.05f, paused: false)) newBattles++;
            Assert.That(clock.Battle, Is.InRange(0f, HoloDirector.BattleLength));
        }
        Assert.That(newBattles, Is.EqualTo(2), "two full battles in two and a half battles' time");

        clock.RestartBattle();
        clock.Tick(0.05f, paused: false);
        Assert.That(clock.Battle, Is.EqualTo(0f), "a pairing dealt from outside starts its battle from the top");
    }

    [Test]
    public void StillMoment_ShowsTheWholeFight()
    {
        float t = HoloDirector.StillMoment;
        Assert.That(HoloDirector.Presence(t), Is.EqualTo(1f));
        Assert.That(HoloDirector.FocusWeight(t), Is.EqualTo(0f), "on the overview");
        for (int seed = 0; seed < 100; seed++)
        {
            var battle = HoloBattle.Generate(new Random(seed));
            Assert.That(battle.Engagement.Shots.All(s => s.StartsAt <= t), Is.True, $"seed {seed}: every volley has opened");
            Assert.That(battle.Engagement.Measure!.StartsAt + 1.2f, Is.LessThanOrEqualTo(t), $"seed {seed}: the measure is fully drawn");
        }
    }

    private static float WalkedTo(IReadOnlyList<Vector2> trail, int leg, float t)
    {
        float d = 0f;
        for (int i = 0; i < leg; i++) d += Vector2.Distance(trail[i], trail[i + 1]);
        return d + Vector2.Distance(trail[leg], trail[leg + 1]) * t;
    }
}
