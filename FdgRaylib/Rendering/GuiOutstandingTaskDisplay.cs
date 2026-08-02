using FDG;
using FDG.StageResolution;

namespace FdgRaylib.Rendering;

/// <summary>
/// Subscribes to the engine's outstanding-task stream and answers "whose decision is the game
/// waiting on right now?" for the status HUD (#318). Local players' tasks are filtered out - the
/// resolver panel already shows those - so what remains is exactly the set of other people the
/// local player is waiting for. Purely a read model: the renderer draws it via
/// <see cref="StatusHudOverlay"/> (the old draggable "Outstanding Tasks" ImGui window is gone).
/// </summary>
public class GuiOutstandingTaskDisplay : IOutstandingListDisplay, IDisposable
{
    private readonly object _lock = new();
    private IReadOnlyCollection<OutstandingTaskInfo> _tasks = Array.Empty<OutstandingTaskInfo>();
    private IDisposable? _subscription;
    private readonly IReadOnlyList<PlayerID> _localPlayerIDs;

    /// <param name="localPlayerIDs">The launched game's <c>IFDGGame.LocalPlayerIDs</c>. May be the
    /// engine's live list; only ever read here.</param>
    public GuiOutstandingTaskDisplay(IReadOnlyList<PlayerID> localPlayerIDs)
    {
        _localPlayerIDs = localPlayerIDs;
    }

    public void AssignLister(IOutstandingTaskLister lister)
    {
        _subscription = lister.OutstandingTasks.Subscribe(tasks =>
        {
            lock (_lock) _tasks = tasks;
        });
    }

    /// <summary>
    /// Tasks currently awaiting a decision from a non-local player, oldest first. Empty in pure
    /// hotseat games (every player is local) and whenever nobody else holds up the game.
    /// Thread-safe; called from the render thread while the subscription writes from the engine
    /// thread.
    /// </summary>
    public IReadOnlyList<OutstandingTaskInfo> GetWaitingOnOthers()
    {
        IReadOnlyCollection<OutstandingTaskInfo> tasks;
        lock (_lock) tasks = _tasks;
        if (tasks.Count == 0) return Array.Empty<OutstandingTaskInfo>();

        var waiting = new List<OutstandingTaskInfo>();
        foreach (var task in tasks)
            if (!_localPlayerIDs.Contains(task.PlayerInfo.PlayerID))
                waiting.Add(task);
        return waiting;
    }

    public void Dispose()
    {
        _subscription?.Dispose();
    }
}
