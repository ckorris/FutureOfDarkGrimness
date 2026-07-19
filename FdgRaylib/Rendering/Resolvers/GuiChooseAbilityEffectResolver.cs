using System.Numerics;
using FDG.StageResolution;
using FDG.StageResolution.Requests;
using ImGuiNET;

namespace FdgRaylib.Rendering.Resolvers;

/// <summary>
/// #197 P5a — "pick one of this rule's effects", as an ImGui panel. Mandatory by design: no Back button and
/// no cancel sentinel, because the rule text says "pick one effect". The stage only raises this when two or
/// more effects are available, so there is always a real choice to make.
/// </summary>
public class GuiChooseAbilityEffectResolver : IStageResolver<ChooseAbilityEffectRequest, int>, IGuiResolver
{
    private readonly object _lock = new();
    private ChooseAbilityEffectRequest? _request;
    private TaskCompletionSource<int>? _tcs;

    public bool HasPendingRequest { get { lock (_lock) return _request != null; } }

    // #248: keyboard navigation — number keys instant-pick, arrows move this highlight, Enter commits.
    // Still mandatory: no Esc/Back (the rule text says "pick one effect").
    private readonly KeyboardListNav _nav = new();
    private static readonly uint KbHighlightCol = ImGui.ColorConvertFloat4ToU32(new Vector4(0.55f, 0.90f, 1.00f, 0.90f));

    public Task<int> Resolve(ChooseAbilityEffectRequest request)
    {
        var tcs = new TaskCompletionSource<int>();
        lock (_lock) { _tcs = tcs; _request = request; }
        return tcs.Task;
    }

    public void Draw(int screenW, int screenH)
    {
        ChooseAbilityEffectRequest? request;
        TaskCompletionSource<int>? tcs;
        lock (_lock) { request = _request; tcs = _tcs; }
        if (request == null || tcs == null) return;

        ImGui.SetNextWindowPos(Vector2.Zero, ImGuiCond.Always);
        ImGui.SetNextWindowSize(new Vector2(screenW, screenH), ImGuiCond.Always);
        ImGui.Begin("##AbilityEffectBackdrop",
            ImGuiWindowFlags.NoMove | ImGuiWindowFlags.NoResize | ImGuiWindowFlags.NoCollapse |
            ImGuiWindowFlags.NoTitleBar | ImGuiWindowFlags.NoScrollbar | ImGuiWindowFlags.NoInputs |
            ImGuiWindowFlags.NoBackground);

        const float pad = 16f;
        const float btnH = 28f;
        const float descScale = 0.82f;
        const float descIndent = 10f;
        const float gapAfterBtn = 6f;
        const float gapAfterDesc = 8f;

        float dw = ResolverPanelLayout.W;
        float btnW = dw - pad * 2;

        float headerH = ImGui.CalcTextSize(request.RuleName, false, dw - pad * 2).Y + 4f;
        float instrH = ImGui.CalcTextSize(request.Instructions, false, dw - pad * 2).Y + 8f;

        float descWrapMeasure = (btnW - descIndent) / descScale;
        float[] rowHeights = new float[request.Options.Count];
        for (int i = 0; i < request.Options.Count; i++)
        {
            string desc = request.Options[i].Description;
            rowHeights[i] = string.IsNullOrEmpty(desc)
                ? btnH + gapAfterBtn
                : btnH + 2f + ImGui.CalcTextSize(desc, false, descWrapMeasure).Y * descScale + gapAfterDesc;
        }

        ImGui.SetCursorPos(new Vector2(ResolverPanelLayout.X, ResolverPanelLayout.Y));
        ImGui.PushStyleColor(ImGuiCol.ChildBg, new Vector4(0.15f, 0.15f, 0.20f, 0.97f));
        ImGui.PushStyleVar(ImGuiStyleVar.ChildRounding, 6f);
        ImGui.BeginChild("##AbilityEffectDialog", new Vector2(dw, ResolverPanelLayout.H),
            ImGuiChildFlags.Borders, ImGuiWindowFlags.None);
        ImGui.PopStyleColor();
        ImGui.PopStyleVar();

        ImGui.SetCursorPos(new Vector2(pad, pad));
        ImGui.PushStyleColor(ImGuiCol.Text, new Vector4(0.85f, 0.80f, 0.55f, 1f));
        ImGui.TextUnformatted(request.RuleName);
        ImGui.PopStyleColor();

        ImGui.SetCursorPos(new Vector2(pad, pad + headerH));
        ImGui.PushTextWrapPos(dw - pad);
        ImGui.TextUnformatted(request.Instructions);
        ImGui.PopTextWrapPos();

        // #248: keyboard picks, applied once after drawing (same-frame click + key can't double-resolve).
        int picked = _nav.Update(request, request.Options.Count, out _);
        var dl = ImGui.GetWindowDrawList();

        float y = pad + headerH + instrH + 4f;
        for (int i = 0; i < request.Options.Count; i++)
        {
            ChooseAbilityEffectRequest.EffectOption option = request.Options[i];

            ImGui.SetCursorPos(new Vector2(pad, y));
            Vector2 origin = ImGui.GetCursorScreenPos();
            if (ImGui.Button($"{ResolverHotkeys.NumberPrefix(i)}{option.Label}##{i}", new Vector2(btnW, btnH))
                && picked < 0)
                picked = i;
            if (i == _nav.Index)
                dl.AddRect(origin, origin + new Vector2(btnW, btnH), KbHighlightCol, 4f, ImDrawFlags.None, 2f);

            if (!string.IsNullOrEmpty(option.Description))
            {
                ImGui.SetCursorPos(new Vector2(pad + descIndent, y + btnH + 2f));
                ImGui.PushStyleColor(ImGuiCol.Text, new Vector4(0.62f, 0.66f, 0.74f, 1f));
                ImGui.SetWindowFontScale(descScale);
                ImGui.PushTextWrapPos(dw - pad);
                ImGui.TextUnformatted(option.Description);
                ImGui.PopTextWrapPos();
                ImGui.SetWindowFontScale(1f);
                ImGui.PopStyleColor();
            }

            y += rowHeights[i];
        }

        ImGui.EndChild();
        ImGui.End();

        if (picked >= 0)
            Complete(tcs, picked);
    }

    private void Complete(TaskCompletionSource<int> tcs, int index)
    {
        lock (_lock) { _request = null; _tcs = null; }
        tcs.SetResult(index);
    }
}
