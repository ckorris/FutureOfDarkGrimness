using System;
using System.Collections.Generic;
using System.Linq;
using FDG;

namespace FdgRaylib.Rendering.Resolvers;

/// <summary>
/// #409 - "turn the unit and re-form it", the answer to a group step that cannot be made rigidly. A group
/// step faces every model along ONE heading (the unit's direction of travel, plus the wheel), but a rigid
/// step keeps the centres' spacing. For long rectangular bases (cavalry, bikes) packed 0.1" apart, any
/// heading more than about 20 degrees off the rank's own makes each base swing into its neighbour, and the
/// #399 gate rightly refused the step - so a packed cavalry rank could barely turn at all. A rigid wheel
/// avoids the stacking but charges the outer models for the whole arc, which runs them out of move first.
///
/// <para>Instead the unit re-forms to face the new heading, and the arrangement is chosen to cost as
/// little as possible: OPR moves each model up to its own allowance and lets it end facing any way, so the
/// only cost is each model's straight-line travel. Candidates are the current shape opened up just enough
/// to clear at the new heading (the "respace" idea), and every <see cref="FormationLibrary"/> row
/// partition laid out across the heading at the bases' true extents. Each is assigned model-to-slot by a
/// bottleneck assignment (the worst single model's travel is what limits the step), tie-broken by the
/// least total travel, which also keeps the models' relative order - an optimal Euclidean matching never
/// crosses two paths. A joined hero takes the centre of the front rank unless that clearly costs more.
/// The winner is the candidate that carries the unit furthest toward the cursor within every model's
/// budget, then the one with the smallest worst-case travel.</para>
///
/// <para>Pure geometry, no table: the caller still runs every gate (terrain, enemies, table edge, the #399
/// self-overlap check) on the result.</para>
/// </summary>
internal static class GroupReform
{
    /// <summary>A chosen re-form: each model's slot laid out around the unit's CURRENT centroid (input
    /// order), ready to be the base shape of the two-array <c>PlanGroupMove</c> with no rotation.</summary>
    internal readonly record struct Result(Position[] Slots, string Name);

    // Edge-to-edge spacing of a laid-out formation - FormationLibrary's convention everywhere else.
    private const float Gap = 0.1f;
    // Clearance an opened-up shape must keep between bases, so float noise never reads as contact.
    private const float OpenClearance = 0.02f;
    // Cohesion is checked a hair inside the limits so the exact-constant checks downstream agree.
    private const float CohesionMargin = 0.02f;
    // A hero keeps the front-centre slot unless that raises the worst model's travel by more than this.
    private const float HeroSlack = 1f;
    // Candidates whose reach toward the cursor (inches) differs by less than this are treated as equally far.
    private const float ReachTie = 0.25f;
    // Hysteresis: the shape shown last frame is kept unless another reaches this much further, or (at equal
    // reach) cuts the worst model's travel by this much. Without it near-equal candidates traded places
    // frame to frame as the cursor moved - correct, and unpleasant to watch.
    private const float StickReach = 1f;
    private const float StickWorst = 1f;
    // ...and the same for who stands where: the previous heading's assignment is kept while its worst
    // model's travel is within this of the new optimum, so a sweeping cursor doesn't swap models around.
    private const float StickAssignment = 0.5f;

    /// <summary>
    /// The best legal re-form facing <paramref name="heading"/>, moving toward <paramref name="target"/>
    /// (null = stay put), or null if no candidate fits every model's budget and the cohesion rules. One-shot
    /// form of <see cref="Prepare"/> + <see cref="Prepared.Solve"/>.
    /// </summary>
    /// <param name="heroIndex">Index of a joined hero in the inputs, or -1.</param>
    public static Result? Solve(IReadOnlyList<IBaseShape> shapes, IReadOnlyList<Position> origins,
        IReadOnlyList<float> budgets, Float2 heading, Position? target, int heroIndex, string? preferred = null)
        => Prepare(shapes, origins, heading, heroIndex)?.Solve(budgets, target, preferred);

