using FDG.StageResolution.Requests;

namespace FdgRaylib.Rendering.Resolvers;

/// <summary>
/// #343 — deployment's undo history, at ACTION granularity. The placed list is roster-ordered, not
/// chronological, so "remove the last list entry" diverged from "reverse the last thing I did": undoing
/// a group drop deleted one model (stranding the other N-1 with no way back to the formation ghost),
/// and a drag-edit was not undoable at all — Undo after a drag deleted a DIFFERENT model. Each user
/// gesture records one action here; undo pops one action and inverts it against the placed list.
///
/// <para>Undoing a group drop empties the list, which is exactly what brings the formation ghost back —
/// <see cref="GuiPlaceObjectsResolver{T}"/> gates it on <c>_placed.Count == 0</c> — and hands the drop's
/// rotation back so the ghost returns oriented as it was dropped. No redo: movement has none either,
/// and re-doing a placement is a click.</para>
///
/// <para>Arithmetic-only (no ImGui), same split as <c>ModelRoster</c> / <c>PlacementPanelLayout</c>,
/// so the inversion rules are unit-tested in <c>PlacementHistoryTests</c>.</para>
/// </summary>
public class PlacementHistory<T>
{
    private abstract record Step;
    private sealed record Place : Step;
    private sealed record Drag(int Index, PlacedObjectEntry<T> Before) : Step;
    private sealed record GroupDrop(float RotationRadians) : Step;
    private sealed record Restart(List<PlacedObjectEntry<T>> Entries) : Step;

    private readonly List<Step> _steps = new();

    public bool CanUndo => _steps.Count > 0;

    /// <summary>A single model appended to the end of the placed list.</summary>
    public void RecordPlace() => _steps.Add(new Place());

    /// <summary>A placed model picked up and dropped somewhere else. <paramref name="before"/> is the
    /// entry as it stood when it was picked up (position AND facing), captured before the overwrite.</summary>
    public void RecordDrag(int index, PlacedObjectEntry<T> before) => _steps.Add(new Drag(index, before));

    /// <summary>The whole unit dropped at once from the formation ghost (always from an empty list).
    /// <paramref name="rotationRadians"/> is the ghost rotation the drop was made with, captured before
    /// the resolver resets it.</summary>
    public void RecordGroupDrop(float rotationRadians) => _steps.Add(new GroupDrop(rotationRadians));

    /// <summary>Restart pressed: everything cleared. Snapshots <paramref name="cleared"/> (copy, not
    /// reference — the caller clears the live list right after).</summary>
    public void RecordRestart(IEnumerable<PlacedObjectEntry<T>> cleared) =>
        _steps.Add(new Restart(new List<PlacedObjectEntry<T>>(cleared)));

    /// <summary>Forget everything — a new request is starting or the placement was committed.</summary>
    public void Clear() => _steps.Clear();

    /// <summary>
    /// Inverts the most recent action against <paramref name="placed"/>. Returns false with nothing to
    /// undo. <paramref name="groupRotationToRestore"/> is set when the undone action was a group drop,
    /// so the caller can hand the returning ghost its rotation back.
    /// </summary>
    public bool TryUndo(List<PlacedObjectEntry<T>> placed, out float? groupRotationToRestore)
    {
        groupRotationToRestore = null;
        if (_steps.Count == 0) return false;

        Step step = _steps[^1];
        _steps.RemoveAt(_steps.Count - 1);

        switch (step)
        {
            case Place:
                if (placed.Count > 0) placed.RemoveAt(placed.Count - 1);
                break;
            case Drag drag:
                if (drag.Index < placed.Count) placed[drag.Index] = drag.Before;
                break;
            case GroupDrop drop:
                placed.Clear();
                groupRotationToRestore = drop.RotationRadians;
                break;
            case Restart restart:
                placed.Clear();
                placed.AddRange(restart.Entries);
                break;
        }
        return true;
    }
}
