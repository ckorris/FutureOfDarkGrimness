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
}

/// <summary>
/// The two armies' units on the main menu's war table (#448): the eight unit slots of the signed-off
/// mockup (four a side - a small command group, a ten-model squad, a five-model squad and a vehicle on
/// the near side; an eight-model squad with two casualties, a five-model squad, three large bases and a
/// big walker on the far side), filled by a random allowed <see cref="MenuArmies"/> pairing. Labels are
/// the army abbreviation plus a per-side number with a space between, since several abbreviations
/// already contain hyphens.
///
/// <para>The slots sit clear of every terrain footprint in <see cref="HoloTableScene.BuildDefault"/>
/// (a test checks it); formations come from a fixed seed per slot so a layout is stable for a pairing.</para>
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

    public MenuArmy Near { get; }
    public MenuArmy Far { get; }
    public IReadOnlyList<HoloUnit> Units { get; }

    public HoloSkirmish(MenuArmy near, MenuArmy far)
    {
        Near = near;
        Far = far;
        var units = new List<HoloUnit>();
        var numberBySide = new int[2];
        for (int i = 0; i < Slots.Length; i++)
        {
            Slot slot = Slots[i];
            MenuArmy army = slot.Side == 0 ? near : far;
            string label = $"{army.Abbr} {++numberBySide[slot.Side]}";
            var trail = slot.Trail.Length == 0 ? Array.Empty<Vector2>() : slot.Trail.Append(slot.Centre).ToArray();
            units.Add(new HoloUnit(label, slot.Side, army, slot.Centre, slot.BaseRadius, slot.Dead,
                Formation(slot, seed: 100 + i), trail));
        }
        Units = units;
    }

    public static HoloSkirmish Random(Random rng)
    {
        var (near, far) = MenuArmies.PickPair(rng);
        return new HoloSkirmish(near, far);
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
