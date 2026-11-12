using System.Numerics;

namespace FdgRaylib.Rendering.MenuBackground;

/// <summary>Colour family of a hologram line or fill; the renderer maps each to an RGB.</summary>
internal enum EHoloTint
{
    Terrain,
    Foliage,
    RimTop,
    Bracket,
    Surface,
}

/// <summary>One glowing line: <see cref="Intensity"/> scales its brightness (0..1+), <see cref="Width"/>
/// is in pixels at 1080p. <see cref="Piece"/> is the terrain piece index, or -1 for the table itself.</summary>
internal readonly record struct HoloLine(Vector3 A, Vector3 B, float Intensity, float Width, EHoloTint Tint, int Piece = -1);

/// <summary>A faint planar quad (a wall face, a roof) drawn under the lines.</summary>
internal readonly record struct HoloFill(Vector3 A, Vector3 B, Vector3 C, Vector3 D, float Intensity, EHoloTint Tint, int Piece = -1);

/// <summary>An inch label on the table rim.</summary>
internal readonly record struct HoloLabel(Vector3 At, string Text);

/// <summary>A blocking piece's outline on the table plane; a piece can have several (one per ruin leg).
/// <see cref="Centre"/> and <see cref="Radius"/> are a circle round it, so a distance check can usually
/// settle "nowhere near" with one subtraction instead of walking the outline (#464: generation cost).</summary>
internal readonly record struct HoloFootprint(int Piece, IReadOnlyList<Vector2> Polygon)
{
    public Vector2 Centre { get; } = Polygon.Aggregate(Vector2.Zero, (a, b) => a + b) / Polygon.Count;
    public float Radius { get; } = Polygon.Max(v => Vector2.Distance(v, Polygon.Aggregate(Vector2.Zero, (a, b) => a + b) / Polygon.Count));
}

/// <summary>
/// The main menu's war table as hologram geometry (#448): a 72"x48" slab with grid, inch ticks and
/// labels, and a fixed layout of wireframe terrain pieces (ruins, containers, a bunker, forests,
/// barricades, a rubble field). Built once; independent of screen size (the camera projects it).
///
/// <para><see cref="BuildDefault"/> is the layout signed off in the original mockup (since deleted);
/// <see cref="BuildRandom"/> deals a fresh one for every battle (owner, 2026-10-30) from the same kinds of
/// piece. Each blocking piece records its 2D footprint, which lines of fire are tested against; the rubble
/// field is area terrain - it blocks nothing, but units keep off it, so it has an <see cref="AreaFootprints"/>
/// outline instead.</para>
/// </summary>
internal sealed class HoloTableScene
{
    public const float TableW = 72f;
    public const float TableD = 48f;
    public const float HalfW = TableW / 2f;
    public const float HalfD = TableD / 2f;
    public const float RimDepth = 1.5f;

    private readonly List<HoloLine> _lines = new();
    private readonly List<HoloFill> _fills = new();
    private readonly List<HoloLabel> _labels = new();
    private readonly List<HoloFootprint> _footprints = new();
    private readonly List<Vector2> _pieceCentres = new();
    private readonly List<HoloFootprint> _areaFootprints = new();

    public IReadOnlyList<HoloLine> Lines => _lines;
    public IReadOnlyList<HoloFill> Fills => _fills;
    public IReadOnlyList<HoloLabel> Labels => _labels;

    /// <summary>The blocking pieces' footprints (polygons on the table plane).</summary>
    public IReadOnlyList<HoloFootprint> Footprints => _footprints;

    /// <summary>Centre of every piece, indexed by <see cref="HoloLine.Piece"/>.</summary>
    public IReadOnlyList<Vector2> PieceCentres => _pieceCentres;

    /// <summary>Area terrain outlines (the rubble field): no cover, no blocking, but no place to stand a base.</summary>
    public IReadOnlyList<HoloFootprint> AreaFootprints => _areaFootprints;

    /// <summary>Everything a unit must keep off: blocking footprints and area terrain.</summary>
    public IEnumerable<HoloFootprint> Obstacles => _footprints.Concat(_areaFootprints);

    /// <summary>The bunker's height, and the tallest any piece stands - a forest's trees only roughly.</summary>
    public const float BunkerHeight = 3f;

