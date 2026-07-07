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

    // Detail text (stat/weapon lines under a heading) is lighter + smaller than the heading, mirroring the
    // wound-assignment dialog so every selector reads the same.
    private static readonly uint DetailCol = ImGui.ColorConvertFloat4ToU32(new Vector4(0.72f, 0.78f, 0.85f, 1f));

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

        float pad     = 16f;
        float instrH  = 48f;
        float rowH    = 32f;   // single-line rows (Back button)

        // Text metrics — a bright heading in the main font size, detail lines smaller + dimmer, matching the
        // wounds dialog. Detail lines wrap so long weapon lists stack instead of clipping off the edge.
        float mainSize   = ImGui.GetFontSize();
        float smallSize  = MathF.Round(mainSize * 0.72f);
        float smallScale = smallSize / mainSize;
        float mainLineH  = ImGui.GetTextLineHeight();
        float smallLineH = smallSize + 2f;
        const float btnPadY  = 6f;
        const float textPadX = 10f;

        float dw = ResolverPanelLayout.W;   // dock into the right-column resolver panel (#GreenUIPolish)
        float btnW = dw - pad * 2f;
        float wrapW = btnW - textPadX * 2f;

        // Build heading + wrapped detail lines for EVERY option (valid first, then invalid), sizing each row
        // to its content. Invalid options keep their full stats and just append the reason to the heading,
        // so a grayed-out unit reads the same as a pickable one -- only its heading and disabled state differ.
        int total = validCount + invalidCount;
        var headings   = new string[total];
        var detailWrap = new List<string>[total];
        var rowHeights = new float[total];
        for (int i = 0; i < total; i++)
        {
            bool isValid = i < validCount;
            DataBinding<T> option = isValid ? request.ValidOptions[i].Option
                                            : request.InvalidOptions[i - validCount].Option;
            string name = isValid ? request.ValidOptions[i].Name
                                  : request.InvalidOptions[i - validCount].Name;
            var (heading, details) = OptionContent(option, name);
            headings[i]   = isValid ? heading : $"{heading}  ({request.InvalidOptions[i - validCount].Reason})";
            detailWrap[i] = new List<string>();
            foreach (string d in details)
                detailWrap[i].AddRange(WrapDetail(d, smallScale, wrapW));
            rowHeights[i] = btnPadY * 2f + mainLineH + detailWrap[i].Count * smallLineH;
        }

        float dh = ResolverPanelLayout.H;   // fill the panel; content taller than this scrolls
        float dx = ResolverPanelLayout.X;
        float dy = ResolverPanelLayout.Y;

        ImGui.SetCursorPos(new Vector2(dx, dy));
        ImGui.PushStyleColor(ImGuiCol.ChildBg, new Vector4(0.15f, 0.15f, 0.20f, 0.97f));
        ImGui.PushStyleVar(ImGuiStyleVar.ChildRounding, 6f);
        ImGui.BeginChild("##SelectionDialog", new Vector2(dw, dh), ImGuiChildFlags.Borders,
            ImGuiWindowFlags.None);
        ImGui.PopStyleColor();
        ImGui.PopStyleVar();

        // Instructions
        ImGui.SetCursorPos(new Vector2(pad, pad));
        ImGui.PushTextWrapPos(dw - pad);
        ImGui.TextUnformatted(request.Instructions);
        ImGui.PopTextWrapPos();

        // Each option is a real button (for click + hover feedback on valid ones) with a bright heading and
        // smaller, dimmer wrapped detail lines drawn over it (the wounds-dialog treatment). Invalid options
        // render the same way but disabled and dimmed, with the reason baked into their heading above.
        var dl   = ImGui.GetWindowDrawList();
        var font = ImGui.GetFont();
        uint headCol     = ImGui.GetColorU32(ImGuiCol.Text);
        uint headDimCol  = ImGui.ColorConvertFloat4ToU32(new Vector4(0.55f, 0.55f, 0.58f, 1f));
        uint detailDimCol = ImGui.ColorConvertFloat4ToU32(new Vector4(0.48f, 0.50f, 0.55f, 1f));
        float listY = pad + instrH;
        float y = listY;
        for (int i = 0; i < total; i++)
        {
            bool isValid = i < validCount;
            ImGui.SetCursorPos(new Vector2(pad, y));
            Vector2 origin = ImGui.GetCursorScreenPos();

            if (!isValid) ImGui.BeginDisabled(true);
            bool clicked = ImGui.Button($"##opt{i}", new Vector2(btnW, rowHeights[i]));
            bool hovered = isValid && ImGui.IsItemHovered();
            if (!isValid) ImGui.EndDisabled();

            float tx = origin.X + textPadX;
            float ty = origin.Y + btnPadY;
            dl.AddText(new Vector2(tx, ty), isValid ? headCol : headDimCol, headings[i]);
            float wy = ty + mainLineH;
            foreach (string line in detailWrap[i])
            {
                dl.AddText(font, smallSize, new Vector2(tx + 2f, wy), isValid ? DetailCol : detailDimCol, line);
                wy += smallLineH;
            }

            if (isValid && hovered) OnValidOptionHovered(request.ValidOptions[i]);
            if (isValid && clicked) Complete(tcs, request.ValidOptions[i].Option);

            y += rowHeights[i] + ImGui.GetStyle().ItemSpacing.Y;
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

    /// <summary>An option's dialog content: a bright heading plus zero or more smaller, dimmer detail lines
    /// (stats, weapons) that wrap. Default is just the option name. Override to enrich (e.g. unit/model stats).
    /// Called for both valid and invalid options (invalid ones append their reason to the heading but keep the
    /// same stats), so it takes the data + name rather than a ValidOption. Game-facing: ASCII only (CLAUDE.md).</summary>
    protected virtual (string Heading, IReadOnlyList<string> Details) OptionContent(DataBinding<T> option, string name)
        => (name, System.Array.Empty<string>());

    /// <summary>Called while a valid option's dialog button is hovered — lets subclasses highlight the
    /// corresponding object on the table canvas.</summary>
    protected virtual void OnValidOptionHovered(SelectionRequest<T>.ValidOption opt) { }

    // Word-wrap a detail line to maxWidth. Detail text renders at the smaller size, so widths measured at the
    // main font size are scaled by smallScale (text width scales ~linearly with font size) — avoids a
    // dependency on the per-size CalcTextSizeA overload.
    private static List<string> WrapDetail(string text, float smallScale, float maxWidth)
    {
        var lines = new List<string>();
        if (string.IsNullOrEmpty(text)) { lines.Add(""); return lines; }

        string cur = "";
        foreach (string word in text.Split(' '))
        {
            string test = cur.Length == 0 ? word : cur + " " + word;
            if (ImGui.CalcTextSize(test).X * smallScale > maxWidth && cur.Length > 0)
            {
                lines.Add(cur);
                cur = word;
            }
            else cur = test;
        }
        lines.Add(cur);
        return lines;
    }

    protected void Complete(TaskCompletionSource<DataBinding<T>> tcs, DataBinding<T> option)
    {
        lock (_lock) { _request = null; _tcs = null; }
        tcs.SetResult(option);
    }
}
