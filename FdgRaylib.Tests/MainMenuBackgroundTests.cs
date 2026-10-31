using System.Numerics;
using FdgRaylib.Rendering;
using FdgRaylib.Rendering.MenuBackground;
using NUnit.Framework;

namespace FdgRaylib.Tests;

// #448 — the main menu sits on a holographic war table. The drawing is Raylib/ImGui (checked by eye and
// with `--menu-preview`); what is pinned here is the arithmetic underneath it, whose failure modes are
// invisible on the developer's own display: the table sliding under the button panel or off the edge on
// a 4:3 or ultrawide screen, a button escaping its panel at some scale, and terrain pieces overlapping or
// hanging off the table (which would also break the later line-of-fire choice that tests shots against
// their footprints).
[TestFixture]
public class MainMenuBackgroundTests
{
    private static readonly (int W, int H)[] Screens =
    {
        (1280, 720), (1280, 1024), (1366, 768), (1920, 1080), (1920, 1200), (2560, 1080), (2560, 1440), (3840, 2160),
    };

    private static HoloCamera FitFor(MainMenuLayout layout, int screenH)
    {
        ScreenRect r = layout.TableRegion;
        return HoloCamera.FitToRegion(new Vector3(0f, 1f, 0f), HoloTableScene.SlabCorners, r.X, r.Y, r.W, r.H, screenH);
    }

    [TestCaseSource(nameof(Screens))]
    public void WholeTable_LandsInsideTheRegionRightOfThePanel((int W, int H) screen)
    {
        var layout = MainMenuLayout.Compute(screen.W, screen.H);
        HoloCamera cam = FitFor(layout, screen.H);
        ScreenRect r = layout.TableRegion;

        Assert.That(r.X, Is.GreaterThan(layout.Panel.Right), "the table region starts right of the button panel");
        foreach (Vector3 corner in HoloTableScene.SlabCorners)
        {
            Assert.That(cam.Depth(corner), Is.GreaterThan(0f), "every corner is in front of the camera");
            Vector2 p = cam.Project(corner);
            Assert.That(p.X, Is.InRange(r.X - 0.5f, r.Right + 0.5f), $"corner {corner} x on {screen}");
            Assert.That(p.Y, Is.InRange(r.Y - 0.5f, r.Bottom + 0.5f), $"corner {corner} y on {screen}");
        }
    }

    [TestCaseSource(nameof(Screens))]
    public void Table_FillsTheRegion_OnItsTighterAxis((int W, int H) screen)
    {
        // The fit pulls the camera in until the slab touches the region on one axis - a regression that
        // left the table a postage stamp in the middle of the screen would still pass the test above.
        var layout = MainMenuLayout.Compute(screen.W, screen.H);
        HoloCamera cam = FitFor(layout, screen.H);
        var pts = HoloTableScene.SlabCorners.Select(cam.Project).ToList();
        float w = pts.Max(p => p.X) - pts.Min(p => p.X);
        float h = pts.Max(p => p.Y) - pts.Min(p => p.Y);
        ScreenRect r = layout.TableRegion;

        Assert.That(MathF.Max(w / r.W, h / r.H), Is.GreaterThan(0.99f));
    }

    [TestCaseSource(nameof(Screens))]
    public void Buttons_StayInsideThePanel_WithoutOverlapping((int W, int H) screen)
    {
        var layout = MainMenuLayout.Compute(screen.W, screen.H);
        var buttons = layout.Primary.Concat(layout.Secondary).Concat(layout.Tertiary).ToList();
        Assert.That(buttons, Has.Count.EqualTo(7), "Host/Solo, Join, Army Forge, Combat Calculator, Load Game, Options, Quit");

        ScreenRect panel = layout.Panel;
        foreach (ScreenRect b in buttons)
        {
            Assert.That(b.X, Is.GreaterThanOrEqualTo(panel.X));
            Assert.That(b.Right, Is.LessThanOrEqualTo(panel.Right));
            Assert.That(b.Y, Is.GreaterThanOrEqualTo(panel.Y));
            Assert.That(b.Bottom, Is.LessThanOrEqualTo(panel.Bottom));
        }
        for (int i = 0; i < buttons.Count; i++)
        for (int j = i + 1; j < buttons.Count; j++)
        {
            ScreenRect a = buttons[i], b = buttons[j];
            bool overlap = a.X < b.Right && b.X < a.Right && a.Y < b.Bottom && b.Y < a.Bottom;
            Assert.That(overlap, Is.False, $"buttons {i} and {j} overlap on {screen}");
        }

        Assert.That(layout.UnderlineY, Is.LessThan(panel.Y), "the title sits above the panel");
        Assert.That(panel.Bottom, Is.LessThan(screen.H), "the panel fits on screen");
    }

