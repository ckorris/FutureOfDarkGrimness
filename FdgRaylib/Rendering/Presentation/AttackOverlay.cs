using System;
using System.Collections.Generic;
using System.Numerics;
using FDG;
using FDG.Presentation.Beats;
using Raylib_cs;
using static FdgRaylib.Rendering.Presentation.WeaponEffectCatalog;

namespace FdgRaylib.Rendering.Presentation;

/// <summary>
/// Draws the active <see cref="AttackBeat"/> in world space (over the table). #239: the beat's
/// WeaponEffect key selects a <see cref="WeaponEffectCatalog"/> style (form + palette), and the
/// beat's hit share decides which shots CONNECT — a hit lands with its impact visual, a miss flies
/// visibly past the target (with a deterministic sideways error) and just fades. Ranged shots fan
/// out across the targetable defenders (the beat's <c>To</c> is already LoS-filtered); melee is a
/// styled swing at the nearest struck model. Inch->pixel matches the model renderer.
/// </summary>
public static class AttackOverlay
{
    public static void Draw(AttackBeat beat, float progress, float scale, int originX, int originY, float tableH)
    {
        if (beat.From.Count == 0 || beat.To.Count == 0) return;

        int volleys = Math.Max(1, beat.VolleyCount);
        int shooters = beat.From.Count;
        int totalShots = AttackShotPlan.TotalShots(shooters, volleys);
        int visualHits = AttackShotPlan.VisualHits(beat.HitCount, beat.AttackCount, totalShots);
        // AP scales the animation size: AP0 = 1×, AP4 = 3×, linear from there.
        float apScale = 1f + 0.5f * Math.Max(0, beat.ArmorPenetration);

        // Each volley owns a time slice [v/volleys, (v+1)/volleys], played one after another. Within
        // a volley every firing weapon (From) fires together — each From entry is its real model.
        for (int v = 0; v < volleys; v++)
        {
            float volleyT = progress * volleys - v;
            if (volleyT <= 0f || volleyT >= 1f) continue;

            for (int i = 0; i < shooters; i++)
            {
                int shot = AttackShotPlan.ShotIndex(v, i, shooters);
                bool hits = AttackShotPlan.ShotHits(shot, totalShots, visualHits);
                Vector2 f = ToPixel(beat.From[i], scale, originX, originY, tableH);

                if (beat.IsMelee)
                {
                    // Melee is adjacent — strike the nearest defender, swinging toward it.
                    Vector2 at = ToPixel(Nearest(beat.From[i], beat.To), scale, originX, originY, tableH);
                    DrawMelee(Melee(beat.WeaponEffect), f, at, volleyT, scale, apScale, hits, shot);
                }
                else
                {
                    // Spread fire: shooter i targets a different defender, rotating by volley so over
                    // successive volleys the shots sweep the whole targetable enemy unit.
                    Position toPos = beat.To[(i + v) % beat.To.Count];
                    Vector2 t = ToPixel(toPos, scale, originX, originY, tableH);
                    DrawRanged(Ranged(beat.WeaponEffect), f, t, volleyT, apScale, hits, shot);
                }
            }
        }
    }

    // ---------------- ranged ----------------

    private static void DrawRanged(RangedEffectStyle s, Vector2 from, Vector2 to, float t,
        float apScale, bool hits, int shot)
    {
        // A miss travels PAST the real target and never sparks; a hit ends exactly on it.
        Vector2 end = hits ? to : MissEndpoint(from, to, shot);

        switch (s.Form)
        {
            case RangedForm.Tracer: DrawTracer(s, from, end, t, apScale, hits); break;
            case RangedForm.Bolt:   DrawBolt(s, from, end, t, apScale, hits); break;
            case RangedForm.Beam:   DrawBeam(s, from, end, t, apScale); break;
            case RangedForm.Rocket: DrawRocket(s, from, end, t, apScale, hits); break;
            case RangedForm.Lobbed: DrawLobbed(s, from, end, t, apScale); break;
            case RangedForm.Cone:   DrawCone(s, from, end, t, apScale, shot); break;
            case RangedForm.Glob:   DrawGlob(s, from, end, t, apScale, shot); break;
        }

        if (hits) DrawImpact(s, to, ImpactIntensity(t, s.LandFraction), apScale);
    }

