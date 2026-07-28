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
        const float descScale = 0.82f;   // descriptions render smaller than the option label
        const float descIndent = 10f;
        const float gapAfterBtn = 6f;
        const float gapAfterDesc = 8f;
        const float textPadX = 10f;      // inset of the hand-drawn label from the button's left edge
        const float btnPadY = 6f;

        // #298: rows are sized from the font, not from a hardcoded pixel count - see OptionRowHeight.
        float lineH = ImGui.GetTextLineHeight();
        float btnH = ResolverPanelLayout.OptionRowHeight();

        float dw = ResolverPanelLayout.W;   // dock into the right-column resolver panel
        float btnW = dw - pad * 2;
        float labelWrap = btnW - textPadX * 2f;

        // #248: letter hotkeys - pinned letters for the built-in actions, pool letters for the rest.
        // The label carries the letter so the shortcut is discoverable on the button itself. Assigned
        // before measuring because the letter is part of the text being wrapped.
        char?[] letters = ResolverHotkeys.AssignLetters(request.ValidOptions);

        // Measure heights up front so the dialog is sized to its content and a wrapped description can never
        // overflow into the next row. CalcTextSize ignores SetWindowFontScale, so measure the description at
        // the scaled-equivalent wrap width and scale the resulting height back down.
        float instrH = ImGui.CalcTextSize(request.Instructions, false, dw - pad * 2).Y + 8f;

        float descWrapMeasure = (btnW - descIndent) / descScale;
        float[] rowHeights = new float[validCount];
        float[] btnHeights = new float[validCount];
        List<string>[] labelLines = new List<string>[validCount];
        string?[] descs = new string?[validCount];
        for (int i = 0; i < validCount; i++)
        {
            string opt = request.ValidOptions[i];
            labelLines[i] = ResolverText.Wrap(LabelFor(opt, letters[i]), labelWrap);
            btnHeights[i] = MathF.Max(btnH, labelLines[i].Count * lineH + btnPadY * 2f);

            float h = btnHeights[i] + gapAfterBtn;
            if (hasDescriptions
                && request.OptionDescriptions!.TryGetValue(opt, out string? d)
                && !string.IsNullOrEmpty(d))
            {
                descs[i] = d;
                float descH = ImGui.CalcTextSize(d, false, descWrapMeasure).Y * descScale;
                h = btnHeights[i] + 2f + descH + gapAfterDesc;
            }
            rowHeights[i] = h;
        }

        float dh = ResolverPanelLayout.H;   // fill the panel; content taller than this scrolls
        float dx = ResolverPanelLayout.X;
        float dy = ResolverPanelLayout.Y;

        ImGui.SetCursorPos(new Vector2(dx, dy));
        ImGui.PushStyleColor(ImGuiCol.ChildBg, new Vector4(0.15f, 0.15f, 0.20f, 0.97f));
        ImGui.PushStyleVar(ImGuiStyleVar.ChildRounding, 6f);
        ImGui.BeginChild("##StrSelDialog", new Vector2(dw, dh), ImGuiChildFlags.Borders,
            ImGuiWindowFlags.None);
        ImGui.PopStyleColor();
        ImGui.PopStyleVar();

        ImGui.SetCursorPos(new Vector2(pad, pad));
        ImGui.PushTextWrapPos(dw - pad);
        ImGui.TextUnformatted(request.Instructions);
        ImGui.PopTextWrapPos();

        // The picked option is completed once after the loop so two same-frame presses can't double-resolve.
        // Labels are drawn by hand over an empty button: a melee weapon option ("2x Great Sword - A2, AP1,
        // Rending") is wider than the narrow panel, and ImGui.Button center-CLIPS its label, which silently
        // ate the stat tail - the special rules included (#298). Wrapped left-aligned text shows all of it.
        var dl = ImGui.GetWindowDrawList();
        uint labelCol = ImGui.GetColorU32(ImGuiCol.Text);
        string? picked = null;

        float y = pad + instrH + 4f;
        for (int i = 0; i < validCount; i++)
        {
            string opt = request.ValidOptions[i];
            ImGui.SetCursorPos(new Vector2(pad, y));
            Vector2 origin = ImGui.GetCursorScreenPos();
            if (ImGui.Button($"##opt{i}", new Vector2(btnW, btnHeights[i])))
                picked ??= opt;
            if (letters[i] is char pressedKey && ResolverHotkeys.IsLetterPressed(pressedKey))
                picked ??= opt;

            float ty = origin.Y + (btnHeights[i] - labelLines[i].Count * lineH) * 0.5f;
            foreach (string line in labelLines[i])
            {
                dl.AddText(new Vector2(origin.X + textPadX, ty), labelCol, line);
                ty += lineH;
            }

            // Optional subtext under the option (a weapon rule's text, a spell's effect summary):
            // smaller and dimmed.
            if (descs[i] != null)
            {
                ImGui.SetCursorPos(new Vector2(pad + descIndent, y + btnHeights[i] + 2f));
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
            uint dimCol = ImGui.ColorConvertFloat4ToU32(new Vector4(0.55f, 0.55f, 0.58f, 1f));
            for (int i = 0; i < invalidCount; i++)
            {
                var opt = request.InvalidOptions[i];
                // Same hand-drawn treatment as a valid row, so a grayed-out option reads the same and its
                // reason ("Must attack with Deadly weapons first.") is never clipped away either.
                List<string> lines = ResolverText.Wrap($"{opt.Option} ({opt.Reason})", labelWrap);
                float rowH = MathF.Max(btnH, lines.Count * lineH + btnPadY * 2f);

                ImGui.SetCursorPos(new Vector2(pad, y));
                Vector2 origin = ImGui.GetCursorScreenPos();
                ImGui.BeginDisabled(true);
                ImGui.Button($"##invalid{validCount + i}", new Vector2(btnW, rowH));
                ImGui.EndDisabled();

                float ty = origin.Y + (rowH - lines.Count * lineH) * 0.5f;
                foreach (string line in lines)
                {
                    dl.AddText(new Vector2(origin.X + textPadX, ty), dimCol, line);
                    ty += lineH;
                }
                y += rowH + gapAfterBtn;
            }
        }

        // #248: cancellable string menus (a pristine activation's action menu) get a Back button +
        // Backspace, replying null — the SelectionRequest cancel sentinel. Esc is reserved for the
        // in-game menu.
        bool cancelled = false;
        if (request.AllowCancel)
        {
            ImGui.SetCursorPos(new Vector2(pad, y + 8f));
            ImGui.PushStyleColor(ImGuiCol.Button, new Vector4(0.25f, 0.25f, 0.30f, 1f));
            if (ImGui.Button("Back  (Backspace)##back", new Vector2(btnW, ResolverPanelLayout.ActionRowHeight())))
                cancelled = true;
            ImGui.PopStyleColor();

            if (ResolverHotkeys.IsBackPressed())
                cancelled = true;
        }

        ImGui.EndChild();
        ImGui.End();

        if (picked != null)
            Complete(tcs, picked);
        else if (cancelled)
            Complete(tcs, null!);
    }

    /// <summary>The text drawn on an option's button: the option, prefixed with its letter hotkey when
    /// one was assigned ("[A] Advance"). Game-facing: ASCII only (CLAUDE.md).</summary>
    private static string LabelFor(string option, char? letter) =>
        letter is char key ? $"[{key}] {option}" : option;

    private void Complete(TaskCompletionSource<string> tcs, string choice)
    {
        lock (_lock) { _request = null; _tcs = null; }
        tcs.SetResult(choice);
    }
}
