using System.Numerics;
using FDG.Data;
using FDG.StageResolution;
using FDG.StageResolution.Requests;
using ImGuiNET;

namespace FdgRaylib.Rendering.Resolvers;

public class GuiSelectionResolver<T> : IStageResolver<SelectionRequest<T>, DataBinding<T>>, IGuiResolver
{
    protected readonly object _lock = new();
    protected SelectionRequest<T>? _request;
    protected TaskCompletionSource<DataBinding<T>>? _tcs;

    public bool HasPendingRequest { get { lock (_lock) return _request != null; } }

    public Task<DataBinding<T>> Resolve(SelectionRequest<T> request)
    {
        var tcs = new TaskCompletionSource<DataBinding<T>>();
        lock (_lock)
        {
            _tcs     = tcs;
            _request = request;
        }
        return tcs.Task;
    }

    public virtual void Draw(int screenW, int screenH)
    {
        SelectionRequest<T>? request;
        TaskCompletionSource<DataBinding<T>>? tcs;
        lock (_lock) { request = _request; tcs = _tcs; }
        if (request == null || tcs == null) return;

        // Semi-transparent backdrop
        ImGui.SetNextWindowPos(Vector2.Zero, ImGuiCond.Always);
        ImGui.SetNextWindowSize(new Vector2(screenW, screenH), ImGuiCond.Always);
        ImGui.Begin("##SelectionBackdrop",
            ImGuiWindowFlags.NoMove | ImGuiWindowFlags.NoResize | ImGuiWindowFlags.NoCollapse |
            ImGuiWindowFlags.NoTitleBar | ImGuiWindowFlags.NoScrollbar | ImGuiWindowFlags.NoInputs |
            ImGuiWindowFlags.NoBackground);

        int validCount   = request.ValidOptions.Count;
        int invalidCount = request.InvalidOptions.Count;
        float rowH   = 32f;
        float pad    = 16f;
        float instrH = 48f;
        float backH  = request.AllowCancel ? rowH + pad : 0f; // extra height for Back button, if shown

        // Valid options may carry multi-line labels (OptionLabel override, e.g. model stats) — size each
        // row to its line count. Single-line labels keep the classic 32px row.
        float lineH = ImGui.GetTextLineHeight();
        string[] labels = new string[validCount];
        float[] rowHeights = new float[validCount];
        float validH = 0f;
        for (int i = 0; i < validCount; i++)
        {
            labels[i] = OptionLabel(request.ValidOptions[i]);
            int lineCount = 1 + labels[i].Count(c => c == '\n');
            rowHeights[i] = lineCount == 1 ? rowH : lineH * lineCount + 14f;
            validH += rowHeights[i];
        }

        float dw = MathF.Min(screenW * 0.45f, 560f);
        float dh = MathF.Min(instrH + pad + validH + invalidCount * rowH + backH + pad * 2, screenH * 0.80f);
        float dx = (screenW - dw) * 0.5f;
        float dy = (screenH - dh) * 0.5f;

        ImGui.SetCursorPos(new Vector2(dx, dy));
        ImGui.PushStyleColor(ImGuiCol.ChildBg, new Vector4(0.15f, 0.15f, 0.20f, 0.97f));
        ImGui.PushStyleVar(ImGuiStyleVar.ChildRounding, 6f);
        ImGui.BeginChild("##SelectionDialog", new Vector2(dw, dh), ImGuiChildFlags.Borders,
            ImGuiWindowFlags.NoScrollbar);
        ImGui.PopStyleColor();
        ImGui.PopStyleVar();

        // Instructions
        ImGui.SetCursorPos(new Vector2(pad, pad));
        ImGui.PushTextWrapPos(dw - pad);
        ImGui.TextUnformatted(request.Instructions);
        ImGui.PopTextWrapPos();

        // Valid options
        float listY = pad + instrH;
        float btnW  = dw - pad * 2;
        float y = listY;
        for (int i = 0; i < validCount; i++)
        {
            var opt = request.ValidOptions[i];
            ImGui.SetCursorPos(new Vector2(pad, y));
            if (ImGui.Button(labels[i] + $"##{i}", new Vector2(btnW, rowHeights[i] - 4f)))
                Complete(tcs, opt.Option);
            else if (ImGui.IsItemHovered())
                OnValidOptionHovered(opt);
            y += rowHeights[i];
        }

        // Invalid options (grayed out)
        if (invalidCount > 0)
        {
            ImGui.PushStyleColor(ImGuiCol.Text, new Vector4(0.5f, 0.5f, 0.5f, 1f));
            for (int i = 0; i < invalidCount; i++)
            {
                var opt = request.InvalidOptions[i];
                ImGui.SetCursorPos(new Vector2(pad, y));
                ImGui.BeginDisabled(true);
                ImGui.Button($"{opt.Name} ({opt.Reason})##{validCount + i}", new Vector2(btnW, rowH - 4f));
                ImGui.EndDisabled();
                y += rowH;
            }
            ImGui.PopStyleColor();
        }

        // Back button — only for cancellable selections. Mandatory choices (which unit to activate/deploy)
        // have no back-destination, and a null reply from Back crashes the networked reply path.
        if (request.AllowCancel)
        {
            ImGui.SetCursorPos(new Vector2(pad, y + pad));
            ImGui.PushStyleColor(ImGuiCol.Button, new Vector4(0.25f, 0.25f, 0.30f, 1f));
            if (ImGui.Button("Back##back", new Vector2(btnW, rowH - 4f)))
                Complete(tcs, null!);
            ImGui.PopStyleColor();
        }

        ImGui.EndChild();
        ImGui.End();
    }

    /// <summary>The text on a valid option's dialog button. Override to enrich (may be multi-line — rows
    /// auto-size). Game-facing: ASCII only (see CLAUDE.md).</summary>
    protected virtual string OptionLabel(SelectionRequest<T>.ValidOption opt) => opt.Name;

    /// <summary>Called while a valid option's dialog button is hovered — lets subclasses highlight the
    /// corresponding object on the table canvas.</summary>
    protected virtual void OnValidOptionHovered(SelectionRequest<T>.ValidOption opt) { }

    protected void Complete(TaskCompletionSource<DataBinding<T>> tcs, DataBinding<T> option)
    {
        lock (_lock) { _request = null; _tcs = null; }
        tcs.SetResult(option);
    }
}