    // 0 until the shot lands (per the style's LandFraction), then ramps 0..1 over the remainder.
    private static float ImpactIntensity(float t, float landFraction) =>
        t <= landFraction ? 0f : (t - landFraction) / Math.Max(0.05f, 1f - landFraction);

    // Where a missed shot ends up: past the target along the flight line, pushed sideways by a
    // deterministic per-shot error so a volley's misses splay instead of stacking.
    private static Vector2 MissEndpoint(Vector2 from, Vector2 to, int shot)
    {
        Vector2 dir = to - from;
        Vector2 perp = Perp(dir);
        float side = ((shot * 37) % 7 - 3) / 3f; // -1..1, deterministic
        return to + dir * 0.22f + perp * (8f * side);
    }

    // A missed projectile fades out over the overshoot stretch instead of popping.
    private static float MissFade(float t, bool hits) =>
        hits || t < 0.75f ? 1f : Math.Max(0f, 1f - (t - 0.75f) / 0.25f);

    private static void DrawTracer(RangedEffectStyle s, Vector2 from, Vector2 end, float t,
        float apScale, bool hits)
    {
        DrawMuzzleFlash(from, t < 0.3f ? 1f - t / 0.3f : 0f, apScale, s.Glow);

        float fade = MissFade(t, hits);
        Raylib.DrawLineEx(from, end, 1.5f, Fade(s.Core, 0.27f * fade)); // faint full trajectory
        Vector2 head = Vector2.Lerp(from, end, t);
        Vector2 tail = Vector2.Lerp(from, end, Math.Max(0f, t - 0.12f));
        Raylib.DrawLineEx(tail, head, 3f * s.Width * apScale, Fade(s.Core, fade)); // streak
        Raylib.DrawCircleV(head, 3.5f * s.Width * apScale, Fade(s.Core, fade));    // projectile head
    }

    private static void DrawBolt(RangedEffectStyle s, Vector2 from, Vector2 end, float t,
        float apScale, bool hits)
    {
        DrawMuzzleFlash(from, t < 0.25f ? 1f - t / 0.25f : 0f, apScale * 0.7f, s.Glow);

        float fade = MissFade(t, hits);
        Vector2 head = Vector2.Lerp(from, end, t);
        Vector2 tail = Vector2.Lerp(from, end, Math.Max(0f, t - 0.16f));
        Raylib.DrawLineEx(tail, head, 2.5f * s.Width * apScale, Fade(s.Glow, 0.5f * fade)); // trail
        Raylib.DrawCircleV(head, 6f * s.Width * apScale, Fade(s.Glow, 0.35f * fade));       // halo
        Raylib.DrawCircleV(head, 3.5f * s.Width * apScale, Fade(s.Core, fade));             // core orb
    }

    private static void DrawBeam(RangedEffectStyle s, Vector2 from, Vector2 end, float t, float apScale)
    {
        // Instantaneous: the whole beam flashes in, holds, and decays within the volley slice.
        float e = t < 0.2f ? t / 0.2f : Math.Max(0f, 1f - (t - 0.2f) / 0.8f);
        if (e <= 0f) return;
        Raylib.DrawLineEx(from, end, 6f * s.Width * apScale, Fade(s.Glow, 0.4f * e));
        Raylib.DrawLineEx(from, end, 2.2f * s.Width * apScale, Fade(s.Core, e));
        DrawMuzzleFlash(from, e, apScale, s.Core);
    }

