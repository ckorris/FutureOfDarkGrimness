using System;
using System.Collections.Generic;
using System.Numerics;
using FDG;
using FDG.Presentation.Beats;
using Raylib_cs;

namespace FdgRaylib.Rendering.Presentation;

/// <summary>
/// Draws the active <see cref="AttackBeat"/> in world space (over the table). Ranged: bright
/// projectiles travel from the firing models, fanned out across the targetable defenders (the beat's
/// <c>To</c> is already LoS-filtered) so it reads as the whole unit firing, not everyone piling onto
/// one model. Melee: a slim gray blade sweeps through an arc at the struck model. Inch→pixel matches
/// the model renderer.
/// </summary>
public static class AttackOverlay
{
    private static readonly Color Tracer      = new(255, 220, 80, 255);
    private static readonly Color TracerFaint = new(255, 220, 80, 70);
    private static readonly Color Blade       = new(205, 210, 220, 255); // steel gray
    private static readonly Color BladeEdge    = new(60, 65, 75, 255);

    public static void Draw(AttackBeat beat, float progress, float scale, int originX, int originY, float tableH)
    {
        if (beat.From.Count == 0 || beat.To.Count == 0) return;

        int volleys = Math.Max(1, beat.VolleyCount);
        // AP scales the animation size: AP0 = 1×, AP4 = 3×, linear from there.
        float apScale = 1f + 0.5f * Math.Max(0, beat.ArmorPenetration);

        // Each volley owns a time slice [v/volleys, (v+1)/volleys], played one after another. Within
        // a volley every firing weapon (From) fires together — each From entry is its real model.
        for (int v = 0; v < volleys; v++)
        {
            float volleyT = progress * volleys - v;
            if (volleyT <= 0f || volleyT >= 1f) continue;

            for (int i = 0; i < beat.From.Count; i++)
            {
                Vector2 f = ToPixel(beat.From[i], scale, originX, originY, tableH);

                if (beat.IsMelee)
                {
                    // Melee is adjacent — strike the nearest defender.
                    Vector2 at = ToPixel(Nearest(beat.From[i], beat.To), scale, originX, originY, tableH);
                    DrawMeleeBlade(at, volleyT, scale, apScale);
                }
                else
                {
                    // Spread fire: shooter i targets a different defender, rotating by volley so over
                    // successive volleys the shots sweep the whole targetable enemy unit.
                    Position toPos = beat.To[(i + v) % beat.To.Count];
                    Vector2 t = ToPixel(toPos, scale, originX, originY, tableH);
                    DrawTracer(f, t, volleyT, apScale);
                }
            }
        }
    }

    private static void DrawTracer(Vector2 from, Vector2 to, float t, float apScale)
    {
        Raylib.DrawLineEx(from, to, 1.5f, TracerFaint);              // faint full trajectory
        Vector2 head = Vector2.Lerp(from, to, t);
        Vector2 tail = Vector2.Lerp(from, to, Math.Max(0f, t - 0.12f));
        Raylib.DrawLineEx(tail, head, 3f * apScale, Tracer);        // streak
        Raylib.DrawCircleV(head, 3.5f * apScale, Tracer);           // projectile head
    }

    // A slim blade pivoting at its base (the struck model), sweeping through a downward arc — a sword
    // swing, deliberately gray so it never reads as the red "dying" cue.
    private static void DrawMeleeBlade(Vector2 at, float t, float scale, float apScale)
    {
        float len       = Math.Max(10f, scale * 0.55f) * apScale;
        float halfWidth = len * 0.12f;

        const float startAng = -2.1f, endAng = -0.6f;  // radians; screen-space (y down) downward swing
        float ang = startAng + (endAng - startAng) * t;

        float pulse = 1f - Math.Abs(t - 0.5f) * 2f;                  // fade in then out across the swing
        byte a = (byte)Math.Clamp((0.4f + 0.6f * pulse) * 255f, 0f, 255f);
        var fill = new Color(Blade.R, Blade.G, Blade.B, a);
        var edge = new Color(BladeEdge.R, BladeEdge.G, BladeEdge.B, a);

        var dir  = new Vector2(MathF.Cos(ang), MathF.Sin(ang));
        var perp = new Vector2(-dir.Y, dir.X);
        Vector2 tip   = at + dir * len;
        Vector2 baseL = at + perp * halfWidth;
        Vector2 baseR = at - perp * halfWidth;

        // Fill both windings so it shows regardless of orientation, then outline the edge.
        Raylib.DrawTriangle(tip, baseL, baseR, fill);
        Raylib.DrawTriangle(tip, baseR, baseL, fill);
        Raylib.DrawTriangleLines(tip, baseL, baseR, edge);
    }

    private static Position Nearest(Position from, IReadOnlyList<Position> candidates)
    {
        Position best = candidates[0];
        float bestSq = float.MaxValue;
        foreach (Position c in candidates)
        {
            float dx = c.x - from.x, dz = c.z - from.z;
            float sq = dx * dx + dz * dz;
            if (sq < bestSq) { bestSq = sq; best = c; }
        }
        return best;
    }

    private static Vector2 ToPixel(Position pos, float scale, int originX, int originY, float tableH) =>
        new(originX + pos.x * scale, originY + (tableH - pos.z) * scale);
}
