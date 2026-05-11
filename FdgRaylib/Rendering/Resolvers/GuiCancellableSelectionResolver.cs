using System.Numerics;
using FDG.Data;
using FDG.StageResolution;
using FDG.StageResolution.Requests;
using ImGuiNET;

namespace FdgRaylib.Rendering.Resolvers;

public class GuiCancellableSelectionResolver<T>
    : IStageResolver<CancellableSelectionRequest<T>, CancellableResult<DataBinding<T>>>, IGuiResolver
{
    private readonly object _lock = new();
    private CancellableSelectionRequest<T>? _request;
    private TaskCompletionSource<CancellableResult<DataBinding<T>>>? _tcs;

    public bool HasPendingRequest { get { lock (_lock) return _request != null; } }

    public Task<CancellableResult<DataBinding<T>>> Resolve(CancellableSelectionRequest<T> request)
    {
        var tcs = new TaskCompletionSource<CancellableResult<DataBinding<T>>>();
        lock (_lock)
        {
            _tcs     = tcs;
            _request = request;
        }
        return tcs.Task;
    }

    public void Draw(int screenW, int screenH)
    {
        CancellableSelectionRequest<T>? request;
        TaskCompletionSource<CancellableResult<DataBinding<T>>>? tcs;
        lock (_lock) { request = _request; tcs = _tcs; }
        if (request == null || tcs == null) return;

        ImGui.SetNextWindowPos(Vector2.Zero, ImGuiCond.Always);
        ImGui.SetNextWindowSize(new Vector2(screenW, screenH), ImGuiCond.Always);
        ImGui.Begin("##CancellableSelectionBackdrop",
            ImGuiWindowFlags.NoMove | ImGuiWindowFlags.NoResize | ImGuiWindowFlags.NoCollapse |
            ImGuiWindowFlags.NoTitleBar | ImGuiWindowFlags.NoScrollbar | ImGuiWindowFlags.NoInputs |
            ImGuiWindowFlags.NoBackground);

        int validCount   = request.ValidOptions.Count;
        int invalidCount = request.InvalidOptions.Count;
        int totalRows    = validCount + invalidCount;
        float rowH   = 32f;
        float pad    = 16f;
        float instrH = 48f;
        float backH  = rowH + pad;
        float dw = MathF.Min(screenW * 0.45f, 560f);
        float dh = MathF.Min(instrH + pad + totalRows * rowH + backH + pad * 2, screenH * 0.80f);
        float dx = (screenW - dw) * 0.5f;
        float dy = (screenH - dh) * 0.5f;

        ImGui.SetCursorPos(new Vector2(dx, dy));
        ImGui.PushStyleColor(ImGuiCol.ChildBg, new Vector4(0.15f, 0.15f, 0.20f, 0.97f));
        ImGui.PushStyleVar(ImGuiStyleVar.ChildRounding, 6f);
        ImGui.BeginChild("##CancellableSelectionDialog", new Vector2(dw, dh), ImGuiChildFlags.Borders,
            ImGuiWindowFlags.NoScrollbar);
        ImGui.PopStyleColor();
        ImGui.PopStyleVar();

        ImGui.SetCursorPos(new Vector2(pad, pad));
        ImGui.PushTextWrapPos(dw - pad);
        ImGui.TextUnformatted(request.Instructions);
        ImGui.PopTextWrapPos();

        float listY = pad + instrH;
        float btnW  = dw - pad * 2;
        for (int i = 0; i < validCount; i++)
        {
            var opt = request.ValidOptions[i];
            ImGui.SetCursorPos(new Vector2(pad, listY + i * rowH));
            if (ImGui.Button(opt.Name + $"##{i}", new Vector2(btnW, rowH - 4f)))
                Complete(tcs, new Selected<DataBinding<T>>(opt.Option));
        }

        if (invalidCount > 0)
        {
            float invalidStart = listY + validCount * rowH;
            ImGui.PushStyleColor(ImGuiCol.Text, new Vector4(0.5f, 0.5f, 0.5f, 1f));
            for (int i = 0; i < invalidCount; i++)
            {
                var opt = request.InvalidOptions[i];
                ImGui.SetCursorPos(new Vector2(pad, invalidStart + i * rowH));
                ImGui.BeginDisabled(true);
                ImGui.Button($"{opt.Name} ({opt.Reason})##{validCount + i}", new Vector2(btnW, rowH - 4f));
                ImGui.EndDisabled();
            }
            ImGui.PopStyleColor();
        }

        float backY = listY + totalRows * rowH + pad;
        ImGui.SetCursorPos(new Vector2(pad, backY));
        ImGui.PushStyleColor(ImGuiCol.Button, new Vector4(0.25f, 0.25f, 0.30f, 1f));
        if (ImGui.Button("Back##back", new Vector2(btnW, rowH - 4f)))
            Complete(tcs, new Cancelled<DataBinding<T>>());
        ImGui.PopStyleColor();

        ImGui.EndChild();
        ImGui.End();
    }

    private void Complete(TaskCompletionSource<CancellableResult<DataBinding<T>>> tcs,
        CancellableResult<DataBinding<T>> result)
    {
        lock (_lock) { _request = null; _tcs = null; }
        tcs.SetResult(result);
    }
}
