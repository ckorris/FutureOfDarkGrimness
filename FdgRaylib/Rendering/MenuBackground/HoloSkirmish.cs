using System.Numerics;

namespace FdgRaylib.Rendering.MenuBackground;

/// <summary>What a unit does during a battle on the menu's war table (#448, owner feedback 2026-10-30).</summary>
internal enum EHoloRole
{
    /// <summary>Stands its ground and shoots for the whole battle.</summary>
    Holds,

    /// <summary>Walks in along its route, stops, then switches to shooting.</summary>
    Advances,

    /// <summary>Keeps moving the whole battle along a multi-leg route; never shoots or is shot at.</summary>
    Patrols,

    /// <summary>Charges an enemy unit that is charging it back (#464): the two walk in, meet front rank to
    /// front rank on the centre line at the same moment, and fight in base contact for the rest of the
    /// battle. Neither shoots nor is shot at.</summary>
    Charges,
}

/// <summary>
/// One unit on the menu's war table: a group of round bases sharing one flag. <see cref="Models"/> lists
/// every base at the unit's FINAL spot; the first <see cref="Dead"/> of them are casualties (drawn faded
/// and dashed). A single large base is a vehicle. <see cref="Trail"/> is its route, ending at its centre.
/// </summary>
internal sealed record HoloUnit(
    string Label,
    int Side,
    MenuArmy Army,
    Vector2 Centre,
    float BaseRadius,
    int Dead,
    IReadOnlyList<Vector2> Models,
    IReadOnlyList<Vector2> Trail)
{
    public EHoloRole Role { get; init; } = EHoloRole.Holds;

    /// <summary>Battle time it reaches its spot and may start shooting (0 for a unit that holds; the end
    /// of the battle for one that patrols - it never arrives).</summary>
    public float ArriveAt { get; init; }

    /// <summary>Which melee a unit that charges belongs to (1, 2...): the two units sharing a number, one a
    /// side, fight each other. Zero for every other role. A number rather than a reference to the foe, so
    /// the record's value equality never chases a cycle.</summary>
    public int MeleeGroup { get; init; }

    public bool IsVehicle => Models.Count == 1;
    public int AliveCount => Models.Count - Dead;
    public IEnumerable<Vector2> Alive => Models.Skip(Dead);

    /// <summary>+1 for the near side (facing away from the viewer), -1 for the far side.</summary>
    public float Facing => Side == 0 ? 1f : -1f;

    /// <summary>The living model the flag stands on: the one nearest the unit's centre.</summary>
    public Vector2 Leader => Alive.OrderBy(m => Vector2.DistanceSquared(m, Centre)).First();

    /// <summary>The text over the flag: "ORK 2 x10", or just "ORK 4" for a lone model.</summary>
    public string Tag => Models.Count > 1 ? $"{Label} x{AliveCount}" : Label;

    /// <summary>
    /// How far along its route the unit is at <paramref name="battleTime"/>, 0..1: a unit that holds is
    /// already there; one that advances eases in and stops at <see cref="ArriveAt"/>; one that charges
    /// sets off slowly and gathers speed, so it hits its foe at full tilt at <see cref="ArriveAt"/>; one
    /// that patrols moves at a steady pace from the first moment to the last.
    /// </summary>
    public float ProgressAt(float battleTime) => Role switch
    {
        EHoloRole.Advances => HoloDirector.Smooth(battleTime / MathF.Max(ArriveAt, 0.01f)),
        EHoloRole.Charges => MathF.Pow(Math.Clamp(battleTime / MathF.Max(ArriveAt, 0.01f), 0f, 1f), 2f),
        EHoloRole.Patrols => Math.Clamp(battleTime / HoloDirector.BattleLength, 0f, 1f),
        _ => 1f,
    };

    /// <summary>
    /// How far the unit is from its final spot when it is <paramref name="progress"/> (0..1) of the way
    /// along its trail, measured by distance: add it to every model. Zero for a unit with no trail, and
    /// zero once it has arrived.
    /// </summary>
    public Vector2 OffsetAt(float progress)
    {
        if (Trail.Count < 2) return Vector2.Zero;
        float total = 0f;
        for (int i = 0; i + 1 < Trail.Count; i++) total += Vector2.Distance(Trail[i], Trail[i + 1]);
        float left = Math.Clamp(progress, 0f, 1f) * total;
        for (int i = 0; i + 1 < Trail.Count; i++)
        {
            float leg = Vector2.Distance(Trail[i], Trail[i + 1]);
            if (left <= leg || i + 2 == Trail.Count)
                return Vector2.Lerp(Trail[i], Trail[i + 1], leg == 0f ? 1f : Math.Clamp(left / leg, 0f, 1f)) - Centre;
            left -= leg;
        }
        return Vector2.Zero;
    }
}

