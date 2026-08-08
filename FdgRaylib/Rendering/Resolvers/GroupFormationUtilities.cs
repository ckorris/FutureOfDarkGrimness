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

    /// <summary>
    /// The one heading a group-mode step faces the whole unit along: the unit's direction of travel
    /// (centroid delta from <paramref name="from"/> to <paramref name="to"/>). A step that moves no
    /// centroid (an in-place formation morph or pure rotation) falls back to the average of
    /// <paramref name="facings"/> - for a unit whose models face every direction, any single answer
    /// is a judgement call, and the average is the one the wheel then adjusts from. Perfectly
    /// opposing facings fall back to the first.
    /// </summary>
    public static Float2 GroupHeading(IReadOnlyList<Position> from, IReadOnlyList<Position> to,
        IReadOnlyList<Float2> facings)
    {
        float dx = 0f, dz = 0f;
        for (int i = 0; i < from.Count; i++) { dx += to[i].x - from[i].x; dz += to[i].z - from[i].z; }
        float len = MathF.Sqrt(dx * dx + dz * dz);
        if (len > 1e-3f * from.Count) return new Float2(dx / len, dz / len);

        float fx = 0f, fy = 0f;
        foreach (var f in facings) { fx += f.X; fy += f.Y; }
        float flen = MathF.Sqrt(fx * fx + fy * fy);
        return flen > 1e-3f ? new Float2(fx / flen, fy / flen) : facings[0];
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
        => PlanGroupMove(lastPositions, lastPositions, budgets, pivot, cos, sin, desiredTx, desiredTz);

    /// <summary>
    /// As the 7-arg overload, but the rigid transform is applied to <paramref name="basePositions"/> (which
    /// may be a coherency-repaired shape) while each model's budget is measured against its real start in
    /// <paramref name="originPositions"/>. So a model's total straight-line travel — the cohesion correction
    /// (base − origin) plus the rigid move — is what must stay within budget. When the two arrays are the
    /// same reference the result is identical to the rigid-only path. Both arrays share the input order.
    /// </summary>
    public static GroupMoveResult PlanGroupMove(
        IReadOnlyList<Position> basePositions, IReadOnlyList<Position> originPositions,
        IReadOnlyList<float> budgets,
        Position pivot, float cos, float sin, float desiredTx, float desiredTz)
    {
        int n = basePositions.Count;
        // Combined rotation+repair displacement A_i = rotate(base_i) - origin_i, and the largest translation
        // scale s in [0,1] such that |A_i + s*T| <= budget_i for all i. Per model this is a quadratic in s:
        //   (T.T) s^2 + 2(A.T) s + (A.A - b^2) <= 0.
        float wTerm = desiredTx * desiredTx + desiredTz * desiredTz;
        const float Eps = 1e-9f;

        bool withinBudget = true;
        float scale = 1f;

        for (int i = 0; i < n; i++)
        {
            Position rotated = RigidTransform(basePositions[i], pivot, cos, sin, 0f, 0f);
            float ax = rotated.x - originPositions[i].x;
            float az = rotated.z - originPositions[i].z;

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
            result[i] = RigidTransform(basePositions[i], pivot, cos, sin, tx, tz);

        return new GroupMoveResult(result, scale, withinBudget);
    }

    /// <summary>
    /// Largest s in [0,1] for which <paramref name="feasibleAt"/> holds, found by bisection (#155).
    /// Assumes s = 0 is feasible (the caller checks that first). Used to pull a group translation back
    /// until every model's step clears a constraint the closed-form budget solve can't express — e.g.
    /// the difficult-terrain entry clamp.
    /// </summary>
    public static float LargestFeasibleScale(Func<float, bool> feasibleAt, float tolerance = 0.004f)
    {
        if (feasibleAt(1f)) return 1f;
        float lo = 0f, hi = 1f;
        while (hi - lo > tolerance)
        {
            float mid = (lo + hi) * 0.5f;
            if (feasibleAt(mid)) lo = mid;
            else hi = mid;
        }
        return lo;
    }

    // The repair targets a hair inside the limits so the caller's exact-constant cohesion check (no margin)
    // reliably agrees the repaired shape is legal.
    private const float CoherencyRepairMargin = 0.02f;
    private const float RelaxDamping  = 0.5f;  // fraction of each step's correction applied per iteration
    private const int   RelaxMaxIters = 600;

    /// <summary>
    /// Returns coherency-repaired positions by iterative relaxation: every iteration, each link that is too
    /// long pulls BOTH of its endpoints toward each other (the 1" nearest-neighbour rule per model, plus the
    /// 9" rule on the farthest pair). The correction ripples down the chain, so the displacement needed to
    /// reabsorb a straggler is SHARED across several models instead of dumped on one — which minimises the
    /// largest single move and therefore preserves the most group-drag distance (the group step is bottlenecked
    /// by whichever model spent the most of its budget repairing). Models that are already in cohesion barely
    /// move; the motion grades down with distance from the break. A light separation sweep keeps bases from
    /// stacking. Order matches the inputs; operates on the x/z plane. An already-coherent unit is returned
    /// unchanged (the loop exits on the first check).
    /// </summary>
    public static Position[] RepairCoherencyByContraction(
        IReadOnlyList<Position> positions, IReadOnlyList<float> radii,
        float maxNearestB2B, float maxFarthestB2B)
    {
        int n = positions.Count;
        var work = new Position[n];
        for (int i = 0; i < n; i++) work[i] = positions[i];
        if (n <= 1) return work;

        float nearTarget = maxNearestB2B - CoherencyRepairMargin;
        float farTarget  = maxFarthestB2B - CoherencyRepairMargin;

        for (int iter = 0; iter < RelaxMaxIters; iter++)
        {
            if (IsCoherent(work, radii, nearTarget, farTarget)) break;

            var dx = new float[n];
            var dz = new float[n];

            // 1" nearest-neighbour rule: each over-long link shares its excess 50/50 between both ends.
            for (int i = 0; i < n; i++)
            {
                int j = NearestOther(work, radii, i);
                if (j < 0) continue;
                float b2b = Position.GetDistance2D(work[i], work[j]) - radii[i] - radii[j];
                if (b2b <= nearTarget) continue;
                AddPull(dx, dz, work, i, j, 0.5f * (b2b - nearTarget));
            }

            // 9" all-pairs rule: pull the farthest pair together (again shared between both ends).
            var (a, b, far) = FarthestPair(work, radii);
            if (far > farTarget)
                AddPull(dx, dz, work, a, b, 0.5f * (far - farTarget));

            for (int i = 0; i < n; i++)
                work[i] = new Position(work[i].x + RelaxDamping * dx[i], work[i].z + RelaxDamping * dz[i]);

            SeparateOverlaps(work, radii); // keep bases from stacking as they gather
        }
        return work;
    }

    /// <summary>Adds, to the displacement accumulators, a pull of <paramref name="amount"/>" that moves
    /// model <paramref name="i"/> toward model <paramref name="j"/> and an equal-and-opposite pull moving
    /// <paramref name="j"/> toward <paramref name="i"/> — sharing a link's correction between both ends.</summary>
    private static void AddPull(float[] dx, float[] dz, Position[] pos, int i, int j, float amount)
    {
        float vx = pos[j].x - pos[i].x, vz = pos[j].z - pos[i].z;
        float len = MathF.Sqrt(vx * vx + vz * vz);
        if (len < 1e-6f) return;
        float ux = vx / len, uz = vz / len;
        dx[i] += amount * ux; dz[i] += amount * uz;
        dx[j] -= amount * ux; dz[j] -= amount * uz;
    }

    /// <summary>True when every model is within <paramref name="nearTarget"/>" of its nearest neighbour and
    /// no pair exceeds <paramref name="farTarget"/>" (all base-to-base).</summary>
    private static bool IsCoherent(Position[] pos, IReadOnlyList<float> radii, float nearTarget, float farTarget)
    {
        int n = pos.Length;
        for (int i = 0; i < n; i++)
        {
            int j = NearestOther(pos, radii, i);
            if (j < 0) continue;
            if (Position.GetDistance2D(pos[i], pos[j]) - radii[i] - radii[j] > nearTarget) return false;
        }
        return FarthestPair(pos, radii).far <= farTarget;
    }

    /// <summary>Index of the model nearest <paramref name="i"/> (base-to-base), or -1 if it is the only one.</summary>
    private static int NearestOther(Position[] pos, IReadOnlyList<float> radii, int i)
    {
        int best = -1; float nearest = float.PositiveInfinity;
        for (int j = 0; j < pos.Length; j++)
        {
            if (j == i) continue;
            float b2b = Position.GetDistance2D(pos[i], pos[j]) - radii[i] - radii[j];
            if (b2b < nearest) { nearest = b2b; best = j; }
        }
        return best;
    }

    /// <summary>The pair of models farthest apart (base-to-base), as (indexA, indexB, b2b).</summary>
    private static (int a, int b, float far) FarthestPair(Position[] pos, IReadOnlyList<float> radii)
    {
        int ba = 0, bb = 0; float far = float.NegativeInfinity;
        for (int i = 0; i < pos.Length; i++)
            for (int j = i + 1; j < pos.Length; j++)
            {
                float b2b = Position.GetDistance2D(pos[i], pos[j]) - radii[i] - radii[j];
                if (b2b > far) { far = b2b; ba = i; bb = j; }
            }
        return (ba, bb, far);
    }

    /// <summary>One sweep that pushes any overlapping pair (base-to-base &lt; 0) apart by half the penetration
    /// each. Bases touching (0") is legal, so this only fires on genuine overlap; combined with the gather it
    /// keeps the relaxed formation overlap-free.</summary>
    private static void SeparateOverlaps(Position[] pos, IReadOnlyList<float> radii)
    {
        int n = pos.Length;
        for (int i = 0; i < n; i++)
            for (int j = i + 1; j < n; j++)
            {
                float vx = pos[j].x - pos[i].x, vz = pos[j].z - pos[i].z;
                float len = MathF.Sqrt(vx * vx + vz * vz);
                float minDist = radii[i] + radii[j];
                if (len >= minDist) continue;
                float pen = minDist - len;
                float ux, uz;
                if (len < 1e-6f) { ux = 1f; uz = 0f; }    // coincident — separate along an arbitrary axis
                else             { ux = vx / len; uz = vz / len; }
                float half = 0.5f * pen;
                pos[i] = new Position(pos[i].x - half * ux, pos[i].z - half * uz);
                pos[j] = new Position(pos[j].x + half * ux, pos[j].z + half * uz);
            }
    }

    // #277: deployment row layout moved to the engine's FormationLibrary (shared with PackGrid and the
    // formation cycle), replacing the old ComputeDeploymentOffsets here.
}
