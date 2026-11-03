using System.Numerics;
using FDG.Data;
using FDG.StageResolution;
using FDG.StageResolution.Requests;
using ImGuiNET;

namespace FdgRaylib.Rendering.Resolvers;

public class GuiCancellableSelectionResolver<T>
    : IStageResolver<CancellableSelectionRequest<T>, CancellableResult<DataBinding<T>>>, IGuiResolver
{
    protected readonly object _lock = new();
    protected CancellableSelectionRequest<T>? _request;
    protected TaskCompletionSource<CancellableResult<DataBinding<T>>>? _tcs;

    public bool HasPendingRequest { get { lock (_lock) return _request != null; } }

    // #248: keyboard navigation — number keys instant-pick, arrows move this highlight, Enter commits.
    private readonly KeyboardListNav _nav = new();
    private static readonly uint KbHighlightCol = ImGui.ColorConvertFloat4ToU32(new Vector4(0.55f, 0.90f, 1.00f, 0.90f));

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

    public virtual void Draw(int screenW, int screenH)
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
        float pad    = 16f;
        // #298: rows are sized from the font instead of a hardcoded 32px, which the 4K font nearly filled.
        // The instruction block is measured for the same reason - at 48px a wrapped prompt ran under row 1.
        float btnH   = ResolverPanelLayout.OptionRowHeight();
        float rowH   = btnH + 4f;
        float instrH = ImGui.CalcTextSize(request.Instructions, false,
            ResolverPanelLayout.W - pad * 2f).Y + 12f;
        float dw = ResolverPanelLayout.W;   // dock into the right-column resolver panel
        float dh = ResolverPanelLayout.H;
        float dx = ResolverPanelLayout.X;
        float dy = ResolverPanelLayout.Y;

        ImGui.SetCursorPos(new Vector2(dx, dy));
        ImGui.PushStyleColor(ImGuiCol.ChildBg, new Vector4(0.15f, 0.15f, 0.20f, 0.97f));
        ImGui.PushStyleVar(ImGuiStyleVar.ChildRounding, 6f);
        ImGui.BeginChild("##CancellableSelectionDialog", new Vector2(dw, dh), ImGuiChildFlags.Borders,
            ImGuiWindowFlags.None);
        ImGui.PopStyleColor();
        ImGui.PopStyleVar();

        ImGui.SetCursorPos(new Vector2(pad, pad));
        ImGui.PushTextWrapPos(dw - pad);
        ImGui.TextUnformatted(request.Instructions);
        ImGui.PopTextWrapPos();

        // #248: keyboard picks — number key resolves immediately, arrows move a highlight Enter commits,
        // Esc = Back. Applied once after drawing so same-frame click + key can't double-resolve the TCS.
        int kbPick = _nav.Update(request, validCount, out bool kbMoved);
        DataBinding<T>? picked = kbPick >= 0 ? request.ValidOptions[kbPick].Option : null;
        bool cancelled = false;

        var dl = ImGui.GetWindowDrawList();
        float listY = pad + instrH;
        float btnW  = dw - pad * 2;
        for (int i = 0; i < validCount; i++)
        {
            var opt = request.ValidOptions[i];
            float rowY = listY + i * rowH;

            // Arrow navigation scrolls the highlighted row into view (fixed-height panel, long lists scroll).
            if (kbMoved && i == _nav.Index)
            {
                float viewH = ImGui.GetWindowHeight();
                if (rowY < ImGui.GetScrollY() + pad)
                    ImGui.SetScrollY(MathF.Max(0f, rowY - pad));
                else if (rowY + rowH > ImGui.GetScrollY() + viewH - pad)
                    ImGui.SetScrollY(rowY + rowH - viewH + pad);
            }

            ImGui.SetCursorPos(new Vector2(pad, rowY));
            Vector2 origin = ImGui.GetCursorScreenPos();
            if (ImGui.Button($"{ResolverHotkeys.NumberPrefix(i)}{opt.Name}##{i}", new Vector2(btnW, btnH)))
                picked ??= opt.Option;

            if (i == _nav.Index)
            {
                dl.AddRect(origin, origin + new Vector2(btnW, btnH), KbHighlightCol, 4f,
                    ImDrawFlags.None, 2f);
                OnValidOptionHighlighted(opt);
            }
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
                ImGui.Button($"{opt.Name} ({opt.Reason})##{validCount + i}", new Vector2(btnW, btnH));
                ImGui.EndDisabled();
            }
            ImGui.PopStyleColor();
        }

        // #248: Backspace backs out too (Esc is reserved for the in-game menu).
        float backY = listY + totalRows * rowH + pad;
        ImGui.SetCursorPos(new Vector2(pad, backY));
        ImGui.PushStyleColor(ImGuiCol.Button, new Vector4(0.25f, 0.25f, 0.30f, 1f));
        if (ImGui.Button("Back  (Backspace)##back", new Vector2(btnW, ResolverPanelLayout.ActionRowHeight())))
            cancelled = true;
        ImGui.PopStyleColor();

        if (ResolverHotkeys.IsBackPressed())
            cancelled = true;

        ImGui.EndChild();
        ImGui.End();

        if (picked != null) Complete(tcs, new Selected<DataBinding<T>>(picked));
        else if (cancelled) Complete(tcs, new Cancelled<DataBinding<T>>());
    }

    /// <summary>#248: called while a valid option carries the keyboard highlight (arrow navigation).
    /// Subclasses ring the object on the canvas like a hover; no tooltips (wrong position).</summary>
    protected virtual void OnValidOptionHighlighted(CancellableSelectionRequest<T>.ValidOption opt) { }

    protected void Complete(TaskCompletionSource<CancellableResult<DataBinding<T>>> tcs,
        CancellableResult<DataBinding<T>> result)
    {
        lock (_lock) { _request = null; _tcs = null; }
        tcs.SetResult(result);
    }
}
