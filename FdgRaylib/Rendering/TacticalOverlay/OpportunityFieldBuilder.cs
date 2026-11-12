using System.Collections.Generic;

namespace FdgRaylib.Rendering.TacticalOverlay;

/// <summary>One weapon-range band: the range, its max-blend value (higher = inner = shorter range,
/// so a texel keeps the best band any target model gives it), and the label to draw on its edge.</summary>
internal readonly record struct BandSpec(float RangeInches, byte Value, string Label);

/// <summary>A target model as the field sees it: centre (world inches) and base radius.</summary>
internal readonly record struct FieldTargetModel(float X, float Z, float Radius);

/// <summary>
/// Builds the opportunity-field band mask (spec section 5): nested filled discs of radius
/// (weapon range + shooter base radius + target base radius) around every target model, max-blended so
/// a texel's value is the best (innermost / shortest-range) band it achieves. Pure geometry -- the
/// authoritative "can I actually shoot from here" stays with the pips (RulesProbe); this is the picture.
/// </summary>
internal static class OpportunityFieldBuilder
{
    public static void Build(FieldMask mask, IReadOnlyList<FieldTargetModel> targets,
        float shooterRadius, IReadOnlyList<BandSpec> bands)
    {
        mask.Clear();
        foreach (FieldTargetModel t in targets)
            foreach (BandSpec band in bands)
                mask.RasterizeDiscMax(t.X, t.Z, band.RangeInches + shooterRadius + t.Radius, band.Value);
    }
}
