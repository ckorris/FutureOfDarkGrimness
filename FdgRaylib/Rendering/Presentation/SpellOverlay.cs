using System;
using System.Collections.Generic;
using System.Numerics;
using FDG;
using FDG.Presentation.Beats;
using Raylib_cs;

namespace FdgRaylib.Rendering.Presentation;

/// <summary>
/// #274 — draws the active <see cref="SpellEffectBeat"/> in world space (over the table). Six
/// variants, each a distinct read at a glance:
///
/// <list type="bullet">
///   <item><b>CastSuccess</b> — a bright rune ring blooming outward off the caster with motes rising
///         out of it. Energy leaving the model.</item>
///   <item><b>CastFailure</b> — the same ring COLLAPSING inward and going out, with sparks that fall
///         and die. Energy failing to leave the model, deliberately the visual inverse.</item>
///   <item><b>TargetBoon</b> — a warm green swell rising up each target with a halo settling over it.</item>
///   <item><b>TargetBane</b> — a violet claw of inward-driving spikes, snapping shut on each target.</item>
///   <item><b>AssistBoost</b> — blue streams flowing from each helper into the caster, ending in a
///         reinforcing pulse. Beat magnitude (tokens spent) thickens them.</item>
///   <item><b>AssistHinder</b> — the same in orange, but jagged and pulling AWAY from the caster:
///         the same channel, read as interference.</item>
/// </list>
///
/// <para>
/// Positions are staggered deterministically (by index, no RNG) so a multi-model unit ripples rather
/// than flashing as one block, and everything stays inside the beat's own envelope — the engine
/// paces the beat, this only sub-divides it. Inch-&gt;pixel matches the model renderer.
/// </para>
/// </summary>
public static class SpellOverlay
{
    // Success/failure read against each other; boon/bane read against each other; assist colors match
    // the cast-assist banners already used for the same events (blue helps, orange interferes).
    private static readonly Color SuccessCore = new(190, 230, 255, 255);
    private static readonly Color SuccessGlow = new(80,  150, 255, 255);
    private static readonly Color FailCore    = new(220, 70,  60,  255);
    private static readonly Color FailGlow    = new(110, 60,  70,  255);
    private static readonly Color BoonCore    = new(180, 255, 200, 255);
    private static readonly Color BoonGlow    = new(70,  210, 120, 255);
    private static readonly Color BaneCore    = new(225, 150, 255, 255);
    private static readonly Color BaneGlow    = new(150, 50,  210, 255);
    private static readonly Color BoostCore   = new(77,  153, 255, 255);
    private static readonly Color HinderCore  = new(255, 153, 38,  255);

    // How much of the beat one position's own animation occupies. The rest is stagger room, so the
    // last model in a unit still finishes before the beat does.
    private const float PerPositionSpan = 0.75f;

    public static void Draw(SpellEffectBeat beat, float progress, float scale, int originX, int originY,
        float tableH)
    {
        if (beat.Positions.Count == 0) return;

        // Assist streams run on the whole beat's clock (they flow between units, not at one model),
        // so they are drawn once rather than per staggered position.
        if (beat.Visual is ESpellVisual.AssistBoost or ESpellVisual.AssistHinder)
        {
            DrawAssist(beat, progress, scale, originX, originY, tableH);
            return;
        }

        for (int i = 0; i < beat.Positions.Count; i++)
        {
            float t = Staggered(progress, i, beat.Positions.Count);
            if (t <= 0f) continue;

            Vector2 at = ToPixel(beat.Positions[i], scale, originX, originY, tableH);
            float unit = Math.Max(6f, scale * 0.5f); // one "model radius" of screen space

            switch (beat.Visual)
            {
                case ESpellVisual.CastSuccess: DrawCastSuccess(at, t, unit, i); break;
                case ESpellVisual.CastFailure: DrawCastFailure(at, t, unit, i); break;
                case ESpellVisual.TargetBoon:  DrawBoon(at, t, unit, i);        break;
                case ESpellVisual.TargetBane:  DrawBane(at, t, unit, i);        break;
            }
        }
    }

    /// <summary>
    /// Position <paramref name="i"/>'s own 0..1 progress: each starts slightly later than the last so
    /// a unit ripples. Returns &lt;= 0 before this position's slice opens. Internal for tests.
    /// </summary>
    internal static float Staggered(float progress, int i, int count)
    {
        if (count <= 1) return Math.Clamp(progress, 0f, 1f);
        float lead = (1f - PerPositionSpan) * i / (count - 1);
        return Math.Clamp((progress - lead) / PerPositionSpan, 0f, 1f);
    }

