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

/// <summary>A blocking piece's outline on the table plane; a piece can have several (one per ruin leg).</summary>
internal readonly record struct HoloFootprint(int Piece, IReadOnlyList<Vector2> Polygon);

/// <summary>
/// The main menu's war table as hologram geometry (#448): a 72"x48" slab with grid, inch ticks and
/// labels, and a fixed layout of wireframe terrain pieces (ruins, containers, a bunker, forests,
/// barricades, a crater). Built once; independent of screen size (the camera projects it).
///
/// <para>The layout is the one signed off in <c>docs/menu-background/mockup-terrain.jpg</c>. Each blocking
/// piece also records its 2D footprint, which the line-of-fire choice for the skirmish (a later slice)
/// tests shots against; the crater is area terrain and has none.</para>
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

    public IReadOnlyList<HoloLine> Lines => _lines;
    public IReadOnlyList<HoloFill> Fills => _fills;
    public IReadOnlyList<HoloLabel> Labels => _labels;

    /// <summary>The blocking pieces' footprints (polygons on the table plane).</summary>
    public IReadOnlyList<HoloFootprint> Footprints => _footprints;

    /// <summary>Centre of every piece, indexed by <see cref="HoloLine.Piece"/>.</summary>
    public IReadOnlyList<Vector2> PieceCentres => _pieceCentres;

    /// <summary>The slab's eight corners: everything the camera must keep on screen.</summary>
    public static IReadOnlyList<Vector3> SlabCorners { get; } = BuildSlabCorners();

    public static HoloTableScene BuildDefault()
    {
        var scene = new HoloTableScene();
        scene.AddTable();

        scene.AddRuin(new(-24, 2), 15, 5.5f, (new(-5, 0), new(5, 0)), (new(-5, 0), new(-5, 7)));
        scene.AddRuin(new(25, -2), -160, 6.0f, (new(-4, 0), new(6, 0)), (new(6, 0), new(6, 6)));
        scene.AddRuin(new(5, 18), 5, 4.5f, (new(-6, 0), new(4, 0)));
        scene.AddBox(new(-14, 15), new(7, 3, 3), 20);
        scene.AddBox(new(-30, -12), new(3, 6, 2.5f), 0);
        scene.AddBox(new(17, -21), new(6, 2.5f, 2.5f), -10);
        scene.AddBox(new(34, 4), new(2.5f, 6, 3), 0);
        scene.AddBunker(new(-10, -21), 3f, 3f);
        scene.AddForest(new(-18, -4), 5f, new(-2, -1.5f), new(1.5f, -2), new(0, 2.2f), new(2.6f, 1.2f), new(-2.6f, 1.8f));
        scene.AddForest(new(20, 11), 4.5f, new(-1.8f, 0), new(1.5f, 1.5f), new(1.0f, -2.0f), new(-0.5f, 2.6f));
        scene.AddCrater(new(6, -2), 3.2f);
        scene.AddBox(new(2, -6), new(6, 0.8f, 1.2f), -25, intensity: 0.9f);    // barricades
        scene.AddBox(new(-14, 8), new(5, 0.8f, 1.2f), 10, intensity: 0.9f);
        return scene;
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

        // Inch ticks every 6" around the rim (longer every 12"), labelled every 12" along the near long
        // side and the right short side - the two the camera faces.
        for (int x = -(int)HalfW; x <= HalfW; x += 6)
        {
            bool big = x % 12 == 0;
            float len = big ? 1.4f : 0.7f;
            Line(new(x, -HalfD, 0), new(x, -HalfD - len, 0), big ? 0.8f : 0.5f, 2f);
            Line(new(x, HalfD, 0), new(x, HalfD + len, 0), 0.5f, 1f);
            if (big && x > -HalfW) _labels.Add(new HoloLabel(new(x, -HalfD - 2.8f, 0), $"{x + (int)HalfW}\""));
        }
        for (int y = -(int)HalfD; y <= HalfD; y += 6)
        {
            bool big = y % 12 == 0;
            float len = big ? 1.4f : 0.7f;
            Line(new(-HalfW, y, 0), new(-HalfW - len, y, 0), big ? 0.8f : 0.5f, 2f);
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
        float hx = size.X / 2f, hy = size.Y / 2f, h = size.Z;
        Vector2[] fp =
        {
            Rotate(new(-hx, -hy), yawDeg, centre), Rotate(new(hx, -hy), yawDeg, centre),
            Rotate(new(hx, hy), yawDeg, centre), Rotate(new(-hx, hy), yawDeg, centre),
        };
        _footprints.Add(new HoloFootprint(piece, fp));
        _fills.Add(new HoloFill(At(fp[0], h), At(fp[1], h), At(fp[2], h), At(fp[3], h), 0.10f * intensity, EHoloTint.Terrain, piece));
        for (int i = 0; i < 4; i++)
        {
            Vector2 a = fp[i], b = fp[(i + 1) % 4];
            Line(At(a, 0), At(b, 0), 0.55f * intensity, 1.5f, piece: piece);
            Line(At(a, h), At(b, h), 1.0f * intensity, 2f, piece: piece);
            Line(At(a, 0), At(a, h), 0.8f * intensity, 1.5f, piece: piece);
        }
    }

    /// <summary>A ruined wall run: each leg is a thin wall with a broken, jagged top and window holes.</summary>
    private void AddRuin(Vector2 centre, float yawDeg, float height, params (Vector2 A, Vector2 B)[] legs)
    {
        int piece = BeginPiece(centre);
        var rng = new Random(1000 + piece);
        const float halfThick = 0.35f;
        foreach (var (la, lb) in legs)
        {
            Vector2 a = Rotate(la, yawDeg, centre), b = Rotate(lb, yawDeg, centre);
            Vector2 along = b - a;
            float length = along.Length();
            Vector2 u = along / length;
            Vector2 n = new Vector2(-u.Y, u.X) * halfThick;
            _footprints.Add(new HoloFootprint(piece, new[] { a + n, b + n, b - n, a - n }));

            int steps = Math.Max(4, (int)(length / 1.2f));
            var heights = new float[steps + 1];
            for (int i = 0; i <= steps; i++)
                heights[i] = i == 0 || i == steps
                    ? height * (0.6f + 0.4f * rng.NextSingle())
                    : height * (0.45f + 0.55f * rng.NextSingle());

            for (int i = 0; i <= steps; i++)
            {
                Vector2 p = a + along * (i / (float)steps);
                if (i < steps)
                {
                    Vector2 q = a + along * ((i + 1) / (float)steps);
                    foreach (float side in new[] { 1f, -1f })
                    {
                        Vector2 o = n * side;
                        Line(At(p + o, heights[i]), At(q + o, heights[i + 1]), 1f, 2f, piece: piece);
                        Line(At(p + o, 0), At(q + o, 0), 0.5f, 1.5f, piece: piece);
                    }
                    _fills.Add(new HoloFill(At(p, 0), At(q, 0), At(q, heights[i + 1]), At(p, heights[i]), 0.07f, EHoloTint.Terrain, piece));

                    // A window in every other panel tall enough to hold one.
                    if (i > 0 && i < steps - 1 && i % 2 == 1 && heights[i] > 2.6f)
                    {
                        Vector2 m = (p + q) * 0.5f;
                        Vector2 w0 = m - u * 0.35f, w1 = m + u * 0.35f;
                        Line(At(w0, 1.2f), At(w1, 1.2f), 0.6f, 1.2f, piece: piece);
                        Line(At(w0, 2.2f), At(w1, 2.2f), 0.6f, 1.2f, piece: piece);
                        Line(At(w0, 1.2f), At(w0, 2.2f), 0.6f, 1.2f, piece: piece);
                        Line(At(w1, 1.2f), At(w1, 2.2f), 0.6f, 1.2f, piece: piece);
                    }
                }
                if (i == 0 || i == steps || i % 2 == 0)
                    Line(At(p, 0), At(p, heights[i]), 0.7f, 1.5f, piece: piece);
            }
        }
    }

    /// <summary>An octagonal bunker with a firing slit.</summary>
    private void AddBunker(Vector2 centre, float radius, float height)
    {
        int piece = BeginPiece(centre);
        var ring = new Vector2[8];
        for (int i = 0; i < 8; i++)
        {
            float a = i * MathF.PI / 4f + MathF.PI / 8f;
            ring[i] = centre + new Vector2(MathF.Cos(a), MathF.Sin(a)) * radius;
        }
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
        Vector2 s0 = Vector2.Lerp(ring[1], ring[2], 0.2f), s1 = Vector2.Lerp(ring[1], ring[2], 0.8f);
        foreach (float z in new[] { height * 0.6f, height * 0.75f })
            Line(At(s0, z), At(s1, z), 0.9f, 1.2f, piece: piece);
    }

    /// <summary>A forest: a dashed, irregular boundary with stacked-cone wireframe trees inside.</summary>
    private void AddForest(Vector2 centre, float radius, params Vector2[] trees)
    {
        int piece = BeginPiece(centre);
        var rng = new Random(2000 + piece);
        const int n = 14;
        var fp = new Vector2[n];
        for (int i = 0; i < n; i++)
        {
            float a = i * 2f * MathF.PI / n;
            fp[i] = centre + new Vector2(MathF.Cos(a), MathF.Sin(a)) * radius * (0.85f + 0.25f * rng.NextSingle());
        }
        _footprints.Add(new HoloFootprint(piece, fp));
        for (int i = 0; i < n; i += 2)
            Line(At(fp[i], 0), At(fp[(i + 1) % n], 0), 0.8f, 2f, EHoloTint.Foliage, piece);

        foreach (Vector2 offset in trees)
        {
            Vector2 p = centre + offset;
            float treeHeight = 5f + 1.5f * rng.NextSingle();
            Line(At(p, 0), At(p, 1.4f), 0.7f, 1.5f, EHoloTint.Foliage, piece);
            foreach (var (z, r) in new[] { (1.4f, 1.5f), (2.8f, 1.15f), (4.0f, 0.8f) })
            {
                var ring = new Vector3[12];
                for (int i = 0; i < 12; i++)
                {
                    float a = i * 2f * MathF.PI / 12f;
                    ring[i] = new Vector3(p.X + r * MathF.Cos(a), p.Y + r * MathF.Sin(a), z);
                }
                for (int i = 0; i < 12; i++)
                    Line(ring[i], ring[(i + 1) % 12], 0.75f, 1.3f, EHoloTint.Foliage, piece);
                for (int i = 0; i < 12; i += 3)
                    Line(ring[i], At(p, MathF.Min(treeHeight, z + 2.2f)), 0.85f, 1.3f, EHoloTint.Foliage, piece);
            }
        }
    }

    /// <summary>A crater: concentric irregular rings on the table, the inner two dashed. Area terrain - it
    /// does not block, so it has no footprint.</summary>
    private void AddCrater(Vector2 centre, float radius)
    {
        int piece = BeginPiece(centre);
        var rng = new Random(3000 + piece);
        foreach (var (r, intensity, dashed) in new[] { (radius, 0.9f, false), (radius * 0.62f, 0.55f, true), (radius * 0.3f, 0.4f, true) })
        {
            var ring = new Vector3[24];
            for (int i = 0; i < 24; i++)
            {
                float a = i * 2f * MathF.PI / 24f;
                float rr = r * (0.9f + 0.2f * rng.NextSingle());
                ring[i] = new Vector3(centre.X + rr * MathF.Cos(a), centre.Y + rr * MathF.Sin(a), 0.03f);
            }
            for (int i = 0; i < 24; i++)
                if (!dashed || i % 2 == 0)
                    Line(ring[i], ring[(i + 1) % 24], intensity, 1.5f, piece: piece);
        }
    }
}