    private static void DrawRocket(RangedEffectStyle s, Vector2 from, Vector2 end, float t,
        float apScale, bool hits)
    {
        DrawMuzzleFlash(from, t < 0.3f ? 1f - t / 0.3f : 0f, apScale, s.Core);

        float fade = MissFade(t, hits);
        Vector2 head = Vector2.Lerp(from, end, t);
        // Smoke puffs hang behind the rocket, growing and thinning as they age.
        for (int k = 1; k <= 4; k++)
        {
            float pt = t - 0.07f * k;
            if (pt <= 0f) continue;
            Vector2 puff = Vector2.Lerp(from, end, pt);
            Raylib.DrawCircleV(puff, (2f + 1.2f * k) * s.Width, Fade(s.Glow, (0.30f - 0.06f * k) * fade));
        }
        Raylib.DrawCircleV(head, 3.5f * s.Width * apScale, Fade(s.Core, fade)); // warhead + tail flame
        Vector2 flame = Vector2.Lerp(from, end, Math.Max(0f, t - 0.05f));
        Raylib.DrawLineEx(flame, head, 3f * s.Width, Fade(s.Core, 0.8f * fade));
    }

    private static void DrawLobbed(RangedEffectStyle s, Vector2 from, Vector2 end, float t, float apScale)
    {
        // Parabolic arc: the shell rises off the flight line and slams down at the end.
        float dist = Vector2.Distance(from, end);
        float arc = Math.Clamp(dist * 0.30f, 12f, 80f);
        Vector2 pos = Vector2.Lerp(from, end, t);
        pos.Y -= arc * MathF.Sin(MathF.PI * Math.Clamp(t, 0f, 1f));

        DrawMuzzleFlash(from, t < 0.2f ? 1f - t / 0.2f : 0f, apScale, s.Glow);
        Raylib.DrawCircleV(pos, 4f * s.Width * apScale, s.Core);                 // dark shell
        Raylib.DrawCircleLines((int)pos.X, (int)pos.Y, 4f * s.Width * apScale + 1.5f, Fade(s.Glow, 0.6f));
    }

    private static void DrawCone(RangedEffectStyle s, Vector2 from, Vector2 end, float t,
        float apScale, int shot)
    {
        // A gout of expanding puffs along the direction of fire — no projectile head. The stream
        // walks out over the first part of the slice and burns down at the end.
        Vector2 dir = end - from;
        Vector2 perp = Perp(dir);
        const int puffs = 7;
        for (int k = 0; k < puffs; k++)
        {
            float pt = (k + 1) / (float)puffs;               // position along the cone
            if (t * 1.3f < pt) continue;                     // stream still walking out
            float dieDown = t > 0.75f ? (t - 0.75f) / 0.25f : 0f;
            float alpha = Math.Clamp(0.75f - 0.45f * pt - dieDown, 0f, 1f);
            if (alpha <= 0f) continue;

            float wobble = MathF.Sin(shot * 3.1f + k * 2.7f + t * 6f) * 3.5f * pt;
            Vector2 pos = from + dir * pt + perp * wobble;
            float r = (3f + 10f * pt) * s.Width * (0.8f + 0.2f * apScale);
            Raylib.DrawCircleV(pos, r, Fade(Blend(s.Core, s.Glow, pt), alpha * 0.6f));
        }
    }

    private static void DrawGlob(RangedEffectStyle s, Vector2 from, Vector2 end, float t,
        float apScale, int shot)
    {
        DrawMuzzleFlash(from, t < 0.2f ? 1f - t / 0.2f : 0f, apScale * 0.5f, s.Core);

        float fade = MissFade(t, true); // globs splash or splat, they don't fade mid-air
        Vector2 dir = end - from;
        Vector2 perp = Perp(dir);
        float Wob(float at) => MathF.Sin(at * 9f + shot * 1.7f) * 5f;

        Vector2 head = Vector2.Lerp(from, end, t) + perp * Wob(t);
        // Droplets dribbling behind the lump.
        for (int k = 1; k <= 2; k++)
        {
            float pt = t - 0.09f * k;
            if (pt <= 0f) continue;
            Vector2 drop = Vector2.Lerp(from, end, pt) + perp * Wob(pt);
            Raylib.DrawCircleV(drop, (2.2f - 0.6f * k) * s.Width, Fade(s.Glow, 0.5f * fade));
        }
        Raylib.DrawCircleV(head, 4f * s.Width * apScale, Fade(s.Core, fade));
        Raylib.DrawCircleV(head + perp * 2f, 2f * s.Width * apScale, Fade(s.Glow, 0.8f * fade));
    }

