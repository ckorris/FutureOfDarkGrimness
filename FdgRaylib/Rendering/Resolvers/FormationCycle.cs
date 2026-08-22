using FDG;

namespace FdgRaylib.Rendering.Resolvers;

/// <summary>
/// Per-request formation cycling state for the group placement/movement overlays (#277): the unit's
/// legal <see cref="FormationLibrary"/> shapes, with an optional leading "current shape" entry at
/// index 0 for units that already stand somewhere (movement, consolidation, teleport/reposition).
/// Ctrl+Wheel cycles the index (see <see cref="GroupInput"/>); plain Wheel keeps rotating.
/// </summary>
internal sealed class FormationCycle
{
    private readonly List<FormationLibrary.Formation> _catalog;

    /// <summary>Whether index 0 means "keep the unit's current shape" rather than a catalog entry.</summary>
    public bool IncludesCurrentShape { get; }

    public int Index { get; private set; }

    private FormationCycle(List<FormationLibrary.Formation> catalog, bool includesCurrentShape)
    {
        _catalog = catalog;
        IncludesCurrentShape = includesCurrentShape;
    }

    /// <summary>
    /// Builds the cycle from per-model extents (pass circumscribing radii for all three to stay
    /// rotation-safe). Catalog order is FormationLibrary's: line first, then deeper shapes; entries
    /// whose span would break the 9" all-pairs rule are filtered out. If EVERY partition fails the
    /// filter (a handful of huge bases) and there is no current shape to fall back on, the most
    /// compact partition is kept anyway so placement still has a shape to offer — downstream
    /// validation reports the span honestly.
    /// </summary>
    public static FormationCycle Build(IReadOnlyList<float> halfXs, IReadOnlyList<float> halfZs,
        IReadOnlyList<float> radii, bool includeCurrentShape)
    {
        var catalog = FormationLibrary.LegalFormations(halfXs, halfZs, radii, gap: 0.1f,
            GameWideConstants.MAX_MODEL_DISTANCE_FROM_ALL_OTHER_MODELS_INCHES);
        if (catalog.Count == 0 && !includeCurrentShape)
        {
            var partitions = FormationLibrary.RowPartitions(halfXs.Count);
            if (partitions.Count > 0)
            {
                int[] last = partitions[^1];
                catalog.Add(new FormationLibrary.Formation(last, FormationLibrary.Describe(last)));
            }
        }
        return new FormationCycle(catalog, includeCurrentShape);
    }

    public int Count => _catalog.Count + (IncludesCurrentShape ? 1 : 0);

    public bool IsCurrentShape => IncludesCurrentShape && Index == 0;

    /// <summary>The selected catalog formation. Only valid when <see cref="IsCurrentShape"/> is false.</summary>
    public FormationLibrary.Formation Selected => _catalog[IncludesCurrentShape ? Index - 1 : Index];

    public string Label => IsCurrentShape ? "current" : Selected.Name;

    public void Cycle(int delta)
    {
        if (Count > 0) Index = ((Index + delta) % Count + Count) % Count;
    }

    /// <summary>Back to index 0 — after a committed group step bakes the picked formation into the
    /// unit's real positions, "current shape" IS that formation.</summary>
    public void Reset() => Index = 0;
}