    /// <summary>
    /// The candidate shapes for one heading, each with its models already assigned to slots - everything
    /// but the scoring, which is the only part that depends on the cursor or the budgets. The resolver keeps
    /// one across frames for as long as the heading and the unit's positions hold.
    ///
    /// <para>Slots are assigned for the TURN alone (each model's distance to its slot around the current
    /// centroid), not for the turn plus the move toward the cursor. The translation is the same for every
    /// model, so this changes little in cost, and it means a moving cursor never reshuffles who stands
    /// where.</para>
    /// </summary>
    /// <param name="previous">The last heading's candidates, whose assignments are kept where still good.</param>
    public static Prepared? Prepare(IReadOnlyList<IBaseShape> shapes, IReadOnlyList<Position> origins,
        Float2 heading, int heroIndex, Prepared? previous = null)
    {
        int n = shapes.Count;
        if (n < 2) return null;

        float hl = MathF.Sqrt(heading.X * heading.X + heading.Y * heading.Y);
        if (hl < 1e-6f) return null;
        var fwd = new Float2(heading.X / hl, heading.Y / hl);
        var right = new Float2(fwd.Y, -fwd.X);
        Position c0 = GroupFormationUtilities.Centroid(origins);

        Position ToWorld(float dx, float dz) =>
            new(c0.x + dx * right.X + dz * fwd.X, c0.z + dx * right.Y + dz * fwd.Y);

        // The unit's own shape, untouched, when its bases already clear at the new heading. It is never
        // stretched to make them clear: a stretch sized to the exact heading changed the spacing with every
        // twitch of the cursor. Every other candidate uses the one standard spacing below.
        var opened = new List<(string name, Position[] slots)>();
        Position[] asIs = origins.ToArray();
        if (AllClear(shapes, asIs, fwd, OpenClearance) && IsCoherent(shapes, asIs, fwd))
            opened.Add(("current shape", asIs));

        // Every row partition, laid out across the heading at each base's own extents (a rectangle facing
        // the heading is its width across and its length deep).
        var halfXs = new float[n];
        var halfZs = new float[n];
        for (int i = 0; i < n; i++)
            (halfXs[i], halfZs[i]) = BaseShapeGeometry.FootprintHalfExtents(shapes[i], new Float2(0f, 1f));

        var candidates = new List<(string name, Position[] slots)>(opened);
        var assignments = new Dictionary<string, int[]>();
        foreach (int[] rows in FormationLibrary.RowPartitions(n))
        {
            // Provisional slots (input-order extents) decide who goes where; the final layout re-spaces with
            // the assigned models' own extents, as FormationLibrary.PlanFormationOffsets does.
            var provisional = FormationLibrary.LayoutOffsets(halfXs, halfZs, rows, Gap);
            var cost = new float[n, n];
            for (int m = 0; m < n; m++)
                for (int k = 0; k < n; k++)
                    cost[m, k] = Position.GetDistance2D(origins[m], ToWorld(provisional[k].dx, provisional[k].dz));

            string name = FormationLibrary.Describe(rows);
            int[] slotOf = Assign(cost, heroIndex, heroSlot: rows[0] / 2);
            if (previous?.AssignmentFor(name) is { } prior
                && WorstCost(cost, prior) <= WorstCost(cost, slotOf) + StickAssignment)
                slotOf = prior;
            assignments[name] = slotOf;

            var modelForSlot = new int[n];
            for (int m = 0; m < n; m++) modelForSlot[slotOf[m]] = m;
            var permX = new float[n];
            var permZ = new float[n];
            for (int k = 0; k < n; k++) { permX[k] = halfXs[modelForSlot[k]]; permZ[k] = halfZs[modelForSlot[k]]; }
            var offsets = FormationLibrary.LayoutOffsets(permX, permZ, rows, Gap);

            var slots = new Position[n];
            for (int m = 0; m < n; m++) slots[m] = ToWorld(offsets[slotOf[m]].dx, offsets[slotOf[m]].dz);
            if (IsCoherent(shapes, slots, fwd)) candidates.Add((name, slots));
        }

        return new Prepared(origins.ToArray(), fwd, c0, candidates, assignments);
    }

