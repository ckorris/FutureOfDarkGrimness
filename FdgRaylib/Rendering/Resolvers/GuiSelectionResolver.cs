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

    // #248: keyboard navigation — number keys instant-pick, arrows move this highlight, Enter commits it.
    private readonly KeyboardListNav _nav = new();
    private static readonly uint KbHighlightCol = ImGui.ColorConvertFloat4ToU32(new Vector4(0.55f, 0.90f, 1.00f, 0.90f));

    // #315: canvas-driven row emphasis (the Assign Wounds #286 treatment) — a tinted fill + bright border
    // painted on rows a subclass flags via IsRowEmphasized, e.g. a hovered transport's occupants.
    private static readonly uint RowEmphasisCol   = ImGui.ColorConvertFloat4ToU32(new Vector4(1f, 0.95f, 0.30f, 0.90f));
    private static readonly uint RowEmphasisBgCol = ImGui.ColorConvertFloat4ToU32(new Vector4(1f, 0.95f, 0.30f, 0.13f));

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
        // #298: the instruction block is measured rather than assumed 48px - at the 4K font a wrapped
        // prompt overran the first row.
        float instrH  = ImGui.CalcTextSize(request.Instructions, false,
            ResolverPanelLayout.W - pad * 2f).Y + 12f;

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
            // #248: valid options advertise their number key in the heading ("[1] Warriors").
            if (isValid) heading = ResolverHotkeys.NumberPrefix(i) + heading;
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

        // #248: keyboard picks — a number key resolves immediately; arrows move a highlight that Enter
        // commits. The pick is applied ONCE after the frame's drawing (picked/cancelled locals below), so
        // a click and a key landing on the same frame can't double-resolve the TaskCompletionSource.
        int kbPick = _nav.Update(request, validCount, out bool kbMoved);
        DataBinding<T>? picked = kbPick >= 0 ? request.ValidOptions[kbPick].Option : null;
        bool cancelled = false;

        float listY = pad + instrH;
        float y = listY;
        bool emphasisScrolled = false;   // #315: scroll at most one emphasised row into view per frame
        for (int i = 0; i < total; i++)
        {
            bool isValid = i < validCount;
            ImGui.SetCursorPos(new Vector2(pad, y));

            // Arrow navigation scrolls the highlighted row into view (the panel is fixed-height; long
            // activation lists scroll). Cursor Y is in content coordinates, matching GetScrollY's space.
            if (kbMoved && isValid && i == _nav.Index)
            {
                float viewH = ImGui.GetWindowHeight();
                if (y < ImGui.GetScrollY() + pad)
                    ImGui.SetScrollY(MathF.Max(0f, y - pad));
                else if (y + rowHeights[i] > ImGui.GetScrollY() + viewH - pad)
                    ImGui.SetScrollY(y + rowHeights[i] - viewH + pad);
                ImGui.SetCursorPos(new Vector2(pad, y));
            }

            Vector2 origin = ImGui.GetCursorScreenPos();

            if (!isValid) ImGui.BeginDisabled(true);
            bool clicked = ImGui.Button($"##opt{i}", new Vector2(btnW, rowHeights[i]));
            bool hovered = isValid && ImGui.IsItemHovered();
            if (!isValid) ImGui.EndDisabled();

            // #315: the table -> list direction (the Assign Wounds #286 treatment). The button's own hover
            // styling only fires for the mouse being over IT, so canvas-driven emphasis paints the row
            // here instead — fill + border under the text — and scrolls an off-view row back into view
            // (only when actually off-view, so it never fights a deliberate scroll).
            DataBinding<T> option = isValid ? request.ValidOptions[i].Option
                                            : request.InvalidOptions[i - validCount].Option;
            if (IsRowEmphasized(option))
            {
                if (!emphasisScrolled)
                {
                    ScrollRowIntoView(y, rowHeights[i]);
                    emphasisScrolled = true;
                }
                Vector2 rowMax = origin + new Vector2(btnW, rowHeights[i]);
                dl.AddRectFilled(origin, rowMax, RowEmphasisBgCol, 4f);
                dl.AddRect(origin, rowMax, RowEmphasisCol, 4f, ImDrawFlags.None, 2f);
            }

            float tx = origin.X + textPadX;
            float ty = origin.Y + btnPadY;
            dl.AddText(new Vector2(tx, ty), isValid ? headCol : headDimCol, headings[i]);
            float wy = ty + mainLineH;
            foreach (string line in detailWrap[i])
            {
                dl.AddText(font, smallSize, new Vector2(tx + 2f, wy), isValid ? DetailCol : detailDimCol, line);
                wy += smallLineH;
            }

            // #248: the keyboard highlight gets a bright border + the same canvas emphasis as a hover
            // (ring only — no tooltip, which would pop at the unrelated mouse position).
            if (isValid && i == _nav.Index)
            {
                dl.AddRect(origin, origin + new Vector2(btnW, rowHeights[i]), KbHighlightCol, 4f,
                    ImDrawFlags.None, 2f);
                OnValidOptionHighlighted(request.ValidOptions[i]);
            }

            if (isValid && hovered) OnValidOptionHovered(request.ValidOptions[i]);
            if (isValid && clicked) picked ??= request.ValidOptions[i].Option;

            y += rowHeights[i] + ImGui.GetStyle().ItemSpacing.Y;
        }

        // Exit button — only for cancellable selections. Mandatory choices (which unit to activate/deploy)
        // have no back-destination, and a null reply from Back crashes the networked reply path.
        // #248: Backspace backs out too (Esc is reserved for the in-game menu).
        //
        // #331: the label comes from the request. Usually that is "Back", but where cancelling is a real
        // choice rather than a rewind (deploy-time embark: cancel = deploy on the table) the stage names it,
        // and a player who never presses Back can still find the action. The key hint comes from
        // ResolverKeybinds so the button can't advertise a key that has moved (#295).
        if (request.AllowCancel)
        {
            ImGui.SetCursorPos(new Vector2(pad, y + pad));
            bool isPlainBack = request.CancelLabel == SelectionRequest<T>.DEFAULT_CANCEL_LABEL;
            string label = $"{request.CancelLabel}  {ResolverKeybinds.Back.Parenthetical}##back";
            var size = new Vector2(btnW, ResolverPanelLayout.ActionRowHeight());

            // A named choice is an action, not an escape hatch, so it gets the ordinary button treatment
            // instead of the dim back-out tint that tells the eye to skip it.
            if (isPlainBack) ImGui.PushStyleColor(ImGuiCol.Button, new Vector4(0.25f, 0.25f, 0.30f, 1f));
            if (ImGui.Button(label, size))
                cancelled = true;
            if (isPlainBack) ImGui.PopStyleColor();

            if (ResolverHotkeys.IsBackPressed())
                cancelled = true;
        }

        ImGui.EndChild();
        ImGui.End();

        if (picked != null) Complete(tcs, picked);
        else if (cancelled) Complete(tcs, null!);
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

    /// <summary>#248: called while a valid option carries the keyboard highlight (arrow navigation).
    /// Subclasses ring the object on the canvas like a hover — but must NOT raise hover tooltips,
    /// which would pop at the unrelated mouse position.</summary>
    protected virtual void OnValidOptionHighlighted(SelectionRequest<T>.ValidOption opt) { }

    /// <summary>#315: true to paint this option's row with the canvas-driven emphasis (tinted fill +
    /// bright border, scrolled into view if off-screen) — the table -> list half of the two-way hover
    /// binding. Asked for valid and invalid rows alike; default none.</summary>
    protected virtual bool IsRowEmphasized(DataBinding<T> option) => false;

    /// <summary>Scrolls the option list so a row at <paramref name="rowY"/> (content coordinates) is
    /// fully visible, and does nothing when it already is. Mirrors the Assign Wounds dialog (#286).</summary>
    private static void ScrollRowIntoView(float rowY, float rowHeight)
    {
        float scrollY = ImGui.GetScrollY();
        float viewH   = ImGui.GetWindowHeight();
        if (rowY < scrollY)
            ImGui.SetScrollY(MathF.Max(0f, rowY));
        else if (rowY + rowHeight > scrollY + viewH)
            ImGui.SetScrollY(rowY + rowHeight - viewH);
    }

    // Word-wrap a detail line to maxWidth, at the smaller detail font size (#298: the wrapping itself now
    // lives in ResolverText, shared with the string-selection panel).
    private static List<string> WrapDetail(string text, float smallScale, float maxWidth)
        => ResolverText.Wrap(text, maxWidth, smallScale);

    protected void Complete(TaskCompletionSource<DataBinding<T>> tcs, DataBinding<T> option)
    {
        lock (_lock) { _request = null; _tcs = null; }
        tcs.SetResult(option);
    }
}
