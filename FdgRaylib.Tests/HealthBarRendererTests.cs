using FdgRaylib.Rendering;
using NUnit.Framework;

namespace FdgRaylib.Tests;

// #152: unit health bars. Pure float logic — hide-at-full (Civ-style), fraction clamping, color ramp.
[TestFixture]
public class HealthBarRendererTests
{
    [Test]
    public void ShouldShow_IsHiddenAtFullStrength_ShownWhenDamaged()
    {
        Assert.That(HealthBarRenderer.ShouldShow(9f, 9f), Is.False, "exactly full is hidden");
        Assert.That(HealthBarRenderer.ShouldShow(8.9999f, 9f), Is.False, "within epsilon of full is hidden (no float-noise sliver)");
        Assert.That(HealthBarRenderer.ShouldShow(8f, 9f), Is.True, "a Tough model down one wound (but alive) still shows the bar");
        Assert.That(HealthBarRenderer.ShouldShow(2.5f, 3f), Is.True, "fractional (deterministic-path) wounds show");
        Assert.That(HealthBarRenderer.ShouldShow(0f, 5f), Is.True);
    }

    [Test]
    public void ShouldShow_HandlesDegenerateMax()
    {
        Assert.That(HealthBarRenderer.ShouldShow(0f, 0f), Is.False);
        Assert.That(HealthBarRenderer.ShouldShow(5f, 0f), Is.False);
    }

    [Test]
    public void Fraction_IsAFloatRatio_ClampedToUnitRange()
    {
        Assert.That(HealthBarRenderer.Fraction(2.5f, 3f), Is.EqualTo(0.8333f).Within(0.001f));
        Assert.That(HealthBarRenderer.Fraction(8f, 9f), Is.EqualTo(0.8889f).Within(0.001f));
        Assert.That(HealthBarRenderer.Fraction(-2f, 5f), Is.EqualTo(0f), "overkill / negative clamps to 0");
        Assert.That(HealthBarRenderer.Fraction(10f, 5f), Is.EqualTo(1f), "over-full clamps to 1");
        Assert.That(HealthBarRenderer.Fraction(1f, 0f), Is.EqualTo(0f));
    }

    [Test]
    public void FillColor_RampsGreenToRed()
    {
        Assert.That(HealthBarRenderer.FillColor(1f), Is.EqualTo(((byte)70, (byte)200, (byte)90)), "full = green");
        Assert.That(HealthBarRenderer.FillColor(0.5f), Is.EqualTo(((byte)230, (byte)200, (byte)60)), "half = yellow");
        Assert.That(HealthBarRenderer.FillColor(0f), Is.EqualTo(((byte)220, (byte)60, (byte)60)), "empty = red");
    }
}