    [Test]
    public void ButtonTiers_ShrinkFromPrimaryToTertiary()
    {
        var layout = MainMenuLayout.Compute(1920, 1080);

        Assert.That(layout.Primary[0].H, Is.GreaterThan(layout.Secondary[0].H));
        Assert.That(layout.Secondary[0].H, Is.GreaterThan(layout.Tertiary[0].H));
        Assert.That(layout.PrimaryFontPx, Is.GreaterThan(layout.SecondaryFontPx));
        Assert.That(layout.SecondaryFontPx, Is.GreaterThan(layout.TertiaryFontPx));
        Assert.That(layout.Tertiary[0].Y, Is.EqualTo(layout.Tertiary[1].Y), "Options and Quit share a row");
    }

    [Test]
    public void TerrainFootprints_StayOnTheTable()
    {
        var scene = HoloTableScene.BuildDefault();
        Assert.That(scene.Footprints, Is.Not.Empty);
        foreach (HoloFootprint fp in scene.Footprints)
        foreach (Vector2 v in fp.Polygon)
        {
            Assert.That(MathF.Abs(v.X), Is.LessThan(HoloTableScene.HalfW), $"piece {fp.Piece} vertex {v}");
            Assert.That(MathF.Abs(v.Y), Is.LessThan(HoloTableScene.HalfD), $"piece {fp.Piece} vertex {v}");
        }
    }

    [Test]
    public void TerrainPieces_DoNotOverlapEachOther()
    {
        var scene = HoloTableScene.BuildDefault();
        var fps = scene.Footprints;
        for (int i = 0; i < fps.Count; i++)
        for (int j = i + 1; j < fps.Count; j++)
        {
            if (fps[i].Piece == fps[j].Piece) continue;   // a ruin's own legs meet at its corner
            Assert.That(PolygonsOverlap(fps[i].Polygon, fps[j].Polygon), Is.False,
                $"pieces {fps[i].Piece} and {fps[j].Piece} overlap");
        }
    }

    [Test]
    public void EveryPieceLine_StaysOverTheTable()
    {
        var scene = HoloTableScene.BuildDefault();
        foreach (HoloLine l in scene.Lines.Where(l => l.Piece >= 0))
        foreach (Vector3 p in new[] { l.A, l.B })
        {
            Assert.That(MathF.Abs(p.X), Is.LessThanOrEqualTo(HoloTableScene.HalfW), $"piece {l.Piece}");
            Assert.That(MathF.Abs(p.Y), Is.LessThanOrEqualTo(HoloTableScene.HalfD), $"piece {l.Piece}");
            Assert.That(p.Z, Is.GreaterThanOrEqualTo(0f), $"piece {l.Piece} goes below the table");
        }
    }

    [Test]
    public void TableLabels_AreAscii()
    {
        // CLAUDE.md: anything past U+00FF renders as '?' - the inch mark must be a plain double quote.
        foreach (HoloLabel label in HoloTableScene.BuildDefault().Labels)
            Assert.That(label.Text.All(c => c >= 0x20 && c < 0x7F), Is.True, label.Text);
    }

    private static bool PolygonsOverlap(IReadOnlyList<Vector2> a, IReadOnlyList<Vector2> b)
    {
        for (int i = 0; i < a.Count; i++)
        for (int j = 0; j < b.Count; j++)
            if (SegmentsCross(a[i], a[(i + 1) % a.Count], b[j], b[(j + 1) % b.Count])) return true;
        return Contains(a, b[0]) || Contains(b, a[0]);
    }

    private static bool SegmentsCross(Vector2 p, Vector2 q, Vector2 r, Vector2 s)
    {
        static float Cross(Vector2 o, Vector2 a, Vector2 b) => (a.X - o.X) * (b.Y - o.Y) - (a.Y - o.Y) * (b.X - o.X);
        float d1 = Cross(r, s, p), d2 = Cross(r, s, q), d3 = Cross(p, q, r), d4 = Cross(p, q, s);
        return (d1 > 0) != (d2 > 0) && (d3 > 0) != (d4 > 0);
    }

    private static bool Contains(IReadOnlyList<Vector2> poly, Vector2 pt)
    {
        bool inside = false;
        for (int i = 0, j = poly.Count - 1; i < poly.Count; j = i++)
            if ((poly[i].Y > pt.Y) != (poly[j].Y > pt.Y) &&
                pt.X < (poly[j].X - poly[i].X) * (pt.Y - poly[i].Y) / (poly[j].Y - poly[i].Y) + poly[i].X)
                inside = !inside;
        return inside;
    }
}