    // ---------------- impacts (hits only) ----------------

    private static void DrawImpact(RangedEffectStyle s, Vector2 at, float intensity, float apScale)
    {
        if (intensity <= 0f) return;
        switch (s.Impact)
        {
            case ImpactKind.Spark:
                DrawImpactSpark(at, 1f - intensity * 0.4f, apScale, s.Core);
                break;

            case ImpactKind.Explosion:
            {
                float r = (5f + 14f * intensity) * apScale;
                Raylib.DrawCircleV(at, r * 0.55f, Fade(s.Glow, (1f - intensity) * 0.9f));
                Raylib.DrawCircleV(at, r * 0.3f, Fade(Raylib_cs.Color.White, (1f - intensity) * 0.8f));
                Raylib.DrawCircleLines((int)at.X, (int)at.Y, r, Fade(s.Glow, 1f - intensity));
                DrawImpactSpark(at, 1f - intensity * 0.5f, apScale * 1.3f, s.Glow);
                break;
            }

            case ImpactKind.Splatter:
            {
                float reach = (4f + 9f * intensity) * apScale;
                for (int k = 0; k < 5; k++)
                {
                    float ang = k * (MathF.PI * 2f / 5f) + 0.7f;
                    var d = new Vector2(MathF.Cos(ang), MathF.Sin(ang));
                    Raylib.DrawCircleV(at + d * reach, 2.2f * (1f - 0.5f * intensity), Fade(s.Core, 1f - intensity));
                }
                Raylib.DrawCircleV(at, 3f * apScale * (1f - 0.4f * intensity), Fade(s.Core, 1f - intensity * 0.6f));
                break;
            }

            case ImpactKind.Ring:
            {
                float r = (4f + 13f * intensity) * apScale;
                Raylib.DrawCircleLines((int)at.X, (int)at.Y, r, Fade(s.Glow, 1f - intensity));
                Raylib.DrawCircleLines((int)at.X, (int)at.Y, r * 0.6f, Fade(s.Core, (1f - intensity) * 0.8f));
                break;
            }

            case ImpactKind.Shatter:
            {
                float reach = (5f + 8f * intensity) * apScale;
                for (int k = 0; k < 6; k++)
                {
                    float ang = k * (MathF.PI / 3f) + 0.35f;
                    var d = new Vector2(MathF.Cos(ang), MathF.Sin(ang));
                    Raylib.DrawLineEx(at + d * (reach * 0.3f), at + d * reach, 1.4f, Fade(s.Core, 1f - intensity));
                    Raylib.DrawCircleV(at + d * reach, 1.4f, Fade(s.Core, (1f - intensity) * 0.9f));
                }
                break;
            }

            case ImpactKind.Bloom:
            {
                float pulse = 1f - intensity;
                Raylib.DrawCircleV(at, (4f + 8f * intensity) * apScale, Fade(s.Glow, pulse * 0.55f));
                Raylib.DrawCircleV(at, 2.5f * apScale, Fade(Raylib_cs.Color.White, pulse * 0.9f));
                break;
            }
        }
    }

    // ---------------- melee ----------------

    private static void DrawMelee(MeleeEffectStyle s, Vector2 from, Vector2 at, float t,
        float scale, float apScale, bool hits, int shot)
    {
        switch (s.Form)
        {
            case MeleeForm.Slash:  DrawSlash(s, from, at, t, scale, apScale, hits, shot); break;
            case MeleeForm.Smash:  DrawSmash(s, from, at, t, scale, apScale, hits); break;
            case MeleeForm.Thrust: DrawThrust(s, from, at, t, scale, apScale, hits); break;
            case MeleeForm.Rake:   DrawRake(s, from, at, t, scale, apScale, hits); break;
        }
    }

