using FDG;
using FDG.Data;

namespace FdgRaylib.Placement;

/// <summary>
/// #269 — whole-formation cohesion for a reposition-style placement (Teleport, Fanatic, reposition-at-
/// activation), checked once against the FINAL set of positions rather than per click.
///
/// <para>The placement resolvers used to gate each click on "within 1in base-to-base of an already-placed
/// model". That is far stricter than the rule it was standing in for: it forces the unit to be rebuilt in
/// the request's list order, so model 2 is confined to a 1in band around wherever model 1 landed no matter
/// how much of its own reach ring is free. Cohesion is a property of the finished formation, not of the
/// order you happen to place it in - so it belongs on the Done gate.</para>
///
/// <para>The test is the engine's "not worsened" rule, mirroring
/// <c>MovementUtilities.ValidateCoherencyNotWorsened</c> (#159): each model must end within
/// <see cref="GameWideConstants.MAX_MODEL_DISTANCE_FROM_ANY_OTHER_MODEL_INCHES"/> of its nearest
/// team-mate and <see cref="GameWideConstants.MAX_MODEL_DISTANCE_FROM_ALL_OTHER_MODELS_INCHES"/> of its
/// farthest - OR no worse on each count than it already was. Without the lenient half, a unit that
/// casualties had already scattered could never accept ANY placement, including standing still, which
/// would trap the prompt.</para>
///
/// Display-independent so it is testable without ImGui, following the <c>ReachRingPlan</c> /
/// <c>MeasurementGeometry</c> precedent; the resolvers keep the prompting.
/// </summary>
internal static class PlacementCohesion
{
    /// <summary>Matches the engine movement validator's cohesion tolerance - a placement resolved through
    /// pixel-to-inch conversion lands a hair off an exact boundary.</summary>
    internal const float EpsilonInches = 0.01f;

    /// <summary>One model's footprint at some moment: where it is, its base shape, and its facing.</summary>
    internal readonly record struct Footprint(Position Position, IBaseShape Shape, Float2 Facing);

    /// <summary>
    /// What a finished placement does to the unit's cohesion. <paramref name="StrandedCount"/> models have
    /// no team-mate within the nearest-neighbour limit; <paramref name="ScatteredCount"/> exceed the
    /// all-pairs limit. <paramref name="WorstNearestGapInches"/> is the largest offending
    /// nearest-neighbour gap, for the readout ("...nearest team-mate is 3.4in away").
    /// </summary>
    internal readonly record struct Report(int StrandedCount, int ScatteredCount, float WorstNearestGapInches)
    {
        internal bool IsAcceptable => StrandedCount == 0 && ScatteredCount == 0;
    }

    /// <summary>
    /// Evaluates the placement. <paramref name="before"/> and <paramref name="after"/> are index-aligned:
    /// entry i is the same model where it started and where it would end. A unit of one model (or a
    /// mismatched pair of lists, which callers should not produce) is vacuously acceptable.
    /// </summary>
    internal static Report Evaluate(IReadOnlyList<Footprint> before, IReadOnlyList<Footprint> after)
    {
        int count = after.Count;
        if (count <= 1 || before.Count != count) return new Report(0, 0, 0f);

        ComputeExtents(before, out float[] nearestBefore, out float[] farthestBefore);
        ComputeExtents(after, out float[] nearestAfter, out float[] farthestAfter);

        int stranded = 0, scattered = 0;
        float worstNearest = 0f;

        for (int i = 0; i < count; i++)
        {
            // "Not worsened": the limit is the rule's, or this model's own starting gap if that was already
            // wider. Re-forming moves that shrink the gaps stay legal; scattering further does not.
            float nearestLimit = MathF.Max(GameWideConstants.MAX_MODEL_DISTANCE_FROM_ANY_OTHER_MODEL_INCHES,
                nearestBefore[i]) + EpsilonInches;
            if (nearestAfter[i] > nearestLimit)
            {
                stranded++;
                worstNearest = MathF.Max(worstNearest, nearestAfter[i]);
            }

            float farthestLimit = MathF.Max(GameWideConstants.MAX_MODEL_DISTANCE_FROM_ALL_OTHER_MODELS_INCHES,
                farthestBefore[i]) + EpsilonInches;
            if (farthestAfter[i] > farthestLimit) scattered++;
        }

        return new Report(stranded, scattered, worstNearest);
    }

    /// <summary>A one-line description of what is wrong, or null when the placement is acceptable.</summary>
    internal static string? Describe(Report report)
    {
        if (report.IsAcceptable) return null;

        if (report.StrandedCount > 0)
        {
            string models = report.StrandedCount == 1 ? "1 model is" : $"{report.StrandedCount} models are";
            return $"{models} out of cohesion - nearest team-mate is {report.WorstNearestGapInches:F1}\" away " +
                   $"(max {GameWideConstants.MAX_MODEL_DISTANCE_FROM_ANY_OTHER_MODEL_INCHES:F0}\").";
        }

        string scattered = report.ScatteredCount == 1 ? "1 model is" : $"{report.ScatteredCount} models are";
        return $"{scattered} too far from the rest of the unit " +
               $"(max {GameWideConstants.MAX_MODEL_DISTANCE_FROM_ALL_OTHER_MODELS_INCHES:F0}\" between any two).";
    }

    // Per-model nearest and farthest base-to-base gaps at the given footprints, index-aligned with the input.
    // Shape- and facing-aware (#150) so the readout matches the engine's own coherency measurement.
    private static void ComputeExtents(IReadOnlyList<Footprint> models, out float[] nearest, out float[] farthest)
    {
        int count = models.Count;
        nearest = new float[count];
        farthest = new float[count];
        for (int i = 0; i < count; i++)
        {
            nearest[i] = float.PositiveInfinity;
            farthest[i] = float.NegativeInfinity;
        }

        for (int i = 0; i < count; i++)
        {
            for (int j = i + 1; j < count; j++)
            {
                float gap = BaseShapeGeometry.SurfaceGap2D(
                    models[i].Shape, models[i].Position, models[i].Facing,
                    models[j].Shape, models[j].Position, models[j].Facing);

                nearest[i] = MathF.Min(gap, nearest[i]);
                nearest[j] = MathF.Min(gap, nearest[j]);
                farthest[i] = MathF.Max(gap, farthest[i]);
                farthest[j] = MathF.Max(gap, farthest[j]);
            }
        }
    }
}
