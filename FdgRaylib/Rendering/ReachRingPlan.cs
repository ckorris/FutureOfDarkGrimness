namespace FdgRaylib.Rendering;

/// <summary>
/// The display-independent core of the reposition reach ring (#214): given the state of a placement in
/// progress, WHICH models still need a reach circle drawn and which of them the next click affects.
///
/// <para>A reposition-style placement (Teleport, Fanatic, reposition-at-activation) bounds each model
/// individually - within <c>MaxDistanceFromStartInches</c> of its OWN start - and passes the whole table
/// as the deployment zone, so there is no single zone outline that shows the constraint. One ring per
/// model still to place is the honest picture.</para>
///
/// Split out from <see cref="Resolvers.GuiPlaceObjectsResolver{T}"/> so the selection rules are testable
/// without ImGui; the resolver keeps the drawing.
/// </summary>
internal static class ReachRingPlan
{
    /// <summary>One ring to draw: which model (by index into the request's model list), and whether it is
    /// the model the next click will affect (drawn brighter).</summary>
    internal readonly record struct Ring(int ModelIndex, bool IsActive);

    /// <summary>
    /// The rings for a placement in progress.
    /// <paramref name="placedCount"/> models have been placed (in list order), so the rest still need one.
    /// <paramref name="dragIndex"/> is the placed model being re-placed, if any - it needs a ring again.
    /// <paramref name="groupDrop"/> (group mode, nothing placed yet) means one click places EVERY model,
    /// so no single ring is more live than another.
    /// </summary>
    internal static List<Ring> Build(int modelCount, int placedCount, int? dragIndex, bool groupDrop)
    {
        List<Ring> rings = new List<Ring>();
        if (modelCount <= 0) return rings;

        // Dragging overrides the "next unplaced" cursor: the click lands on the dragged model instead.
        int? activeUnplaced = dragIndex.HasValue || placedCount >= modelCount ? null : placedCount;

        for (int i = placedCount; i < modelCount; i++)
        {
            rings.Add(new Ring(i, groupDrop || i == activeUnplaced));
        }

        if (dragIndex.HasValue)
        {
            rings.Add(new Ring(dragIndex.Value, IsActive: true));
        }

        return rings;
    }
}
