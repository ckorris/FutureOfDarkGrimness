using System;

namespace FdgRaylib.Rendering;

/// <summary>
/// #287 — the one way a wound quantity is written for the player. Wound counts are floats (#199: a hit x
/// save x regeneration chain of expected values), and under the probabilistic roller they are routinely
/// fractional, so the two naive spellings both mislead:
/// <list type="bullet">
/// <item>bare interpolation prints the whole float - "Wounds: 8.666667/12";</item>
/// <item><c>F0</c> silently TRUNCATES the fraction - a 3.4-wound pool reads "3 / 3 wounds assigned",
/// which is not the number the engine is working with.</item>
/// </list>
/// Rounding to hundredths keeps the information the player needs (8.67, 3.4) without the noise, and
/// drops trailing zeros so a realistic-mode whole number still reads "12", not "12.00".
///
/// <para>Every wound display goes through here - the unit/model hover tooltips, the Assign Wounds panel
/// and its canvas hover label, the model picker, and the CLI resolver - so the surfaces cannot drift
/// apart again. ASCII only (CLAUDE.md): the invariant culture never emits a non-ASCII separator.</para>
/// </summary>
public static class WoundFormat
{
    /// <summary>A wound quantity, rounded to the nearest hundredth with trailing zeros dropped.</summary>
    public static string Format(float wounds)
    {
        // A residue that rounds away must not print as "-0": remaining-wound counters are computed by
        // subtraction chains that land on tiny negatives routinely (#199's epsilon territory), and "-0"
        // on a wound counter reads as a bug to the player.
        float rounded = MathF.Round(wounds, 2, MidpointRounding.AwayFromZero);
        if (rounded == 0f) rounded = 0f;   // collapses negative zero
        return rounded.ToString("0.##", System.Globalization.CultureInfo.InvariantCulture);
    }

    /// <summary>"remaining/total", both rounded - the counter shown on multi-wound (Tough) models.</summary>
    public static string Fraction(float remaining, float total) => $"{Format(remaining)}/{Format(total)}";
}