    /// <summary>The candidates for one heading (<see cref="Prepare"/>), ready to be scored per frame.</summary>
    internal sealed class Prepared
    {
        private readonly Position[] _origins;
        private readonly Float2 _fwd;
        private readonly Position _c0;
        private readonly List<(string name, Position[] slots)> _candidates;
        private readonly Dictionary<string, int[]> _assignments;

        internal Prepared(Position[] origins, Float2 fwd, Position c0, List<(string, Position[])> candidates,
            Dictionary<string, int[]> assignments)
        {
            _origins = origins;
            _fwd = fwd;
            _c0 = c0;
            _candidates = candidates;
            _assignments = assignments;
        }

        /// <summary>The named formation's slots at this heading (a Ctrl+Wheel pick), if it is legal here.</summary>
        internal Position[]? SlotsFor(string name)
        {
            foreach (var (n, slots) in _candidates)
                if (n == name) return slots;
            return null;
        }

        /// <summary>The slot per model this heading gave the named formation, if it built one.</summary>
        internal int[]? AssignmentFor(string name) => _assignments.TryGetValue(name, out var a) ? a : null;

        /// <summary>The unit's facing for every candidate.</summary>
        public Float2 Heading => _fwd;

        /// <summary>
        /// Scores every candidate against the budgets and the cursor (see <see cref="GroupReform"/>), keeping
        /// <paramref name="preferred"/> - the shape chosen last frame - unless another is clearly better.
        /// </summary>
        public Result? Solve(IReadOnlyList<float> budgets, Position? target, string? preferred = null)
        {
            int n = _origins.Length;
            float tx = target is { } t ? t.x - _c0.x : 0f;
            float tz = target is { } t2 ? t2.z - _c0.z : 0f;
            float wanted = MathF.Sqrt(tx * tx + tz * tz);

            Result? best = null, kept = null;
            float bestReach = -1f, bestWorst = float.MaxValue, keptReach = 0f, keptWorst = 0f;
            foreach (var (name, slots) in _candidates)
            {
                var plan = GroupFormationUtilities.PlanGroupMove(slots, _origins, budgets, _c0, 1f, 0f, tx, tz);
                if (!plan.WithinBudget) continue;

                float reach = plan.TranslationScale * wanted;
                float worst = 0f;
                for (int i = 0; i < n; i++)
                    worst = MathF.Max(worst, Position.GetDistance2D(_origins[i], plan.NewPositions[i]));

                if (name == preferred) { kept = new Result(slots, name); keptReach = reach; keptWorst = worst; }

                // Further toward the cursor wins; among equals, the cheaper worst case; among those, the
                // earlier candidate (the unit's own shape first, then the library's shallow-to-deep order).
                bool better = reach > bestReach + ReachTie
                    || (reach > bestReach - ReachTie && worst < bestWorst - 0.05f);
                if (!better) continue;
                best = new Result(slots, name);
                bestReach = reach;
                bestWorst = worst;
            }

            if (kept != null && best != null
                && bestReach <= keptReach + StickReach
                && !(bestReach > keptReach - ReachTie && bestWorst < keptWorst - StickWorst))
                return kept;
            return best;
        }
    }

