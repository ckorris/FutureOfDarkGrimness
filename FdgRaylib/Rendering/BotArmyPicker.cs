namespace FdgRaylib.Rendering;

/// <summary>
/// Chooses starter armies for the lobby's bots (#372). Pure: it takes the catalog, the points limit and
/// what everyone else is already using, and returns an army - no file IO, no ImGui - so the ranking and
/// the no-repeats rotation are unit-tested directly.
///
/// <para>Five rules, in priority order:</para>
/// <list type="number">
///   <item>Never hand out an army OVER the points limit while the folder holds a legal one - the #153
///   launch gate would flag it, so it is not a usable pick at all.</item>
///   <item>Among the legal ones, prefer those CLOSE to the limit, closest first.</item>
///   <item>Treat everything inside the band (#388) as equally good and pick one at RANDOM. Closest-first
///   alone made the opening pick of every lobby the same file - the bundled folder has five armies at
///   exactly 1000 points, so the path tiebreak decided it, and it was the same one every game.</item>
///   <item>Skip armies another player is already using - unless that would leave nothing, in which case
///   a duplicate is better than no army.</item>
///   <item>Don't show a player the same army twice until it has cycled through all of them; the cycle
///   then starts over.</item>
/// </list>
/// </summary>
public sealed class BotArmyPicker
{
    /// <summary>
    /// #388: how far under the points limit an army may be and still count as "built for this game".
    /// Armies inside the band are interchangeable and the pick among them is random; below it, the
    /// closest-first order still stands, so a 1000-pt list is never offered in a 2000-pt lobby while a
    /// 1900-pt one is eligible.
    /// </summary>
    public const int BandPercentUnderLimit = 5;

    private readonly IReadOnlyList<ArmyCatalogEntry> _catalog;
    private readonly Random _rng;

    // Per player slot: the army Keys already handed to it this lobby. Cleared once it has seen them all,
    // which is what makes the re-roll button a rotation rather than a random walk.
    private readonly Dictionary<Guid, HashSet<string>> _shown = new();

    // The limit every rotation in _shown was built against. Moving it invalidates all of them: both
    // which armies are legal and which is closest are answers to this number.
    private int? _pointsLimit;

    /// <param name="rng">Injected so the band's random pick is reproducible under test; the lobby
    /// leaves it null and gets <see cref="Random.Shared"/>.</param>
    public BotArmyPicker(IReadOnlyList<ArmyCatalogEntry> catalog, Random? rng = null)
    {
        _catalog = catalog;
        _rng = rng ?? Random.Shared;
    }

    /// <summary>
    /// The catalog ordered best-first for <paramref name="pointsLimit"/>: legal armies before over-limit
    /// ones, then by distance from the limit, then by path so the order never depends on hash iteration.
    /// </summary>
    public static IReadOnlyList<ArmyCatalogEntry> Rank(
        IReadOnlyList<ArmyCatalogEntry> catalog, int pointsLimit) =>
        catalog
            .OrderBy(a => a.Points > pointsLimit ? 1 : 0)
            .ThenBy(a => Math.Abs(pointsLimit - a.Points))
            .ThenBy(a => a.Path, StringComparer.Ordinal)
            .ToList();

    /// <summary>
    /// The next army for <paramref name="slot"/>, or null when the catalog is empty.
    /// </summary>
    /// <param name="inUseByOthers"><see cref="ArmyCatalogEntry.Key"/>s held by every OTHER player.</param>
    public ArmyCatalogEntry? PickNext(Guid slot, int pointsLimit, IReadOnlySet<string> inUseByOthers)
    {
        if (_catalog.Count == 0) return null;

        // The lobby's limit moved, so every rotation recorded against the old one is stale.
        if (_pointsLimit != pointsLimit)
        {
            _pointsLimit = pointsLimit;
            _shown.Clear();
        }

        IReadOnlyList<ArmyCatalogEntry> ranked = Rank(_catalog, pointsLimit);

        // Over-limit armies are a LAST RESORT, not merely a low-ranked option. Ranking alone used to be
        // the whole rule, which held right up until a slot had seen every legal army - at that point the
        // first unseen entry was an illegal one, and re-rolling started walking through armies the launch
        // gate rejects. Restarting the legal cycle is the correct answer there.
        List<ArmyCatalogEntry> legal = ranked.Where(army => army.Points <= pointsLimit).ToList();
        IReadOnlyList<ArmyCatalogEntry> pool = legal.Count > 0 ? legal : ranked;

        HashSet<string> shown = _shown.TryGetValue(slot, out HashSet<string>? s) ? s : _shown[slot] = new();

        int bandFloor = pointsLimit - pointsLimit * BandPercentUnderLimit / 100;

        ArmyCatalogEntry? pick =
            Choose(pool, a => !shown.Contains(a.Key) && !inUseByOthers.Contains(a.Key), bandFloor, pointsLimit);

        // Everything unseen is taken by someone else, or this slot has now seen the whole pool. Start
        // the rotation over - still preferring an army nobody else holds.
        if (pick is null)
        {
            shown.Clear();
            pick = Choose(pool, a => !inUseByOthers.Contains(a.Key), bandFloor, pointsLimit)
                // Fewer distinct armies than players: a duplicate beats leaving the bot army-less.
                ?? Choose(pool, _ => true, bandFloor, pointsLimit);
        }

        shown.Add(pick.Value.Key);
        return pick;
    }

    /// <summary>Drops a slot's rotation history (the player left, or its army was set by hand).</summary>
    public void Forget(Guid slot) => _shown.Remove(slot);

    /// <summary>
    /// An eligible army from <paramref name="pool"/> (already ranked): a RANDOM one from inside the band
    /// when the band holds any, otherwise the closest eligible one, as before (#388). The band is capped
    /// at the limit as well as floored, so the all-over-limit last-resort pool still goes closest-first
    /// rather than handing out a 5000-pt list because a 2200-pt one was equally "in band".
    /// </summary>
    private ArmyCatalogEntry? Choose(IReadOnlyList<ArmyCatalogEntry> pool,
        Func<ArmyCatalogEntry, bool> eligible, int bandFloor, int pointsLimit)
    {
        List<ArmyCatalogEntry> band = new();
        foreach (ArmyCatalogEntry entry in pool)
        {
            if (entry.Points >= bandFloor && entry.Points <= pointsLimit && eligible(entry))
            {
                band.Add(entry);
            }
        }

        return band.Count > 0 ? band[_rng.Next(band.Count)] : First(pool, eligible);
    }

    private static ArmyCatalogEntry? First(
        IReadOnlyList<ArmyCatalogEntry> ranked, Func<ArmyCatalogEntry, bool> predicate)
    {
        foreach (ArmyCatalogEntry entry in ranked)
        {
            if (predicate(entry)) return entry;
        }
        return null;
    }
}
