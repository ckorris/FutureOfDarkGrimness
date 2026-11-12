using FdgRaylib.Rendering;
using NUnit.Framework;

namespace FdgRaylib.Tests;

// #387 — the one composer behind every "this range is modified" string (shoot panel rows + detail
// pane, CLI rows, overlay band labels). Pinned so the surfaces cannot drift apart, and so the
// unstamped-0 sentinel from WeaponTargetStats.EffectiveRangeInches always reads as unmodified.
[TestFixture]
public class RangeDeltaTextTests
{
    [Test]
    public void Unmodified_AndUnstamped_ProduceNothing()
    {
        Assert.That(RangeDeltaText.IsModified(24f, 24f), Is.False, "equal = unmodified");
        Assert.That(RangeDeltaText.IsModified(24f, 0f), Is.False, "0 = unstamped, never an indicator");
        Assert.That(RangeDeltaText.Suffix(24f, 24f), Is.Empty);
        Assert.That(RangeDeltaText.RowFact(24f, 0f), Is.Empty);
        Assert.That(RangeDeltaText.Detail(24f, 24f), Is.Null);
    }

    [Test]
    public void Buffed_ShowsSignedDelta()
    {
        Assert.That(RangeDeltaText.Delta(24f, 30f), Is.EqualTo("+6\""));
        Assert.That(RangeDeltaText.Suffix(24f, 30f), Is.EqualTo(" (+6\")"));
        Assert.That(RangeDeltaText.RowFact(24f, 30f), Is.EqualTo("range 30\" (+6\")"));
        Assert.That(RangeDeltaText.Detail(24f, 30f), Is.EqualTo("Range 30\" (base 24\", +6\")"));
    }

    [Test]
    public void Debuffed_ShowsNegativeDelta()
    {
        Assert.That(RangeDeltaText.RowFact(24f, 18f), Is.EqualTo("range 18\" (-6\")"));
    }

    [Test]
    public void FractionalDelta_KeepsOneDecimal()
    {
        Assert.That(RangeDeltaText.Delta(24f, 28.5f), Is.EqualTo("+4.5\""));
    }
}
