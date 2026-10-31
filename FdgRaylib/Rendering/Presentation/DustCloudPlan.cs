using System;
using System.Collections.Generic;

namespace FdgRaylib.Rendering.Presentation;

/// <summary>
/// #399 - the geometry of the dust cloud that marks a unit arriving from reserve, as pure arithmetic:
/// where each puff sits, how big it is and how solid, for a given 0..1 progress. <see cref="DustOverlay"/>
/// turns that into Raylib circles. Split out for the same reason <c>AttackShotPlan</c> and
/// <c>DifficultShortfallPlan</c> are: the shape of an animation is worth pinning, and a drawing call is
/// not testable.
///
/// <para>Everything is derived from the puff INDEX, never from an RNG. Two players watching the same
/// ambush land on two machines must see the same cloud - the beat crosses the wire, so the visual has to
/// be a function of it - and a cloud that reshuffles every frame boils instead of billowing.</para>
///
/// <para>Inches throughout, measured from the arriving model's centre; the overlay scales to pixels.</para>
/// </summary>
internal static class DustCloudPlan
{
    /// <summary>How many puffs make up one model's cloud. Enough to read as a cloud, few enough that a
    /// ten-model unit is not a wall of circles.</summary>
    internal const int PuffCount = 7;

    /// <summary>Fraction of the envelope a puff spends growing before it starts thinning out.</summary>
    private const float RiseFraction = 0.3f;

    /// <summary>Puff radius at full bloom, in model radii - a cloud a bit wider than the base it hides.</summary>
    private const float PuffRadiusInModelRadii = 0.62f;

    /// <summary>How far the puffs have drifted out from the centre at the end, in model radii.</summary>
    private const float DriftInModelRadii = 1.35f;

    /// <summary>The ground ring's final radius, in model radii - the kick of dust the landing throws out.</summary>
    private const float RingRadiusInModelRadii = 2.1f;

    /// <summary>The ring is the impact, so it is over well before the cloud is.</summary>
    private const float RingSpan = 0.45f;

    /// <summary>One puff of one model's cloud: where it is, how big, how solid.</summary>
    internal readonly record struct Puff(float OffsetXInches, float OffsetZInches, float RadiusInches,
        float Alpha);

    /// <summary>
    /// The cloud around ONE arriving model at <paramref name="t"/> (0..1).
    /// <paramref name="modelRadiusInches"/> scales the whole thing so a big base throws a big cloud.
    /// <paramref name="seed"/> is the model's index in the unit: it rotates the puff ring so two
    /// neighbouring models do not produce the identical figure side by side.
    /// Returns an empty list outside the envelope rather than invisible puffs.
    /// </summary>
    internal static IReadOnlyList<Puff> Puffs(float t, float modelRadiusInches, int seed)
    {
        if (t <= 0f || t >= 1f) return Array.Empty<Puff>();

        var puffs = new List<Puff>(PuffCount);
        // A quarter turn per model index, wrapped - a cheap decorrelation that stays deterministic.
        float phase = seed * (MathF.PI * 0.5f) / PuffCount;

        for (int i = 0; i < PuffCount; i++)
        {
            // Each puff runs its own slightly later clock, so the cloud unfurls instead of pulsing as
            // one ring. The last one still finishes inside the envelope.
            float lead = i / (float)PuffCount * (1f - RiseFraction) * 0.5f;
            float pt = Normalize(t - lead, 1f - lead);
            if (pt <= 0f) continue;

            float angle = phase + i * (MathF.Tau / PuffCount);
            float drift = modelRadiusInches * DriftInModelRadii * Ease(pt);
            // Alternate puffs sit nearer the centre, so the cloud has a body rather than being a ring.
            float reach = (i % 2 == 0) ? drift : drift * 0.55f;

            puffs.Add(new Puff(
                MathF.Cos(angle) * reach,
                MathF.Sin(angle) * reach,
                modelRadiusInches * PuffRadiusInModelRadii * (0.35f + 0.65f * Ease(pt)),
                Alpha(pt)));
        }

        return puffs;
    }

    /// <summary>The ground ring's radius at <paramref name="t"/>; 0 once the ring is over.</summary>
    internal static float RingRadiusInches(float t, float modelRadiusInches)
    {
        float rt = Normalize(t, RingSpan);
        return rt <= 0f ? 0f : modelRadiusInches * RingRadiusInModelRadii * Ease(rt);
    }

    /// <summary>The ground ring's opacity at <paramref name="t"/>: full at the landing, gone by RingSpan.</summary>
    internal static float RingAlpha(float t)
    {
        float rt = Normalize(t, RingSpan);
        return rt <= 0f ? 0f : 1f - rt;
    }

    /// <summary>
    /// Where model <paramref name="index"/> of <paramref name="count"/> is in its own animation when
    /// the beat is <paramref name="progress"/> through. The unit lands as one - they were all placed in
    /// the same instant - but a few frames of stagger keeps a rank from flashing as a single block.
    /// </summary>
    internal static float Staggered(float progress, int index, int count)
    {
        if (count <= 1) return progress;
        // Only a fifth of the envelope is stagger room, so even the last model's cloud completes.
        float lead = index / (float)count * 0.2f;
        return Normalize(progress - lead, 1f - lead);
    }

    /// <summary>Rise fast, thin out slowly - dust, not a flashbulb. 0 outside the envelope.</summary>
    private static float Alpha(float t)
    {
        if (t <= 0f || t >= 1f) return 0f;
        return t < RiseFraction
            ? t / RiseFraction
            : 1f - (t - RiseFraction) / (1f - RiseFraction);
    }

    // Decelerating ease: the cloud throws out fast and then settles, which is what makes it read as
    // something displaced rather than something inflating.
    private static float Ease(float t) => 1f - (1f - t) * (1f - t);

    private static float Normalize(float value, float span)
        => span <= 0f ? 0f : Math.Clamp(value / span, 0f, 1f);
}
