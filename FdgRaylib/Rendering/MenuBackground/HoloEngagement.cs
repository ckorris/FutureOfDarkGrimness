using System.Numerics;

namespace FdgRaylib.Rendering.MenuBackground;

/// <summary>One line of fire on the menu's war table: a shooting model's base centre to a target model's.</summary>
internal sealed record HoloShot(HoloUnit Shooter, HoloUnit Target, Vector2 From, Vector2 To);

/// <summary>A base-to-base measuring line and the distance it reads, in inches.</summary>
internal sealed record HoloMeasure(HoloUnit A, HoloUnit B, Vector2 From, Vector2 To, float Inches);

/// <summary>
/// What is happening on the menu's war table (#448 slice 3): who is shooting whom, where the fire is,
/// which two units are being measured between, and where the radar pulse starts. Pure geometry over a
/// <see cref="HoloSkirmish"/> and the terrain footprints; the renderer animates it.
///
/// <para>Line of fire is the simple local check the owner chose (2026-10-30): a shot or measure is
/// allowed only if its 2D segment stays at least <see cref="ClearancePad"/> from every blocking piece's
/// footprint. It is cosmetic and deliberately not the engine's LOS - nothing here is a rules call.</para>
/// </summary>
internal sealed class HoloEngagement
{
    /// <summary>How far a drawn line must stay from terrain, in inches - so a tracer never grazes a wall.</summary>
    public const float ClearancePad = 0.4f;

    /// <summary>Longest fight the planner will pick, centre to centre (a typical rifle reach).</summary>
    public const float MaxShotRange = 30f;

    private const int MaxFights = 3;

    public IReadOnlyList<HoloShot> Shots { get; }
    public HoloUnit? Burning { get; }
    public Vector2 FireAt { get; }
    public HoloMeasure? Measure { get; }
    public Vector2 PulseCentre { get; }

    private HoloEngagement(IReadOnlyList<HoloShot> shots, HoloUnit? burning, Vector2 fireAt, HoloMeasure? measure, Vector2 pulseCentre)
    {
        Shots = shots;
        Burning = burning;
        FireAt = fireAt;
        Measure = measure;
        PulseCentre = pulseCentre;
    }

    public static HoloEngagement Plan(HoloSkirmish skirmish, IReadOnlyList<HoloFootprint> footprints)
    {
        var squads = skirmish.Units.Where(u => !u.IsVehicle).ToList();

        // Every enemy pairing in range whose two lines of fire are both clear, nearest first; then the
        // closest fights with a different shooter each (a unit fires at one target).
        var candidates = new List<(HoloUnit S, HoloUnit T, List<HoloShot> Lines, float Dist)>();
        foreach (HoloUnit s in squads)
        foreach (HoloUnit t in squads.Where(t => t.Side != s.Side))
        {
            float dist = Vector2.Distance(s.Centre, t.Centre);
            if (dist > MaxShotRange) continue;
            List<HoloShot> lines = ShotLines(s, t);
            if (lines.All(l => IsClear(l.From, l.To, footprints))) candidates.Add((s, t, lines, dist));
        }

        var shots = new List<HoloShot>();
        var shooters = new HashSet<HoloUnit>();
        foreach (var c in candidates.OrderBy(c => c.Dist).ThenBy(c => c.S.Label, StringComparer.Ordinal))
        {
            if (shooters.Count == MaxFights) break;
            if (!shooters.Add(c.S)) continue;
            shots.AddRange(c.Lines);
        }

        // The fire goes on a target that has already lost models, if one is under fire; else the first.
        var targets = shots.Select(s => s.Target).Distinct().ToList();
        HoloUnit? burning = targets.FirstOrDefault(t => t.Dead > 0) ?? targets.FirstOrDefault();
        Vector2 fireAt = burning is null ? Vector2.Zero
            : burning.Dead > 0 ? Mean(burning.Models.Take(burning.Dead)) : burning.Alive.First();

        var engaged = shots.SelectMany(s => new[] { s.Shooter, s.Target }).Distinct().ToList();
        Vector2 pulse = engaged.Count > 0 ? Mean(engaged.Select(u => u.Centre)) : Vector2.Zero;

        return new HoloEngagement(shots, burning, fireAt, PlanMeasure(skirmish, footprints, engaged), pulse);
    }