/// <summary>
/// The two armies' units on the main menu's war table (#448): four a side, filled by a random allowed
/// <see cref="MenuArmies"/> pairing. Labels are the army abbreviation plus a per-side number with a space
/// between, since several abbreviations already contain hyphens.
///
/// <para>The roster is the mockup's - near side a small command group, a ten-model squad, a five-model
/// squad and a vehicle; far side an eight-model squad with two casualties, a five-model squad, three large
/// bases and a big walker - except that Titan Lords field only big single-model units (owner,
/// 2026-10-30). The constructor uses the mockup's fixed positions (the fallback layout, clear of every
/// footprint in <see cref="HoloTableScene.BuildDefault"/>); <see cref="Generate"/> places the roster at
/// random and gives every unit a role - holds, advances then shoots, patrols all battle, or charges an
/// enemy unit charging it back and fights it in base contact (#464).</para>
/// </summary>
internal sealed class HoloSkirmish
{
    private sealed record Slot(int Side, Vector2 Centre, int Count, float BaseRadius, int Dead, Vector2[] Trail);

    private static readonly Slot[] Slots =
    {
        new(0, new(-21, -16), 3, 0.6f, 0, new Vector2[] { new(-28, -21), new(-24, -19) }),
        new(0, new(-9, -9), 10, 0.5f, 0, Array.Empty<Vector2>()),
        new(0, new(4, -13), 5, 0.5f, 0, Array.Empty<Vector2>()),
        new(0, new(31, -14), 1, 2.0f, 0, new Vector2[] { new(26, -22), new(29, -18) }),
        new(1, new(-3, 10), 8, 0.5f, 2, Array.Empty<Vector2>()),
        new(1, new(13, 5), 5, 0.5f, 0, Array.Empty<Vector2>()),
        new(1, new(-28, 16), 3, 0.8f, 0, new Vector2[] { new(-34, 21), new(-31, 18) }),
        new(1, new(30, 17), 1, 1.7f, 0, new Vector2[] { new(34, 22), new(32, 20) }),
    };

    /// <summary>The army whose units are all big single models (knights and titans).</summary>
    public const string BigModelsOnlyArmy = "TITAN";

    private static readonly float[] TitanRadii = { 2.2f, 1.9f, 2.0f, 1.8f };

    /// <summary>When the fixed layout's walkers arrive (the original slice-4 timing).</summary>
    private const float FixedArrival = 8f;

    public MenuArmy Near { get; }
    public MenuArmy Far { get; }
    public IReadOnlyList<HoloUnit> Units { get; }

    /// <summary>The mockup's layout: every unit in its signed-off spot; its walkers advance, the rest hold.</summary>
    public HoloSkirmish(MenuArmy near, MenuArmy far)
        : this(near, far, RosterFor(near, far).Select((slot, i) => MakeUnit(slot, near, far, Formation(slot, seed: 100 + i),
            slot.Trail.Length > 0 ? EHoloRole.Advances : EHoloRole.Holds, slot.Trail.Length > 0 ? FixedArrival : 0f)).ToList())
    {
    }

    private HoloSkirmish(MenuArmy near, MenuArmy far, IReadOnlyList<HoloUnit> units)
    {
        Near = near;
        Far = far;
        // Number each side's units in roster order: the label is the army plus its place on its side.
        var numberBySide = new int[2];
        Units = units.Select(u => u with { Label = $"{(u.Side == 0 ? near : far).Abbr} {++numberBySide[u.Side]}" }).ToList();
    }