    /// <summary>Gap random pieces keep between each other, inches - room for a unit or a line of fire.</summary>
    public const float PieceGap = 2.5f;

    /// <summary>The slab's eight corners: everything the camera must keep on screen.</summary>
    public static IReadOnlyList<Vector3> SlabCorners { get; } = BuildSlabCorners();

    public static HoloTableScene BuildDefault()
    {
        var scene = new HoloTableScene();
        scene.AddTable();

        scene.AddRuin(new(-24, 2), 15, (new(-5, 0), new(5, 0)), (new(-5, 0), new(-5, 7)));
        scene.AddRuin(new(25, -2), -160, (new(-4, 0), new(6, 0)), (new(6, 0), new(6, 6)));
        scene.AddRuin(new(5, 18), 5, (new(-6, 0), new(4, 0)));
        scene.AddBox(new(-14, 15), new(7, 3, 3), 20);
        scene.AddBox(new(-30, -12), new(3, 6, 2.5f), 0);
        scene.AddBox(new(17, -21), new(6, 2.5f, 2.5f), -10);
        scene.AddBox(new(34, 4), new(2.5f, 6, 3), 0);
        scene.AddBunker(new(-10, -21), 3f);
        scene.AddForest(new(-18, -4), 5f, null, new(-2, -1.5f), new(1.5f, -2), new(0, 2.2f), new(2.6f, 1.2f), new(-2.6f, 1.8f),
            new(-0.4f, -0.2f), new(-1.2f, 0.9f), new(1.1f, 0.4f), new(0.3f, -2.9f), new(-3.0f, -0.1f));
        scene.AddForest(new(20, 11), 4.5f, null, new(-1.8f, 0), new(1.5f, 1.5f), new(1.0f, -2.0f), new(-0.5f, 2.6f),
            new(0, 0.3f), new(-0.8f, -1.6f), new(2.4f, -0.4f), new(0.9f, 2.9f));
        scene.AddRubble(new(6, -2), 3.2f);
        scene.AddBox(new(2, -6), new(6, 0.8f, 1.2f), -25, intensity: 0.9f);    // barricades
        scene.AddBox(new(-14, 8), new(5, 0.8f, 1.2f), 10, intensity: 0.9f);
        return scene;
    }

