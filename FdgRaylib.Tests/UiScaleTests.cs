using FdgRaylib.Rendering;
using NUnit.Framework;

namespace FdgRaylib.Tests;

// The resolution-derived UI scale: anchored at 1.4 on a 4K (2160p) display, scaled down for smaller
// screens, clamped so a laptop stays readable and a >4K display doesn't balloon.
[TestFixture]
public class UiScaleTests
{
    [Test]
    public void FourK_StaysAtTheTunedAnchor()
    {
        Assert.That(RaylibRenderer.ComputeUiScale(2160), Is.EqualTo(1.4f).Within(0.001f));
    }

    [Test]
    public void TallerThan4K_IsCappedAtTheAnchor()
    {
        Assert.That(RaylibRenderer.ComputeUiScale(4320), Is.EqualTo(1.4f).Within(0.001f));
    }

    [Test]
    public void Laptop1080p_IsFlooredAtOne()
    {
        // 1080/2160*1.4 = 0.7, below the 1.0 floor.
        Assert.That(RaylibRenderer.ComputeUiScale(1080), Is.EqualTo(1.0f).Within(0.001f));
    }

    [Test]
    public void BetweenFloorAndAnchor_ScalesProportionally()
    {
        // 1620/2160*1.4 = 1.05, inside [1.0, 1.4].
        Assert.That(RaylibRenderer.ComputeUiScale(1620), Is.EqualTo(1.05f).Within(0.001f));
    }

    [Test]
    public void UnknownHeight_FallsBackToTheTunedDefault()
    {
        Assert.That(RaylibRenderer.ComputeUiScale(0), Is.EqualTo(1.4f).Within(0.001f));
    }
}