    // A slim blade swung from the attacking model toward the struck model: its base pivots at the
    // attacker and the tip sweeps through an arc centered on that direction. Uniform length — a
    // distant defender just gets a swing in its direction, never a stretched blade.
    private static void DrawSlash(MeleeEffectStyle s, Vector2 from, Vector2 at, float t,
        float scale, float apScale, bool hits, int shot)
    {
        float len = Math.Max(16f, scale * 0.5f) * apScale * s.Width;
        float halfWidth = len * 0.11f;

        float centerAng = MathF.Atan2(at.Y - from.Y, at.X - from.X);
        const float halfArc = 0.55f; // radians; ~±31° sweep
        float ang = centerAng - halfArc + 2f * halfArc * t;

        float pulse = 1f - Math.Abs(t - 0.5f) * 2f; // fade in then out across the swing
        float alpha = 0.4f + 0.6f * pulse;

        if (s.Afterimage) // ghost of the blade a step behind — the energy/daemon shimmer
            DrawBladeTriangle(from, ang - 0.28f, len, halfWidth, Fade(s.Blade, alpha * 0.35f), Fade(s.Edge, alpha * 0.35f));
        DrawBladeTriangle(from, ang, len, halfWidth, Fade(s.Blade, alpha), Fade(s.Edge, alpha));

        DrawSlashAccent(s, from, ang, len, alpha, t, shot);

        // Clash spark where the blade meets the target — only when this swing actually connects.
        if (hits) DrawImpactSpark(at, pulse * 0.9f, apScale, s.Blade);
    }

    private static void DrawBladeTriangle(Vector2 from, float ang, float len, float halfWidth,
        Raylib_cs.Color fill, Raylib_cs.Color edge)
    {
        var dir = new Vector2(MathF.Cos(ang), MathF.Sin(ang));
        var perp = new Vector2(-dir.Y, dir.X);
        Vector2 tip = from + dir * len;
        Vector2 baseL = from + perp * halfWidth;
        Vector2 baseR = from - perp * halfWidth;

        // Fill both windings so it shows regardless of orientation, then outline the edge.
        Raylib.DrawTriangle(tip, baseL, baseR, fill);
        Raylib.DrawTriangle(tip, baseR, baseL, fill);
        Raylib.DrawTriangleLines(tip, baseL, baseR, edge);
    }

    private static void DrawSlashAccent(MeleeEffectStyle s, Vector2 from, float ang, float len,
        float alpha, float t, int shot)
    {
        var dir = new Vector2(MathF.Cos(ang), MathF.Sin(ang));
        var perp = new Vector2(-dir.Y, dir.X);

        switch (s.Accent)
        {
            case MeleeAccent.ElectricArcs:
                // Two jagged filaments crackling along the blade.
                for (int arc = 0; arc < 2; arc++)
                {
                    Vector2 prev = from;
                    for (int k = 1; k <= 4; k++)
                    {
                        float f = k / 4f;
                        float zig = MathF.Sin(shot * 2.3f + k * 5.1f + arc * 3.7f + t * 25f) * len * 0.10f;
                        Vector2 p = from + dir * (len * f) + perp * zig;
                        Raylib.DrawLineEx(prev, p, 1.3f, Fade(s.Edge, alpha));
                        prev = p;
                    }
                }
                break;

            case MeleeAccent.Ooze:
                // Droplets slung off the tip, sagging downward as the swing proceeds.
                for (int k = 0; k < 3; k++)
                {
                    float back = 0.15f + 0.12f * k;
                    Vector2 p = from + dir * (len * (1f - back)) + new Vector2(0f, len * 0.12f * (k + 1) * t);
                    Raylib.DrawCircleV(p, 2.2f - 0.4f * k, Fade(s.Edge, alpha * (0.9f - 0.25f * k)));
                }
                break;

            case MeleeAccent.Smoke:
                // Wisps trailing the swing arc.
                for (int k = 1; k <= 3; k++)
                {
                    float trailAng = ang - 0.16f * k;
                    var tdir = new Vector2(MathF.Cos(trailAng), MathF.Sin(trailAng));
                    Raylib.DrawCircleV(from + tdir * (len * 0.8f), 3f + 1.4f * k, Fade(s.Edge, alpha * (0.4f - 0.09f * k)));
                }
                break;

            case MeleeAccent.Teeth:
                // Tick marks along the blade spine + sparks flying off the tip.
                for (int k = 1; k <= 5; k++)
                {
                    Vector2 p = from + dir * (len * k / 6f);
                    Raylib.DrawLineEx(p - perp * 2.2f, p + perp * 2.2f, 1.3f, Fade(s.Edge, alpha));
                }
                Raylib.DrawCircleV(from + dir * len, 1.8f, Fade(s.Blade, alpha * MathF.Abs(MathF.Sin(t * 20f))));
                break;
        }
    }

