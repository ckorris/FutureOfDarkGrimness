using System.Numerics;
using FdgRaylib.Rendering.MenuBackground;
using NUnit.Framework;

namespace FdgRaylib.Tests;

// #448, owner feedback 2026-10-30: "randomize the unit positions, shooting, movements, and terrain at each
// battle". Every rule the fixed layout satisfied by hand-placement now has to hold for whatever the
// generator deals, so this sweeps a few hundred seeds and checks each battle: terrain on the table and
// apart, bases on the table, off terrain and apart, walk-in routes clear of terrain, every shot and the
// measure clear of terrain (the owner's one hard rule), and a battle worth watching. It also pins that
// the battles really vary and that the fixed-layout fallback is (practically) never needed.
[TestFixture]
public class MainMenuRandomBattleTests
{
    private const int Seeds = 300;

    // Generated once per run and shared: a few hundred battles is a couple of seconds of work.
    private static readonly Lazy<List<(int Seed, HoloBattle Battle)>> Generated =
        new(() => Enumerable.Range(0, Seeds).Select(seed => (seed, HoloBattle.Generate(new Random(seed)))).ToList());

    private static IEnumerable<(int Seed, HoloBattle Battle)> Battles() => Generated.Value;

    [Test]
    public void Generator_AlmostNeverFallsBackToTheFixedLayout()
    {
        int fallbacks = Battles().Count(b => b.Battle.IsFallback);
        Assert.That(fallbacks, Is.LessThanOrEqualTo(Seeds / 100), $"{fallbacks} of {Seeds} battles fell back");
    }

    [Test]
    public void Terrain_StaysOnTheTable_AndApart()
    {
        foreach (var (seed, b) in Battles().Where(x => !x.Battle.IsFallback))
        {
            var pieces = b.Scene.Obstacles.ToList();
            Assert.That(pieces.Select(p => p.Piece).Distinct().Count(), Is.GreaterThanOrEqualTo(8), $"seed {seed}: a sparse table");
            foreach (HoloFootprint fp in pieces)
            foreach (Vector2 v in fp.Polygon)
                Assert.That(MathF.Abs(v.X) < HoloTableScene.HalfW - 1f && MathF.Abs(v.Y) < HoloTableScene.HalfD - 1f, Is.True,
                    $"seed {seed}: piece {fp.Piece} is not an inch inside the table");
            for (int i = 0; i < pieces.Count; i++)
            for (int j = i + 1; j < pieces.Count; j++)
            {
                if (pieces[i].Piece == pieces[j].Piece) continue;
                Assert.That(HoloGeometry.PolygonPolygon(pieces[i].Polygon, pieces[j].Polygon),
                    Is.GreaterThanOrEqualTo(HoloTableScene.PieceGap - 0.01f), $"seed {seed}: pieces {pieces[i].Piece}/{pieces[j].Piece} crowd");
            }
        }
    }

    [Test]
    public void Bases_StayOnTheTable_OffTerrain_AndApart()
    {
        foreach (var (seed, b) in Battles())
        {
            var obstacles = b.Scene.Obstacles.ToList();
            var all = b.Skirmish.Units.SelectMany(u => u.Models.Select(m => (Unit: u, At: m))).ToList();
            foreach (var (u, at) in all)
            {
                Assert.That(MathF.Abs(at.X) + u.BaseRadius < HoloTableScene.HalfW, Is.True, $"seed {seed}: {u.Label} off the table");
                Assert.That(MathF.Abs(at.Y) + u.BaseRadius < HoloTableScene.HalfD, Is.True, $"seed {seed}: {u.Label} off the table");
                foreach (HoloFootprint o in obstacles)
                    Assert.That(HoloGeometry.PointPolygon(at, o.Polygon), Is.GreaterThan(u.BaseRadius),
                        $"seed {seed}: {u.Label} stands on piece {o.Piece}");
            }
            for (int i = 0; i < all.Count; i++)
            for (int j = i + 1; j < all.Count; j++)
                Assert.That(Vector2.Distance(all[i].At, all[j].At), Is.GreaterThan(all[i].Unit.BaseRadius + all[j].Unit.BaseRadius),
                    $"seed {seed}: {all[i].Unit.Label} and {all[j].Unit.Label} overlap");
        }
    }

