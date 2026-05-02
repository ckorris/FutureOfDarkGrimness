using System.Numerics;
using FDG.StageResolution;
using ImGuiNET;

namespace FdgRaylib.Rendering;

public class GuiOutstandingTaskDisplay : IOutstandingListDisplay
{
    private readonly object _lock = new();
    private IReadOnlyCollection<OutstandingTaskInfo> _tasks = Array.Empty<OutstandingTaskInfo>();
    private IDisposable? _subscription;

    public void AssignLister(IOutstandingTaskLister lister)
    {
        _subscription = lister.OutstandingTasks.Subscribe(tasks =>
        {
            lock (_lock) _tasks = tasks;
        });
    }

    public void Draw(int screenW, int screenH)
    {
        IReadOnlyCollection<OutstandingTaskInfo> tasks;
        lock (_lock) tasks = _tasks;

        float w = MathF.Max(200f, screenW * 0.22f);
        float x = (screenW - w) * 0.5f;

        ImGui.SetNextWindowPos(new Vector2(x, 8f), ImGuiCond.Always);
        ImGui.SetNextWindowSizeConstraints(new Vector2(w, 0), new Vector2(w, screenH * 0.4f));
        ImGui.SetNextWindowBgAlpha(0.75f);
        ImGui.Begin("Outstanding Tasks",
            ImGuiWindowFlags.NoMove | ImGuiWindowFlags.NoResize | ImGuiWindowFlags.NoCollapse |
            ImGuiWindowFlags.NoScrollbar | ImGuiWindowFlags.AlwaysAutoResize |
            ImGuiWindowFlags.NoFocusOnAppearing | ImGuiWindowFlags.NoNav);

        if (tasks.Count == 0)
        {
            ImGui.TextDisabled("No outstanding tasks.");
        }
        else
        {
            foreach (var task in tasks)
            {
                string playerName = task.PlayerInfo.Name;
                int team = task.PlayerInfo.TeamNumber;
                string label = team >= 0
                    ? $"{playerName} (Team {team + 1}): {task.TaskName}"
                    : $"{playerName}: {task.TaskName}";
                ImGui.TextUnformatted(label);
            }
        }

        ImGui.End();
    }

    public void Dispose()
    {
        _subscription?.Dispose();
    }
}
