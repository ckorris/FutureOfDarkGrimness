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
        ImGui.Begin("##StrSelBackdrop",
            ImGuiWindowFlags.NoMove | ImGuiWindowFlags.NoResize | ImGuiWindowFlags.NoCollapse |
            ImGuiWindowFlags.NoTitleBar | ImGuiWindowFlags.NoScrollbar | ImGuiWindowFlags.NoInputs |
            ImGuiWindowFlags.NoBackground);

        int validCount   = request.ValidOptions.Count;
        int invalidCount = request.InvalidOptions.Count;
        bool hasDescriptions = request.OptionDescriptions != null && request.OptionDescriptions.Count > 0;

        const float pad = 16f;
        const float btnH = 28f;
        const float descScale = 0.82f;   // descriptions render smaller than the option label
        const float descIndent = 10f;
        const float gapAfterBtn = 6f;
        const float gapAfterDesc = 8f;
        const float invalidRowH = 32f;

        float dw = MathF.Min(screenW * 0.5f, 620f);
        float btnW = dw - pad * 2;

        // Measure heights up front so the dialog is sized to its content and a wrapped description can never
        // overflow into the next row. CalcTextSize ignores SetWindowFontScale, so measure the description at
        // the scaled-equivalent wrap width and scale the resulting height back down.
        float instrH = ImGui.CalcTextSize(request.Instructions, false, dw - pad * 2).Y + 8f;

        float descWrapMeasure = (btnW - descIndent) / descScale;
        float[] rowHeights = new float[validCount];
        string?[] descs = new string?[validCount];
        float validHeight = 0f;
        for (int i = 0; i < validCount; i++)
        {
            string opt = request.ValidOptions[i];
            float h = btnH + gapAfterBtn;
            if (hasDescriptions
                && request.OptionDescriptions!.TryGetValue(opt, out string? d)
                && !string.IsNullOrEmpty(d))
            {
                descs[i] = d;
                float descH = ImGui.CalcTextSize(d, false, descWrapMeasure).Y * descScale;
                h = btnH + 2f + descH + gapAfterDesc;
            }
            rowHeights[i] = h;
            validHeight += h;
        }

        float contentH = pad + instrH + 4f + validHeight + invalidCount * invalidRowH + pad;
        float dh = MathF.Min(contentH, screenH * 0.85f);
        float dx = screenW - dw - 12f;   // right-aligned in the open right-side space (#105)
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

        float y = pad + instrH + 4f;
        for (int i = 0; i < validCount; i++)
        {
            string opt = request.ValidOptions[i];
            ImGui.SetCursorPos(new Vector2(pad, y));
            if (ImGui.Button($"{opt}##{i}", new Vector2(btnW, btnH)))
                Complete(tcs, opt);

            // Optional subtext under the option (e.g. a spell's effect summary): smaller and dimmed.
            if (descs[i] != null)
            {
                ImGui.SetCursorPos(new Vector2(pad + descIndent, y + btnH + 2f));
                ImGui.PushStyleColor(ImGuiCol.Text, new Vector4(0.62f, 0.66f, 0.74f, 1f));
                ImGui.SetWindowFontScale(descScale);
                ImGui.PushTextWrapPos(dw - pad);
                ImGui.TextUnformatted(descs[i]!);
                ImGui.PopTextWrapPos();
                ImGui.SetWindowFontScale(1f);
                ImGui.PopStyleColor();
            }
            y += rowHeights[i];
        }

        if (invalidCount > 0)
        {
            ImGui.PushStyleColor(ImGuiCol.Text, new Vector4(0.5f, 0.5f, 0.5f, 1f));
            for (int i = 0; i < invalidCount; i++)
            {
                var opt = request.InvalidOptions[i];
                ImGui.SetCursorPos(new Vector2(pad, y));
                ImGui.BeginDisabled(true);
                ImGui.Button($"{opt.Option} ({opt.Reason})##{validCount + i}", new Vector2(btnW, btnH));
                ImGui.EndDisabled();
                y += invalidRowH;
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