    /// <summary>
    /// A fresh random layout: three ruins, two forests, a bunker, three containers, a rubble field and two
    /// barricades, each a random size, shape and rotation, dropped where they fit - at least
    /// <see cref="PieceGap"/> from every other piece and an inch inside the table. Largest first, so the
    /// small ones fill the gaps; a piece that finds no room after a couple of hundred tries is left out.
    /// Every piece also leaves the room <paramref name="keepOut"/> asks for (#464: the menu places its units
    /// first and fits the terrain round them).
    /// </summary>
    public static HoloTableScene BuildRandom(Random rng, HoloKeepOut? keepOut = null)
    {
        keepOut ??= HoloKeepOut.None;
        var scene = new HoloTableScene();
        scene.AddTable();
        var placed = new List<IReadOnlyList<Vector2>>();
        float U(float lo, float hi) => lo + rng.NextSingle() * (hi - lo);

        bool TryPlace(float reach, Func<Vector2, float, IReadOnlyList<IReadOnlyList<Vector2>>> outline, Action<Vector2, float> add)
        {
            for (int attempt = 0; attempt < 200; attempt++)
            {
                var centre = new Vector2(U(-HalfW + reach + 1f, HalfW - reach - 1f), U(-HalfD + reach + 1f, HalfD - reach - 1f));
                float yaw = U(0f, 360f);
                var polys = outline(centre, yaw);
                bool onTable = polys.All(poly => poly.All(v => MathF.Abs(v.X) < HalfW - 1f && MathF.Abs(v.Y) < HalfD - 1f));
                if (!onTable || polys.Any(poly => placed.Any(other => !HoloGeometry.BoundsApart(poly, other, PieceGap)
                                                                      && HoloGeometry.PolygonPolygon(poly, other) < PieceGap))) continue;
                if (!polys.All(keepOut.Allows)) continue;
                placed.AddRange(polys);
                add(centre, yaw);
                return true;
            }
            return false;
        }

        for (int i = 0; i < 3; i++)
        {
            (Vector2, Vector2)[] legs;
            if (rng.Next(3) == 0)
            {
                float len = U(8f, 11f);
                legs = new[] { (new Vector2(-len / 2f, 0f), new Vector2(len / 2f, 0f)) };
            }
            else
            {
                float a = U(7f, 10f), b = U(4f, 7f), end = rng.Next(2) == 0 ? -a / 2f : a / 2f;
                legs = new[] { (new Vector2(-a / 2f, 0f), new Vector2(a / 2f, 0f)), (new Vector2(end, 0f), new Vector2(end, b)) };
            }
            // An L's far corner sits well past either leg's half-length; reach is the true farthest point.
            float reach = legs.SelectMany(l => new[] { l.Item1, l.Item2 }).Max(p => p.Length()) + RuinHalfThick * 1.42f;
            TryPlace(reach, (c, yaw) => RuinFootprints(c, yaw, legs), (c, yaw) => scene.AddRuin(c, yaw, legs));
        }
        for (int i = 0; i < 2; i++)
        {
            float r = U(4f, 5.2f);
            Vector2[] trees = ScatterTrees(rng, r);
            int seed = rng.Next();
            TryPlace(r * 1.1f, (c, _) => new[] { Ring(c, r * 1.1f, 14) }, (c, _) => scene.AddForest(c, r, seed, trees));
        }
        {
            float r = U(2.6f, 3.2f);
            TryPlace(r, (c, _) => new[] { BunkerRing(c, r) }, (c, _) => scene.AddBunker(c, r));
        }
        for (int i = 0; i < 3; i++)
        {
            var size = new Vector3(U(5f, 7f), U(2.5f, 3.2f), U(2.5f, BunkerHeight));
            TryPlace(size.X / 2f + 0.5f, (c, yaw) => new[] { BoxFootprint(c, size, yaw) }, (c, yaw) => scene.AddBox(c, size, yaw));
        }
        {
            float r = U(2.6f, 3.6f);
            int seed = rng.Next();
            TryPlace(r * 1.1f, (c, _) => new[] { Ring(c, r * 1.1f, 16) }, (c, _) => scene.AddRubble(c, r, seed));
        }
        for (int i = 0; i < 2; i++)
        {
            var size = new Vector3(U(4.5f, 6f), 0.8f, 1.2f);
            TryPlace(size.X / 2f + 0.5f, (c, yaw) => new[] { BoxFootprint(c, size, yaw) }, (c, yaw) => scene.AddBox(c, size, yaw, intensity: 0.9f));
        }
        return scene;
    }

    /// <summary>Eight to eleven trees spread inside the inner part of a forest, kept a little apart.</summary>
    private static Vector2[] ScatterTrees(Random rng, float radius)
    {
        int want = 8 + rng.Next(4);
        var trees = new List<Vector2>();
        for (int attempt = 0; attempt < 400 && trees.Count < want; attempt++)
        {
            float a = rng.NextSingle() * 2f * MathF.PI, d = MathF.Sqrt(rng.NextSingle()) * radius * 0.7f;
            var p = new Vector2(MathF.Cos(a), MathF.Sin(a)) * d;
            if (trees.All(t => Vector2.Distance(t, p) > 1.0f)) trees.Add(p);
        }
        return trees.ToArray();
    }

    // ---- footprint outlines (shared by the drawing and the random layout's spacing check) ----

    private static Vector2[] Ring(Vector2 centre, float radius, int sides)
    {
        var ring = new Vector2[sides];
        for (int i = 0; i < sides; i++)
        {
            float a = i * 2f * MathF.PI / sides;
            ring[i] = centre + new Vector2(MathF.Cos(a), MathF.Sin(a)) * radius;
        }
        return ring;
    }

    private static Vector2[] BoxFootprint(Vector2 centre, Vector3 size, float yawDeg)
    {
        float hx = size.X / 2f, hy = size.Y / 2f;
        return new[]
        {
            Rotate(new(-hx, -hy), yawDeg, centre), Rotate(new(hx, -hy), yawDeg, centre),
            Rotate(new(hx, hy), yawDeg, centre), Rotate(new(-hx, hy), yawDeg, centre),
        };
    }

    private const float RuinHalfThick = 0.75f;