    // An overhead slam onto the struck model: the head drops, then the ground answers with a ring,
    // cracks, and dust. A whiffed smash still thuds down but the ground barely reacts.
    private static void DrawSmash(MeleeEffectStyle s, Vector2 from, Vector2 at, float t,
        float scale, float apScale, bool hits)
    {
        float size = Math.Max(14f, scale * 0.45f) * apScale * s.Width;
        const float landT = 0.45f;

        if (t < landT)
        {
            float drop = 1f - t / landT;
            Vector2 head = at + new Vector2(0f, -size * 1.6f * drop);
            Raylib.DrawCircleV(head, size * 0.45f, Fade(s.Blade, 0.5f + 0.5f * (1f - drop)));
            Raylib.DrawCircleLines((int)head.X, (int)head.Y, size * 0.45f + 1.5f, Fade(s.Edge, 0.9f));
            return;
        }

        float after = (t - landT) / (1f - landT);
        float fade = 1f - after;
        if (hits)
        {
            float r = size * (0.5f + 1.1f * after);
            Raylib.DrawCircleLines((int)at.X, (int)at.Y, r, Fade(s.Edge, fade));
            for (int k = 0; k < 6; k++) // radial ground cracks
            {
                float angC = k * (MathF.PI / 3f) + 0.25f;
                var d = new Vector2(MathF.Cos(angC), MathF.Sin(angC));
                Raylib.DrawLineEx(at + d * (r * 0.25f), at + d * r, 1.6f, Fade(s.Edge, fade));
            }
            Raylib.DrawCircleV(at, size * 0.4f * fade, Fade(s.Blade, fade * 0.8f));
        }
        else
        {
            // Dust puff only — the slam missed.
            Raylib.DrawCircleV(at + new Vector2(size * 0.4f, 0f), size * 0.3f * (1f + after), Fade(s.Edge, fade * 0.35f));
        }
    }

    // A straight lunge: the shaft shoots out toward the target with a bright glint at the tip.
    private static void DrawThrust(MeleeEffectStyle s, Vector2 from, Vector2 at, float t,
        float scale, float apScale, bool hits)
    {
        float len = Math.Max(18f, scale * 0.55f) * apScale * s.Width;
        Vector2 dirTo = at - from;
        Vector2 dir = dirTo.LengthSquared() > 0.0001f ? Vector2.Normalize(dirTo) : new Vector2(1f, 0f);

        // Out fast, back slow.
        float reach = t < 0.5f ? len * (t / 0.5f) : len * (1f - 0.5f * (t - 0.5f) / 0.5f);
        Vector2 tip = from + dir * reach;

        float pulse = 1f - Math.Abs(t - 0.5f) * 2f;
        Raylib.DrawLineEx(from, tip, 3f * s.Width, Fade(s.Blade, 0.5f + 0.5f * pulse));
        if (t is > 0.3f and < 0.7f)
            Raylib.DrawCircleV(tip, 2.6f, Fade(Raylib_cs.Color.White, pulse));

        if (hits && t > 0.45f) DrawImpactSpark(at, pulse * 0.9f, apScale, s.Blade);
    }

