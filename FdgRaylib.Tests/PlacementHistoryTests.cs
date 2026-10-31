using FDG;
using FDG.StageResolution.Requests;
using FdgRaylib.Rendering.Resolvers;
using NUnit.Framework;

namespace FdgRaylib.Tests;

// #343 — deployment undo at ACTION granularity. The placed list is roster-ordered, not chronological,
// so the old "pop the last list entry" Undo deleted one model out of a group drop (stranding the rest
// with no way back to the formation ghost) and, after a drag-edit, deleted a DIFFERENT model than the
// one just touched. These tests pin the inversion rules: each recorded gesture undoes as one step, a
// drag restores the pre-drag entry (position AND facing), a group drop empties the list and hands back
// its ghost rotation, and Restart round-trips the cleared snapshot by copy.
//
// The drawing/gesture wiring is ImGui (hand-verified); what lives here is the arithmetic that fails
// silently — same split as ModelRosterTests / PlacementPanelLayout.
[TestFixture]
public class PlacementHistoryTests
{
    // The history never reads the binding, so tests use entries with a null one (T = int keeps the
    // engine's ModelData out of what is purely list arithmetic).
    private static PlacedObjectEntry<int> Entry(float x, float z, Float2? facing = null) =>
        new(null!, new Position(x, z), facing);

    [Test]
    public void TryUndo_WithEmptyHistory_ReturnsFalseAndTouchesNothing()
    {
        var history = new PlacementHistory<int>();
        var placed = new List<PlacedObjectEntry<int>> { Entry(1f, 1f) };

        Assert.That(history.CanUndo, Is.False);
        Assert.That(history.TryUndo(placed, out float? rotation), Is.False);
        Assert.That(rotation, Is.Null);
        Assert.That(placed, Has.Count.EqualTo(1));
    }

    [Test]
    public void UndoPlace_RemovesTheLastEntry()
    {
        var history = new PlacementHistory<int>();
        var placed = new List<PlacedObjectEntry<int>> { Entry(1f, 1f) };
        history.RecordPlace();
        placed.Add(Entry(2f, 2f));
        history.RecordPlace();

        Assert.That(history.TryUndo(placed, out _), Is.True);

        Assert.That(placed, Has.Count.EqualTo(1));
        Assert.That(placed[0].Position.x, Is.EqualTo(1f));
    }

    [Test]
    public void UndoDrag_RestoresThePreDragEntryAtItsIndex_NotTheListTail()
    {
        // The old bug shape: place A then B, drag A somewhere else, press Undo — B vanished and A kept
        // its new spot. Action-granular undo restores A instead and leaves B alone.
        var history = new PlacementHistory<int>();
        var before = Entry(1f, 1f, new Float2(0f, 1f));
        var placed = new List<PlacedObjectEntry<int>> { before, Entry(2f, 2f) };
        history.RecordPlace();
        history.RecordPlace();

        history.RecordDrag(0, placed[0]);
        placed[0] = Entry(5f, 5f, new Float2(1f, 0f));

        Assert.That(history.TryUndo(placed, out float? rotation), Is.True);

        Assert.That(rotation, Is.Null);
        Assert.That(placed, Has.Count.EqualTo(2));
        Assert.That(placed[0], Is.EqualTo(before)); // position AND facing back
        Assert.That(placed[1].Position.x, Is.EqualTo(2f));
    }

    [Test]
    public void UndoGroupDrop_EmptiesTheListAndReturnsTheDropRotation()
    {
        // An emptied list is what re-opens the formation ghost (the resolver gates it on Count == 0);
        // the rotation comes back so the ghost returns oriented as it was dropped.
        var history = new PlacementHistory<int>();
        var placed = new List<PlacedObjectEntry<int>>();
        history.RecordGroupDrop(0.75f);
        placed.Add(Entry(1f, 1f));
        placed.Add(Entry(2f, 1f));
        placed.Add(Entry(3f, 1f));

        Assert.That(history.TryUndo(placed, out float? rotation), Is.True);

        Assert.That(placed, Is.Empty);
        Assert.That(rotation, Is.EqualTo(0.75f));
    }

    [Test]
    public void UndoRestart_RestoresTheClearedSnapshotByCopy()
    {
        var history = new PlacementHistory<int>();
        var placed = new List<PlacedObjectEntry<int>> { Entry(1f, 1f), Entry(2f, 2f) };

        history.RecordRestart(placed);
        placed.Clear();
        placed.Add(Entry(9f, 9f)); // re-placement after the restart must not corrupt the snapshot

        history.RecordPlace();
        Assert.That(history.TryUndo(placed, out _), Is.True);  // undo the re-placement
        Assert.That(history.TryUndo(placed, out _), Is.True);  // undo the restart

        Assert.That(placed, Has.Count.EqualTo(2));
        Assert.That(placed[0].Position.x, Is.EqualTo(1f));
        Assert.That(placed[1].Position.x, Is.EqualTo(2f));
    }

    [Test]
    public void MixedSequence_UnwindsInReverseGestureOrder()
    {
        var history = new PlacementHistory<int>();
        var placed = new List<PlacedObjectEntry<int>>();

        // drop the unit as a group, drag model 1, drag model 0
        history.RecordGroupDrop(0.5f);
        placed.Add(Entry(1f, 1f));
        placed.Add(Entry(2f, 1f));
        history.RecordDrag(1, placed[1]);
        placed[1] = Entry(2f, 4f);
        history.RecordDrag(0, placed[0]);
        placed[0] = Entry(1f, 4f);

        Assert.That(history.TryUndo(placed, out _), Is.True);          // last drag back
        Assert.That(placed[0].Position.z, Is.EqualTo(1f));
        Assert.That(placed[1].Position.z, Is.EqualTo(4f));

        Assert.That(history.TryUndo(placed, out _), Is.True);          // first drag back
        Assert.That(placed[1].Position.z, Is.EqualTo(1f));

        Assert.That(history.TryUndo(placed, out float? rotation), Is.True); // the drop lifts
        Assert.That(placed, Is.Empty);
        Assert.That(rotation, Is.EqualTo(0.5f));
        Assert.That(history.CanUndo, Is.False);
    }

    [Test]
    public void Clear_ForgetsEverything()
    {
        var history = new PlacementHistory<int>();
        var placed = new List<PlacedObjectEntry<int>> { Entry(1f, 1f) };
        history.RecordPlace();

        history.Clear();

        Assert.That(history.CanUndo, Is.False);
        Assert.That(history.TryUndo(placed, out _), Is.False);
        Assert.That(placed, Has.Count.EqualTo(1));
    }

    [Test]
    public void UndoDrag_WhoseIndexNoLongerExists_IsANoOpForTheList()
    {
        // Defensive: the resolver clears the history with the request, so a stale index should never
        // happen — but an out-of-range inversion must degrade to "nothing" rather than throw mid-frame.
        var history = new PlacementHistory<int>();
        var placed = new List<PlacedObjectEntry<int>>();
        history.RecordDrag(3, Entry(1f, 1f));

        Assert.That(history.TryUndo(placed, out _), Is.True);
        Assert.That(placed, Is.Empty);
    }
}