    [Test]
    public void SidesKeepToTheirOwnHalves()
    {
        foreach (var (seed, b) in Battles().Where(x => !x.Battle.IsFallback))
        foreach (HoloUnit u in b.Skirmish.Units)
            Assert.That(u.Side == 0 ? u.Centre.Y < 0f : u.Centre.Y > 0f, Is.True, $"seed {seed}: {u.Label} on the wrong half");
    }

    [Test]
    public void WalkInRoutes_KeepClearOfTerrain_ForEveryModel()
    {
        foreach (var (seed, b) in Battles())
        {
            var obstacles = b.Scene.Obstacles.ToList();
            foreach (HoloUnit u in b.Skirmish.Units.Where(u => u.Trail.Count >= 2))
            foreach (Vector2 m in u.Models)
            {
                Vector2 o = m - u.Centre;
                for (int i = 0; i + 1 < u.Trail.Count; i++)
                    Assert.That(HoloGeometry.IsClear(u.Trail[i] + o, u.Trail[i + 1] + o, obstacles, u.BaseRadius), Is.True,
                        $"seed {seed}: {u.Label} walks through terrain");
            }
        }
    }

    [Test]
    public void EveryShotAndMeasure_IsClearOfTerrain_AndEveryBattleIsWorthWatching()
    {
        foreach (var (seed, b) in Battles())
        {
            foreach (HoloShot shot in b.Engagement.Shots)
                Assert.That(HoloEngagement.IsClear(shot.From, shot.To, b.Scene.Footprints), Is.True,
                    $"seed {seed}: {shot.Shooter.Label} -> {shot.Target.Label} shoots through terrain");
            if (b.Engagement.Measure is { } m)
                Assert.That(HoloEngagement.IsClear(m.From, m.To, b.Scene.Footprints), Is.True, $"seed {seed}: measure crosses terrain");
            Assert.That(HoloBattle.IsWorthWatching(b.Engagement), Is.True, $"seed {seed}: a dull battle");
            Assert.That(b.Engagement.FocusBox(seed), Is.Not.Null, $"seed {seed}: nothing for the camera to frame");
        }
    }

    [Test]
    public void Battles_ReallyVary_AndASeedRepeatsExactly()
    {
        var battles = Battles().Select(x => x.Battle).Take(20).ToList();
        Assert.That(battles.Select(b => string.Join(";", b.Scene.PieceCentres.Select(c => $"{c.X:0.0},{c.Y:0.0}"))).Distinct().Count(),
            Is.EqualTo(20), "terrain differs battle to battle");
        Assert.That(battles.Select(b => string.Join(";", b.Skirmish.Units.Select(u => $"{u.Centre.X:0.0},{u.Centre.Y:0.0}"))).Distinct().Count(),
            Is.EqualTo(20), "positions differ battle to battle");
        Assert.That(battles.Select(b => $"{b.Skirmish.Near.Abbr}/{b.Skirmish.Far.Abbr}").Distinct().Count(), Is.GreaterThan(10),
            "and so do the armies");

        HoloBattle again = HoloBattle.Generate(new Random(7)), first = HoloBattle.Generate(new Random(7));
        Assert.That(again.Scene.PieceCentres, Is.EqualTo(first.Scene.PieceCentres));
        Assert.That(again.Skirmish.Units.Select(u => u.Centre), Is.EqualTo(first.Skirmish.Units.Select(u => u.Centre)));
    }

    [Test]
    public void EverySide_HoldsAdvancesAndPatrols()
    {
        foreach (var (seed, b) in Battles().Where(x => !x.Battle.IsFallback))
        foreach (int side in new[] { 0, 1 })
        {
            var roles = b.Skirmish.Units.Where(u => u.Side == side).Select(u => u.Role).ToList();
            Assert.That(roles, Does.Contain(EHoloRole.Holds).And.Contain(EHoloRole.Advances).And.Contain(EHoloRole.Patrols),
                $"seed {seed} side {side}");
        }
    }