    // Three parallel claw streaks raking through the same sweep.
    private static void DrawRake(MeleeEffectStyle s, Vector2 from, Vector2 at, float t,
        float scale, float apScale, bool hits)
    {
        float len = Math.Max(16f, scale * 0.5f) * apScale * s.Width;
        float centerAng = MathF.Atan2(at.Y - from.Y, at.X - from.X);
        const float halfArc = 0.45f;
        float ang = centerAng - halfArc + 2f * halfArc * t;
        var dir = new Vector2(MathF.Cos(ang), MathF.Sin(ang));
        var perp = new Vector2(-dir.Y, dir.X);

        float pulse = 1f - Math.Abs(t - 0.5f) * 2f;
        float alpha = 0.4f + 0.6f * pulse;
        for (int k = -1; k <= 1; k++)
        {
            Vector2 offset = perp * (k * len * 0.16f);
            DrawBladeTriangle(from + offset, ang, len * (1f - 0.12f * Math.Abs(k)), len * 0.045f,
                Fade(s.Blade, alpha), Fade(s.Edge, alpha));
        }

        if (hits && pulse > 0.2f)
        {
            // Three short parallel gashes on the target.
            for (int k = -1; k <= 1; k++)
            {
                Vector2 offset = perp * (k * 4.5f);
                Raylib.DrawLineEx(at + offset - dir * 5f, at + offset + dir * 5f, 1.8f, Fade(s.Blade, pulse));
            }
        }
    }

    // ---------------- shared bits ----------------

    // A bright, fast-fading burst with a couple of cross spikes — the shot leaving or landing.
    private static void DrawMuzzleFlash(Vector2 at, float intensity, float apScale, Raylib_cs.Color color)
    {
        if (intensity <= 0f) return;
        var core = Fade(Blend(color, Raylib_cs.Color.White, 0.6f), intensity);
        var spikeC = Fade(color, intensity * 0.7f);
        float r = (5f + 3f * apScale) * (0.7f + 0.3f * intensity);
        Raylib.DrawCircleV(at, r, core);
        float spike = r * 2.4f;
        Raylib.DrawLineEx(new Vector2(at.X - spike, at.Y), new Vector2(at.X + spike, at.Y), 2f, spikeC);
        Raylib.DrawLineEx(new Vector2(at.X, at.Y - spike), new Vector2(at.X, at.Y + spike), 2f, spikeC);
    }

    // Radial shards flung out from an impact / melee clash point.
    private static void DrawImpactSpark(Vector2 at, float intensity, float apScale, Raylib_cs.Color color)
    {
        if (intensity <= 0f) return;
        var c = Fade(color, intensity);
        float reach = (5f + 5f * apScale) * (1.3f - intensity); // spreads as it fades
        for (int k = 0; k < 5; k++)
        {
            float ang = k * (MathF.PI * 2f / 5f) + 0.4f;
            var dir = new Vector2(MathF.Cos(ang), MathF.Sin(ang));
            Raylib.DrawLineEx(at + dir * (reach * 0.35f), at + dir * reach, 1.6f, c);
        }
        Raylib.DrawCircleV(at, 1.8f * apScale, c);
    }

    private static Vector2 Perp(Vector2 dir)
    {
        var perp = new Vector2(-dir.Y, dir.X);
        return perp.LengthSquared() > 0.0001f ? Vector2.Normalize(perp) : new Vector2(0f, 1f);
    }

    private static Raylib_cs.Color Fade(Raylib_cs.Color c, float alpha) =>
        new(c.R, c.G, c.B, (byte)Math.Clamp(c.A * Math.Clamp(alpha, 0f, 1f), 0f, 255f));

    private static Raylib_cs.Color Blend(Raylib_cs.Color a, Raylib_cs.Color b, float f) => new(
        (byte)(a.R + (b.R - a.R) * f), (byte)(a.G + (b.G - a.G) * f),
        (byte)(a.B + (b.B - a.B) * f), (byte)(a.A + (b.A - a.A) * f));

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
