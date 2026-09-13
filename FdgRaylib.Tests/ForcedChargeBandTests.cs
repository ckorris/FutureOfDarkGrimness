using FDG;
using FdgRaylib.Rendering;
using FdgRaylib.Rendering.Resolvers;
using NUnit.Framework;

namespace FdgRaylib.Tests;

// #334 — the 1" forced-charge standoff band, app side. The RULE is the engine's (ForcedChargeUtilities,
// shared with the ChooseActionStage gate this previews); what is pinned here is the two things the app adds:
// the outline geometry the player reads the boundary off, and the wording of the panel warning.
//
// The outline is a rules boundary, so it has to be EXACT, not a ring that roughly hugs the base. The check
// below is the strongest available: every point the renderer emits is fed back through the base shape's own
// DistanceToLocalPoint and must come out at the band distance.
[TestFixture]
public class ForcedChargeBandTests
{
    private const float Band = GameWideConstants.ENEMY_STANDOFF_DISTANCE_INCHES;
    private static readonly Float2 Forward = new(0f, 1f);

    // Generated with the base facing forward, so its local frame is the table frame and the shape's own
    // local-space distance function can measure the points directly.
    private static void AssertEveryPointIsAtBandDistance(IBaseShape shape, float band)
    {
        Float2[] outline = ModelBaseRenderer.BandOutline(shape, Forward, band);

        Assert.That(outline, Is.Not.Empty);
        foreach (Float2 p in outline)
        {
            Assert.That(shape.DistanceToLocalPoint(p.X, p.Y), Is.EqualTo(band).Within(0.02f),
                $"point ({p.X:F3}, {p.Y:F3}) is not {band}\" from the base surface");
        }
    }

    [Test]
    public void BandOutline_CircleBase_RingSitsOneInchOffTheBase()
    {
        AssertEveryPointIsAtBandDistance(new CircleBase(0.5f), Band);
    }

    [Test]
    public void BandOutline_RectangleBase_CornersAreRoundedNotSquared()
    {
        // The whole reason this does not reuse DrawOutlineImGui's inflateInches: pushing a rectangle's
        // half-extents out by 1" puts its corners 1.41" from the base, claiming ground the rule leaves legal.
        // The Minkowski outline rounds them, so every point is at 1.00".
        AssertEveryPointIsAtBandDistance(new RectangleBase(1f, 2f), Band);
    }

    [Test]
    public void BandOutline_RotatedRectangle_TracksTheFacing()
    {
        // Turned 90 degrees, the long axis runs along table X. A point on the band directly ahead in +Z is
        // then only half the WIDTH plus the band away, not half the height.
        var shape = new RectangleBase(1f, 2f);
        Float2[] outline = ModelBaseRenderer.BandOutline(shape, new Float2(1f, 0f), Band);

        float maxZ = 0f, maxX = 0f;
        foreach (Float2 p in outline)
        {
            if (p.Y > maxZ) maxZ = p.Y;
            if (p.X > maxX) maxX = p.X;
        }

        Assert.That(maxZ, Is.EqualTo(0.5f + Band).Within(0.02f), "half-width + band across the short axis");
        Assert.That(maxX, Is.EqualTo(1.0f + Band).Within(0.02f), "half-length + band along the long axis");
    }

    [Test]
    public void BandOutline_IsAClosedLoop()
    {
        // The renderer strokes it with ImDrawFlags.Closed, so the last point must not double back on the
        // first - a duplicated seam point draws a visible nick in the ring.
        Float2[] outline = ModelBaseRenderer.BandOutline(new RectangleBase(1f, 2f), Forward, Band);

        Assert.That(outline.Length, Is.GreaterThan(8));
        float dx = outline[0].X - outline[^1].X, dz = outline[0].Y - outline[^1].Y;
        Assert.That(MathF.Sqrt(dx * dx + dz * dz), Is.GreaterThan(0.0001f));
    }

    [Test]
    public void ForcedChargeWarning_OneModel_ReadsSingular()
    {
        string text = GuiDefineMovementResolver.ForcedChargeWarning(1, new[] { "Ork Boyz" }, hasMeleeWeapons: true);

        Assert.That(text, Does.Contain("1 model ends"));
        Assert.That(text, Does.Contain("Ork Boyz"));
        Assert.That(text, Does.Contain("cannot Pass"));
        Assert.That(text, Does.Contain("must Charge"));
    }

    [Test]
    public void ForcedChargeWarning_SeveralModelsAndUnits_NamesThemAll()
    {
        string text = GuiDefineMovementResolver.ForcedChargeWarning(3, new[] { "Ork Boyz", "Grot Mob" },
            hasMeleeWeapons: true);

        Assert.That(text, Does.Contain("3 models end"));
        Assert.That(text, Does.Contain("Ork Boyz, Grot Mob"));
    }

    [Test]
    public void ForcedChargeWarning_UnitWithNoMeleeWeapon_DoesNotPromiseACharge()
    {
        // Pass is gated by proximity alone, but Charge needs a melee weapon - so a rifle-only unit inside the
        // band can do neither and lands on the engine's zero-options fallback. Saying "it must Charge" there
        // would be untrue, and the true version is the more useful warning: this is a trap, not a choice.
        string text = GuiDefineMovementResolver.ForcedChargeWarning(1, new[] { "Ork Boyz" },
            hasMeleeWeapons: false);

        Assert.That(text, Does.Contain("cannot Pass"));
        Assert.That(text, Does.Contain("no melee weapon"));
        Assert.That(text, Does.Not.Contain("must Charge"));
    }

    [Test]
    public void ForcedChargeWarning_IsAsciiOnly()
    {
        // The ImGui font atlas bakes Basic Latin + Latin-1 only; anything above U+00FF renders as '?'.
        foreach (bool melee in new[] { true, false })
        {
            string text = GuiDefineMovementResolver.ForcedChargeWarning(2, new[] { "Ork Boyz" }, melee);
            foreach (char c in text)
                Assert.That(c, Is.LessThanOrEqualTo((char)0xFF), $"non-Latin-1 character '{c}' in a game string");
        }
    }
}