    /// <summary>The mockup's slots, with a Titan Lords side swapped for four big single models at the
    /// same spots.</summary>
    private static IEnumerable<Slot> RosterFor(MenuArmy near, MenuArmy far)
    {
        int titan = 0;
        foreach (Slot slot in Slots)
        {
            bool bigOnly = (slot.Side == 0 ? near : far).Abbr == BigModelsOnlyArmy;
            yield return bigOnly ? slot with { Count = 1, BaseRadius = TitanRadii[titan++ % TitanRadii.Length], Dead = 0 } : slot;
        }
    }

    private static HoloUnit MakeUnit(Slot slot, MenuArmy near, MenuArmy far, IReadOnlyList<Vector2> models, EHoloRole role, float arriveAt)
    {
        var trail = slot.Trail.Length == 0 ? Array.Empty<Vector2>() : slot.Trail.Append(slot.Centre).ToArray();
        return new HoloUnit("", slot.Side, slot.Side == 0 ? near : far, slot.Centre, slot.BaseRadius, slot.Dead, models, trail)
        {
            Role = role,
            ArriveAt = arriveAt,
        };
    }

    // Random placement rules, inches.
    private const float TerrainClearance = 0.6f;   // base edge to terrain
    private const float UnitGap = 4f;              // a standing base's edge to another's: room for both labels
    private const float PathGap = 1.5f;            // a moving base's edge to any other base or path
    private const float EdgeMargin = 0.5f;         // base edge to the table edge
    private const float CentreClear = 2f;          // base edge to the centre line, for everyone not in melee
    private const float MinArrival = 5f, MaxArrival = 10f;   // advancers arrive well before StillMoment

    /// <summary>Most melees in one battle (#464, owner 2026-11-02: "0-2 in every battle").</summary>
    public const int MaxMelees = 2;

    /// <summary>The gap between two bases in melee, edge to edge: close enough to read as contact, never
    /// overlapping.</summary>
    public const float ContactGap = 0.15f;

    /// <summary>How far the near-side unit's flag stands to the right of its foe's in a melee, inches. The
    /// near side writes its tag to the right of its flag and the far side to the left, so this way round
    /// the two tags lean apart instead of running into each other.</summary>
    private const float MinFlagSplit = 1.5f, MaxFlagSplit = 2.5f;

