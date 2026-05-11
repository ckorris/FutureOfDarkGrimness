using System.Numerics;
using FDG;
using FDG.StageResolution;
using FDG.StageResolution.Requests;
using ImGuiNET;

namespace FdgRaylib.Rendering.Resolvers;

public class GuiAssignWoundsResolver : IStageResolver<AssignWoundsRequest, AssignWoundsResults>, IGuiResolver
{
    private readonly object _lock = new();
    private AssignWoundsRequest? _request;
    private AssignWoundsResults? _results;
    private TaskCompletionSource<AssignWoundsResults>? _tcs;

    public bool HasPendingRequest { get { lock (_lock) return _request != null; } }

    public Task<AssignWoundsResults> Resolve(AssignWoundsRequest request)
    {
        var tcs     = new TaskCompletionSource<AssignWoundsResults>();
        var results = new AssignWoundsResults(request.UnitReceivingWounds, request.TotalWoundsToAssign);
        lock (_lock) { _tcs = tcs; _request = request; _results = results; }
        return tcs.Task;
    }

    public void Draw(int screenW, int screenH)
    {
        AssignWoundsRequest? request;
        AssignWoundsResults? results;
        TaskCompletionSource<AssignWoundsResults>? tcs;
        lock (_lock) { request = _request; results = _results; tcs = _tcs; }
        if (request == null || results == null || tcs == null) return;

        ImGui.SetNextWindowPos(Vector2.Zero, ImGuiCond.Always);
        ImGui.SetNextWindowSize(new Vector2(screenW, screenH), ImGuiCond.Always);
        ImGui.Begin("##WoundsBackdrop",
            ImGuiWindowFlags.NoMove | ImGuiWindowFlags.NoResize | ImGuiWindowFlags.NoCollapse |
            ImGuiWindowFlags.NoTitleBar | ImGuiWindowFlags.NoScrollbar | ImGuiWindowFlags.NoInputs |
            ImGuiWindowFlags.NoBackground);

        var models  = results.PendingWounds;
        float rowH  = 36f;
        float pad   = 16f;
        float hdrH  = 72f;
        float footH = 44f;
        float dw = MathF.Min(screenW * 0.45f, 520f);
        float dh = MathF.Min(hdrH + pad + models.Count * rowH + pad + footH + pad, screenH * 0.82f);
        float dx = (screenW - dw) * 0.5f;
        float dy = (screenH - dh) * 0.5f;

        ImGui.SetCursorPos(new Vector2(dx, dy));
        ImGui.PushStyleColor(ImGuiCol.ChildBg, new Vector4(0.15f, 0.15f, 0.20f, 0.97f));
        ImGui.PushStyleVar(ImGuiStyleVar.ChildRounding, 6f);
        ImGui.BeginChild("##WoundsDialog", new Vector2(dw, dh), ImGuiChildFlags.Borders,
            ImGuiWindowFlags.NoScrollbar);
        ImGui.PopStyleColor();
        ImGui.PopStyleVar();

        // Header
        string unitName = request.UnitReceivingWounds.GetValue().Name;
        ImGui.SetCursorPos(new Vector2(pad, pad));
        ImGui.PushTextWrapPos(dw - pad);
        ImGui.TextUnformatted($"Assign Wounds: {unitName}");
        ImGui.Spacing();
        ImGui.TextUnformatted($"{results.TotalAssignedWounds:F0} / {results.TotalWoundsToAssign:F0} wounds assigned");
        ImGui.PopTextWrapPos();

        // Model buttons (scrollable if many models)
        float listY = pad + hdrH;
        float listH = dh - listY - pad - footH - pad;
        float btnW  = dw - pad * 2;
        ImGui.SetCursorPos(new Vector2(pad, listY));
        ImGui.BeginChild("##WoundsList", new Vector2(btnW, listH), ImGuiChildFlags.None);

        for (int i = 0; i < models.Count; i++)
        {
            var pw         = models[i];
            var modelData  = pw.Model.GetValue();
            float total    = modelData.TotalWounds;
            float dealt    = modelData.WoundsDealt;
            float remaining = total - dealt - pw.Wounds;
            bool canTake   = remaining > 0;

            string label = $"Model {i + 1}  —  {remaining:F0} wounds remaining##{i}";
            ImGui.BeginDisabled(!canTake);
            if (ImGui.Button(label, new Vector2(ImGui.GetContentRegionAvail().X, rowH - 4f)))
            {
                results.TryAddWounds(pw.Model);
                if (results.IsFinishedAssigning)
                    Complete(tcs, results);
            }
            ImGui.EndDisabled();
        }

        ImGui.EndChild();

        // Auto-assign button
        float btnY = dh - pad - footH;
        ImGui.SetCursorPos(new Vector2(pad, btnY));
        if (ImGui.Button("Auto-assign All", new Vector2(btnW, footH - 4f)))
        {
            AutoFillRemaining(results);
            Complete(tcs, results);
        }

        ImGui.EndChild();
        ImGui.End();
    }

    private void Complete(TaskCompletionSource<AssignWoundsResults> tcs, AssignWoundsResults results)
    {
        lock (_lock) { _request = null; _results = null; _tcs = null; }
        tcs.SetResult(results);
    }

    // AssignWoundsResults.AutoFill() has a bug (modelWoundsRemaining always 0); fill manually.
    private static void AutoFillRemaining(AssignWoundsResults results)
    {
        foreach (var pw in results.PendingWounds)
        {
            if (results.IsFinishedAssigning) break;
            results.TryAddWounds(pw.Model);
        }
    }
}