    // Energy leaving the model: a ring blooming outward, brightest at the start, with motes rising
    // out of it as it goes.
    private static void DrawCastSuccess(Vector2 at, float t, float unit, int seed)
    {
        float fade = 1f - t * t;                       // holds bright, then drops off
        float r = unit * (0.35f + 2.2f * Ease(t));

        Raylib.DrawCircleLines((int)at.X, (int)at.Y, r, Fade(SuccessCore, fade));
        Raylib.DrawCircleLines((int)at.X, (int)at.Y, r * 0.72f, Fade(SuccessGlow, fade * 0.8f));
        Raylib.DrawCircleV(at, unit * 0.5f * (1f - t), Fade(SuccessCore, fade * 0.55f));

        // Six motes rising and spreading out of the ring.
        for (int k = 0; k < 6; k++)
        {
            float ang = MathF.Tau * k / 6f + seed * 0.7f;
            float reach = r * (0.6f + 0.5f * t);
            var p = new Vector2(at.X + MathF.Cos(ang) * reach,
                                at.Y + MathF.Sin(ang) * reach - unit * 1.4f * t);
            Raylib.DrawCircleV(p, Math.Max(1f, unit * 0.16f * (1f - t)), Fade(SuccessCore, fade));
        }
    }

    // The deliberate inverse: the ring collapses INWARD and snuffs out, sparks fall instead of rise.
    private static void DrawCastFailure(Vector2 at, float t, float unit, int seed)
    {
        float fade = 1f - t;
        float r = unit * (2.0f - 1.7f * Ease(t));      // starts wide, chokes down to nothing

        Raylib.DrawCircleLines((int)at.X, (int)at.Y, r, Fade(FailCore, fade));
        Raylib.DrawCircleLines((int)at.X, (int)at.Y, r * 1.25f, Fade(FailGlow, fade * 0.5f));

        // Guttering sparks: drift outward a little, then fall.
        for (int k = 0; k < 5; k++)
        {
            float ang = MathF.Tau * k / 5f + seed * 1.3f;
            float reach = unit * (0.9f + 0.35f * t);
            var p = new Vector2(at.X + MathF.Cos(ang) * reach,
                                at.Y + MathF.Sin(ang) * reach + unit * 1.6f * t * t);
            Raylib.DrawCircleV(p, Math.Max(1f, unit * 0.13f * fade), Fade(FailCore, fade * 0.9f));
        }
    }

    // A warm swell rising up the model, settling into a halo that sits over it.
    private static void DrawBoon(Vector2 at, float t, float unit, int seed)
    {
        float fade = t < 0.7f ? 1f : 1f - (t - 0.7f) / 0.3f;

        // Halo settling down onto the model from above.
        float haloY = at.Y - unit * 1.5f * (1f - Ease(t));
        Raylib.DrawCircleLines((int)at.X, (int)haloY, unit * 0.9f, Fade(BoonCore, fade));
        Raylib.DrawCircleLines((int)at.X, (int)haloY, unit * 0.7f, Fade(BoonGlow, fade * 0.7f));

        // Updrafting motes.
        for (int k = 0; k < 5; k++)
        {
            float phase = Frac(t + k * 0.2f + seed * 0.11f);
            float sway = MathF.Sin(phase * MathF.Tau + k * 1.7f + seed) * unit * 0.35f;
            var p = new Vector2(at.X + sway, at.Y + unit * 0.8f - unit * 2.2f * phase);
            Raylib.DrawCircleV(p, Math.Max(1f, unit * 0.15f * (1f - phase)), Fade(BoonCore, fade * (1f - phase)));
        }
    }