    /// <summary>
    /// The roster placed at random on <paramref name="scene"/>, or null if some unit found no room (the
    /// caller tries another table). Each side gets at least one unit that holds and one that advances; up
    /// to <see cref="MaxMelees"/> pairs of units charge each other (#464), and each side has a patroller
    /// unless two of its units are in melee. Holders and advancers end up in the middle band so they are
    /// within shooting range; advancers walk in from behind (toward their own edge); patrollers wander a
    /// three-leg route through their own half for the whole battle; a melee pair is placed together, both
    /// walking in from behind to meet front rank to front rank on the centre line. Every base keeps off all
    /// terrain (craters included); every moving model's path keeps a base's width clear of terrain and
    /// clear of every other unit's bases and paths, so nothing walks through anything; standing units keep
    /// room for their labels. The two units of a melee are exempt from those gaps with each other only.
    /// </summary>
    public static HoloSkirmish? Generate(MenuArmy near, MenuArmy far, HoloTableScene scene, Random rng)
    {
        var obstacles = scene.Obstacles.ToList();
        var standing = new List<(Vector2 At, float Radius)>();
        var paths = new List<(Vector2 A, Vector2 B, float Radius)>();
        float U(float lo, float hi) => lo + rng.NextSingle() * (hi - lo);

        // Roles: each side is dealt holds, advances, patrols and one more holds-or-advances; a melee takes
        // the last of those from each side, and a second melee the patroller.
        var roster = RosterFor(near, far).ToList();
        var roles = new EHoloRole[roster.Count];
        int melees = rng.Next(MaxMelees + 1);
        var chargers = new List<int>[] { new(), new() };
        foreach (int side in new[] { 0, 1 })
        {
            var mine = Enumerable.Range(0, roster.Count).Where(i => roster[i].Side == side).OrderBy(_ => rng.Next()).ToList();
            var deal = new[] { EHoloRole.Holds, EHoloRole.Advances, EHoloRole.Patrols, rng.Next(2) == 0 ? EHoloRole.Holds : EHoloRole.Advances };
            for (int k = 0; k < melees; k++) deal[deal.Length - 1 - k] = EHoloRole.Charges;
            for (int k = 0; k < mine.Count; k++)
            {
                roles[mine[k]] = deal[k % deal.Length];
                if (roles[mine[k]] == EHoloRole.Charges) chargers[side].Add(mine[k]);
            }
        }

        // Would these bases and their paths fit among everything placed so far? A unit that stops (stands)
        // also keeps label room from every standing base and stays off every path.
        bool Fits(IReadOnlyList<Vector2> models, List<(Vector2 A, Vector2 B)> myPaths, float r, float face, bool stands, float centreClear)
        {
            if (!models.All(m => OnTable(m, r, face, centreClear) && obstacles.All(o => HoloGeometry.PointPolygon(m, o.Polygon) > r + TerrainClearance)))
                return false;
            if (!myPaths.All(p => OnTable(p.A, r, face, centreClear) && HoloGeometry.IsClear(p.A, p.B, obstacles, r + 0.3f))) return false;
            if (stands && !models.All(m => standing.All(s => Vector2.Distance(m, s.At) > r + s.Radius + UnitGap))) return false;
            if (!myPaths.All(p => standing.All(s => HoloGeometry.PointSegment(s.At, p.A, p.B) > r + s.Radius + PathGap)
                                  && paths.All(q => HoloGeometry.SegmentDistance(p.A, p.B, q.A, q.B) > r + q.Radius + PathGap)))
                return false;
            return !stands || models.All(m => paths.All(q => HoloGeometry.PointSegment(m, q.A, q.B) > r + q.Radius + PathGap));
        }

        void Commit(IReadOnlyList<Vector2> models, List<(Vector2 A, Vector2 B)> myPaths, float r, bool stands)
        {
            if (stands) standing.AddRange(models.Select(m => (m, r)));
            paths.AddRange(myPaths.Select(p => (p.A, p.B, r)));
        }

        // Melees first: each needs a clear stretch of the centre line, the scarcest ground on the table.
        var placed = new HoloUnit?[roster.Count];
        for (int g = 0; g < melees; g++)
        {
            int ia = chargers[0][g], ib = chargers[1][g];
            for (int attempt = 0; attempt < 300 && placed[ia] is null; attempt++)
            {
                var (sa, sb) = MeleePair(roster[ia], roster[ib], rng);
                if (HoloEngagement.ContactPoints(sa.Models.Skip(sa.Slot.Dead), sa.Slot.BaseRadius,
                        sb.Models.Skip(sb.Slot.Dead), sb.Slot.BaseRadius).Count == 0)
                    continue;
                var pa = ModelPaths(sa.Models, sa.Slot.Centre, sa.Slot.Trail).ToList();
                var pb = ModelPaths(sb.Models, sb.Slot.Centre, sb.Slot.Trail).ToList();
                if (!Fits(sa.Models, pa, sa.Slot.BaseRadius, 1f, stands: true, centreClear: 0f)
                    || !Fits(sb.Models, pb, sb.Slot.BaseRadius, -1f, stands: true, centreClear: 0f))
                    continue;
                float arrive = U(MinArrival, MaxArrival);   // the same for both: they meet as they arrive
                placed[ia] = MakeUnit(sa.Slot, near, far, sa.Models, EHoloRole.Charges, arrive) with { MeleeGroup = g + 1 };
                placed[ib] = MakeUnit(sb.Slot, near, far, sb.Models, EHoloRole.Charges, arrive) with { MeleeGroup = g + 1 };
                Commit(sa.Models, pa, sa.Slot.BaseRadius, stands: true);
                Commit(sb.Models, pb, sb.Slot.BaseRadius, stands: true);
            }
            if (placed[ia] is null) return null;
        }

        for (int i = 0; i < roster.Count; i++)
        {
            if (placed[i] is not null) continue;
            Slot template = roster[i];
            EHoloRole role = roles[i];
            float face = template.Side == 0 ? 1f : -1f;
            for (int attempt = 0; attempt < 300 && placed[i] is null; attempt++)
            {
                Vector2 centre;
                Vector2[] trail;
                if (role == EHoloRole.Patrols)
                {
                    var route = PatrolRoute(rng, face);
                    centre = route[^1];
                    trail = route.Take(route.Length - 1).ToArray();
                }
                else
                {
                    centre = new Vector2(U(-31f, 31f), -face * U(4f, 14f));
                    trail = Array.Empty<Vector2>();
                    if (role == EHoloRole.Advances) trail = WalkIn(centre, face, rng);
                }

                Slot slot = template with { Centre = centre, Trail = trail };
                IReadOnlyList<Vector2> models = Formation(slot, rng.Next());
                float r = template.BaseRadius;
                var myPaths = ModelPaths(models, centre, trail).ToList();
                bool stands = role != EHoloRole.Patrols;
                if (!Fits(models, myPaths, r, face, stands, CentreClear)) continue;

                float arrive = role switch
                {
                    EHoloRole.Advances => U(MinArrival, MaxArrival),
                    EHoloRole.Patrols => HoloDirector.BattleLength,
                    _ => 0f,
                };
                placed[i] = MakeUnit(slot, near, far, models, role, arrive);
                Commit(models, myPaths, r, stands);
            }
            if (placed[i] is null) return null;
        }
        return new HoloSkirmish(near, far, placed.Select(u => u!).ToList());
    }

