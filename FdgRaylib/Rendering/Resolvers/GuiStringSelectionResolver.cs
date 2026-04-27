using System.Numerics;
using FDG.StageResolution;
using FDG.StageResolution.Requests;
using ImGuiNET;

namespace FdgRaylib.Rendering.Resolvers;

public class GuiStringSelectionResolver : IStageResolver<StringSelectionRequest, string>, IGuiResolver
{
    private readonly object _lock = new();
    private StringSelectionRequest? _request;
    private TaskCompletionSource<string>? _tcs;

    public bool HasPendingRequest { get { lock (_lock) return _request != null; } }

    public Task<string> Resolve(StringSelectionRequest request)
    {
        var tcs = new TaskCompletionSource<string>();
        lock (_lock) { _tcs = tcs; _request = request; }
        return tcs.Task;
    }

    public void Draw(int screenW, int screenH)
    {
        StringSelectionRequest? request;
        TaskCompletionSource<string>? tcs;
        lock (_lock) { request = _request; tcs = _tcs; }
        if (request == null || tcs == null) return;

        ImGui.SetNextWindowPos(Vector2.Zero, ImGuiCond.Always);
        ImGui.SetNextWindowSize(new Vector2(screenW, screenH), ImGuiCond.Always);
        ImGui.PushStyleColor(ImGuiCol.WindowBg, new Vector4(0f, 0f, 0f, 0.55f));
        ImGui.Begin("##StrSelBackdrop",
            ImGuiWindowFlags.NoMove | ImGuiWindowFlags.NoResize | ImGuiWindowFlags.NoCollapse |
            ImGuiWindowFlags.NoTitleBar | ImGuiWindowFlags.NoScrollbar);
        ImGui.PopStyleColor();

        int validCount   = request.ValidOptions.Count;
        int invalidCount = request.InvalidOptions.Count;
        float rowH   = 32f;
        float pad    = 16f;
        float instrH = 48f;
        float dw = MathF.Min(screenW * 0.45f, 560f);
        float dh = MathF.Min(instrH + pad + (validCount + invalidCount) * rowH + pad * 2, screenH * 0.80f);
        float dx = (screenW - dw) * 0.5f;
        float dy = (screenH - dh) * 0.5f;

        ImGui.SetCursorPos(new Vector2(dx, dy));
        ImGui.PushStyleColor(ImGuiCol.ChildBg, new Vector4(0.15f, 0.15f, 0.20f, 0.97f));
        ImGui.PushStyleVar(ImGuiStyleVar.ChildRounding, 6f);
        ImGui.BeginChild("##StrSelDialog", new Vector2(dw, dh), ImGuiChildFlags.Borders,
            ImGuiWindowFlags.NoScrollbar);
        ImGui.PopStyleColor();
        ImGui.PopStyleVar();

        ImGui.SetCursorPos(new Vector2(pad, pad));
        ImGui.PushTextWrapPos(dw - pad);
        ImGui.TextUnformatted(request.Instructions);
        ImGui.PopTextWrapPos();

        float btnW  = dw - pad * 2;
        float listY = pad + instrH;
        for (int i = 0; i < validCount; i++)
        {
            string opt = request.ValidOptions[i];
            ImGui.SetCursorPos(new Vector2(pad, listY + i * rowH));
            if (ImGui.Button($"{opt}##{i}", new Vector2(btnW, rowH - 4f)))
                Complete(tcs, opt);
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
                ImGui.Button($"{opt.Option} ({opt.Reason})##{validCount + i}", new Vector2(btnW, rowH - 4f));
                ImGui.EndDisabled();
            }
            ImGui.PopStyleColor();
        }

        ImGui.EndChild();
        ImGui.End();
    }

    private void Complete(TaskCompletionSource<string> tcs, string choice)
    {
        lock (_lock) { _request = null; _tcs = null; }
        tcs.SetResult(choice);
    }
}