    /// <summary>
    /// The row partitions this unit can legally form up in at the standard spacing and its bases' true
    /// extents - the Ctrl+Wheel catalog, so a picked formation is spaced exactly like a re-form. Legality
    /// doesn't depend on the heading: the whole layout turns with it.
    /// </summary>
    public static List<FormationLibrary.Formation> LegalPartitions(IReadOnlyList<IBaseShape> shapes)
    {
        int n = shapes.Count;
        var halfXs = new float[n];
        var halfZs = new float[n];
        for (int i = 0; i < n; i++)
            (halfXs[i], halfZs[i]) = BaseShapeGeometry.FootprintHalfExtents(shapes[i], new Float2(0f, 1f));

        var legal = new List<FormationLibrary.Formation>();
        foreach (int[] rows in FormationLibrary.RowPartitions(n))
        {
            var at = FormationLibrary.LayoutOffsets(halfXs, halfZs, rows, Gap)
                .Select(o => new Position(o.dx, o.dz)).ToArray();
            if (IsCoherent(shapes, at, new Float2(0f, 1f)))
                legal.Add(new FormationLibrary.Formation(rows, FormationLibrary.Describe(rows)));
        }
        return legal;
    }

    /// <summary>True when every pair of bases, all facing <paramref name="facing"/>, is at least
    /// <paramref name="clearance"/>" apart. Stops at the first pair that is not.</summary>
    private static bool AllClear(IReadOnlyList<IBaseShape> shapes, Position[] at, Float2 facing, float clearance)
    {
        var hulls = Footprints(shapes, at, facing);
        for (int i = 0; i < at.Length; i++)
            for (int j = i + 1; j < at.Length; j++)
            {
                // Bases farther apart than their circumscribing circles (plus the clearance) cannot touch.
                float reach = shapes[i].CircumscribedRadiusInches + shapes[j].CircumscribedRadiusInches + clearance;
                if (Position.GetDistance2D(at[i], at[j]) > reach) continue;
                if (BaseShapeGeometry.FootprintGap(hulls[i], hulls[j]) < clearance) return false;
            }
        return true;
    }

    // Each hull built once per layout rather than once per pair (#191's validator pattern).
    private static BaseFootprint[] Footprints(IReadOnlyList<IBaseShape> shapes, IReadOnlyList<Position> at, Float2 facing)
    {
        var hulls = new BaseFootprint[at.Count];
        for (int i = 0; i < at.Count; i++) hulls[i] = shapes[i].Footprint(at[i], facing);
        return hulls;
    }

    /// <summary>No two bases overlap, every model is within 1" of another, and no pair is more than 9"
    /// apart - all surface to surface at the shared facing.</summary>
    internal static bool IsCoherent(IReadOnlyList<IBaseShape> shapes, IReadOnlyList<Position> at, Float2 facing)
    {
        int n = at.Count;
        float near = GameWideConstants.MAX_MODEL_DISTANCE_FROM_ANY_OTHER_MODEL_INCHES - CohesionMargin;
        float far = GameWideConstants.MAX_MODEL_DISTANCE_FROM_ALL_OTHER_MODELS_INCHES - CohesionMargin;
        var hulls = Footprints(shapes, at, facing);
        var nearest = new float[n];
        Array.Fill(nearest, float.MaxValue);
        for (int i = 0; i < n; i++)
            for (int j = i + 1; j < n; j++)
            {
                float g = BaseShapeGeometry.FootprintGap(hulls[i], hulls[j]);
                if (g < 0f || g > far) return false;
                nearest[i] = MathF.Min(nearest[i], g);
                nearest[j] = MathF.Min(nearest[j], g);
            }
        for (int i = 0; i < n; i++)
            if (nearest[i] > near) return false;
        return true;
    }

    /// <summary>
    /// Model-to-slot assignment (<c>result[model] = slot</c>): the smallest achievable worst-case cost first,
    /// then the least total cost among assignments that meet it. A hero is pinned to
    /// <paramref name="heroSlot"/> unless that raises the worst case by more than <see cref="HeroSlack"/>.
    /// </summary>
    internal static int[] Assign(float[,] cost, int heroIndex, int heroSlot)
    {
        int n = cost.GetLength(0);
        float limit = Bottleneck(cost, -1, -1);
        int pinModel = -1, pinSlot = -1;
        if (heroIndex >= 0 && heroIndex < n && heroSlot >= 0 && heroSlot < n)
        {
            float pinned = Bottleneck(cost, heroIndex, heroSlot);
            if (pinned <= limit + HeroSlack) { limit = pinned; pinModel = heroIndex; pinSlot = heroSlot; }
        }

        const double Forbidden = 1e9;
        var c = new double[n, n];
        for (int m = 0; m < n; m++)
            for (int k = 0; k < n; k++)
            {
                bool allowed = cost[m, k] <= limit + 1e-4f
                    && (pinModel < 0 || (m == pinModel) == (k == pinSlot));
                c[m, k] = allowed ? cost[m, k] : Forbidden;
            }
        return Hungarian(c);
    }