    /// <summary>A ruin's outline: a plain rectangle for a single leg; for two legs (the second starting at
    /// the corner), one L-shaped outline with the corner squared off - the block reads as a single piece.</summary>
    private static List<IReadOnlyList<Vector2>> RuinFootprints(Vector2 centre, float yawDeg, (Vector2 A, Vector2 B)[] legs)
    {
        if (legs.Length == 1)
        {
            Vector2 a = Rotate(legs[0].A, yawDeg, centre), b = Rotate(legs[0].B, yawDeg, centre);
            Vector2 n = Perp(b - a) * RuinHalfThick;
            return new() { new[] { a + n, b + n, b - n, a - n } };
        }
        var (c, p1, p2) = RuinCorner(centre, yawDeg, legs);
        Vector2 u1 = Vector2.Normalize(p1 - c) * RuinHalfThick, u2 = Vector2.Normalize(p2 - c) * RuinHalfThick;
        return new() { new[] { c - u1 - u2, p1 - u2, p1 + u2, c + u1 + u2, p2 + u1, p2 - u1 } };
    }

    /// <summary>An L ruin's corner and the far ends of its two legs, on the table.</summary>
    private static (Vector2 Corner, Vector2 End1, Vector2 End2) RuinCorner(Vector2 centre, float yawDeg, (Vector2 A, Vector2 B)[] legs)
    {
        Vector2 corner = legs[1].A;
        Vector2 end1 = Vector2.Distance(legs[0].A, corner) < 0.01f ? legs[0].B : legs[0].A;
        return (Rotate(corner, yawDeg, centre), Rotate(end1, yawDeg, centre), Rotate(legs[1].B, yawDeg, centre));
    }

    private static Vector2 Perp(Vector2 v)
    {
        Vector2 u = Vector2.Normalize(v);
        return new Vector2(-u.Y, u.X);
    }

    private static Vector2[] BunkerRing(Vector2 centre, float radius)
    {
        var ring = new Vector2[8];
        for (int i = 0; i < 8; i++)
        {
            float a = i * MathF.PI / 4f + MathF.PI / 8f;
            ring[i] = centre + new Vector2(MathF.Cos(a), MathF.Sin(a)) * radius;
        }
        return ring;
    }

    private static IReadOnlyList<Vector3> BuildSlabCorners()
    {
        var corners = new List<Vector3>();
        foreach (float sx in new[] { -1f, 1f })
        foreach (float sy in new[] { -1f, 1f })
        foreach (float z in new[] { 0f, -RimDepth })
            corners.Add(new Vector3(sx * HalfW, sy * HalfD, z));
        return corners;
    }

    // ---- table ----

    private void Line(Vector3 a, Vector3 b, float intensity, float width, EHoloTint tint = EHoloTint.Terrain, int piece = -1) =>
        _lines.Add(new HoloLine(a, b, intensity, width, tint, piece));

    private void AddTable()
    {
        Vector3[] top = { new(-HalfW, -HalfD, 0), new(HalfW, -HalfD, 0), new(HalfW, HalfD, 0), new(-HalfW, HalfD, 0) };
        _fills.Add(new HoloFill(top[0], top[1], top[2], top[3], 1f, EHoloTint.Surface));

        // Grid first so the rim draws over it: every inch faintly, every 6" brighter.
        for (int x = -(int)HalfW; x <= HalfW; x++)
            Line(new(x, -HalfD, 0), new(x, HalfD, 0), x % 6 == 0 ? 0.20f : 0.045f, 1f);
        for (int y = -(int)HalfD; y <= HalfD; y++)
            Line(new(-HalfW, y, 0), new(HalfW, y, 0), y % 6 == 0 ? 0.20f : 0.045f, 1f);

        for (int i = 0; i < 4; i++)
        {
            Vector3 a = top[i], b = top[(i + 1) % 4];
            Vector3 down = new(0, 0, -RimDepth);
            Line(a, b, 1f, 3f, EHoloTint.RimTop);
            Line(a + down, b + down, 0.45f, 2f);
            Line(a, a + down, 0.6f, 2f);
        }

        // Inch ticks every 6" (longer every 12"), labelled every 12", along the near long side and the
        // right short side only - the two the camera faces.
        for (int x = -(int)HalfW; x <= HalfW; x += 6)
        {
            bool big = x % 12 == 0;
            float len = big ? 1.4f : 0.7f;
            Line(new(x, -HalfD, 0), new(x, -HalfD - len, 0), big ? 0.8f : 0.5f, 2f);
            if (big && x > -HalfW) _labels.Add(new HoloLabel(new(x, -HalfD - 2.8f, 0), $"{x + (int)HalfW}\""));
        }
        for (int y = -(int)HalfD; y <= HalfD; y += 6)
        {
            bool big = y % 12 == 0;
            float len = big ? 1.4f : 0.7f;
            Line(new(HalfW, y, 0), new(HalfW + len, y, 0), big ? 0.8f : 0.5f, 2f);
            if (big && y > -HalfD) _labels.Add(new HoloLabel(new(HalfW + 3.2f, y, 0), $"{y + (int)HalfD}\""));
        }

        // Corner brackets, lifted a hair so they sit on top of the rim line.
        foreach (float sx in new[] { -1f, 1f })
        foreach (float sy in new[] { -1f, 1f })
        {
            var c = new Vector3(sx * HalfW, sy * HalfD, 0.02f);
            Line(c, c - new Vector3(sx * 4f, 0, 0), 1f, 4f, EHoloTint.Bracket);
            Line(c, c - new Vector3(0, sy * 4f, 0), 1f, 4f, EHoloTint.Bracket);
        }
    }

