using System.Numerics;
using System.Text.RegularExpressions;
using FdgRaylib.Rendering.MenuBackground;
using NUnit.Framework;

namespace FdgRaylib.Tests;

// #448 slice 2 — two random armies' units on the main menu's war table. The colours and the "never
// together" clash groups were chosen by the owner; the hand-off between the design-time pairing script
// (which proves every ALLOWED pair is distinguishable) and the C# table that actually picks pairs is the
// fragile part, so it is pinned here, along with the pairing rules and the unit placement (no base on
// terrain, off the table, or overlapping another).
[TestFixture]
public class MainMenuArmiesTests
{
    [Test]
    public void ArmyTable_MatchesThePairingScript()
    {
        string? script = ScriptPath();
        if (script is null) Assert.Ignore("pairing script not found next to the test binary's checkout");
        string text = File.ReadAllText(script!);

        var scriptArmies = Regex.Matches(text, @"""([A-Z-]+)"":\s*\(""([^""]+)"",\s*""(#[0-9A-F]{6})""\)")
            .Select(m => (Abbr: m.Groups[1].Value, Name: m.Groups[2].Value, Hex: m.Groups[3].Value))
            .ToList();
        var csArmies = MenuArmies.All.Select(a => (a.Abbr, a.Name, Hex: Hex(a.Rgb))).ToList();
        Assert.That(csArmies, Is.EquivalentTo(scriptArmies), "ARMIES in army_pairings_check.py vs MenuArmies.All");

        var scriptGroups = Regex.Matches(text, @"\(""([^""]+)"",\s*\[([^\]]+)\]\)")
            .Select(m => m.Groups[1].Value + ": " + string.Join(",",
                Regex.Matches(m.Groups[2].Value, @"""([A-Z-]+)""").Select(x => x.Groups[1].Value)))
            .ToList();
        var csGroups = MenuArmies.ClashGroups.Select(g => g.Name + ": " + string.Join(",", g.Members)).ToList();
        Assert.That(csGroups, Is.EqualTo(scriptGroups), "CLASH_GROUPS in army_pairings_check.py vs MenuArmies.ClashGroups");
    }

    [Test]
    public void Armies_AreUniqueAndAscii()
    {
        Assert.That(MenuArmies.All.Select(a => a.Abbr), Is.Unique);
        foreach (MenuArmy a in MenuArmies.All)
            Assert.That(a.Abbr.All(c => c >= 0x20 && c < 0x7F), Is.True, a.Abbr);
        foreach (var (_, members) in MenuArmies.ClashGroups)
            foreach (string abbr in members)
                Assert.That(MenuArmies.All.Any(a => a.Abbr == abbr), Is.True, $"clash group names unknown army {abbr}");
    }

    [Test]
    public void AllowedPairs_NeverMirrorAndNeverShareAClashGroup()
    {
        Assert.That(MenuArmies.AllowedPairs, Is.Not.Empty);
        foreach (var (a, b) in MenuArmies.AllowedPairs)
        {
            Assert.That(a.Abbr, Is.Not.EqualTo(b.Abbr));
            Assert.That(MenuArmies.ClashGroups.Any(g => g.Members.Contains(a.Abbr) && g.Members.Contains(b.Abbr)),
                Is.False, $"{a.Abbr} vs {b.Abbr} share a clash group");
        }
        // Spot-check the owner's examples and the two the pairing script caught.
        MenuArmy Get(string abbr) => MenuArmies.All.Single(x => x.Abbr == abbr);
        Assert.That(MenuArmies.CanFace(Get("ORK"), Get("GOB")), Is.False);
        Assert.That(MenuArmies.CanFace(Get("D-ELF"), Get("ROBOT")), Is.False);
        Assert.That(MenuArmies.CanFace(Get("DAO"), Get("TITAN")), Is.False);
        Assert.That(MenuArmies.CanFace(Get("ORK"), Get("B-SIST")), Is.True);
        Assert.That(MenuArmies.CanFace(Get("ORK"), Get("ORK")), Is.False, "no mirror matches");
    }

    [Test]
    public void EveryArmy_CanAppear_AgainstPlentyOfOpponents()
    {
        foreach (MenuArmy a in MenuArmies.All)
        {
            int opponents = MenuArmies.AllowedPairs.Count(p => p.A == a || p.B == a);
            Assert.That(opponents, Is.GreaterThanOrEqualTo(12), a.Abbr);
        }
    }

    [Test]
    public void PickPair_OnlyDealsAllowedPairs_OnBothSides()
    {
        var rng = new Random(448);
        var seenNear = new HashSet<string>();
        for (int i = 0; i < 4000; i++)
        {
            var (near, far) = MenuArmies.PickPair(rng);
            Assert.That(MenuArmies.CanFace(near, far), Is.True, $"{near.Abbr} vs {far.Abbr}");
            seenNear.Add(near.Abbr);
        }
        Assert.That(seenNear, Has.Count.EqualTo(MenuArmies.All.Count), "every army turns up, on the near side too");
    }

    [Test]
    public void Skirmish_LabelsEachSide_ArmyThenNumber()
    {
        MenuArmy ork = MenuArmies.All.Single(a => a.Abbr == "ORK");
        MenuArmy sisters = MenuArmies.All.Single(a => a.Abbr == "B-SIST");
        var skirmish = new HoloSkirmish(sisters, ork);

        Assert.That(skirmish.Units.Where(u => u.Side == 0).Select(u => u.Label),
            Is.EqualTo(new[] { "B-SIST 1", "B-SIST 2", "B-SIST 3", "B-SIST 4" }));
        Assert.That(skirmish.Units.Where(u => u.Side == 1).Select(u => u.Label),
            Is.EqualTo(new[] { "ORK 1", "ORK 2", "ORK 3", "ORK 4" }));
        Assert.That(skirmish.Units.Single(u => u.Label == "B-SIST 2").Tag, Is.EqualTo("B-SIST 2 x10"));
        Assert.That(skirmish.Units.Single(u => u.Label == "ORK 1").Tag, Is.EqualTo("ORK 1 x6"), "two casualties");
        Assert.That(skirmish.Units.Single(u => u.Label == "ORK 4").Tag, Is.EqualTo("ORK 4"), "a lone vehicle has no count");
    }

    [Test]
    public void Skirmish_HasSquadsAndVehiclesOnBothSides()
    {
        var skirmish = new HoloSkirmish(MenuArmies.All[0], MenuArmies.All[1]);
        foreach (int side in new[] { 0, 1 })
        {
            var units = skirmish.Units.Where(u => u.Side == side).ToList();
            Assert.That(units.Count(u => u.IsVehicle), Is.EqualTo(1), $"side {side} vehicle");
            Assert.That(units.Select(u => u.Models.Count).Distinct().Count(), Is.GreaterThanOrEqualTo(3),
                $"side {side} group sizes vary");
        }
    }

    [Test]
    public void Skirmish_BasesStayOnTheTable_OffTerrain_AndApart()
    {
        var scene = HoloTableScene.BuildDefault();
        var skirmish = new HoloSkirmish(MenuArmies.All[0], MenuArmies.All[1]);
        var all = skirmish.Units.SelectMany(u => u.Models.Select(m => (Unit: u, At: m))).ToList();

        foreach (var (unit, at) in all)
        {
            Assert.That(MathF.Abs(at.X) + unit.BaseRadius, Is.LessThan(HoloTableScene.HalfW), unit.Label);
            Assert.That(MathF.Abs(at.Y) + unit.BaseRadius, Is.LessThan(HoloTableScene.HalfD), unit.Label);
            foreach (HoloFootprint fp in scene.Footprints)
                Assert.That(DistanceToPolygon(at, fp.Polygon), Is.GreaterThan(unit.BaseRadius + 0.3f),
                    $"{unit.Label} base at {at} is on terrain piece {fp.Piece}");
        }

        for (int i = 0; i < all.Count; i++)
        for (int j = i + 1; j < all.Count; j++)
            Assert.That(Vector2.Distance(all[i].At, all[j].At), Is.GreaterThan(all[i].Unit.BaseRadius + all[j].Unit.BaseRadius),
                $"{all[i].Unit.Label} and {all[j].Unit.Label} bases overlap");
    }

    [Test]
    public void Skirmish_TrailsEndAtTheirUnit()
    {
        var skirmish = new HoloSkirmish(MenuArmies.All[0], MenuArmies.All[1]);
        foreach (HoloUnit u in skirmish.Units.Where(u => u.Trail.Count > 0))
            Assert.That(u.Trail[^1], Is.EqualTo(u.Centre), u.Label);
        Assert.That(skirmish.Units.Count(u => u.Trail.Count > 0), Is.GreaterThan(0));
    }

    private static string Hex(Vector3 rgb) =>
        $"#{(int)MathF.Round(rgb.X * 255):X2}{(int)MathF.Round(rgb.Y * 255):X2}{(int)MathF.Round(rgb.Z * 255):X2}";

    /// <summary>Negative inside the polygon, else the distance to its nearest edge.</summary>
    private static float DistanceToPolygon(Vector2 p, IReadOnlyList<Vector2> poly)
    {
        bool inside = false;
        float best = float.MaxValue;
        for (int i = 0, j = poly.Count - 1; i < poly.Count; j = i++)
        {
            Vector2 a = poly[j], b = poly[i];
            if ((b.Y > p.Y) != (a.Y > p.Y) && p.X < (a.X - b.X) * (p.Y - b.Y) / (a.Y - b.Y) + b.X) inside = !inside;
            Vector2 ab = b - a;
            float t = Math.Clamp(Vector2.Dot(p - a, ab) / ab.LengthSquared(), 0f, 1f);
            best = MathF.Min(best, Vector2.Distance(p, a + ab * t));
        }
        return inside ? -best : best;
    }

    private static string? ScriptPath()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null)
        {
            string candidate = Path.Combine(dir.FullName, "docs", "menu-background", "army_pairings_check.py");
            if (File.Exists(candidate)) return candidate;
            dir = dir.Parent;
        }
        return null;
    }
}
