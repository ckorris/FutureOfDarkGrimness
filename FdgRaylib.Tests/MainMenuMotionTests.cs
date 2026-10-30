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
        Assert.That(HoloDirector.Advance(HoloDirector.MeasureFrom), Is.EqualTo(1f), "vehicles have arrived before they are measured");
        Assert.That(HoloDirector.FocusIn, Is.GreaterThan(HoloDirector.MeasureFrom), "the close-up comes after the action is set up");
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
            Assert.That(MathF.Abs(HoloDirector.Advance(t + dt) - HoloDirector.Advance(t)), Is.LessThan(0.01f), $"advance at {t}");
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
    public void AdvancingUnits_DoNotShoot()
    {
        // They are still walking in when the shooting starts; a tracer from where a unit will be is wrong.
        var scene = HoloTableScene.BuildDefault();
        foreach (var (a, b) in MenuArmies.AllowedPairs.Take(20))
        {
            var plan = HoloEngagement.Plan(new HoloSkirmish(a, b), scene.Footprints);
            Assert.That(plan.Shots, Is.Not.Empty);
            Assert.That(plan.Shots.All(s => s.Shooter.Trail.Count == 0 && s.Target.Trail.Count == 0), Is.True);
        }
    }

    [Test]
    public void AWalkingUnit_IsPassedOver_EvenWhenItIsTheNearestTarget()
    {
        // The fixed menu layout never puts a walking unit within range of an enemy, so the rule is pinned
        // on a hand-built table: the walker is the closest enemy to the far squad, yet nobody fires at it
        // and it fires at nobody.
        MenuArmy near = MenuArmies.All[0], far = MenuArmies.All[1];
        HoloUnit Squad(string label, int side, MenuArmy army, Vector2 c, params Vector2[] trail) => new(label, side, army, c, 0.5f, 0,
            new[] { c + new Vector2(-1.2f, 0f), c, c + new Vector2(1.2f, 0f) }, trail.Length == 0 ? Array.Empty<Vector2>() : trail.Append(c).ToArray());

        var walker = Squad("W", 0, near, new Vector2(0f, -5f), new Vector2(0f, -15f));
        var other = Squad("N", 0, near, new Vector2(20f, -5f));
        var target = Squad("F", 1, far, new Vector2(0f, 5f));
        var plan = HoloEngagement.Plan(new[] { walker, other, target }, Array.Empty<HoloFootprint>());

        Assert.That(plan.Shots, Is.Not.Empty, "the stationary pair still fight");
        Assert.That(plan.Shots.Any(s => s.Shooter == walker || s.Target == walker), Is.False);
    }

    private static float WalkedTo(IReadOnlyList<Vector2> trail, int leg, float t)
    {
        float d = 0f;
        for (int i = 0; i < leg; i++) d += Vector2.Distance(trail[i], trail[i + 1]);
        return d + Vector2.Distance(trail[leg], trail[leg + 1]) * t;
    }
}
