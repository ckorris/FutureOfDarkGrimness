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

    private const float Height = 5f;
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

    /// <summary>True only for a real, damaged unit — hidden at full strength (within <see cref="Epsilon"/>).</summary>
    public static bool ShouldShow(float remaining, float max) => max > Epsilon && remaining < max - Epsilon;

    public static float Fraction(float remaining, float max) =>
        max <= 0f ? 0f : Math.Clamp(remaining / max, 0f, 1f);

    /// <summary>Green when healthy → yellow → red when low.</summary>
    public static (byte r, byte g, byte b) FillColor(float fraction) =>
        fraction >= 0.5f
            ? Lerp((230, 200, 60), (70, 200, 90), (fraction - 0.5f) / 0.5f)   // yellow → green
            : Lerp((220, 60, 60), (230, 200, 60), fraction / 0.5f);           // red → yellow

    public static void Draw(ImDrawListPtr dl, float centerX, float topY, float width, float remaining, float max)
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