    /// <summary>A walk in from behind: a start 6-11" back toward the unit's own edge and a little to one
    /// side, and a kink half way.</summary>
    private static Vector2[] WalkIn(Vector2 centre, float face, Random rng)
    {
        float U(float lo, float hi) => lo + rng.NextSingle() * (hi - lo);
        var start = centre + new Vector2(U(-4f, 4f), -face * U(6f, 11f));
        var mid = Vector2.Lerp(start, centre, 0.5f) + new Vector2(U(-1.5f, 1.5f), 0f);
        return new[] { start, mid };
    }

    /// <summary>
    /// Two units in melee (#464), one a side, at a random spot along the centre line: each unit's foremost
    /// base stands half a <see cref="ContactGap"/> short of the line, the near unit's flag a little to the
    /// right of its foe's, and both walk in from behind. Returns each unit's slot (centre
    /// and trail filled in) and its bases.
    /// </summary>
    private static ((Slot Slot, Vector2[] Models) A, (Slot Slot, Vector2[] Models) B) MeleePair(Slot near, Slot far, Random rng)
    {
        float U(float lo, float hi) => lo + rng.NextSingle() * (hi - lo);
        float x = U(-27f, 27f);
        var a = FrontOnCentreLine(near, x, rng.Next());
        var b = FrontOnCentreLine(far, x, rng.Next());

        // Slide the far unit sideways so its flag stands just left of the near unit's.
        Vector2 Leader((Slot Slot, Vector2[] Models) u) =>
            u.Models.Skip(u.Slot.Dead).OrderBy(m => Vector2.DistanceSquared(m, u.Slot.Centre)).First();
        var shift = new Vector2(Leader(a).X - U(MinFlagSplit, MaxFlagSplit) - Leader(b).X, 0f);
        b = (b.Slot with { Centre = b.Slot.Centre + shift }, b.Models.Select(m => m + shift).ToArray());

        a = (a.Slot with { Trail = WalkIn(a.Slot.Centre, 1f, rng) }, a.Models);
        b = (b.Slot with { Trail = WalkIn(b.Slot.Centre, -1f, rng) }, b.Models);
        return (a, b);
    }