    [Test]
    public void MovingUnits_NeverPassThroughAnything()
    {
        // Every moving model's path keeps a base's width plus a gap from every other unit's standing bases
        // and from every other moving model's path (timing ignored, so paths never even cross).
        foreach (var (seed, b) in Battles().Where(x => !x.Battle.IsFallback))
        {
            var units = b.Skirmish.Units;
            IEnumerable<(Vector2 A, Vector2 B)> PathsOf(HoloUnit u) => u.Trail.Count < 2
                ? Enumerable.Empty<(Vector2, Vector2)>()
                : u.Models.SelectMany(m => Enumerable.Range(0, u.Trail.Count - 1)
                    .Select(i => (u.Trail[i] + (m - u.Centre), u.Trail[i + 1] + (m - u.Centre))));
            foreach (HoloUnit mover in units.Where(u => u.Trail.Count >= 2))
            foreach (HoloUnit other in units.Where(u => u != mover))
            {
                float clear = mover.BaseRadius + other.BaseRadius;
                foreach (var (a, c) in PathsOf(mover))
                {
                    if (other.Role != EHoloRole.Patrols)
                        foreach (Vector2 m in other.Models)
                            Assert.That(HoloGeometry.PointSegment(m, a, c), Is.GreaterThan(clear), $"seed {seed}: {mover.Label} walks through {other.Label}");
                    foreach (var (p, q) in PathsOf(other))
                        Assert.That(HoloGeometry.SegmentDistance(a, c, p, q), Is.GreaterThan(clear), $"seed {seed}: {mover.Label} crosses {other.Label}'s path");
                }
            }
        }
    }

    [Test]
    public void TitanLords_FieldOnlyBigSingleModels()
    {
        // Owner, 2026-10-30: "when it's TITAN, it should ONLY have big single-model units".
        MenuArmy titan = MenuArmies.All.Single(a => a.Abbr == HoloSkirmish.BigModelsOnlyArmy);
        var foes = MenuArmies.All.Where(a => MenuArmies.CanFace(titan, a)).ToList();
        Assert.That(foes, Is.Not.Empty);
        int seen = 0;
        foreach (var (seed, b) in Battles())
        foreach (HoloUnit u in b.Skirmish.Units.Where(u => u.Army == titan))
        {
            seen++;
            Assert.That(u.Models, Has.Count.EqualTo(1), $"seed {seed}: {u.Label}");
            Assert.That(u.BaseRadius, Is.GreaterThanOrEqualTo(1.7f), $"seed {seed}: {u.Label} is not big");
        }
        Assert.That(seen, Is.GreaterThan(0), "TITAN turned up in the sweep");

        foreach (bool titanNear in new[] { true, false })
        {
            var fixedLayout = titanNear ? new HoloSkirmish(titan, foes[0]) : new HoloSkirmish(foes[0], titan);
            Assert.That(fixedLayout.Units.Where(u => u.Army == titan).All(u => u.IsVehicle && u.BaseRadius >= 1.7f), Is.True);
            Assert.That(fixedLayout.Units.Where(u => u.Army != titan).Any(u => !u.IsVehicle), Is.True, "only the Titan side changes");
        }
    }

    [Test]
    public void FixedLayout_IsUnchanged_ForTheFallback()
    {
        // BuildDefault and the slot positions are the signed-off mockup; the random generator must not
        // have disturbed them (the fallback and the older tests depend on them).
        var scene = HoloTableScene.BuildDefault();
        Assert.That(scene.PieceCentres, Has.Count.EqualTo(13));
        Assert.That(scene.PieceCentres[0], Is.EqualTo(new Vector2(-24, 2)));
        Assert.That(scene.AreaFootprints, Has.Count.EqualTo(1), "the crater");
        var skirmish = new HoloSkirmish(MenuArmies.All[0], MenuArmies.All[1]);
        Assert.That(skirmish.Units[1].Centre, Is.EqualTo(new Vector2(-9, -9)));
    }
}
