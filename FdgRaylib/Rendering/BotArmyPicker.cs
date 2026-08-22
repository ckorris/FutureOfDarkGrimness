namespace FdgRaylib.Rendering;

/// <summary>
/// Chooses starter armies for the lobby's bots (#372). Pure: it takes the catalog, the points limit and
/// what everyone else is already using, and returns an army - no file IO, no ImGui - so the ranking and
/// the no-repeats rotation are unit-tested directly.
///
/// <para>Four rules, in priority order:</para>
/// <list type="number">
///   <item>Never hand out an army OVER the points limit while the folder holds a legal one - the #153
///   launch gate would flag it, so it is not a usable pick at all.</item>
///   <item>Among the legal ones, prefer those CLOSE to the limit, closest first.</item>
///   <item>Skip armies another player is already using - unless that would leave nothing, in which case
///   a duplicate is better than no army.</item>
///   <item>Don't show a player the same army twice until it has cycled through all of them; the cycle
///   then starts over.</item>
/// </list>
/// </summary>
public sealed class BotArmyPicker
{
    private readonly IReadOnlyList<ArmyCatalogEntry> _catalog;

    // Per player slot: the army Keys already handed to it this lobby. Cleared once it has seen them all,
    // which is what makes the re-roll button a rotation rather than a random walk.
    private readonly Dictionary<Guid, HashSet<string>> _shown = new();

    // The limit every rotation in _shown was built against. Moving it invalidates all of them: both
    // which armies are legal and which is closest are answers to this number.
    private int? _pointsLimit;

    public BotArmyPicker(IReadOnlyList<ArmyCatalogEntry> catalog) => _catalog = catalog;

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

        ArmyCatalogEntry? pick =
            First(pool, a => !shown.Contains(a.Key) && !inUseByOthers.Contains(a.Key));

        // Everything unseen is taken by someone else, or this slot has now seen the whole pool. Start
        // the rotation over - still preferring an army nobody else holds.
        if (pick is null)
        {
            shown.Clear();
            pick = First(pool, a => !inUseByOthers.Contains(a.Key))
                // Fewer distinct armies than players: a duplicate beats leaving the bot army-less.
                ?? pool[0];
        }

        shown.Add(pick.Value.Key);
        return pick;
    }

    /// <summary>Drops a slot's rotation history (the player left, or its army was set by hand).</summary>
    public void Forget(Guid slot) => _shown.Remove(slot);

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
