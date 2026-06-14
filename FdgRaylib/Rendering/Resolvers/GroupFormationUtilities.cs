using FDG;

namespace FdgRaylib.Rendering.Resolvers;

/// <summary>
/// Pure geometry for group (whole-unit) placement and movement. Kept free of ImGui / table-state
/// so the math can be reasoned about and unit-tested in isolation. All positions are in table
/// inches on the x/z plane (y is the ground, always 0 here).
/// </summary>
public static class GroupFormationUtilities
{
    /// <summary>Average of the given points (x/z). Returns origin for an empty list.</summary>
    public static Position Centroid(IReadOnlyList<Position> points)
    {
        if (points.Count == 0) return new Position(0f, 0f);
        float sx = 0f, sz = 0f;
        for (int i = 0; i < points.Count; i++) { sx += points[i].x; sz += points[i].z; }
        return new Position(sx / points.Count, sz / points.Count);
    }

    /// <summary>Rotate <paramref name="p"/> about <paramref name="pivot"/> by (cos,sin), then translate by (tx,tz).</summary>
    public static Position RigidTransform(Position p, Position pivot, float cos, float sin, float tx, float tz)
    {
        float dx = p.x - pivot.x;
        float dz = p.z - pivot.z;
        float rx = dx * cos - dz * sin;
        float rz = dx * sin + dz * cos;
        return new Position(pivot.x + rx + tx, pivot.z + rz + tz);
    }

    public readonly struct GroupMoveResult
    {
        /// <summary>Final position per input model, in the same order as the inputs.</summary>
        public readonly Position[] NewPositions;
        /// <summary>Fraction of the requested translation actually applied, in [0,1].</summary>
        public readonly float TranslationScale;
        /// <summary>
        /// True when every model's straight-line travel for this step stays within its remaining
        /// budget. False means the rotation alone already pushes at least one model over its limit,
        /// so the step is distance-invalid no matter how far we pull the translation back.
        /// </summary>
        public readonly bool WithinBudget;

        public GroupMoveResult(Position[] newPositions, float translationScale, bool withinBudget)
        {
            NewPositions = newPositions;
            TranslationScale = translationScale;
            WithinBudget = withinBudget;
        }
    }

    /// <summary>
    /// Plans one rigid group step: rotate the formation about <paramref name="pivot"/> by (cos,sin),
    /// then translate by up to (desiredTx,desiredTz). The translation is scaled back uniformly so the
    /// farthest-moving model's straight-line travel exactly hits its remaining budget — keeping the
    /// formation's shape intact ("true per-model travel"). If a model is already over budget from the
    /// rotation alone, <see cref="GroupMoveResult.WithinBudget"/> is false and the caller should treat
    /// the step as invalid.
    /// </summary>
    /// <param name="lastPositions">Each model's current (last committed) position.</param>
    /// <param name="budgets">Each model's remaining move distance (inches) for this step; same order/length.</param>
    public static GroupMoveResult PlanGroupMove(
        IReadOnlyList<Position> lastPositions, IReadOnlyList<float> budgets,
        Position pivot, float cos, float sin, float desiredTx, float desiredTz)
    {
        int n = lastPositions.Count;
        // Rotation-only displacement A_i for each model, and the largest translation scale s in [0,1]
        // such that |A_i + s*T| <= budget_i for all i. Per model this is a quadratic in s:
        //   (T.T) s^2 + 2(A.T) s + (A.A - b^2) <= 0.
        float wTerm = desiredTx * desiredTx + desiredTz * desiredTz;
        const float Eps = 1e-9f;

        bool withinBudget = true;
        float scale = 1f;

        for (int i = 0; i < n; i++)
        {
            Position rotated = RigidTransform(lastPositions[i], pivot, cos, sin, 0f, 0f);
            float ax = rotated.x - lastPositions[i].x;
            float az = rotated.z - lastPositions[i].z;

            float b = MathF.Max(0f, budgets[i]);
            float c = ax * ax + az * az - b * b;

            if (c > 0f)
            {
                // Rotation alone exceeds this model's budget — no s >= 0 satisfies it.
                withinBudget = false;
                scale = 0f;
                continue;
            }
            if (wTerm < Eps) continue; // no translation requested; rotation already feasible (c <= 0)

            float b1 = 2f * (ax * desiredTx + az * desiredTz);
            float disc = b1 * b1 - 4f * wTerm * c; // >= 0 because c <= 0 and wTerm > 0
            float sHi = (-b1 + MathF.Sqrt(MathF.Max(0f, disc))) / (2f * wTerm);
            float sCap = Math.Clamp(sHi, 0f, 1f);
            if (sCap < scale) scale = sCap;
        }

        float tx = desiredTx * scale;
        float tz = desiredTz * scale;

        // Final positions: rotated about pivot, then translated by the scaled amount.
        var result = new Position[n];
        for (int i = 0; i < n; i++)
            result[i] = RigidTransform(lastPositions[i], pivot, cos, sin, tx, tz);

        return new GroupMoveResult(result, scale, withinBudget);
    }

