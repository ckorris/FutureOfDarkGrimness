using System.Numerics;

namespace FdgRaylib.Rendering.MenuBackground;

/// <summary>One line of fire on the menu's war table: a shooting model's base centre to a target model's.</summary>
internal sealed record HoloShot(HoloUnit Shooter, HoloUnit Target, Vector2 From, Vector2 To)
{
    /// <summary>Battle time the shooting starts: once the table has faded in and both units have reached
    /// their spots - a unit that advances moves first, then switches to shooting.</summary>
    public float StartsAt => MathF.Max(HoloDirector.FightsFrom, MathF.Max(Shooter.ArriveAt, Target.ArriveAt));
}

/// <summary>
/// Two units fighting in base contact (#464): one a side, both of them <see cref="EHoloRole.Charges"/>
/// units of the same <see cref="HoloUnit.MeleeGroup"/>. <see cref="Contacts"/> are the points where their
/// front ranks meet - where the clash sparks.
/// </summary>
internal sealed record HoloMelee(HoloUnit A, HoloUnit B, IReadOnlyList<Vector2> Contacts)
{
    /// <summary>Battle time the two meet (they arrive together) and the sparks start.</summary>
    public float StartsAt => MathF.Max(A.ArriveAt, B.ArriveAt);
}

/// <summary>A base-to-base measuring line and the distance it reads, in inches.</summary>
internal sealed record HoloMeasure(HoloUnit A, HoloUnit B, Vector2 From, Vector2 To, float Inches)
{
    /// <summary>Battle time the line starts to draw: a second after both units are standing still.</summary>
    public float StartsAt => MathF.Max(HoloDirector.FightsFrom, MathF.Max(A.ArriveAt, B.ArriveAt)) + 1f;
}

