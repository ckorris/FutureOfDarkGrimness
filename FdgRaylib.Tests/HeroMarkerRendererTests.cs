using System;
using System.Numerics;
using FdgRaylib.Rendering;
using NUnit.Framework;

namespace FdgRaylib.Tests;

// #227: joined-Hero indicator. Pure bits only -- the star geometry (centre, alternating radii, top-pointing)
// and the ASCII tooltip tag. Hero detection off IUnit.JoinedHeroModelId and the Raylib draw need a live
// unit / window and are left to hand-verification.
[TestFixture]
public class HeroMarkerRendererTests
{
    [Test]
    public void StarPoints_HasTenVerticesAlternatingOuterAndInner()
    {
        float outer = 10f, inner = outer * 0.4f;
        Vector2[] pts = HeroMarkerRenderer.StarPoints(0f, 0f, outer);

        Assert.That(pts.Length, Is.EqualTo(10), "five-point star = 5 outer + 5 inner vertices");
        for (int i = 0; i < pts.Length; i++)
        {
            float radius = pts[i].Length();
            float expected = (i % 2 == 0) ? outer : inner;
            Assert.That(radius, Is.EqualTo(expected).Within(0.001f), $"vertex {i} sits on the {(i % 2 == 0 ? "outer" : "inner")} radius");
        }
    }

    [Test]
    public void StarPoints_FirstVertexPointsStraightUp()
    {
        // Screen space: up is -y. The first (outer) point should be directly above the centre.
        Vector2[] pts = HeroMarkerRenderer.StarPoints(50f, 50f, 8f);
        Assert.That(pts[0].X, Is.EqualTo(50f).Within(0.001f), "top point is horizontally centred");
        Assert.That(pts[0].Y, Is.EqualTo(42f).Within(0.001f), "top point is one outer-radius above centre (-y)");
    }

    [Test]
    public void StarPoints_IsCentredOnGivenPoint()
    {
        Vector2[] pts = HeroMarkerRenderer.StarPoints(30f, 20f, 12f);
        float meanX = 0f, meanY = 0f;
        foreach (var p in pts) { meanX += p.X; meanY += p.Y; }
        meanX /= pts.Length; meanY /= pts.Length;
        Assert.That(meanX, Is.EqualTo(30f).Within(0.001f), "vertices are symmetric about the centre X");
        Assert.That(meanY, Is.EqualTo(20f).Within(0.001f), "vertices are symmetric about the centre Y");
    }

    [Test]
    public void OuterRadiusPx_ScalesWithBaseButClampsBothEnds()
    {
        Assert.That(HeroMarkerRenderer.OuterRadiusPx(20f), Is.EqualTo(10f).Within(0.001f), "mid-size base = half its radius");
        Assert.That(HeroMarkerRenderer.OuterRadiusPx(2f), Is.EqualTo(4f).Within(0.001f), "tiny base clamps up to the legibility floor");
        Assert.That(HeroMarkerRenderer.OuterRadiusPx(200f), Is.EqualTo(14f).Within(0.001f), "huge base clamps down so the star never swamps it");
    }

    [Test]
    public void FormatHeroTag_IsAsciiAndCarriesNameAndStats()
    {
        string tag = HeroMarkerRenderer.FormatHeroTag("Elven Noble", 3, 4);
        Assert.That(tag, Is.EqualTo("Hero: Elven Noble  Qua 3+  Def 4+"));
        AssertAscii(tag);
    }

    // #342: a save written before the name rode the attachment. The tag must degrade to what it printed
    // then, not to "Hero: " with a hole in it.
    [TestCase(null)]
    [TestCase("")]
    [TestCase("   ")]
    public void FormatHeroTag_WithoutAName_FallsBackToTheBareTag(string? name)
    {
        Assert.That(HeroMarkerRenderer.FormatHeroTag(name, 3, 4), Is.EqualTo("Hero  Qua 3+  Def 4+"));
    }

    [Test]
    public void FormatHeroNameLine_IsAsciiAndCarriesNameAndPoints()
    {
        string line = HeroMarkerRenderer.FormatHeroNameLine("Elven Noble", 150);
        Assert.That(line, Is.EqualTo("+ Elven Noble - 150pts"));
        AssertAscii(line);
    }

    [Test]
    public void FormatHeroNameLine_WithoutPoints_OmitsThem()
    {
        // Pre-#342 save: points folded into the host and the hero's share was never captured. "0pts" would
        // read as "this hero was free", so the cost is simply absent -- as the unit header does at 0.
        Assert.That(HeroMarkerRenderer.FormatHeroNameLine("Elven Noble", 0),
            Is.EqualTo("+ Elven Noble"));
    }

    [Test]
    public void FormatHeroNameLine_WithoutAName_StillMarksTheHero()
    {
        Assert.That(HeroMarkerRenderer.FormatHeroNameLine(null, 150), Is.EqualTo("+ Hero - 150pts"));
        Assert.That(HeroMarkerRenderer.FormatHeroNameLine("", 0), Is.EqualTo("+ Hero"));
    }

    // The font atlas bakes Basic Latin + Latin-1 only; anything above renders as '?' in game.
    private static void AssertAscii(string text)
    {
        foreach (char c in text)
            Assert.That(c, Is.LessThanOrEqualTo((char)0x7F), $"non-ASCII char in \"{text}\"");
    }
}