    // Spikes driving inward and snapping shut on the model.
    private static void DrawBane(Vector2 at, float t, float unit, int seed)
    {
        float fade = 1f - t * t;
        float reach = unit * (2.4f - 2.0f * Ease(t)); // claw closing

        for (int k = 0; k < 6; k++)
        {
            float ang = MathF.Tau * k / 6f + seed * 0.9f + t * 0.6f; // slight twist as it closes
            var dir = new Vector2(MathF.Cos(ang), MathF.Sin(ang));
            Raylib.DrawLineEx(at + dir * reach, at + dir * (reach * 0.35f), 2f, Fade(BaneCore, fade));
        }

        // The bruise left behind, brightest as the spikes meet.
        Raylib.DrawCircleV(at, unit * 0.75f * Ease(t), Fade(BaneGlow, fade * 0.45f));
        Raylib.DrawCircleLines((int)at.X, (int)at.Y, unit * (0.3f + 0.9f * t), Fade(BaneCore, fade * 0.6f));
    }

    // Tokens spent to sway the roll: streams between the spending unit and the caster, plus a pulse on
    // the caster. A boost flows IN and reinforces; a hinder flows the same channel but jagged and
    // pulling away. With no source unit (a pure self-boost) only the pulse plays.
    private static void DrawAssist(SpellEffectBeat beat, float progress, float scale, int originX,
        int originY, float tableH)
    {
        bool boost = beat.Visual == ESpellVisual.AssistBoost;
        Color core = boost ? BoostCore : HinderCore;
        float fade = progress < 0.75f ? 1f : 1f - (progress - 0.75f) / 0.25f;
        float unit = Math.Max(6f, scale * 0.5f);
        // More tokens spent = a heavier channel. Capped so a big pool doesn't swamp the table.
        float weight = Math.Min(3f, 1f + 0.5f * Math.Max(0, beat.Magnitude - 1));

        Vector2 caster = Centroid(beat.Positions, scale, originX, originY, tableH);

        for (int i = 0; i < beat.Sources.Count; i++)
        {
            Vector2 from = ToPixel(beat.Sources[i], scale, originX, originY, tableH);
            Vector2 delta = caster - from;
            float len = delta.Length();
            if (len < 1f) continue;                     // co-located: nothing to draw a stream along
            Vector2 dir = delta / len;
            Vector2 perp = new Vector2(-dir.Y, dir.X);

            // Faint full channel, then a packet travelling it. A boost runs source->caster; a hinder
            // runs the other way, so the direction alone says which it is.
            Raylib.DrawLineEx(from, caster, weight, Fade(core, 0.22f * fade));

            float head = Frac(progress * 1.6f + i * 0.17f);
            float along = boost ? head : 1f - head;
            for (int k = 0; k < 7; k++)
            {
                float f = along + (boost ? -k : k) * 0.045f;
                if (f < 0f || f > 1f) continue;
                // A hinder wobbles; a boost runs clean.
                float wobble = boost ? 0f
                    : MathF.Sin(f * 22f + i * 2.3f + progress * 9f) * unit * 0.30f;
                Vector2 p = from + dir * (len * f) + perp * wobble;
                float a = fade * (1f - k / 7f);
                Raylib.DrawCircleV(p, Math.Max(1f, unit * 0.17f * weight * (1f - k / 9f)), Fade(core, a));
            }
        }

        // The pulse on the caster: what all of this is actually doing to the roll.
        float pulse = 0.5f + 0.5f * MathF.Sin(progress * MathF.Tau * 2f);
        float r = unit * (boost ? 1.0f + 0.5f * pulse : 1.4f - 0.4f * pulse);
        Raylib.DrawCircleLines((int)caster.X, (int)caster.Y, r, Fade(core, fade));
        Raylib.DrawCircleLines((int)caster.X, (int)caster.Y, r * 0.6f, Fade(core, fade * 0.6f));
    }

    private static Vector2 Centroid(IReadOnlyList<Position> positions, float scale, int originX,
        int originY, float tableH)
    {
        float x = 0f, z = 0f;
        foreach (Position p in positions) { x += p.x; z += p.z; }
        return ToPixel(new Position(x / positions.Count, z / positions.Count), scale, originX, originY, tableH);
    }

    // Fast-out easing: the interesting part of every one of these effects is the first third.
    private static float Ease(float t) => 1f - (1f - t) * (1f - t);

    private static float Frac(float v) => v - MathF.Floor(v);

    private static Color Fade(Color c, float alpha) =>
        new(c.R, c.G, c.B, (byte)Math.Clamp(c.A * Math.Clamp(alpha, 0f, 1f), 0f, 255f));

    private static Vector2 ToPixel(Position pos, float scale, int originX, int originY, float tableH) =>
        new(originX + pos.x * scale, originY + (tableH - pos.z) * scale);
}