    /// <summary>
    /// Lays a unit out for group deployment: a single horizontal row (bases ~<paramref name="gap"/>"
    /// apart) when that fits within <paramref name="maxPairwiseInches"/>, otherwise two balanced rows
    /// with the longer row on the forward side (toward table centre, per <paramref name="forwardZSign"/>).
    /// Returns each model's offset from the formation centroid in table axes (x = row direction,
    /// z = depth), before any user rotation/translation; order matches <paramref name="radii"/>.
    /// </summary>
    public static (float dx, float dz)[] ComputeDeploymentOffsets(
        IReadOnlyList<float> radii, float gap, float maxPairwiseInches, float forwardZSign)
    {
        int n = radii.Count;
        var offsets = new (float dx, float dz)[n];
        if (n == 0) return offsets;
        if (n == 1) { offsets[0] = (0f, 0f); return offsets; }

        if (RowBaseToBaseSpan(radii, 0, n, gap) <= maxPairwiseInches)
        {
            LayoutRowX(radii, 0, n, gap, 0f, offsets);
            Recenter(offsets);
            return offsets;
        }

        int frontCount = (n + 1) / 2; // longer row goes forward (toward table centre) when odd
        float frontMaxR = MaxRadius(radii, 0, frontCount);
        float backMaxR  = MaxRadius(radii, frontCount, n);
        float rowSep = frontMaxR + gap + backMaxR;
        float fz = (forwardZSign >= 0f ? 0.5f : -0.5f) * rowSep;
        LayoutRowX(radii, 0, frontCount, gap, fz, offsets);
        LayoutRowX(radii, frontCount, n, gap, -fz, offsets);
        Recenter(offsets);
        return offsets;
    }

    /// <summary>End-to-end base-to-base span of models [start,end) laid left-to-right with the given gap.</summary>
    private static float RowBaseToBaseSpan(IReadOnlyList<float> radii, int start, int end, float gap)
    {
        if (end - start <= 1) return 0f;
        float centerSpan = 0f;
        for (int i = start + 1; i < end; i++) centerSpan += radii[i - 1] + gap + radii[i];
        return centerSpan - radii[start] - radii[end - 1];
    }

    /// <summary>Lays models [start,end) along x at constant z, centred on x = 0, into <paramref name="offsets"/>.</summary>
    private static void LayoutRowX(IReadOnlyList<float> radii, int start, int end, float gap, float z,
        (float dx, float dz)[] offsets)
    {
        float x = 0f, lastX = 0f;
        for (int i = start; i < end; i++)
        {
            if (i == start) x = 0f;
            else x += radii[i - 1] + gap + radii[i];
            offsets[i] = (x, z);
            lastX = x;
        }
        float mid = lastX * 0.5f; // first centre is 0, so midpoint is lastX/2
        for (int i = start; i < end; i++) offsets[i] = (offsets[i].dx - mid, z);
    }

    private static float MaxRadius(IReadOnlyList<float> radii, int start, int end)
    {
        float m = 0f;
        for (int i = start; i < end; i++) if (radii[i] > m) m = radii[i];
        return m;
    }

    /// <summary>Shifts all offsets so the formation centroid sits at (0,0).</summary>
    private static void Recenter((float dx, float dz)[] offsets)
    {
        float sx = 0f, sz = 0f;
        for (int i = 0; i < offsets.Length; i++) { sx += offsets[i].dx; sz += offsets[i].dz; }
        float cx = sx / offsets.Length, cz = sz / offsets.Length;
        for (int i = 0; i < offsets.Length; i++) offsets[i] = (offsets[i].dx - cx, offsets[i].dz - cz);
    }
}