    // ---- terrain pieces ----

    private static Vector2 Rotate(Vector2 p, float yawDeg, Vector2 centre)
    {
        float a = yawDeg * MathF.PI / 180f;
        return new Vector2(centre.X + p.X * MathF.Cos(a) - p.Y * MathF.Sin(a),
                           centre.Y + p.X * MathF.Sin(a) + p.Y * MathF.Cos(a));
    }

    private static Vector3 At(Vector2 p, float z) => new(p.X, p.Y, z);

    private int BeginPiece(Vector2 centre)
    {
        _pieceCentres.Add(centre);
        return _pieceCentres.Count - 1;
    }

    /// <summary>A container or barricade: a box of <paramref name="size"/> (X, Y footprint, Z height).</summary>
    private void AddBox(Vector2 centre, Vector3 size, float yawDeg, float intensity = 1f)
    {
        int piece = BeginPiece(centre);
        Vector2[] fp = BoxFootprint(centre, size, yawDeg);
        _footprints.Add(new HoloFootprint(piece, fp));
        DrawBox(piece, fp, size.Z, intensity);
    }

    private void DrawBox(int piece, Vector2[] fp, float h, float intensity)
    {
        _fills.Add(new HoloFill(At(fp[0], h), At(fp[1], h), At(fp[2], h), At(fp[3], h), 0.10f * intensity, EHoloTint.Terrain, piece));
        for (int i = 0; i < 4; i++)
        {
            Vector2 a = fp[i], b = fp[(i + 1) % 4];
            Line(At(a, 0), At(b, 0), 0.55f * intensity, 1.5f, piece: piece);
            Line(At(a, h), At(b, h), 1.0f * intensity, 2f, piece: piece);
            Line(At(a, 0), At(a, h), 0.8f * intensity, 1.5f, piece: piece);
        }
    }

    /// <summary>A ruin: a plain flat-topped block, straight or L-shaped, outlined like a Tetris piece -
    /// its outer edges only, no seams between the legs.</summary>
    private void AddRuin(Vector2 centre, float yawDeg, params (Vector2 A, Vector2 B)[] legs)
    {
        const float height = BunkerHeight;
        int piece = BeginPiece(centre);
        List<IReadOnlyList<Vector2>> outline = RuinFootprints(centre, yawDeg, legs);
        foreach (IReadOnlyList<Vector2> fp in outline)
            _footprints.Add(new HoloFootprint(piece, fp));

        // The top as rectangles that tile the outline without overlapping (an L is not convex, so no fan).
        IReadOnlyList<Vector2> o = outline[0];
        if (legs.Length == 1)
            AddTop(o[0], o[1], o[2], o[3]);
        else
        {
            var (c, _, p2) = RuinCorner(centre, yawDeg, legs);
            Vector2 u2 = Vector2.Normalize(p2 - c) * RuinHalfThick;
            AddTop(o[0], o[1], o[2], o[0] + 2f * u2);   // the first leg, corner square included
            AddTop(o[0] + 2f * u2, o[3], o[4], o[5]);   // the second leg beyond it
        }

        foreach (IReadOnlyList<Vector2> fp in outline)
            for (int i = 0; i < fp.Count; i++)
            {
                Vector2 a = fp[i], b = fp[(i + 1) % fp.Count];
                Line(At(a, 0), At(b, 0), 0.55f, 1.5f, piece: piece);
                Line(At(a, height), At(b, height), 1.0f, 2f, piece: piece);
                Line(At(a, 0), At(a, height), 0.8f, 1.5f, piece: piece);
            }

        void AddTop(Vector2 a, Vector2 b, Vector2 c, Vector2 d) =>
            _fills.Add(new HoloFill(At(a, height), At(b, height), At(c, height), At(d, height), 0.10f, EHoloTint.Terrain, piece));
    }

