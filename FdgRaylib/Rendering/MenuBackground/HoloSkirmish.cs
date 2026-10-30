using System.Numerics;

namespace FdgRaylib.Rendering.MenuBackground;

/// <summary>
/// One unit on the menu's war table: a group of round bases sharing one flag. <see cref="Models"/> lists
/// every base; the first <see cref="Dead"/> of them are casualties (drawn faded and dashed). A single
/// large base is a vehicle. <see cref="Trail"/> is where it came from, ending at its centre.
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
/// The two armies' units on the main menu's war table (#448): the eight unit slots of the signed-off
/// mockup (four a side - a small command group, a ten-model squad, a five-model squad and a vehicle on
/// the near side; an eight-model squad with two casualties, a five-model squad, three large bases and a
/// big walker on the far side), filled by a random allowed <see cref="MenuArmies"/> pairing. Labels are
/// the army abbreviation plus a per-side number with a space between, since several abbreviations
/// already contain hyphens.
///
/// <para>The constructor uses the mockup's fixed positions, which sit clear of every footprint in
/// <see cref="HoloTableScene.BuildDefault"/> (a test checks it). <see cref="Generate"/> keeps the same
/// roster but places every unit at random on a given table (owner, 2026-10-30: randomise positions and
/// movement each battle).</para>
/// </summary>
internal sealed class HoloSkirmish
{
    private sealed record Slot(int Side, Vector2 Centre, int Count, float BaseRadius, int Dead, Vector2[] Trail);

    /// <summary>The roster: the mockup's slots (their positions are the fixed layout's; their counts,
    /// base sizes, casualties and whether they walk in are kept for random battles too).</summary>

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

    public MenuArmy Near { get; }
    public MenuArmy Far { get; }
    public IReadOnlyList<HoloUnit> Units { get; }

    /// <summary>The mockup's layout: every unit in its signed-off spot.</summary>
    public HoloSkirmish(MenuArmy near, MenuArmy far)
        : this(near, far, Slots.Select((slot, i) => MakeUnit(slot, near, far, Formation(slot, seed: 100 + i))).ToList())
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

    private static HoloUnit MakeUnit(Slot slot, MenuArmy near, MenuArmy far, IReadOnlyList<Vector2> models)
    {
        var trail = slot.Trail.Length == 0 ? Array.Empty<Vector2>() : slot.Trail.Append(slot.Centre).ToArray();
        return new HoloUnit("", slot.Side, slot.Side == 0 ? near : far, slot.Centre, slot.BaseRadius, slot.Dead, models, trail);
    }

    // Random placement rules, inches.
    private const float TerrainClearance = 0.6f;   // base edge to terrain
    private const float UnitGap = 4f;              // base edge to another unit's: room for both flags' labels
    private const float EdgeMargin = 0.5f;         // base edge to the table edge

    /// <summary>
    /// The same roster placed at random on <paramref name="scene"/>, or null if some unit found no room
    /// (the caller tries another table). Each side keeps to its own half; units that stay put stand in
    /// the middle band so the two sides are within shooting range, while walkers and vehicles can be
    /// anywhere in their half. A walker gets a two-leg route in from behind (toward its own table edge),
    /// and every model's path must keep clear of terrain. Bases keep off all terrain - craters included -
    /// and apart from other units.
    /// </summary>
    public static HoloSkirmish? Generate(MenuArmy near, MenuArmy far, HoloTableScene scene, Random rng)
    {
        var obstacles = scene.Obstacles.ToList();
        var placed = new List<(Vector2 At, float Radius)>();
        var units = new List<HoloUnit>();
        float U(float lo, float hi) => lo + rng.NextSingle() * (hi - lo);

        foreach (Slot template in Slots)
        {
            bool walks = template.Trail.Length > 0;
            float face = template.Side == 0 ? 1f : -1f;
            var (yLo, yHi) = walks || template.Count == 1 ? (5f, 21f) : (4f, 14f);
            HoloUnit? unit = null;
            for (int attempt = 0; attempt < 300 && unit is null; attempt++)
            {
                var centre = new Vector2(U(-31f, 31f), -face * U(yLo, yHi));
                Vector2[] trail = Array.Empty<Vector2>();
                if (walks)
                {
                    var start = centre + new Vector2(U(-4f, 4f), -face * U(6f, 10f));
                    var mid = Vector2.Lerp(start, centre, 0.5f) + new Vector2(U(-1.5f, 1.5f), 0f);
                    trail = new[] { start, mid };
                }
                Slot slot = template with { Centre = centre, Trail = trail };
                IReadOnlyList<Vector2> models = Formation(slot, rng.Next());
                if (!BasesFit(models, template.BaseRadius, obstacles, placed)) continue;
                if (walks && !RouteClear(models, centre, trail, template.BaseRadius, obstacles)) continue;
                unit = MakeUnit(slot, near, far, models);
            }
            if (unit is null) return null;
            units.Add(unit);
            placed.AddRange(unit.Models.Select(m => (m, unit.BaseRadius)));
        }
        return new HoloSkirmish(near, far, units);
    }

    private static bool OnTable(Vector2 p, float r) =>
        MathF.Abs(p.X) + r < HoloTableScene.HalfW - EdgeMargin && MathF.Abs(p.Y) + r < HoloTableScene.HalfD - EdgeMargin;

    private static bool BasesFit(IReadOnlyList<Vector2> models, float r, List<HoloFootprint> obstacles, List<(Vector2 At, float Radius)> placed) =>
        models.All(m => OnTable(m, r)
                        && obstacles.All(o => HoloGeometry.PointPolygon(m, o.Polygon) > r + TerrainClearance)
                        && placed.All(p => Vector2.Distance(m, p.At) > r + p.Radius + UnitGap));

    /// <summary>Every model walks the route offset by where it stands in the formation; each of those
    /// paths has to start on the table and keep a base's width clear of terrain.</summary>
    private static bool RouteClear(IReadOnlyList<Vector2> models, Vector2 centre, Vector2[] trail, float r, List<HoloFootprint> obstacles)
    {
        foreach (Vector2 m in models)
        {
            Vector2 o = m - centre;
            var path = trail.Select(t => t + o).Append(m).ToList();
            if (!OnTable(path[0], r)) return false;
            for (int i = 0; i + 1 < path.Count; i++)
                if (!HoloGeometry.IsClear(path[i], path[i + 1], obstacles, r + 0.3f)) return false;
        }
        return true;
    }

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