    private static float WorstCost(float[,] cost, int[] slotOf)
    {
        float worst = 0f;
        for (int m = 0; m < slotOf.Length; m++) worst = MathF.Max(worst, cost[m, slotOf[m]]);
        return worst;
    }

    /// <summary>The smallest threshold under which a perfect model-to-slot matching exists, optionally with
    /// one model pinned to one slot.</summary>
    private static float Bottleneck(float[,] cost, int pinModel, int pinSlot)
    {
        int n = cost.GetLength(0);
        var values = new List<float>(n * n);
        foreach (float v in cost) values.Add(v);
        values.Sort();

        int lo = 0, hi = values.Count - 1;
        while (lo < hi)
        {
            int mid = (lo + hi) / 2;
            if (HasPerfectMatching(cost, values[mid], pinModel, pinSlot)) hi = mid; else lo = mid + 1;
        }
        return values[lo];
    }

    private static bool HasPerfectMatching(float[,] cost, float limit, int pinModel, int pinSlot)
    {
        int n = cost.GetLength(0);
        var modelOfSlot = new int[n];
        Array.Fill(modelOfSlot, -1);

        bool Allowed(int m, int k) => cost[m, k] <= limit && (pinModel < 0 || (m == pinModel) == (k == pinSlot));

        bool TryAugment(int m, bool[] seen)
        {
            for (int k = 0; k < n; k++)
            {
                if (seen[k] || !Allowed(m, k)) continue;
                seen[k] = true;
                if (modelOfSlot[k] < 0 || TryAugment(modelOfSlot[k], seen)) { modelOfSlot[k] = m; return true; }
            }
            return false;
        }

        for (int m = 0; m < n; m++)
            if (!TryAugment(m, new bool[n])) return false;
        return true;
    }

    /// <summary>Minimum-cost perfect assignment (Hungarian, potentials form). Returns slot per model.</summary>
    private static int[] Hungarian(double[,] a)
    {
        int n = a.GetLength(0);
        var u = new double[n + 1];
        var v = new double[n + 1];
        var p = new int[n + 1];   // p[slot] = model, 1-based
        var way = new int[n + 1];
        for (int i = 1; i <= n; i++)
        {
            p[0] = i;
            int j0 = 0;
            var minv = new double[n + 1];
            var used = new bool[n + 1];
            Array.Fill(minv, double.MaxValue);
            do
            {
                used[j0] = true;
                int i0 = p[j0], j1 = 0;
                double delta = double.MaxValue;
                for (int j = 1; j <= n; j++)
                {
                    if (used[j]) continue;
                    double cur = a[i0 - 1, j - 1] - u[i0] - v[j];
                    if (cur < minv[j]) { minv[j] = cur; way[j] = j0; }
                    if (minv[j] < delta) { delta = minv[j]; j1 = j; }
                }
                for (int j = 0; j <= n; j++)
                {
                    if (used[j]) { u[p[j]] += delta; v[j] -= delta; }
                    else minv[j] -= delta;
                }
                j0 = j1;
            } while (p[j0] != 0);
            do
            {
                int j1 = way[j0];
                p[j0] = p[j1];
                j0 = j1;
            } while (j0 != 0);
        }

        var slotOf = new int[n];
        for (int j = 1; j <= n; j++) slotOf[p[j] - 1] = j - 1;
        return slotOf;
    }
}