    /// <summary>The unit's formation centred on <paramref name="x"/>, then moved straight forward or back
    /// until its foremost base's edge is half a <see cref="ContactGap"/> short of the centre line.</summary>
    private static (Slot Slot, Vector2[] Models) FrontOnCentreLine(Slot slot, float x, int seed)
    {
        float face = slot.Side == 0 ? 1f : -1f;
        var centre = new Vector2(x, -face * 5f);
        Vector2[] models = Formation(slot with { Centre = centre }, seed).ToArray();
        float front = face > 0 ? models.Max(m => m.Y) : models.Min(m => m.Y);
        var lift = new Vector2(0f, -face * (slot.BaseRadius + ContactGap / 2f) - front);
        return (slot with { Centre = centre + lift }, models.Select(m => m + lift).ToArray());
    }

    /// <summary>A three-leg wander through the side's own half, mostly across the table: four points,
    /// the last being where the patrol ends when the battle does.</summary>
    private static Vector2[] PatrolRoute(Random rng, float face)
    {
        float U(float lo, float hi) => lo + rng.NextSingle() * (hi - lo);
        var pts = new List<Vector2> { new(U(-28f, 28f), -face * U(6f, 20f)) };
        float heading = rng.Next(2) == 0 ? 0f : MathF.PI;
        for (int leg = 0; leg < 3; leg++)
        {
            heading += U(-0.9f, 0.9f);
            pts.Add(pts[^1] + new Vector2(MathF.Cos(heading), MathF.Sin(heading)) * U(5f, 8f));
        }
        return pts.ToArray();
    }

    /// <summary>Each model's own copy of the route (offset by where it stands in the formation), as
    /// segments; none for a unit that does not move.</summary>
    private static IEnumerable<(Vector2 A, Vector2 B)> ModelPaths(IReadOnlyList<Vector2> models, Vector2 centre, Vector2[] trail)
    {
        if (trail.Length == 0) yield break;
        foreach (Vector2 m in models)
        {
            Vector2 o = m - centre;
            var path = trail.Select(t => t + o).Append(m).ToList();
            for (int i = 0; i + 1 < path.Count; i++) yield return (path[i], path[i + 1]);
        }
    }

    /// <summary>On the table with a margin, and in the side's own half, at least
    /// <paramref name="centreClear"/> clear of the centre line (<paramref name="face"/>: +1 near side, whose
    /// half is negative Y). A unit in melee passes zero: its front rank stands right at the line.</summary>
    private static bool OnTable(Vector2 p, float r, float face, float centreClear) =>
        MathF.Abs(p.X) + r < HoloTableScene.HalfW - EdgeMargin && MathF.Abs(p.Y) + r < HoloTableScene.HalfD - EdgeMargin
        && -face * p.Y > centreClear + r;

    /// <summary>
    /// Loose ranks of up to five, a base's width plus about half an inch apart, odd ranks staggered,
    /// rear ranks behind the front (relative to the side's facing), jittered a little so it reads as
    /// models rather than a grid, then centred on the slot.
    /// </summary>
    private static IReadOnlyList<Vector2> Formation(Slot slot, int seed)
    {
        if (slot.Count == 1) return new[] { slot.Centre };

        var rng = new Random(seed);
        float face = slot.Side == 0 ? 1f : -1f;
        float spacing = 2f * slot.BaseRadius + 0.55f;
        int cols = Math.Min(slot.Count, 5);
        var pts = new List<Vector2>();
        for (int i = 0; i < slot.Count; i++)
        {
            int row = i / cols, col = i % cols;
            int inRow = Math.Min(cols, slot.Count - row * cols);
            float x = (col - (inRow - 1) / 2f) * spacing + (row % 2 == 1 ? spacing * 0.5f : 0f);
            float y = -row * spacing * 0.9f * face;
            pts.Add(new Vector2(x + Jitter(rng), y + Jitter(rng)));
        }
        Vector2 mean = pts.Aggregate(Vector2.Zero, (a, b) => a + b) / pts.Count;
        return pts.Select(p => slot.Centre + p - mean).ToList();
    }

    // +-0.15" on each axis: even two neighbours jittered straight at each other close the 0.55" gap by
    // under 0.43", so bases never touch.
    private static float Jitter(Random rng) => (rng.NextSingle() - 0.5f) * 0.3f;
}
