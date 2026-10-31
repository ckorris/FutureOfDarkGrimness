using System;
using System.Numerics;
using FDG;
using Raylib_cs;

namespace FdgRaylib.Rendering;

/// <summary>
/// App-side "this model is a joined Hero" indicator (#227). A small white star (dark-outlined so it reads
/// on any player colour) drawn centred ON the hero model's base, plus a hover-tooltip tag carrying the
/// hero's own Quality / Defense. The star sits INSIDE the base, overlaying the player-colour fill, so it
/// never collides with the selection / hover halos or the weapon / charge range rings -- all of which live
/// AROUND the base as outlines / rings. (Gold was rejected for exactly that reason: it would blend with
/// the selection effects.)
///
/// Which model is the hero comes straight from the engine (<see cref="IUnit.JoinedHeroModelId"/>, #006);
/// this only renders it. The star geometry and the tag text are pure functions (unit-tested); only the
/// Raylib draw needs a window.
/// </summary>
public static class HeroMarkerRenderer
{
    /// <summary>True if <paramref name="model"/> is the joined Hero (#006) fighting inside <paramref name="unit"/>.</summary>
    public static bool IsHeroModel(IUnit unit, IModel model) =>
        unit.JoinedHeroModelId is { } heroId && model.ID == heroId;

    // A classic five-point star: 5 outer points, 5 inner, inner radius at 40% of the outer.
    private const int Points = 5;
    private const float InnerRatio = 0.4f;

    // Outer radius as a fraction of the base's pixel radius, clamped so the star stays legible when zoomed
    // out yet never swamps a large base.
    private const float RadiusFraction = 0.5f;
    private const float MinRadiusPx = 4f;
    private const float MaxRadiusPx = 14f;

    public static float OuterRadiusPx(float baseRadiusPx) =>
        Math.Clamp(baseRadiusPx * RadiusFraction, MinRadiusPx, MaxRadiusPx);

    /// <summary>
    /// The 2*<see cref="Points"/> perimeter vertices of the star centred at (cx, cy), alternating outer and
    /// inner radius, the first vertex pointing straight up (screen -y). Pure geometry -- unit-tested.
    /// </summary>
    public static Vector2[] StarPoints(float cx, float cy, float outerRadius)
    {
        var pts = new Vector2[Points * 2];
        float inner = outerRadius * InnerRatio;
        for (int i = 0; i < Points * 2; i++)
        {
            float r = (i % 2 == 0) ? outerRadius : inner;                 // even = outer point, odd = inner notch
            float ang = -MathF.PI / 2f + i * (MathF.PI / Points);         // start at top (-90 deg), step 36 deg
            pts[i] = new Vector2(cx + MathF.Cos(ang) * r, cy + MathF.Sin(ang) * r);
        }
        return pts;
    }

    /// <summary>
    /// Draws the hero star filled white with a dark outline, centred at pixel (cx, cy), sized to the base.
    /// <paramref name="alpha"/> (0-255) fades it with the model (dying / gliding presentation states).
    /// </summary>
    public static void DrawStarRaylib(float cx, float cy, float baseRadiusPx, byte alpha)
    {
        Vector2[] perimeter = StarPoints(cx, cy, OuterRadiusPx(baseRadiusPx));
        var centre  = new Vector2(cx, cy);
        var white   = new Color((byte)255, (byte)255, (byte)255, alpha);
        var outline = new Color((byte)20, (byte)20, (byte)20, alpha);

        // Fill as a fan of centre-anchored triangles (the concave star is star-convex from its centre, so
        // every perimeter vertex is visible from the middle). Raylib back-face-culls by winding and ours
        // depends on orientation, so -- like the heading marker -- draw both orderings: exactly one renders,
        // the other is culled, so there is no double-blend even while fading.
        for (int i = 0; i < perimeter.Length; i++)
        {
            Vector2 a = perimeter[i], b = perimeter[(i + 1) % perimeter.Length];
            Raylib.DrawTriangle(centre, a, b, white);
            Raylib.DrawTriangle(centre, b, a, white);
        }

        for (int i = 0; i < perimeter.Length; i++)
            Raylib.DrawLineEx(perimeter[i], perimeter[(i + 1) % perimeter.Length], 1.2f, outline);
    }

    /// <summary>
    /// Tooltip / army-list tag for the joined hero, e.g. "Hero: Elven Noble  Qua 3+  Def 4+". ASCII-only.
    /// The name comes from <c>HeroAttachment.Name</c> (#342); a pre-#342 save has none, so the tag falls
    /// back to the bare "Hero  Qua 3+  Def 4+" it printed before.
    /// </summary>
    public static string FormatHeroTag(string? name, int quality, int defense) =>
        string.IsNullOrWhiteSpace(name)
            ? $"Hero  Qua {quality}+  Def {defense}+"
            : $"Hero: {name}  Qua {quality}+  Def {defense}+";

    /// <summary>
    /// The army list's under-the-unit-name line for a joined hero (#342), e.g. "+ Elven Noble - 150pts".
    /// Its own line rather than appended to the unit header: real hero names run long ("Knight Veteran
    /// Master Brother") and would blow out the table row's width. Points are omitted when the engine
    /// carried none (pre-#342 save), matching how the unit header hides a 0 cost. ASCII-only.
    /// </summary>
    public static string FormatHeroNameLine(string? name, int pointCost)
    {
        string label = string.IsNullOrWhiteSpace(name) ? "Hero" : name!;
        return pointCost > 0 ? $"+ {label} - {pointCost}pts" : $"+ {label}";
    }
}
