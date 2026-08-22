using System.Numerics;
using FDG;
using ImGuiNET;

namespace FdgRaylib.Rendering;

/// <summary>
/// App-side unit health bars (#152). A small bar under each DAMAGED unit, hidden at full strength
/// (Civ-style). The fill is the unit's total remaining wounds / total max — a granular FLOAT sum across
/// models, so a multi-wound (Tough) model that takes a wound but lives still drains the bar, and the
/// deterministic dice path's fractional wounds are honoured rather than rounded.
/// </summary>
public static class HealthBarRenderer
{
    // Float slack so noise (and exactly-full) never flickers a sliver of bar onto a healthy unit.
    public const float Epsilon = 0.001f;

    public const float Height = 5f;
    private const float MinWidth = 22f;

    /// <summary>
    /// Total remaining vs max wounds, summed across ALL the unit's models (dead included, so the
    /// denominator is the unit's original total and casualties show as the bar shrinking). Remaining is
    /// floored per model at 0, so an over-killed model (WoundsDealt &gt; TotalWounds) can't subtract another
    /// model's health — unlike the engine's raw <c>RemainingWounds</c>.
    /// </summary>
    public static (float remaining, float max) Compute(IUnit unit)
    {
        float remaining = 0f, max = 0f;
        foreach (IModel model in unit.Models)
        {
            max += model.TotalWounds;
            remaining += MathF.Max(0f, model.TotalWounds - model.WoundsDealt);
        }
        return (remaining, max);
    }

    /// <summary>
    /// #347: where along the bar a MODEL is lost, as fractions of the unit's total wounds, strictly inside
    /// (0, 1). A 5-model squad of 1-wound troopers ticks at every fifth; a squad of Tough(3) models ticks
    /// every three wounds, so the bar shows how much punishment is left in the model currently taking it
    /// rather than only how much is left in the unit.
    ///
    /// <para>Boundaries are the running sum of each model's <c>TotalWounds</c> in ROSTER order, which is
    /// the only order available: the bar's fill is an aggregate (see <see cref="Compute"/>), so no tick can
    /// name a particular model. It is the SPACING that carries the meaning - "this much damage costs you a
    /// body" - and that is order-independent for the uniform units this is drawn on. A mixed unit (a Tough
    /// hero joined to 1-wound troopers) gets a correct set of boundaries in an arbitrary arrangement, which
    /// still reads as the right number of ticks in the right sizes.</para>
    ///
    /// <para>Empty for a single-model unit: with nothing to lose but itself, every tick would sit at 0 or 1
    /// and the bar would just gain a border.</para>
    /// </summary>
    public static IReadOnlyList<float> CasualtyTicks(IUnit unit)
    {
        var boundaries = new List<float>();
        float max = 0f, running = 0f;
        foreach (IModel model in unit.Models)
        {
            max += model.TotalWounds;
        }
        if (max <= Epsilon) return boundaries;

        foreach (IModel model in unit.Models)
        {
            running += model.TotalWounds;
            if (running <= Epsilon || running >= max - Epsilon) continue;  // drops the final model's edge
            boundaries.Add(running / max);
        }
        return boundaries;
    }

    /// <summary>True only for a real, damaged unit — hidden at full strength (within <see cref="Epsilon"/>).</summary>
    public static bool ShouldShow(float remaining, float max) => max > Epsilon && remaining < max - Epsilon;

    public static float Fraction(float remaining, float max) =>
        max <= 0f ? 0f : Math.Clamp(remaining / max, 0f, 1f);

    /// <summary>
    /// Green above half strength, snapping to yellow at exactly 50% and ramping to red toward 0. Half
    /// strength is the morale cliff — a failed test routs/destroys a unit at ≤50% wounds (the engine's
    /// <c>IsAtHalfStrength</c> is <c>remaining*2 &lt;= max</c>) — so the colour changes AT that threshold
    /// rather than easing through it, making the dangerous half-strength state read at a glance.
    /// </summary>
    public static (byte r, byte g, byte b) FillColor(float fraction) =>
        fraction > 0.5f
            ? ((byte)70, (byte)200, (byte)90)                        // green: above half strength
            : Lerp((220, 60, 60), (230, 200, 60), fraction / 0.5f);  // red (0%) → yellow (50%)

    /// <param name="casualtyTicks">#347: model-loss boundaries from <see cref="CasualtyTicks"/>, drawn as
    /// thin light rules across the bar. Null or empty draws the plain bar (single-model units, callers that
    /// have no unit in hand).</param>
    public static void Draw(ImDrawListPtr dl, float centerX, float topY, float width, float remaining,
        float max, IReadOnlyList<float>? casualtyTicks = null)
    {
        float frac = Fraction(remaining, max);
        float w = MathF.Max(MinWidth, width);

        var tl = new Vector2(centerX - w * 0.5f, topY);
        var br = new Vector2(centerX + w * 0.5f, topY + Height);

        dl.AddRectFilled(tl, br, U32(20, 20, 20, 210));
        if (frac > 0f)
        {
            (byte r, byte g, byte b) = FillColor(frac);
            dl.AddRectFilled(tl, new Vector2(tl.X + w * frac, br.Y), U32(r, g, b, 235));
        }

        // #347: casualty rules, over the fill and under the border. Deliberately faint and hairline — they
        // are a scale on the bar, not a second thing to read; at a glance you should see the fill and only
        // notice the divisions when you go looking for "how many more hits is that".
        if (casualtyTicks != null)
        {
            // Light and translucent: legible over both the green and the red end of the fill, invisible
            // enough not to compete with it.
            uint tickColor = U32(235, 235, 235, 120);
            // Inset by a pixel top and bottom so the ticks read as gradations rather than as the bar being
            // chopped into separate boxes.
            float y0 = tl.Y + 1f, y1 = br.Y - 1f;
            foreach (float at in casualtyTicks)
            {
                if (at <= 0f || at >= 1f) continue;
                float x = MathF.Round(tl.X + w * at);
                // Never draw a tick under the border it would merge with.
                if (x <= tl.X + 0.5f || x >= br.X - 0.5f) continue;
                dl.AddLine(new Vector2(x, y0), new Vector2(x, y1), tickColor, 1f);
            }
        }

        dl.AddRect(tl, br, U32(0, 0, 0, 220));
    }

    private static (byte, byte, byte) Lerp((int r, int g, int b) a, (int r, int g, int b) c, float t)
    {
        t = Math.Clamp(t, 0f, 1f);
        return ((byte)(a.r + (c.r - a.r) * t),
                (byte)(a.g + (c.g - a.g) * t),
                (byte)(a.b + (c.b - a.b) * t));
    }

    private static uint U32(byte r, byte g, byte b, byte a) =>
        ImGui.ColorConvertFloat4ToU32(new Vector4(r / 255f, g / 255f, b / 255f, a / 255f));
}
