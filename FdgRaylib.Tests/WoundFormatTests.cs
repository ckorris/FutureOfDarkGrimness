using FdgRaylib.Rendering;
using NUnit.Framework;

namespace FdgRaylib.Tests;

// #287 — wound quantities are floats (#199), and under the probabilistic roller they are routinely
// fractional. The two spellings the UI used before both lied in their own way: bare interpolation printed
// "8.666667", and F0 TRUNCATED a 3.4-wound pool to "3". WoundFormat is the single rounder every wound
// display now goes through.
[TestFixture]
public class WoundFormatTests
{
    [Test]
    public void RoundsToTheNearestHundredth()
    {
        Assert.That(WoundFormat.Format(8.666667f), Is.EqualTo("8.67"));
        Assert.That(WoundFormat.Format(0.333333f), Is.EqualTo("0.33"));
        Assert.That(WoundFormat.Format(3.4f), Is.EqualTo("3.4"), "a single decimal keeps one place");
    }

    [Test]
    public void DropsTrailingZeros_SoWholeNumbersReadAsIntegers()
    {
        Assert.That(WoundFormat.Format(12f), Is.EqualTo("12"),
            "realistic mode is all whole numbers - they must not read as 12.00");
        Assert.That(WoundFormat.Format(0f), Is.EqualTo("0"));
        Assert.That(WoundFormat.Format(2.50f), Is.EqualTo("2.5"));
    }

    // The old F0 formatting rounded a 3.4-wound pool to "3", so the Assign Wounds header claimed the
    // assignment was complete while the engine still had 0.4 to place. This is the regression that matters.
    [Test]
    public void KeepsTheFraction_ThatF0Truncated()
    {
        Assert.That(WoundFormat.Format(3.4f), Is.Not.EqualTo("3"));
        Assert.That(WoundFormat.Format(0.5f), Is.EqualTo("0.5"),
            "half a wound must not round away to nothing");
    }

    [Test]
    public void Fraction_WritesBothSidesRounded()
    {
        Assert.That(WoundFormat.Fraction(8.666667f, 12f), Is.EqualTo("8.67/12"));
        Assert.That(WoundFormat.Fraction(3f, 3f), Is.EqualTo("3/3"));
    }

    // A rounded-away residue must not print as a negative zero ("-0"), which reads as nonsense on a
    // wound counter. Float subtraction chains reach tiny negatives routinely (#199's epsilon territory).
    [Test]
    public void TinyNegativeResidue_ReadsAsZero()
    {
        Assert.That(WoundFormat.Format(-0.0001f), Is.EqualTo("0"));
    }

    // ASCII-only (CLAUDE.md): the invariant culture never emits a comma decimal separator, which the
    // ImGui font would still render but which would read as a thousands separator to the player.
    [Test]
    public void UsesAnInvariantDecimalPoint()
    {
        Assert.That(WoundFormat.Format(8.67f), Does.Contain(".").And.Not.Contain(","));
    }
}
