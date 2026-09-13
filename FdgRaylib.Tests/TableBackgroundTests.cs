using System;
using FDG;
using FdgRaylib.Rendering;
using NUnit.Framework;

namespace FdgRaylib.Tests;

// #265 — the lobby's table-surface pick and the colours the renderer paints it with.
[TestFixture]
public class TableBackgroundTests
{
    [Test]
    public void EveryBackground_HasItsOwnStyle()
    {
        var surfaces = new System.Collections.Generic.HashSet<(byte, byte, byte)>();

        foreach (ETableBackground background in Enum.GetValues<ETableBackground>())
        {
            TableBackgroundStyle style = TableBackgrounds.For(background);
            Assert.That(surfaces.Add((style.Surface.R, style.Surface.G, style.Surface.B)), Is.True,
                $"{background} reuses another surface colour - the dropdown would look broken");
        }
    }

    [Test]
    public void Forest_KeepsTheOriginalBoard()
    {
        // The pre-#265 constants, so every existing save and the default lobby look untouched.
        TableBackgroundStyle forest = TableBackgrounds.For(ETableBackground.Forest);

        Assert.That((forest.Surface.R, forest.Surface.G, forest.Surface.B), Is.EqualTo(((byte)40, (byte)100, (byte)40)));
        Assert.That((forest.Border.R, forest.Border.G, forest.Border.B), Is.EqualTo(((byte)150, (byte)105, (byte)55)));
        Assert.That((forest.GridMinor.R, forest.GridMinor.G, forest.GridMinor.B, forest.GridMinor.A),
            Is.EqualTo(((byte)33, (byte)85, (byte)33, (byte)80)));
        Assert.That((forest.GridMajor.R, forest.GridMajor.G, forest.GridMajor.B, forest.GridMajor.A),
            Is.EqualTo(((byte)24, (byte)66, (byte)24, (byte)150)));
        Assert.That(forest.MottleScale, Is.EqualTo(5f));
    }

    [Test]
    public void UnknownValue_FallsBackToForest()
    {
        Assert.That(TableBackgrounds.For((ETableBackground)999),
            Is.EqualTo(TableBackgrounds.For(ETableBackground.Forest)));
    }

    [Test]
    public void GridLines_AreEtchedDarkerThanTheSurface()
    {
        foreach (ETableBackground background in Enum.GetValues<ETableBackground>())
        {
            TableBackgroundStyle style = TableBackgrounds.For(background);
            int surface = style.Surface.R + style.Surface.G + style.Surface.B;
            int minor   = style.GridMinor.R + style.GridMinor.G + style.GridMinor.B;
            int major   = style.GridMajor.R + style.GridMajor.G + style.GridMajor.B;

            Assert.That(minor, Is.LessThan(surface), $"{background} minor grid is not etched");
            Assert.That(major, Is.LessThan(minor), $"{background} major grid must read stronger than minor");
        }
    }

    [Test]
    public void MottleTint_StaysDark_SoAdditiveBlendingDoesNotBlowOutPaleSurfaces()
    {
        foreach (ETableBackground background in Enum.GetValues<ETableBackground>())
        {
            TableBackgroundStyle style = TableBackgrounds.For(background);
            Assert.That(Math.Max(style.MottleTint.R, Math.Max(style.MottleTint.G, style.MottleTint.B)),
                Is.LessThanOrEqualTo(80), $"{background} mottle tint is too bright to add over the felt");
            Assert.That(style.MottleScale, Is.GreaterThan(0f), $"{background} has no mottle scale");
        }
    }

    [Test]
    public void EveryLabel_IsAsciiAndNonEmpty()
    {
        foreach (ETableBackground background in Enum.GetValues<ETableBackground>())
        {
            string label = TableBackgrounds.Label(background);
            Assert.That(label, Is.Not.Empty);
            foreach (char c in label)
                Assert.That(c, Is.LessThan((char)128), $"'{label}' has a glyph the ImGui atlas cannot bake");
        }
    }

    [Test]
    public void MarsLike_ReadsWithAHyphen()
    {
        Assert.That(TableBackgrounds.Label(ETableBackground.MarsLike), Is.EqualTo("Mars-Like"));
        Assert.That(TableBackgrounds.Label(ETableBackground.Forest), Is.EqualTo("Forest"));
    }
}
