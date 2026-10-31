using System;
using FDG.Presentation.Beats;

namespace FdgRaylib.Rendering.Presentation;

/// <summary>
/// Pure math for #239's truthful attack animation: which of an <see cref="AttackBeat"/>'s visual
/// shots connect. The beat carries the semantic totals (natural hits / attacks rolled); the visual
/// shot grid is shooters x volleys, and the hit share is scaled onto it and spread evenly
/// (Bresenham-style), deterministically — the overlay (impact visuals) and the sound cues (impact
/// sounds per volley) must agree shot-for-shot, so both consume this one plan.
/// </summary>
public static class AttackShotPlan
{
    /// <summary>The visual shot grid: every firing model shoots once per volley.</summary>
    public static int TotalShots(int shooterCount, int volleyCount) =>
        Math.Max(1, shooterCount) * Math.Max(1, volleyCount);

    /// <summary>
    /// How many visual shots should land. The true hit fraction (hits/attacks) is scaled onto the
    /// visual grid and rounded; any real hit shows at least one impact (fractional Realistic-mode
    /// dice can round to zero while wounds still land). AttackCount &lt;= 0 means the beat predates
    /// #239 (or came from a path with no roll) — legacy behavior, everything connects.
    /// </summary>
    public static int VisualHits(float hitCount, float attackCount, int totalShots)
    {
        if (attackCount <= 0f) return totalShots;
        if (hitCount <= 0.01f) return 0;

        float fraction = Math.Clamp(hitCount / attackCount, 0f, 1f);
        int hits = (int)MathF.Round(fraction * totalShots);
        return Math.Clamp(hits, 1, totalShots);
    }

    /// <summary>Shots are indexed volley-major: volley v, shooter i -> v * shooters + i.</summary>
    public static int ShotIndex(int volley, int shooter, int shooterCount) =>
        volley * Math.Max(1, shooterCount) + shooter;

    /// <summary>
    /// Whether a shot connects. Exactly <paramref name="visualHits"/> of the
    /// <paramref name="totalShots"/> indices return true, spread as evenly as possible
    /// (index s hits iff floor((s+1)h/n) &gt; floor(sh/n)) — so hits and misses interleave across
    /// volleys and shooters instead of front-loading.
    /// </summary>
    public static bool ShotHits(int shotIndex, int totalShots, int visualHits)
    {
        if (visualHits <= 0 || totalShots <= 0) return false;
        if (visualHits >= totalShots) return true;
        return (long)(shotIndex + 1) * visualHits / totalShots
             > (long)shotIndex * visualHits / totalShots;
    }

    /// <summary>Whether any shot of the volley connects — drives the per-volley impact sound.</summary>
    public static bool VolleyHasHit(int volley, int shooterCount, int volleyCount, int visualHits)
    {
        int shooters = Math.Max(1, shooterCount);
        int total = TotalShots(shooterCount, volleyCount);
        for (int i = 0; i < shooters; i++)
        {
            if (ShotHits(ShotIndex(volley, i, shooters), total, visualHits)) return true;
        }
        return false;
    }

    /// <summary>Convenience: the visual hit count for a beat's own grid.</summary>
    public static int VisualHits(AttackBeat beat) =>
        VisualHits(beat.HitCount, beat.AttackCount,
            TotalShots(beat.From.Count, Math.Max(1, beat.VolleyCount)));

    /// <summary>Whether the attack lands anything at all — gates the melee hit-stop and impact cues.</summary>
    public static bool HasAnyHit(AttackBeat beat) =>
        beat.AttackCount <= 0f || beat.HitCount > 0.01f;
}