    /// <summary>An octagonal bunker.</summary>
    private void AddBunker(Vector2 centre, float radius)
    {
        const float height = BunkerHeight;
        int piece = BeginPiece(centre);
        Vector2[] ring = BunkerRing(centre, radius);
        _footprints.Add(new HoloFootprint(piece, ring));
        // The roof as a fan of quads from the centre (each a degenerate quad = one triangle).
        for (int i = 0; i < 8; i++)
            _fills.Add(new HoloFill(At(centre, height), At(ring[i], height), At(ring[(i + 1) % 8], height),
                At(centre, height), 0.10f, EHoloTint.Terrain, piece));
        for (int i = 0; i < 8; i++)
        {
            Vector2 a = ring[i], b = ring[(i + 1) % 8];
            Line(At(a, 0), At(b, 0), 0.5f, 1.5f, piece: piece);
            Line(At(a, height), At(b, height), 1.0f, 2f, piece: piece);
            Line(At(a, 0), At(a, height), 0.75f, 1.5f, piece: piece);
        }
    }

    /// <summary>A forest: a stand of trees, each a plain vertical line about the bunker's height. Its
    /// irregular outline is only the footprint (it blocks lines of fire); nothing is drawn round it.</summary>
    private void AddForest(Vector2 centre, float radius, int? seed, params Vector2[] trees)
    {
        int piece = BeginPiece(centre);
        var rng = new Random(seed ?? 2000 + piece);
        const int n = 14;
        var fp = new Vector2[n];
        for (int i = 0; i < n; i++)
        {
            float a = i * 2f * MathF.PI / n;
            fp[i] = centre + new Vector2(MathF.Cos(a), MathF.Sin(a)) * radius * (0.85f + 0.25f * rng.NextSingle());
        }
        _footprints.Add(new HoloFootprint(piece, fp));

        foreach (Vector2 offset in trees)
        {
            Vector2 p = centre + offset;
            Line(At(p, 0), At(p, BunkerHeight * (0.8f + 0.4f * rng.NextSingle())), 0.9f, 2f, EHoloTint.Foliage, piece);
        }
    }

    /// <summary>A rubble field: a scatter of small, low boxes. Area terrain - it does not block, so its
    /// outline goes in <see cref="AreaFootprints"/>, not <see cref="Footprints"/>.</summary>
    private void AddRubble(Vector2 centre, float radius, int? seed = null)
    {
        int piece = BeginPiece(centre);
        _areaFootprints.Add(new HoloFootprint(piece, Ring(centre, radius * 1.1f, 16)));
        var rng = new Random(seed ?? 3000 + piece);
        var placed = new List<Vector2[]>();
        for (int attempt = 0; attempt < 200 && placed.Count < 7; attempt++)
        {
            float a = rng.NextSingle() * 2f * MathF.PI, d = MathF.Sqrt(rng.NextSingle()) * radius * 0.75f;
            var at = centre + new Vector2(MathF.Cos(a), MathF.Sin(a)) * d;
            var size = new Vector3(0.7f + 0.9f * rng.NextSingle(), 0.7f + 0.9f * rng.NextSingle(), 0.4f + 0.8f * rng.NextSingle());
            Vector2[] fp = BoxFootprint(at, size, rng.NextSingle() * 360f);
            if (placed.Any(other => !HoloGeometry.BoundsApart(fp, other, 0.3f) && HoloGeometry.PolygonPolygon(fp, other) < 0.3f)) continue;
            placed.Add(fp);
            DrawBox(piece, fp, size.Z, 0.8f);
        }
    }
}