    /// <summary>The two vehicles if their line is clear, else the closest clear pair of units not already
    /// fighting; base edge to base edge, the way the rules measure.</summary>
    private static HoloMeasure? PlanMeasure(HoloSkirmish skirmish, IReadOnlyList<HoloFootprint> footprints, IReadOnlyList<HoloUnit> engaged)
    {
        var pairs = new List<(HoloUnit A, HoloUnit B)>();
        var vehicles = skirmish.Units.Where(u => u.IsVehicle).ToList();
        if (vehicles.Count(v => v.Side == 0) > 0 && vehicles.Count(v => v.Side == 1) > 0)
            pairs.Add((vehicles.First(v => v.Side == 0), vehicles.First(v => v.Side == 1)));
        var idle = skirmish.Units.Where(u => !engaged.Contains(u)).ToList();
        pairs.AddRange(
            from a in idle from b in idle
            where a.Side == 0 && b.Side == 1
            orderby Vector2.Distance(a.Centre, b.Centre)
            select (a, b));

        foreach (var (a, b) in pairs)
        {
            HoloMeasure m = BaseToBase(a, b);
            if (IsClear(m.From, m.To, footprints)) return m;
        }
        return null;
    }

    private static HoloMeasure BaseToBase(HoloUnit a, HoloUnit b)
    {
        var (p, q) = (from x in a.Alive from y in b.Alive orderby Vector2.DistanceSquared(x, y) select (x, y)).First();
        float d = Vector2.Distance(p, q);
        Vector2 u = (q - p) / d;
        return new HoloMeasure(a, b, p + u * a.BaseRadius, q - u * b.BaseRadius, d - a.BaseRadius - b.BaseRadius);
    }

    /// <summary>Two lines of fire: the shooter's two models nearest the target, each to one of the target's
    /// two models nearest the shooter.</summary>
    private static List<HoloShot> ShotLines(HoloUnit s, HoloUnit t)
    {
        var from = s.Alive.OrderBy(m => Vector2.DistanceSquared(m, t.Centre)).Take(2).ToList();
        var to = t.Alive.OrderBy(m => Vector2.DistanceSquared(m, s.Centre)).Take(2).ToList();
        return Enumerable.Range(0, 2).Select(i => new HoloShot(s, t, from[i % from.Count], to[i % to.Count])).ToList();
    }

    /// <summary>True when the segment a-b keeps at least <paramref name="pad"/> inches from every footprint
    /// (neither end inside one, no edge within the pad).</summary>
    public static bool IsClear(Vector2 a, Vector2 b, IReadOnlyList<HoloFootprint> footprints, float pad = ClearancePad)
    {
        foreach (HoloFootprint fp in footprints)
        {
            IReadOnlyList<Vector2> poly = fp.Polygon;
            if (Contains(poly, a) || Contains(poly, b)) return false;
            for (int i = 0; i < poly.Count; i++)
                if (SegmentDistance(a, b, poly[i], poly[(i + 1) % poly.Count]) < pad) return false;
        }
        return true;
    }

    private static Vector2 Mean(IEnumerable<Vector2> pts)
    {
        var list = pts.ToList();
        return list.Aggregate(Vector2.Zero, (s, p) => s + p) / list.Count;
    }

    private static bool Contains(IReadOnlyList<Vector2> poly, Vector2 p)
    {
        bool inside = false;
        for (int i = 0, j = poly.Count - 1; i < poly.Count; j = i++)
            if ((poly[i].Y > p.Y) != (poly[j].Y > p.Y) &&
                p.X < (poly[j].X - poly[i].X) * (p.Y - poly[i].Y) / (poly[j].Y - poly[i].Y) + poly[i].X)
                inside = !inside;
        return inside;
    }

    private static float PointSegment(Vector2 p, Vector2 a, Vector2 b)
    {
        Vector2 ab = b - a;
        float len2 = ab.LengthSquared();
        float t = len2 == 0f ? 0f : Math.Clamp(Vector2.Dot(p - a, ab) / len2, 0f, 1f);
        return Vector2.Distance(p, a + ab * t);
    }

    private static float SegmentDistance(Vector2 a, Vector2 b, Vector2 c, Vector2 d)
    {
        static float Cross(Vector2 o, Vector2 p, Vector2 q) => (p.X - o.X) * (q.Y - o.Y) - (p.Y - o.Y) * (q.X - o.X);
        float d1 = Cross(c, d, a), d2 = Cross(c, d, b), d3 = Cross(a, b, c), d4 = Cross(a, b, d);
        if ((d1 > 0) != (d2 > 0) && (d3 > 0) != (d4 > 0)) return 0f;   // they cross
        return MathF.Min(MathF.Min(PointSegment(a, c, d), PointSegment(b, c, d)),
                         MathF.Min(PointSegment(c, a, b), PointSegment(d, a, b)));
    }
}