/// <summary>
/// What is happening on the menu's war table (#448 slice 3): who is shooting whom, which units are locked
/// in melee and where they touch (#464), which two units are being measured between, and where the
/// camera's close-up goes. Pure geometry over a
/// <see cref="HoloSkirmish"/> and the terrain footprints; the renderer animates it. (The flames on the
/// stricken unit were taken out at the owner's request, 2026-10-30.)
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

    /// <summary>Each fight once: a shooting unit and the unit it fires at.</summary>
    public IReadOnlyList<(HoloUnit Shooter, HoloUnit Target)> Fights { get; }

    /// <summary>Each melee, then each pair of units trading fire, once however many ways they shoot - the
    /// close-ups.</summary>
    public IReadOnlyList<(HoloUnit A, HoloUnit B)> Clashes { get; }

    public HoloMeasure? Measure { get; }

    /// <summary>The melees (#464), each pair of units once.</summary>
    public IReadOnlyList<HoloMelee> Melees { get; }

    private HoloEngagement(IReadOnlyList<HoloShot> shots, HoloMeasure? measure, IReadOnlyList<HoloMelee> melees)
    {
        Shots = shots;
        Fights = shots.Select(s => (s.Shooter, s.Target)).Distinct().ToList();
        // A melee is the busiest thing on the table, so it heads the camera's list of close-ups.
        Clashes = melees.Select(m => (m.A, m.B))
            .Concat(Fights.Where(f => !Fights.Any(g => g.Shooter == f.Target && g.Target == f.Shooter)
                                      || string.CompareOrdinal(f.Shooter.Label, f.Target.Label) < 0)).ToList();
        Measure = measure;
        Melees = melees;
    }

    /// <summary>Margin round a fight's models in the camera's close-up, and the smallest close-up, inches.</summary>
    private const float FocusMargin = 4f;
    private static readonly Vector2 MinFocusHalf = new(12f, 9f);

    /// <summary>
    /// Where the camera's close-up looks (owner, 2026-10-30: "always zoom somewhere where there's
    /// fighting"): a box round every model of one clash - clash <paramref name="index"/>, wrapping, so
    /// successive battles look at different fights - with a margin, never smaller than a dozen inches
    /// across. Null when nothing is fighting.
    /// </summary>
    public (Vector2 Centre, Vector2 Half)? FocusBox(int index)
    {
        if (Clashes.Count == 0) return null;
        var (a, b) = Clashes[((index % Clashes.Count) + Clashes.Count) % Clashes.Count];
        var pts = a.Models.Concat(b.Models).ToList();
        var min = new Vector2(pts.Min(p => p.X), pts.Min(p => p.Y)) - new Vector2(FocusMargin);
        var max = new Vector2(pts.Max(p => p.X), pts.Max(p => p.Y)) + new Vector2(FocusMargin);
        return ((min + max) * 0.5f, Vector2.Max((max - min) * 0.5f, MinFocusHalf));
    }

    public static HoloEngagement Plan(HoloSkirmish skirmish, IReadOnlyList<HoloFootprint> footprints) =>
        Plan(skirmish.Units, footprints);

    /// <summary>Plans over any set of units (tests build their own; the menu passes a skirmish's).</summary>
    public static HoloEngagement Plan(IReadOnlyList<HoloUnit> units, IReadOnlyList<HoloFootprint> footprints)
    {
        // Patrollers never stop moving, so they never shoot, and units in melee are busy. Everyone else
        // does - from where they end up; an advancer's shots wait until it (and its target) have arrived
        // (HoloShot.StartsAt).
        var squads = units.Where(u => u.Role is EHoloRole.Holds or EHoloRole.Advances).ToList();

        // Every enemy pairing in range where at least one model can see the target, nearest first; then
        // the closest fights with a different shooter each (a unit fires at one target).
        var candidates = new List<(HoloUnit S, HoloUnit T, List<HoloShot> Lines, float Dist)>();
        foreach (HoloUnit s in squads)
        foreach (HoloUnit t in squads.Where(t => t.Side != s.Side))
        {
            float dist = Vector2.Distance(s.Centre, t.Centre);
            if (dist > MaxShotRange) continue;
            List<HoloShot> lines = ShotLines(s, t, footprints);
            if (lines.Count > 0) candidates.Add((s, t, lines, dist));
        }

        var shots = new List<HoloShot>();
        var shooters = new HashSet<HoloUnit>();
        foreach (var c in candidates.OrderBy(c => c.Dist).ThenBy(c => c.S.Label, StringComparer.Ordinal))
        {
            if (shooters.Count == MaxFights) break;
            if (!shooters.Add(c.S)) continue;
            shots.AddRange(c.Lines);
        }

        var engaged = shots.SelectMany(s => new[] { s.Shooter, s.Target }).Distinct().ToList();
        return new HoloEngagement(shots, PlanMeasure(units, footprints, engaged), PlanMelees(units));
    }

    /// <summary>Every melee: the two units of each <see cref="HoloUnit.MeleeGroup"/>, near side first, with
    /// the points where their living front ranks touch. A group with no touching bases has no melee.</summary>
    private static List<HoloMelee> PlanMelees(IReadOnlyList<HoloUnit> units)
    {
        var melees = new List<HoloMelee>();
        foreach (var group in units.Where(u => u.Role == EHoloRole.Charges && u.MeleeGroup > 0).GroupBy(u => u.MeleeGroup).OrderBy(g => g.Key))
        {
            HoloUnit? a = group.FirstOrDefault(u => u.Side == 0), b = group.FirstOrDefault(u => u.Side == 1);
            if (a is null || b is null) continue;
            var contacts = ContactPoints(a.Alive, a.BaseRadius, b.Alive, b.BaseRadius);
            if (contacts.Count > 0) melees.Add(new HoloMelee(a, b, contacts));
        }
        return melees;
    }

    /// <summary>Most spark points one melee shows: the closest pairs of bases.</summary>
    public const int MaxContacts = 6;

    /// <summary>How close two bases' edges must be to count as touching, inches (the generator stands
    /// them <see cref="HoloSkirmish.ContactGap"/> apart; a staggered rank sits a little further).</summary>
    private const float TouchReach = 0.6f;

    /// <summary>
    /// Where two units' bases touch: for every pair of bases, one from each, whose edges are within
    /// <see cref="TouchReach"/>, the point midway across the gap between them - closest pairs first, at
    /// most <see cref="MaxContacts"/>. Each base of the unit with more bases is used once; a base of the
    /// other may meet several (a vehicle's big base meets a whole front rank), never one base twice.
    /// </summary>
    public static List<Vector2> ContactPoints(IEnumerable<Vector2> a, float ra, IEnumerable<Vector2> b, float rb)
    {
        var pairs = (
            from p in a from q in b
            let gap = Vector2.Distance(p, q) - ra - rb
            where gap < TouchReach
            orderby gap
            select (p, q, gap)).ToList();
        bool aIsMany = a.Count() >= b.Count();
        var used = new HashSet<Vector2>();
        var points = new List<Vector2>();
        foreach (var (p, q, gap) in pairs)
        {
            if (points.Count == MaxContacts) break;
            if (!used.Add(aIsMany ? p : q)) continue;
            Vector2 u = Vector2.Normalize(q - p);
            points.Add(p + u * (ra + MathF.Max(gap, 0f) / 2f));
        }
        return points;
    }

    /// <summary>A measuring line between two units that end up standing still (never a patroller, nor a
    /// unit in melee):
    /// preferably two not already fighting, else any; nearest clear pair first; base edge to base edge,
    /// the way the rules measure.</summary>
    private static HoloMeasure? PlanMeasure(IReadOnlyList<HoloUnit> units, IReadOnlyList<HoloFootprint> footprints, IReadOnlyList<HoloUnit> engaged)
    {
        var still = units.Where(u => u.Role is EHoloRole.Holds or EHoloRole.Advances).ToList();
        var pairs = (
            from a in still from b in still
            where a.Side == 0 && b.Side == 1
            orderby engaged.Contains(a) || engaged.Contains(b), Vector2.Distance(a.Centre, b.Centre)
            select (a, b)).ToList();

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

    /// <summary>
    /// The unit's volley: EVERY living model that can see the target fires (owner, 2026-10-30 - not just
    /// the front two), each at the nearest target model it has a clear line to. A model with no clear
    /// line to any of them holds its fire.
    /// </summary>
    private static List<HoloShot> ShotLines(HoloUnit s, HoloUnit t, IReadOnlyList<HoloFootprint> footprints)
    {
        var lines = new List<HoloShot>();
        foreach (Vector2 from in s.Alive)
        {
            foreach (Vector2 to in t.Alive.OrderBy(x => Vector2.DistanceSquared(x, from)))
            {
                if (!IsClear(from, to, footprints)) continue;
                lines.Add(new HoloShot(s, t, from, to));
                break;
            }
        }
        return lines;
    }

    /// <summary>True when the segment a-b keeps at least <paramref name="pad"/> inches from every footprint
    /// (neither end inside one, no edge within the pad).</summary>
    public static bool IsClear(Vector2 a, Vector2 b, IReadOnlyList<HoloFootprint> footprints, float pad = ClearancePad) =>
        HoloGeometry.IsClear(a, b, footprints, pad);
}
