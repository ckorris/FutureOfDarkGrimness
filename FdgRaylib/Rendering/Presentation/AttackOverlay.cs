using System;
using System.Collections.Generic;
using System.Numerics;
using FDG;
using FDG.Presentation.Beats;
using Raylib_cs;

namespace FdgRaylib.Rendering.Presentation;

/// <summary>
/// Draws the active <see cref="AttackBeat"/> in world space (over the table). Ranged: a bright
/// projectile travels from each attacker model toward its nearest target along a faint trajectory.
/// Melee: a flashing "clash" spark at the contact point. Inch→pixel matches the model renderer.
/// </summary>
public static class AttackOverlay
{
    private static readonly Color Tracer      = new(255, 220, 80, 255);
    private static readonly Color TracerFaint = new(255, 220, 80, 70);
    private static readonly Color Clash       = new(255, 90, 70, 255);

    public static void Draw(AttackBeat beat, float progress, float scale, int originX, int originY, float tableH)
    {
        if (beat.From.Count == 0 || beat.To.Count == 0) return;

        foreach (Position from in beat.From)
        {
            Position target = Nearest(from, beat.To);
            Vector2 f = ToPixel(from, scale, originX, originY, tableH);
            Vector2 t = ToPixel(target, scale, originX, originY, tableH);

            if (beat.IsMelee)
                DrawClash(t, progress, scale);
            else
                DrawTracer(f, t, progress);
        }
    }

    private static void DrawTracer(Vector2 from, Vector2 to, float t)
    {
        Raylib.DrawLineEx(from, to, 1.5f, TracerFaint);          // faint full trajectory
        Vector2 head = Vector2.Lerp(from, to, t);
        Vector2 tail = Vector2.Lerp(from, to, Math.Max(0f, t - 0.12f));
        Raylib.DrawLineEx(tail, head, 3f, Tracer);               // streak
        Raylib.DrawCircleV(head, 3.5f, Tracer);                  // projectile head
    }

    private static void DrawClash(Vector2 at, float t, float scale)
    {
        float pulse = 1f - Math.Abs(t - 0.5f) * 2f;              // 0 → 1 → 0
        byte a = (byte)Math.Clamp(pulse * 255f, 0f, 255f);
        var c = new Color(Clash.R, Clash.G, Clash.B, a);
        float r = Math.Max(6f, scale * 0.4f);
        Raylib.DrawLineEx(new Vector2(at.X - r, at.Y - r), new Vector2(at.X + r, at.Y + r), 3f, c);
        Raylib.DrawLineEx(new Vector2(at.X - r, at.Y + r), new Vector2(at.X + r, at.Y - r), 3f, c);
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
